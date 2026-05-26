using System.Text.Json.Serialization;

namespace DorkNet.Models.Players;

/// <summary>
/// RecNet.Progression — dump.cs:582273, Deserialize at RVA 0x1142BB0.
///
/// Verified by disassembly: reads "PlayerId", "Level", "XP" via
/// Util.GetKey&lt;int&gt;. All three keys are required (non-default) — missing
/// any of them throws KeyNotFoundException at the client.
///
/// Note: the JSON uses "PlayerId" not "AccountId" even though the C# property
/// is AccountId on most other types. Returned by:
///   GET api.rec.net/api/players/v1/progression/{accountId}
/// </summary>
public class Progression
{
    [JsonPropertyName("PlayerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("Level")]
    public int Level { get; set; } = 1;

    [JsonPropertyName("XP")]
    public int XP { get; set; } = 0;
}
