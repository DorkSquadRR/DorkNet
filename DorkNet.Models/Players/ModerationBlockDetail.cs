using System.Text.Json.Serialization;

namespace DorkNet.Models.Players;

/// <summary>
/// RecNet.ModerationBlockDetail — dump.cs:583626, Deserialize at RVA 0x1450830.
///
/// Verified by disassembly. Required keys (Util.GetKey, throws if missing):
///   ReportCategory (int), Duration (int), GameSessionId (long), Message (string).
/// Optional keys (Util.GetKeyOrDefault):
///   IsHostKick (bool, default false), IsBan (bool, default false),
///   PlayerIdReporter (Nullable&lt;int&gt;, default null).
///
/// Returned by:
///   GET api.rec.net/api/PlayerReporting/v1/moderationBlockDetails
/// (Single object, NOT a list — the client method GetModerationBlockDetails
/// returns IPromise&lt;ModerationBlockDetail&gt;.)
///
/// To indicate "user is not blocked": ReportCategory=0, Duration=0,
/// GameSessionId=0, Message="", IsBan=false, IsHostKick=false,
/// PlayerIdReporter=null.
/// </summary>
public class ModerationBlockDetail
{
    // PlayerReporting.ReportCategory enum (int)
    [JsonPropertyName("ReportCategory")]
    public int ReportCategory { get; set; } = 0;

    [JsonPropertyName("Duration")]
    public int Duration { get; set; } = 0;

    [JsonPropertyName("GameSessionId")]
    public long GameSessionId { get; set; } = 0;

    [JsonPropertyName("Message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("IsHostKick")]
    public bool IsHostKick { get; set; } = false;

    [JsonPropertyName("PlayerIdReporter")]
    public int? PlayerIdReporter { get; set; } = null;

    [JsonPropertyName("IsBan")]
    public bool IsBan { get; set; } = false;
}
