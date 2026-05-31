namespace DorkNet.Server.Data.Entities;

/// <summary>
/// A device that tried to log in / create an account while signups were
/// disabled and got refused (it has no account yet). Recorded so the
/// public <c>/join</c> page can show a player the device id their own
/// game client just reported — matched by request IP — instead of making
/// them dig the Unity <c>deviceUniqueIdentifier</c> out of Player.log by
/// hand. Purely a UX aid for the signup-code redeem flow; rows are
/// upserted by <c>DeviceId</c> and can be pruned freely.
/// </summary>
public class PendingDeviceEntity
{
    public long Id { get; set; }

    /// <summary>The Unity device id the refused client sent.</summary>
    public string DeviceId { get; set; } = string.Empty;

    public int Platform { get; set; }
    public string PlatformId { get; set; } = string.Empty;

    /// <summary>Last request IP the refusal came from — the /join page
    /// matches on this so a player sees their own device.</summary>
    public string? LastIp { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
