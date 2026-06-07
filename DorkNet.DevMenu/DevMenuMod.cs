using HarmonyLib;
using Il2CppRecRoom.Debugging;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using UnityEngine;
using UObject = UnityEngine.Object;

[assembly: MelonInfo(typeof(DorkNet.DevMenu.DevMenuMod), "DorkNet DevMenu", "1.0.0", "Dork Squad")]
[assembly: MelonGame(null, null)]

namespace DorkNet.DevMenu;

public class DevMenuMod : MelonMod
{
    internal static MelonLogger.Instance Log => Instance!.LoggerInstance;
    internal static DevMenuMod? Instance;
    internal HarmonyLib.Harmony PatchHarmony => HarmonyInstance;
    private static readonly Dictionary<string, Type> ResolvedTypeCache = new();

    public static class Cfg
    {
        public static bool EnableDebugConsole = false;
        public static string DebugConsoleToggleKey = "BackQuote";
        public static bool DiagnoseDevMenu = false;
        public static bool EnableDevWatchButton = false;
        public static string[] DevCommands = { "Help=help", "Fly=fly", "NoClip=noclip" };
    }

    public override void OnInitializeMelon()
    {
        Instance = this;
        Log.Msg("=== DorkNet DevMenu loading ===");
        LoadConfig();

        if (Cfg.EnableDebugConsole)
        {
            foreach (var m in new[] { "OnTimeCheatDetected", "OnObscuredTypeCheatDetected", "OnHeightCheatDetected", "OnAdvancedMovementCheatDetected" })
                TryPatchByName("CheatManager", m, prefix: nameof(DebugConsolePatches.SuppressCheatDetected_Prefix));
            Log.Msg($"[debugconsole] enabled; press '{Cfg.DebugConsoleToggleKey}' to toggle the console.");
        }
    }

    public override void OnUpdate()
    {
        if (Cfg.EnableDebugConsole) DebugConsolePatches.PollToggleKey();
        if (Cfg.DiagnoseDevMenu) DevMenuProbe.Tick();
        if (Cfg.EnableDevWatchButton) DevWatchButton.Tick();
    }

    private static void LoadConfig()
    {
        try
        {
            var modDll = Assembly.GetExecutingAssembly().Location;
            var modsDir = Path.GetDirectoryName(modDll) ?? string.Empty;
            var gameDir = Path.GetDirectoryName(modsDir) ?? string.Empty;
            var userData = Path.Combine(gameDir, "MelonLoader", "UserData");
            Directory.CreateDirectory(userData);

            var devPath = Path.Combine(userData, "dorknet-devmenu.json");
            var legacyPath = Path.Combine(userData, "dorknet-clientmod.json");
            var path = File.Exists(devPath) ? devPath : legacyPath;
            if (!File.Exists(path))
            {
                Log.Msg($"[config] no dev config found; all dev menu features disabled");
                return;
            }

            var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            if (r.TryGetProperty("EnableDebugConsole", out var v)) Cfg.EnableDebugConsole = v.GetBoolean();
            if (r.TryGetProperty("DebugConsoleToggleKey", out v)) Cfg.DebugConsoleToggleKey = v.GetString() ?? Cfg.DebugConsoleToggleKey;
            if (r.TryGetProperty("DiagnoseDevMenu", out v)) Cfg.DiagnoseDevMenu = v.GetBoolean();
            if (r.TryGetProperty("EnableDevWatchButton", out v)) Cfg.EnableDevWatchButton = v.GetBoolean();
            if (r.TryGetProperty("DevCommands", out v) && v.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var e in v.EnumerateArray())
                {
                    var s = e.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
                }
                if (list.Count > 0) Cfg.DevCommands = list.ToArray();
            }

            Log.Msg($"[config] loaded {Path.GetFileName(path)}: debugConsole={Cfg.EnableDebugConsole}, devWatchButton={Cfg.EnableDevWatchButton}, diagnose={Cfg.DiagnoseDevMenu}");
        }
        catch (Exception ex)
        {
            Log.Warning($"[config] load failed, using disabled defaults: {ex.Message}");
        }
    }

    private bool TryPatchByName(string typeName, string methodName, Type[]? args = null, string? prefix = null, string? postfix = null)
    {
        try
        {
            var type = ResolveType(typeName);
            if (type is null)
            {
                Log.Warning($"[patch-miss] {typeName}.{methodName}: type not found");
                return false;
            }

            var method = args is null
                ? AccessTools.Method(type, methodName)
                : AccessTools.Method(type, methodName, args);
            if (method is null)
            {
                Log.Warning($"[patch-miss] {typeName}.{methodName}: method not found");
                return false;
            }

            HarmonyMethod? pre = prefix is null ? null : new HarmonyMethod(GetPatchMethod(prefix));
            HarmonyMethod? post = postfix is null ? null : new HarmonyMethod(GetPatchMethod(postfix));
            Instance!.PatchHarmony.Patch(method, pre, post);
            Log.Msg($"[patch-ok] {typeName}.{methodName}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"[patch-fail] {typeName}.{methodName}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static MethodInfo GetPatchMethod(string name)
    {
        foreach (var holder in new[] { typeof(DebugConsolePatches) })
        {
            var m = holder.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            if (m is not null) return m;
        }
        throw new MissingMethodException($"patch method '{name}' not found");
    }

    internal static Type? ResolveType(string requested)
    {
        if (ResolvedTypeCache.TryGetValue(requested, out var cached)) return cached;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? found = null;
            try
            {
                found = asm.GetType(requested, throwOnError: false)
                    ?? asm.GetType("Il2Cpp" + requested, throwOnError: false);
                if (found is null)
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.FullName == requested ||
                            t.Name == requested ||
                            t.FullName?.EndsWith("." + requested, StringComparison.Ordinal) == true ||
                            t.FullName?.EndsWith(".Il2Cpp" + requested, StringComparison.Ordinal) == true)
                        {
                            found = t;
                            break;
                        }
                    }
                }
            }
            catch { }

            if (found is null) continue;
            ResolvedTypeCache[requested] = found;
            return found;
        }
        return null;
    }
}

