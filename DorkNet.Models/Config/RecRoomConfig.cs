using System.Text.Json.Serialization;

namespace DorkNet.Models.Config;

// Required keys per RecNet.RecRoomConfig.Deserialize disassembly (RVA 0x1146FA0).
// Required-throw keys: ShareBaseUrl, LevelProgressionMaps[].Level, [].RequiredXp.
// Required-object keys (GetObjectKey, accept null): ServerMaintenance,
// AutoMicMutingConfig.
//
// NOTE: the level entry uses "RequiredXp" (lowercase 'p') per the IL string
// literal — NOT "RequiredXP". A capital P here would throw KeyNotFoundException.
public class RecRoomConfig
{
    [JsonPropertyName("MessageOfTheDay")]
    public string MessageOfTheDay { get; set; } = "Welcome to the private server!";

    [JsonPropertyName("CdnBaseUri")]
    public string CdnBaseUri { get; set; } = string.Empty;

    // Required by client (string, GetKey). Used for share-link generation.
    [JsonPropertyName("ShareBaseUrl")]
    public string ShareBaseUrl { get; set; } = "https://rec.net/";

    // GetObjectKey<ServerMaintenanceDTO>. Required key. The deserializer
    // accepts null without throwing, but the C# property getter on the
    // client doesn't null-check before use during boot. Send a real DTO
    // with StartsInMinutes far in the future = "no upcoming maintenance".
    [JsonPropertyName("ServerMaintenance")]
    public ServerMaintenanceDTO? ServerMaintenance { get; set; } = new();

    // GetObjectKey<AutoMicMutingConfig>. Required by the client AND
    // dereferenced by AudioManager.Initialize during PostLogin boot —
    // null causes:
    //   NullReferenceException at RecNet.Config.get_AutoMicMutingConfig
    //   at AudioManager.Initialize
    // which kills the boot sequence with "Error initializing Core Systems".
    // Must be a real object with all 8 float fields populated.
    [JsonPropertyName("AutoMicMutingConfig")]
    public AutoMicMutingConfig AutoMicMutingConfig { get; set; } = new();

    [JsonPropertyName("PhotonConfig")]
    public PhotonConfig PhotonConfig { get; set; } = new();

    [JsonPropertyName("MatchmakingParams")]
    public MatchmakingParams MatchmakingParams { get; set; } = new();

    [JsonPropertyName("LevelProgressionMaps")]
    public List<LevelProgressionEntry> LevelProgressionMaps { get; set; } = LevelProgressionEntry.Default();

    [JsonPropertyName("DailyObjectives")]
    public List<List<DailyObjective>> DailyObjectives { get; set; } = DailyObjective.DefaultSets();

    // 2018.06 client (RecNet.OPOHNGAOCCD.Deserialize, RecNet.cs:9062-9068) reads
    // ConfigTable as an ARRAY of {Key,Value} objects, NOT a JSON object map:
    //   foreach elem in dict["ConfigTable"]: table[elem["Key"]] = elem["Value"]
    // The 2020 client read it as an object — but this branch targets 2018, so we
    // emit the array form. Keep the settable Dictionary for existing call sites
    // (ConfigService.GetConfig populates it) and project to the wire array.
    [JsonIgnore]
    public Dictionary<string, string> ConfigTable { get; set; } = [];

    [JsonPropertyName("ConfigTable")]
    public List<ConfigTableEntry> ConfigTableWire =>
        ConfigTable.Select(kv => new ConfigTableEntry { Key = kv.Key, Value = kv.Value }).ToList();

    [JsonPropertyName("ServiceUrls")]
    public Dictionary<string, string> ServiceUrls { get; set; } = [];
}

