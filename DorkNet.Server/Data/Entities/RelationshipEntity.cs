namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Storage-side enum mirroring the int values previously hard-coded in
/// <see cref="RelationshipEntity"/>. Stays separate from the on-the-wire
/// <c>DorkNet.Models.Relationships.RelationshipStatus</c> so the contract
/// can evolve without touching the DB representation. Underlying int
/// values match — no migration data fixup needed.
/// </summary>
public enum RelationshipStatus
{
    /// <summary>Mutual friend (both directions accepted).</summary>
    Friend = 1,

    /// <summary>Outgoing request from <see cref="RelationshipEntity.RequesterId"/>
    /// awaiting accept/decline by <see cref="RelationshipEntity.TargetId"/>.</summary>
    PendingSent = 2,

    /// <summary>
    /// Reserved for the receiving-side view if we later denormalise. The
    /// backing column is still 1-2-4 in practice; PendingReceived is what
    /// the wire DTO computes from the caller's perspective when the row's
    /// PendingSent direction is "into" them.
    /// </summary>
    PendingReceived = 3,

    /// <summary>Block held by <see cref="RelationshipEntity.RequesterId"/>
    /// against <see cref="RelationshipEntity.TargetId"/>. Hides the target
    /// from the requester's friend list and prevents new requests both ways.</summary>
    Blocked = 4,
}

public class RelationshipEntity
{
    public long Id { get; set; }
    public long RequesterId { get; set; }
    public long TargetId { get; set; }
    public RelationshipStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>True when <see cref="RequesterId"/> favorited
    /// <see cref="TargetId"/>. Favorited shows up first in the friend
    /// list and triggers an extra notification on "online".</summary>
    public bool Favorited { get; set; }

    /// <summary>True when <see cref="RequesterId"/> muted text from
    /// <see cref="TargetId"/>. Drops chat-message notifications but
    /// keeps the friendship intact.</summary>
    public bool Muted { get; set; }

    /// <summary>True when <see cref="RequesterId"/> ignored
    /// <see cref="TargetId"/> (a soft block — hides the player from
    /// the room browser without breaking the relationship row).</summary>
    public bool Ignored { get; set; }

    public PlayerEntity? Requester { get; set; }
}
