// DorkNet.DebugMod — the opt-in debugging companion to DorkNet.ClientMod.
//
// The shipping ClientMod carries ONLY the patches needed to connect and
// survive anti-cheat. All of the verbose diagnostic/tracing code lives
// here: HTTP header/request tracing, join/quit/studio/room-load traces,
// the process-level exception sink, auth-token tracing, the registration
// dialog trace, and the in-game DebugConsole enable/toggle path. Drop
// DorkNet.DebugMod.dll into the Mods folder next to DorkNet.ClientMod.dll
// to turn all of it on; remove it to ship a quiet, minimal client.
//
// It is a fully self-contained MelonMod (its own Harmony instance + its
// own copy of the reflection/patch helpers) so it neither needs nor
// couples to ClientMod at load time. Debug knobs are read from the same
// <MelonLoader>\UserData\dorknet-clientmod.json the ClientMod uses.

using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

[assembly: MelonInfo(typeof(DorkNet.DebugMod.DebugMod), "DorkNet DebugMod", "1.0.0", "Dork Squad")]
[assembly: MelonGame(null, null)]

namespace DorkNet.DebugMod;

public class DebugMod : MelonMod
{
    internal static MelonLogger.Instance Log => Instance!.LoggerInstance;
    internal static DebugMod? Instance;
    internal static string? DiagnosticsLogPath;
    private static readonly Dictionary<string, Type> ResolvedTypeCache = new();
    private readonly HashSet<string> _diagnosticPatchLabels = new();
    private bool _httpDiagnosticsRegistered;
    private bool _joinTracePatchesRegistered;
    private bool _quitTraceCoreRegistered;
    private bool _quitTraceGameComplete;
    private int _quitTraceRetryFrame;
    private bool _debugConsolePatchesComplete;
    private int _debugConsoleRetryFrame;
    private bool _diagnosticCoreRegistered;
    private bool _diagnosticGameComplete;
    private bool _processExceptionHandlersRegistered;
    private bool _firstUpdateLogged;
    private bool _devCommandsDumped;
    private int  _devCommandsDumpFrame;
    private int  _devCommandsDumpTries;

    public static class Cfg
    {
        // Log-only trace for the stuck first-dorm "Change Username?" dialog.
        public static bool   TraceRegistrationDialog = true;
        // Force-enable the client's built-in RecRoom.Debugging.DebugConsole
        // (normally dev-gated) and silence CheatManager's tamper detectors so
        // movement/time commands don't drop you to the dorm. Default OFF. The
        // toggle key is a UnityEngine.KeyCode name; BackQuote is the `~` key.
        public static bool   EnableDebugConsole = false;
        public static string DebugConsoleToggleKey = "BackQuote";
        public static string[] DevCommands = Array.Empty<string>();
        // Read-only one-shot: dump the full runtime command table to
        // UserData/dorknet-devcommands.txt. Default OFF.
        public static bool   DumpDevCommands = false;
    }

    public override void OnInitializeMelon()
    {
        Instance = this;
        Log.Msg("=== DorkNet DebugMod loading ===");
        LoadConfig();
    }

    public override void OnLateInitializeMelon()
    {
        Log.Msg("=== Registering diagnostic patches ===");
        RegisterProcessExceptionHandlers();
        RegisterHttpDiagnostics();
        RegisterQuitTracePatches(logMisses: true);
        RegisterStudioTracePatches();
        RegisterJoinTracePatches();
        // Auth-token setter trace (LBNJFPOLCDL.JHLKGBGJKKK(access, refresh, key)).
        // The shipping ClientMod used to register this unconditionally; it now
        // lives here with the rest of the diagnostics.
        TryPatchByName("LBNJFPOLCDL", "JHLKGBGJKKK",
                       args: new[] { typeof(string), typeof(string), typeof(string) },
                       prefix: nameof(AuthPatches.CaptureTokenArgs_Prefix));
        // Deep game-side diagnostics: CheatManager tamper-detection logging,
        // the Unity Debug.LogError/LogException sink, Photon connect/join
        // tracing, and the room-load MoveNext state dumps. Unwired in the
        // shipping client (kept out of the minimal footprint); this is their
        // home now that diagnostics are opt-in.
        RegisterDiagnostics();
        if (Cfg.EnableDebugConsole)
            RegisterDebugConsolePatches(logMisses: true);
        Log.Msg("=== Diagnostic patches registered ===");
    }

    public override void OnUpdate()
    {
        if (!_firstUpdateLogged)
        {
            _firstUpdateLogged = true;
            Log.Msg("[lifecycle] first OnUpdate tick (Unity frame loop is live)");
        }

        if (!_quitTraceGameComplete && ++_quitTraceRetryFrame >= 300)
        {
            _quitTraceRetryFrame = 0;
            RegisterQuitTracePatches(logMisses: false);
        }

        if (Cfg.EnableDebugConsole && !_debugConsolePatchesComplete && ++_debugConsoleRetryFrame >= 300)
        {
            _debugConsoleRetryFrame = 0;
            RegisterDebugConsolePatches(logMisses: false);
        }

        if (Cfg.EnableDebugConsole)
            DebugConsolePatches.PollToggleKey();

        // Retry the read-only command dump every ~5s (the config singleton only
        // exists after the ~26s load + dorm entry). Stops as soon as it finds a
        // populated runtime table, or after ~2.5 min of attempts.
        if (Cfg.DumpDevCommands && !_devCommandsDumped && ++_devCommandsDumpFrame >= 300)
        {
            _devCommandsDumpFrame = 0;
            if (DebugConsolePatches.DumpCommandDiagnostics() || ++_devCommandsDumpTries >= 30)
                _devCommandsDumped = true;
        }
    }

