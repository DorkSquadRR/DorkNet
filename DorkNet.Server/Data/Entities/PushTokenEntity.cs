using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One push-notification token per (player, platform). Backs
/// <c>POST api/messages/v1/IOSSaveDeviceToken</c> +
/// <c>IOSClearDeviceToken</c>. Stored so an APNS / FCM proxy could
/// fan out push notifications to mobile clients; on a private
/// server we just persist for audit + future use.
/// </summary>
public class PushTokenEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }

    /// <summary>Platform: "ios", "android", "fcm", etc.</summary>
    [MaxLength(16)]
    public string Platform { get; set; } = "ios";

    [MaxLength(256)]
    public string Token { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One row per (player, platform user id) that the player has
/// chosen to ignore even before they appear in
/// <see cref="RelationshipEntity"/>. Backs
/// <c>POST api/relationships/v1/bulkignoreplatformusers</c> — when
/// a Steam friend joins later, the relationship is auto-set to
/// Ignored. Lets the import stick across friend-discovery cycles.
/// </summary>
public class PlatformIgnoreEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }

    /// <summary>PlatformType enum: 0=Steam, 1=Oculus, 2=PlayStation,
    /// 3=Microsoft, 5=IOS.</summary>
    public int Platform { get; set; }

    [MaxLength(64)]
    public string PlatformUserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One row per "card" displayed on the watch's home screen / cards
/// tab. Backs <c>GET api/cards/v1/all</c>. Cards are short tappable
/// tiles ("Daily Login", "New Room: Foo", "Friend invited you to
/// X") seeded by the server and consumed by the client.
/// </summary>
public class CardEntity
{
    public long Id { get; set; }

    /// <summary>If null, the card is global (shown to everyone).
    /// If set, only visible to that specific player.</summary>
    public long? PlayerId { get; set; }

    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Body { get; set; } = string.Empty;

    [MaxLength(256)]
    public string ImageName { get; set; } = string.Empty;

    [MaxLength(512)]
    public string ActionUrl { get; set; } = string.Empty;

    /// <summary>Card category — "announcement", "daily", "event",
    /// "system". Drives the icon the watch renders.</summary>
    [MaxLength(32)]
    public string Category { get; set; } = "announcement";

    public int Priority { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
}
