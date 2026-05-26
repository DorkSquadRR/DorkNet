namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Links a <see cref="PlaylistEntity"/> to one of its member rooms. The
/// playlist's room ordering is driven by <see cref="OrderIndex"/> so a
/// curator can promote/demote entries without renumbering rows.
/// </summary>
public class PlaylistRoomEntity
{
    public long Id { get; set; }
    public long PlaylistId { get; set; }
    public long RoomId { get; set; }
    public int OrderIndex { get; set; } = 0;
}
