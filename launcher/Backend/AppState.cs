using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DorkNet.Launcher.Backend;

/// <summary>Persisted user choices that should survive across launches:
/// host-vs-join mode (asked on first launch, switchable in settings),
/// the picked Rec Room install path, the selected server version, and
/// any Photon AppIds the user has configured. Serialized as JSON at
/// <see cref="AppPaths.StateFile"/>.</summary>
public sealed class AppState
{
    [JsonPropertyName("mode")]
    public AppMode Mode { get; set; } = AppMode.Unset;

    /// <summary>Path to the user's Recroom_Release_Data folder. First
    /// run auto-fills via <see cref="RecRoomPicker.Detect"/> (scans
    /// common manual-install paths only, never Steam library); user
    /// can override via the folder dialog.</summary>
    [JsonPropertyName("recRoomPath")]
    public string? RecRoomPath { get; set; }

    /// <summary>The version key currently selected for host mode
    /// (e.g. <c>december_2020_12_18</c>). For join mode this is set
    /// from the parsed join code.</summary>
    [JsonPropertyName("selectedVersion")]
    public string? SelectedVersion { get; set; }

    /// <summary>Photon Realtime AppId for host mode. The user gets
    /// their own (free at dashboard.photonengine.com); DorkNet never
    /// ships one. Empty until set.</summary>
    [JsonPropertyName("photonAppId")]
    public string PhotonAppId { get; set; } = string.Empty;

    [JsonPropertyName("photonVoiceAppId")]
    public string PhotonVoiceAppId { get; set; } = string.Empty;

    [JsonPropertyName("photonRegion")]
    public string PhotonRegion { get; set; } = "us";

    /// <summary>How the host exposes the server: <c>Internet</c> uses
    /// a Cloudflare quick-tunnel (works for friends anywhere with the
    /// join code); <c>LocalNetwork</c> binds the LAN IP and skips the
    /// tunnel entirely (no internet dependency, joiners must be on
    /// the same network).</summary>
    [JsonPropertyName("hostingMode")]
    public HostingMode HostingMode { get; set; } = HostingMode.Internet;

    /// <summary>Host's server name shown in the join code. Empty until
    /// the user fills it in on first-run host setup.</summary>
    [JsonPropertyName("serverName")]
    public string ServerName { get; set; } = string.Empty;

    /// <summary>True once the user has dismissed the branded welcome
    /// screen. Subsequent launches skip straight to the setup wizard
    /// (or the main host/join view if setup is also done).</summary>
    [JsonPropertyName("welcomeSeen")]
    public bool WelcomeSeen { get; set; }

    /// <summary>True once the user has finished the multi-step setup
    /// wizard. Future launches skip the wizard and open the
    /// host/join view directly. Settings -> "Re-run setup" can clear
    /// this if the user wants to walk through again.</summary>
    [JsonPropertyName("setupComplete")]
    public bool SetupComplete { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static AppState Load()
    {
        if (!File.Exists(AppPaths.StateFile)) return new AppState();
        try
        {
            var json = File.ReadAllText(AppPaths.StateFile);
            var state = JsonSerializer.Deserialize<AppState>(json, JsonOptions) ?? new AppState();

            // Backfill the welcome/setup flags for users upgrading from a
            // pre-wizard build. If they already had a working host setup
            // they've effectively passed those screens — don't drag them
            // back through the wizard on first launch of the new build.
            var migrated = false;
            if (!state.WelcomeSeen && state.Mode != AppMode.Unset)
            { state.WelcomeSeen = true; migrated = true; }
            if (!state.SetupComplete && state.Mode != AppMode.Unset &&
                !string.IsNullOrEmpty(state.RecRoomPath) &&
                (state.Mode == AppMode.Join || !string.IsNullOrEmpty(state.PhotonAppId)))
            { state.SetupComplete = true; migrated = true; }
            // Flush the migration so subsequent launches read the
            // backfilled values directly from disk — keeps the file as
            // the authoritative source instead of needing to re-derive
            // them every load.
            if (migrated) { try { state.Save(); } catch { } }

            return state;
        }
        catch
        {
            // Corrupt state file — start fresh rather than crash on every
            // launch. The user's mode pick + Photon config gets lost but
            // they can re-enter; the alternative is unrecoverable.
            return new AppState();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(AppPaths.StateFile, json);
    }
}

public enum AppMode
{
    /// <summary>First launch — show the host-vs-join wizard.</summary>
    Unset,
    Host,
    Join,
}

public enum HostingMode
{
    /// <summary>Default — Cloudflare quick-tunnel, public over the
    /// internet, works for joiners anywhere.</summary>
    Internet,
    /// <summary>LAN only — bind the machine's local IP, skip the
    /// tunnel, no internet dependency. Joiners must be on the same
    /// network.</summary>
    LocalNetwork,
}
