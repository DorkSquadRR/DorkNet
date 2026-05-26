using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Per-player dorm state. Decouples each player's dorm save bytes from
/// the canonical RoomEntity row at <c>RoomId=1</c> (which is shared
/// across every dorm visit and would otherwise leak one player's save
/// into every other player's dorm).
///
/// One row per account that has ever entered their dorm. Created on the
/// first <c>/api/rooms/v4/details/1</c> hit and updated whenever the
/// player saves their dorm via Maker Pen.
/// </summary>
public class DormStateEntity
{
    /// <summary>The owning account id. Primary key — one dorm state row
    /// per player.</summary>
    [Key]
    public long PlayerId { get; set; }

    /// <summary>Blob name for the player's most recent dorm save. Format
    /// <c>dorm_p&lt;accountId&gt;_v&lt;n&gt;.dat</c>; matches the
    /// <see cref="RoomDataBlobEntity.BlobName"/> stored when the upload
    /// landed. Empty string before the player has saved.</summary>
    [MaxLength(128)]
    public string CurrentDataBlobName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