// 2018 ConfigTable element shape: {"Key": "...", "Value": "..."} (PascalCase,
// LitJson Util.GetKey<string>("Key"/"Value")).
public class ConfigTableEntry
{
    [JsonPropertyName("Key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("Value")]
    public string Value { get; set; } = string.Empty;
}

public class PhotonConfig
{
    [JsonPropertyName("AppId")]
    public string AppId { get; set; } = string.Empty;

    [JsonPropertyName("VoiceAppId")]
    public string VoiceAppId { get; set; } = string.Empty;

    [JsonPropertyName("CloudRegion")]
    public string CloudRegion { get; set; } = "us";

    [JsonPropertyName("CrcCheckEnabled")]
    public bool CrcCheckEnabled { get; set; } = false;

    [JsonPropertyName("EnableServerTracingAfterDisconnect")]
    public bool EnableServerTracingAfterDisconnect { get; set; } = false;
}

public class MatchmakingParams
{
    // 2018 RecNet.LOIBFONMHMF.Deserialize reads exactly TWO required float keys
    // via Util.GetKey<Single> (verified via dnlib). It's fetched as an optional
    // object (GetObjectKey) inside config/v2, but once PRESENT the client
    // deserializes it — wrong/missing keys throw KeyNotFoundException →
    // "Received malformed RecNet response" → "Failed to connect to RecNet" at
    // boot. The 2020 MaxPlayersPerRoom/MinPlayersToStart keys are NOT read here.
    [JsonPropertyName("PreferFullRoomsFrequency")]
    public float PreferFullRoomsFrequency { get; set; } = 0.5f;

    [JsonPropertyName("PreferEmptyRoomsFrequency")]
    public float PreferEmptyRoomsFrequency { get; set; } = 0.5f;
}

public class LevelProgressionEntry
{
    [JsonPropertyName("Level")]
    public int Level { get; set; }

    // Note the lowercase 'p' — the client's RecRoomConfig.Deserialize calls
    // Util.GetKey<int>("RequiredXp", ...). PascalCase XP would miss.
    [JsonPropertyName("RequiredXp")]
    public int RequiredXp { get; set; }

    public static List<LevelProgressionEntry> Default()
    {
        var entries = new List<LevelProgressionEntry>();
        for (int i = 0; i <= 30; i++)
            entries.Add(new LevelProgressionEntry { Level = i, RequiredXp = i * 500 });
        return entries;
    }
}

// Inner item shape for RecRoomConfig.DailyObjectives, verified by the
// disassembled iteration body — only "type" and "score" are read. Other
// fields the client doesn't read get ignored, so we only send these two.
public class DailyObjective
{
    [JsonPropertyName("type")]
    public int Type { get; set; }

    [JsonPropertyName("score")]
    public int Score { get; set; }

    public static List<List<DailyObjective>> DefaultSets()
    {
        var sets = new List<List<DailyObjective>>();
        for (int day = 0; day < 7; day++)
        {
            sets.Add(
            [
                new DailyObjective { Type = 1, Score = 50 },
                new DailyObjective { Type = 2, Score = 30 },
                new DailyObjective { Type = 3, Score = 20 },
            ]);
        }
        return sets;
    }
}

/// <summary>
/// RecNet.AutoMicMutingConfig — dump.cs class, Deserialize at RVA 0xFA3AE0.
/// All 8 float fields are required (Util.GetKey&lt;float&gt;). Defaults are
/// "feature effectively disabled" — high thresholds, no force-mute.
/// </summary>
public class AutoMicMutingConfig
{
    [JsonPropertyName("MicSpamVolumeThreshold")]
    public float MicSpamVolumeThreshold { get; set; } = 1.0f;

    [JsonPropertyName("MicVolumeSampleInterval")]
    public float MicVolumeSampleInterval { get; set; } = 0.1f;

    [JsonPropertyName("MicVolumeSampleRollingWindowLength")]
    public float MicVolumeSampleRollingWindowLength { get; set; } = 5.0f;

    [JsonPropertyName("MicSpamSamplePercentageForWarning")]
    public float MicSpamSamplePercentageForWarning { get; set; } = 1.0f;

    [JsonPropertyName("MicSpamSamplePercentageForWarningToEnd")]
    public float MicSpamSamplePercentageForWarningToEnd { get; set; } = 0.5f;

    [JsonPropertyName("MicSpamSamplePercentageForForceMute")]
    public float MicSpamSamplePercentageForForceMute { get; set; } = 1.0f;

    [JsonPropertyName("MicSpamSamplePercentageForForceMuteToEnd")]
    public float MicSpamSamplePercentageForForceMuteToEnd { get; set; } = 0.5f;

    [JsonPropertyName("MicSpamWarningStateVolumeMultiplier")]
    public float MicSpamWarningStateVolumeMultiplier { get; set; } = 1.0f;
}

/// <summary>
/// RecNet.ServerMaintenanceDTO — dump.cs, Deserialize at RVA 0xAF5450.
/// Single required key: StartsInMinutes (int). A large positive value
/// means "maintenance window is far away" = effectively never.
/// </summary>
public class ServerMaintenanceDTO
{
    [JsonPropertyName("StartsInMinutes")]
    public int StartsInMinutes { get; set; } = int.MaxValue;
}
