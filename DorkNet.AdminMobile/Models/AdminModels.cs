using System.Text.Json.Serialization;

namespace DorkNet.AdminMobile.Models;

public sealed class AdminLoginResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    [JsonPropertyName("account_id")]
    public long AccountId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class PlayerSummary
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsDeveloper { get; set; }
    public bool IsVerified { get; set; }
    public bool IsJunior { get; set; }
    public bool Online { get; set; }
    public int Level { get; set; }
    public int XP { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? BannedUntil { get; set; }
}

public sealed class RoomSummary
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long CreatorPlayerId { get; set; }
    public int BlobCount { get; set; }
    public bool IsAGRoom { get; set; }
    public bool IsDormRoom { get; set; }
}

public sealed class InstanceSummary
{
    public long RoomInstanceId { get; set; }
    public long RoomId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public string PhotonRoomId { get; set; } = string.Empty;
    public string PhotonRegionId { get; set; } = string.Empty;
    public int MaxCapacity { get; set; }
    public bool IsPrivate { get; set; }
    public List<InstanceParticipant> Participants { get; set; } = [];
}

public sealed class InstanceParticipant
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsMaster { get; set; }
}

public sealed class ServerSettingsDto
{
    public bool SignupsDisabled { get; set; }
    public bool GlobalFriendsEnabled { get; set; }
    public bool WeeklyChallengesCompletedRequired { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AdminStats
{
    public PlayerStats Players { get; set; } = new();
    public RoomStats Rooms { get; set; } = new();
    public int Inventions { get; set; }
    public PhotoStats Photos { get; set; } = new();
    public ModerationStats Moderation { get; set; } = new();
    public DateTime ServerTime { get; set; }
}

public sealed class PlayerStats
{
    public int Total { get; set; }
    public int OnlineNow { get; set; }
    public int BannedNow { get; set; }
    public int NewToday { get; set; }
}

public sealed class RoomStats
{
    public int Total { get; set; }
    public long TotalVisits { get; set; }
    public long TotalCheers { get; set; }
    public int ActiveSessionCount { get; set; }
    public int InGamePlayerCount { get; set; }
}

public sealed class PhotoStats
{
    public int Today { get; set; }
}

public sealed class ModerationStats
{
    public int OpenReports { get; set; }
    public int ActiveIpBans { get; set; }
}
