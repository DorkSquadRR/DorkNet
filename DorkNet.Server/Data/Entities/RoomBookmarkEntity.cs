namespace DorkNet.Server.Data.Entities;

/// <summary>
/// A "favorite" / bookmark — when a player taps the heart on a room in the
/// watch. Backs `api/rooms/v2/mybookmarks`.
/// </summary>
public class RoomBookmarkEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }
    public long RoomId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
