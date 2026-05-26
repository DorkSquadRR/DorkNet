using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// A "Group" / club — wire type <c>RecNet.Group</c>
/// (<c>Cpp2IL_CS/.../RecNet/Group.cs</c>). Field names + types match
/// the client's <c>Group.Deserialize</c> shape so the watch round-
/// trips cleanly. Surfaces in the watch's profile tab as the player's
/// primary-group affiliation.
/// </summary>
public class ClubEntity
{
    /// <summary>Mirrors wire field <c>GroupId</c>.</summary>
    public long Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Mirrors wire <c>CreatorId</c> (Int32). Cast to int
    /// in serialiser.</summary>
    public long CreatorPlayerId { get; set; }

    [MaxLength(256)]
    public string ImageName { get; set; } = string.Empty;

    /// <summary>GroupBanStatus enum: 0=GoodStanding, 1=InReview,
    /// 2=TempLock, 3=Permaban.</summary>
    public int BanStatus { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One row per (club, player). Permissions is the
/// <c>GroupMembershipPermissions</c> flags enum (Member=0, Moderator=24,
/// CoOwner=124, Owner=127, Pending=128 for join-requests on private
/// groups).</summary>
public class ClubMembershipEntity
{
    public long Id { get; set; }
    public long ClubId { get; set; }
    public long PlayerId { get; set; }

    /// <summary>GroupMembershipPermissions flags enum value.</summary>
    public int Permissions { get; set; } = 0;

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
