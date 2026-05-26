using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Metadata for a single saved version of a room's PersistedRoomData
/// protobuf. The actual bytes live in S3 at the canonical
/// (bucket, key) returned by <see cref="Services.BlobRouter.Route"/>;
/// this row only carries the lookup string + provenance.
///
/// One row per save. Visitors fetching the room get the row whose
/// <see cref="BlobName"/> matches the current
/// <c>RoomEntity.CurrentDataBlobName</c>; the CDN controller takes
/// that name, routes it via <see cref="Services.BlobRouter"/>, and
/// streams bytes out of S3.
///
/// Was originally a fat row with a <c>Bytes</c> column; that column
/// was dropped once S3 became the canonical store. Only text columns
/// remain in the DB.
/// </summary>
public class RoomDataBlobEntity
{
    public long Id { get; set; }

    /// <summary>FK to RoomEntity. Indexed for "give me all versions of
    /// this room" queries (history, GC). Set to 0 for shared assets
    /// not bound to a specific room (HTRs, PV images, polaroids).</summary>
    public long RoomId { get; set; }

    /// <summary>The string the client uses as the cdn URL segment.
    /// <see cref="Services.BlobRouter.Route"/> converts this into a
    /// canonical (bucket, key) deterministically.</summary>
    [MaxLength(128)]
    public string BlobName { get; set; } = string.Empty;

    /// <summary>The player id of the uploader. Audit trail; also used by
    /// the GC to keep the room owner's saves longer than visitor saves.</summary>
    public long UploadedByPlayerId { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>For per-subroom history blobs (the `Restore to old version`
    /// UI is per-subroom), the SubRoomId this snapshot belongs to. Null for
    /// canonical scene blobs and shared assets (HTRs, PV images, polaroids).
    /// Set by the zip-importer when ingesting <c>SubRooms/.../History/</c>
    /// folders, and by the live save flow if you ever wire that path up to
    /// stamp it.</summary>
    public long? SubRoomId { get; set; }

    /// <summary>Comma-separated list of file names referenced by the
    /// PersistedRoomData (e.g. invention thumbnails, audio clips). The
    /// client passes these via the upload's referencedFilenames argument;
    /// the server uses them later when GC'ing orphaned blobs.</summary>
    [MaxLength(2048)]
    public string ReferencedFilenamesCsv { get; set; } = string.Empty;

    /// <summary>
    /// Legacy bytes column. The codebase no longer writes to this — S3
    /// is the canonical store — but rows imported / uploaded before the
    /// S3-only cutover still hold their bytes here, and the backfill
    /// migrator (<c>POST /api/admin/v1/storage/backfill</c>) reads them
    /// to upload into S3. After backfill is verified, a follow-up EF
    /// migration drops this column entirely and only metadata remains.
    /// Nullable so freshly-inserted rows can omit it without breaking
    /// SQLite's NOT NULL invariant.
    /// </summary>
    public byte[]? Bytes { get; set; }
}