    // ── Config ────────────────────────────────────────────────────────
    private static void LoadConfig()
    {
        try
        {
            var userData = ResolveUserDataDirectory();
            Directory.CreateDirectory(userData);
            DiagnosticsLogPath = Path.Combine(userData, "dorknet-diagnostics.log");

            var path = Path.Combine(userData, "dorknet-clientmod.json");
            if (!File.Exists(path))
            {
                Log.Msg($"[config] no file at {path}; using debug defaults");
                return;
            }
            var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            if (TryGetConfigValue(r, "TraceRegistrationDialog", out var v))  Cfg.TraceRegistrationDialog = v.GetBoolean();
            if (TryGetConfigValue(r, "EnableDebugConsole", out v))            Cfg.EnableDebugConsole = v.GetBoolean();
            if (TryGetConfigValue(r, "DebugConsoleToggleKey", out v))         Cfg.DebugConsoleToggleKey = v.GetString() ?? Cfg.DebugConsoleToggleKey;
            if (TryGetConfigValue(r, "DumpDevCommands", out v))               Cfg.DumpDevCommands = v.GetBoolean();
            if (TryGetConfigValue(r, "DevCommands", out v) && v.ValueKind == JsonValueKind.Array)
            {
                var commands = new List<string>();
                foreach (var e in v.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                        commands.Add(e.GetString()!);
                Cfg.DevCommands = commands.ToArray();
            }
            Log.Msg($"[config] debug: debugConsole={Cfg.EnableDebugConsole}, " +
                    $"dumpDevCommands={Cfg.DumpDevCommands}, devCommands={Cfg.DevCommands.Length}");
        }
        catch (Exception ex)
        {
            Log.Warning($"[config] load failed, using defaults: {ex.Message}");
        }
    }

    private MethodInfo GetPatchMethod(string name)
    {
        foreach (var holder in new[] { typeof(HttpTracePatches), typeof(AuthPatches), typeof(QuitTracePatches), typeof(DiagnosticPatches), typeof(RoomLoadDiagnostics), typeof(SavePatches), typeof(ChatPatches), typeof(JoinPatches), typeof(RegistrationPatches), typeof(DebugConsolePatches), typeof(MakerPenGiftPreviewPatches) })
        {
            var m = holder.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            if (m is not null) return m;
        }
        throw new MissingMethodException($"patch method '{name}' not found on any holder");
    }
    private bool TryPatch(string label, Type target, string methodName,
                          Type[]? args, string? prefix = null, string? postfix = null,
                          bool logMiss = true)
    {
        try
        {
            var method = methodName == "ctor"
                ? (MethodBase)target.GetConstructor(args ?? Type.EmptyTypes)!
                : (args is null ? AccessTools.Method(target, methodName)
                                : AccessTools.Method(target, methodName, args));
            if (method is null)
            {
                // `logMiss` is throttled by the retry-loop caller — once per
                // 5 seconds in OnUpdate, every time in the one-shot initial
                // RegisterDiagnostics. Without the gate the per-second retry
                // loop would spam `[patch-miss]` lines forever for any method
                // whose name drifted in a newer game build.
                if (logMiss) Log.Warning($"[patch-miss] {label}");
                return false;
            }
            HarmonyInstance.Patch(method,
                prefix:  prefix  is null ? null : new HarmonyMethod(GetPatchMethod(prefix)),
                postfix: postfix is null ? null : new HarmonyMethod(GetPatchMethod(postfix)));
            Log.Msg($"[patch-ok] {label}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[patch-fail] {label}: {ex.Message}");
            return false;
        }
    }
    private bool TryPatchByName(string typeName, string methodName,
                                Type[]? args = null, string? prefix = null, string? postfix = null,
                                bool logMiss = true)
    {
        var label = FormatPatchLabel(typeName, methodName, args);
        var type = ResolveType(typeName);
        if (type is null)
        {
            if (logMiss) Log.Warning($"[patch-miss] {label}: type not found");
            return false;
        }
        return TryPatch(label, type, methodName, args, prefix, postfix, logMiss);
    }
    private bool TryPatchDiagnosticByName(string typeName, string methodName,
                                          Type[]? args = null, string? prefix = null, string? postfix = null,
                                          bool logMiss = true)
    {
        var label = FormatPatchLabel(typeName, methodName, args);
        if (_diagnosticPatchLabels.Contains(label))
            return true;

        if (!TryPatchByName(typeName, methodName, args, prefix, postfix, logMiss))
            return false;

        _diagnosticPatchLabels.Add(label);
        return true;
    }
    internal static Type? ResolveType(string name)
    {
        if (ResolvedTypeCache.TryGetValue(name, out var cached))
            return cached;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(name, throwOnError: false);
                if (t is not null) return CacheResolvedType(name, t);
                var dottedPrefixed = asm.GetType("Il2Cpp." + name, throwOnError: false);
                if (dottedPrefixed is not null) return CacheResolvedType(name, dottedPrefixed);
                var prefixed = asm.GetType("Il2Cpp" + name, throwOnError: false);
                if (prefixed is not null) return CacheResolvedType(name, prefixed);
                // For namespaced types like "RecNet.Core", also try with
                // the Il2Cpp prefix on the namespace (i.e. "Il2CppRecNet.Core").
                var dot = name.IndexOf('.');
                if (dot > 0)
                {
                    var nsPrefixed = asm.GetType("Il2Cpp" + name.Substring(0, dot) + name.Substring(dot), throwOnError: false);
                    if (nsPrefixed is not null) return CacheResolvedType(name, nsPrefixed);
                }
            }
            catch { /* unloaded / dynamic asm — skip */ }
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var t in GetLoadableTypes(asm))
            {
                if (MatchesRequestedType(t, name))
                    return CacheResolvedType(name, t);
            }
        }
        return null;
    }
    private static Type CacheResolvedType(string name, Type type)
    {
        ResolvedTypeCache[name] = type;
        return type;
    }
    private static IEnumerable<Type> GetLoadableTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var types = new List<Type>();
            foreach (var t in ex.Types)
            {
                if (t is not null) types.Add(t);
            }
            return types;
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }
    private static bool MatchesRequestedType(Type type, string requested)
    {
        var prefixed = "Il2Cpp" + requested;
        var dottedPrefixed = "Il2Cpp." + requested;
        if (type.FullName == requested || type.FullName == prefixed || type.FullName == dottedPrefixed) return true;
        if (type.Name == requested || type.Name == prefixed) return true;
        return type.FullName?.EndsWith("." + requested, StringComparison.Ordinal) == true ||
               type.FullName?.EndsWith(".Il2Cpp." + requested, StringComparison.Ordinal) == true ||
               type.FullName?.EndsWith(".Il2Cpp" + requested, StringComparison.Ordinal) == true;
    }
    private static string FormatPatchLabel(string typeName, string methodName, Type[]? args)
    {
        if (args is null) return $"{typeName}.{methodName}";
        var names = new List<string>();
        foreach (var arg in args)
            names.Add(arg.Name);
        return $"{typeName}.{methodName}({string.Join(",", names)})";
    }
    private static string FormatParameterTypes(ParameterInfo[] parameters)
    {
        var names = new List<string>();
        foreach (var parameter in parameters)
            names.Add(parameter.ParameterType.Name);
        return string.Join(",", names);
    }
    private static bool TypeNameContains(Type type, string value)
    {
        var fullName = type.FullName ?? type.Name;
        return fullName.Contains(value, StringComparison.Ordinal);
    }
    private static bool MethodNameContains(MethodInfo method, string value)
        => method.Name.Contains(value, StringComparison.Ordinal);
    private static string ResolveUserDataDirectory()
    {
        foreach (var typeName in new[]
        {
            "MelonLoader.MelonEnvironment, MelonLoader",
            "MelonLoader.MelonUtils, MelonLoader",
        })
        {
            var type = Type.GetType(typeName);
            var prop = type?.GetProperty("UserDataDirectory", BindingFlags.Public | BindingFlags.Static);
            if (prop?.GetValue(null) is string value && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        var modDll = Assembly.GetExecutingAssembly().Location;
        var modsDir = Path.GetDirectoryName(modDll) ?? string.Empty;
        var gameDir = Path.GetDirectoryName(modsDir) ?? AppContext.BaseDirectory;
        return Path.Combine(gameDir, "MelonLoader", "UserData");
    }
    private static bool TryGetConfigValue(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
    private void RegisterJoinTracePatches()
    {
        if (_joinTracePatchesRegistered) return;
        Log.Msg("=== Registering room-join trace ===");

        // Watch "Dorm Room" button. Void, no params, unique name —
        // HomeScreenFlow.txt:4791, calls RunJoinRoom("DormRoom",…) at :4848.
        TryPatchByName("AGUI.StackedUI.HomeScreenFlow", "Button_DormRoom",
                       args: Type.EmptyTypes,
                       prefix: nameof(JoinPatches.ButtonDormRoom_Prefix));

        // Every SessionManager.RunJoinRoom overload (string-name entry +
        // the RoomInstance inner legs). Logs the method + its arguments.
        PatchAllOverloads("SessionManager", "RunJoinRoom",
                          nameof(JoinPatches.RunJoinRoom_Prefix));

        // The /goto/room/{name|id} promise builders — both the string
        // (room-name) and Int64 (room-id) overloads of NHBPIIGDAJP.
        PatchAllOverloads("OJMCBOKJFOF", "NHBPIIGDAJP",
                          nameof(JoinPatches.Goto_Prefix));

        // The RunJoinRoom promise-chain callbacks compiled onto the
        // SessionManager <>c display class — most importantly the reject
        // handler b__165_1(string error) that raises the happyfox toast.
        // Match by the (de-mangled) substring "RunJoinRoom" + the lambda
        // marker "b__" so we hit the callbacks but not the RunJoinRoom
        // state-machine MoveNext.
        PatchNestedLambdas("SessionManager", "RunJoinRoom",
                           nameof(JoinPatches.JoinCallback_Prefix));

        // Additionally capture the REJECT REASON string on b__165_1 (the
        // happyfox-toast handler). Matches only that one lambda; it ends
        // up with two prefixes (flow + error string), which is harmless.
        PatchNestedLambdas("SessionManager", "RunJoinRoom_b__165_1",
                           nameof(JoinPatches.JoinError_Prefix));

        _joinTracePatchesRegistered = true;
        Log.Msg("=== Room-join trace registered ===");
    }
    private bool PatchAllOverloads(string typeName, string methodName, string prefix, bool logMiss = true)
    {
        var type = ResolveType(typeName);
        if (type is null)
        {
            if (logMiss) Log.Warning($"[patch-miss] {typeName}.{methodName}(*): type not found");
            return false;
        }
        var prefixMethod = GetPatchMethod(prefix);
        var patched = 0;
        foreach (var method in AccessTools.GetDeclaredMethods(type))
        {
            if (method.Name != methodName) continue;
            if (method.IsAbstract || method.ContainsGenericParameters) continue;
            try
            {
                HarmonyInstance.Patch(method, prefix: new HarmonyMethod(prefixMethod));
                patched++;
            }
            catch (Exception ex)
            {
                Log.Warning($"[patch-fail] {typeName}.{methodName} overload: {ex.Message}");
            }
        }
        if (patched == 0)
        {
            if (logMiss) Log.Warning($"[patch-miss] {typeName}.{methodName}(*): no overloads matched");
            return false;
        }
        Log.Msg($"[patch-ok] {typeName}.{methodName}(*) x{patched}");
        return true;
    }
    private bool PatchNestedLambdas(string typeName, string nameContains, string prefix, bool logMiss = true)
    {
        var type = ResolveType(typeName);
        if (type is null)
        {
            if (logMiss) Log.Warning($"[patch-miss] {typeName}+<lambdas>({nameContains}): type not found");
            return false;
        }
        var prefixMethod = GetPatchMethod(prefix);
        var patched = 0;
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var method in AccessTools.GetDeclaredMethods(nested))
            {
                if (method.IsAbstract || method.ContainsGenericParameters) continue;
                if (method.Name.IndexOf("b__", StringComparison.Ordinal) < 0) continue;
                if (method.Name.IndexOf(nameContains, StringComparison.Ordinal) < 0) continue;
                try
                {
                    HarmonyInstance.Patch(method, prefix: new HarmonyMethod(prefixMethod));
                    patched++;
                    Log.Msg($"[patch-ok] {typeName}+{nested.Name}.{method.Name}");
                }
                catch (Exception ex)
                {
                    Log.Warning($"[patch-fail] {typeName}+{nested.Name}.{method.Name}: {ex.Message}");
                }
            }
        }
        if (patched == 0 && logMiss)
            Log.Warning($"[patch-miss] {typeName}+<lambdas>({nameContains}): none matched");
        return patched > 0;
    }
    private void RegisterHttpDiagnostics()
    {
        if (_httpDiagnosticsRegistered) return;

        var complete = true;
        complete &= TryPatchBndRequestBuilderDiagnostics();
        complete &= TryPatchDiagnosticAllOverloads("BestHTTP.HTTPRequest", "CallCallback",
            postfix: nameof(HttpTracePatches.CallCallback_Postfix),
            finalizer: nameof(HttpTracePatches.CallCallback_Finalizer));
        complete &= TryPatchByName("BestHTTP.HTTPRequest", "AddHeader",
            args: new[] { typeof(string), typeof(string) },
            prefix: nameof(HttpTracePatches.HeaderSet_Prefix));
        complete &= TryPatchByName("BestHTTP.HTTPRequest", "SetHeader",
            args: new[] { typeof(string), typeof(string) },
            prefix: nameof(HttpTracePatches.HeaderSet_Prefix));

        _httpDiagnosticsRegistered = complete;
    }
    private bool TryPatchBndRequestBuilderDiagnostics()
    {
        const string label = "BNDIAONDFFF.request-builder";
        if (_diagnosticPatchLabels.Contains(label))
            return true;

        var type = ResolveType("BNDIAONDFFF");
        if (type is null)
        {
            Log.Warning("[patch-miss] BNDIAONDFFF request-builder: type not found");
            return false;
        }

        var prefix = new HarmonyMethod(GetPatchMethod(nameof(HttpTracePatches.RecNetRequestBuilderCtor_Prefix)));
        var postfix = new HarmonyMethod(GetPatchMethod(nameof(HttpTracePatches.RecNetRequestBuilderCtor_Postfix)));
        var patched = 0;

        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            var parameters = ctor.GetParameters();
            if (parameters.Length != 3 || parameters[2].ParameterType != typeof(string)) continue;

            try
            {
                HarmonyInstance.Patch(ctor, prefix: prefix, postfix: postfix);
                patched++;
            }
            catch (Exception ex)
            {
                Log.Warning($"[patch-fail] BNDIAONDFFF..ctor({FormatParameterTypes(parameters)}): {ex.Message}");
            }
        }

        foreach (var method in AccessTools.GetDeclaredMethods(type))
        {
            if (method.Name != "FILJELOLBKK") continue;
            var parameters = method.GetParameters();
            if (parameters.Length != 3 || parameters[2].ParameterType != typeof(string)) continue;

            try
            {
                HarmonyInstance.Patch(method, prefix: prefix, postfix: postfix);
                patched++;
            }
            catch (Exception ex)
            {
                Log.Warning($"[patch-fail] BNDIAONDFFF.FILJELOLBKK({FormatParameterTypes(parameters)}): {ex.Message}");
            }
        }

        if (patched == 0)
        {
            Log.Warning("[patch-miss] BNDIAONDFFF request-builder overloads");
            return false;
        }

        _diagnosticPatchLabels.Add(label);
        Log.Msg($"[patch-ok] BNDIAONDFFF request-builder x{patched}");
        return true;
    }
    private void RegisterQuitTracePatches(bool logMisses)
    {
        if (!_quitTraceCoreRegistered)
        {
            TryPatchByName("UnityEngine.Application", "Quit", Type.EmptyTypes,
                           prefix: nameof(QuitTracePatches.ApplicationQuit_Prefix),
                           logMiss: logMisses);
            TryPatchByName("UnityEngine.Application", "Quit", new[] { typeof(int) },
                           prefix: nameof(QuitTracePatches.ApplicationQuitInt_Prefix),
                           logMiss: logMisses);
            _quitTraceCoreRegistered = true;
        }

        var complete = true;
        complete &= TryPatchDiagnosticByName("OPEOLPDLEBO", "GDNAOLMIDLM",
                                             prefix: nameof(QuitTracePatches.ApplicationWantsToQuit_Prefix),
                                             logMiss: logMisses);
        complete &= TryPatchDiagnosticByName("OPEOLPDLEBO", "DGHJGMBNHCL",
                                             prefix: nameof(QuitTracePatches.CreateExitTask_Prefix),
                                             logMiss: logMisses);

        _quitTraceGameComplete = complete;
    }
    private void RegisterDiagnostics()
    {
        Log.Msg("=== Registering diagnostics ===");
        if (!_diagnosticCoreRegistered)
        {
            TryPatchByName("UnityEngine.Application", "Quit", Type.EmptyTypes,
                           prefix: nameof(DiagnosticPatches.ApplicationQuit_Prefix));
            TryPatchByName("UnityEngine.Application", "Quit", new[] { typeof(int) },
                           prefix: nameof(DiagnosticPatches.ApplicationQuitInt_Prefix));
            _diagnosticCoreRegistered = true;
        }

        _diagnosticGameComplete = RegisterGameDiagnostics(logMisses: true);
        Log.Msg("=== Diagnostics registered ===");
    }
    private bool RegisterGameDiagnostics(bool logMisses)
    {
        // CheatManager fires these when its anti-cheat heuristics trip —
        // logging them surfaces "client thinks it's been tampered with",
        // a common cause of silent dorm-drops / kicks on a modded client.
        // Names verified in CheatManager.txt for 2020.12.18; the older
        // build's "AnalyticsHelper.<X>Cheat" names don't exist here, and
        // there's no DeveloperFlag/StreamingAsset callback nor a
        // BootSequence.RegisterError sink in this build — those legacy
        // hooks were removed (they only ever logged [patch-miss] forever).
        foreach (var method in new[]
        {
            "OnTimeCheatDetected",
            "OnObscuredTypeCheatDetected",
            "OnHeightCheatDetected",
            "OnAdvancedMovementCheatDetected",
        })
        {
            TryPatchDiagnosticByName("CheatManager", method,
                                      prefix: nameof(DiagnosticPatches.AnalyticsCheat_Prefix),
                                      logMiss: logMisses);
        }

        RegisterDeepRoomDiagnostics(logMisses);
        // SteamManager.Awake + SteamPlatformManager.*LoginInitialize were
        // pure-logging diagnostics that fired during Unity's very first
        // wake-up tick. On december (EAC build, IL2CPP wrapped Steam
        // methods) MonoMod's lazy CompileMethodHook on the trampoline
        // crashes the CLR with 0x80131506 the instant Awake JITs. The
        // hooks gave us no behavioral leverage — only a `[call] …` log
        // line — so they're disabled. If anyone needs Steam-side visibility
        // later, route it through the game's own log lines instead.
        return true;
    }
    private void RegisterDebugConsolePatches(bool logMisses)
    {
        var complete = true;
        complete &= TryPatchDiagnosticByName("RecRoom.Debugging.DebugConsole",
                                             "Awake",
                                             args: Type.EmptyTypes,
                                             prefix: nameof(DebugConsolePatches.Awake_Prefix),
                                             logMiss: logMisses);
        complete &= TryPatchDiagnosticByName("RecRoom.Debugging.DebugConsole",
                                             "InputField_OnEndEdit",
                                             args: Type.EmptyTypes,
                                             prefix: nameof(DebugConsolePatches.InputFieldOnEndEdit_Prefix),
                                             logMiss: logMisses);

        // Dev commands that change movement/time trip retail anti-cheat
        // callbacks. Suppress the three callbacks present in the 2023.03.21
        // dump; keep the legacy height detector as a best-effort optional
        // patch for adjacent builds without keeping this retry loop alive.
        complete &= TryPatchDiagnosticByName("CheatManager",
                                             "OnTimeCheatDetected",
                                             args: Type.EmptyTypes,
                                             prefix: nameof(DebugConsolePatches.SuppressCheatDetected_Prefix),
                                             logMiss: logMisses);
        complete &= TryPatchDiagnosticByName("CheatManager",
                                             "OnObscuredTypeCheatDetected",
                                             args: Type.EmptyTypes,
                                             prefix: nameof(DebugConsolePatches.SuppressCheatDetected_Prefix),
                                             logMiss: logMisses);
        complete &= TryPatchDiagnosticByName("CheatManager",
                                             "OnAdvancedMovementCheatDetected",
                                             args: Type.EmptyTypes,
                                             prefix: nameof(DebugConsolePatches.SuppressCheatDetected_Prefix),
                                             logMiss: logMisses);
        TryPatchDiagnosticByName("CheatManager",
                                 "OnHeightCheatDetected",
                                 args: Type.EmptyTypes,
                                 prefix: nameof(DebugConsolePatches.SuppressCheatDetected_Prefix),
                                 logMiss: false);

        _debugConsolePatchesComplete = complete;
        if (complete)
            Log.Msg("[debugconsole] restoration patches armed");
    }
    private bool TryPatchDiagnosticAllOverloads(string typeName, string methodName,
                                                string? prefix = null, string? postfix = null,
                                                string? finalizer = null, bool logMiss = true)
    {
        var label = $"{typeName}.{methodName}(*)";
        if (_diagnosticPatchLabels.Contains(label))
            return true;

        var type = ResolveType(typeName);
        if (type is null)
        {
            if (logMiss) Log.Warning($"[patch-miss] {label}: type not found");
            return false;
        }

        var patched = 0;
        foreach (var method in AccessTools.GetDeclaredMethods(type))
        {
            if (method.Name != methodName) continue;
            if (method.IsAbstract || method.ContainsGenericParameters) continue;
            try
            {
                HarmonyInstance.Patch(method,
                    prefix: prefix is null ? null : new HarmonyMethod(GetPatchMethod(prefix)),
                    postfix: postfix is null ? null : new HarmonyMethod(GetPatchMethod(postfix)),
                    finalizer: finalizer is null ? null : new HarmonyMethod(GetPatchMethod(finalizer)));
                patched++;
            }
            catch (Exception ex)
            {
                Log.Warning($"[patch-fail] {type.FullName}.{method.Name}: {ex.Message}");
            }
        }

        if (patched == 0)
        {
            if (logMiss) Log.Warning($"[patch-miss] {label}: no overloads matched");
            return false;
        }

        _diagnosticPatchLabels.Add(label);
        Log.Msg($"[patch-ok] {label} x{patched}");
        return true;
    }
    private bool RegisterDeepRoomDiagnostics(bool logMisses)
    {
        var complete = true;

        complete &= TryPatchDiagnosticByName("UnityEngine.Debug", "LogError",
                                             args: new[] { typeof(object) },
                                             prefix: nameof(RoomLoadDiagnostics.LogError_Prefix),
                                             logMiss: logMisses);
        complete &= TryPatchDiagnosticByName("UnityEngine.Debug", "LogException",
                                             args: new[] { typeof(Exception) },
                                             prefix: nameof(RoomLoadDiagnostics.LogException_Prefix),
                                             logMiss: logMisses);

        foreach (var method in new[]
        {
            "ConnectUsingSettings",
            "ConnectToRegion",
            "JoinRoom",
            "JoinOrCreateRoom",
            "CreateRoom",
            "LeaveRoom",
            "Disconnect",
        })
        {
            complete &= TryPatchDiagnosticAllOverloads("PhotonNetwork", method,
                                                       prefix: nameof(RoomLoadDiagnostics.NamedCall_Prefix),
                                                       finalizer: nameof(RoomLoadDiagnostics.NamedCall_Finalizer),
                                                       logMiss: logMisses);
        }

        foreach (var item in new (string Type, string Method)[]
        {
            ("CMMIJKCDKPL", "CDNPLAGEDBK"),
            ("KGHEDLNPANK", "CDNPLAGEDBK"),
            ("HMPNINFEDFB", "IJIOJAIBPKE"),
            ("PhotonNetwork", "Disconnect"),
        })
        {
            complete &= TryPatchDiagnosticAllOverloads(item.Type, item.Method,
                                                       prefix: nameof(RoomLoadDiagnostics.NamedCall_Prefix),
                                                       finalizer: nameof(RoomLoadDiagnostics.NamedCall_Finalizer),
                                                       logMiss: logMisses);
        }

        complete &= PatchKnownRoomLoadMoveNext(logMisses);
        return true;
    }
    private void RegisterStudioTracePatches()
    {
        TryPatchByName("RecRoom.Core.Studio.RecRoomObjectPrefabManager", "RegisterPrefabs",
                       postfix: nameof(RoomLoadDiagnostics.StudioRegisterPrefabs_Postfix));
        TryPatchByName("RecRoom.Core.Studio.RecRoomObjectPrefabManager", "TryGetPrefab",
                       postfix: nameof(RoomLoadDiagnostics.StudioTryGetPrefab_Postfix));
        TryPatchByName("RecRoom.Core.Studio.RecRoomObjectPrefabManager", "IsKnownPrefab",
                       postfix: nameof(RoomLoadDiagnostics.StudioIsKnownPrefab_Postfix));
        TryPatchByName("NLDBPDCNNCF", "FKLDMPMFLBD",
                       prefix: nameof(RoomLoadDiagnostics.StudioUnityAssetFetch_Prefix));
    }
    private bool PatchKnownRoomLoadMoveNext(bool logMiss)
    {
        const string label = "room-load-state-machines.MoveNext";
        if (_diagnosticPatchLabels.Contains(label))
            return true;

        var nameNeedles = new[]
        {
            "OMJKHJLFOCO", "AFMPFLKGABO",
            "AOEBBIFGIKI", "FINJCCNPHEP", "FDDFOKNJIFL", "GMNHJNAHAAI",
            "MHDOLJJPEAD", "LFGLDJCMJDN", "PFDIKJHMBDI", "ICAGCHKPIOM",
            "JEBACILNLJI", "DINBAFNHLPJ", "HLHPNABABME", "PAPLNIPKAMG",
            "IBEOONPEELF", "JHPHMNGOFLB", "NLDBPDCNNCF", "LONOBOBHMJL",
            "BNDIAONDFFF", "HOONLPFFADG", "PHODPEHLHGM",
        };

        var patched = 0;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in GetLoadableTypes(asm))
            {
                var fullName = type.FullName ?? type.Name;
                var match = false;
                foreach (var needle in nameNeedles)
                {
                    if (fullName.IndexOf(needle, StringComparison.Ordinal) >= 0)
                    {
                        match = true;
                        break;
                    }
                }
                if (!match) continue;

                IEnumerable<MethodInfo> methods;
                try { methods = AccessTools.GetDeclaredMethods(type); }
                catch { continue; }

                foreach (var method in methods)
                {
                    if (method.Name != "MoveNext") continue;
                    if (method.IsAbstract || method.ContainsGenericParameters) continue;
                    try
                    {
                        HarmonyInstance.Patch(method,
                            finalizer: new HarmonyMethod(GetPatchMethod(nameof(RoomLoadDiagnostics.MoveNext_Finalizer))));
                        patched++;
                        Log.Msg($"[patch-ok] room-load MoveNext {type.FullName}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"[patch-fail] room-load MoveNext {type.FullName}: {ex.Message}");
                    }
                }
            }
        }

        if (patched == 0)
        {
            if (logMiss) Log.Warning("[patch-miss] room-load state-machine MoveNext hooks");
            return false;
        }

        _diagnosticPatchLabels.Add(label);
        return true;
    }
    private void RegisterProcessExceptionHandlers()
    {
        if (_processExceptionHandlersRegistered) return;
        _processExceptionHandlersRegistered = true;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                DiagnosticPatches.Write($"[process-exception] unhandled terminating={e.IsTerminating} {RoomLoadDiagnostics.FormatException(ex, e.ExceptionObject)}");
            }
            catch { }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try
            {
                DiagnosticPatches.Write($"[process-exception] unobserved-task {RoomLoadDiagnostics.FormatException(e.Exception, e.Exception)}");
            }
            catch { }
        };
    }
}

