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

    /// <summary>Path to the user's Recroom_Release_Data folder. Picked
    /// via the folder dialog; never auto-detected per project policy
    /// (no Steam library scanning).</summary>
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

    /// <summary>Host's server name shown in the join code. Empty until
    /// the user fills it in on first-run host setup.</summary>
    [JsonPropertyName("serverName")]
    public string ServerName { get; set; } = string.Empty;

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
            return JsonSerializer.Deserialize<AppState>(json, JsonOptions) ?? new AppState();
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
