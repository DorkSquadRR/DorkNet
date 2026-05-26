using System.Text.Json.Serialization;

namespace DorkNet.Models.Players;

/// <summary>
/// RecNet.Reputation — dump.cs:584087, Deserialize at RVA 0x114A830.
///
/// Verified by disassembly. ALL Cheer*/Subscriber* keys are required
/// (Util.GetKey&lt;int&gt;), missing any throws KeyNotFoundException.
/// "SelectedCheer" is the only optional one (Util.GetKeyOrDefault&lt;Nullable&lt;int&gt;&gt;).
///
/// CRITICAL TYPO: the JSON key is "Noteriety" — the dev mistyped "Notoriety".
/// The C# property is named Notoriety in dump.cs (line 584117) but the dict
/// lookup uses "Noteriety". Server response MUST match the typo.
///
/// Returned by:
///   GET api.rec.net/api/playerReputation/v1/{accountId}
/// </summary>
public class Reputation
{
    [JsonPropertyName("AccountId")]
    public int AccountId { get; set; }

    // sic: see class XML doc — typo in the binary's Util.GetKey call.
    [JsonPropertyName("Noteriety")]
    public float Notoriety { get; set; } = 0f;

    [JsonPropertyName("CheerGeneral")]
    public int CheerGeneral { get; set; }

    [JsonPropertyName("CheerHelpful")]
    public int CheerHelpful { get; set; }

    [JsonPropertyName("CheerGreatHost")]
    public int CheerGreatHost { get; set; }

    [JsonPropertyName("CheerSportsman")]
    public int CheerSportsman { get; set; }

    [JsonPropertyName("CheerCreative")]
    public int CheerCreative { get; set; }

    [JsonPropertyName("CheerCredit")]
    public int CheerCredit { get; set; }

    [JsonPropertyName("SubscriberCount")]
    public int SubscriberCount { get; set; }

    [JsonPropertyName("SubscribedCount")]
    public int SubscribedCount { get; set; }

    // PlayerCheering.CheerCategory enum (int) — Nullable; null means "no
    // active cheer set". This is the only optional key.
    [JsonPropertyName("SelectedCheer")]
    public int? SelectedCheer { get; set; } = null;
}
