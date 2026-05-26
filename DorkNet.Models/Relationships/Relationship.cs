using System.Text.Json.Serialization;

namespace DorkNet.Models.Relationships;

public class RelationshipData
{
    [JsonPropertyName("AccountId")]
    public long AccountId { get; set; }

    [JsonPropertyName("OtherAccountId")]
    public long OtherAccountId { get; set; }

    [JsonPropertyName("Status")]
    public RelationshipStatus Status { get; set; }

    [JsonPropertyName("CreatedAt")]
    public DateTime CreatedAt { get; set; }
}

public enum RelationshipStatus
{
    None = 0,
    Friend = 1,
    PendingSent = 2,
    PendingReceived = 3,
    Blocked = 4,
}
