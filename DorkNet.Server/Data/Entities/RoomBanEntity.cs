using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One per-room ban issued by a room owner / admin via
/// <c>POST /api/rooms/v2/banfromroom</c>. The match service consults
/// these rows when resolving an instance join: if the joining player
/// has an active row matching the room id, the join is refused.
/// </summary>
public class RoomBanEntity
{
    public long Id { get; set; }
    public long RoomId { get; set; }
    public long BannedPlayerId { get; set; }
    public long BannedByPlayerId { get; set; }

    /// <summary>BanType enum mirror: 0 = Soft (kick), 1 = Permanent,
    /// 2 = Temporary (until <see cref="Until"/>).</summary>
    public int BanType { get; set; }

    public DateTime? Until { get; set; }

    [MaxLength(512)]
    public string Reason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