internal static class RoomLoadDiagnostics
{
    private static readonly Dictionary<string, int> Counts = new();
    private static int _globalCount;

    public static void LogError_Prefix(object? message)
    {
        DiagnosticPatches.Write($"[unity-error] LogError {Sanitize(message?.ToString() ?? "<null>", 1600)}");
        WriteStack("unity-error");
    }

    public static void LogException_Prefix(Exception exception)
    {
        DiagnosticPatches.Write($"[unity-exception] {FormatException(exception, exception)}");
        WriteStack("unity-exception");
    }

    public static void NamedCall_Prefix(MethodBase __originalMethod)
    {
        var name = FullMethodName(__originalMethod);
        if (!ShouldLog("call:" + name, 80)) return;

        DiagnosticPatches.Write($"[runtime-call] ENTER {name} [{Signature(__originalMethod)}]");
        if (IsImportantCall(name))
            WriteStack("runtime-call");
    }

    public static Exception? NamedCall_Finalizer(Exception? __exception, MethodBase __originalMethod)
    {
        if (__exception is not null)
        {
            DiagnosticPatches.Write($"[runtime-call] EXCEPTION {FullMethodName(__originalMethod)} {FormatException(__exception, __exception)}");
            WriteStack("runtime-call-exception");
        }
        return __exception;
    }

    public static void MoveNext_Prefix(object __instance, MethodBase __originalMethod)
    {
        var name = FullMethodName(__originalMethod);
        if (!ShouldLog("enter:" + name, 120)) return;
        DiagnosticPatches.Write($"[room-load] ENTER {name} state={DumpInstanceFields(__instance, 28)}");
    }

    public static void MoveNext_Postfix(object __instance, MethodBase __originalMethod)
    {
        var name = FullMethodName(__originalMethod);
        if (!ShouldLog("exit:" + name, 120)) return;
        DiagnosticPatches.Write($"[room-load] EXIT {name} state={DumpInstanceFields(__instance, 28)}");
    }

    public static Exception? MoveNext_Finalizer(Exception? __exception, object __instance, MethodBase __originalMethod)
    {
        if (__exception is not null)
        {
            DiagnosticPatches.Write(
                $"[room-load] EXCEPTION {FullMethodName(__originalMethod)} " +
                $"{FormatException(__exception, __exception)} state={DumpInstanceFields(__instance, 40)}");
            WriteStack("room-load-exception");
        }
        return __exception;
    }

    // ── Studio baked-asset load trace ──────────────────────────────────
    // Pinpoints where the baked-Studio-scene load chain breaks. The room
    // joins but renders no custom geometry and logs "Expected a prefab,
    // but found none"; the server serves everything but the client never
    // requests a bundle. These probes answer: (1) does the client ever
    // call the RecNet "get unity asset" API (FKLDMPMFLBD); (2) does it
    // ever register baked prefabs; (3) which prefab GUIDs does it look up
    // and miss.

    /// <summary>RecNet unity-asset getter
    /// <c>NLDBPDCNNCF.FKLDMPMFLBD(string unityAssetId, byte target, int version, CancellationToken)</c>
    /// — the call that builds <c>unity_assets/{id}/{target}/{version}</c>. If
    /// this never fires, the client decided not to fetch the bundle at all
    /// (trigger missing upstream); if it fires, the URL/host/response is the
    /// problem.</summary>
    public static void StudioUnityAssetFetch_Prefix(object? __0, object? __1, object? __2)
    {
        DiagnosticPatches.Write(
            $"[studio] UNITY-ASSET FETCH id={__0?.ToString() ?? "<null>"} " +
            $"target={__1?.ToString() ?? "?"} version={__2?.ToString() ?? "?"}");
        WriteStack("studio-assetfetch");
    }

    /// <summary><c>RecRoomObjectPrefabManager.RegisterPrefabs</c> — baked
    /// prefabs being registered after a bundle loads. If this never fires,
    /// no bundle was loaded.</summary>
    public static void StudioRegisterPrefabs_Postfix(object? __0)
    {
        int count = -1;
        try
        {
            if (__0 is not null)
            {
                var p = __0.GetType().GetProperty("Count");
                if (p?.GetValue(__0) is int c) count = c;
            }
        }
        catch { /* best-effort count */ }
        DiagnosticPatches.Write($"[studio] RegisterPrefabs count={count}");
        WriteStack("studio-registerprefabs");
    }

    /// <summary><c>RecRoomObjectPrefabManager.TryGetPrefab(Guid, out GameObject)</c>
    /// — the lookup that fails with "Expected a prefab, but found none".
    /// Logs the GUID + whether it resolved.</summary>
    public static void StudioTryGetPrefab_Postfix(object? __0, bool __result)
    {
        if (!ShouldLog("studio-trygetprefab", 200)) return;
        DiagnosticPatches.Write(
            $"[studio] TryGetPrefab guid={__0?.ToString() ?? "<null>"} found={__result}");
    }

    /// <summary><c>RecRoomObjectPrefabManager.IsKnownPrefab(Guid)</c>.</summary>
    public static void StudioIsKnownPrefab_Postfix(object? __0, bool __result)
    {
        if (!ShouldLog("studio-isknownprefab", 200)) return;
        DiagnosticPatches.Write(
            $"[studio] IsKnownPrefab guid={__0?.ToString() ?? "<null>"} known={__result}");
    }

    public static string FormatException(Exception? ex, object? fallback)
    {
        if (ex is null) return fallback?.ToString() ?? "<null>";
        var builder = new StringBuilder();
        AppendException(builder, ex, 0);
        return Sanitize(builder.ToString(), 5000);
    }

