using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// A club announcement — wire type <c>RecNet.ClubAnnouncement</c>
/// (<c>Cpp2IL_ISIL/.../NFEMLMAFFIP.txt</c>). The 2020.12 watch reads
/// the JSON keys
/// <c>AnnouncementId, CreatorAccountId, ClubId, Title, Body, ImageName,
/// CreatedAt, Meta</c> verbatim — <see cref="ClubsController.ToWireAnnouncement"/>
/// emits exactly those, PascalCase. Drives the bell-icon "you have unread
/// club announcements" feed under <c>announcements/v2/mine/unread</c> and
/// <c>announcements/v2/subscription/mine/unread</c>.
/// </summary>
public class ClubAnnouncementEntity
{
    /// <summary>Mirrors wire field <c>AnnouncementId</c> (Int64).</summary>
    public long Id { get; set; }

    public long ClubId { get; set; }

    /// <summary>Mirrors wire <c>CreatorAccountId</c> (Int32 in the
    /// payload — cast in the serialiser).</summary>
    public long AuthorPlayerId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string Body { get; set; } = string.Empty;

    [MaxLength(256)]
    public string ImageName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One row per (announcement, player). Records which player has read
/// which announcement so the unread feeds can filter out already-seen
/// rows. Upserted by <c>ClubService.MarkAnnouncementReadAsync</c>.
/// </summary>
public class ClubAnnouncementReadEntity
{
    public long Id { get; set; }
    public long AnnouncementId { get; set; }
    public long PlayerId { get; set; }
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}
