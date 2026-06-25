// DorkNet ProtoDumper — runtime Google.Protobuf schema extractor.
//
// Rec Room compiles its room-save / creation / circuits data model from
// .proto files into Google.Protobuf C# classes. In the shipped IL2CPP build
// those classes are name-obfuscated and the descriptor base64 is split into
// scattered string-literal chunks, so static reconstruction from
// global-metadata.dat is unreliable.
//
// At RUNTIME every FileDescriptor still carries its original, un-obfuscated
// FileDescriptorProto (message names, field names, numbers, types intact).
// The catch: these descriptors are built LAZILY and in a specific dependency
// order by the game. Force-building them ourselves (by reading a message
// type's static descriptor property out of order) throws inside the type's
// static constructor and the failure is cached — poisoning the type. So we do
// NOT enumerate types. Instead we Harmony-postfix the one library method every
// generated cctor funnels through — Google.Protobuf.Reflection.FileDescriptor
// .BuildFrom / .FromGeneratedCode — and capture each descriptor as the game
// builds it. Completely passive: just play the client through the dorm, a
// created room, the Maker Pen and a circuits board, and every descriptor that
// loads is recorded.
//
// Output (under <RecRoom>\DorkNet-ProtoDump\):
//   manifest.tsv          — one "<proto path>\t<base64>" line per file
//   files\<sanitised>.b64 — same base64, one file each
//   dump.log              — capture log
//
// Decode offline with decode_dump.py (see README.md).

using HarmonyLib;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

[assembly: MelonInfo(typeof(DorkNet.ProtoDumper.ProtoDumper), "DorkNet ProtoDumper", "2.0.0", "Dork Squad")]
[assembly: MelonGame(null, null)]

namespace DorkNet.ProtoDumper;

public class ProtoDumper : MelonMod
{
    private static MelonLogger.Instance? _log;

    // proto file name -> descriptor base64 (accumulated as the game builds them)
    private static readonly Dictionary<string, string> Files = new(StringComparer.Ordinal);
    private static readonly object Gate = new();
    private static string _outDir = "";
    private static string _filesDir = "";
    private static volatile bool _dirty;
    private int _frame;

    public override void OnInitializeMelon()
    {
        _log = LoggerInstance;
        try
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? Directory.GetCurrentDirectory();
            _outDir = Path.Combine(baseDir, "DorkNet-ProtoDump");
            _filesDir = Path.Combine(_outDir, "files");
            Directory.CreateDirectory(_filesDir);
            _log.Msg($"ProtoDumper ready. Output dir: {_outDir}");
        }
        catch (Exception e) { _log?.Error($"ProtoDumper init failed: {e}"); }

