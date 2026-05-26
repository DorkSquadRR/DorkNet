using System.Text.Json.Serialization;

namespace DorkNet.Models.Players;

public class UpdateProfileRequest
{
    [JsonPropertyName("DisplayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("Bio")]
    public string? Bio { get; set; }
}
