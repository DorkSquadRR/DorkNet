namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One row per (Room, Player) recording the player's visit history
/// to that room. Backs the proper <c>RoomEntity.VisitorCount</c>
/// (count of unique players who've visited) vs <c>VisitCount</c>
/// (total joins, including the same player multiple times) split
/// the official Rec.Net stats column distinguishes.
///
/// Upserted by <c>GoToController</c> on every <c>POST /goto/room</c>
/// so the per-room "who's been here, when" history is queryable for
/// the Hot-rooms ranking + admin diagnostics.
/// </summary>
public class RoomVisitEntity
{
    public long Id { get; set; }
    public long RoomId { get; set; }
    public long PlayerId { get; set; }

    /// <summary>Set once on the player's first visit; immutable after.
    /// Used to decide whether to bump RoomEntity.VisitorCount.</summary>
    public DateTime FirstVisitAt { get; set; } = DateTime.UtcNow;

    /// <summary>Updated on every visit. Drives "recent visitors"
    /// queries.</summary>
    public DateTime LastVisitAt { get; set; } = DateTime.UtcNow;

    /// <summary>Per-player visit count (this player visited this room
    /// N times). Sum across all rows = RoomEntity.VisitCount.</summary>
    public int VisitCount { get; set; } = 0;
}
