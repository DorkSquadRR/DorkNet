using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One per (player, deviceId) — recorded on
/// <c>POST /api/deviceid/v1/register</c>. Lets admins flag
/// duplicate-account abuse: when a banned player creates a new
/// account on the same device, the new account inherits the device
/// ban automatically.
/// </summary>
public class PlayerDeviceEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }

    [MaxLength(128)]
    public string DeviceId { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Platform { get; set; } = string.Empty;

    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>Set by admin tooling — if true, every account using
    /// this device is auto-banned at login.</summary>
    public bool IsBanned { get; set; } = false;
}

/// <summary>Per-player notification preferences. iOS-style toggle
/// page; Android / Quest just persist the same shape.</summary>
public class NotificationPrefsEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }

    [MaxLength(32)]
    public string Platform { get; set; } = string.Empty;

    public bool AllowAll { get; set; } = true;
    public bool AllowFriendRequest { get; set; } = true;
    public bool AllowMessage { get; set; } = true;
    public bool AllowEventInvite { get; set; } = true;
    public bool AllowAnnouncements { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Sticky cohort assignment — one row per (player, cohort
/// key). Created on first hit to <c>GET /api/cohort/v1/{key}</c> with
/// a randomly selected variant; remains stable for the lifetime of
/// the player so A/B tests don't flicker between sessions.</summary>
public class CohortAssignmentEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }

    [MaxLength(64)]
    public string CohortKey { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Variant { get; set; } = string.Empty;

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}
