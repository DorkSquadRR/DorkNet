using System.Text.Json.Serialization;

namespace DorkNet.Models.Avatar;

public class AvatarItem
{
    [JsonPropertyName("ItemId")]
    public long ItemId { get; set; }

    [JsonPropertyName("Name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("Equipped")]
    public bool Equipped { get; set; }
}
