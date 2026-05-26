using System.Text.Json.Serialization;

namespace DorkNet.Models.Avatar;

public class AvatarData
{
    [JsonPropertyName("AccountId")]
    public long AccountId { get; set; }

    [JsonPropertyName("AvatarItemInstances")]
    public List<AvatarItemInstance> AvatarItemInstances { get; set; } = [];
}

public class AvatarItemInstance
{
    [JsonPropertyName("Id")]
    public long Id { get; set; }

    [JsonPropertyName("ItemId")]
    public long ItemId { get; set; }

    [JsonPropertyName("IsEquipped")]
    public bool IsEquipped { get; set; }
}
