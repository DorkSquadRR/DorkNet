// DorkNet.DebugMod — opt-in debugging switch for DorkNet.ClientMod.
//
// This mod intentionally does almost nothing on its own. Its job is to
// EXIST: when its DLL sits in the Mods folder next to DorkNet.ClientMod.dll,
// the ClientMod detects it at startup (Cfg.EnableDiagnostics) and installs
// its verbose diagnostic patches — HTTP header/callback tracing, join/quit
// /studio traces, and the deep RegisterDiagnostics probes. Remove this DLL
// to ship a quiet, minimal client that only carries the connect + anti-cheat
// patches.
//
// Keeping the switch as a separate assembly (rather than a config flag baked
// into the shipping mod) means the diagnostics literally cannot turn
// themselves on in a normal install — you have to deliberately drop this
// file in. That is the "as safe as possible" default the main mod wants.

using MelonLoader;

[assembly: MelonInfo(typeof(DorkNet.DebugMod.DebugMod), "DorkNet DebugMod", "1.0.0", "Dork Squad")]
[assembly: MelonGame(null, null)]

namespace DorkNet.DebugMod;

public class DebugMod : MelonMod
{
    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg(
            "=== DorkNet DebugMod present — DorkNet.ClientMod diagnostics are ENABLED. " +
            "Remove DorkNet.DebugMod.dll from Mods to ship a quiet client. ===");
    }
}
