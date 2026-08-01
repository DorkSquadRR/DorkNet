using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// A "Group" / club — wire type <c>RecNet.Group</c>
/// (<c>Cpp2IL_CS/.../RecNet/Group.cs</c>) for the legacy
/// <c>api/groups/v1/*</c> surface, and the newer <c>RecNet.Club</c>
/// (<c>Cpp2IL_ISIL/.../PLILLKHMNDA.txt</c>) for the
/// <c>clubs.localhost/club/*</c> surface used by 2020.12. Both wire
/// shapes are projected from this one entity by the respective
/// controllers; field names + types match each client deserializer
/// so the watch round-trips cleanly.
///
/// The 2020.12 Club wire keys (per ISIL deserializer):
///   <c>ClubId, Name, Description, MainImageName, State, CreatorAccountId,
///   Category, Visibility, Joinability, AllowJuniors, MemberCount,
///   IsRRO, ClubhouseRoomId, ClubType</c>.
/// MemberCount is derived live from <see cref="ClubMembershipEntity"/>
/// at projection time so we never have to keep a denormalised counter
/// in sync; the other fields are persisted columns on this row.
/// </summary>
public class ClubEntity
{
    /// <summary>Mirrors wire field <c>GroupId</c> / <c>ClubId</c>.</summary>
    public long Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Mirrors wire <c>CreatorId</c> (legacy) / <c>CreatorAccountId</c>
    /// (Int32 in the 2020.12 payload). Cast to int in the wire mappers.</summary>
    public long CreatorPlayerId { get; set; }

    /// <summary>Legacy <c>ImageName</c> for <c>api/groups/v1/*</c>. The 2020.12
    /// Club wire field is <c>MainImageName</c> — same column, two projection
    /// names.</summary>
    [MaxLength(256)]
    public string ImageName { get; set; } = string.Empty;

    /// <summary>GroupBanStatus enum: 0=GoodStanding, 1=InReview,
    /// 2=TempLock, 3=Permaban.</summary>
    public int BanStatus { get; set; } = 0;

    /// <summary>Club state enum on the 2020.12 wire (<c>State</c>).
    /// 0=Active, 1=Disbanded, 2=Suspended. Defaults to Active so legacy
    /// rows created via <c>api/groups/v1/*</c> still surface as live.</summary>
    public int State { get; set; } = 0;

    /// <summary>Primary category-tag name surfaced in the 2020.12 Club
    /// wire <c>Category</c> field. Empty string is valid — the watch
    /// renders "uncategorized" when blank.</summary>
    [MaxLength(64)]
    public string Category { get; set; } = string.Empty;

    /// <summary>Club visibility enum on the wire (<c>Visibility</c>).
    /// 0=Public, 1=Unlisted, 2=Private.</summary>
    public int Visibility { get; set; } = 0;

    /// <summary>Club joinability enum on the wire (<c>Joinability</c>).
    /// 0=Open, 1=InviteOnly, 2=AskToJoin — confirmed from the 2023 dump
    /// (enum FEHIHCMDOLN). This comment previously had 1 and 2 swapped, which
    /// is the reading the requesttojoin handler was fixed away from.</summary>
    public int Joinability { get; set; } = 0;

    /// <summary>Drives the wire <c>AllowJuniors</c> bool. True means
    /// junior accounts can join + see member-only content.</summary>
    public bool AllowJuniors { get; set; } = true;

    /// <summary>Drives the wire <c>IsRRO</c> bool — Rec Room Original
    /// stamp on first-party clubs. Default false; admin-toggle only.</summary>
    public bool IsRRO { get; set; } = false;

    /// <summary>Drives the wire nullable <c>ClubhouseRoomId</c>. When
    /// set, the watch shows a "Go to clubhouse" CTA on the club
    /// detail screen that routes to this room's standard goto flow.</summary>
    public long? ClubhouseRoomId { get; set; }

    /// <summary>Drives the wire <c>ClubType</c> int. 0=Standard,
    /// 1=Featured. The legacy api/groups/v1 surface ignores this; the
    /// 2020.12 watch uses it to sort featured clubs to the top of
    /// browse.</summary>
    public int ClubType { get; set; } = 0;

    /// <summary>Minimum account level required to join, surfaced as the wire
    /// <c>MinLevel</c> and set by <c>PUT club/{id}/minlevel</c>. 0 = no gate.</summary>
    public int MinLevel { get; set; } = 0;

    /// <summary>Whether the club's chat channel is available. Drives the wire
    /// <c>ClubChatEnabled</c> and is the negation of what
    /// <c>GET club/{id}/hasDisabledClubChat</c> answers.</summary>
    public bool ClubChatEnabled { get; set; } = true;

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
