namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One cheer (the watch's positive-feedback button). Targets exactly
/// one of: a player (stored on <see cref="TargetPlayerId"/>), a room
/// (<see cref="TargetRoomId"/>), or a photo (<see cref="TargetPhotoId"/>).
/// Unused targets are 0. Aggregated reputation queries SUM rows by
/// target.
///
/// One row per (FromPlayer, target, Type) — re-cheering is idempotent
/// (we upsert on the unique index).
/// </summary>
public class CheerEntity
{
    public long Id { get; set; }

    public long FromPlayerId { get; set; }

    public long TargetPlayerId { get; set; }
    public long TargetRoomId { get; set; }
    public long TargetPhotoId { get; set; }
    public long TargetInventionId { get; set; }

    /// <summary>RoomieCheerType enum mirror: 0=General, 1=Helpful,
    /// 2=GreatHost, 3=Sportsman, 4=Creative, 5=Credit. Stored as int
    /// to leave room for future categories without a migration.</summary>
    public int Type { get; set; }

    public DateTime CheeredAt { get; set; } = DateTime.UtcNow;
}