internal static class DebugConsolePatches
{
    private static bool _consoleVisible;
    private static bool _warned;
    private static KeyCode _toggleKey = KeyCode.BackQuote;
    private static bool _keyParsed;
    private static MethodInfo? _toggleMethod;

    public static bool SuppressCheatDetected_Prefix() => false;

    public static void PollToggleKey()
    {
        try
        {
            if (!_keyParsed)
            {
                _keyParsed = true;
                if (!Enum.TryParse(DevMenuMod.Cfg.DebugConsoleToggleKey, ignoreCase: true, out _toggleKey))
                {
                    _toggleKey = KeyCode.BackQuote;
                    DevMenuMod.Log.Warning($"[debugconsole] invalid key '{DevMenuMod.Cfg.DebugConsoleToggleKey}', defaulting to BackQuote");
                }
            }

            if (Input.GetKeyDown(_toggleKey))
            {
                Toggle();
            }
        }
        catch (Exception ex)
        {
            if (!_warned)
            {
                _warned = true;
                DevMenuMod.Log.Warning($"[debugconsole] poll failed: {ex.Message}");
            }
        }
    }

    private static void Toggle()
    {
        try
        {
            var console = UObject.FindObjectOfType<DebugConsole>();
            if (console == null)
            {
                DevMenuMod.Log.Warning("[debugconsole] no live DebugConsole in scene");
                return;
            }

            _toggleMethod ??= AccessTools.Method(typeof(DebugConsole), "ShowInputField", new[] { typeof(bool) })
                ?? AccessTools.Method(typeof(DebugConsole), "MCNBJFECHLJ", new[] { typeof(bool) })
                ?? FindToggleByShape(typeof(DebugConsole));
            if (_toggleMethod is null)
            {
                DevMenuMod.Log.Warning("[debugconsole] show/hide method not found");
                return;
            }

            _consoleVisible = !_consoleVisible;
            _toggleMethod.Invoke(console, new object[] { _consoleVisible });
            DevMenuMod.Log.Msg($"[debugconsole] {(_consoleVisible ? "opened" : "closed")}");
        }
        catch (Exception ex)
        {
            DevMenuMod.Log.Warning($"[debugconsole] toggle failed: {ex.Message}");
        }
    }

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
            if (found is not null) return null;
            found = m;
        }
        return found;
    }
}

internal static class DevMenuProbe
{
    private static int _frame;
    private static bool _done;

    public static void Tick()
    {
        if (_done) return;
        _frame++;
        if (_frame < 180) return;
        _done = true;

        var homeType = DevMenuMod.ResolveType("AGUI.StackedUI.HomeScreenFlow") ?? DevMenuMod.ResolveType("HomeScreenFlow");
        var consoleType = DevMenuMod.ResolveType("RecRoom.Debugging.DebugConsole") ?? typeof(DebugConsole);
        DevMenuMod.Log.Msg("=== [devmenu-probe] runtime dev-menu state ===");
        DevMenuMod.Log.Msg($"[devmenu-probe] HomeScreenFlow type: {(homeType is null ? "NOT FOUND" : homeType.FullName)}");
        DevMenuMod.Log.Msg($"[devmenu-probe] DebugConsole type: {(consoleType is null ? "NOT FOUND" : consoleType.FullName)}");
        try
        {
            var console = UObject.FindObjectOfType<DebugConsole>();
            DevMenuMod.Log.Msg($"[devmenu-probe] DebugConsole live instance: {(console == null ? "null" : "FOUND")}");
        }
        catch (Exception ex)
        {
            DevMenuMod.Log.Warning($"[devmenu-probe] console instance probe failed: {ex.Message}");
        }
        DevMenuMod.Log.Msg("=== [devmenu-probe] done ===");
    }
}
