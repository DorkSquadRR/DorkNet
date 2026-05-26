using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One row per available club category tag. Backs
/// <c>GET /club/categoryTags</c> — the 2020.12 watch deserialises that
/// response as <c>List&lt;String&gt;</c> (<see
/// cref="Controllers.Clubs.ClubsController.GetCategoryTags"/> projects
/// just the <see cref="Name"/> column out per
/// <c>Cpp2IL_ISIL/.../JDJGIBLMFKK.txt:&gt;GetPrimaryTags</c>'s
/// <c>Action&lt;List&lt;String&gt;&gt;</c> callback signature), but the
/// table stores Id + OrderIndex + Active so admins can re-order the
/// list and soft-delete entries without losing referential history in
/// <see cref="ClubCategoryAssignmentEntity"/>.
/// </summary>
public class ClubCategoryTagEntity
{
    public long Id { get; set; }

    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Display order — lower values render first. Letters and
    /// re-ordering both flow through admin tooling; the wire response is
    /// emitted in <see cref="OrderIndex"/> order.</summary>
    public int OrderIndex { get; set; }

    /// <summary>Soft-delete flag. Inactive tags stay in the table so
    /// historical assignments resolve, but are excluded from the
    /// <c>/club/categoryTags</c> response.</summary>
    public bool Active { get; set; } = true;
}

/// <summary>
/// Junction row binding a <see cref="ClubEntity"/> to a
/// <see cref="ClubCategoryTagEntity"/>. Many-to-many; a single club can
/// carry multiple category tags (e.g. "Sports" + "Hangout"). The
/// 2020.12 watch surfaces the Club's primary category via the
/// <c>Category</c> string field on the Club wire type — assignments
/// are admin-facing for now and feed future v2 endpoints that return
/// the full tag list per club.
/// </summary>
public class ClubCategoryAssignmentEntity
{
    public long Id { get; set; }
    public long ClubId { get; set; }
    public long CategoryTagId { get; set; }
}

/// <summary>
/// One row per (club, subscriber) — drives the
/// <c>announcements/v2/subscription/mine/unread</c> feed. Distinct from
/// <see cref="ClubMembershipEntity"/> because a player can subscribe to
/// announcements without joining the club's roster (e.g. "follow this
/// public club's announcements" without becoming a member).
/// </summary>
public class ClubSubscriptionEntity
{
    public long Id { get; set; }
    public long ClubId { get; set; }
    public long PlayerId { get; set; }
    public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;
}
