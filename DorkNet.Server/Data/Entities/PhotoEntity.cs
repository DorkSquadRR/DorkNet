using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One in-game camera photo, posted to the social feed. The actual
/// pixel bytes live in <see cref="RoomDataBlobEntity"/> keyed by
/// <see cref="BlobName"/> (uploaded earlier via storage.rec.net's
/// /upload endpoint with FileType=Image). This entity wraps that blob
/// with the social metadata: who took it, where, who's tagged in it,
/// when it was posted, and how many cheers it has.
///
/// Public photos are visible without auth on feed.rec.net; private
/// photos are only listable by the uploader (and admins). Soft-delete
/// via <see cref="DeletedAt"/> so moderation can take a photo down
/// without losing the audit trail.
/// </summary>
public class PhotoEntity
{
    public long Id { get; set; }

    public long UploaderPlayerId { get; set; }

    /// <summary>The cdn filename — the same string returned by
    /// storage.rec.net/upload and used in the cdn.rec.net/{name}
    /// download path. Indexed because feed lookups join blob bytes
    /// via this key.</summary>
    [MaxLength(256)]
    public string BlobName { get; set; } = string.Empty;

    /// <summary>Optional caption typed on the watch's "Share" panel
    /// before posting. Limited so a single photo can't grow the
    /// table row unbounded.</summary>
    [MaxLength(2048)]
    public string Caption { get; set; } = string.Empty;

    /// <summary>The room the photo was taken in (resolved from
    /// PlayerPresenceService at upload time). 0 if the player wasn't
    /// in any room (rare — only happens during loading).</summary>
    public long RoomId { get; set; }

    /// <summary>Comma-separated list of tagged player IDs. The watch
    /// builds this from the players visible in the camera's frustum
    /// when the shutter was pressed. Tagged players show up in their
    /// own "Photos of me" feed.</summary>
    [MaxLength(2048)]
    public string TaggedPlayerIdsCsv { get; set; } = string.Empty;

    /// <summary>If false, photo only appears in the uploader's own
    /// "My photos" page (and admin views); if true, it shows up on
    /// the public feed.rec.net feed and any tagged player's
    /// "Photos of me" tab. Defaults to true since the camera's
    /// "Share" button is the natural action — making it private is
    /// an explicit toggle.</summary>
    public bool IsPublic { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft-delete sentinel — non-null means the photo is
    /// hidden from every feed but still in the table for audit.
    /// Admin moderation tools set this; the bytes blob is kept until
    /// a separate cleanup job decides the audit window has elapsed.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>Denormalised cheer counter — incremented when a
    /// CheerEntity row is inserted with TargetPhotoId=this.Id, so the
    /// feed sort doesn't have to JOIN+aggregate every render.</summary>
    public int CheerCount { get; set; }

    public int ViewCount { get; set; }
}
