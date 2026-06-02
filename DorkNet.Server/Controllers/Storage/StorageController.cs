using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Storage;

/// <summary>
/// storage.rec.net — file upload endpoint the 2020 client posts to when
/// the master client persists a room save. Verified against the IL2CPP
/// disassembly of <c>RecNet.Storage.UploadFile</c>:
/// <list type="bullet">
///   <item>Method: POST.</item>
///   <item>URL: <c>https://storage.rec.net/upload</c> (Service=Storage,
///       requestUri="upload"; per Service enum at dump.cs:586534).</item>
///   <item>Body: <c>multipart/form-data</c> with fields
///     <c>File</c> (binary, content-type from caller or
///     <c>application/octet-stream</c>), <c>FileType</c>
///     (Storage.FileType: 0=Unknown, 1=RoomSave, 2=Holotar, 3=Image,
///     4=Video, 5=Invention), and optional <c>References</c>
///     (comma-joined filenames).</item>
///   <item>Response: <c>UploadFileResponseDTO</c> with one key
///     <c>Filename</c> — the cdn URL segment the client will request via
///     <c>cdn.rec.net/room/{Filename}</c> the next time anyone enters
///     the room.</item>
/// </list>
///
/// Permission rule: the room's creator, accepted co-owners, and admins
/// can persist a new blob. Dorm saves are always scoped to the local
/// player's own dorm row.
/// </summary>
[ApiController]
[Authorize]
public class StorageController(
    DorkNetDbContext db,
    PlayerPresenceService presence,
    IObjectStorage objectStorage,
    ILogger<StorageController> logger) : ControllerBase
{
    /// <summary>
    /// FileType enum values from the client's <c>RecNet.Storage+FileType</c>
    /// (dump.cs:586846). Kept as a private enum so callers can pattern-
    /// match without leaking client-side numbering elsewhere.
    /// </summary>
    private enum FileType
    {
        Unknown   = 0,
        RoomSave  = 1,
        Holotar   = 2,
        Image     = 3,
        Video     = 4,
        Invention = 5,
    }

    [HttpPost("/upload", Order = -2000)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload()
    {
        // Form parsing — ASP.NET Core deserialises the multipart body
        // automatically. The two field names are the literal strings the
        // client uses (capitalised; verified in
        // RecNet.Storage.UploadFile ISIL state {028, 044}).
        if (!Request.HasFormContentType)
            return BadRequest(new { error = "expected multipart/form-data" });

        var form = await Request.ReadFormAsync();
        var file = form.Files["File"];
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "missing or empty File part" });

        if (!int.TryParse(form["FileType"].ToString(), out var fileTypeRaw))
            return BadRequest(new { error = "missing or non-numeric FileType" });
        var fileType = (FileType)fileTypeRaw;

        var playerId = this.RequireCurrentPlayerId();
        var references = form["References"].ToString() ?? string.Empty;
        logger.LogInformation(
            "[storage] upload accepted host={Host} fileType={FileType} player={PlayerId} bytes={Bytes} refs={ReferenceCount}",
            Request.Host.Host,
            fileType,
            playerId,
            file.Length,
            string.IsNullOrWhiteSpace(references)
                ? 0
                : references.Split(',', StringSplitOptions.RemoveEmptyEntries).Length);

        // Read the upload bytes upfront. SQLite BLOB column expects a
        // byte[]; the upload is small (KB to ~1MB) so memory is fine.
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        return fileType switch
        {
            FileType.RoomSave => await UploadRoomSaveAsync(playerId, bytes, references),
            FileType.Invention => await UploadInventionAsync(playerId, bytes, references),
            FileType.Image => await UploadImageAsync(playerId, bytes),
            FileType.Holotar => await UploadGenericAsync(playerId, bytes, "holotar"),
            FileType.Video => await UploadGenericAsync(playerId, bytes, "video"),
            _ => Ok(new
            {
                filename = $"stub_{Guid.NewGuid():N}.{Extension(fileType)}",
            }),
        };
    }


    private async Task<IActionResult> UploadRoomSaveAsync(
        long playerId, byte[] bytes, string references)
    {
        // Determine which room the upload belongs to. The client doesn't
        // include the room id in the request — it just posts a save and
        // expects the server to know based on session context. We use
        // PlayerPresenceService, which is updated by /goto/room/* and
        // is the canonical "what room is this player currently in"
        // store.
        var presenceRoom = presence.GetRoom(playerId);
        if (presenceRoom is null)
        {
            logger.LogWarning(
                "[storage] room save from player {PlayerId} had no active presence; saving to personal dorm",
                playerId);
            var dorm = await db.Rooms.FirstOrDefaultAsync(r => r.IsDormRoom && r.CreatorPlayerId == playerId);
            if (dorm is not null)
                return await UploadDormSaveAsync(playerId, bytes, references, dorm.Id);

            return BadRequest(new { error = "no active room session" });
        }

        var roomId = presenceRoom.RoomId;
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null)
        {
            var roomName = string.IsNullOrWhiteSpace(presenceRoom.Name)
                ? $"Room_{roomId}"
                : presenceRoom.Name;
            if (await db.Rooms.AnyAsync(r => r.Name == roomName))
                roomName = $"Room_{roomId}";

            logger.LogWarning(
                "[storage] room save from player {PlayerId} referenced missing room {RoomId} ({RoomName}); creating owner room row",
                playerId, roomId, roomName);
            room = new RoomEntity
            {
                Id = roomId,
                Name = roomName,
                Description = "Created from an active game session.",
                CreatorPlayerId = playerId,
                Accessibility = presenceRoom.IsPrivate ? 0 : 1,
                IsAGRoom = true,
                IsDormRoom = false,
                LocationReplicationId = string.IsNullOrWhiteSpace(presenceRoom.Location)
                    ? "76d98498-60a1-430c-ab76-b54a29b7a163"
                    : presenceRoom.Location,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
        }

        // Dorm saves go to a per-player DormStateEntity instead of the
        // shared RoomEntity. Pass the real dorm Room.Id through so the
        // saved blob is keyed against the player's own dorm row (every
        // player has their own dorm RoomEntity now, not the legacy
        // canonical sentinel id=1).
        if (room.IsDormRoom)
            return await UploadDormSaveAsync(playerId, bytes, references, room.Id);

        var canSave = room.CreatorPlayerId == playerId
            || await db.Players.AnyAsync(p => p.Id == playerId && p.IsAdmin)
            || await db.RoomRoles.AnyAsync(r =>
                r.RoomId == room.Id &&
                r.PlayerId == playerId &&
                r.Accepted &&
                r.Role == 0);
        if (!canSave)
        {
            logger.LogWarning(
                "[storage] player {PlayerId} tried to save room {RoomId} (creator={CreatorId})",
                playerId, room.Id, room.CreatorPlayerId);
            return Forbid();
        }

        // Allocate the next BlobName for this room. We use a monotonic
        // counter per-room: the (N+1)th save becomes "room_<id>_v<N+1>.dat".
        // SQLite handles the count() query fast enough for our scale; if
        // this grows too big, switch to a per-room counter column on
        // RoomEntity.
        var versionNumber = await db.RoomDataBlobs
            .Where(b => b.RoomId == roomId)
            .CountAsync() + 1;
        var blobName = $"room_{roomId}_v{versionNumber}.dat";
        var (bucket, key) = BlobRouter.Route(blobName);
        await StoreBlobObjectAsync(bucket, key, bytes, "application/octet-stream");

        var entry = new RoomDataBlobEntity
        {
            RoomId = roomId,
            BlobName = blobName,
            UploadedByPlayerId = playerId,
            UploadedAt = DateTime.UtcNow,
            ReferencedFilenamesCsv = references,
        };
        db.RoomDataBlobs.Add(entry);

        // Update RoomEntity.CurrentDataBlobName so the next visitor's
        // /api/rooms/v4/details/{id} response carries the new name and
        // the persistence flow downloads the latest bytes.
        room.CurrentDataBlobName = blobName;
        room.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        logger.LogInformation(
            "[storage] room save: room={RoomId} player={PlayerId} blob={Blob} size={Size}B",
            roomId, playerId, blobName, bytes.Length);

        return Ok(new { filename = blobName });
    }

    /// <summary>
    /// Per-player dorm save. Stores bytes in <see cref="RoomDataBlobEntity"/>
    /// keyed under <c>RoomId=1</c> (matchmaking is unchanged) and
    /// upserts the <see cref="DormStateEntity"/> row for this player so
    /// next time they enter their dorm, RoomsController.Details serves
    /// the right BlobName from their personal state row instead of the
    /// shared RoomEntity row.
    /// </summary>
    private async Task<IActionResult> UploadDormSaveAsync(
        long playerId, byte[] bytes, string references, long dormRoomId)
    {
        // Migration backfill: pre-personal-dorm saves used the legacy
        // canonical RoomId=1. Re-key any of those rows for THIS player
        // to their actual dorm RoomId so the version count below
        // doesn't collide with existing blob names. Idempotent — runs
        // a no-op once everything's migrated.
        var blobNamePrefix = $"dorm_p{playerId}_v";
        await db.RoomDataBlobs
            .Where(b => b.UploadedByPlayerId == playerId
                && b.BlobName.StartsWith(blobNamePrefix)
                && b.RoomId != dormRoomId)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.RoomId, dormRoomId));

        // Per-player blob-name-based versioning. Counting by blob-name
        // prefix is more robust than RoomId because it survives any
        // future migrations of the dorm row identity.
        var versionNumber = await db.RoomDataBlobs
            .Where(b => b.UploadedByPlayerId == playerId
                && b.BlobName.StartsWith(blobNamePrefix))
            .CountAsync() + 1;
        var blobName = $"dorm_p{playerId}_v{versionNumber}.dat";
        var (bucket, key) = BlobRouter.Route(blobName);
        await StoreBlobObjectAsync(bucket, key, bytes, "application/octet-stream");

        db.RoomDataBlobs.Add(new RoomDataBlobEntity
        {
            RoomId = dormRoomId,
            BlobName = blobName,
            UploadedByPlayerId = playerId,
            UploadedAt = DateTime.UtcNow,
            ReferencedFilenamesCsv = references,
        });

        // Upsert DormStateEntity for this player.
        var dormState = await db.DormStates
            .FirstOrDefaultAsync(d => d.PlayerId == playerId);
        if (dormState is null)
        {
            dormState = new DormStateEntity { PlayerId = playerId };
            db.DormStates.Add(dormState);
        }
        dormState.CurrentDataBlobName = blobName;
        dormState.UpdatedAt = DateTime.UtcNow;

        // Bump the dorm room's UpdatedAt timestamp so the watch's
        // freshness check (DataModifiedAt comparison) sees this as the
        // latest version and stops showing the "room is not up to
        // date" warning after a save.
        await db.Rooms
            .Where(r => r.Id == dormRoomId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow)
                .SetProperty(r => r.CurrentDataBlobName, blobName));

        await db.SaveChangesAsync();

        logger.LogInformation(
            "[storage] dorm save: player={PlayerId} blob={Blob} size={Size}B",
            playerId, blobName, bytes.Length);

        return Ok(new { filename = blobName });
    }

    /// <summary>
    /// Per-creation invention upload. Stores bytes in
    /// <see cref="RoomDataBlobEntity"/> with a recognisable
    /// <c>invention_p&lt;id&gt;_<guid>.dat</c> blob name; the
    /// metadata row in <see cref="InventionEntity"/> is created
    /// separately by the client's POST to
    /// <c>api/inventions/v3/save</c> referencing the returned
    /// Filename. We don't enforce a per-invention id at upload time
    /// (the inventon row may not exist yet) — the unique BlobName
    /// guards uniqueness on its own.
    /// </summary>
    private async Task<IActionResult> UploadInventionAsync(
        long playerId, byte[] bytes, string references)
    {
        var blobName = $"invention_p{playerId}_{Guid.NewGuid():N}.dat";
        var (bucket, key) = BlobRouter.Route(blobName);
        await StoreBlobObjectAsync(bucket, key, bytes, "application/octet-stream");

        db.RoomDataBlobs.Add(new RoomDataBlobEntity
        {
            // RoomId=0 is the "no specific room — invention library blob"
            // sentinel. The cdn lookup keys on BlobName so RoomId is
            // informational only here.
            RoomId = 0,
            BlobName = blobName,
            UploadedByPlayerId = playerId,
            UploadedAt = DateTime.UtcNow,
            ReferencedFilenamesCsv = references,
        });
        await db.SaveChangesAsync();
        logger.LogInformation(
            "[storage] invention upload: player={PlayerId} blob={Blob} size={Size}B",
            playerId, blobName, bytes.Length);
        return Ok(new { filename = blobName });
    }

    /// <summary>Profile image / screenshot upload. Stored as bytes on
    /// the same RoomDataBlobs table (RoomId=0 is the
    /// "no specific room" sentinel). The catch-all serves them via
    /// <c>cdn.rec.net/{Filename}</c> like every other blob. The
    /// caller is expected to follow up with
    /// <c>POST account/v1/profileimage</c> to wire the returned
    /// filename into <see cref="DorkNet.Server.Data.Entities.PlayerEntity.ProfileImageName"/>.
    /// </summary>
    private async Task<IActionResult> UploadImageAsync(long playerId, byte[] bytes)
    {
        var blobName = $"img_p{playerId}_{Guid.NewGuid():N}.png";
        var (bucket, key) = BlobRouter.Route(blobName);
        await StoreBlobObjectAsync(bucket, key, bytes, "image/png");

        db.RoomDataBlobs.Add(new RoomDataBlobEntity
        {
            RoomId = 0,
            BlobName = blobName,
            UploadedByPlayerId = playerId,
            UploadedAt = DateTime.UtcNow,
            ReferencedFilenamesCsv = string.Empty,
        });
        await db.SaveChangesAsync();
        logger.LogInformation(
            "[storage] image upload: player={PlayerId} blob={Blob} size={Size}B",
            playerId, blobName, bytes.Length);
        return Ok(new { filename = blobName });
    }

    /// <summary>Catch-all bytes-on-disk upload used for FileTypes that
    /// don't have specialised behaviour (Holotar, Video). Stored
    /// identically to Image; callers reference the returned Filename
    /// from wherever they need it.</summary>
    private async Task<IActionResult> UploadGenericAsync(
        long playerId, byte[] bytes, string kind)
    {
        var ext = kind switch { "video" => "mp4", _ => "bin" };
        var blobName = $"{kind}_p{playerId}_{Guid.NewGuid():N}.{ext}";
        var contentType = kind switch
        {
            "video" => "video/mp4",
            _ => "application/octet-stream",
        };
        var (bucket, key) = BlobRouter.Route(blobName);
        await StoreBlobObjectAsync(bucket, key, bytes, contentType);

        db.RoomDataBlobs.Add(new RoomDataBlobEntity
        {
            RoomId = 0,
            BlobName = blobName,
            UploadedByPlayerId = playerId,
            UploadedAt = DateTime.UtcNow,
            ReferencedFilenamesCsv = string.Empty,
        });
        await db.SaveChangesAsync();
        logger.LogInformation(
            "[storage] {Kind} upload: player={PlayerId} blob={Blob} size={Size}B",
            kind, playerId, blobName, bytes.Length);
        return Ok(new { filename = blobName });
    }

    private async Task StoreBlobObjectAsync(string bucket, string key, byte[] bytes, string contentType)
    {
        try
        {
            using var s3Timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var written = await objectStorage.PutAsync(bucket, key, bytes, contentType, s3Timeout.Token);
            logger.LogInformation(
                "[storage] object write: bucket={Bucket} key={Key} bytes={Bytes} mode={Mode}",
                bucket,
                key,
                written,
                objectStorage.IsS3Configured ? "s3" : "disk-fallback");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "[storage] object write failed: bucket={Bucket} key={Key}; keeping DB fallback so the client request can complete",
                bucket,
                key);
        }
    }

    private static string Extension(FileType type) => type switch
    {
        FileType.RoomSave => "dat",
        FileType.Image => "png",
        FileType.Video => "mp4",
        FileType.Holotar => "holotar",
        FileType.Invention => "inv",
        _ => "bin",
    };
}