    private static void AppendException(StringBuilder builder, Exception ex, int depth)
    {
        if (depth > 4) return;
        if (builder.Length > 0) builder.Append(" | ");
        builder.Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
            builder.Append(" stack=").Append(ex.StackTrace);
        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
                AppendException(builder, inner, depth + 1);
        }
        else if (ex.InnerException is not null)
        {
            AppendException(builder, ex.InnerException, depth + 1);
        }
    }

    private static bool ShouldLog(string key, int perKeyLimit)
    {
        lock (Counts)
        {
            _globalCount++;
            if (_globalCount > 12000)
                return false;

            Counts.TryGetValue(key, out var count);
            if (count >= perKeyLimit)
                return false;

            Counts[key] = count + 1;
            return true;
        }
    }

    private static string DumpInstanceFields(object? instance, int maxFields)
    {
        if (instance is null) return "<null>";
        try
        {
            var type = instance.GetType();
            var parts = new List<string>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var field in type.GetFields(flags))
            {
                if (parts.Count >= maxFields)
                {
                    parts.Add("...");
                    break;
                }

                object? value;
                try { value = field.GetValue(instance); }
                catch (Exception ex)
                {
                    parts.Add($"{field.Name}=<read failed {ex.GetType().Name}>");
                    continue;
                }

                var described = DescribeValue(value, 400);
                if (ShouldExpandField(field, value))
                    described += " " + DumpNestedFields(value, 16);
                parts.Add($"{field.Name}={described}");
            }

            return $"{type.FullName} {{{string.Join("; ", parts)}}}";
        }
        catch (Exception ex)
        {
            return $"<dump failed {ex.GetType().Name}: {Sanitize(ex.Message, 400)}>";
        }
    }

    private static bool ShouldExpandField(FieldInfo field, object? value)
    {
        if (value is null) return false;
        if (value is string) return false;

        var valueType = value.GetType();
        if (valueType.IsPrimitive || valueType.IsEnum || value is decimal)
            return false;
        if (value is IEnumerable)
            return false;

        var fieldName = field.Name ?? string.Empty;
        var typeName = valueType.FullName ?? valueType.Name;
        return fieldName.IndexOf("this", StringComparison.OrdinalIgnoreCase) >= 0 ||
               typeName.IndexOf("OMJKHJLFOCO", StringComparison.Ordinal) >= 0 ||
               typeName.IndexOf("RoomPermissions", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string DumpNestedFields(object value, int maxFields)
    {
        try
        {
            var type = value.GetType();
            var parts = new List<string>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var field in type.GetFields(flags))
            {
                if (parts.Count >= maxFields)
                {
                    parts.Add("...");
                    break;
                }

                object? nested;
                try { nested = field.GetValue(value); }
                catch (Exception ex)
                {
                    parts.Add($"{field.Name}=<read failed {ex.GetType().Name}>");
                    continue;
                }

                parts.Add($"{field.Name}={DescribeValue(nested, 220)}");
            }

            return "{nested " + string.Join("; ", parts) + "}";
        }
        catch (Exception ex)
        {
            return $"{{nested dump failed {ex.GetType().Name}: {Sanitize(ex.Message, 220)}}}";
        }
    }

    private static string DescribeValue(object? value, int limit)
    {
        if (value is null) return "null";

        var type = value.GetType();
        if (value is string text) return "\"" + Sanitize(text, limit) + "\"";
        if (value is Exception ex) return FormatException(ex, ex);
        if (type.IsPrimitive || type.IsEnum || value is decimal)
            return Sanitize(value.ToString() ?? "<null>", limit);
        if (value is IEnumerable enumerable && value is not string)
            return DescribeEnumerable(enumerable, type, limit);

        var rendered = value.ToString();
        if (string.IsNullOrWhiteSpace(rendered) || rendered == type.FullName)
            return "<" + type.FullName + ">";
        return "<" + type.FullName + " " + Sanitize(rendered, limit) + ">";
    }

    private static string DescribeEnumerable(IEnumerable enumerable, Type type, int limit)
    {
        var parts = new List<string>();
        var count = 0;
        foreach (var item in enumerable)
        {
            if (count++ >= 6)
            {
                parts.Add("...");
                break;
            }
            parts.Add(DescribeValue(item, 120));
        }
        return "<" + type.FullName + " [" + Sanitize(string.Join(", ", parts), limit) + "]>";
    }

    private static string Signature(MethodBase method)
    {
        try
        {
            var parameters = method.GetParameters();
            var parts = new List<string>(parameters.Length);
            foreach (var parameter in parameters)
                parts.Add(parameter.ParameterType.FullName ?? parameter.ParameterType.Name);
            return string.Join(", ", parts);
        }
        catch
        {
            return "<signature unread>";
        }
    }

    private static string FullMethodName(MethodBase method) =>
        $"{method.DeclaringType?.FullName ?? "<null>"}.{method.Name}";

    private static bool IsImportantCall(string name) =>
        name.IndexOf("Disconnect", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("Join", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("Photon", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("CDNPLAGEDBK", StringComparison.Ordinal) >= 0 ||
        name.IndexOf("IJIOJAIBPKE", StringComparison.Ordinal) >= 0;

    private static void WriteStack(string tag)
    {
        try
        {
            DiagnosticPatches.Write($"[{tag}-stack] " + new StackTrace(skipFrames: 2, fNeedFileInfo: false));
        }
        catch (Exception ex)
        {
            DiagnosticPatches.Write($"[{tag}-stack] failed: {ex.Message}");
        }
    }

    private static string Sanitize(string value, int limit)
    {
        var redacted = Regex.Replace(value, @"(?i)(Bearer\s+)[-._~+/=A-Za-z0-9]+", "$1<redacted>");
        redacted = Regex.Replace(redacted, @"(?i)(access_token|accessToken|refresh_token|refreshToken|authorization|token|key)=([^&\s]+)", "$1=<redacted>");
        redacted = redacted.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return redacted.Length <= limit ? redacted : redacted[..limit] + "...<truncated>";
    }
}
internal static class DebugConsolePatches
{
    private static bool _consoleVisible;
    private static bool _pollErrorLogged;
    private static int _probeFrame;
    private static bool _probeDone;

    private static bool _inputResolved;
    private static MethodInfo? _getKeyDown;     // UnityEngine.Input.GetKeyDown(KeyCode)
    private static object? _toggleKeyValue;     // boxed KeyCode enum value

    private static bool _consoleResolved;
    private static Type? _consoleType;
    private static MethodInfo? _toggleMethod;     // ShowInputField(bool) / MCNBJFECHLJ(bool)
    private static MethodInfo? _setConsoleText;   // DebugConsole.SetConsoleText(string)
    private static List<ConfiguredCommand>? _commandCache;

    // Harmony prefix returning false → skips the original CheatManager
    // detector, so a tripped heuristic never runs its punish/drop path.
    public static bool SuppressCheatDetected_Prefix() => false;

    public static bool Awake_Prefix()
    {
        return false;
    }

    public static bool InputFieldOnEndEdit_Prefix(object __instance)
    {
        var command = ReadConsoleInput(__instance);
        if (string.IsNullOrWhiteSpace(command))
            return true;

        ExecuteCommand(command, __instance);
        return false;
    }

    public static string FormatConfiguredCommandsForLog()
    {
        var commands = GetAvailableCommands();
        if (commands.Count == 0) return "<none>";

        var parts = new List<string>();
        foreach (var command in commands)
            parts.Add($"{command.Label}={command.Command}");
        return string.Join(", ", parts);
    }

    // Polled every frame from DebugMod.OnUpdate while EnableDebugConsole is set.
    public static void PollToggleKey()
    {
        try
        {
            if (!_inputResolved) ResolveInput();
            // One-shot probe ~3s in (≈180 frames @60fps): log whether the
            // console object actually exists, so we learn that even if the
            // hotkey never fires.
            if (!_probeDone && _probeFrame++ > 180)
            {
                _probeDone = true;
                ProbeConsole();
            }
            if (_getKeyDown is null || _toggleKeyValue is null) return;
            if (_getKeyDown.Invoke(null, new[] { _toggleKeyValue }) is true)
            {
                DebugMod.Log.Msg($"[debugconsole] toggle key '{DebugMod.Cfg.DebugConsoleToggleKey}' detected");
                Toggle();
            }
        }
        catch (Exception ex)
        {
            if (!_pollErrorLogged) { _pollErrorLogged = true; DebugMod.Log.Warning($"[debugconsole] poll failed (logged once): {ex}"); }
        }
    }

    // Diagnostic: reports whether a live DebugConsole component exists and
    // whether the show/hide method resolved — without needing a keypress.
    private static void ProbeConsole()
    {
        if (!_consoleResolved) ResolveConsole();
        if (_consoleType is null) { DebugMod.Log.Warning("[debugconsole] probe: DebugConsole type NOT resolved"); return; }
        try
        {
            var active = FindConsoleInstance();
            DebugMod.Log.Msg($"[debugconsole] probe: type={_consoleType.FullName}, toggleMethod={(_toggleMethod?.Name ?? "<none>")}, activeInstance={(active is null ? "null" : "found")}");
        }
        catch (Exception ex) { DebugMod.Log.Warning($"[debugconsole] probe failed: {ex.Message}"); }
    }

    public static void ExecuteCommand(string command) => ExecuteCommand(command, consoleInstance: null);

    private static void ExecuteCommand(string command, object? consoleInstance)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        var output = RunCommand(command);
        if (consoleInstance is null)
        {
            if (!_consoleResolved) ResolveConsole();
            consoleInstance = FindConsoleInstance();
        }
        if (consoleInstance is not null)
            WriteConsoleOutput(consoleInstance, output);

        var parts = SplitCommandLine(command);
        var verb = parts.Count > 0 ? parts[0] : "<empty>";
        DebugMod.Log.Msg($"[debugconsole] command '{CleanLabel(verb)}': {FirstLine(output)}");
    }

    private static string RunCommand(string commandLine)
    {
        var tokens = SplitCommandLine(commandLine);
        if (tokens.Count == 0)
            return string.Empty;

        var verb = tokens[0];
        if (IsHelpCommand(verb))
            return BuildCommandReferenceText("Debug Console Commands", maxCommands: 120);

        var args = tokens.GetRange(1, tokens.Count - 1);
        if (!TryFindCommand(verb, out var command))
            return $"Unknown debug command: {verb}\n\n{BuildCommandReferenceText("Available Debug Console Commands", maxCommands: 80)}";

        if (!command.HasRuntimeTarget)
        {
            var expanded = SplitCommandLine(command.Command);
            if (expanded.Count == 0)
                return $"Debug command '{verb}' has no backing command.";

            var expandedArgs = expanded.Count > 1 ? expanded.GetRange(1, expanded.Count - 1) : new List<string>();
            expandedArgs.AddRange(args);
            if (!TryFindCommand(expanded[0], out command) || !command.HasRuntimeTarget)
                return $"Debug command '{verb}' points at '{command.Command}', but no runtime command metadata was found.";
            args = expandedArgs;
        }

        if (!TryResolveCommandType(command, out var targetType, out var typeError))
            return typeError;

        if (!TryFindCallableMethod(targetType!, command.MethodName, args, out var method, out var converted, out var methodError))
            return methodError;

        object? target = null;
        if (!method!.IsStatic)
        {
            target = FindFirstInstance(targetType!);
            if (target is null)
                return $"Command '{command.MethodName}' needs a live {targetType!.Name} instance, but none was found.";
        }

        try
        {
            var result = method.Invoke(target, converted);
            var suffix = result is null ? string.Empty : $"\nResult: {FormatResult(result)}";
            return $"Executed: {command.Label}{suffix}";
        }
        catch (TargetInvocationException ex)
        {
            var inner = ex.InnerException ?? ex;
            return $"Command '{command.MethodName}' failed: {inner.GetType().Name}: {inner.Message}";
        }
        catch (Exception ex)
        {
            return $"Command '{command.MethodName}' failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static bool IsHelpCommand(string verb) =>
        string.Equals(verb, "help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(verb, "?", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(verb, "ListRegisteredCommands", StringComparison.OrdinalIgnoreCase);

    private static bool TryResolveCommandType(ConfiguredCommand command, out Type? type, out string error)
    {
        foreach (var name in new[] { command.ObfuscatedDeclaringType, command.DeclaringType })
        {
            var normalized = NormalizeTypeName(name);
            if (string.IsNullOrWhiteSpace(normalized)) continue;
            type = DebugMod.ResolveType(normalized);
            if (type is not null)
            {
                error = string.Empty;
                return true;
            }
        }

        type = null;
        error = $"Command '{command.MethodName}' has no resolvable declaring type (declaring='{command.DeclaringType}', obfuscated='{command.ObfuscatedDeclaringType}').";
        return false;
    }

    private static string NormalizeTypeName(string value)
    {
        value = (value ?? string.Empty).Trim();
        var comma = value.IndexOf(',');
        return comma >= 0 ? value[..comma].Trim() : value;
    }

    private static bool TryFindCallableMethod(Type type, string methodName, List<string> args,
                                               out MethodInfo? method, out object?[] converted, out string error)
    {
        foreach (var candidate in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            if (candidate.Name != methodName) continue;
            if (candidate.IsAbstract || candidate.ContainsGenericParameters) continue;
            if (TryConvertArguments(candidate.GetParameters(), args, out converted))
            {
                method = candidate;
                error = string.Empty;
                return true;
            }
        }

        method = null;
        converted = Array.Empty<object?>();
        error = $"No overload of {type.Name}.{methodName} accepts {args.Count} argument(s): {string.Join(" ", args)}";
        return false;
    }

    private static bool TryConvertArguments(ParameterInfo[] parameters, List<string> args, out object?[] converted)
    {
        converted = new object?[parameters.Length];
        var argIndex = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (IsVector3(parameter.ParameterType) && args.Count - argIndex >= 3)
            {
                if (!TryParseFloat(args[argIndex], out var x) ||
                    !TryParseFloat(args[argIndex + 1], out var y) ||
                    !TryParseFloat(args[argIndex + 2], out var z) ||
                    !TryCreateVector3(parameter.ParameterType, x, y, z, out converted[i]))
                    return false;
                argIndex += 3;
                continue;
            }

            if (argIndex >= args.Count)
            {
                if (parameter.HasDefaultValue)
                {
                    converted[i] = parameter.DefaultValue;
                    continue;
                }
                return false;
            }

            if (!TryConvertArgument(args[argIndex], parameter.ParameterType, out converted[i]))
                return false;
            argIndex++;
        }

        return argIndex == args.Count;
    }

    private static bool TryConvertArgument(string value, Type targetType, out object? converted)
    {
        converted = null;

        if (targetType == typeof(string) || targetType.FullName == "System.String")
        {
            converted = value;
            return true;
        }

        var nullable = Nullable.GetUnderlyingType(targetType);
        if (nullable is not null)
            targetType = nullable;

        if (targetType == typeof(bool))
        {
            if (bool.TryParse(value, out var b))
            {
                converted = b;
                return true;
            }
            if (string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
                value == "1")
            {
                converted = true;
                return true;
            }
            if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
                value == "0")
            {
                converted = false;
                return true;
            }
            return false;
        }

        if (targetType.IsEnum)
        {
            try
            {
                converted = Enum.Parse(targetType, value, ignoreCase: true);
                return true;
            }
            catch { return false; }
        }

        try
        {
            if (targetType == typeof(int))    { converted = int.Parse(value, CultureInfo.InvariantCulture); return true; }
            if (targetType == typeof(long))   { converted = long.Parse(value, CultureInfo.InvariantCulture); return true; }
            if (targetType == typeof(float))  { converted = float.Parse(value, CultureInfo.InvariantCulture); return true; }
            if (targetType == typeof(double)) { converted = double.Parse(value, CultureInfo.InvariantCulture); return true; }
            if (targetType == typeof(short))  { converted = short.Parse(value, CultureInfo.InvariantCulture); return true; }
            if (targetType == typeof(byte))   { converted = byte.Parse(value, CultureInfo.InvariantCulture); return true; }
        }
        catch { return false; }

        return false;
    }

    private static bool IsVector3(Type type) =>
        type.Name == "Vector3" && (type.Namespace == "UnityEngine" || type.FullName?.EndsWith(".Vector3", StringComparison.Ordinal) == true);

    private static bool TryParseFloat(string value, out float result) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static bool TryCreateVector3(Type vectorType, float x, float y, float z, out object? vector)
    {
        vector = null;
        try
        {
            var ctor = vectorType.GetConstructor(new[] { typeof(float), typeof(float), typeof(float) });
            if (ctor is null) return false;
            vector = ctor.Invoke(new object[] { x, y, z });
            return true;
        }
        catch { return false; }
    }

    private static object? FindFirstInstance(Type type)
    {
        try
        {
            var il2cppType = ToIl2CppType(type);
            if (il2cppType is null) return null;
            var resources = DebugMod.ResolveType("UnityEngine.Resources");
            var all = OneArgMethod(resources, "FindObjectsOfTypeAll", il2cppType)?.Invoke(null, new[] { il2cppType });
            var count = ArrLen(all);
            for (var i = 0; i < count; i++)
            {
                var item = ArrItem(all, i);
                if (item is not null) return item;
            }
        }
        catch { }
        return null;
    }

    private static string FormatResult(object result)
    {
        var text = result.ToString() ?? result.GetType().Name;
        return text.Length <= 500 ? text : text[..500] + "...";
    }

    private static string FirstLine(string value)
    {
        value = value.Replace("\r", "");
        var idx = value.IndexOf('\n');
        return idx >= 0 ? value[..idx] : value;
    }

    private static List<string> SplitCommandLine(string value)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(value)) return result;

        var sb = new StringBuilder();
        var inQuotes = false;
        var quote = '\0';
        var escaping = false;

        foreach (var ch in value.Trim())
        {
            if (escaping)
            {
                sb.Append(ch);
                escaping = false;
                continue;
            }

            if (ch == '\\')
            {
                escaping = true;
                continue;
            }

            if (inQuotes)
            {
                if (ch == quote)
                {
                    inQuotes = false;
                    continue;
                }
                sb.Append(ch);
                continue;
            }

            if (ch is '"' or '\'')
            {
                inQuotes = true;
                quote = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
                continue;
            }

            sb.Append(ch);
        }

        if (escaping) sb.Append('\\');
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }

    private static void ResolveInput()
    {
        _inputResolved = true;
        var keyCodeType = DebugMod.ResolveType("UnityEngine.KeyCode");
        var inputType   = DebugMod.ResolveType("UnityEngine.Input");
        if (keyCodeType is null || inputType is null)
        {
            DebugMod.Log.Warning("[debugconsole] UnityEngine.Input/KeyCode not found — hotkey disabled");
            return;
        }
        try { _toggleKeyValue = Enum.Parse(keyCodeType, DebugMod.Cfg.DebugConsoleToggleKey, ignoreCase: true); }
        catch
        {
            DebugMod.Log.Warning($"[debugconsole] '{DebugMod.Cfg.DebugConsoleToggleKey}' is not a KeyCode; defaulting to BackQuote");
            _toggleKeyValue = Enum.Parse(keyCodeType, "BackQuote");
        }
        _getKeyDown = AccessTools.Method(inputType, "GetKeyDown", new[] { keyCodeType });
        if (_getKeyDown is null) DebugMod.Log.Warning("[debugconsole] Input.GetKeyDown(KeyCode) not found");
        else DebugMod.Log.Msg($"[debugconsole] hotkey armed: '{DebugMod.Cfg.DebugConsoleToggleKey}' (poll live)");
    }

    private static void ResolveConsole()
    {
        _consoleResolved = true;
        _consoleType = DebugMod.ResolveType("RecRoom.Debugging.DebugConsole");
        if (_consoleType is null) { DebugMod.Log.Warning("[debugconsole] DebugConsole type not found"); return; }

        // Prefer the readable 2020.03 name, then the 2020.12 obfuscated
        // name, then a structural fallback that fits both builds.
        _toggleMethod = AccessTools.Method(_consoleType, "ShowInputField", new[] { typeof(bool) })
                        ?? AccessTools.Method(_consoleType, "MCNBJFECHLJ", new[] { typeof(bool) })
                        ?? AccessTools.Method(_consoleType, "KIAMDEDKFBC", new[] { typeof(bool) })
                        ?? FindToggleByShape(_consoleType);
        if (_toggleMethod is null) DebugMod.Log.Warning("[debugconsole] show/hide method not found");
    }

    // The console's show/hide is the single private, instance, void method
    // taking exactly one bool, excluding compiler-generated members (the
    // property backing setter on 2020.12 is also void(bool) but is marked
    // [CompilerGenerated]).
    private static MethodInfo? FindToggleByShape(Type t)
    {
        MethodInfo? found = null;
        foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (m.ReturnType != typeof(void)) continue;
            var ps = m.GetParameters();
            if (ps.Length != 1 || ps[0].ParameterType != typeof(bool)) continue;
            if (m.Name.StartsWith("set_", StringComparison.Ordinal)) continue;
            if (m.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false)) continue;
            if (found is not null) return null; // ambiguous — don't guess
            found = m;
        }
        return found;
    }

    public static void Toggle()
    {
        if (!_consoleResolved) ResolveConsole();
        if (_consoleType is null || _toggleMethod is null) return;
        try
        {
            var instance = FindConsoleInstance();
            if (instance is null)
            {
                DebugMod.Log.Warning("[debugconsole] no live DebugConsole in scene — it may not be instantiated on this build");
                return;
            }
            // Best-effort: flip the static "show button" enable so the
            // game's own Update keeps the console available instead of
            // re-hiding it. Failures are ignored (obfuscated builds).
            EnableShowButton();
            _consoleVisible = !_consoleVisible;
            _toggleMethod.Invoke(instance, new object[] { _consoleVisible });
            if (_consoleVisible)
                ShowConfiguredCommands(instance);
            DebugMod.Log.Msg($"[debugconsole] {(_consoleVisible ? "opened" : "closed")}");
        }
        catch (Exception ex) { DebugMod.Log.Warning($"[debugconsole] toggle failed: {ex.Message}"); }
    }

    private static object? FindConsoleInstance()
    {
        if (_consoleType is null) return null;
        try
        {
            var il2cppType = ToIl2CppType(_consoleType);
            if (il2cppType is null) return null;

            var resources = DebugMod.ResolveType("UnityEngine.Resources");
            var all = OneArgMethod(resources, "FindObjectsOfTypeAll", il2cppType)?.Invoke(null, new[] { il2cppType });
            var count = ArrLen(all);
            for (var i = 0; i < count; i++)
            {
                var item = ArrItem(all, i);
                if (item is not null) return item;
            }

            var objType = DebugMod.ResolveType("UnityEngine.Object");
            var findOne = OneArgMethod(objType, "FindObjectOfType", il2cppType);
            return findOne?.Invoke(null, new[] { il2cppType });
        }
        catch (Exception ex)
        {
            DebugMod.Log.Warning($"[debugconsole] FindConsoleInstance failed: {ex.Message}");
            return null;
        }
    }

    private static void ShowConfiguredCommands(object instance)
    {
        try
        {
            if (_consoleType is null) return;
            _setConsoleText ??= AccessTools.Method(_consoleType, "SetConsoleText", new[] { typeof(string) });
            if (_setConsoleText is null)
            {
                DebugMod.Log.Warning($"[debugconsole] SetConsoleText(string) not found; configured commands: {FormatConfiguredCommandsForLog()}");
                return;
            }

            _setConsoleText.Invoke(instance, new object[] { BuildConfiguredCommandText() });
            DebugMod.Log.Msg($"[debugconsole] displayed configured commands: {FormatConfiguredCommandsForLog()}");
        }
        catch (Exception ex)
        {
            DebugMod.Log.Warning($"[debugconsole] failed to display configured commands: {ex.Message}");
        }
    }

    private static string BuildConfiguredCommandText()
    {
        return BuildCommandReferenceText("DorkNet debug commands", maxCommands: 80) + "\n\nType a command and press Enter.";
    }

    private static string ReadConsoleInput(object consoleInstance)
    {
        var input = FindConsoleInput(consoleInstance);
        return input is null ? string.Empty : ReadText(input);
    }

    private static void WriteConsoleOutput(object consoleInstance, string text)
    {
        try
        {
            if (_consoleType is not null)
            {
                _setConsoleText ??= AccessTools.Method(_consoleType, "SetConsoleText", new[] { typeof(string) });
                if (_setConsoleText is not null && _setConsoleText.DeclaringType?.IsInstanceOfType(consoleInstance) == true)
                {
                    try
                    {
                        _setConsoleText.Invoke(consoleInstance, new object[] { text });
                        return;
                    }
                    catch (TargetException)
                    {
                        // The live IL2CPP proxy can differ from the resolved wrapper type.
                    }
                }
            }

            var input = FindConsoleInput(consoleInstance);
            if (input is not null)
                SetText(input, text);
        }
        catch (Exception ex)
        {
            DebugMod.Log.Warning($"[debugconsole] output write failed: {ex.Message}");
        }
    }

    private static object? FindConsoleInput(object consoleInstance)
    {
        foreach (var name in new[] { "KECJGKLKFGP", "inputField", "InputField" })
        {
            var value = GetMemberValue(consoleInstance, name);
            if (value is not null) return value;
        }

        var type = consoleInstance.GetType();
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (!LooksLikeInputField(prop.PropertyType) || !prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
            try { if (prop.GetValue(consoleInstance) is { } value) return value; } catch { }
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (!LooksLikeInputField(field.FieldType)) continue;
            try { if (field.GetValue(consoleInstance) is { } value) return value; } catch { }
        }

        return null;
    }

    private static bool LooksLikeInputField(Type type) =>
        type.Name.IndexOf("InputField", StringComparison.OrdinalIgnoreCase) >= 0 ||
        type.FullName?.IndexOf("InputField", StringComparison.OrdinalIgnoreCase) >= 0;

    private static string ReadText(object textObject)
    {
        try
        {
            var type = textObject.GetType();
            var prop = type.GetProperty("text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                       ?? type.GetProperty("Text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop is not null && prop.CanRead && prop.GetValue(textObject) is string s)
                return s;

            var getter = type.GetMethod("get_text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)
                         ?? type.GetMethod("get_Text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            return getter?.Invoke(textObject, Array.Empty<object>()) as string ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static void SetText(object textObject, string text)
    {
        try
        {
            var type = textObject.GetType();
            var prop = type.GetProperty("text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                       ?? type.GetProperty("Text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop is not null && prop.CanWrite)
            {
                prop.SetValue(textObject, text);
                return;
            }

            var setter = type.GetMethod("set_text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null)
                         ?? type.GetMethod("set_Text", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            setter?.Invoke(textObject, new object[] { text });
        }
        catch { }
    }

    public static string BuildCommandReferenceText(string header, int maxCommands = int.MaxValue)
    {
        var commands = GetAvailableCommands();
        var sb = new StringBuilder();
        sb.AppendLine(header);
        sb.AppendLine();
        if (commands.Count == 0)
        {
            sb.AppendLine("No DebugConsoleCommandConfig commands found.");
        }
        else
        {
            var visible = Math.Min(commands.Count, maxCommands);
            for (var i = 0; i < visible; i++)
                sb.AppendLine($"{i + 1}. {commands[i].Label}: {commands[i].Command}");
            if (visible < commands.Count)
            {
                sb.AppendLine();
                sb.AppendLine($"+ {commands.Count - visible} more commands available through the debug console.");
            }
        }
        return sb.ToString().TrimEnd();
    }

    internal static List<ConfiguredCommand> GetAvailableCommands()
    {
        if (_commandCache is not null) return _commandCache;

        var commands = new List<ConfiguredCommand>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddConfiguredCommands(commands, seen);
        AddRuntimeCommands(commands, seen);
        commands.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
        _commandCache = commands;
        DebugMod.Log.Msg($"[debugconsole] command table loaded: {commands.Count} command(s)");
        return commands;
    }

    // Read-only one-shot diagnostic (Cfg.DumpDevCommands). Writes a full report
    // to UserData/dorknet-devcommands.txt: does a live DebugConsole exist, and
    // for every runtime command meta, does its declaring type resolve and does
    // a method with that name exist on it (i.e. is it actually callable on this
    // obfuscated build). Invokes NOTHING and touches no game UI — safe to run
    // in a live session. This is what turns the text-console path from guessing
    // into engineering: the resolvable/total tally says whether the feature is
    // buildable as-is or needs an obfuscation name map.
    // Returns true once it observes a populated runtime command table (so the
    // caller can stop retrying); false while the config singleton isn't up yet.
    public static bool DumpCommandDiagnostics()
    {
        var sb = new StringBuilder();
        int runtime = 0;
        void W(string s) { sb.Append(s).Append('\n'); DebugMod.Log.Msg("[devcmd] " + s); }

        try
        {
            // Force a fresh scan — an early call (before the config singleton
            // loads) must not poison the cache with an empty table.
            _commandCache = null;
            W("=== DorkNet dev-command diagnostic ===");

            if (!_consoleResolved) ResolveConsole();
            object? liveConsole = null;
            try { liveConsole = FindConsoleInstance(); } catch (Exception ex) { W($"FindConsoleInstance threw: {ex.Message}"); }
            W($"DebugConsole type resolved : {_consoleType?.FullName ?? "<none>"}");
            W($"DebugConsole live instance : {(liveConsole is null ? "NULL (UI shell not instantiated — text console can't be shown, would need our own UI)" : "FOUND (UI can be driven)")}");
            W($"toggle method             : {_toggleMethod?.Name ?? "<none>"}");
            W("");

            List<ConfiguredCommand> commands;
            try { commands = GetAvailableCommands(); }
            catch (Exception ex) { W($"GetAvailableCommands threw: {ex}"); commands = new List<ConfiguredCommand>(); }

            int typeOk = 0, methodOk = 0;
            var samplesOk = new List<string>();
            var samplesBad = new List<string>();

            foreach (var c in commands)
            {
                if (!c.HasRuntimeTarget) continue;
                runtime++;

                var resolvedType = TryResolveCommandType(c, out var t, out _) ? t : null;
                if (resolvedType is null)
                {
                    if (samplesBad.Count < 30)
                        samplesBad.Add($"  [no-type] {c.MethodName}  (declaring='{c.DeclaringType}' obf='{c.ObfuscatedDeclaringType}')");
                    continue;
                }
                typeOk++;

                var hasMethod = false;
                foreach (var m in resolvedType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
                    if (m.Name == c.MethodName) { hasMethod = true; break; }

                if (hasMethod)
                {
                    methodOk++;
                    if (samplesOk.Count < 40) samplesOk.Add($"  [OK] {resolvedType.Name}.{c.MethodName}");
                }
                else if (samplesBad.Count < 30)
                {
                    samplesBad.Add($"  [no-method] {resolvedType.Name}.{c.MethodName}  (type resolved, method name not present → obfuscated/stripped)");
                }
            }

            W($"total commands in table   : {commands.Count}");
            W($"runtime-target commands   : {runtime}");
            W($"  declaring type resolves : {typeOk}");
            W($"  method callable by name : {methodOk}   <-- this number decides feasibility");
            W("");
            W("--- sample CALLABLE commands ---");
            foreach (var s in samplesOk) W(s);
            W("");
            W("--- sample UNRESOLVED commands ---");
            foreach (var s in samplesBad) W(s);
            W("");
            W("--- FULL runtime command list (label | methodName | declaringType | obfuscatedDeclaringType) ---");
            foreach (var c in commands)
            {
                if (!c.HasRuntimeTarget) continue;
                sb.Append("  ").Append(c.Label).Append(" | ").Append(c.MethodName)
                  .Append(" | ").Append(c.DeclaringType).Append(" | ").Append(c.ObfuscatedDeclaringType).Append('\n');
            }
        }
        catch (Exception ex)
        {
            sb.Append("DIAGNOSTIC FAILED: ").Append(ex).Append('\n');
            DebugMod.Log.Warning($"[devcmd] diagnostic failed: {ex.Message}");
        }

        // Only persist the report once the table is actually populated, so an
        // early empty attempt doesn't overwrite a good report from a later try.
        if (runtime > 0)
        {
            try
            {
                var dir = Path.GetDirectoryName(DebugMod.DiagnosticsLogPath) ?? ".";
                var outPath = Path.Combine(dir, "dorknet-devcommands.txt");
                File.WriteAllText(outPath, sb.ToString());
                DebugMod.Log.Msg($"[devcmd] wrote {outPath} ({runtime} runtime commands)");
            }
            catch (Exception ex) { DebugMod.Log.Warning($"[devcmd] could not write report: {ex.Message}"); }
        }
        else
        {
            DebugMod.Log.Msg("[devcmd] command table not populated yet — will retry");
        }

        return runtime > 0;
    }

    private static bool TryFindCommand(string verb, out ConfiguredCommand command)
    {
        var commands = GetAvailableCommands();
        foreach (var candidate in commands)
        {
            if (!candidate.HasRuntimeTarget) continue;
            if (CommandMatches(candidate, verb))
            {
                command = candidate;
                return true;
            }
        }
        foreach (var candidate in commands)
        {
            if (CommandMatches(candidate, verb))
            {
                command = candidate;
                return true;
            }
        }

        command = default;
        return false;
    }

    private static bool CommandMatches(ConfiguredCommand command, string verb)
    {
        if (string.Equals(command.MethodName, verb, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(command.Command, verb, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(command.Label, verb, StringComparison.OrdinalIgnoreCase)) return true;

        var shortLabel = command.Label;
        var dot = shortLabel.LastIndexOf('.');
        if (dot >= 0 && dot < shortLabel.Length - 1)
            shortLabel = shortLabel[(dot + 1)..];
        if (string.Equals(shortLabel, verb, StringComparison.OrdinalIgnoreCase)) return true;

        var commandTokens = SplitCommandLine(command.Command);
        return commandTokens.Count > 0 && string.Equals(commandTokens[0], verb, StringComparison.OrdinalIgnoreCase);
    }

    private static void AddConfiguredCommands(List<ConfiguredCommand> commands, HashSet<string> seen)
    {
        foreach (var entry in DebugMod.Cfg.DevCommands)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var split = entry.IndexOf('=');
            var label = split >= 0 ? entry[..split].Trim() : entry.Trim();
            var command = split >= 0 ? entry[(split + 1)..].Trim() : entry.Trim();
            if (string.IsNullOrWhiteSpace(command)) continue;
            if (string.IsNullOrWhiteSpace(label)) label = command;
            var parts = SplitCommandLine(command);
            var methodName = parts.Count > 0 ? parts[0] : command;
            AddCommand(commands, seen, label, command, declaringType: string.Empty, obfuscatedDeclaringType: string.Empty, methodName, isRuntime: false);
        }
    }

    private static void AddRuntimeCommands(List<ConfiguredCommand> commands, HashSet<string> seen)
    {
        try
        {
            var cfgType = DebugMod.ResolveType("RecRoom.Debugging.DebugConsoleCommandConfig");
            if (cfgType is null)
            {
                DebugMod.Log.Warning("[debugconsole] DebugConsoleCommandConfig type not found");
                return;
            }

            foreach (var method in cfgType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (method.GetParameters().Length != 0) continue;
                if (!cfgType.IsAssignableFrom(method.ReturnType)) continue;
                try { AddCommandConfig(method.Invoke(null, Array.Empty<object>()), commands, seen); }
                catch { }
            }

            var il2cppType = ToIl2CppType(cfgType);
            var resources = DebugMod.ResolveType("UnityEngine.Resources");
            if (il2cppType is not null && resources is not null)
            {
                var all = OneArgMethod(resources, "FindObjectsOfTypeAll", il2cppType)?.Invoke(null, new[] { il2cppType });
                var count = ArrLen(all);
                for (var i = 0; i < count; i++)
                    AddCommandConfig(ArrItem(all, i), commands, seen);
            }
        }
        catch (Exception ex)
        {
            DebugMod.Log.Warning($"[debugconsole] runtime command scan failed: {ex.Message}");
        }
    }

    private static void AddCommandConfig(object? config, List<ConfiguredCommand> commands, HashSet<string> seen)
    {
        if (config is null) return;
        var metas = GetMemberValue(config, "Metas");
        var count = ArrLen(metas);
        for (var i = 0; i < count; i++)
        {
            var meta = ArrItem(metas, i);
            if (meta is null) continue;
            var methodName = ReadStringMember(meta, "MethodName");
            if (string.IsNullOrWhiteSpace(methodName)) continue;
            var declaringType = ReadStringMember(meta, "DeclaringType");
            var obfuscatedDeclaringType = ReadStringMember(meta, "ObfuscatedDeclaringType");
            var label = string.IsNullOrWhiteSpace(declaringType)
                ? methodName
                : $"{ShortTypeName(declaringType)}.{methodName}";
            AddCommand(commands, seen, label, methodName, declaringType, obfuscatedDeclaringType, methodName, isRuntime: true);
        }
    }

    private static void AddCommand(List<ConfiguredCommand> commands, HashSet<string> seen, string label, string command,
                                   string declaringType, string obfuscatedDeclaringType, string methodName, bool isRuntime)
    {
        label = CleanLabel(label);
        command = (command ?? string.Empty).Trim();
        methodName = (methodName ?? string.Empty).Trim();
        if (command.Length == 0 || methodName.Length == 0) return;

        var key = isRuntime
            ? $"{NormalizeTypeName(obfuscatedDeclaringType)}|{NormalizeTypeName(declaringType)}|{methodName}"
            : $"configured|{label}|{command}";
        if (!seen.Add(key)) return;

        commands.Add(new ConfiguredCommand(label, command, declaringType, obfuscatedDeclaringType, methodName, isRuntime));
    }

    private static object? GetMemberValue(object instance, string name)
    {
        var type = instance.GetType();
        var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (prop is not null && prop.CanRead && prop.GetIndexParameters().Length == 0)
        {
            try { return prop.GetValue(instance); } catch { }
        }

        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field is not null)
        {
            try { return field.GetValue(instance); } catch { }
        }

        return null;
    }

    private static string ReadStringMember(object instance, string name)
    {
        return GetMemberValue(instance, name)?.ToString() ?? string.Empty;
    }

    private static string CleanLabel(string label)
    {
        label = (label ?? string.Empty).Trim();
        if (label.Length == 0) return "Command";

        var cleaned = new char[label.Length];
        for (var i = 0; i < label.Length; i++)
        {
            var ch = label[i];
            cleaned[i] = char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' or ' ' ? ch : '_';
        }

        return new string(cleaned);
    }

    private static string ShortTypeName(string typeName)
    {
        var idx = typeName.LastIndexOf('.');
        return idx >= 0 && idx < typeName.Length - 1 ? typeName[(idx + 1)..] : typeName;
    }

    internal readonly struct ConfiguredCommand
    {
        public ConfiguredCommand(string label, string command, string declaringType,
                                 string obfuscatedDeclaringType, string methodName, bool hasRuntimeTarget)
        {
            Label = label;
            Command = command;
            DeclaringType = declaringType;
            ObfuscatedDeclaringType = obfuscatedDeclaringType;
            MethodName = methodName;
            HasRuntimeTarget = hasRuntimeTarget;
        }

        public string Label { get; }
        public string Command { get; }
        public string DeclaringType { get; }
        public string ObfuscatedDeclaringType { get; }
        public string MethodName { get; }
        public bool HasRuntimeTarget { get; }
    }

    private static void EnableShowButton()
    {
        if (_consoleType is null) return;
        try
        {
            var prop = _consoleType.GetProperty("ShowButton", BindingFlags.Public | BindingFlags.Static);
            if (prop is not null && prop.CanWrite) { prop.SetValue(null, true); return; }
            foreach (var name in new[] { "showButton", "IEGCNLHPKFH" })
            {
                var f = _consoleType.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
                if (f is not null && f.FieldType == typeof(bool)) { f.SetValue(null, true); return; }
            }
        }
        catch { /* best-effort — the toggle still works without it */ }
    }

    private static object? ToIl2CppType(Type managed)
    {
        try
        {
            var il2cppTypeType = DebugMod.ResolveType("Il2CppInterop.Runtime.Il2CppType");
            var from = il2cppTypeType?.GetMethod("From", new[] { typeof(Type), typeof(bool) })
                       ?? il2cppTypeType?.GetMethod("From", new[] { typeof(Type) });
            if (from is null) return null;
            var args = from.GetParameters().Length == 2
                ? new object[] { managed, true }
                : new object[] { managed };
            return from.Invoke(null, args);
        }
        catch { return null; }
    }

    private static MethodInfo? OneArgMethod(Type? type, string name, object arg)
    {
        if (type is null) return null;
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.Name != name) continue;
            var parameters = method.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(arg))
                return method;
        }
        return null;
    }

    private static int ArrLen(object? array)
    {
        if (array is null) return 0;
        if (array is Array managedArray) return managedArray.Length;
        var type = array.GetType();
        var length = type.GetProperty("Length") ?? type.GetProperty("Count");
        return (length?.GetValue(array) as int?) ?? 0;
    }

    private static object? ArrItem(object? array, int index)
    {
        if (array is null) return null;
        if (array is Array managedArray) return managedArray.GetValue(index);
        var getItem = array.GetType().GetMethod("get_Item", new[] { typeof(int) });
        return getItem?.Invoke(array, new object[] { index });
    }
}
internal static class MakerPenGiftPreviewPatches
{
    private static bool _loggedMissingPointer;
    private static bool _loggedMissingRestrictionFlag;

    public static bool LaserPointerEnabled_Prefix(object __instance, bool ONGBFDACHHG)
    {
        try
        {
            if (__instance is null) return true;
            var t = __instance.GetType();
            if (ReadField(__instance, t, "pointerLineRenderer") is null)
            {
                if (!_loggedMissingPointer)
                {
                    _loggedMissingPointer = true;
                    DebugMod.Log.Msg("[makerpen-preview] skipped LaserPointerEnabled on MakerPenVisuals with no pointerLineRenderer");
                }
                return false;
            }

            if (ONGBFDACHHG && ReadField(__instance, t, "EANJBIDHAMN") is null)
            {
                if (!_loggedMissingRestrictionFlag)
                {
                    _loggedMissingRestrictionFlag = true;
                    DebugMod.Log.Msg("[makerpen-preview] skipped LaserPointerEnabled=true on MakerPenVisuals with no laser restriction flag");
                }
                return false;
            }
        }
        catch (Exception ex)
        {
            DebugMod.Log.Warning($"[makerpen-preview] guard failed, letting original run: {ex.Message}");
        }

        return true;
    }

    private static object? ReadField(object instance, Type type, string name)
    {
        var field = AccessTools.Field(type, name) ?? AccessTools.Field(type.BaseType, name);
        return field?.GetValue(instance);
    }
}
internal static class AuthPatches
{
    private static string? _latestAccessToken;
    private static int _tokenLogCount;

    public static void CaptureTokenArgs_Prefix(object? __0, object? __1, object? __2)
    {
        try
        {
            var accessToken = __0?.ToString();
            if (LooksLikeJwt(accessToken))
                _latestAccessToken = accessToken;

            if (_tokenLogCount++ < 3)
            {
                DiagnosticPatches.Write(
                    "[auth-trace] token setter " +
                    $"access={Presence(__0)} refresh={Presence(__1)} key={Presence(__2)}");
            }
        }
        catch (Exception ex)
        {
            DebugMod.Log.Warning($"[auth-trace] token capture failed: {ex.Message}");
        }
    }

    private static bool LooksLikeJwt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var dots = 0;
        foreach (var ch in value)
            if (ch == '.') dots++;
        return dots == 2;
    }

    private static string Presence(object? value) =>
        string.IsNullOrWhiteSpace(value?.ToString()) ? "missing" : "present";
}
internal static class HttpTracePatches
{
    private const int BodyPreviewBytes = 700;
    private static readonly Regex SensitivePairRegex = new(
        @"(?i)(password|client_secret|access_token|accessToken|refresh_token|refreshToken|authorization|token|key)=([^&\s]+)",
        RegexOptions.Compiled);
    private static readonly Regex BearerRegex = new(
        @"(?i)Bearer\s+[-._~+/=A-Za-z0-9]+",
        RegexOptions.Compiled);
    private static readonly Regex SensitiveHeaderRegex = new(
        @"(?im)^(Authorization|Cookie|Set-Cookie|X-RNSIG):\s*[^\r\n]+",
        RegexOptions.Compiled);
    private static readonly Regex SensitiveJsonStringRegex = new(
        @"(?i)""([^""]*(?:access_token|accessToken|refresh_token|refreshToken|authorization|token|secret|client_secret|key)[^""]*)""\s*:\s*""[^""]*""",
        RegexOptions.Compiled);
    private static readonly Dictionary<IntPtr, string> RecNetRequestInfoByPtr = new();
    private static int _requestId;
    private static int _failureLogCount;
    private static int _responseTupleLogCount;
    private static int _builderSendLogCount;
    private static int _callbackLogCount;
    private static int _uriUnreadDumpCount;

    public static void RecNetRequestSend_Prefix(object __instance, MethodBase __originalMethod)
    {
        TraceRequestObject(__instance, $"{__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}");
    }

    public static void RecNetRequestBuilderCtor_Prefix(object? __0, object? __1, object? __2)
    {
        var id = NextRequestId();
        DiagnosticPatches.Write(
            $"[http-trace] #{id} BNDIAONDFFF.ctor method={SanitizeInline(__0?.ToString() ?? "<null>")} " +
            $"service={SanitizeInline(__1?.ToString() ?? "<null>")} path={SanitizeInline(__2?.ToString() ?? "<null>")}");
    }

    public static void RecNetRequestBuilderCtor_Postfix(object __instance, object? __0, object? __1, object? __2)
    {
        try
        {
            var ptr = TryGetIl2CppPtr(__instance);
            if (ptr == IntPtr.Zero) return;

            var info =
                $"ptr=0x{ptr.ToInt64():X} method={SanitizeInline(__0?.ToString() ?? "<null>")} " +
                $"service={SanitizeInline(__1?.ToString() ?? "<null>")} path={SanitizeInline(__2?.ToString() ?? "<null>")}";
            lock (RecNetRequestInfoByPtr)
            {
                RecNetRequestInfoByPtr[ptr] = info;
                if (RecNetRequestInfoByPtr.Count > 256)
                    RecNetRequestInfoByPtr.Clear();
            }

            DiagnosticPatches.Write($"[recnet-request-map] {info}");
        }
        catch (Exception ex)
        {
            LogFailure("recnet-request-map", ex);
        }
    }

    public static void RecNetBuilderSend_Prefix(object __instance, MethodBase __originalMethod)
    {
        if (System.Threading.Interlocked.Increment(ref _builderSendLogCount) > 120) return;

        try
        {
            DiagnosticPatches.Write(
                $"[recnet-send] ENTER {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{LookupRecNetRequestInfo(__instance)} {DumpBndNativeFields(__instance)}");
        }
        catch (Exception ex)
        {
            LogFailure("recnet-send-enter", ex);
        }
    }

    public static void RecNetBuilderSend_Postfix(object __instance, MethodBase __originalMethod)
    {
        if (_builderSendLogCount > 120) return;

        try
        {
            DiagnosticPatches.Write(
                $"[recnet-send] EXIT {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{LookupRecNetRequestInfo(__instance)} {DumpBndNativeFields(__instance)}");
        }
        catch (Exception ex)
        {
            LogFailure("recnet-send-exit", ex);
        }
    }

    public static void RecNetResponseTuple_Prefix(object __instance, MethodBase __originalMethod)
    {
        if (System.Threading.Interlocked.Increment(ref _responseTupleLogCount) > 80) return;

        try
        {
            DiagnosticPatches.Write(
                $"[recnet-response-map] ENTER {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{LookupRecNetRequestInfo(__instance)} {DumpBndNativeFields(__instance)} {DumpObjectFields(__instance, 20, 600)}");
        }
        catch (Exception ex)
        {
            LogFailure("recnet-response-map-enter", ex);
        }
    }

    public static void RecNetResponseTuple_Postfix(object __instance, MethodBase __originalMethod)
    {
        if (_responseTupleLogCount > 80) return;

        try
        {
            DiagnosticPatches.Write(
                $"[recnet-response-map] EXIT {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{LookupRecNetRequestInfo(__instance)} {DumpBndNativeFields(__instance)} {DumpObjectFields(__instance, 20, 600)}");
        }
        catch (Exception ex)
        {
            LogFailure("recnet-response-map-exit", ex);
        }
    }

    public static Exception? RecNetResponseTuple_Finalizer(Exception? __exception, object __instance, MethodBase __originalMethod)
    {
        if (__exception is not null)
        {
            DiagnosticPatches.Write(
                $"[recnet-response-map] EXCEPTION {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{LookupRecNetRequestInfo(__instance)} {DumpBndNativeFields(__instance)} {RoomLoadDiagnostics.FormatException(__exception, __exception)} " +
                $"state={DumpObjectFields(__instance, 36, 1200)}");
        }
        return __exception;
    }

    public static void HeaderSet_Prefix(object __instance, object? __0, object? __1, MethodBase __originalMethod)
    {
        try
        {
            var name = __0?.ToString() ?? "<null>";
            var value = RedactHeaderValue(name, __1?.ToString());
            var uri = TryReadRequestUri(__instance)?.ToString() ?? "<uri unread>";
            DiagnosticPatches.Write(
                $"[http-header] {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} " +
                $"{SanitizeInline(uri)} {name}: {value}");
        }
        catch (Exception ex)
        {
            LogFailure("header", ex);
        }
    }

    public static void CallCallback_Prefix(object __instance, MethodBase __originalMethod)
    {
        try
        {
            DiagnosticPatches.Write(
                $"[http-callback] ENTER {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{DescribeRequestAndResponse(__instance)}");
        }
        catch (Exception ex)
        {
            LogFailure("callback-enter", ex);
        }
    }

    public static void CallCallback_Postfix(object __instance, MethodBase __originalMethod)
    {
        try
        {
            if (!ShouldLogCallback(__instance)) return;
            DiagnosticPatches.Write(
                $"[http-callback] EXIT {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                $"{DescribeRequestAndResponse(__instance)}");
        }
        catch (Exception ex)
        {
            LogFailure("callback-exit", ex);
        }
    }

    public static Exception? CallCallback_Finalizer(Exception? __exception, object __instance, MethodBase __originalMethod)
    {
        if (__exception is not null)
        {
            try
            {
                DiagnosticPatches.Write(
                    $"[http-callback] EXCEPTION {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name} " +
                    $"{DescribeRequestAndResponse(__instance)} " +
                    $"{RoomLoadDiagnostics.FormatException(__exception, __exception)} " +
                    $"state={DumpObjectFields(__instance, 32, 900)}");
            }
            catch (Exception ex)
            {
                LogFailure("callback-finalizer", ex);
            }
        }

        return __exception;
    }

    public static void TraceUrl(string? url, string source)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return;

        var id = NextRequestId();
        DiagnosticPatches.Write($"[http-trace] #{id} {source} url={SanitizeInline(url)}");
    }

    public static void TraceRequestObject(object? request, string source)
    {
        if (request is null) return;

        try
        {
            var id = NextRequestId();
            var uriValue = TryReadRequestUri(request);
            var uri = uriValue?.ToString() ?? "<uri unread>";
            var method = TryReadHttpMethod(request);
            var headers = TryReadHeaders(request);
            var body = TryReadBodyPreview(request);
            var fallback = uriValue is null && System.Threading.Interlocked.Increment(ref _uriUnreadDumpCount) <= 40
                ? $" requestFields={DumpObjectFields(request, 24, 260)}"
                : "";

            DiagnosticPatches.Write(
                $"[http-trace] #{id} {source} method={method} uri={SanitizeInline(uri)} " +
                $"headers={headers} body={body}{fallback}");
        }
        catch (Exception ex)
        {
            LogFailure(source, ex);
        }
    }

    private static int NextRequestId() => System.Threading.Interlocked.Increment(ref _requestId);

    private static Uri? TryReadRequestUri(object request)
    {
        if (TryInvokeUriMethod(request, "KPKNHFBHPHO", out var uri)) return uri;
        if (TryInvokeUriMethod(request, "IBLHNHHBNNE", out uri)) return uri;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = request.GetType();

        if (TryReadUriValue(type.GetProperty("Uri", flags)?.GetValue(request), out uri)) return uri;
        if (TryReadUriValue(type.GetProperty("CurrentUri", flags)?.GetValue(request), out uri)) return uri;
        if (TryReadUriValue(type.GetField("<Uri>k__BackingField", flags)?.GetValue(request), out uri)) return uri;

        foreach (var prop in type.GetProperties(flags))
        {
            if (prop.Name.IndexOf("Uri", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
            try
            {
                if (TryReadUriValue(prop.GetValue(request), out uri)) return uri;
            }
            catch { }
        }

        foreach (var field in type.GetFields(flags))
        {
            if (field.Name.IndexOf("Uri", StringComparison.OrdinalIgnoreCase) < 0) continue;
            try
            {
                if (TryReadUriValue(field.GetValue(request), out uri)) return uri;
            }
            catch { }
        }

        return null;
    }

    private static bool TryInvokeUriMethod(object request, string methodName, out Uri? uri)
    {
        uri = null;
        try
        {
            var method = AccessTools.Method(request.GetType(), methodName, Type.EmptyTypes);
            if (method is null) return false;
            return TryReadUriValue(method.Invoke(request, Array.Empty<object>()), out uri);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadUriValue(object? value, out Uri? uri)
    {
        uri = null;
        if (value is null) return false;
        if (value is Uri direct)
        {
            uri = direct;
            return true;
        }

        var text = value.ToString();
        if (Uri.TryCreate(text, UriKind.Absolute, out var parsed))
        {
            uri = parsed;
            return true;
        }

        return false;
    }

    private static string TryReadHttpMethod(object request)
    {
        foreach (var name in new[] { "MethodType", "Method", "LNJILMKOBIP" })
        {
            if (TryReadMemberValue(request, name, out var value))
                return SanitizeInline(value?.ToString() ?? "<null>");
        }

        return "<method unread>";
    }

    private static string TryReadHeaders(object request)
    {
        try
        {
            var dump = AccessTools.Method(request.GetType(), "DumpHeaders", Type.EmptyTypes);
            if (dump?.Invoke(request, Array.Empty<object>()) is object rawDump)
                return SanitizeInline(rawDump.ToString() ?? "<empty>");
        }
        catch { }

        var known = new List<string>();
        foreach (var name in new[]
        {
            "Authorization", "Cookie", "X-RNSIG", "Accept-Language",
            "Content-Type", "User-Agent"
        })
        {
            var value = TryReadHeaderValue(request, name);
            if (!string.IsNullOrWhiteSpace(value))
                known.Add($"{name}:{RedactHeaderValue(name, value)}");
        }

        return known.Count == 0 ? "<headers unread>" : string.Join("; ", known);
    }

    private static string? TryReadHeaderValue(object request, string name)
    {
        foreach (var methodName in new[] { "GetFirstHeaderValue", "FKHEHIMHHBP" })
        {
            try
            {
                var method = AccessTools.Method(request.GetType(), methodName, new[] { typeof(string) });
                var value = method?.Invoke(request, new object[] { name })?.ToString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            catch { }
        }

        return null;
    }

    private static string TryReadBodyPreview(object request)
    {
        foreach (var methodName in new[] { "BGMFJOKNJAJ", "GetEntityBody" })
        {
            try
            {
                var method = AccessTools.Method(request.GetType(), methodName, Type.EmptyTypes);
                if (method is null) continue;
                var body = method.Invoke(request, Array.Empty<object>());
                var bytes = TryCopyBytes(body, BodyPreviewBytes + 1);
                if (bytes is null) continue;
                if (bytes.Length == 0) return "<empty>";

                var shown = Math.Min(bytes.Length, BodyPreviewBytes);
                var text = Encoding.UTF8.GetString(bytes, 0, shown);
                var suffix = bytes.Length > BodyPreviewBytes ? "...<truncated>" : "";
                return $"{bytes.Length} bytes \"{SanitizeInline(text)}{suffix}\"";
            }
            catch { }
        }

        return "<body unread>";
    }

    private static bool ShouldLogCallback(object request)
    {
        var response = TryReadResponseObject(request);
        var status = response is null ? null : TryReadStatusCode(response);
        if (status >= 400) return true;
        return System.Threading.Interlocked.Increment(ref _callbackLogCount) <= 80;
    }

    private static string DescribeRequestAndResponse(object request)
    {
        var uri = TryReadRequestUri(request)?.ToString() ?? "<uri unread>";
        var method = TryReadHttpMethod(request);
        var response = TryReadResponseObject(request);
        var responseText = response is null ? "<response unread>" : DescribeResponseObject(response);
        var requestState = DescribeKnownMembers(request, "State", "Exception", "Error", "IsCancellationRequested");
        return $"method={method} uri={SanitizeInline(uri)} request={requestState} response={responseText}";
    }

    private static object? TryReadResponseObject(object request)
    {
        foreach (var name in new[] { "Response", "response", "_response", "<Response>k__BackingField" })
        {
            if (TryReadMemberValue(request, name, out var value) && value is not null)
                return value;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var prop in request.GetType().GetProperties(flags))
        {
            if (prop.Name.IndexOf("Response", StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!prop.CanRead || prop.GetIndexParameters().Length != 0) continue;
            try
            {
                var value = prop.GetValue(request);
                if (value is not null) return value;
            }
            catch { }
        }

        foreach (var field in request.GetType().GetFields(flags))
        {
            if (field.Name.IndexOf("Response", StringComparison.OrdinalIgnoreCase) < 0) continue;
            try
            {
                var value = field.GetValue(request);
                if (value is not null) return value;
            }
            catch { }
        }

        return null;
    }

    private static int? TryReadStatusCode(object response)
    {
        if (TryReadMemberValue(response, "StatusCode", out var value))
        {
            try { return Convert.ToInt32(value); }
            catch { }
        }

        return null;
    }

    private static string DescribeResponseObject(object response)
    {
        var known = DescribeKnownMembers(response,
            "StatusCode", "Message", "IsSuccess", "DataAsText", "Data", "Exception", "Error");
        return $"{response.GetType().FullName} {{{known}}}";
    }

    private static string DescribeKnownMembers(object obj, params string[] names)
    {
        var parts = new List<string>();
        foreach (var name in names)
        {
            if (!TryReadMemberValue(obj, name, out var value)) continue;
            parts.Add($"{name}={FormatValue(value, 700)}");
        }
        return parts.Count == 0 ? "<no known members>" : string.Join("; ", parts);
    }

    private static string FormatValue(object? value, int limit)
    {
        if (value is null) return "null";
        if (value is byte[] bytes)
            return $"byte[{bytes.Length}]";
        if (value is IEnumerable enumerable && value is not string)
            return FormatEnumerable(enumerable, value.GetType(), limit);
        var text = value is Exception ex
            ? $"{ex.GetType().Name}: {ex.Message}"
            : value.ToString() ?? "<null>";
        text = SanitizeInline(text);
        return text.Length <= limit ? text : text[..limit] + "...<truncated>";
    }

    private static string FormatEnumerable(IEnumerable enumerable, Type type, int limit)
    {
        try
        {
            var parts = new List<string>();
            var countText = TryReadEnumerableCount(enumerable, out var count) ? count.ToString() : "?";
            var shown = 0;

            foreach (var item in enumerable)
            {
                if (shown++ >= 6)
                {
                    parts.Add("...");
                    break;
                }

                parts.Add(FormatValue(item, 180));
            }

            var rendered = $"{type.FullName} count={countText} [{string.Join(", ", parts)}]";
            return rendered.Length <= limit ? rendered : rendered[..limit] + "...<truncated>";
        }
        catch (Exception ex)
        {
            return $"{type.FullName} <enumeration failed {ex.GetType().Name}: {SanitizeInline(ex.Message)}>";
        }
    }

    private static bool TryReadEnumerableCount(IEnumerable enumerable, out int count)
    {
        count = 0;
        if (enumerable is ICollection collection)
        {
            count = collection.Count;
            return true;
        }

        try
        {
            var prop = enumerable.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop?.GetValue(enumerable) is int value)
            {
                count = value;
                return true;
            }
        }
        catch { }

        return false;
    }

    private static byte[]? TryCopyBytes(object? value, int limit)
    {
        if (value is null) return null;
        if (value is byte[] direct)
            return CopyLimited(direct, limit);

        if (value is IEnumerable enumerable)
        {
            var bytes = new List<byte>();
            foreach (var item in enumerable)
            {
                if (item is null) continue;
                try { bytes.Add(Convert.ToByte(item)); }
                catch { return null; }
                if (bytes.Count >= limit) break;
            }
            return bytes.ToArray();
        }

        return null;
    }

    private static byte[] CopyLimited(byte[] source, int limit)
    {
        var count = Math.Min(source.Length, limit);
        var copy = new byte[count];
        Array.Copy(source, copy, count);
        return copy;
    }

    private static bool TryReadMemberValue(object request, string name, out object? value)
    {
        value = null;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var type = request.GetType();

        try
        {
            var prop = type.GetProperty(name, flags);
            if (prop is not null && prop.CanRead && prop.GetIndexParameters().Length == 0)
            {
                value = prop.GetValue(request);
                return true;
            }
        }
        catch { }

        try
        {
            var field = type.GetField(name, flags);
            if (field is not null)
            {
                value = field.GetValue(request);
                return true;
            }
        }
        catch { }

        try
        {
            var method = AccessTools.Method(type, name, Type.EmptyTypes);
            if (method is not null)
            {
                value = method.Invoke(request, Array.Empty<object>());
                return true;
            }
        }
        catch { }

        return false;
    }

    private static string DumpObjectFields(object? instance, int maxFields, int valueLimit)
    {
        if (instance is null) return "<null>";

        try
        {
            var type = instance.GetType();
            var parts = new List<string>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var field in type.GetFields(flags))
            {
                if (parts.Count >= maxFields)
                {
                    parts.Add("...");
                    break;
                }

                object? value;
                try { value = field.GetValue(instance); }
                catch (Exception ex)
                {
                    parts.Add($"{field.Name}=<read failed {ex.GetType().Name}>");
                    continue;
                }

                parts.Add($"{field.Name}={FormatValue(value, valueLimit)}");
            }

            return $"{type.FullName} {{{string.Join("; ", parts)}}}";
        }
        catch (Exception ex)
        {
            return $"<field dump failed {ex.GetType().Name}: {SanitizeInline(ex.Message)}>";
        }
    }

    private static string LookupRecNetRequestInfo(object instance)
    {
        var ptr = TryGetIl2CppPtr(instance);
        if (ptr == IntPtr.Zero) return "ptr=<unread>";

        lock (RecNetRequestInfoByPtr)
        {
            return RecNetRequestInfoByPtr.TryGetValue(ptr, out var info)
                ? info
                : $"ptr=0x{ptr.ToInt64():X} request=<unmapped>";
        }
    }

    private static string DumpBndNativeFields(object? instance)
    {
        var ptr = TryGetIl2CppPtr(instance);
        if (ptr == IntPtr.Zero) return "native=<unread>";

        try
        {
            var method = Marshal.ReadByte(ptr, 16);
            var service = Marshal.ReadInt32(ptr, 20);
            var pathPtr = Marshal.ReadIntPtr(ptr, 24);
            var fieldsPtr = Marshal.ReadIntPtr(ptr, 32);
            var filesPtr = Marshal.ReadIntPtr(ptr, 40);
            var objectFieldsPtr = Marshal.ReadIntPtr(ptr, 48);
            var bodyKind = Marshal.ReadInt32(ptr, 56);
            var callbackPtr = Marshal.ReadIntPtr(ptr, 64);
            var path = ReadIl2CppString(pathPtr);

            return
                "native={" +
                $"method={method}; service={service}; path={SanitizeInline(path)}; " +
                $"pathPtr=0x{pathPtr.ToInt64():X}; fieldsPtr=0x{fieldsPtr.ToInt64():X}; " +
                $"filesPtr=0x{filesPtr.ToInt64():X}; objectFieldsPtr=0x{objectFieldsPtr.ToInt64():X}; " +
                $"bodyKind={bodyKind}; callbackPtr=0x{callbackPtr.ToInt64():X}" +
                "}";
        }
        catch (Exception ex)
        {
            return $"native=<dump failed {ex.GetType().Name}: {SanitizeInline(ex.Message)}>";
        }
    }

    private static string ReadIl2CppString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return "<null>";

        try
        {
            return IL2CPP.Il2CppStringToManaged(ptr) ?? "<null>";
        }
        catch (Exception ex)
        {
            return $"<string read failed 0x{ptr.ToInt64():X} {ex.GetType().Name}>";
        }
    }

    private static IntPtr TryGetIl2CppPtr(object? instance)
    {
        try
        {
            return instance is Il2CppObjectBase il2CppObject
                ? IL2CPP.Il2CppObjectBaseToPtrNotNull(il2CppObject)
                : IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static string RedactHeaderValue(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "<empty>";
        if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("X-RNSIG", StringComparison.OrdinalIgnoreCase))
        {
            return "<redacted>";
        }

        return SanitizeInline(value);
    }

    private static string SanitizeInline(string value)
    {
        var redacted = SensitivePairRegex.Replace(value, "$1=<redacted>");
        redacted = BearerRegex.Replace(redacted, "Bearer <redacted>");
        redacted = SensitiveHeaderRegex.Replace(redacted, "$1: <redacted>");
        redacted = SensitiveJsonStringRegex.Replace(redacted, "\"$1\":\"<redacted>\"");
        redacted = redacted.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        return redacted.Length <= 1200 ? redacted : redacted[..1200] + "...<truncated>";
    }

    private static void LogFailure(string source, Exception ex)
    {
        if (_failureLogCount++ < 10)
            DebugMod.Log.Warning($"[http-trace] {source} failed: {ex.GetType().Name}: {ex.Message}");
    }
}
internal static class SavePatches
{
    // Last DataBlobName parsed off a SubscriptionUpdateRoom push for
    // our currently-loaded room. Captured BEFORE the watch's caching
    // gate (Rooms.OnSubscriptionUpdateRoom early-outs on a repeat
    // (roomId, subRoomId) tuple — which is every post-save push for a
    // dorm). SetRoomDataBlobName_Prefix reads this to hijack the
    // would-be no-op setter call into a real transition.
    private static volatile string? s_lastPushedBlobName;
    private static long s_lastPushedRoomId;
    private static long s_lastPushedSubRoomId;

    // Hook on Notifications.OnNotificationReceived(string json). Fires for
    // every SignalR message before any handler dispatch. Parses the JSON
    // and, when the Id is SubscriptionUpdateRoom, stashes the new blob.
    public static void OnNotificationReceived_Prefix(string message)
    {
        try
        {
            if (string.IsNullOrEmpty(message)) return;
            // Fast reject: only parse JSON if it could plausibly be a
            // SubscriptionUpdateRoom payload. Saves cycles on the (very
            // frequent) RoomInstanceUpdate / PresenceUpdate / chat pushes.
            if (message.IndexOf("\"SubscriptionUpdateRoom\"", StringComparison.Ordinal) < 0)
                return;
            using var doc = System.Text.Json.JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return;
            if (!root.TryGetProperty("Id", out var idEl) ||
                idEl.ValueKind != System.Text.Json.JsonValueKind.String ||
                !string.Equals(idEl.GetString(), "SubscriptionUpdateRoom", StringComparison.Ordinal))
                return;
            if (!root.TryGetProperty("Msg", out var msg) ||
                msg.ValueKind != System.Text.Json.JsonValueKind.Object) return;
            if (!msg.TryGetProperty("Room", out var room) ||
                room.ValueKind != System.Text.Json.JsonValueKind.Object) return;
            long roomId = 0;
            if (room.TryGetProperty("RoomId", out var ridEl) &&
                ridEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                roomId = ridEl.GetInt64();
            if (!msg.TryGetProperty("Scenes", out var scenes) ||
                scenes.ValueKind != System.Text.Json.JsonValueKind.Array ||
                scenes.GetArrayLength() == 0) return;
            var first = scenes[0];
            long subRoomId = 0;
            if (first.TryGetProperty("RoomSceneId", out var sidEl) &&
                sidEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                subRoomId = sidEl.GetInt64();
            if (!first.TryGetProperty("DataBlobName", out var blobEl) ||
                blobEl.ValueKind != System.Text.Json.JsonValueKind.String) return;
            var blob = blobEl.GetString();
            if (string.IsNullOrEmpty(blob)) return;
            s_lastPushedRoomId    = roomId;
            s_lastPushedSubRoomId = subRoomId;
            s_lastPushedBlobName  = blob;
            DebugMod.Log.Msg($"[save-reload-fix] captured push via OnNotificationReceived: roomId={roomId} subRoomId={subRoomId} blob={blob}");
        }
        catch (Exception ex)
        {
            DebugMod.Log.Warning($"[save-reload-fix] OnNotificationReceived prefix failed: {ex.Message}");
        }
    }

    public static void OnSubscriptionUpdateRoom_Prefix(object message)
    {
        try
        {
            if (message is null) return;
            var dict = message;

            // The watch parses this dict as Dictionary<string, object>
            // via Util.GetKey. We need three keys: RoomId, SubRoomId,
            // and Scenes[0].DataBlobName. The Il2Cpp dict supports
            // get_Item(key) — call it via reflection.
            var getItem = dict.GetType().GetMethod("get_Item", new[] { typeof(string) });
            if (getItem is null) return;

            // The push wire shape from server (BuildRoomDetails) is
            // {"Room":{...}, "Scenes":[{...}], ...}. The Room id lives
            // on the Room sub-object; the scene's blob on Scenes[0].
            object? roomObj   = TryGet(dict, getItem, "Room");
            object? scenesObj = TryGet(dict, getItem, "Scenes");
            if (roomObj is null || scenesObj is null) return;

            long roomId    = 0;
            long subRoomId = 0;
            try
            {
                var roomItem = roomObj.GetType().GetMethod("get_Item", new[] { typeof(string) });
                if (roomItem is not null)
                {
                    var rid = roomItem.Invoke(roomObj, new object[] { "RoomId" });
                    if (rid is not null) roomId = Convert.ToInt64(rid.ToString());
                }
            }
            catch { /* roomId stays 0 — we'll log it and bail later */ }

            // Scenes is an IL2CPP List<RoomScene>. Take the first
            // (current dorm has one scene; multi-scene rooms target
            // Scenes[currentSubRoomId] specifically, but the post-save
            // push always carries the scene that was just saved).
            object? firstScene = null;
            try
            {
                var scenesIndexer = scenesObj.GetType().GetMethod("get_Item", new[] { typeof(int) });
                firstScene = scenesIndexer?.Invoke(scenesObj, new object[] { 0 });
            }
            catch { /* leave firstScene null */ }
            if (firstScene is null) return;

            string? newBlob = null;
            try
            {
                var sceneItem = firstScene.GetType().GetMethod("get_Item", new[] { typeof(string) });
                if (sceneItem is not null)
                {
                    var blob = sceneItem.Invoke(firstScene, new object[] { "DataBlobName" });
                    newBlob = blob?.ToString();
                    var sid = sceneItem.Invoke(firstScene, new object[] { "RoomSceneId" });
                    if (sid is not null) subRoomId = Convert.ToInt64(sid.ToString());
                }
            }
            catch { /* leave newBlob null */ }

            if (string.IsNullOrEmpty(newBlob)) return;

            s_lastPushedRoomId    = roomId;
            s_lastPushedSubRoomId = subRoomId;
            s_lastPushedBlobName  = newBlob;
            DebugMod.Log.Msg($"[save-reload-fix] captured SubscriptionUpdateRoom push: roomId={roomId} subRoomId={subRoomId} blob={newBlob}");
        }
        catch (Exception ex)
        {
            DebugMod.Log.Warning($"[save-reload-fix] OnSubscriptionUpdateRoom prefix failed: {ex.Message}");
        }
    }

    private static object? TryGet(object dict, MethodInfo getItem, string key)
    {
        try { return getItem.Invoke(dict, new object[] { key }); }
        catch { return null; }
    }

    // RoomPersistenceManager.set_RoomDataBlobName runs the change-event
    // path only when the new value differs from the cached field. The
    // 2020 watch's post-save propagation reads from RoomInstance.DataBlob
    // (set by /goto, never updated by SubscriptionUpdateRoom), so on a
    // save it calls the setter with the OLD blob — a no-op that skips
    // OnRoomDataBlobNameChanged. The cached PersistedRoomData stays
    // pointed at the boot-time bytes, and PostSaveReloading deserializes
    // those over the live world — wiping any MakerPen object the user
    // just placed.
    //
    // Live trace evidence (Latest.log, dorm save at 17:14): the watch's
    // own set_RoomDataBlobName fires with old="v10" new="v10" ~900ms
    // after b__4 returns True, even though the SubscriptionUpdateRoom
    // push already carried Scenes[0].DataBlobName="v11".
    //
    // Strategy: intercept the setter. If the incoming value matches the
    // current value (no-op) AND Rooms.LocalRoomScene.DataBlobName has a
    // newer version (the freshly-pushed one), substitute that into the
    // value parameter so the real setter sees a transition and fires
    // OnRoomDataBlobNameChanged → DownloadRoomDataBlobAsync(newBlob).
    // Falls through silently when no override is needed.
    public static void SetRoomDataBlobName_Prefix(object __instance, ref string value)
    {
        string currentForLog = "<unread>";
        string sceneForLog   = "<unread>";
        string scenes0ForLog = "<unread>";
        try
        {
            if (__instance is null)
            {
                DebugMod.Log.Msg("[save-reload-fix] prefix: __instance null — skipping");
                return;
            }

            var rpmType = __instance.GetType();
            var blobNameProp = rpmType.GetProperty("RoomDataBlobName",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (blobNameProp is null)
            {
                DebugMod.Log.Msg($"[save-reload-fix] prefix: RoomDataBlobName property not found on {rpmType.FullName}");
                return;
            }
            currentForLog = (blobNameProp.GetValue(__instance) as string) ?? "<null>";

            var roomsType = FindGameType("RecNet.Rooms");
            if (roomsType is null)
            {
                DebugMod.Log.Msg("[save-reload-fix] prefix: RecNet.Rooms type not found");
                return;
            }

            var localScene = roomsType.GetProperty("LocalRoomScene",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (localScene is not null)
                sceneForLog = (localScene.GetType().GetProperty("DataBlobName")?.GetValue(localScene) as string) ?? "<null>";

            var localDetails = roomsType.GetProperty("LocalRoomDetails",
                BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (localDetails is not null)
            {
                var scenes = localDetails.GetType().GetProperty("Scenes")?.GetValue(localDetails);
                if (scenes is not null)
                {
                    var indexer = scenes.GetType().GetMethod("get_Item", new[] { typeof(int) });
                    var first = indexer?.Invoke(scenes, new object[] { 0 });
                    if (first is not null)
                        scenes0ForLog = (first.GetType().GetProperty("DataBlobName")?.GetValue(first) as string) ?? "<null>";
                }
            }

            // Unconditional trace of EVERYTHING we read so we can see in
            // the log exactly why we did or didn't override.
            DebugMod.Log.Msg($"[save-reload-fix] prefix fired: value=\"{value}\" current=\"{currentForLog}\" " +
                        $"LocalRoomScene.DataBlobName=\"{sceneForLog}\" Scenes[0].DataBlobName=\"{scenes0ForLog}\" " +
                        $"lastPushedBlob=\"{s_lastPushedBlobName ?? "<unset>"}\"");

            // Pick a candidate "newer" blob name. Priority order:
            //   1. Last-pushed-from-SubscriptionUpdateRoom (captured by
            //      OnSubscriptionUpdateRoom_Prefix BEFORE the watch's
            //      cache gate drops the push).
            //   2. LocalRoomScene.DataBlobName.
            //   3. Scenes[0].DataBlobName.
            string? pushedBlob = s_lastPushedBlobName;
            if (string.IsNullOrEmpty(pushedBlob) || string.Equals(pushedBlob, currentForLog, StringComparison.Ordinal))
                pushedBlob = sceneForLog is "<unread>" or "<null>" ? null : sceneForLog;
            if (string.IsNullOrEmpty(pushedBlob) || string.Equals(pushedBlob, currentForLog, StringComparison.Ordinal))
                pushedBlob = scenes0ForLog is "<unread>" or "<null>" ? null : scenes0ForLog;

            if (string.IsNullOrEmpty(pushedBlob)) return;
            if (string.Equals(pushedBlob, value, StringComparison.Ordinal)) return;
            if (string.Equals(pushedBlob, currentForLog, StringComparison.Ordinal)) return;

            DebugMod.Log.Msg($"[save-reload-fix] hijacking set_RoomDataBlobName({value}); substituting newer blob {pushedBlob}");
            value = pushedBlob;
        }
        catch (Exception ex)
        {
            DebugMod.Log.Warning($"[save-reload-fix] set_RoomDataBlobName prefix failed: {ex.Message} " +
                            $"(value=\"{value}\" current=\"{currentForLog}\" scene=\"{sceneForLog}\" scenes0=\"{scenes0ForLog}\")");
        }
    }

    private static Type? FindGameType(string name) => DebugMod.ResolveType(name);
}
internal static class QuitTracePatches
{
    public static bool ApplicationQuit_Prefix()
    {
        Write("[quit-trace] UnityEngine.Application.Quit()");
        WriteStack();
        return true;
    }

    public static bool ApplicationQuitInt_Prefix(int exitCode)
    {
        Write($"[quit-trace] UnityEngine.Application.Quit({exitCode})");
        WriteStack();
        if (exitCode == 1550502249 || exitCode == 533223478)
        {
            Write($"[quit-blocked] UnityEngine.Application.Quit fatal client exit {exitCode}");
            return false;
        }
        return true;
    }

    public static bool SessionManagerTryApplicationQuit_Prefix(MethodBase __originalMethod)
    {
        Write($"[quit-trace] {FormatOriginal(__originalMethod)}");
        WriteStack();
        return true;
    }

    public static bool SessionManagerTryApplicationQuitInt_Prefix(int DKOGJICFAEP, MethodBase __originalMethod)
    {
        Write($"[quit-trace] {FormatOriginal(__originalMethod)}({DKOGJICFAEP})");
        WriteStack();
        return true;
    }

    public static bool SessionManagerFatalApplicationQuit_Prefix(int DKOGJICFAEP, string EPAIMCEIMPA, MethodBase __originalMethod)
    {
        Write($"[quit-trace] {FormatOriginal(__originalMethod)}({DKOGJICFAEP}, {EPAIMCEIMPA})");
        WriteStack();
        return true;
    }

    public static void ApplicationWantsToQuit_Prefix(MethodBase __originalMethod)
    {
        Write($"[quit-trace] {FormatOriginal(__originalMethod)} wants-to-quit handler entered");
        WriteStack();
    }

    public static void CreateExitTask_Prefix(MethodBase __originalMethod)
    {
        Write($"[quit-trace] {FormatOriginal(__originalMethod)} creating exit task");
        WriteStack();
    }

    private static string FormatOriginal(MethodBase method)
    {
        return $"{method.DeclaringType?.FullName}.{method.Name}";
    }

    private static void Write(string message)
    {
        try { DebugMod.Log.Msg(message); } catch { }
    }

    private static void WriteStack()
    {
        try
        {
            Write("[quit-trace-stack] " + new StackTrace(skipFrames: 2, fNeedFileInfo: false));
        }
        catch (Exception ex)
        {
            Write("[quit-trace-stack] failed: " + ex.Message);
        }
    }
}
internal static class DiagnosticPatches
{
    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [diagnostic] {message}";
        try { DebugMod.Log.Msg(line); } catch { }
        try
        {
            if (!string.IsNullOrEmpty(DebugMod.DiagnosticsLogPath))
                File.AppendAllText(DebugMod.DiagnosticsLogPath, line + Environment.NewLine);
        }
        catch { }
    }

    public static bool ApplicationQuit_Prefix()
    {
        Write("[quit] UnityEngine.Application.Quit()");
        WriteStack();
        return true;
    }

    public static bool ApplicationQuitInt_Prefix(int exitCode)
    {
        Write($"[quit] UnityEngine.Application.Quit({exitCode})");
        WriteStack();
        return true;
    }

    public static void AnalyticsCheat_Prefix(MethodBase __originalMethod)
    {
        Write($"[cheat-analytics] {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}");
        WriteStack();
    }

    public static void NamedMethod_Prefix(MethodBase __originalMethod)
    {
        Write($"[call] {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}");
        WriteStack();
    }

    public static bool SessionManagerTryApplicationQuit_Prefix(MethodBase __originalMethod)
    {
        Write($"[quit-trace] {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}");
        WriteStack();
        return true;
    }

    public static bool SessionManagerTryApplicationQuitInt_Prefix(int DKOGJICFAEP, MethodBase __originalMethod)
    {
        Write($"[quit-trace] {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}({DKOGJICFAEP})");
        WriteStack();
        return true;
    }

    public static bool SessionManagerFatalApplicationQuit_Prefix(int DKOGJICFAEP, string EPAIMCEIMPA, MethodBase __originalMethod)
    {
        Write($"[quit-trace] {__originalMethod.DeclaringType?.FullName}.{__originalMethod.Name}({DKOGJICFAEP}, {EPAIMCEIMPA})");
        WriteStack();
        return true;
    }

    private static void WriteStack()
    {
        try
        {
            Write("[stack] " + new StackTrace(skipFrames: 2, fNeedFileInfo: false));
        }
        catch (Exception ex)
        {
            Write("[stack] failed: " + ex.Message);
        }
    }
}
internal static class ChatPatches
{
    private static bool s_logged;

    // Force RecNet.AccountExtensions.CanLocalPlayerChat to return true.
    // PlayerEmotes.SendChatEmoteCoroutine d__53 offset 040 calls this
    // before raising the in-room Photon chat RPC; if it returns false
    // the coroutine bails silently and the sender doesn't even see
    // their own message echo.
    public static void CanLocalPlayerChat_Postfix(ref bool __result)
    {
        if (!__result && !s_logged)
        {
            s_logged = true;
            DebugMod.Log.Msg("[chat-fix] CanLocalPlayerChat returned false; forcing true to unblock room chat");
        }
        __result = true;
    }

    // Skip the original. Returning false from a Harmony prefix stops
    // the underlying call.
    public static bool RemoveInvalidCharactersFromMessage_Prefix()
    {
        DebugMod.Log.Msg("[chat-trace] RemoveInvalidCharactersFromMessage skipped");
        return false;
    }

    public static void SendChatEmote_Prefix(string message)
    {
        DebugMod.Log.Msg($"[chat-trace] SendChatEmote msg=\"{message}\"");
    }

    public static void ProcessNewChatMessageReceived_Prefix(string message, bool playAudioForLocalPlayer)
    {
        DebugMod.Log.Msg($"[chat-trace] ProcessNewChatMessageReceived msg=\"{message}\" audio={playAudioForLocalPlayer}");
    }

    public static void RpcChatEmote_Prefix(string message)
    {
        DebugMod.Log.Msg($"[chat-trace] RpcChatEmote (RPC RECEIVED) msg=\"{message}\"");
    }
}
internal static class RegistrationPatches
{
    private static bool s_loggedFullyReg;

    // RRUI.Data.NUX.RegistrationModel.IsFullyRegistered() — postfix reads
    // (does NOT modify) the return value. False ⇒ the dorm raises the
    // "Change Username?" prompt; true ⇒ no prompt. Logged once so the file
    // stays readable (it's polled from several call sites every boot).
    public static void IsFullyRegistered_Postfix(bool __result)
    {
        if (!s_loggedFullyReg)
        {
            s_loggedFullyReg = true;
            DiagnosticPatches.Write($"[reg-trace] RegistrationModel.IsFullyRegistered() = {__result}  ({(__result ? "no prompt — fully registered" : "NOT registered → dorm will raise the Change Username? prompt")})");
        }
    }

    // DormroomSceneManager.<PromptForRegistration>b__6_* lambdas. b__6_1 is
    // the dialog-response handler (a dialog was shown). Names reach the
    // prefix via __originalMethod so we can tell which branch ran.
    public static void PromptLambda_Prefix(MethodBase __originalMethod)
    {
        DiagnosticPatches.Write($"[reg-trace] DormroomSceneManager prompt lambda fired: {__originalMethod.Name}");
    }

    // ConfirmUIDialog button handlers. If one of these fires when the user
    // taps a dialog button but the modal stays open, the break is in the
    // resolve/hide completer (UIDialog`1.Respond's close path) — not input
    // routing. "Okay" on the screenshot is Button_Affirmative.
    public static bool ButtonAffirmative_Prefix(object __instance)
    {
        DiagnosticPatches.Write("[reg-trace] >>> ConfirmUIDialog.Button_Affirmative pressed (this is 'Okay')");

        if (!IsRegistrationWarningDialog(__instance))
        {
            return true;
        }

        if (TryResolveAffirmativeWithoutClose(__instance))
        {
            DiagnosticPatches.Write("[registration-fix] resolved first-dorm registration warning without the broken close-before-resolve path");
            return false;
        }

        DiagnosticPatches.Write("[registration-fix] registration warning matched, but reflection resolve failed; falling back to original button handler");
        return true;
    }

    public static void ButtonNegative_Prefix() =>
        DiagnosticPatches.Write("[reg-trace] >>> ConfirmUIDialog.Button_Negative pressed");
    public static void ButtonNotNow_Prefix() =>
        DiagnosticPatches.Write("[reg-trace] >>> ConfirmUIDialog.Button_NotNow pressed");
    public static void ButtonCancel_Prefix() =>
        DiagnosticPatches.Write("[reg-trace] >>> ConfirmUIDialog.Button_Cancel pressed");

    private static bool IsRegistrationWarningDialog(object instance)
    {
        var title = ReadTextField(instance, "titleText");
        var body = ReadTextField(instance, "bodyText");

        return Contains(title, "Change Username")
            && Contains(body, "permanently lost")
            && Contains(body, "password");
    }

    private static bool TryResolveAffirmativeWithoutClose(object instance)
    {
        try
        {
            var respond = AccessTools.Method(instance.GetType(), "Respond");
            if (respond is null)
            {
                respond = AccessTools.Method(instance.GetType().BaseType, "Respond");
            }

            if (respond is null)
            {
                return false;
            }

            var parameters = respond.GetParameters();
            if (parameters.Length != 2)
            {
                return false;
            }

            var response = CreateAffirmativeResponse(parameters[0].ParameterType);
            respond.Invoke(instance, new[] { response, (object)false });
            return true;
        }
        catch (Exception ex)
        {
            DiagnosticPatches.Write($"[registration-fix] resolve failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static object CreateAffirmativeResponse(Type responseType)
    {
        if (responseType.IsEnum)
        {
            return Enum.ToObject(responseType, 1);
        }

        return Convert.ChangeType(1, responseType);
    }

    private static string? ReadTextField(object instance, string fieldName)
    {
        try
        {
            var field = AccessTools.Field(instance.GetType(), fieldName);
            var textObject = field?.GetValue(instance);
            if (textObject is null)
            {
                return null;
            }

            var property = AccessTools.Property(textObject.GetType(), "text");
            if (property?.GetValue(textObject) is string text)
            {
                return text;
            }

            var getter = AccessTools.Method(textObject.GetType(), "get_text");
            return getter?.Invoke(textObject, Array.Empty<object>()) as string;
        }
        catch (Exception ex)
        {
            DiagnosticPatches.Write($"[registration-fix] could not read {fieldName}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static bool Contains(string? value, string fragment)
    {
        return value?.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
internal static class JoinPatches
{
    // AGUI.StackedUI.HomeScreenFlow.Button_DormRoom() — the watch's
    // "Dorm Room" button. If this never fires, the button's UnityEvent
    // binding is the problem, not the join chain.
    public static void ButtonDormRoom_Prefix()
    {
        DiagnosticPatches.Write("[join-trace] >>> watch DORM ROOM button pressed (HomeScreenFlow.Button_DormRoom)");
    }

    // Any SessionManager.RunJoinRoom overload. We DON'T read argument
    // values here: Harmony's `object[] __args` forces a trampoline that
    // boxes the obfuscated IL2CPP reference-type params (KMKPEOGJDFK,
    // RoomInstance, …), and MonoMod fatal-CLRs (0x80131506) compiling it
    // when the boot-time dorm auto-join first calls this method. Logging
    // the overload's parameter *types* (pure reflection metadata, no value
    // boxing) is enough to tell which overload fired — the entry overload
    // (String,…) is the "DormRoom" press; the others are inner legs.
    public static void RunJoinRoom_Prefix(MethodBase __originalMethod)
    {
        DiagnosticPatches.Write($"[join-trace] SessionManager.RunJoinRoom  [{Sig(__originalMethod)}]");
    }

    // OJMCBOKJFOF.NHBPIIGDAJP(name|id) — the get-room-by-name / by-id
    // resolver that feeds the join. __0 is the single first param: a room
    // NAME string (String overload) or room ID (Int64 overload). Reading
    // it via `object __0` is safe — it's a string/long, not one of the
    // obfuscated ref-types that crashed `object[] __args`. This is the
    // value we need to compare boot (works) vs button (fails).
    public static void Goto_Prefix(MethodBase __originalMethod, object __0)
    {
        var arg = __0?.ToString() ?? "<null>";
        DiagnosticPatches.Write($"[join-trace] /goto resolve {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}(\"{arg}\")  [{Sig(__originalMethod)}]");
    }

    // RunJoinRoom promise-chain callbacks (SessionManager <>c lambdas).
    // b__165_1 is the reject handler that raises the "An error occured /
    // contact recroom.happyfox.com" toast — so if THIS name appears, the
    // join failed; the success legs (other b__*) firing means it advanced.
    // (Same __args/0x80131506 constraint as above — name only.)
    public static void JoinCallback_Prefix(MethodBase __originalMethod)
    {
        DiagnosticPatches.Write($"[join-trace] RunJoinRoom callback {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}");
    }

    // Dedicated capture for the reject handler b__165_1(string error) —
    // the answer to "why did the join fail". Single string param, so
    // `object __0` is safe (same as OnCustomAuthenticationFailed). This
    // is the string that becomes the happyfox toast.
    public static void JoinError_Prefix(object __0)
    {
        var err = __0?.ToString() ?? "<null>";
        DiagnosticPatches.Write($"[join-trace] !!! RunJoinRoom REJECTED — error=\"{err}\"  (raises the happyfox toast)");
    }

    // Parameter-type list from reflection metadata only — never reads
    // instance values, so it can't trigger the MonoMod trampoline crash.
    private static string Sig(MethodBase m)
    {
        var ps = m.GetParameters();
        var names = new List<string>(ps.Length);
        foreach (var p in ps) names.Add(p.ParameterType.Name);
        return $"{ps.Length} args: {string.Join(",", names)}";
    }
}
