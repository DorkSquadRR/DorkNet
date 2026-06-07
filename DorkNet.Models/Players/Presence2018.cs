using System.Text.Json.Serialization;

namespace DorkNet.Models.Players;

/// <summary>
/// June-2018 presence record returned from POST api/presence/v2/list.
/// Keys verified from RecNet.DBHHFILLFLC.Deserialize: PlayerId (long),
/// IsOnline (bool), GameSession (optional object — null when the player is
/// online but not in a session; the client null-checks it).
/// </summary>
public class Presence2018
{
    [JsonPropertyName("PlayerId")]
    public long PlayerId { get; set; }

    [JsonPropertyName("IsOnline")]
    public bool IsOnline { get; set; }

    [JsonPropertyName("GameSession")]
    public object? GameSession { get; set; }
}
