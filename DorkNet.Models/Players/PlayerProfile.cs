using System.Text.Json.Serialization;

namespace DorkNet.Models.Players;

public class PlayerProfile
{
    private string? profileImage;

    [JsonPropertyName("AccountId")]
    public long AccountId { get; set; }

    [JsonPropertyName("Username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("ProfileImage")]
    public string? ProfileImage
    {
        get => profileImage;
        set => profileImage = value;
    }

    [JsonPropertyName("ProfileImageName")]
    public string? ProfileImageName
    {
        get => profileImage;
        set => profileImage = value;
    }

    [JsonPropertyName("BannerImage")]
    public string? BannerImage { get; set; }

    [JsonPropertyName("IsJunior")]
    public bool IsJunior { get; set; } = false;

    [JsonPropertyName("Level")]
    public int Level { get; set; } = 1;

    [JsonPropertyName("XP")]
    public int XP { get; set; } = 0;

    [JsonPropertyName("Reputation")]
    public int Reputation { get; set; } = 0;

    [JsonPropertyName("Bio")]
    public string Bio { get; set; } = string.Empty;

    [JsonPropertyName("IsVerified")]
    public bool IsVerified { get; set; } = false;

    [JsonPropertyName("IsDeveloper")]
    public bool IsDeveloper { get; set; } = false;

    [JsonPropertyName("IsCommunityTeam")]
    public bool IsCommunityTeam { get; set; } = false;

    [JsonPropertyName("CanReceiveInvites")]
    public bool CanReceiveInvites { get; set; } = true;

    [JsonPropertyName("CreatedAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