        InstallHooks();
    }

    private void InstallHooks()
    {
        try
        {
            // Google.Protobuf library type — NOT obfuscated (it's the library,
            // not game code), reached by its original name via every loaded
            // assembly so we never reference the interop DLL at compile time.
            Type? fd = ResolveType("Google.Protobuf.Reflection.FileDescriptor");
            if (fd == null)
            {
                _log?.Error("ProtoDumper: FileDescriptor type not found — cannot hook.");
                return;
            }

            var postfix = new HarmonyMethod(
                typeof(ProtoDumper).GetMethod(nameof(Captured_Postfix),
                    BindingFlags.Public | BindingFlags.Static));

            int patched = 0;
            // BuildFrom is the lower-level builder every descriptor passes
            // through; FromGeneratedCode is the generated-code entry point.
            // Patch both (deduped downstream) so nothing is missed.
            foreach (string name in new[] { "BuildFrom", "FromGeneratedCode" })
            {
                foreach (MethodInfo m in fd.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != name) continue;
                    try { HarmonyInstance.Patch(m, postfix: postfix); patched++; }
                    catch (Exception e) { _log?.Warning($"ProtoDumper: patch {name} failed: {e.Message}"); }
                }
            }
            _log?.Msg($"ProtoDumper: installed {patched} descriptor-build hooks. Play through your content; descriptors are captured as they load.");
        }
        catch (Exception e) { _log?.Error($"ProtoDumper hook install error: {e}"); }
    }

    // Resolve a type by its ORIGINAL (game-side) name, accounting for the
    // Il2Cpp prefix Il2CppInterop puts on the namespace (so
    // "Google.Protobuf.Reflection.FileDescriptor" ->
    // "Il2CppGoogle.Protobuf.Reflection.FileDescriptor").
    private static Type? ResolveType(string name)
    {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(name, false) ?? asm.GetType("Il2Cpp" + name, false);
                if (t != null) return t;
                int dot = name.IndexOf('.');
                if (dot > 0)
                {
                    var ns = asm.GetType("Il2Cpp" + name.Substring(0, dot) + name.Substring(dot), false);
                    if (ns != null) return ns;
                }
            }
            catch { }
        }
        return null;
    }

    // Harmony postfix on FileDescriptor.BuildFrom / FromGeneratedCode.
    // __result is the freshly-built FileDescriptor (Il2Cpp proxy, boxed as
    // object — same pattern the ClientMod uses for il2cpp reference returns).
    public static void Captured_Postfix(object? __result)
    {
        if (__result == null) return;
        try { CollectFile(__result); }
        catch { /* never let our hook disturb the game */ }
    }

    private static void CollectFile(object file)
    {
        string? name = GetProp(file, "Name") as string;
        if (string.IsNullOrEmpty(name)) return;

        lock (Gate)
        {
            if (Files.ContainsKey(name!)) return;

            string b64 = DescriptorBase64(file);
            Files[name!] = b64;
            _dirty = true;
        }

        // Capture import dependencies too (closure of well-known + leaf protos).
        try
        {
            object? deps = GetProp(file, "Dependencies");
            if (deps != null)
            {
                var dt = deps.GetType();
                var countP = dt.GetProperty("Count");
                var itemM = dt.GetMethod("get_Item", new[] { typeof(int) });
                if (countP != null && itemM != null)
                {
                    int n = (int)(countP.GetValue(deps) ?? 0);
                    for (int i = 0; i < n; i++)
                    {
                        object? dep = itemM.Invoke(deps, new object[] { i });
                        if (dep != null) CollectFile(dep);
                    }
                }
            }
        }
        catch { }
    }

    private static string DescriptorBase64(object file)
    {
        try
        {
            // FileDescriptor exposes no public SerializedData property — only
            // the Il2CppInterop backing field (a ByteString with the original
            // descriptor bytes). Fall back to re-serialising the parsed Proto.
            object? sd = GetProp(file, "_SerializedData_k__BackingField")
                         ?? GetProp(file, "SerializedData");
            if (sd != null)
            {
                string b = ByteStringToBase64(sd);
                if (b.Length > 0) return b;
            }
            object? proto = GetProp(file, "Proto");
            if (proto != null)
            {
                MethodInfo? toBs = proto.GetType().GetMethod("ToByteString", Type.EmptyTypes);
                object? bs = toBs?.Invoke(proto, null);
                if (bs != null) return ByteStringToBase64(bs);
            }
        }
        catch { }
        return "";
    }

    private static string ByteStringToBase64(object byteString)
    {
        MethodInfo? toB64 = byteString.GetType().GetMethod("ToBase64", Type.EmptyTypes);
        return toB64?.Invoke(byteString, null) as string ?? "";
    }

    private static object? GetProp(object obj, string propName)
    {
        var p = obj.GetType().GetProperty(propName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return p?.GetValue(obj);
    }

    public override void OnUpdate()
    {
        // Flush to disk shortly after new descriptors are captured (every ~1s).
        if (++_frame % 60 == 0 && _dirty)
            Flush();
    }

    public override void OnApplicationQuit() => Flush();

    private static void Flush()
    {
        lock (Gate)
        {
            if (!_dirty) return;
            _dirty = false;
            try
            {
                var sb = new StringBuilder();
                int withData = 0;
                foreach (var kv in Files)
                {
                    if (kv.Value.Length == 0) continue;
                    withData++;
                    sb.Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');
                    string fp = Path.Combine(_filesDir, Sanitize(kv.Key) + ".b64");
                    if (!File.Exists(fp)) File.WriteAllText(fp, kv.Value);
                }
                File.WriteAllText(Path.Combine(_outDir, "manifest.tsv"), sb.ToString());
                string line = $"captured files={Files.Count} (with-data={withData})";
                File.AppendAllText(Path.Combine(_outDir, "dump.log"), line + "\n");
                _log?.Msg($"ProtoDumper: {line} -> {_outDir}");
            }
            catch (Exception e) { _log?.Error($"ProtoDumper flush error: {e}"); }
        }
    }

    private static string Sanitize(string protoPath)
    {
        var sb = new StringBuilder(protoPath.Length);
        foreach (char c in protoPath)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' ? c : '_');
        return sb.ToString();
    }
}
