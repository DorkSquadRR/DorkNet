// Native watch "Dev Console" injection — typed against the per-install Il2Cpp
// interop assemblies (referenced in the csproj, dev-tools only, never shipped).
// Pure reflection couldn't drive this build's watch UI (FindObjectOfType /
// property-get / GetComponentsInChildren all failed under Il2CppInterop), so
// we use real types here: clone an existing watch Button3D, relabel it, and
// wire its OnClick to run console commands via DebugConsole.Execute (which
// works headless even though the console UI isn't in the scene). It's the
// game's own Button3D, so it's interactable with the VR laser AND the
// Screen-Mode mouse. Gated behind Cfg.EnableDevWatchButton.
using System;
using Il2CppInterop.Runtime;
using Il2CppAGUI.StackedUI;
using Il2CppRecRoom.Debugging;
using Il2CppTMPro;
using UnityEngine.Events;
using UObject = UnityEngine.Object;

namespace DorkNet.DevMenu;

internal static class DevWatchButton
{
    private static int _frame;
    private static bool _done;

    // Polled from Mod.OnUpdate while Cfg.EnableDevWatchButton is set. Waits for
    // the watch's HomeScreenFlow to exist, then injects once.
    public static void Tick()
    {
        if (_done) return;
        _frame++;
        if (_frame < 120 || (_frame % 30) != 0) return; // settle, then ~2/s
        var home = UObject.FindObjectOfType<HomeScreenFlow>();
        if (home == null && _frame < 3600) return;       // wait for the watch
        _done = true;
        if (home == null) { DevMenuMod.Log.Warning("[devbtn] HomeScreenFlow never appeared"); return; }
        try { Inject(home); }
        catch (Exception ex) { DevMenuMod.Log.Warning($"[devbtn] inject failed: {ex}"); }
    }

    private static void Inject(HomeScreenFlow home)
    {
        var buttons = home.GetComponentsInChildren<Button3D>(true);
        if (buttons == null || buttons.Length == 0)
        {
            DevMenuMod.Log.Warning("[devbtn] no Button3D found under HomeScreenFlow");
            return;
        }

        // Prefer a source button that HAS a TMP label, so our clones can be
        // relabeled (HomeButton etc. are icon-only — no text child).
        Button3D? src = null;
        for (int i = 0; i < buttons.Length; i++)
            if (buttons[i].GetComponentInChildren<TextMeshProUGUI>(true) != null) { src = buttons[i]; break; }
        bool labeled = src != null;
        if (src == null) src = buttons[0]; // fall back: inject anyway, just no text
        DevMenuMod.Log.Msg($"[devbtn] {buttons.Length} watch buttons; clone source='{src.name}' labeled={labeled}");

        int n = 0;
        foreach (var (label, cmd) in ParsePresets())
        {
            var clone = UObject.Instantiate(src, src.transform.parent);
            clone.name = $"DorkNetDev_{label}";
            clone.gameObject.SetActive(true);
            clone.transform.localScale = src.transform.localScale;

            var tmp = clone.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) tmp.text = label;

            var command = cmd; // capture per-button
            clone.OnClick.RemoveAllListeners();
            clone.OnClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>((Action)(() => Run(label, command))));
            _clones.Add(clone);
            n++;
        }
        DevMenuMod.Log.Msg($"[devbtn] injected {n} dev command button(s) into the watch bar — open the watch to use them");
        DumpCommands();
    }

    // Log the real console command names from DebugConsoleCommandConfig.Metas
    // so we can set DevCommands correctly (the asset loads even though the
    // console component isn't in the scene).
    private static void DumpCommands()
    {
        try
        {
            var cfgs = UObject.FindObjectsOfTypeAll(Il2CppType.Of<DebugConsoleCommandConfig>());
            DevMenuMod.Log.Msg($"[devcmds] {cfgs.Length} DebugConsoleCommandConfig asset(s) loaded");
            for (int c = 0; c < cfgs.Length; c++)
            {
                var cfg = cfgs[c].TryCast<DebugConsoleCommandConfig>();
                var metas = cfg?.Metas;
                if (metas == null) continue;
                DevMenuMod.Log.Msg($"[devcmds] {metas.Count} command(s):");
                for (int i = 0; i < metas.Count; i++)
                {
                    var m = metas[i];
                    DevMenuMod.Log.Msg($"    {m.MethodName}   ({m.DeclaringType})");
                }
            }
        }
        catch (Exception ex) { DevMenuMod.Log.Warning($"[devcmds] failed: {ex.Message}"); }
    }

    private static void Run(string label, string command)
    {
        DevMenuMod.Log.Msg($"[devbtn] '{label}' -> DebugConsole.Execute(\"{command}\")");
        try { DebugConsole.Execute(command); }
        catch (Exception ex) { DevMenuMod.Log.Warning($"[devbtn] Execute('{command}') failed: {ex.Message}"); }
    }

    // Presets come from Cfg.DevCommands as "Label=command" strings.
    private static System.Collections.Generic.IEnumerable<(string label, string cmd)> ParsePresets()
    {
        foreach (var entry in DevMenuMod.Cfg.DevCommands)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var eq = entry.IndexOf('=');
            if (eq > 0) yield return (entry.Substring(0, eq).Trim(), entry.Substring(eq + 1).Trim());
            else yield return (entry.Trim(), entry.Trim());
        }
    }

    private static readonly System.Collections.Generic.List<Button3D> _clones = new();
}
