using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Controllers.Match;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Rooms.V2;

/// <summary>
/// api.rec.net/api/rooms/* — DB-backed implementation of the watch's
/// Rooms tab.
///
/// Replaces the previous stubs in MiscStubsController for:
///   v1/hot, v1/filters, v1/tags, v1/search, v2/baserooms, v2/myrooms,
///   v2/mybookmarks, v2/mymoderated, v2/mysubscribed, v2/myrecent,
///   v2/name/{name}, v2/{id}, v3/{id}, v3/details/{id}, v4/details/{id},
///   roomserver/rooms/{id}, v1/personaldetails/{id}, v2/personaldetails/{id}.
///
/// All endpoints serialize Rooms via RoomService.ToWireRoom which matches
/// Room.Deserialize at RVA 0x114E430 (PascalCase keys, all 16 required
/// fields plus optional VR-low / mobile / mic-mute flags).
/// </summary>
[ApiController]
public class RoomsController(
    RoomService rooms,
    PlaylistService playlists,
    DorkNetDbContext db,
    PlayerPresenceService presence,
    OnlinePresenceService onlinePresence,
    DomainConfig domain,
    NotificationService notifications,
    ServerSettingsService serverSettings,
    ILogger<RoomsController> logger) : ControllerBase
{
    private long? CurrentPlayerId => ControllerBaseExtensions.CurrentPlayerId(this);

    // ── Browse / Hot / Search ────────────────────────────────────────────

    /// <summary>
    /// `Rooms.GetHotRooms(roomScoreType, tags)` — the watch's "Trending" tab.
    /// `roomScoreType` is ignored (we always sort by HotScore). `tags` is
    /// passed through as a substring filter against the comma-separated
    /// TagsCsv column (e.g. `#community`, `#recroomoriginal`).
    /// </summary>
    [HttpGet("api/rooms/v1/hot")]
    public async Task<IActionResult> HotV1(
        [FromQuery] string? roomScoreType,
        [FromQuery] string? tags)
        => Ok((await rooms.HotAsync(tags)).Select(RoomService.ToWireRoom).ToList());

    [HttpGet("rooms/hot")]
    public async Task<IActionResult> HotRoomServer(
        [FromQuery] string? tag,
        [FromQuery] string? tags,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        var all = await rooms.HotAsync(tag ?? tags, take: 200);
        var page = all
            .Skip(Math.Max(skip, 0))
            .Take(Math.Clamp(take, 1, 100))
            .Select(RoomService.ToWireRoom)
            .ToList();

        return Ok(new
        {
            Results = page,
            TotalResults = all.Count,
        });
    }

    [HttpGet("rooms/topcreators")]
    [HttpGet("roomserver/rooms/topcreators")]
    public async Task<IActionResult> TopCreators([FromQuery] int skip = 0, [FromQuery] int take = 100)
    {
        var rows = await PublicRoomQuery()
            .OrderByDescending(r => r.HotScore)
            .ThenByDescending(r => r.VisitCount)
            .Take(250)
            .ToListAsync();
        var page = rows
            .GroupBy(r => r.CreatorPlayerId)
            .Select(g => g.First())
            .Skip(Math.Max(skip, 0))
            .Take(Math.Clamp(take, 1, 100))
            .ToList();
        return Ok(await BuildRoomServerListAsync(page));
    }

    [HttpGet("rooms/contestwinners")]
    [HttpGet("roomserver/rooms/contestwinners")]
    public async Task<IActionResult> ContestWinners([FromQuery] int skip = 0, [FromQuery] int take = 100)
    {
        var rows = await PublicRoomQuery()
            .Where(r =>
                EF.Functions.Like(r.TagsCsv, "%contest%") ||
                EF.Functions.Like(r.TagsCsv, "%winner%") ||
                EF.Functions.Like(r.TagsCsv, "%featured%"))
            .OrderByDescending(r => r.HotScore)
            .ThenBy(r => r.Name)
            .Skip(Math.Max(skip, 0))
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync();

        if (rows.Count == 0)
        {
            rows = await PublicRoomQuery()
                .OrderByDescending(r => r.HotScore)
                .ThenBy(r => r.Name)
                .Skip(Math.Max(skip, 0))
                .Take(Math.Clamp(take, 1, 100))
                .ToListAsync();
        }

        return Ok(await BuildRoomServerListAsync(rows));
    }

    [HttpGet("rooms/fromcreators")]
    [HttpPost("rooms/fromcreators")]
    [HttpGet("roomserver/rooms/fromcreators")]
    [HttpPost("roomserver/rooms/fromcreators")]
    public async Task<IActionResult> FromCreators([FromQuery] int skip = 0, [FromQuery] int take = 100)
    {
        var creatorIds = Request.Query["creatorId"]
            .Concat(Request.Query["creatorIds"])
            .Concat(Request.Query["accountId"])
            .Concat(Request.Query["accountIds"])
            .Concat(Request.Query["id"])
            .SelectMany(v => (v ?? string.Empty).Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => long.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var query = PublicRoomQuery();
        if (creatorIds.Count > 0)
            query = query.Where(r => creatorIds.Contains(r.CreatorPlayerId));

        var rows = await query
            .OrderByDescending(r => r.HotScore)
            .ThenByDescending(r => r.UpdatedAt)
            .Skip(Math.Max(skip, 0))
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync();
        return Ok(await BuildRoomServerListAsync(rows));
    }

    [HttpGet("rooms/magic_door")]
    [HttpGet("roomserver/rooms/magic_door")]
    public async Task<IActionResult> MagicDoor()
    {
        var room = await PublicRoomQuery()
            .OrderByDescending(r => r.HotScore)
            .ThenBy(r => r.Id)
            .FirstOrDefaultAsync();
        if (room is null) return NotFound();
        return Ok((await BuildRoomServerListAsync(new[] { room })).First());
    }

    /// <summary>
    /// GET <c>/roomserver/rooms/recommendations</c> — the home-tab
    /// "Recommended" carousel. <b>Wire shape is NOT a flat
    /// <c>List&lt;Room&gt;</c></b>; the watch's
    /// <c>EJDCNGBEICB.JDKLBNBIEAH</c> at <c>EJDCNGBEICB.txt:1349,1430</c>
    /// returns <c>IPromise&lt;List&lt;ANNKHNFLMNP&gt;&gt;</c> and
    /// <see cref="ANNKHNFLMNP.PPGFHEDFBEA"/> at
    /// <c>ANNKHNFLMNP.txt:43-97</c> reads <c>SeedRoom</c> (sub-object,
    /// <c>KLCOGEIGEBJ</c> i.e. full Room) and <c>Rooms</c> (list of
    /// Rooms) as required keys. Returning <c>[{room}, …]</c> raised
    /// <c>KeyNotFoundException: Failed to find key 'Rooms' when
    /// deserializing object of type KLCOGEIGEBJ</c>.
    ///
    /// We group the hot list into one "card" per room — that gives the
    /// watch one row per featured room with that room as its own
    /// SeedRoom and a single-element Rooms list. Cheap, semantically
    /// correct, and avoids the deserializer-crashing empty-Rooms
    /// edge case the watch hits when a group has no rooms.
    /// </summary>
    [HttpGet("roomserver/rooms/recommendations")]
    [HttpGet("rooms/recommendations")]
    public async Task<IActionResult> Recommendations(
        [FromQuery(Name = "splitTestId")] int? splitTestId,
        [FromQuery(Name = "splitTestValue")] int? splitTestValue)
    {
        var hot = await rooms.HotAsync(null, take: 12);
        if (hot.Count == 0) return Ok(Array.Empty<object>());

        var roomIds = hot.Select(r => r.Id).ToList();
        var sceneRows = await db.RoomScenes
            .Where(s => roomIds.Contains(s.RoomId))
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
        var scenesByRoom = sceneRows
            .GroupBy(s => s.RoomId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RoomSceneEntity>)g.ToList());

        var wireRooms = hot
            .Select(r => BuildRoomServerDetails(r, scenesByRoom.GetValueOrDefault(r.Id)))
            .ToList();

        // One ANNKHNFLMNP entry per room: SeedRoom = the room itself,
        // Rooms = [that room]. The watch enumerates the outer list to
        // build the row list; each row shows its SeedRoom as headline.
        var result = wireRooms.Select(room => new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["SeedRoom"] = room,
            ["Rooms"] = new List<object> { room },
        }).ToList();

        return Ok(result);
    }

    [HttpGet("rooms/{roomId:long}/similar")]
    [HttpGet("roomserver/rooms/{roomId:long}/similar")]
    public async Task<IActionResult> SimilarRooms(long roomId, [FromQuery] int take = 12)
    {
        var seed = await rooms.GetByIdAsync(roomId);
        if (seed is null) return NotFound();

        var tags = (seed.TagsCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        var query = PublicRoomQuery().Where(r => r.Id != roomId);
        if (tags.Length > 0)
        {
            var primaryTag = tags[0];
            query = query.Where(r => EF.Functions.Like(r.TagsCsv, $"%{primaryTag}%"));
        }

        var similar = await query
            .OrderByDescending(r => r.HotScore)
            .ThenBy(r => r.Name)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync();

        var seedWire = (await BuildRoomServerListAsync(new[] { seed })).First();
        var similarWire = await BuildRoomServerListAsync(similar);
        return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["SeedRoom"] = seedWire,
            ["Rooms"] = similarWire,
        });
    }

    /// <summary>
    /// `Rooms.SearchForRooms(query)` — free-text room search.
    /// </summary>
    [HttpGet("api/rooms/v1/search")]
    [HttpGet("api/rooms/v2/search")]
    [HttpGet("rooms/search")]
    public async Task<IActionResult> Search(
        [FromQuery(Name = "query")] string? query,
        [FromQuery(Name = "value")] string? value)
        => Ok((await rooms.SearchAsync(query ?? value ?? string.Empty))
            .Select(RoomService.ToWireRoom).ToList());

    /// <summary>GET <c>api/rooms/v2/live</c> — currently-active rooms
    /// (rooms with at least one player presence). Same shape as Hot
    /// — order by HotScore as a proxy for "live activity" since we
    /// don't track instance population separately yet.</summary>
    [HttpGet("api/rooms/v2/live")]
    public async Task<IActionResult> Live([FromQuery] string? roomScoreType)
        => Ok((await rooms.HotAsync(null)).Select(RoomService.ToWireRoom).ToList());

    /// <summary>
    /// `Rooms.GetBaseRooms` — list of "base" rooms used by Rec Room Originals
    /// to seed the room creation UI. Returns the same set as #recroomoriginal.
    /// </summary>
    [HttpGet("api/rooms/v2/baserooms")]
    [HttpGet("api/rooms/v3/baserooms")]
    [HttpGet("rooms/base")]
    public async Task<IActionResult> BaseRooms()
        => Ok((await rooms.HotAsync("#recroomoriginal")).Select(RoomService.ToWireRoom).ToList());

    /// <summary>
    /// `Rooms.GetFilters` — pinned/popular tag chips shown above the room
    /// list in the watch.
    /// </summary>
    // Tag wire format: NO leading '#'. The watch's RoomFilterChip prepends
    // '#' at render time, so sending "#community" produces "##community" in
    // the UI. Names stored in the DB also drop the prefix.
    [HttpGet("api/rooms/v1/filters")]
    public async Task<IActionResult> Filters()
    {
        var tags = await serverSettings.GetPlayMenuTagsAsync();
        return Ok(new
        {
            PinnedFilters = tags.PinnedTags,
            PopularFilters = tags.PopularTags,
            TrendingFilters = tags.TrendingTags,
        });
    }

    [HttpGet("api/rooms/v1/tags")]
    public async Task<IActionResult> Tags()
    {
        var tags = await serverSettings.GetPlayMenuTagsAsync();
        return Ok(tags.PinnedTags
            .Concat(tags.PopularTags)
            .Concat(tags.TrendingTags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    [HttpGet("api/rooms/v1/pinnedtags")]
    public async Task<IActionResult> PinnedTags()
        => Ok((await serverSettings.GetPlayMenuTagsAsync()).PinnedTags);

    [HttpGet("api/rooms/v1/populartags")]
    public async Task<IActionResult> PopularTags()
        => Ok((await serverSettings.GetPlayMenuTagsAsync()).PopularTags);

    [HttpGet("api/rooms/v1/trendingtags")]
    public async Task<IActionResult> TrendingTags()
        => Ok((await serverSettings.GetPlayMenuTagsAsync()).TrendingTags);

    // ── Single room lookups ──────────────────────────────────────────────

    /// <summary>
    /// `Rooms.GetByName(roomName)` — used when the client constructs the
    /// share-link target or when /goto/room/{name} runs. Falls back to a
    /// synthesised "Room_<name>" for unknown names so the deserializer
    /// doesn't crash on 404.
    /// </summary>
    [HttpGet("api/rooms/v2/name/{roomName}")]
    [HttpGet("api/rooms/v3/name/{roomName}")]
    public async Task<IActionResult> ByName(string roomName)
    {
        var r = await rooms.GetByNameAsync(roomName);
        return Ok(RoomService.ToWireRoom(r ?? Synthetic(roomName)));
    }

    /// <summary>
    /// `Rooms.GetById(roomId)` — same flat shape as ByName.
    /// </summary>
    [HttpGet("api/rooms/v2/{roomId:long}")]
    [HttpGet("api/rooms/v3/{roomId:long}")]
    public async Task<IActionResult> ById(long roomId)
    {
        var r = await rooms.GetByIdAsync(roomId);
        return Ok(RoomService.ToWireRoom(r ?? Synthetic($"Room_{roomId}", roomId)));
    }

    [HttpGet("rooms/{roomId:long}")]
    public async Task<IActionResult> RoomServerById(
        long roomId,
        [FromQuery] int? unityAssetTarget,
        [FromQuery] int? unityAssetVersion)
    {
        var room = await rooms.GetByIdAsync(roomId) ?? Synthetic($"Room_{roomId}", roomId);
        var sceneRows = await db.RoomScenes
            .Where(s => s.RoomId == room.Id)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
        var roles = await db.RoomRoles
            .Where(r => r.RoomId == room.Id)
            .ToListAsync();

        return Ok(BuildRoomServerDetails(
            room,
            sceneRows,
            roles: roles,
            unityAssetTarget: unityAssetTarget,
            unityAssetVersion: unityAssetVersion));
    }

    // ── RoomDetails (the boot-critical one) ─────────────────────────────
    //
    // /v4/details/{roomId} is hit *during* the matchmaking goto flow.
    // RoomDetails.Deserialize at RVA 0x114C580 expects PascalCase keys:
    // Room (object), Scenes (list), CoOwners/InvitedCoOwners,
    // Moderators/InvitedModerators, Hosts/InvitedHosts (all int lists),
    // CheerCount, FavoriteCount, VisitCount, Tags.
    //
    // Scenes is populated for every room. The persistence coroutine
    // (`RoomPersistenceManager+<DownloadRoomDataBlobCoroutine>`) runs
    // unconditionally on scene load and dereferences
    // `Rooms.LocalRoomScene.DataBlobName` on its first MoveNext — so
    // returning Scenes=[] makes LocalRoomScene null and NPEs the boot.
    //
    // The historical "Scenes=[] short-circuits the persistence manager"
    // comment that used to live here was the reverse of what actually
    // happens in this build. Confirmed empirically with the [req] log:
    // /v4/details/1 fires before scene load for the dorm too, so
    // Rooms.LocalRoom is populated and the coroutine just needs a
    // populated Scenes[0] to get a non-null LocalRoomScene.

    /// <summary>GET <c>roomserver/rooms/{roomId}/subrooms/{subRoomId}/datahistory</c>
    /// — drives the watch's "Restore to old version" UI in the room-
    /// settings tab. Per-subroom (not per-room) — the watch builds the
    /// URL from the template
    /// <c>String.Format("rooms/{0}/subrooms/{1}/datahistory", roomId, subRoomId)</c>
    /// (verified at <c>EJDCNGBEICB.txt:1190</c>); the <c>roomserver/</c>
    /// prefix is added by RecNet's URL builder for everything on the
    /// rooms host (api.localhost proxies it through the same prefix as the
    /// rest of the rooms API — see all the other <c>roomserver/rooms/...</c>
    /// routes in this controller).
    ///
    /// <para><b>SubRoomId semantics:</b> the watch's 2020.12 build treats
    /// "SubRoomId" as the <see cref="RoomSceneEntity.OrderIndex"/>, NOT
    /// rec.net's long random Guid-like SubRoomId. The heartbeat reports
    /// <c>subRoom=11</c> for the 12th scene in <c>RoomDetails.SubRooms[]</c>;
    /// this endpoint must filter by OrderIndex to match. The zip-importer
    /// stamps each <c>SubRooms/&lt;scene&gt;/History/</c> blob's
    /// <see cref="RoomDataBlobEntity.SubRoomId"/> with the scene's
    /// OrderIndex at insert time — the rec.net SubRoomId is captured in
    /// the sidecar JSON for provenance but isn't what gets indexed.</para>
    ///
    /// <para>Wire shape: <c>List&lt;RoomDataHistoryDTO&gt;</c>. The
    /// watch's <c>DHOCPFIOKHD.PPGFHEDFBEA</c> deserializer reads EXACTLY
    /// four PascalCase keys (<c>DHOCPFIOKHD.txt:136-150</c>):
    /// <c>SubRoomId</c> (long, strict GetKey), <c>DataBlob</c> (string),
    /// <c>SavedByAccountId</c> (long, strict GetKey), <c>CreatedAt</c>
    /// (DateTime, GetKeyOrDefault). Missing strict keys throw
    /// <c>KeyNotFoundException</c> in LitJson and the watch shows the
    /// "Could not retrieve save history from server :-(" toast from
    /// <c>UIRestoreRoomDialog.txt:1970</c>. Extra keys are ignored.</para>
    ///
    /// <para>Pre-2026-05 rows (and any rows the live save flow inserts,
    /// which doesn't populate SubRoomId yet) have a null SubRoomId and
    /// surface under every sub-room's history list — back-compat.</para></summary>
    [HttpGet("roomserver/rooms/{roomId:long}/subrooms/{subRoomId:long}/datahistory")]
    [HttpGet("rooms/{roomId:long}/subrooms/{subRoomId:long}/datahistory")]
    public async Task<IActionResult> SubRoomDataHistory(long roomId, long subRoomId)
    {
        var blobs = await db.RoomDataBlobs
            .Where(b => b.RoomId == roomId
                     && (b.SubRoomId == subRoomId || b.SubRoomId == null))
            // Skip the canonical per-scene blob from the import — the watch
            // is asking for HISTORY, and serving the current `room_<id>_<slug>_<scene>.room`
            // back to it as an option would make `restore to current` a
            // confusing no-op entry in the list.
            .Where(b => !b.BlobName.StartsWith($"room_{roomId}_"))
            .OrderByDescending(b => b.UploadedAt)
            .Take(50)
            .Select(b => new
            {
                SubRoomId = subRoomId,
                DataBlob = b.BlobName,
                SavedByAccountId = b.UploadedByPlayerId,
                CreatedAt = b.UploadedAt,
            })
            .ToListAsync();
        return Ok(blobs);
    }

    /// <summary>GET <c>rooms/{roomId}/subrooms/{subRoomId}/saves/{saveId}</c>
    /// — Studio room save metadata. The 2023 client can load baked Studio
    /// scenes by first asking this endpoint which Unity asset bundle file
    /// belongs to the save, then downloading it from
    /// <c>cdn.../room/{Filename}.assetbundle</c>. The importer persists
    /// the exact bundle filenames from the Studio dump on RoomScenes, so
    /// this response is backed by the imported archive bytes rather than a
    /// placeholder.</summary>
    [HttpGet("rooms/{roomId:long}/subrooms/{subRoomId:long}/saves/{saveId:long}")]
    [HttpGet("roomserver/rooms/{roomId:long}/subrooms/{subRoomId:long}/saves/{saveId:long}")]
    [Authorize]
    public async Task<IActionResult> StudioSubRoomSave(
        long roomId,
        long subRoomId,
        long saveId,
        [FromQuery] int? unityAssetTarget,
        [FromQuery] int? unityAssetVersion)
    {
        var room = await rooms.GetByIdAsync(roomId);
        if (room is null) return NotFound(new { error = "room_not_found", roomId });

        var sceneRows = await db.RoomScenes
            .Where(s => s.RoomId == roomId)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
        var scene = sceneRows.FirstOrDefault(s => s.OrderIndex == (int)subRoomId)
            ?? sceneRows.FirstOrDefault(s => s.StudioSubRoomDataSaveId == saveId);
        if (scene is null) return NotFound(new { error = "subroom_not_found", roomId, subRoomId });

        return Ok(BuildStudioSaveWire(room, scene, subRoomId, saveId, unityAssetTarget, unityAssetVersion));
    }

    /// <summary>GET <c>rooms/{roomId}/subrooms</c> — the subroom list the
    /// 2023 client / Studio editor fetches
    /// (<c>String.Format("rooms/{0}/subrooms", roomId)</c>). Emits the same
    /// per-subroom shape as the <c>SubRooms[]</c> entries inside
    /// <c>/roomserver/rooms/{id}</c>, so a single-scene room synthesises one
    /// "Home" entry and a multi-scene imported room enumerates its
    /// RoomScenes.</summary>
    [HttpGet("rooms/{roomId:long}/subrooms")]
    [HttpGet("roomserver/rooms/{roomId:long}/subrooms")]
    [Authorize]
    public async Task<IActionResult> SubRooms(long roomId)
    {
        var room = await rooms.GetByIdAsync(roomId);
        if (room is null) return NotFound(new { error = "room_not_found", roomId });

        var sceneRows = await db.RoomScenes
            .Where(s => s.RoomId == roomId)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();

        var wire = sceneRows.Count > 0
            ? sceneRows.Select(s => BuildSubRoomWire(room, s)).ToArray()
            : new[] { BuildSubRoomWire(room, null) };
        return Ok(wire);
    }

    /// <summary>GET <c>rooms/{roomId}/subrooms/{subRoomId}</c> — one subroom
    /// (<c>String.Format("rooms/{0}/subrooms/{1}", roomId, subRoomId)</c>).
    /// <paramref name="subRoomId"/> is the RoomScene OrderIndex.</summary>
    [HttpGet("rooms/{roomId:long}/subrooms/{subRoomId:long}")]
    [HttpGet("roomserver/rooms/{roomId:long}/subrooms/{subRoomId:long}")]
    [Authorize]
    public async Task<IActionResult> SubRoom(long roomId, long subRoomId)
    {
        var room = await rooms.GetByIdAsync(roomId);
        if (room is null) return NotFound(new { error = "room_not_found", roomId });

        var scene = await db.RoomScenes
            .Where(s => s.RoomId == roomId && s.OrderIndex == (int)subRoomId)
            .FirstOrDefaultAsync();
        // A single-scene room has no RoomScene row; subRoom 0 is the
        // synthesised "Home" scene.
        if (scene is null && subRoomId != 0)
            return NotFound(new { error = "subroom_not_found", roomId, subRoomId });

        return Ok(BuildSubRoomWire(room, scene));
    }

    /// <summary>GET <c>rooms/{roomId}/subrooms/{subRoomId}/data</c> — the
    /// current persisted save for the subroom
    /// (<c>String.Format("rooms/{0}/subrooms/{1}/data", roomId, subRoomId)</c>).
    /// Returns the same <c>SubRoomDataSave</c> shape as
    /// <c>/saves/{saveId}</c> resolved to the scene's current save id.</summary>
    [HttpGet("rooms/{roomId:long}/subrooms/{subRoomId:long}/data")]
    [HttpGet("roomserver/rooms/{roomId:long}/subrooms/{subRoomId:long}/data")]
    [Authorize]
    public async Task<IActionResult> SubRoomData(
        long roomId,
        long subRoomId,
        [FromQuery] int? unityAssetTarget,
        [FromQuery] int? unityAssetVersion)
    {
        var room = await rooms.GetByIdAsync(roomId);
        if (room is null) return NotFound(new { error = "room_not_found", roomId });

        var scene = await db.RoomScenes
            .Where(s => s.RoomId == roomId && s.OrderIndex == (int)subRoomId)
            .FirstOrDefaultAsync();
        if (scene is null && subRoomId != 0)
            return NotFound(new { error = "subroom_not_found", roomId, subRoomId });

        var saveId = scene?.StudioSubRoomDataSaveId ?? 0L;
        return Ok(BuildStudioSaveWire(room, scene, subRoomId, saveId, unityAssetTarget, unityAssetVersion));
    }

    /// <summary>GET <c>rooms/{roomId}/subrooms/{subRoomId}/saves</c> — the
    /// list of saves for a subroom
    /// (<c>String.Format("rooms/{0}/subrooms/{1}/saves", roomId, subRoomId)</c>).
    /// Returns the current save plus any historical blobs recorded for the
    /// subroom, newest first, each in the <c>SubRoomDataSave</c> shape.</summary>
    [HttpGet("rooms/{roomId:long}/subrooms/{subRoomId:long}/saves")]
    [HttpGet("roomserver/rooms/{roomId:long}/subrooms/{subRoomId:long}/saves")]
    [Authorize]
    public async Task<IActionResult> SubRoomSaves(long roomId, long subRoomId)
    {
        var room = await rooms.GetByIdAsync(roomId);
        if (room is null) return NotFound(new { error = "room_not_found", roomId });

        var scene = await db.RoomScenes
            .Where(s => s.RoomId == roomId && s.OrderIndex == (int)subRoomId)
            .FirstOrDefaultAsync();
        if (scene is null && subRoomId != 0)
            return NotFound(new { error = "subroom_not_found", roomId, subRoomId });

        var currentSaveId = scene?.StudioSubRoomDataSaveId ?? 0L;
        var saves = new List<object>
        {
            BuildStudioSaveWire(room, scene, subRoomId, currentSaveId, null, null),
        };

        // Historical blobs for this subroom (the same rows the datahistory
        // endpoint surfaces), surfaced as prior saves so the Studio editor's
        // version picker has the full set. Distinct by blob name, current
        // save excluded (already first in the list).
        var history = await db.RoomDataBlobs
            .Where(b => b.RoomId == roomId && b.SubRoomId == subRoomId)
            .OrderByDescending(b => b.UploadedAt)
            .Take(50)
            .ToListAsync();
        foreach (var h in history)
        {
            if (!string.IsNullOrWhiteSpace(scene?.DataBlobName) &&
                string.Equals(h.BlobName, scene!.DataBlobName, StringComparison.OrdinalIgnoreCase))
                continue;
            saves.Add(new
            {
                SubRoomDataSaveId = currentSaveId,
                SubRoomId = subRoomId,
                UnityAssetId = scene?.StudioUnityAssetId ?? string.Empty,
                SubRoomUnityAssetId = scene?.StudioUnityAssetId ?? string.Empty,
                ReferencedUnityAssetIds = Array.Empty<string>(),
                DataBlob = h.BlobName,
                DataBlobName = h.BlobName,
                PersistenceVersion = 41,
                OMVersion = 0,
                SavedByAccountId = h.UploadedByPlayerId,
                SavedOnPlatform = 7,
                SavedOnDeviceClass = 2,
                SavedAt = (h.UploadedAt == default ? DateTime.UtcNow : h.UploadedAt)
                    .ToString("yyyy-MM-ddTHH:mm:ssZ"),
            });
        }

        return Ok(saves);
    }

    /// <summary>Build one subroom entry in the wire shape the watch's
    /// <c>SubRooms[]</c> array uses (see <c>BuildRoomServerDetails</c>). A
    /// null <paramref name="scene"/> yields the synthesised "Home" scene for
    /// single-scene rooms.</summary>
    private object BuildSubRoomWire(RoomEntity room, RoomSceneEntity? scene)
    {
        var fallback = CurrentOrSyntheticDataBlobName(room);
        var orderIndex = scene?.OrderIndex ?? 0;
        var dataBlob = scene is null
            ? fallback
            : SceneOrSyntheticDataBlobName(room, scene.DataBlobName, fallback);
        var modified = scene?.DataModifiedAt ?? room.UpdatedAt;
        return new
        {
            SubRoomId = (long)orderIndex,
            RoomId = room.Id,
            Name = scene?.Name ?? "Home",
            DataBlob = dataBlob,
            DataSavedAt = (modified == default ? DateTime.UtcNow : modified)
                .ToString("yyyy-MM-ddTHH:mm:ssZ"),
            IsSandbox = scene?.IsSandbox ?? false,
            MaxPlayers = scene?.MaxPlayers ?? 8,
            Accessibility = room.Accessibility,
            UnitySceneId = scene?.RoomSceneLocationId ?? room.LocationReplicationId,
            UnityAssetId = scene?.StudioUnityAssetId ?? string.Empty,
            SubRoomUnityAssetId = scene?.StudioUnityAssetId ?? string.Empty,
            CurrentSave = new
            {
                SubRoomDataSaveId = scene?.StudioSubRoomDataSaveId ?? 0L,
                SubRoomId = (long)orderIndex,
                UnityAssetId = scene?.StudioUnityAssetId ?? string.Empty,
                SubRoomUnityAssetId = scene?.StudioUnityAssetId ?? string.Empty,
                ReferencedUnityAssetIds = Array.Empty<string>(),
                DataBlob = dataBlob,
            },
        };
    }

    /// <summary>Build the <c>SubRoomDataSave</c> wire object for a Studio
    /// save — shared by <c>/saves/{saveId}</c>, <c>/data</c>, and the first
    /// entry of <c>/saves</c>. A null <paramref name="scene"/> (single-scene
    /// room) returns a save with no baked bundles, pointing at the room's
    /// current data blob.</summary>
    private object BuildStudioSaveWire(
        RoomEntity room,
        RoomSceneEntity? scene,
        long subRoomId,
        long saveId,
        int? unityAssetTarget,
        int? unityAssetVersion)
    {
        var bundles = StudioBundlesForScene(scene, saveId);

        if (bundles.Count == 0 && scene is not null)
        {
            logger.LogWarning(
                "[rooms-studio-save] no asset bundles room={RoomId} subRoom={SubRoomId} save={SaveId} scene={Scene} csvLen={CsvLen}",
                room.Id, subRoomId, saveId, scene.Name, scene.StudioAssetBundleNamesCsv?.Length ?? 0);
        }

        var unityAssetId = scene?.StudioUnityAssetId ?? string.Empty;
        var bakedAssets = BuildStudioBakedAssets(room, scene, saveId, unityAssetTarget, unityAssetVersion);
        var primary = bakedAssets.FirstOrDefault();
        var unityAsset = BuildStudioUnityAsset(room, scene, bakedAssets);
        var dataBlob = !string.IsNullOrWhiteSpace(scene?.DataBlobName)
            ? scene!.DataBlobName
            : CurrentOrSyntheticDataBlobName(room);

        logger.LogInformation(
            "[rooms-studio-save] room={RoomId} subRoom={SubRoomId} save={SaveId} scene={Scene} target={Target} version={Version} primary={Primary} bundles={BundleCount}",
            room.Id, subRoomId, saveId, scene?.Name ?? "Home", unityAssetTarget, unityAssetVersion,
            primary?.Filename ?? string.Empty, bundles.Count);

        return new
        {
            SubRoomDataSaveId = saveId,
            SubRoomId = subRoomId,
            UnityAssetId = unityAssetId,
            SubRoomUnityAssetId = unityAssetId,
            ReferencedUnityAssetIds = Array.Empty<string>(),
            DataBlob = dataBlob,
            DataBlobName = dataBlob,
            PersistenceVersion = 41,
            OMVersion = 0,
            SavedByAccountId = room.CreatorPlayerId,
            SavedOnPlatform = 7,
            SavedOnDeviceClass = 2,
            CreatedByAccountId = room.CreatorPlayerId,
            UnityAssetHash = string.Empty,
            UnityAsset = unityAsset,
            UnityAssetFilename = primary?.Filename ?? string.Empty,
            BakedUnityAssets = bakedAssets,
            UnitySubAssets = Array.Empty<object>(),
        };
    }

    /// <summary>POST <c>roomserver/rooms/{roomId}/subrooms/{subRoomId}/restoredata</c>
    /// — the watch's "Go" button in the Restore Room dialog. Body is
    /// form-urlencoded with a single <c>filename</c> field carrying the
    /// historical blob name from the datahistory list. <paramref name="subRoomId"/>
    /// is RoomSceneEntity.OrderIndex (same convention as the GET above).
    ///
    /// Effect: stamp the chosen scene's DataBlobName to the historical
    /// blob and, when restoring the room's current entry scene, also
    /// update the room-level CurrentDataBlobName so a fresh /v4/details
    /// returns the restored bytes too. Then push SubscriptionUpdateRoom
    /// so any clients already cached on the room reload the scene.
    ///
    /// Returns CreateModifyRoomSceneResponse — same shape as
    /// /api/rooms/v1/datahistory/restore — so the watch's
    /// ProcessCreateModifyRoomSceneResponse chain can deserialise and
    /// flip the Restore dialog out of its "applying" state.</summary>
    [HttpPost("roomserver/rooms/{roomId:long}/subrooms/{subRoomId:long}/restoredata")]
    [HttpPost("rooms/{roomId:long}/subrooms/{subRoomId:long}/restoredata")]
    [Authorize]
    public async Task<IActionResult> SubRoomRestoreData(
        long roomId, long subRoomId, [FromForm] string? filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return BadRequest(new { error = "missing_filename" });

        var pid = this.RequireCurrentPlayerId();
        var room = await rooms.GetByIdAsync(roomId);
        if (room is null) return NotFound(new { error = "room_not_found" });
        if (room.CreatorPlayerId != pid) return Forbid();

        var blob = await db.RoomDataBlobs
            .FirstOrDefaultAsync(b => b.BlobName == filename && b.RoomId == roomId);
        if (blob is null) return NotFound(new { error = "blob_not_found", filename });

        var sceneRows = await db.RoomScenes
            .Where(s => s.RoomId == roomId)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
        var sceneRow = sceneRows.FirstOrDefault(s => s.OrderIndex == (int)subRoomId);
        if (sceneRow is null) return NotFound(new { error = "subroom_not_found", subRoomId });

        var restoredAt = DateTime.UtcNow;
        sceneRow.DataBlobName = filename;
        sceneRow.DataModifiedAt = restoredAt;

        // Only stamp the room-level CurrentDataBlobName when restoring the
        // entry scene (OrderIndex 0). Restoring a non-entry sub-room
        // shouldn't rewrite what the room loads at the front door.
        if (sceneRow.OrderIndex == 0)
        {
            await db.Rooms
                .Where(r => r.Id == roomId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.CurrentDataBlobName, filename)
                    .SetProperty(r => r.UpdatedAt, restoredAt));
            room.CurrentDataBlobName = filename;
            room.UpdatedAt = restoredAt;
        }
        await db.SaveChangesAsync();

        var pushRoom = room.IsDormRoom
            ? CloneWithCreator(room, pid, room.CurrentDataBlobName)
            : CloneWithCreator(room, room.CreatorPlayerId, room.CurrentDataBlobName);
        var roomDetailsPayload = BuildRoomDetails(pushRoom, sceneRows, sceneRow.OrderIndex, filename);
        await notifications.NotifyAsync(pid, PushNotificationId.SubscriptionUpdateRoom, roomDetailsPayload);

        logger.LogInformation(
            "[rooms-restore] player={PlayerId} room={RoomId} subRoom={SubRoomId} scene={Scene} blob={Blob}",
            pid, roomId, subRoomId, sceneRow.Name, filename);

        // Watch's response deserializer is RecNetResult<PPENFJMFPNE> —
        // the same legacy success/value/error envelope used by
        // roomserver/rooms/{id}/subrooms/{id}/data. `value` must be the
        // flattened /roomserver/rooms/{id} DTO, not the modern
        // { Room, Scenes } details payload pushed over SubscriptionUpdateRoom.
        var roomServerDetails = BuildRoomServerDetails(pushRoom, sceneRows, filename);
        return Ok(new { success = true, value = roomServerDetails, error = string.Empty });
    }

    /// <summary>POST api/rooms/v4/saveData — the watch's
    /// <c>RecNet.Rooms.UploadLocalRoomSceneData</c> save flow.
    /// Body shape mirrors <c>RecNet.Rooms+SaveRoomSceneRequest</c>
    /// (Cpp2IL_CS RecNet/Rooms.cs:858): RoomSceneId, RoomDataFilename,
    /// InventionUsages[], CreatorActionContext, RequestPlayerId.
    ///
    /// Persists the new <c>CurrentDataBlobName</c> on the room
    /// (and, for dorms, the per-player DormStateEntity) so the next
    /// /v4/details + cdn fetch loads what just got uploaded. Returns
    /// a populated <c>RoomScene</c> JSON — that's what the watch's
    /// continuation callback (b__1 in DisplayClass124_0) expects;
    /// returning an empty body trips a "HTTP Error 404" path in
    /// BestHTTP's Future framework even though the HTTP status was
    /// fine.</summary>
    [HttpPost("api/rooms/v4/saveData")]
    [Authorize]
    public async Task<IActionResult> SaveData([FromBody] SaveRoomSceneRequest body)
        => await SaveDataCore(body);

    private async Task<IActionResult> SaveDataCore(
        SaveRoomSceneRequest body,
        long? routeRoomId = null,
        bool wrapCreateModifyResponse = false)
    {
        var pid = this.RequireCurrentPlayerId();

        // Resolve the room. The watch sends RoomSceneId (the scene's
        // OrderIndex per BuildRoomDetails above — 0 for single-scene
        // rooms). To find the parent room we cross-reference the
        // player's current presence (which RecordResponseAsync stamps
        // on every /goto). This is the only signal we have without a
        // RoomId on the request body.
        var current = presence.GetRoom(pid);
        if (current is null && routeRoomId is null)
        {
            // No active room — without it we can't pick a target row.
            // Return a degenerate RoomScene so the deserializer doesn't
            // throw, but flag CanMatchmakeInto=false so the watch
            // doesn't think the save succeeded.
            var missingPresenceScene = new
            {
                RoomSceneId = body.RoomSceneId,
                RoomId = 0L,
                RoomSceneLocationId = "76d98498-60a1-430c-ab76-b54a29b7a163",
                Name = "Home",
                IsSandbox = false,
                DataBlobName = body.RoomDataFilename ?? string.Empty,
                MaxPlayers = 8,
                CanMatchmakeInto = false,
                DataModifiedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            };
            return Ok(wrapCreateModifyResponse
                ? new { success = true, value = missingPresenceScene, error = string.Empty }
                : missingPresenceScene);
        }

        var roomId = routeRoomId ?? current!.RoomId;
        var room = await rooms.GetByIdAsync(roomId);
        if (room is null) return NotFound(new { error = "room_not_found" });

        var newBlob = body.EffectiveRoomDataFilename.Trim();
        if (string.IsNullOrWhiteSpace(newBlob))
        {
            logger.LogWarning(
                "[rooms-save] missing blob filename player={PlayerId} room={RoomId} requestedScene={RequestedSceneId} routeRoom={RouteRoomId}",
                pid, roomId, body.RoomSceneId, routeRoomId);
            return BadRequest(new { error = "missing_room_data_filename" });
        }
        var savedAt = DateTime.UtcNow;

        // Multi-scene rooms have a RoomScenes row per scene. The 2020
        // client usually sends the scene's OrderIndex, but some flows
        // send a stale/non-index id while the local room is still a
        // single-scene dorm/custom room. In that common single-scene
        // case, stamp the only row anyway; otherwise the pushed
        // RoomDetails can keep Scenes[0].DataBlobName at v(N) while
        // Rooms.CurrentDataBlobName is v(N+1), and the post-save reload
        // reads stale bytes.
        var sceneRowsForRoom = await db.RoomScenes
            .Where(s => s.RoomId == roomId)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
        var sceneRow = sceneRowsForRoom.FirstOrDefault(s => s.OrderIndex == (int)body.RoomSceneId)
            ?? (sceneRowsForRoom.Count == 1 ? sceneRowsForRoom[0] : null);

        // Dorms: per-player state, keyed by the calling player so two
        // people in the same dorm room ID save into separate slots.
        if (room.IsDormRoom)
        {
            if (room.CreatorPlayerId != pid) return Forbid();

            var dormState = await db.DormStates.FirstOrDefaultAsync(d => d.PlayerId == pid);
            if (dormState is null)
            {
                dormState = new DormStateEntity { PlayerId = pid };
                db.DormStates.Add(dormState);
            }
            dormState.CurrentDataBlobName = newBlob;
            dormState.UpdatedAt = savedAt;
        }
        else
        {
            var canSave = room.CreatorPlayerId == pid
                || await db.Players.AnyAsync(p => p.Id == pid && p.IsAdmin)
                || await db.RoomRoles.AnyAsync(r =>
                    r.RoomId == room.Id &&
                    r.PlayerId == pid &&
                    r.Accepted &&
                    r.Role == 0);
            if (!canSave) return Forbid();
        }

        if (sceneRow is not null)
        {
            sceneRow.DataBlobName = newBlob;
            sceneRow.DataModifiedAt = savedAt;
        }
        await db.Rooms
            .Where(r => r.Id == roomId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CurrentDataBlobName, newBlob)
                .SetProperty(r => r.UpdatedAt, savedAt));
        await db.SaveChangesAsync();
        room.CurrentDataBlobName = newBlob;
        room.UpdatedAt = savedAt;

        // The watch's MasterUploadRoomDataBlobCoroutine waits for BOTH
        // the /upload + /saveData responses AND a SubscriptionUpdateRoom
        // SignalR push before flipping the save state machine out of
        // SAVING. Without this push the spinner stays "Saving..." forever
        // even though both HTTP calls returned 200 — verified via the
        // RoomDetailsUpdatedCallback closure in
        // RoomPersistenceManager+<>c__DisplayClass174_0.
        //
        // For dorms we re-shape with the caller as creator + their own
        // DormStateEntity blob name so the watch's set_LocalRoomDetails
        // matches LocalRoomDetails.RoomId and triggers
        // SafeRaiseRoomDetailsUpdated(roomId).
        var pushRoom = room;
        if (room.IsDormRoom)
            pushRoom = CloneWithCreator(room, pid, newBlob);
        else
            pushRoom = CloneWithCreator(room, room.CreatorPlayerId, newBlob);
        foreach (var scene in sceneRowsForRoom)
        {
            if (sceneRow is not null && scene.Id == sceneRow.Id)
            {
                scene.DataBlobName = newBlob;
                scene.DataModifiedAt = savedAt;
            }
        }
        var savedSceneId = sceneRow?.OrderIndex ?? body.RoomSceneId;
        var roomDetailsPayload = BuildRoomDetails(pushRoom, sceneRowsForRoom, savedSceneId, newBlob);
        logger.LogInformation(
            "[rooms-save] saveData player={PlayerId} room={RoomId} requestedScene={RequestedSceneId} savedScene={SavedSceneId} blob={BlobName} sceneRow={SceneRowFound} scenes={SceneCount}",
            pid, roomId, body.RoomSceneId, savedSceneId, newBlob, sceneRow is not null, sceneRowsForRoom.Count);
        var activeInstanceId = current is not null && current.RoomId == roomId
            ? current.RoomInstanceId
            : 0L;
        var playersInInstance = activeInstanceId == 0
            ? new[] { pid }
            : onlinePresence.OnlinePlayerIds()
                .Where(playerId =>
                {
                    var playerRoom = presence.GetRoom(playerId);
                    return playerRoom is not null
                        && playerRoom.RoomId == roomId
                        && playerRoom.RoomInstanceId == activeInstanceId;
                })
                .Append(pid)
                .Distinct()
                .ToArray();
        logger.LogInformation(
            "[rooms-save] fanout room update player={PlayerId} room={RoomId} instance={InstanceId} blob={BlobName} recipients={RecipientCount}",
            pid, roomId, activeInstanceId, newBlob, playersInInstance.Length);
        if (activeInstanceId != 0)
        {
            var updatedPrivateInstances = await db.PrivateInstances
                .Where(instance => instance.Id == activeInstanceId && instance.RoomId == roomId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(instance => instance.DataBlob, newBlob));
            if (updatedPrivateInstances > 0)
            {
                logger.LogInformation(
                    "[rooms-save] updated private instance blob room={RoomId} instance={InstanceId} blob={BlobName}",
                    roomId, activeInstanceId, newBlob);
            }
        }

        var updatedRoomInstances = new Dictionary<long, RoomInstanceDto>();
        foreach (var playerId in playersInInstance)
        {
            var currentPresence = presence.GetRoom(playerId);
            if (currentPresence is null && playerId == pid && current is not null)
            {
                currentPresence = current;
            }
            if (currentPresence is null || currentPresence.RoomId != roomId) continue;

            currentPresence.DataBlob = newBlob;
            presence.SetRoom(playerId, currentPresence);
            updatedRoomInstances[playerId] = currentPresence;
        }

        // SubscriptionUpdateRoom delivers the new RoomDetails (with
        // Scenes[0].DataBlobName = newBlob). Rooms.OnSubscriptionUpdateRoom
        // → StoreRoomDetailsInCache → set_LocalRoomDetails iterates
        // Scenes for one matching RoomSceneId == LocalSubRoomId and writes
        // LocalRoomScene to that scene, which is what DownloadRoomData
        // BlobCoroutine reads when PostSaveReloading runs.
        foreach (var playerId in playersInInstance)
        {
            await notifications.NotifyAsync(playerId, PushNotificationId.SubscriptionUpdateRoom, roomDetailsPayload);
        }

        // RoomInstanceUpdate keeps the matchmaking-side dataBlob and any
        // friends-list "in room X" indicators in sync. No nudge — live
        // doesn't send one, and the fake sub-room id confuses the watch's
        // OnLocalGameSessionUpdated scene re-resolve.
        foreach (var (playerId, currentPresence) in updatedRoomInstances)
        {
            await notifications.NotifyAsync(
                playerId,
                PushNotificationId.SubscriptionUpdateGameSession,
                currentPresence);
        }

        // PresenceUpdate keeps the next heartbeat aligned with the blob
        // applied through the room-details push above.
        foreach (var (playerId, currentPresence) in updatedRoomInstances)
        {
            var presencePayload = new
            {
                playerId = (int)playerId,
                statusVisibility = 1,
                deviceClass = 0,
                vrMovementMode = 0,
                roomInstance = new
                {
                    roomInstanceId   = currentPresence.RoomInstanceId,
                    roomId           = currentPresence.RoomId,
                    subRoomId        = currentPresence.SubRoomId,
                    roomInstanceType = currentPresence.RoomInstanceType,
                    location         = currentPresence.Location,
                    dataBlob         = currentPresence.DataBlob,
                    eventId          = currentPresence.EventId,
                    clubId           = currentPresence.ClubId,
                    roomCode         = currentPresence.RoomCode,
                    photonRegionId   = currentPresence.PhotonRegionId,
                    photonRoomId     = currentPresence.PhotonRoomId,
                    name             = currentPresence.NameWire,
                    maxCapacity      = currentPresence.MaxCapacity,
                    isFull           = currentPresence.IsFull,
                    isPrivate        = currentPresence.IsPrivate,
                    isInProgress     = currentPresence.IsInProgress,
                    EncryptVoiceChat = currentPresence.EncryptVoiceChat,
                },
                isOnline = true,
                appVersion = 20201210,
            };
            await notifications.NotifyAsync(playerId, PushNotificationId.SubscriptionUpdatePresence, presencePayload);
        }

        var savedScene = new
        {
            RoomSceneId = savedSceneId,
            RoomId = roomId,
            RoomSceneLocationId = sceneRow?.RoomSceneLocationId ?? room.LocationReplicationId,
            Name = sceneRow?.Name ?? "Home",
            IsSandbox = sceneRow?.IsSandbox ?? false,
            DataBlobName = newBlob,
            MaxPlayers = sceneRow?.MaxPlayers ?? 8,
            CanMatchmakeInto = sceneRow?.CanMatchmakeInto ?? true,
            DataModifiedAt = savedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };
        if (wrapCreateModifyResponse)
        {
            var roomServerDetails = BuildRoomServerDetails(pushRoom, sceneRowsForRoom, newBlob);
            return Ok(new { success = true, value = roomServerDetails, error = string.Empty });
        }

        return Ok(savedScene);
    }

    public class SaveRoomSceneRequest
    {
        public long RoomSceneId { get; set; }
        public string? RoomDataFilename { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("filename")]
        public string? Filename { get; set; }
        public List<long>? InventionUsages { get; set; }
        public string? InventionUsageBase64 { get; set; }
        public CreatorActionContextDto? CreatorActionContext { get; set; }
        public long RequestPlayerId { get; set; }
        public int SaveRequestPlayerId { get; set; }

        public string EffectiveRoomDataFilename =>
            RoomDataFilename ?? Filename ?? string.Empty;
    }

    public class CreatorActionContextDto
    {
        public bool IsTeachableMomentRunning { get; set; }
    }

    /// <summary>POST api/rooms/v1/restore — switch a room back to a
    /// previous save. Body: roomId, blobName (or version id). Updates
    /// the player's DormStateEntity (for dorms) or RoomEntity
    /// CurrentDataBlobName (for owned rooms) so the next visit loads
    /// the historical bytes.</summary>
    /// <summary>POST <c>api/rooms/v1/datahistory/restore</c> — restore a
    /// room to a previous save.
    ///
    /// Request body: <c>RestoreDataHistoryRequest</c> serialized by
    /// Unity's <c>JsonUtility.ToJson</c>, which uses C# field names.
    /// Verified at <c>Cpp2IL_ISIL/.../Rooms.txt:13513-14</c> — the only
    /// field on the request type is the long history id at offset +16,
    /// and Unity's serializer names it <c>roomDataHistoryId</c>. We
    /// look up <see cref="RoomDataBlobEntity"/> by that id, find its
    /// parent room, and rewrite <c>CurrentDataBlobName</c> (and the
    /// dorm-state row for personal dorms) so the next room reload
    /// downloads the historical bytes.
    ///
    /// Response: <c>CreateModifyRoomSceneResponse</c> — keys are
    /// PascalCase per <c>Cpp2IL_ISIL/.../Rooms_NestedType_CreateModifyRoomSceneResponse.txt</c>:
    /// <c>Result</c> (int, CreateModifyRoomStatus: 0=Success), and
    /// <c>RoomScene</c> (the restored scene). The watch's
    /// <c>ProcessCreateModifyRoomSceneResponse</c> chain expects both;
    /// returning a bare <c>{restored: "..."}</c> like the old impl makes
    /// the watch's deserializer throw and the restore silently fails.
    /// The legacy form-urlencoded path stays under
    /// <c>/api/rooms/v1/restore</c> for the admin SPA's restore UI.
    /// </summary>
    public sealed class RestoreDataHistoryRequest
    {
        // Unity JsonUtility serializes C# private backing fields by
        // their declared name. The watch's request type has a single
        // field which Unity emits as "roomDataHistoryId" (camelCase) —
        // [JsonPropertyName] here matches that wire key explicitly so
        // System.Text.Json picks it up regardless of any future global
        // naming policy.
        [System.Text.Json.Serialization.JsonPropertyName("roomDataHistoryId")]
        public long RoomDataHistoryId { get; set; }
    }

    [HttpPost("api/rooms/v1/datahistory/restore")]
    [Authorize]
    public async Task<IActionResult> RestoreDataHistory([FromBody] RestoreDataHistoryRequest body)
    {
        var pid = this.RequireCurrentPlayerId();
        var blob = await db.RoomDataBlobs.FirstOrDefaultAsync(b => b.Id == body.RoomDataHistoryId);
        if (blob is null) return NotFound(new { error = "version_not_found" });

        var room = await rooms.GetByIdAsync(blob.RoomId);
        if (room is null) return NotFound(new { error = "room_not_found" });

        // Ownership: only the dorm owner / room creator can restore.
        // Admin override is intentionally NOT included here — restore
        // is destructive (overrides the player's chosen state) so we
        // require the actual creator.
        if (room.CreatorPlayerId != pid)
            return Forbid();

        if (room.IsDormRoom)
        {
            var dormState = await db.DormStates.FirstOrDefaultAsync(d => d.PlayerId == pid);
            if (dormState is null)
            {
                dormState = new DormStateEntity { PlayerId = pid };
                db.DormStates.Add(dormState);
            }
            dormState.CurrentDataBlobName = blob.BlobName;
            dormState.UpdatedAt = DateTime.UtcNow;
        }
        await db.Rooms
            .Where(r => r.Id == room.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CurrentDataBlobName, blob.BlobName)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));
        await db.SaveChangesAsync();

        // Push the same SubscriptionUpdateRoom the save flow sends so
        // the watch's StoreRoomDetailsInCache → SafeRaiseRoomDetailsUpdated
        // chain fires and any open room-detail panels refresh.
        var pushRoom = room.IsDormRoom ? CloneWithCreator(room, pid, blob.BlobName) : CloneWithCreator(room, room.CreatorPlayerId, blob.BlobName);
        var allScenes = await db.RoomScenes
            .Where(s => s.RoomId == room.Id)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
        var roomDetailsPayload = BuildRoomDetails(pushRoom, allScenes, 0L, blob.BlobName);
        await notifications.NotifyAsync(pid, PushNotificationId.SubscriptionUpdateRoom, roomDetailsPayload);

        // CreateModifyRoomSceneResponse shape. RoomScene fields match
        // RecNet.RoomScene.Deserialize (PascalCase, RoomSceneId,
        // RoomId, RoomSceneLocationId, Name, IsSandbox, DataBlobName,
        // MaxPlayers, CanMatchmakeInto, DataModifiedAt).
        return Ok(new
        {
            Result = 0, // CreateModifyRoomStatus.Success
            RoomScene = new
            {
                RoomSceneId = 0L,
                RoomId = room.Id,
                RoomSceneLocationId = room.LocationReplicationId,
                Name = room.Name,
                IsSandbox = false,
                DataBlobName = blob.BlobName,
                MaxPlayers = 8,
                CanMatchmakeInto = true,
                DataModifiedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            },
        });
    }

    /// <summary>Legacy form-urlencoded restore — kept for the admin SPA's
    /// "Restore version" picker which posts {roomId, blobName} fields.
    /// New callers (the watch) use the JSON variant above.</summary>
    [HttpPost("api/rooms/v1/restore")]
    [Authorize]
    public async Task<IActionResult> RestoreVersionLegacy(
        [FromForm] long roomId,
        [FromForm] string? blobName,
        [FromForm] long version = 0)
    {
        var pid = this.RequireCurrentPlayerId();
        var room = await rooms.GetByIdAsync(roomId);
        if (room is null) return NotFound(new { error = "room_not_found" });

        var blob = !string.IsNullOrEmpty(blobName)
            ? await db.RoomDataBlobs.FirstOrDefaultAsync(b => b.BlobName == blobName && b.RoomId == roomId)
            : await db.RoomDataBlobs.FirstOrDefaultAsync(b => b.Id == version && b.RoomId == roomId);
        if (blob is null) return NotFound(new { error = "version_not_found" });

        if (room.CreatorPlayerId != pid)
            return Forbid();

        if (room.IsDormRoom)
        {
            var dormState = await db.DormStates.FirstOrDefaultAsync(d => d.PlayerId == pid);
            if (dormState is null)
            {
                dormState = new DormStateEntity { PlayerId = pid };
                db.DormStates.Add(dormState);
            }
            dormState.CurrentDataBlobName = blob.BlobName;
            dormState.UpdatedAt = DateTime.UtcNow;
        }
        await db.Rooms
            .Where(r => r.Id == roomId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CurrentDataBlobName, blob.BlobName)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));
        await db.SaveChangesAsync();
        return Ok(new { restored = blob.BlobName });
    }

    /// <summary>GET <c>api/rooms/v{1,2}/personaldetails/{roomId}</c> —
    /// per-caller bookmark/cheer flags for one room. Wire shape verified
    /// against <c>RecNet.PersonalRoomDetails.Deserialize</c>
    /// (<c>Cpp2IL_ISIL/.../PersonalRoomDetails.txt:85-91</c>):
    ///   <c>IsCheering</c>  - bool, REQUIRED (Util.GetKey, throws if missing)
    ///   <c>IsBookmarked</c> - bool, REQUIRED
    /// Both keys are PascalCase. Returning the full room-details payload
    /// here (which is what we used to do) makes the deserialiser throw
    /// KeyNotFoundException for IsCheering, and that blocks the entire
    /// /goto/* flow because RecNet.Rooms.GetPersonalRoomDetails is
    /// awaited before SessionManager.JoinRoom can run.</summary>
    [HttpGet("api/rooms/v2/personaldetails/{roomId:long}")]
    [HttpGet("api/rooms/v1/personaldetails/{roomId:long}")]
    public async Task<IActionResult> PersonalDetails(long roomId)
    {
        var pid = CurrentPlayerId ?? 0;
        bool cheering = false, bookmarked = false;
        if (pid > 0)
        {
            cheering = await db.Cheers.AnyAsync(c => c.TargetRoomId == roomId && c.FromPlayerId == pid);
            bookmarked = await db.RoomBookmarks.AnyAsync(b => b.RoomId == roomId && b.PlayerId == pid);
        }
        return Ok(new { IsCheering = cheering, IsBookmarked = bookmarked });
    }

    /// <summary>
    /// GET <c>/roomserver/rooms/{id}/interactionby/me</c> — sibling of
    /// personaldetails but DIFFERENT wire shape. Per
    /// <c>EJDCNGBEICB.txt:8219,8266</c> the deserialiser is
    /// <c>CJODCLDGFCF</c>, which reads <c>Cheered</c> + <c>Favorited</c>
    /// (PascalCase, both bool, both required — verified at
    /// <c>CJODCLDGFCF.txt:118,123</c>). Sharing the
    /// <c>{IsCheering, IsBookmarked}</c> payload with personaldetails
    /// throws <c>KeyNotFoundException</c> on the watch and stalls the
    /// room-tile UI.
    /// </summary>
    [HttpGet("roomserver/rooms/{roomId:long}/interactionby/me")]
    [HttpGet("rooms/{roomId:long}/interactionby/me")]
    public async Task<IActionResult> RoomInteractionByMe(long roomId)
    {
        var pid = CurrentPlayerId ?? 0;
        bool cheered = false, favorited = false;
        if (pid > 0)
        {
            cheered = await db.Cheers.AnyAsync(c => c.TargetRoomId == roomId && c.FromPlayerId == pid);
            favorited = await db.RoomBookmarks.AnyAsync(b => b.RoomId == roomId && b.PlayerId == pid);
        }
        return Ok(new { Cheered = cheered, Favorited = favorited });
    }

    /// <summary>
    /// GET <c>/roomserver/playlists/{id}/interactionby/me</c> — same
    /// <c>CJODCLDGFCF</c> wire shape as the room variant. Per
    /// <c>EJDCNGBEICB.txt:8600</c> the URL is
    /// <c>"playlists/{0}/interactionby/me"</c>. Reads from
    /// <see cref="PlaylistInteractionEntity"/>; an anonymous caller
    /// (no JWT) reads as (false, false) so the watch's pre-login
    /// browse renders cleanly.
    /// </summary>
    [HttpGet("roomserver/playlists/{playlistId:long}/interactionby/me")]
    [HttpGet("playlists/{playlistId:long}/interactionby/me")]
    public async Task<IActionResult> PlaylistInteractionByMe(long playlistId)
    {
        var pid = CurrentPlayerId ?? 0;
        if (pid <= 0) return Ok(new { Cheered = false, Favorited = false });
        var (cheered, favorited) = await playlists.InteractionForAsync(playlistId, pid);
        return Ok(new { Cheered = cheered, Favorited = favorited });
    }

    [HttpGet("api/rooms/v4/details/{roomId:long}")]
    [HttpGet("api/rooms/v3/details/{roomId:long}")]
    [HttpGet("roomserver/rooms/{roomId:long}")]
    public async Task<IActionResult> Details(long roomId)
    {
        var r = await rooms.GetByIdAsync(roomId);
        var room = r ?? Synthetic($"Room_{roomId}", roomId);

        // Dorm-specific shaping: each player has their own first-class
        // dorm row. Only the dorm's actual creator should see Creator
        // role / Maker Pen. Guests loading someone else's dorm must see
        // the owner as CreatorPlayerId, while still receiving the
        // owner's DormStateEntity blob so they load the same room state.
        if (room.IsDormRoom)
        {
            room = await ShapeDormRoomAsync(room);
        }

        var scenes = await db.RoomScenes
            .Where(s => s.RoomId == room.Id)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
        var roomRoles = await db.RoomRoles
            .Where(r => r.RoomId == room.Id)
            .ToListAsync();
        if (Request.Path.StartsWithSegments("/roomserver", StringComparison.OrdinalIgnoreCase))
            return Ok(BuildRoomServerDetails(room, scenes, roles: roomRoles));

        return Ok(BuildRoomDetails(room, scenes, roles: roomRoles));
    }

    [HttpGet("roomserver/rooms")]
    [HttpGet("rooms")]
    public async Task<IActionResult> RoomServerDetailsByName([FromQuery(Name = "name")] string? roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName))
            return Ok(Array.Empty<object>());

        var pid = CurrentPlayerId ?? 0;
        RoomEntity? room;
        if (roomName.Equals("DormRoom", StringComparison.OrdinalIgnoreCase) && pid > 0)
        {
            room = await rooms.EnsurePersonalDormAsync(pid);
        }
        else
        {
            room = await rooms.GetByNameAsync(roomName);
        }

        room ??= Synthetic(roomName);
        if (room.IsDormRoom)
        {
            // Stamp the wire Name with the requested name so the watch's
            // name-keyed room cache (OJMCBOKJFOF.NHBPIIGDAJP) can find it.
            room = await ShapeDormRoomAsync(room, DormNameOverride(roomName));
        }

        var scenes = await db.RoomScenes
            .Where(s => s.RoomId == room.Id)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
        var roomRoles = await db.RoomRoles
            .Where(r => r.RoomId == room.Id)
            .ToListAsync();

        return Ok(BuildRoomServerDetails(room, scenes, roles: roomRoles));
    }

    /// <summary>GET <c>/roomserver/rooms/bulk?name=X&amp;name=Y</c> or
    /// <c>?id=1&amp;id=2</c> — bulk room cache lookup. Decomp-verified
    /// in EJDCNGBEICB: the Int64 overload writes query key <c>id</c>
    /// and the string overload writes <c>name</c>. Returns the same
    /// per-room object that <c>/roomserver/rooms/{id}</c> emits;
    /// unresolved entries are silently skipped (watch handles a shorter
    /// list fine, but a 404 wedges the room browser).</summary>
    [HttpGet("roomserver/rooms/bulk")]
    [HttpGet("rooms/bulk")]
    public async Task<IActionResult> RoomServerBulkByName()
    {
        var names = Request.Query["name"].Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
        var ids = Request.Query["id"]
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .SelectMany(v => v!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => long.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        if (names.Length == 0 && ids.Length == 0) return Ok(Array.Empty<object>());

        var pid = CurrentPlayerId ?? 0;
        var results = new List<object>(names.Length + ids.Length);
        var emitted = new HashSet<long>();
        foreach (var raw in names)
        {
            var name = raw!.Trim();
            RoomEntity? room;
            if (name.Equals("DormRoom", StringComparison.OrdinalIgnoreCase) && pid > 0)
                room = await rooms.EnsurePersonalDormAsync(pid);
            else
                room = await rooms.GetByNameAsync(name);

            room ??= Synthetic(name);
            if (room.IsDormRoom)
            {
                // Stamp the wire Name with the requested name so the watch's
                // name-keyed room cache (OJMCBOKJFOF.NHBPIIGDAJP) finds it.
                room = await ShapeDormRoomAsync(room, DormNameOverride(name));
            }

            var scenes = await db.RoomScenes
                .Where(s => s.RoomId == room.Id)
                .OrderBy(s => s.OrderIndex)
                .ToListAsync();
            var roomRoles = await db.RoomRoles
                .Where(r => r.RoomId == room.Id)
                .ToListAsync();
            results.Add(BuildRoomServerDetails(room, scenes, roles: roomRoles));
            emitted.Add(room.Id);
        }

        foreach (var id in ids)
        {
            if (!emitted.Add(id)) continue;

            var room = await rooms.GetByIdAsync(id);
            if (room is null) continue;

            if (room.IsDormRoom)
            {
                room = await ShapeDormRoomAsync(room);
            }

            var scenes = await db.RoomScenes
                .Where(s => s.RoomId == room.Id)
                .OrderBy(s => s.OrderIndex)
                .ToListAsync();
            var roomRoles = await db.RoomRoles
                .Where(r => r.RoomId == room.Id)
                .ToListAsync();
            results.Add(BuildRoomServerDetails(room, scenes, roles: roomRoles));
        }
        return Ok(results);
    }

    /// <summary>
    /// Shallow clone of a RoomEntity with a different CreatorPlayerId
    /// and (optionally) a different CurrentDataBlobName. Used to give
    /// the per-caller dorm the appearance of being authored by the
    /// caller and pointing at the caller's own most recent save.
    /// Doesn't touch the DB — purely a response-shaping utility.
    /// </summary>
    /// <summary>When a dorm was resolved via the magic name "DormRoom",
    /// returns that requested name (verbatim, so casing matches what the
    /// watch will look the cache up by) to stamp onto the wire room.
    /// Returns null for any other request name so the dorm keeps its real
    /// "Dorm_{playerId}" name in by-id / direct-name responses.</summary>
    private static string? DormNameOverride(string requestedName) =>
        requestedName.Equals("DormRoom", StringComparison.OrdinalIgnoreCase)
            ? requestedName
            : null;

    private async Task<RoomEntity> ShapeDormRoomAsync(RoomEntity room, string? overrideName = null)
    {
        var dormBlobName = await rooms.ResolveDormDataBlobNameAsync(room.CreatorPlayerId, room.Id);
        var dormName = overrideName ?? await rooms.BuildPersonalDormDisplayNameAsync(room.CreatorPlayerId);
        return CloneWithCreator(room, room.CreatorPlayerId, dormBlobName, dormName);
    }

    public static RoomEntity CloneWithCreator(
        RoomEntity src, long creatorId, string? overrideBlobName = null,
        string? overrideName = null) => new()
    {
        Id = src.Id,
        // overrideName lets the by-name resolvers stamp the wire Name with
        // the name the client actually requested. The personal-dorm entity
        // is named "Dorm_{playerId}", but the watch resolves it via the
        // magic name "DormRoom" (HomeScreenFlow.Button_DormRoom →
        // RunJoinRoom("DormRoom") → OJMCBOKJFOF.NHBPIIGDAJP("DormRoom")).
        // That call caches the response in a Dictionary<string,Room> and
        // looks it back up by the REQUESTED string ("DormRoom"); if the
        // wire Name is "Dorm_{id}" the lookup misses → the promise rejects
        // with "No such room" → the "contact recroom.happyfox.com" toast.
        // Stamping Name="DormRoom" here makes the lookup hit, the same way
        // RecCenter (whose entity Name IS "RecCenter") already works.
        Name = overrideName ?? src.Name,
        Description = src.Description,
        CreatorPlayerId = creatorId,
        ImageName = src.ImageName,
        State = src.State,
        Accessibility = src.Accessibility,
        SupportsLevelVoting = src.SupportsLevelVoting,
        IsAGRoom = src.IsAGRoom,
        IsDormRoom = src.IsDormRoom,
        CloningAllowed = src.CloningAllowed,
        SupportsVRLow = src.SupportsVRLow,
        SupportsMobile = src.SupportsMobile,
        SupportsScreens = src.SupportsScreens,
        SupportsWalkVR = src.SupportsWalkVR,
        SupportsTeleportVR = src.SupportsTeleportVR,
        AllowsJuniors = src.AllowsJuniors,
        AllowNewUsers = src.AllowNewUsers,
        MinLevel = src.MinLevel,
        MaxPlayerCalculationMode = src.MaxPlayerCalculationMode,
        LoadScreensJson = src.LoadScreensJson,
        PromoImagesJson = src.PromoImagesJson,
        PromoExternalContentJson = src.PromoExternalContentJson,
        RoomWarningMask = src.RoomWarningMask,
        CustomRoomWarning = src.CustomRoomWarning,
        DisableMicAutoMute = src.DisableMicAutoMute,
        LocationReplicationId = src.LocationReplicationId,
        IsStudioRoom = src.IsStudioRoom,
        IsRoomLinkedToRecRoomStudio = src.IsRoomLinkedToRecRoomStudio,
        StudioSessionId = src.StudioSessionId,
        TagsCsv = src.TagsCsv,
        CheerCount = src.CheerCount,
        FavoriteCount = src.FavoriteCount,
        VisitCount = src.VisitCount,
        HotScore = src.HotScore,
        CreatedAt = src.CreatedAt,
        UpdatedAt = src.UpdatedAt,
        CurrentDataBlobName = overrideBlobName ?? src.CurrentDataBlobName,
    };

    /// <summary>
    /// Wire shape for the RoomDetails object — matches RoomDetails.Deserialize
    /// at RVA 0x114C580. JSON key names DO NOT necessarily match the C#
    /// property/field names from dump.cs — the deserializer is hand-written
    /// and uses different (mostly PascalCase) literal strings. Verified
    /// empirically: this exact shape gets past Deserialize successfully.
    /// </summary>
    public static object BuildRoomDetails(
        RoomEntity room,
        IReadOnlyList<RoomSceneEntity>? sceneRows = null,
        long? overrideSceneId = null,
        string? overrideDataBlobName = null,
        IReadOnlyList<RoomRoleEntity>? roles = null)
    {
        // Tags is a list of OBJECTS, not strings. RoomDetails.Deserialize
        // does Util.DeserializeList<RoomTag>(tags) — each entry is parsed as
        // a Dictionary with `Type` (int RoomTagType: 0=Auto, 1=PlayerAdded,
        // 2=AG) and `Tag` (string). Sending bare strings crashes:
        //   InvalidCastException: Unable to cast 'String' to 'Dictionary`2'
        var tags = string.IsNullOrEmpty(room.TagsCsv)
            ? Array.Empty<object>()
            : room.TagsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => (object)new
                {
                    Type = RoomTagTypeForWire(t),
                    Tag = t,
                    IsPrimaryGenre = false,
                })
                .ToArray();

        // Scenes population: every room must produce a Scenes[0] entry
        // with RoomSceneId=0 (matching the matchmaking SubRoomId=0) so
        // that Rooms.set_LocalRoomDetails actually transitions
        // Rooms.LocalRoomScene to the new room's scene reference.
        //
        // Why every room: the setter (verified in Cpp2IL ISIL of
        // RecNet.Rooms.set_LocalRoomDetails) iterates Scenes looking
        // for a RoomSceneId match; if none matches (Scenes=[] or
        // mismatched), it falls through WITHOUT writing LocalRoomScene
        // — so the field stays stale at whatever the previous room set.
        // After the dorm fix landed, that stale value started leaking
        // into the rec center: the dorm sets LocalRoomScene → user
        // goes to rec center → if we send Scenes=[], LocalRoomScene
        // STAYS pointing at the dorm scene → DownloadRoomDataBlob
        // Coroutine reads scene.DataBlobName from the dorm and
        // downloads room_1_v1.dat (smoking gun in the server log)
        // during the rec center load. The rec center master init
        // blocks → FROSTBITE.
        //
        // The DataBlobName field is what determines whether the
        // persistence coroutine actually downloads anything.
        // OnRoomDataBlobNameChanged (RoomPersistenceManager.txt ISIL
        // {027-040}) short-circuits when DataBlobName is empty: it
        // sets GetRoomDataBlobPromise to a CompletedPromise<byte[]>
        // with empty bytes and returns immediately — no HTTP fetch,
        // no DeserializeRoomDataBlobCoroutine, the master init
        // proceeds with default-zero permissions.
        //
        // If a room has a saved blob, use it. Otherwise point at
        // a synthetic CDN key, which misses S3 and is served by
        // RoomDataBlobService.GetDefaultBlob. That default is a captured
        // PersistedRoomData (v38); the older synthetic role-only blob and
        // empty DataBlobName path both leave the 2023 room-permissions
        // runtime without data it dereferences during spawn.
        var roleList = roles ?? Array.Empty<RoomRoleEntity>();
        var dataBlobName = CurrentOrSyntheticDataBlobName(room);

        // Multi-scene rooms (imported via tools/import-room.ps1) carry one
        // RoomSceneEntity row per sub-room. Emit the full Scenes[] array
        // when present so cross-scene portals (Lobby→Ch1, etc.) can
        // resolve. Single-scene rooms (every legacy room, every dorm,
        // every AG-Original) fall back to the synthesised Scenes[0] from
        // RoomEntity fields.
        object[] scenes;
        if (sceneRows is { Count: > 0 })
        {
            scenes = sceneRows
                .Select(s => (object)new
                {
                    RoomSceneId = (long)s.OrderIndex,
                    RoomId = room.Id,
                    RoomSceneLocationId = s.RoomSceneLocationId,
                    Name = s.Name,
                    IsSandbox = s.IsSandbox,
                    DataBlobName = !string.IsNullOrWhiteSpace(overrideDataBlobName) &&
                        (overrideSceneId == s.OrderIndex || sceneRows.Count == 1)
                        ? overrideDataBlobName
                        : SceneOrSyntheticDataBlobName(room, s.DataBlobName, dataBlobName),
                    MaxPlayers = s.MaxPlayers,
                    CanMatchmakeInto = s.CanMatchmakeInto,
                    DataModifiedAt = (s.DataModifiedAt == default ? DateTime.UtcNow : s.DataModifiedAt)
                        .ToString("yyyy-MM-ddTHH:mm:ssZ"),
                })
                .ToArray();
        }
        else
        {
            scenes = new object[]
            {
                new
                {
                    RoomSceneId = 0L,                    // MUST match RoomInstance.SubRoomId
                    RoomId = room.Id,
                    RoomSceneLocationId = room.LocationReplicationId,
                    Name = "Home",
                    IsSandbox = false,
                    DataBlobName = dataBlobName,
                    MaxPlayers = room.MaxCapacity,
                    CanMatchmakeInto = true,
                    // Use the room's actual UpdatedAt so the watch's
                    // "is my local cache stale?" check sees an accurate
                    // freshness timestamp. Hardcoding the build date
                    // (2020-03-06) made the watch flag every dorm visit
                    // as "room is not up to date" because the cached
                    // local copy was always newer than the server's
                    // declared timestamp.
                    DataModifiedAt = (room.UpdatedAt == default ? DateTime.UtcNow : room.UpdatedAt)
                        .ToString("yyyy-MM-ddTHH:mm:ssZ"),
                },
            };
        }

        // Owners tile (UIRoomDetails bottom-left) reads CoOwners — the
        // 2020 RoomDetails.Deserialize reads keys WITHOUT the "Ids"
        // suffix (verified at RoomDetails.txt:1096/1141/1146/1191/1196:
        // "CoOwners" / "InvitedCoOwners" / "Moderators" /
        // "InvitedModerators" / "Hosts" / "InvitedHosts"). The
        // GetListKey calls are strict — wrong names throw
        // KeyNotFoundException → "Failed to get room details:
        // Malformed Response" → dorm load stalls in a redirect loop.
        //
        // CreatorPlayerId is always implicitly a CoOwner so RR Originals
        // surface Coach (id=1) as the owner instead of rendering blank.
        // Additional grants come from RoomRoleEntity rows when supplied;
        // callers without role data (clone/myrooms/etc.) ship just the
        // implicit creator.
        int[] PlayersIn(int role, bool accepted) => roleList
            .Where(r => r.Role == role && r.Accepted == accepted)
            .Select(r => (int)r.PlayerId)
            .ToArray();
        var coOwnerSet = new HashSet<int> { (int)room.CreatorPlayerId };
        foreach (var pid in PlayersIn(0, true)) coOwnerSet.Add(pid);
        return new
        {
            Room = RoomService.ToWireRoom(room),
            Scenes = scenes,
            CoOwners = coOwnerSet.ToArray(),
            InvitedCoOwners = PlayersIn(0, false),
            Moderators = PlayersIn(1, true),
            InvitedModerators = PlayersIn(1, false),
            Hosts = PlayersIn(2, true),
            InvitedHosts = PlayersIn(2, false),
            CheerCount = room.CheerCount,
            FavoriteCount = room.FavoriteCount,
            VisitCount = room.VisitCount,
            // VisitorCount is the official Rec.Net "unique visitors"
            // companion field (see Stats payload at apim.rec.net).
            // 2020 RoomDetails.Deserialize doesn't read it but leaving
            // it on the wire is harmless and lets the admin UI / any
            // future client surface the number.
            VisitorCount = room.VisitorCount,
            Tags = tags,
        };
    }

    /// <summary>
    /// 2020.12 <c>/roomserver/rooms/{id}?include=301</c> shape. The client
    /// deserializes this directly as PPENFJMFPNE, not as the older
    /// { Room, Scenes, ... } wrapper.
    /// </summary>
    public static object BuildRoomServerDetails(
        RoomEntity room,
        IReadOnlyList<RoomSceneEntity>? sceneRows = null,
        string? overrideDataBlobName = null,
        IReadOnlyList<RoomRoleEntity>? roles = null,
        int? unityAssetTarget = null,
        int? unityAssetVersion = null)
    {
        var roleList = roles ?? Array.Empty<RoomRoleEntity>();
        // 2023's room-permissions runtime needs a real PersistedRoomData
        // source during spawn. If no saved blob exists, use the captured
        // default via the synthetic CDN path.
        var dataBlobName = CurrentOrSyntheticDataBlobName(room);
        var updatedAt = (room.UpdatedAt == default ? DateTime.UtcNow : room.UpdatedAt)
            .ToString("yyyy-MM-ddTHH:mm:ssZ");

        object[] subRooms = sceneRows is { Count: > 0 }
            ? sceneRows.Select(s =>
            {
                var dataBlob = !string.IsNullOrWhiteSpace(overrideDataBlobName) && sceneRows.Count == 1
                    ? overrideDataBlobName
                    : SceneOrSyntheticDataBlobName(room, s.DataBlobName, dataBlobName);
                // A scene with a baked Studio asset (non-empty UnityAssetId)
                // must advertise a Studio-era PersistenceVersion on its
                // CurrentSave. The 2023 client gates the baked-Addressables
                // load path on this: a version of 0 (the old default) makes
                // it treat the save as a pre-baked legacy blob and it never
                // fetches unity_assets/{id}/{target}/{version}, so the
                // persistence views reference prefabs that were never loaded
                // ("Expected a prefab, but found none"). 41 matches the real
                // RecRocks export (room.json PersistenceVersion=41).
                var baked = !string.IsNullOrWhiteSpace(s.StudioUnityAssetId);
                var saveVersion = baked ? StudioPersistenceVersion : 0;
                var bakedAssets = BuildStudioBakedAssets(
                    room,
                    s,
                    s.StudioSubRoomDataSaveId ?? 0L,
                    unityAssetTarget,
                    unityAssetVersion);
                var unityAsset = bakedAssets.FirstOrDefault();
                var unityAssetParent = BuildStudioUnityAsset(room, s, bakedAssets);
                return (object)new
                {
                    ReplicationId = s.RoomSceneLocationId,
                    PersistenceVersion = saveVersion,
                    SupportsJoinInProgress = true,
                    UseLevelBasedMatchmaking = false,
                    UseAgeBasedMatchmaking = false,
                    UseRecRoyaleMatchmaking = false,
                    SubRoomId = (long)s.OrderIndex,
                    RoomId = room.Id,
                    Name = s.Name,
                    UnityAssetId = s.StudioUnityAssetId,
                    SubRoomUnityAssetId = s.StudioUnityAssetId,
                    UnityAsset = unityAsset?.Filename ?? string.Empty,
                    UnityAssetHash = string.Empty,
                    DataBlob = dataBlob,
                    DataBlobHash = (string?)null,
                    DataSavedAt = (s.DataModifiedAt == default ? DateTime.UtcNow : s.DataModifiedAt)
                        .ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    IsSandbox = s.IsSandbox,
                    MaxPlayers = s.MaxPlayers,
                    Accessibility = room.Accessibility,
                    UnitySceneId = s.RoomSceneLocationId,
                    CurrentSave = new
                    {
                        SubRoomDataSaveId = s.StudioSubRoomDataSaveId ?? 0L,
                        SubRoomId = (long)s.OrderIndex,
                        UnityAssetId = s.StudioUnityAssetId,
                        SubRoomUnityAssetId = s.StudioUnityAssetId,
                        CreatedByAccountId = room.CreatorPlayerId,
                        ReferencedUnityAssetIds = Array.Empty<string>(),
                        DataBlob = dataBlob,
                        DataBlobHash = (string?)null,
                        PersistenceVersion = saveVersion,
                        OMVersion = 0,
                        SavedByAccountId = room.CreatorPlayerId,
                        SavedOnPlatform = 0,
                        SavedOnDeviceClass = 2,
                        Description = string.Empty,
                        ModerationState = 0,
                        CreatedAt = (s.DataModifiedAt == default ? DateTime.UtcNow : s.DataModifiedAt)
                            .ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        UgcSubVersion = saveVersion,
                        UnityAssetHash = string.Empty,
                        UnityAsset = unityAssetParent,
                        UnityAssetFilename = unityAsset?.Filename ?? string.Empty,
                        BakedUnityAssets = bakedAssets,
                        UnitySubAssets = Array.Empty<object>(),
                    },
                    LastModeratedSaveModerationState = 0,
                    DefaultMatchmakingPolicy = 0,
                    ShouldAutoStageSaves = true,
                    StagedSubRoomDataSaveId = (long?)null,
                };
            }).ToArray()
            : new object[]
            {
                new
                {
                    ReplicationId = room.LocationReplicationId,
                    PersistenceVersion = 0,
                    SupportsJoinInProgress = true,
                    UseLevelBasedMatchmaking = false,
                    UseAgeBasedMatchmaking = false,
                    UseRecRoyaleMatchmaking = false,
                    SubRoomId = 0L,
                    RoomId = room.Id,
                    Name = "Home",
                    UnityAssetId = string.Empty,
                    SubRoomUnityAssetId = string.Empty,
                    UnityAsset = string.Empty,
                    UnityAssetHash = string.Empty,
                    DataBlob = dataBlobName,
                    DataBlobHash = (string?)null,
                    DataSavedAt = updatedAt,
                    IsSandbox = false,
                    MaxPlayers = 8,
                    Accessibility = room.Accessibility,
                    UnitySceneId = room.LocationReplicationId,
                    CurrentSave = new
                    {
                        SubRoomDataSaveId = 0L,
                        SubRoomId = 0L,
                        UnityAssetId = string.Empty,
                        SubRoomUnityAssetId = string.Empty,
                        ReferencedUnityAssetIds = Array.Empty<string>(),
                        DataBlob = dataBlobName,
                    },
                },
            };

        var wireRoles = new List<object>
        {
            BuildRoomAccountRoleWire(room.CreatorPlayerId, 30),
        };
        wireRoles.AddRange(roleList.Select(BuildRoomRoleGrantWire));

        var tags = string.IsNullOrEmpty(room.TagsCsv)
            ? Array.Empty<object>()
            : room.TagsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => (object)new
                {
                    Type = RoomTagTypeForWire(t),
                    Tag = t,
                    IsPrimaryGenre = false,
                })
                .ToArray();

        return new
        {
            RoomId = room.Id,
            Name = room.Name,
            Description = room.Description,
            ImageName = room.ImageName,
            WarningMask = room.RoomWarningMask,
            CustomWarning = room.CustomRoomWarning,
            CreatorAccountId = room.CreatorPlayerId,
            State = room.State,
            Accessibility = room.Accessibility,
            SupportsLevelVoting = room.SupportsLevelVoting,
            IsRRO = room.IsAGRoom,
            SupportsScreens = room.SupportsScreens,
            SupportsWalkVR = room.SupportsWalkVR,
            SupportsTeleportVR = room.SupportsTeleportVR,
            SupportsVRLow = room.SupportsVRLow,
            SupportsQuest2 = true,
            SupportsMobile = room.SupportsMobile,
            SupportsJuniors = room.AllowsJuniors,
            AllowNewUsers = room.AllowNewUsers,
            AllowsNewUsers = room.AllowNewUsers,
            MinLevel = room.MinLevel,
            CreatedAt = (room.CreatedAt == default ? DateTime.UtcNow : room.CreatedAt)
                .ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Stats = new
            {
                CheerCount = room.CheerCount,
                FavoriteCount = room.FavoriteCount,
                VisitorCount = room.VisitorCount,
                VisitCount = room.VisitCount,
            },
            IsDorm = room.IsDormRoom,
            IsStudioRoom = room.IsStudioRoom,
            IsRoomLinkedToRecRoomStudio = room.IsRoomLinkedToRecRoomStudio,
            StudioSessionId = room.StudioSessionId,
            CloningAllowed = room.CloningAllowed,
            DisableMicAutoMute = room.DisableMicAutoMute,
            DisableRoomComments = false,
            EncryptVoiceChat = false,
            LoadScreenLocked = false,
            // Studio/UGC version family. The 2023 client reads UgcVersion to
            // decide the asset-bundle version it requests
            // (unity_assets/{id}/{target}/{UgcVersion}) and PersistenceVersion
            // to recognise a baked Studio room at all. Matches the real
            // RecRocks room.json (UgcVersion=1, PersistenceVersion=41). For a
            // non-Studio room these stay at the legacy 0 so behaviour is
            // unchanged.
            PersistenceVersion = room.IsStudioRoom ? StudioPersistenceVersion : 0,
            UgcVersion = room.IsStudioRoom ? StudioUgcVersion : 0,
            UgcSubVersion = room.IsStudioRoom ? StudioPersistenceVersion : 0,
            MinUgcSubVersion = room.IsStudioRoom ? StudioPersistenceVersion : 0,
            IsDeveloperOwned = room.IsStudioRoom || HasRoomTag(room, "developer"),
            IsJuniorCreated = false,
            IsRecRoomApproved = false,
            ExcludeFromLists = false,
            BoostCount = 0,
            RestrictedCircuitsAllowListNames = Array.Empty<string>(),
            PublishStateAvailability = new
            {
                CanSaveAsBeta = false,
                CanSaveAsUpdate = true,
                AvailableUpdateTokenCount = 3,
                NextAvailableUpdateDateTimeUtc = (string?)null,
            },
            PublishState = 0,
            MaxPlayerCalculationMode = room.MaxPlayerCalculationMode,
            SubRooms = subRooms,
            Roles = wireRoles,
            LoadScreens = JsonArrayOrEmpty(room.LoadScreensJson),
            PromoImages = JsonStringArrayOrEmpty(room.PromoImagesJson),
            PromoExternalContent = JsonArrayOrEmpty(room.PromoExternalContentJson),
            Tags = tags,
        };
    }

    private static int RoomTagTypeForWire(string tag)
    {
        if (string.Equals(tag, "beta", StringComparison.OrdinalIgnoreCase)) return 1;
        return tag.ToLowerInvariant() switch
        {
            "rrstudio" or "community" or "screen" or "walkvr" or "teleportvr" or "junior" or "pickup" => 2,
            _ => 0,
        };
    }

    private static bool HasRoomTag(RoomEntity room, string tag)
    {
        return !string.IsNullOrWhiteSpace(room.TagsCsv) &&
               room.TagsCsv
                   .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
    }

    private static JsonElement[] JsonArrayOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<JsonElement>();
        try
        {
            return JsonSerializer.Deserialize<JsonElement[]>(json) ?? Array.Empty<JsonElement>();
        }
        catch (JsonException)
        {
            return Array.Empty<JsonElement>();
        }
    }

    private static string[] JsonStringArrayOrEmpty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>PersistenceVersion / UgcSubVersion stamped on a baked Studio
    /// scene's CurrentSave. The 2023 client's room-load gate treats anything
    /// below the Studio-asset-bundle era as a legacy non-baked save and skips
    /// the Addressables bundle fetch. 41 matches the real RecRocks export
    /// (room.json + the per-save sidecars).</summary>
    public const int StudioPersistenceVersion = 41;

    /// <summary>Room-level UgcVersion for a Studio room. The client uses this
    /// as the asset-bundle version in unity_assets/{id}/{target}/{version}
    /// (RecRocks bundles bake at v1, room.json UgcVersion=1).</summary>
    public const int StudioUgcVersion = 1;


    // ── Clone / create ───────────────────────────────────────────────────

    /// <summary>
    /// `Rooms.CloneRoom(roomIdToClone, name)` — POST api/rooms/v1/clone
    /// with body `{"Name":"<new>","RoomId":<sourceId>}`.
    ///
    /// Response shape (CreateModifyRoomResponse.Deserialize, RVA 0xAF1950):
    ///   Result       (int CreateModifyRoomStatus: 0=Success, 1=Unknown,
    ///                 2=PermissionDenied, 3=RoomNotActive,
    ///                 4=RoomDoesNotExist, 5=RoomHasNoDataBlob, …)
    ///   RoomDetails  (RoomDetails object, only populated on Result=0)
    /// </summary>
    [HttpPost("api/rooms/v1/clone")]
    [Authorize]
    public async Task<IActionResult> Clone([FromBody] CloneRoomRequest req)
    {
        var pid = CurrentPlayerId;
        if (pid is null) return Unauthorized();

        // CloneAsync sets the CreateModifyRoomStatus code itself
        // (Success/RoomDoesNotExist/DuplicateName/InappropriateName) so the
        // watch can show the right toast instead of a generic "Unknown".
        var result = await rooms.CloneAsync(req.RoomId, req.Name ?? string.Empty, pid.Value);
        return Ok(new
        {
            Result = result.Status,
            RoomDetails = result.Room is null ? null : BuildRoomDetails(result.Room),
        });
    }

    public class CloneRoomRequest
    {
        public string? Name { get; set; }
        public long RoomId { get; set; }
    }

    // ── My-Rooms tabs ────────────────────────────────────────────────────

    // 2020.12 OJMCBOKJFOF populates its local rooms cache from these
    // URLs at boot (verified via metadata strings: "rooms/createdby/me",
    // "rooms/moderatedby/me", "rooms/visitedby/me"). The 2020.03 client
    // used /api/rooms/v2/myrooms, while 2020.12 moved the calls under
    // /roomserver/rooms/*. Keep both route families on the same handlers.
    [HttpGet("api/rooms/v2/myrooms")]
    [HttpGet("api/rooms/v1/createdby/me")]
    [HttpGet("api/rooms/v2/createdby/me")]
    [HttpGet("api/rooms/v3/createdby/me")]
    [HttpGet("roomserver/rooms/createdby/me")]
    [HttpGet("roomserver/rooms/ownedby/me")]
    [HttpGet("rooms/createdby/me")]
    [HttpGet("rooms/ownedby/me")]
    [Authorize]
    public async Task<IActionResult> MyRooms()
    {
        var pid = CurrentPlayerId;
        if (pid is null) return Ok(Array.Empty<object>());
        return Ok((await rooms.CreatedByAsync(pid.Value)).Select(RoomService.ToWireRoom).ToList());
    }

    [HttpGet("api/rooms/v1/createdby/{otherPlayerId:long}")]
    [HttpGet("api/rooms/v2/createdby/{otherPlayerId:long}")]
    [HttpGet("api/rooms/v3/createdby/{otherPlayerId:long}")]
    [HttpGet("roomserver/rooms/createdby/{otherPlayerId:long}")]
    [HttpGet("roomserver/rooms/ownedby/{otherPlayerId:long}")]
    [HttpGet("rooms/createdby/{otherPlayerId:long}")]
    [HttpGet("rooms/ownedby/{otherPlayerId:long}")]
    public async Task<IActionResult> CreatedByOther(long otherPlayerId)
        => Ok((await rooms.CreatedByAsync(otherPlayerId)).Select(RoomService.ToWireRoom).ToList());

    [HttpGet("api/rooms/v1/visitedby/me")]
    [HttpGet("api/rooms/v2/visitedby/me")]
    [HttpGet("api/rooms/v3/visitedby/me")]
    [HttpGet("roomserver/rooms/visitedby/me")]
    [HttpGet("rooms/visitedby/me")]
    [Authorize]
    public async Task<IActionResult> VisitedByMe()
    {
        var pid = CurrentPlayerId;
        if (pid is null) return Ok(Array.Empty<object>());
        return await VisitedBy(pid.Value);
    }

    [HttpGet("rooms/visitedby/{playerId:long}")]
    [HttpGet("roomserver/rooms/visitedby/{playerId:long}")]
    public async Task<IActionResult> VisitedBy(long playerId)
    {
        var ids = await db.RoomVisits.AsNoTracking()
            .Where(v => v.PlayerId == playerId)
            .OrderByDescending(v => v.LastVisitAt)
            .Select(v => v.RoomId)
            .Take(100)
            .ToListAsync();
        if (ids.Count == 0) return Ok(Array.Empty<object>());

        var rows = await db.Rooms.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .ToListAsync();
        var byId = rows.ToDictionary(r => r.Id);
        var ordered = ids
            .Select(id => byId.TryGetValue(id, out var room) ? room : null)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
        return Ok(await BuildRoomServerListAsync(ordered));
    }

    [HttpGet("api/rooms/v1/moderatedby/me")]
    [HttpGet("api/rooms/v2/moderatedby/me")]
    [HttpGet("api/rooms/v3/moderatedby/me")]
    [HttpGet("roomserver/rooms/moderatedby/me")]
    [HttpGet("rooms/moderatedby/me")]
    [Authorize]
    public async Task<IActionResult> ModeratedByMe()
    {
        var pid = CurrentPlayerId;
        if (pid is null) return Ok(Array.Empty<object>());
        var ids = await db.RoomRoles.AsNoTracking()
            .Where(r => r.PlayerId == pid.Value && r.Accepted)
            .OrderByDescending(r => r.GrantedAt)
            .Select(r => r.RoomId)
            .Distinct()
            .Take(100)
            .ToListAsync();
        var rows = await db.Rooms.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync();
        return Ok(await BuildRoomServerListAsync(rows));
    }

    [HttpGet("api/rooms/v1/favoritedby/me")]
    [HttpGet("api/rooms/v2/favoritedby/me")]
    [HttpGet("api/rooms/v3/favoritedby/me")]
    [HttpGet("roomserver/rooms/favoritedby/me")]
    [HttpGet("rooms/favoritedby/me")]
    [Authorize]
    public async Task<IActionResult> FavoritedByMe()
    {
        var pid = CurrentPlayerId;
        if (pid is null) return Ok(Array.Empty<object>());
        var rows = await rooms.BookmarkedByAsync(pid.Value);
        return Ok(await BuildRoomServerListAsync(rows));
    }

    [HttpGet("api/rooms/v1/cheeredby/me")]
    [HttpGet("api/rooms/v2/cheeredby/me")]
    [HttpGet("api/rooms/v3/cheeredby/me")]
    [HttpGet("roomserver/rooms/cheeredby/me")]
    [HttpGet("rooms/cheeredby/me")]
    [Authorize]
    public async Task<IActionResult> CheeredByMe()
    {
        var pid = CurrentPlayerId;
        if (pid is null) return Ok(Array.Empty<object>());
        var ids = await db.Cheers.AsNoTracking()
            .Where(c => c.FromPlayerId == pid.Value && c.TargetRoomId != 0)
            .OrderByDescending(c => c.CheeredAt)
            .Select(c => c.TargetRoomId)
            .Distinct()
            .Take(100)
            .ToListAsync();
        var rows = await db.Rooms.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .OrderByDescending(r => r.HotScore)
            .ToListAsync();
        return Ok(await BuildRoomServerListAsync(rows));
    }

    [HttpGet("api/rooms/v2/mybookmarks")]
    [HttpGet("api/rooms/v2/mybookmarkedrooms")]
    [Authorize]
    public async Task<IActionResult> MyBookmarks()
    {
        var pid = CurrentPlayerId;
        if (pid is null) return Ok(Array.Empty<object>());
        return Ok((await rooms.BookmarkedByAsync(pid.Value)).Select(RoomService.ToWireRoom).ToList());
    }

    [HttpGet("api/rooms/v2/mymoderated")]
    [HttpGet("api/rooms/v1/modrooms")]
    [HttpGet("api/rooms/v2/mysubscribed")]
    [HttpGet("api/rooms/v2/myrecent")]
    public IActionResult MyOtherTabs() => Ok(Array.Empty<object>());

    [HttpGet("player_room_data/{roomId:long}")]
    [HttpGet("rooms/{roomId:long}/playerdata/me")]
    [Authorize]
    public async Task<IActionResult> PlayerDataForMe(long roomId)
    {
        var pid = CurrentPlayerId;
        if (pid is null) return Unauthorized();

        var room = await db.Rooms.AsNoTracking()
            .Where(r => r.Id == roomId)
            .Select(r => new { r.Id, r.VisitCount, r.CheerCount, r.FavoriteCount })
            .FirstOrDefaultAsync();

        var favorited = await db.RoomBookmarks
            .AnyAsync(b => b.RoomId == roomId && b.PlayerId == pid.Value);
        var cheered = await db.Cheers
            .AnyAsync(c => c.TargetRoomId == roomId && c.FromPlayerId == pid.Value);
        var visit = await db.RoomVisits.AsNoTracking()
            .Where(v => v.RoomId == roomId && v.PlayerId == pid.Value)
            .Select(v => new { v.VisitCount, v.FirstVisitAt, v.LastVisitAt })
            .FirstOrDefaultAsync();

        return Ok(new
        {
            Data = "CAE=",
            RoomId = room?.Id ?? roomId,
            PlayerId = pid.Value,
            Favorite = favorited,
            Favorited = favorited,
            IsFavorite = favorited,
            Cheer = cheered,
            Cheered = cheered,
            IsCheered = cheered,
            IsBookmarked = favorited,
            IsCheering = cheered,
            VisitCount = visit?.VisitCount ?? 0,
            FirstVisitedAt = visit?.FirstVisitAt,
            LastVisitedAt = visit?.LastVisitAt,
            RoomVisitCount = room?.VisitCount ?? 0,
            RoomCheerCount = room?.CheerCount ?? 0,
            RoomFavoriteCount = room?.FavoriteCount ?? 0,
        });
    }

    [HttpGet("rooms/requiring/{restriction}")]
    [HttpGet("roomserver/rooms/requiring/{restriction}")]
    [Authorize]
    public async Task<IActionResult> RoomsRequiring(string restriction)
    {
        var key = (restriction ?? string.Empty).Trim().TrimStart('#').ToLowerInvariant();
        if (key.Length == 0) return Ok(new List<string>());

        IQueryable<RoomEntity> query = db.Rooms.AsNoTracking()
            .Where(r => !r.HiddenFromBrowse && r.State == 0);

        query = key switch
        {
            "developer" or "studio" or "rrstudio" => query.Where(r =>
                r.IsStudioRoom
                || r.IsRoomLinkedToRecRoomStudio
                || EF.Functions.Like(r.TagsCsv, "%developer%")
                || EF.Functions.Like(r.TagsCsv, "%studio%")),
            "rrplus" or "recroomplus" => query.Where(r =>
                EF.Functions.Like(r.TagsCsv, "%rrplus%")
                || EF.Functions.Like(r.TagsCsv, "%recroomplus%")),
            _ => query.Where(r => EF.Functions.Like(r.TagsCsv, $"%{key}%")),
        };

        var rows = await query
            .OrderByDescending(r => r.HotScore)
            .ThenBy(r => r.Name)
            .Select(r => r.Name)
            .Take(100)
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet("rooms/curated_playlists")]
    [HttpGet("roomserver/rooms/curated_playlists")]
    [Authorize]
    public async Task<IActionResult> CuratedPlaylistsCompat()
    {
        var curated = await playlists.CuratedAsync();
        return Ok(curated.Select(p => p.Id).ToList());
    }

    [HttpGet("clubhousesearch/mostactivenow")]
    [HttpGet("roomserver/clubhousesearch/mostactivenow")]
    [Authorize]
    public async Task<IActionResult> MostActiveClubhouses()
    {
        var rows = await db.Clubs.AsNoTracking()
            .Where(c => c.State == 0 && c.ClubhouseRoomId != null)
            .GroupJoin(
                db.ClubMemberships.AsNoTracking(),
                c => c.Id,
                m => m.ClubId,
                (club, memberships) => new
                {
                    Club = club,
                    MemberCount = memberships.Count(),
                })
            .Join(
                db.Rooms.AsNoTracking(),
                c => c.Club.ClubhouseRoomId!.Value,
                r => r.Id,
                (c, room) => new
                {
                    c.Club,
                    c.MemberCount,
                    Room = room,
                })
            .OrderByDescending(x => x.MemberCount)
            .ThenByDescending(x => x.Room.HotScore)
            .Take(50)
            .ToListAsync();

        return Ok(rows.Select(x => new
        {
            ClubId = x.Club.Id,
            x.Club.Name,
            x.Club.Description,
            MainImageName = x.Club.ImageName,
            ImageName = x.Club.ImageName,
            State = x.Club.State,
            CreatorAccountId = x.Club.CreatorPlayerId,
            Category = x.Club.Category,
            Visibility = x.Club.Visibility,
            Joinability = x.Club.Joinability,
            x.Club.AllowJuniors,
            MemberCount = x.MemberCount,
            x.Club.IsRRO,
            x.Club.ClubhouseRoomId,
            x.Club.ClubType,
            Room = RoomService.ToWireRoom(x.Room),
            RoomId = x.Room.Id,
            RoomName = x.Room.Name,
        }).ToList());
    }

    // ── Bookmark mutations ───────────────────────────────────────────────

    [HttpPost("api/rooms/v1/bookmark/{roomId:long}")]
    [HttpPost("api/rooms/v2/bookmark/{roomId:long}")]
    [Authorize]
    public async Task<IActionResult> Bookmark(long roomId)
    {
        var pid = CurrentPlayerId;
        if (pid is null) return Unauthorized();
        await rooms.BookmarkAsync(pid.Value, roomId);
        return Ok(new { success = true, error = "" });
    }

    [HttpDelete("api/rooms/v1/bookmark/{roomId:long}")]
    [HttpDelete("api/rooms/v2/bookmark/{roomId:long}")]
    [Authorize]
    public async Task<IActionResult> Unbookmark(long roomId)
    {
        var pid = CurrentPlayerId;
        if (pid is null) return Unauthorized();
        await rooms.UnbookmarkAsync(pid.Value, roomId);
        return Ok(new { success = true, error = "" });
    }

    private IQueryable<RoomEntity> PublicRoomQuery() =>
        db.Rooms.AsNoTracking().Where(r =>
            r.State == 0 &&
            r.Accessibility == 1 &&
            !r.IsDormRoom &&
            !r.HiddenFromBrowse);

    private async Task<List<object>> BuildRoomServerListAsync(IReadOnlyCollection<RoomEntity> roomRows)
    {
        if (roomRows.Count == 0) return new List<object>();

        var ids = roomRows.Select(r => r.Id).Distinct().ToList();
        var sceneRows = await db.RoomScenes.AsNoTracking()
            .Where(s => ids.Contains(s.RoomId))
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
        var scenesByRoom = sceneRows
            .GroupBy(s => s.RoomId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RoomSceneEntity>)g.ToList());
        var roleRows = await db.RoomRoles.AsNoTracking()
            .Where(r => ids.Contains(r.RoomId))
            .ToListAsync();
        var rolesByRoom = roleRows
            .GroupBy(r => r.RoomId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RoomRoleEntity>)g.ToList());

        return roomRows
            .Select(r => BuildRoomServerDetails(
                r,
                scenesByRoom.GetValueOrDefault(r.Id),
                roles: rolesByRoom.GetValueOrDefault(r.Id)))
            .ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>GET <c>api/rooms/v1/agRoomIds</c> — list of every
    /// seeded AG / RR-Original room id. Used by the watch to bias the
    /// room browser toward official rooms first.</summary>
    [HttpGet("api/rooms/v1/agRoomIds")]
    [HttpGet("rooms/rro_ids")]
    public async Task<IActionResult> AgRoomIds() =>
        Ok(await rooms.AgRoomIdsAsync());

    /// <summary>GET <c>api/rooms/v1/featuredRoomGroup</c> +
    /// <c>v3/featured</c> — the "Featured" carousel on the watch's
    /// rooms tab. Returns the top-12 AG rooms by HotScore.</summary>
    [HttpGet("api/rooms/v1/featuredRoomGroup")]
    [HttpGet("api/rooms/v3/featured")]
    public async Task<IActionResult> Featured() => Ok(new
    {
        Name = "Featured",
        FeaturedRooms = await rooms.FeaturedAgRoomIdsAsync(12),
    });

    /// <summary>GET <c>featuredrooms/current</c> — the watch's pointer
    /// to the current "Featured" room group. Caller:
    /// <c>EJDCNGBEICB.CFKDADKHAGB</c>. Wire shape is <c>NMPFCIJPODA</c>
    /// with required keys <c>FeaturedRoomGroupId</c> (long) and
    /// <c>Name</c> (string) — a pointer record, NOT the room list
    /// itself (the watch fetches the list separately via
    /// <c>api/rooms/v3/featured</c>). Returning the
    /// <see cref="Featured"/> shape here throws KeyNotFoundException on
    /// the watch's group-id lookup.</summary>
    [HttpGet("featuredrooms/current")]
    public async Task<IActionResult> FeaturedRoomsCurrent()
    {
        var ids = await rooms.FeaturedAgRoomIdsAsync(12);
        var featuredRooms = await db.Rooms
            .Where(r => ids.Contains(r.Id))
            .ToListAsync();
        var byId = featuredRooms.ToDictionary(r => r.Id);
        var wireRooms = ids
            .Select(id => byId.TryGetValue(id, out var room) ? RoomService.ToWireRoom(room) : null)
            .Where(room => room is not null)
            .Cast<object>()
            .ToList();
        return Ok(new
        {
            FeaturedRoomGroupId = 1L,
            Name = "Featured",
            Rooms = wireRooms,
        });
    }

    /// <summary>POST <c>api/rooms/v1/bookmark</c> — toggle bookmark
    /// state for a room. Body: <c>RoomId</c> + optional <c>Bookmark</c>
    /// flag (default = add).</summary>
    [HttpPost("api/rooms/v1/bookmark")]
    [Authorize]
    public async Task<IActionResult> BookmarkV1(
        [FromForm(Name = "RoomId")] long? roomId,
        [FromForm(Name = "Bookmark")] bool? bookmark)
    {
        if (roomId is not long rid) return Ok(new { success = true });
        var pid = this.RequireCurrentPlayerId();
        if (bookmark == false) await rooms.UnbookmarkAsync(pid, rid);
        else await rooms.BookmarkAsync(pid, rid);
        return Ok(new { success = true, error = "" });
    }

    /// <summary>POST <c>rooms/{roomId}/name</c> — rename a room.
    /// Watch path: <c>EJDCNGBEICB.DNOGGKKHMHI(long roomId, string newName)</c>
    /// (analytics tag "change room name"). Body shape: form-urlencoded
    /// <c>Name=...</c> or a bare string in the body. Returns the
    /// updated Room DTO (<c>PPENFJMFPNE : KLCOGEIGEBJ</c>).
    ///
    /// <para>Caller must be the room creator OR a co-owner. Other
    /// validations: name length 1..40, no leading/trailing whitespace,
    /// uniqueness against other Rooms.Name. Failures return
    /// the watch's <c>CreateModifyRoomStatus</c>-style payload with
    /// a non-zero Result so the rename toast surfaces a real reason.</para>
    ///
    /// <para>We accept both POST and PUT because the watch's ISIL
    /// doesn't expose the explicit HTTPMethod literal at this call
    /// site — covering both is cheap and means we can't get bitten
    /// by the wrong one.</para></summary>
    public sealed class RenameBody { public string? Name { get; set; } }

    [HttpPost("rooms/{roomId:long}/name")]
    [HttpPut("rooms/{roomId:long}/name")]
    [Authorize]
    public async Task<IActionResult> RenameRoom(
        long roomId,
        [FromForm(Name = "Name")] string? nameForm,
        [FromForm(Name = "name")] string? nameFormLower,
        [FromBody] RenameBody? body)
    {
        var newName = nameForm ?? nameFormLower ?? body?.Name;
        if (string.IsNullOrWhiteSpace(newName))
            return BadRequest(new { Result = 1, error = "empty_name" });
        newName = newName.Trim();
        if (newName.Length is < 1 or > 40)
            return BadRequest(new { Result = 1, error = "invalid_length", length = newName.Length });

        var me = this.RequireCurrentPlayerId();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound(new { Result = 4, error = "room_not_found" });
        if (room.CreatorPlayerId != me)
        {
            // RoomRoles co-owner check — same authorisation as the
            // existing modify endpoint, lets co-owners rename too.
            var coOwner = await db.RoomRoles.AnyAsync(rr =>
                rr.RoomId == roomId && rr.PlayerId == me && rr.Accepted && (rr.Role == 0 || rr.Role == 1));
            if (!coOwner) return Forbid();
        }

        // Uniqueness — Rooms.Name has a unique index (DorkNetDbContext.cs:118).
        var collision = await db.Rooms.AnyAsync(r => r.Id != roomId && r.Name == newName);
        if (collision) return Conflict(new { Result = 5, error = "name_taken" });

        room.Name = newName;
        room.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        logger.LogInformation("[room-rename] room={Room} by={By} → '{Name}'", roomId, me, newName);

        // Return the full updated Room DTO. The watch's
        // <c>PPENFJMFPNE</c> deserialiser inherits from
        // <c>KLCOGEIGEBJ</c> (the standard Room shape) so we can
        // forward to the same v3-details payload builder by re-reading.
        // BuildRoomDetailsV3Async is too heavy here — just return the
        // minimal mutated subset; the watch UI re-fetches details
        // shortly after for the room-info screen.
        return Ok(new
        {
            Result = 0,
            RoomId = room.Id,
            Name = room.Name,
            Description = room.Description,
            ImageName = room.ImageName,
            CreatorPlayerId = room.CreatorPlayerId,
            CreatorAccountId = room.CreatorPlayerId,
            State = room.State,
            Accessibility = room.Accessibility,
            IsAGRoom = room.IsAGRoom,
            IsRRO = false,
            IsDormRoom = room.IsDormRoom,
        });
    }

    /// <summary>POST <c>rooms/{roomId}/subrooms/{subRoomId}/name</c> —
    /// rename a scene/sub-room within a multi-scene room. Watch path:
    /// <c>EJDCNGBEICB.PAENPJCDLNJ(long roomId, long subRoomId, string newName)</c>.
    /// Updates the matching <see cref="RoomSceneEntity.Name"/> after
    /// verifying ownership and intra-room uniqueness.</summary>
    [HttpPost("rooms/{roomId:long}/subrooms/{subRoomId:long}/name")]
    [HttpPut("rooms/{roomId:long}/subrooms/{subRoomId:long}/name")]
    [Authorize]
    public async Task<IActionResult> RenameSubroom(
        long roomId,
        long subRoomId,
        [FromForm(Name = "Name")] string? nameForm,
        [FromForm(Name = "name")] string? nameFormLower,
        [FromBody] RenameBody? body)
    {
        var newName = nameForm ?? nameFormLower ?? body?.Name;
        if (string.IsNullOrWhiteSpace(newName))
            return BadRequest(new { Result = 1, error = "empty_name" });
        newName = newName.Trim();
        if (newName.Length is < 1 or > 40)
            return BadRequest(new { Result = 1, error = "invalid_length", length = newName.Length });

        var me = this.RequireCurrentPlayerId();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound(new { Result = 4, error = "room_not_found" });
        if (room.CreatorPlayerId != me)
        {
            var coOwner = await db.RoomRoles.AnyAsync(rr =>
                rr.RoomId == roomId && rr.PlayerId == me && rr.Accepted && (rr.Role == 0 || rr.Role == 1));
            if (!coOwner) return Forbid();
        }

        var scene = await db.RoomScenes.FirstOrDefaultAsync(s =>
            s.RoomId == roomId && s.OrderIndex == subRoomId);
        if (scene is null) return NotFound(new { Result = 4, error = "subroom_not_found", subRoomId });

        // Uniqueness within this room — same scene name twice would
        // make /goto/room/X/Y ambiguous against the RoomScenes index
        // on (RoomId, Name) at DorkNetDbContext.cs:153.
        var collision = await db.RoomScenes.AnyAsync(s =>
            s.RoomId == roomId && s.OrderIndex != subRoomId && s.Name == newName);
        if (collision) return Conflict(new { Result = 5, error = "subroom_name_taken" });

        scene.Name = newName;
        scene.DataModifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        logger.LogInformation("[subroom-rename] room={Room} sub={Sub} by={By} → '{Name}'", roomId, subRoomId, me, newName);

        return Ok(new
        {
            Result = 0,
            RoomId = roomId,
            RoomSceneId = scene.OrderIndex,
            SubRoomId = scene.OrderIndex,
            Name = scene.Name,
            scene.DataBlobName,
            RoomSceneLocationId = scene.RoomSceneLocationId,
            scene.IsSandbox,
            scene.MaxPlayers,
            scene.CanMatchmakeInto,
        });
    }

    /// <summary>POST <c>api/rooms/v1/cheer</c> — cheer a room (body
    /// form). Idempotent per (caller, room) — duplicate taps don't
    /// inflate CheerCount.</summary>
    [HttpPost("api/rooms/v1/cheer")]
    [Authorize]
    public async Task<IActionResult> CheerRoom(
        [FromForm(Name = "RoomId")] long? roomId,
        [FromForm(Name = "Type")] int? type)
    {
        if (roomId is not long rid) return Ok(new { success = true });
        var me = this.RequireCurrentPlayerId();
        var (newCount, already) = await rooms.CheerRoomAsync(me, rid, type ?? 0);
        return Ok(new { RoomId = rid, CheerCount = newCount, AlreadyCheered = already });
    }

    /// <summary>POST <c>api/rooms/v1/uncheer</c> — remove a previously
    /// applied room cheer.</summary>
    [HttpPost("api/rooms/v1/uncheer")]
    [HttpPost("api/rooms/v1/uncheer/{roomId:long}")]
    [Authorize]
    public async Task<IActionResult> UncheerRoom(
        long? roomId,
        [FromForm(Name = "RoomId")] long? roomIdForm,
        [FromForm(Name = "Type")] int? type)
    {
        var rid = roomId ?? roomIdForm ?? 0;
        if (rid == 0) return Ok(new { success = true });
        var me = this.RequireCurrentPlayerId();
        var already = await rooms.UncheerRoomAsync(me, rid, type ?? 0);
        return Ok(new { RoomId = rid, AlreadyUncheered = already });
    }

    /// <summary>GET <c>api/rooms/v1/cheers/{roomId}</c> — did the
    /// caller cheer this room, and what's the current count?</summary>
    [HttpGet("api/rooms/v1/cheers/{roomId:long}")]
    [Authorize]
    public async Task<IActionResult> CheckCheered(long roomId)
    {
        var me = this.CurrentPlayerId();
        var (count, iCheered) = await rooms.GetCheerStateAsync(me ?? 0, roomId);
        return Ok(new { RoomId = roomId, CheerCount = count, ICheered = iCheered });
    }

    // ── Bare-path interactionby/me toggles ─────────────────────────────

    [HttpPost("rooms/{roomId:long}/interactionby/me/cheer")]
    [HttpPut("rooms/{roomId:long}/interactionby/me/cheer")]
    [Authorize]
    public async Task<IActionResult> BareCheer(long roomId)
    {
        var me = this.RequireCurrentPlayerId();
        await rooms.CheerRoomAsync(me, roomId, 0);
        return Ok(new { success = true });
    }

    [HttpDelete("rooms/{roomId:long}/interactionby/me/cheer")]
    [Authorize]
    public async Task<IActionResult> BareUncheer(long roomId)
    {
        var me = this.RequireCurrentPlayerId();
        await rooms.UncheerRoomAsync(me, roomId, 0);
        return Ok(new { success = true });
    }

    [HttpPost("rooms/{roomId:long}/interactionby/me/favorite")]
    [HttpPut("rooms/{roomId:long}/interactionby/me/favorite")]
    [Authorize]
    public async Task<IActionResult> BareFavorite(long roomId)
    {
        var me = this.RequireCurrentPlayerId();
        await rooms.BookmarkAsync(me, roomId);
        return Ok(new { success = true });
    }

    [HttpDelete("rooms/{roomId:long}/interactionby/me/favorite")]
    [Authorize]
    public async Task<IActionResult> BareUnfavorite(long roomId)
    {
        var me = this.RequireCurrentPlayerId();
        await rooms.UnbookmarkAsync(me, roomId);
        return Ok(new { success = true });
    }

    // ── Rooms-and-Playlists union (2020.12 watch) ────────────────────────

    /// <summary>
    /// GET <c>/roomserver/roomsandplaylists/hot</c> — the 2020.12 watch's
    /// merged "Trending" / "New" tab. Two URL variants exist with
    /// DIFFERENT wire shapes:
    ///
    ///   1. <c>/roomserver/roomsandplaylists/hot</c> (this endpoint)
    ///      Watch caller: <c>EJDCNGBEICB.EBBHGHEGBKG</c>
    ///      Signature:    <c>IPromise&lt;HJKAOMOICJG&gt;</c>
    ///      Wire shape:   <c>{ TotalResults, Results: [union] }</c>
    ///      (verified at <c>EJDCNGBEICB.txt:2571,2645</c> +
    ///      <c>HJKAOMOICJG.txt:39-50</c>)
    ///   2. <c>/roomserver/hot_roomsandplaylists/{tags-joined}</c>
    ///      Watch caller: <c>OJMCBOKJFOF.EBBHGHEGBKG</c>
    ///      Signature:    <c>IPromise&lt;List&lt;MKAMHOIHOJK&gt;&gt;</c>
    ///      Wire shape:   bare list, see <see cref="HotRoomsAndPlaylistsPath"/>
    ///      (verified at <c>OJMCBOKJFOF.txt:4607,4743</c>)
    ///
    /// Returning a bare list here throws
    /// <c>InvalidCastException: Unable to cast object of type 'List`1'
    /// to type 'Dictionary`2'</c> on the watch — the wrapped
    /// HJKAOMOICJG deserializer reads <c>TotalResults</c> as a top-level
    /// dict key, and a JSON array body fails that cast.
    ///
    /// Inside <c>Results</c>: union entries discriminated by lowercase
    /// <c>roomId</c> key per the <c>MKAMHOIHOJK</c> factory at
    /// <c>MKAMHOIHOJK.txt:621</c> — see <see cref="BuildRoomUnionEntry"/>
    /// + <see cref="BuildPlaylistUnionEntry"/>.
    ///
    /// <para><b>Why rooms-only despite the "and playlists" name:</b> the
    /// Hot tab of the watch's Play page mounts this endpoint and used to
    /// interleave playlist tiles next to room tiles, but design intent is
    /// that playlists appear only under the Moods/Playlists section (their
    /// own endpoints — <c>/api/curatedroomplaylists</c> +
    /// <c>/roomserver/playlists/{id}</c>). The watch's union deserializer
    /// happily accepts a Results list that's 100% rooms, so we keep the
    /// wrapper shape but drop the playlist half.</para>
    /// </summary>
    [HttpGet("roomserver/roomsandplaylists/hot")]
    [HttpGet("roomsandplaylists/hot")]
    public async Task<IActionResult> RoomsAndPlaylistsHot([FromQuery] string? tag)
    {
        var (entries, total) = await BuildHotRoomsAsync(tag);
        return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["TotalResults"] = (long)total,
            ["Results"] = entries,
        });
    }

    /// <summary>
    /// GET <c>/roomserver/hot_roomsandplaylists/{tagsJoined}</c> —
    /// path-style sibling of <see cref="RoomsAndPlaylistsHot"/> with a
    /// BARE <c>List&lt;MKAMHOIHOJK&gt;</c> shape (no TotalResults
    /// wrapper). Tags are comma-joined into the URL path per
    /// <c>OJMCBOKJFOF.txt:4739,4743</c> (<c>String.Join(",", tags)</c>
    /// followed by <c>String.Concat("hot_roomsandplaylists/", joined)</c>).
    /// Empty path is legal — equivalent to "no tag filter".
    /// </summary>
    [HttpGet("roomserver/hot_roomsandplaylists/{tagsJoined?}")]
    [HttpGet("hot_roomsandplaylists/{tagsJoined?}")]
    public async Task<IActionResult> HotRoomsAndPlaylistsPath(string? tagsJoined)
    {
        // Watch joins multiple tags with "," — use the first one as a
        // substring filter since RoomService.HotAsync takes a single
        // tag arg. Empty / null = unfiltered.
        //
        // Returns rooms-only for the same reason as the wrapped
        // RoomsAndPlaylistsHot endpoint above: playlists belong in
        // Moods, not in the Hot tab. See that endpoint's doc-comment.
        var firstTag = string.IsNullOrWhiteSpace(tagsJoined)
            ? null
            : tagsJoined.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
        var (entries, _) = await BuildHotRoomsAsync(firstTag);
        return Ok(entries);
    }

    /// <summary>GET <c>hot_rooms/{tagsJoined?}</c> — rooms-only variant
    /// of <see cref="HotRoomsAndPlaylistsPath"/>. Caller:
    /// <c>OJMCBOKJFOF.DLPLPKCNLNA</c>. Same <c>MKAMHOIHOJK</c> union
    /// entry shape as <c>hot_roomsandplaylists</c>, just skips playlist
    /// rows so the watch's rooms-only browse tab doesn't render
    /// playlist tiles that have nowhere to go.</summary>
    [HttpGet("hot_rooms/{tagsJoined?}")]
    public async Task<IActionResult> HotRoomsOnlyPath(string? tagsJoined)
    {
        var firstTag = string.IsNullOrWhiteSpace(tagsJoined)
            ? null
            : tagsJoined.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
        var roomList = await rooms.HotAsync(firstTag, take: 12);
        var roomIds = roomList.Select(r => r.Id).ToList();
        var sceneRows = roomIds.Count == 0
            ? new List<RoomSceneEntity>()
            : await db.RoomScenes.Where(s => roomIds.Contains(s.RoomId))
                .OrderBy(s => s.OrderIndex).ToListAsync();
        var scenesByRoom = sceneRows.GroupBy(s => s.RoomId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RoomSceneEntity>)g.ToList());
        return Ok(roomList
            .Select(r => BuildRoomUnionEntry(r, scenesByRoom.GetValueOrDefault(r.Id)))
            .ToList());
    }

    /// <summary>
    /// Shared body for the two hot variants — pulls hot rooms only,
    /// fans out sub-room rows in one query to avoid N+1, and returns
    /// the union-shape entries the watch's factory expects (each entry
    /// happens to be a room dict; the factory accepts that). Returns
    /// (entries, totalCount) so the wrapped variant can fill in
    /// TotalResults.
    ///
    /// <para>Take limit raised to 100 (was 12) — the Hot tab used to
    /// surface only the top dozen rooms which is far less than the
    /// catalog the watch's browse UI can actually render. The watch
    /// paginates client-side if it needs to chunk further.</para>
    /// </summary>
    private async Task<(List<IDictionary<string, object>> Entries, int Total)> BuildHotRoomsAsync(string? tag)
    {
        var roomList = await rooms.HotAsync(tag, take: 100);

        var roomIds = roomList.Select(r => r.Id).ToList();
        var sceneRows = roomIds.Count == 0
            ? new List<RoomSceneEntity>()
            : await db.RoomScenes
                .Where(s => roomIds.Contains(s.RoomId))
                .OrderBy(s => s.OrderIndex)
                .ToListAsync();
        var scenesByRoom = sceneRows
            .GroupBy(s => s.RoomId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RoomSceneEntity>)g.ToList());

        var entries = roomList
            .Select(r => BuildRoomUnionEntry(r, scenesByRoom.GetValueOrDefault(r.Id)))
            .ToList();
        return (entries, entries.Count);
    }

    /// <summary>
    /// GET <c>/api/curatedroomplaylists</c> — the editorial "Curated"
    /// row on the watch's playlists tab.
    ///
    /// Wire shape: <c>List&lt;Int64&gt;</c> — JUST the playlist IDs, not
    /// the full playlist objects. Verified against
    /// <c>FPNPBJBCMKB.EPPPMHMMGME</c> (signature
    /// <c>IPromise&lt;List&lt;long&gt;&gt;</c>) and the call site at
    /// <c>BrowseRoomsScreen.txt:2806</c> which constructs an
    /// <c>Action&lt;List&lt;Int64&gt;&gt;</c> callback. Returning rich
    /// objects here throws
    /// <c>InvalidCastException: Unable to cast object of type 'List`1'
    /// to type 'Dictionary`2'</c> on the watch ("Failed to get curated
    /// room playlists: Malformed Response"). The watch fetches each
    /// playlist's details separately via the playlist-detail endpoints.
    /// </summary>
    [HttpGet("api/curatedroomplaylists")]
    public async Task<IActionResult> CuratedRoomPlaylists()
    {
        var curated = await playlists.CuratedAsync();
        return Ok(curated.Select(p => p.Id).ToList());
    }

    /// <summary>
    /// GET <c>/roomserver/playlists/{id}</c> — single-playlist details
    /// with members. The watch deserializes this as <b>BMFAGMFKODA</b>
    /// (verified at <c>BMFAGMFKODA.txt:182-191</c>) which:
    ///   1. Calls <c>KMKPEOGJDFK.PPGFHEDFBEA</c> to read the base
    ///      Playlist union fields (<c>PlaylistId</c> + all
    ///      <c>MKAMHOIHOJK</c> base keys).
    ///   2. Reads required key <c>Rooms</c> — <c>List&lt;KLCOGEIGEBJ&gt;</c>
    ///      (the playlist's member rooms, full Room dicts).
    ///   3. Reads required key <c>Tags</c> — <c>List&lt;DPHPFLGAICI&gt;</c>
    ///      (tag rows with <c>Type</c> + <c>Tag</c>).
    /// Returning just the <c>KMKPEOGJDFK</c> shape (no Rooms/Tags) throws
    /// <c>KeyNotFoundException: Failed to find key 'Rooms' when
    /// deserializing object of type KLCOGEIGEBJ</c> on the watch.
    /// </summary>
    [HttpGet("roomserver/playlists/{id:long}")]
    [HttpGet("playlists/{id:long}")]
    public async Task<IActionResult> PlaylistDetails(long id)
    {
        var p = await db.Playlists.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();

        // Pull member rooms in playlist order, then fan out room +
        // scene rows so each entry can render as a full KLCOGEIGEBJ.
        var roomIds = await db.PlaylistRooms
            .Where(pr => pr.PlaylistId == id)
            .OrderBy(pr => pr.OrderIndex)
            .Select(pr => pr.RoomId)
            .ToListAsync();

        List<RoomEntity> memberRooms;
        Dictionary<long, IReadOnlyList<RoomSceneEntity>> scenesByRoom;
        if (roomIds.Count == 0)
        {
            memberRooms = new List<RoomEntity>();
            scenesByRoom = new Dictionary<long, IReadOnlyList<RoomSceneEntity>>();
        }
        else
        {
            var roomRows = await db.Rooms
                .Where(r => roomIds.Contains(r.Id))
                .ToListAsync();
            // Preserve playlist order — db.Rooms.Where doesn't guarantee
            // it matches the OrderBy on PlaylistRooms.OrderIndex.
            var roomById = roomRows.ToDictionary(r => r.Id);
            memberRooms = roomIds
                .Select(rid => roomById.TryGetValue(rid, out var r) ? r : null)
                .Where(r => r is not null)
                .Select(r => r!)
                .ToList();

            var sceneRows = await db.RoomScenes
                .Where(s => roomIds.Contains(s.RoomId))
                .OrderBy(s => s.OrderIndex)
                .ToListAsync();
            scenesByRoom = sceneRows
                .GroupBy(s => s.RoomId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<RoomSceneEntity>)g.ToList());
        }

        var roomsWire = memberRooms
            .Select(r => BuildRoomServerDetails(r, scenesByRoom.GetValueOrDefault(r.Id)))
            .ToList();

        // Tags wire shape: [{Type:int, Tag:string}] — same shape used in
        // BuildRoomServerDetails for Room.Tags. Split TagsCsv if present.
        var tagsWire = string.IsNullOrWhiteSpace(p.TagsCsv)
            ? new List<object>()
            : p.TagsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => (object)new
                {
                    Type = RoomTagTypeForWire(t),
                    Tag = t,
                    IsPrimaryGenre = false,
                })
                .ToList();

        // Start from BuildPlaylistUnionEntry for base + PlaylistId, then
        // splice in the Rooms + Tags lists. Dictionary lets us add the
        // extra keys after the base build.
        var result = BuildPlaylistUnionEntry(p);
        result["Rooms"] = roomsWire;
        result["Tags"] = tagsWire;
        return Ok(result);
    }

    /// <summary>
    /// GET <c>/roomserver/roomsandplaylists/search?query=X</c> — search
    /// surface used by the watch's friend-mention / room-search affordance.
    /// Verified at <c>EJDCNGBEICB.txt:2486,2560</c> — signature
    /// <c>IPromise&lt;HJKAOMOICJG&gt;</c>. Per
    /// <see cref="HJKAOMOICJG.PPGFHEDFBEA"/> at <c>HJKAOMOICJG.txt:39-50</c>
    /// the response object reads <c>TotalResults</c> (Int64) and
    /// <c>Results</c> (List of <c>MKAMHOIHOJK</c> union entries — same
    /// Room/Playlist factory used by <c>/hot</c>).
    ///
    /// Distinct from <c>/roomserver/search_roomsandplaylists/{query}</c>
    /// (path-param shape from <c>OJMCBOKJFOF.txt:4567</c>); both URLs
    /// reach the same data but differ in shape — the path-param sibling
    /// returns a bare <c>List&lt;MKAMHOIHOJK&gt;</c> without the
    /// TotalResults wrapper.
    /// </summary>
    [HttpGet("roomserver/roomsandplaylists/search")]
    [HttpGet("roomsandplaylists/search")]
    public async Task<IActionResult> RoomsAndPlaylistsSearch([FromQuery] string? query)
    {
        var (merged, total) = await BuildSearchUnionAsync(query);
        return Ok(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["TotalResults"] = (long)total,
            ["Results"] = merged,
        });
    }

    /// <summary>
    /// GET <c>/roomserver/search_roomsandplaylists/{query}</c> — free-text
    /// search across both rooms and playlists, BARE-LIST variant. Per
    /// <c>OJMCBOKJFOF.txt:4439,4567</c> the signature is
    /// <c>IPromise&lt;List&lt;MKAMHOIHOJK&gt;&gt;</c> — no TotalResults
    /// wrapper. URL is <c>search_roomsandplaylists/{query}</c>
    /// (query in path, not query-string).
    /// </summary>
    [HttpGet("roomserver/search_roomsandplaylists/{query}")]
    [HttpGet("search_roomsandplaylists/{query}")]
    public async Task<IActionResult> SearchRoomsAndPlaylists(string query)
    {
        var (merged, _) = await BuildSearchUnionAsync(query);
        return Ok(merged);
    }

    /// <summary>GET <c>search_rooms/{query}</c> — rooms-only search
    /// variant. Same <c>MKAMHOIHOJK</c> union entry shape as
    /// <c>search_roomsandplaylists/{query}</c>, just skips playlist
    /// rows.</summary>
    [HttpGet("search_rooms/{query}")]
    public async Task<IActionResult> SearchRoomsOnly(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Ok(Array.Empty<object>());
        var roomList = await rooms.SearchAsync(query);
        var roomIds = roomList.Select(r => r.Id).ToList();
        var sceneRows = roomIds.Count == 0
            ? new List<RoomSceneEntity>()
            : await db.RoomScenes.Where(s => roomIds.Contains(s.RoomId))
                .OrderBy(s => s.OrderIndex).ToListAsync();
        var scenesByRoom = sceneRows.GroupBy(s => s.RoomId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RoomSceneEntity>)g.ToList());
        return Ok(roomList
            .Select(r => BuildRoomUnionEntry(r, scenesByRoom.GetValueOrDefault(r.Id)))
            .ToList());
    }

    /// <summary>
    /// Shared body for the two search variants. Empty/whitespace query
    /// returns an empty result without round-tripping the DB.
    /// </summary>
    private async Task<(List<IDictionary<string, object>> Entries, int Total)> BuildSearchUnionAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return (new List<IDictionary<string, object>>(), 0);

        // Sequential — shared scoped DbContext isn't thread-safe.
        var roomList = await rooms.SearchAsync(query);
        var playlistList = await playlists.SearchAsync(query);

        var roomIds = roomList.Select(r => r.Id).ToList();
        var sceneRows = await db.RoomScenes
            .Where(s => roomIds.Contains(s.RoomId))
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
        var scenesByRoom = sceneRows
            .GroupBy(s => s.RoomId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RoomSceneEntity>)g.ToList());

        var merged = InterleaveUnion(
            roomList.Select(r => BuildRoomUnionEntry(r, scenesByRoom.GetValueOrDefault(r.Id))),
            playlistList.Select(BuildPlaylistUnionEntry));
        return (merged, roomList.Count + playlistList.Count);
    }

    /// <summary>Round-robin merge of two enumerables so the watch's
    /// hot row alternates between rooms and playlists rather than
    /// stacking one type on top. Once one side runs out the remainder
    /// of the other side spills out in order.</summary>
    private static List<IDictionary<string, object>> InterleaveUnion(
        IEnumerable<IDictionary<string, object>> a,
        IEnumerable<IDictionary<string, object>> b)
    {
        var listA = a.ToList();
        var listB = b.ToList();
        var output = new List<IDictionary<string, object>>(listA.Count + listB.Count);
        int i = 0, j = 0;
        while (i < listA.Count || j < listB.Count)
        {
            if (i < listA.Count) output.Add(listA[i++]);
            if (j < listB.Count) output.Add(listB[j++]);
        }
        return output;
    }

    /// <summary>
    /// Build a union-shape Room entry — same payload as
    /// <see cref="BuildRoomServerDetails"/> but with the additional
    /// lowercase <c>roomId</c> key that the 2020.12
    /// <c>MKAMHOIHOJK</c> factory uses as its Room-vs-Playlist
    /// discriminator (factory at <c>MKAMHOIHOJK.txt:621</c> calls
    /// <c>Dictionary.ContainsKey("roomId")</c>; the actual Room
    /// deserializer at <c>KLCOGEIGEBJ.txt:208-234</c> reads
    /// PascalCase <c>RoomId</c>). Must include BOTH keys.
    /// </summary>
    private static IDictionary<string, object> BuildRoomUnionEntry(
        RoomEntity room,
        IReadOnlyList<RoomSceneEntity>? sceneRows)
    {
        // Start from the canonical RoomServerDetails shape so the union
        // entry carries every field the 2020.12 client expects on a
        // Room subclass (SubRooms, Roles, Stats, etc.). Then add the
        // lowercase 'roomId' discriminator key on top.
        var details = BuildRoomServerDetails(room, sceneRows);
        // Convert anonymous record into a mutable dictionary so we can
        // add the lowercase 'roomId' key that anonymous types can't
        // express (you can't have two properties differing only in
        // letter casing on a single anonymous type).
        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var prop in details.GetType().GetProperties())
        {
            var value = prop.GetValue(details);
            if (value is not null) dict[prop.Name] = value;
        }
        // Discriminator key — lowercase 'roomId'. Without this the
        // MKAMHOIHOJK factory falls through to the Playlist branch
        // and PlaylistId.PPGFHEDFBEA throws on the missing key.
        dict["roomId"] = room.Id;
        return dict;
    }

    /// <summary>
    /// Build a union-shape Playlist entry — the
    /// <see cref="KMKPEOGJDFK"/> branch of MKAMHOIHOJK. Keys read at
    /// <c>MKAMHOIHOJK.txt:516-612</c> (PascalCase base keys) and
    /// <c>KMKPEOGJDFK.txt:68</c> (PascalCase <c>PlaylistId</c>).
    /// Omits the lowercase <c>roomId</c> key so the factory's
    /// Room-vs-Playlist branch falls through to the Playlist
    /// constructor.
    /// </summary>
    public static IDictionary<string, object> BuildPlaylistUnionEntry(PlaylistEntity p)
    {
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["PlaylistId"] = p.Id,
            ["Name"] = p.Name,
            ["Description"] = p.Description,
            ["ImageName"] = p.ImageName,
            ["CreatorAccountId"] = p.CreatorPlayerId,
            ["State"] = 0,
            ["Accessibility"] = 1,
            ["WarningMask"] = 0,
            ["CustomWarning"] = string.Empty,
            ["SupportsLevelVoting"] = false,
            // Playlists are watchable from every device the
            // member rooms support; advertise the broadest set so
            // the watch never filters them out by capability.
            ["IsRRO"] = p.IsCurated,
            ["SupportsScreens"] = true,
            ["SupportsWalkVR"] = true,
            ["SupportsTeleportVR"] = true,
            ["SupportsVRLow"] = true,
            ["SupportsQuest2"] = true,
            ["SupportsMobile"] = true,
            ["SupportsJuniors"] = true,
            ["CreatedAt"] = (p.CreatedAt == default ? DateTime.UtcNow : p.CreatedAt)
                .ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["Stats"] = new
            {
                CheerCount = p.CheerCount,
                FavoriteCount = p.FavoriteCount,
                VisitorCount = p.VisitorCount,
                VisitCount = p.VisitCount,
            },
        };
    }

    // ── Subroom mutations (bare path) ──────────────────────────────────

    public sealed class CreateSubRoomRequest
    {
        public string? Name { get; set; }
        public string? RoomSceneLocationId { get; set; }
        public int? MaxPlayers { get; set; }
        public bool? IsSandbox { get; set; }
    }

    /// <summary>POST <c>rooms/{id}/subrooms</c> — append a new scene.
    /// Owner-gated. OrderIndex = max+1, Name unique within room.</summary>
    [HttpPost("rooms/{roomId:long}/subrooms")]
    [Authorize]
    public async Task<IActionResult> AddSubRoom(long roomId, [FromBody] CreateSubRoomRequest req)
    {
        var pid = this.RequireCurrentPlayerId();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (room.CreatorPlayerId != pid) return Forbid();

        var name = (req.Name ?? string.Empty).Trim();
        if (name.Length == 0) name = "NewScene";
        var collision = await db.RoomScenes.AnyAsync(s => s.RoomId == roomId && s.Name == name);
        if (collision) return Conflict(new { error = "name_taken" });

        var nextOrder = (await db.RoomScenes
            .Where(s => s.RoomId == roomId)
            .Select(s => (int?)s.OrderIndex).MaxAsync() ?? -1) + 1;
        var scene = new RoomSceneEntity
        {
            RoomId = roomId,
            OrderIndex = nextOrder,
            Name = name,
            RoomSceneLocationId = req.RoomSceneLocationId
                ?? "a75f7547-79eb-47c6-8986-6767abcb4f92",
            MaxPlayers = req.MaxPlayers ?? 8,
            IsSandbox = req.IsSandbox ?? false,
        };
        db.RoomScenes.Add(scene);
        await db.SaveChangesAsync();
        return Ok(SceneWire(scene));
    }

    [HttpDelete("rooms/{roomId:long}/subrooms/{subRoomId:long}")]
    [Authorize]
    public async Task<IActionResult> DeleteSubRoom(long roomId, long subRoomId)
    {
        var pid = this.RequireCurrentPlayerId();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (room.CreatorPlayerId != pid) return Forbid();
        var scene = await db.RoomScenes
            .FirstOrDefaultAsync(s => s.RoomId == roomId && s.OrderIndex == subRoomId);
        if (scene is null) return NotFound();
        if (scene.OrderIndex == 0) return BadRequest(new { error = "cannot_delete_entry_scene" });
        db.RoomScenes.Remove(scene);
        await db.SaveChangesAsync();
        return Ok(new { Result = 0 });
    }

    public sealed class SubRoomIntRequest { public int? Value { get; set; } }
    public sealed class SubRoomBoolRequest { public bool? Value { get; set; } }
    public sealed class SubRoomModifyRequest
    {
        public string? Name { get; set; }
        public int? MaxPlayers { get; set; }
        public bool? IsSandbox { get; set; }
        public bool? CanMatchmakeInto { get; set; }
        public string? RoomSceneLocationId { get; set; }
    }
    public sealed class SubRoomMoveRequest { public int? NewIndex { get; set; } }
    public sealed class PublishSubRoomSaveRequest
    {
        public long? SubRoomDataSaveId { get; set; }
        public long? SaveId { get; set; }
        public long? StagedSubRoomDataSaveId { get; set; }
        public string? DataBlob { get; set; }
        public string? DataBlobName { get; set; }
        public string? Filename { get; set; }
    }

    [HttpPost("rooms/{roomId:long}/subrooms/{subRoomId:long}/maxplayers")]
    [HttpPut("rooms/{roomId:long}/subrooms/{subRoomId:long}/maxplayers")]
    [Authorize]
    public Task<IActionResult> SubRoomMaxPlayers(long roomId, long subRoomId, [FromBody] SubRoomIntRequest req) =>
        MutateScene(roomId, subRoomId, s => { if (req.Value is int v) s.MaxPlayers = Math.Max(1, v); });

    [HttpPost("rooms/{roomId:long}/subrooms/{subRoomId:long}/accessibility")]
    [HttpPut("rooms/{roomId:long}/subrooms/{subRoomId:long}/accessibility")]
    [Authorize]
    public Task<IActionResult> SubRoomAccessibility(long roomId, long subRoomId, [FromBody] SubRoomBoolRequest req) =>
        MutateScene(roomId, subRoomId, s => { if (req.Value is bool v) s.CanMatchmakeInto = v; });

    [HttpPost("rooms/{roomId:long}/subrooms/{subRoomId:long}/permissions")]
    [HttpPut("rooms/{roomId:long}/subrooms/{subRoomId:long}/permissions")]
    [HttpPost("roomserver/rooms/{roomId:long}/subrooms/{subRoomId:long}/permissions")]
    [HttpPut("roomserver/rooms/{roomId:long}/subrooms/{subRoomId:long}/permissions")]
    [Authorize]
    public Task<IActionResult> SubRoomPermissions(long roomId, long subRoomId, [FromBody] SubRoomModifyRequest? req) =>
        MutateScene(roomId, subRoomId, s =>
        {
            if (req?.CanMatchmakeInto is bool canMatchmakeInto) s.CanMatchmakeInto = canMatchmakeInto;
            if (req?.MaxPlayers is int maxPlayers) s.MaxPlayers = Math.Max(1, maxPlayers);
            if (req?.IsSandbox is bool isSandbox) s.IsSandbox = isSandbox;
        });

    [HttpPost("rooms/{roomId:long}/subrooms/{subRoomId:long}/publish_save")]
    [HttpPut("rooms/{roomId:long}/subrooms/{subRoomId:long}/publish_save")]
    [HttpPost("roomserver/rooms/{roomId:long}/subrooms/{subRoomId:long}/publish_save")]
    [HttpPut("roomserver/rooms/{roomId:long}/subrooms/{subRoomId:long}/publish_save")]
    [Authorize]
    public async Task<IActionResult> PublishSubRoomSave(long roomId, long subRoomId)
    {
        var pid = this.RequireCurrentPlayerId();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (room.CreatorPlayerId != pid) return Forbid();

        var scene = await GetOrCreateSceneForMutationAsync(room, subRoomId);
        if (scene is null) return NotFound();

        var request = await ReadPublishSubRoomSaveRequestAsync();
        var saveId = request.SubRoomDataSaveId ?? request.SaveId ?? request.StagedSubRoomDataSaveId;
        var dataBlob = request.DataBlobName ?? request.DataBlob ?? request.Filename;

        if (!string.IsNullOrWhiteSpace(dataBlob))
        {
            var blobExists = await db.RoomDataBlobs.AnyAsync(b => b.RoomId == roomId && b.BlobName == dataBlob);
            if (!blobExists) return NotFound(new { error = "blob_not_found", dataBlob });
            scene.DataBlobName = dataBlob;
            if (scene.OrderIndex == 0) room.CurrentDataBlobName = dataBlob;
        }

        if (saveId is > 0)
            scene.StudioSubRoomDataSaveId = saveId.Value;

        scene.DataModifiedAt = DateTime.UtcNow;
        room.UpdatedAt = scene.DataModifiedAt;
        await db.SaveChangesAsync();
        return Ok(SceneWire(scene));
    }

    private async Task<PublishSubRoomSaveRequest> ReadPublishSubRoomSaveRequestAsync()
    {
        var request = new PublishSubRoomSaveRequest();
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            if (long.TryParse(FirstFormValue(form, "subRoomDataSaveId", "SubRoomDataSaveId", "saveId", "SaveId", "stagedSubRoomDataSaveId", "StagedSubRoomDataSaveId"), out var saveId))
                request.SubRoomDataSaveId = saveId;
            request.DataBlobName = FirstFormValue(form, "dataBlobName", "DataBlobName", "dataBlob", "DataBlob", "filename", "Filename");
            return request;
        }

        if (long.TryParse(Request.Query["subRoomDataSaveId"].FirstOrDefault()
                          ?? Request.Query["SubRoomDataSaveId"].FirstOrDefault()
                          ?? Request.Query["saveId"].FirstOrDefault()
                          ?? Request.Query["SaveId"].FirstOrDefault()
                          ?? Request.Query["stagedSubRoomDataSaveId"].FirstOrDefault()
                          ?? Request.Query["StagedSubRoomDataSaveId"].FirstOrDefault(), out var querySaveId))
            request.SubRoomDataSaveId = querySaveId;
        request.DataBlobName = Request.Query["dataBlobName"].FirstOrDefault()
                               ?? Request.Query["DataBlobName"].FirstOrDefault()
                               ?? Request.Query["dataBlob"].FirstOrDefault()
                               ?? Request.Query["DataBlob"].FirstOrDefault()
                               ?? Request.Query["filename"].FirstOrDefault()
                               ?? Request.Query["Filename"].FirstOrDefault();

        if ((Request.ContentLength ?? 0) <= 0) return request;
        try
        {
            var body = await JsonSerializer.DeserializeAsync<PublishSubRoomSaveRequest>(
                Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return body ?? request;
        }
        catch (JsonException)
        {
            return request;
        }
    }

    /// <summary>POST <c>rooms/{id}/subrooms/{sub}/clone</c> — duplicate
    /// a scene (DataBlobName + location pointer). New scene goes at the
    /// end with " Copy" suffix; per-room name uniqueness preserved.</summary>
    [HttpPost("rooms/{roomId:long}/subrooms/{subRoomId:long}/clone")]
    [Authorize]
    public async Task<IActionResult> CloneSubRoom(long roomId, long subRoomId)
    {
        var pid = this.RequireCurrentPlayerId();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (room.CreatorPlayerId != pid) return Forbid();
        var src = await db.RoomScenes
            .FirstOrDefaultAsync(s => s.RoomId == roomId && s.OrderIndex == subRoomId);
        if (src is null) return NotFound();

        var baseName = $"{src.Name} Copy";
        var newName = baseName;
        int suffix = 1;
        while (await db.RoomScenes.AnyAsync(s => s.RoomId == roomId && s.Name == newName))
        {
            suffix += 1;
            newName = $"{baseName} {suffix}";
        }
        var nextOrder = (await db.RoomScenes.Where(s => s.RoomId == roomId)
            .Select(s => (int?)s.OrderIndex).MaxAsync() ?? -1) + 1;
        var clone = new RoomSceneEntity
        {
            RoomId = roomId,
            OrderIndex = nextOrder,
            Name = newName,
            RoomSceneLocationId = src.RoomSceneLocationId,
            DataBlobName = src.DataBlobName,
            MaxPlayers = src.MaxPlayers,
            IsSandbox = src.IsSandbox,
            CanMatchmakeInto = src.CanMatchmakeInto,
        };
        db.RoomScenes.Add(clone);
        await db.SaveChangesAsync();
        return Ok(SceneWire(clone));
    }

    [HttpPost("rooms/{roomId:long}/subrooms/{subRoomId:long}/modify")]
    [HttpPut("rooms/{roomId:long}/subrooms/{subRoomId:long}/modify")]
    [Authorize]
    public Task<IActionResult> ModifySubRoom(long roomId, long subRoomId, [FromBody] SubRoomModifyRequest req) =>
        MutateScene(roomId, subRoomId, s =>
        {
            if (!string.IsNullOrWhiteSpace(req.Name)) s.Name = req.Name.Trim();
            if (req.MaxPlayers is int mp) s.MaxPlayers = Math.Max(1, mp);
            if (req.IsSandbox is bool sb) s.IsSandbox = sb;
            if (req.CanMatchmakeInto is bool cmi) s.CanMatchmakeInto = cmi;
            if (req.RoomSceneLocationId is not null) s.RoomSceneLocationId = req.RoomSceneLocationId;
        });

    /// <summary>POST <c>rooms/{id}/subrooms/{sub}/move</c> — reorder a
    /// scene. Body NewIndex is the target OrderIndex; the in-between
    /// rows shift to fill the gap.</summary>
    [HttpPost("rooms/{roomId:long}/subrooms/{subRoomId:long}/move")]
    [Authorize]
    public async Task<IActionResult> MoveSubRoom(long roomId, long subRoomId, [FromBody] SubRoomMoveRequest req)
    {
        var pid = this.RequireCurrentPlayerId();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (room.CreatorPlayerId != pid) return Forbid();
        var scenes = await db.RoomScenes
            .Where(s => s.RoomId == roomId).OrderBy(s => s.OrderIndex).ToListAsync();
        var moving = scenes.FirstOrDefault(s => s.OrderIndex == subRoomId);
        if (moving is null) return NotFound();
        var newIndex = Math.Clamp(req.NewIndex ?? 0, 0, scenes.Count - 1);
        scenes.Remove(moving);
        scenes.Insert(newIndex, moving);
        // Recompute OrderIndex on the entire row set. The unique
        // (RoomId, OrderIndex) index would clash if we wrote rows in
        // place — flush a sentinel offset first, then settle to
        // final indices.
        const int sentinelOffset = 100_000;
        for (int i = 0; i < scenes.Count; i++) scenes[i].OrderIndex = i + sentinelOffset;
        await db.SaveChangesAsync();
        for (int i = 0; i < scenes.Count; i++) scenes[i].OrderIndex = i;
        await db.SaveChangesAsync();
        return Ok(SceneWire(moving));
    }

    /// <summary>POST <c>roomserver/rooms/{id}/subrooms/{sub}/data</c> —
    /// commit the blob returned by <c>storage/upload</c> as the current
    /// scene save. The 2020 watch posts this as form-urlencoded
    /// (<c>filename</c>, <c>inventionUsage</c>, <c>savedByAccountId</c>),
    /// while some tooling uses JSON. Both variants delegate to the
    /// existing saveData path so room, scene, dorm-state, presence, and
    /// subscription fanout stay consistent.</summary>
    [HttpPost("roomserver/rooms/{roomId:long}/subrooms/{subRoomId:long}/data")]
    [HttpPost("rooms/{roomId:long}/subrooms/{subRoomId:long}/data")]
    [Authorize]
    public async Task<IActionResult> SubRoomData(long roomId, long subRoomId)
    {
        var body = await ReadSaveRoomSceneRequestAsync();
        body.RoomSceneId = subRoomId;
        return await SaveDataCore(body, roomId, wrapCreateModifyResponse: true);
    }

    private async Task<SaveRoomSceneRequest> ReadSaveRoomSceneRequestAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            var request = new SaveRoomSceneRequest
            {
                Filename = FirstFormValue(form, "filename", "Filename"),
                RoomDataFilename = FirstFormValue(form, "roomDataFilename", "RoomDataFilename", "roomDataFileName", "RoomDataFileName"),
                InventionUsageBase64 = FirstFormValue(form, "inventionUsage", "InventionUsage", "inventionUsageBase64", "InventionUsageBase64"),
            };

            if (long.TryParse(FirstFormValue(form, "requestPlayerId", "RequestPlayerId", "savedByAccountId", "SavedByAccountId"), out var requestPlayerId))
                request.RequestPlayerId = requestPlayerId;
            if (int.TryParse(FirstFormValue(form, "saveRequestPlayerId", "SaveRequestPlayerId"), out var saveRequestPlayerId))
                request.SaveRequestPlayerId = saveRequestPlayerId;

            return request;
        }

        if (Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                return await JsonSerializer.DeserializeAsync<SaveRoomSceneRequest>(
                    Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new SaveRoomSceneRequest();
            }
            catch (JsonException)
            {
                return new SaveRoomSceneRequest();
            }
        }

        return new SaveRoomSceneRequest();
    }

    private static string? FirstFormValue(IFormCollection form, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = form[key].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private async Task<IActionResult> MutateScene(long roomId, long subRoomId, Action<RoomSceneEntity> mutator)
    {
        var pid = this.RequireCurrentPlayerId();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (room.CreatorPlayerId != pid) return Forbid();
        var scene = await GetOrCreateSceneForMutationAsync(room, subRoomId);
        if (scene is null) return NotFound();
        mutator(scene);
        scene.DataModifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(SceneWire(scene));
    }

    private async Task<RoomSceneEntity?> GetOrCreateSceneForMutationAsync(RoomEntity room, long subRoomId)
    {
        var scene = await db.RoomScenes
            .FirstOrDefaultAsync(s => s.RoomId == room.Id && s.OrderIndex == subRoomId);
        if (scene is not null || subRoomId != 0) return scene;

        scene = new RoomSceneEntity
        {
            RoomId = room.Id,
            OrderIndex = 0,
            Name = "Home",
            RoomSceneLocationId = room.LocationReplicationId,
            DataBlobName = CurrentOrSyntheticDataBlobName(room),
            MaxPlayers = room.MaxCapacity,
            IsSandbox = false,
            CanMatchmakeInto = true,
            DataModifiedAt = DateTime.UtcNow,
        };
        db.RoomScenes.Add(scene);
        return scene;
    }

    private static object SceneWire(RoomSceneEntity s) => new
    {
        Result = 0,
        RoomId = s.RoomId,
        RoomSceneId = s.OrderIndex,
        SubRoomId = s.OrderIndex,
        Name = s.Name,
        s.DataBlobName,
        RoomSceneLocationId = s.RoomSceneLocationId,
        s.IsSandbox,
        s.MaxPlayers,
        s.CanMatchmakeInto,
    };

    // ── Bare-path roles ────────────────────────────────────────────────

    /// <summary>GET <c>rooms/{id}/roles</c> — account role DTOs consumed by
    /// 2023's EFHPLDPNGIM deserializer.</summary>
    [HttpGet("rooms/{roomId:long}/roles")]
    public async Task<IActionResult> RolesList(long roomId)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        var rows = await db.RoomRoles.Where(r => r.RoomId == roomId).ToListAsync();
        var list = new List<object>(rows.Count + 1)
        {
            BuildRoomAccountRoleWire(room.CreatorPlayerId, 30),
        };
        list.AddRange(rows.Select(BuildRoomRoleGrantWire));
        return Ok(list);
    }

    private static string CurrentOrSyntheticDataBlobName(RoomEntity room)
    {
        if (room.IsDormRoom)
            return RoomService.ResolveWireRoomDataBlobName(room.Id, room.CurrentDataBlobName);

        if (RoomService.IsBakedOriginalRoom(room) && string.IsNullOrWhiteSpace(room.CurrentDataBlobName))
            return string.Empty;

        return !string.IsNullOrWhiteSpace(room.CurrentDataBlobName)
            ? room.CurrentDataBlobName
            : RoomService.SyntheticDefaultRoomDataBlobName(room.Id);
    }

    private static string SceneOrSyntheticDataBlobName(RoomEntity room, string? sceneBlobName, string fallbackBlobName)
    {
        if (!string.IsNullOrWhiteSpace(sceneBlobName) &&
            !(room.IsDormRoom && RoomService.IsLegacySyntheticDefaultRoomDataBlobName(room.Id, sceneBlobName)))
            return sceneBlobName;

        return fallbackBlobName;
    }

    private sealed record StudioBundleInfo(long SaveId, int Target, int Version, string Filename);

    private sealed record BakedUnityAssetWire(
        string UnityAssetId,
        long CreatedByAccountId,
        string Filename,
        int Target,
        int Version,
        string Hash);

    private sealed record UnityAssetWire(
        string UnityAssetId,
        long CreatedByAccountId,
        string Filename,
        BakedUnityAssetWire[] BakedUnityAssets);

    private static BakedUnityAssetWire[] BuildStudioBakedAssets(
        RoomEntity room,
        RoomSceneEntity? scene,
        long saveId,
        int? unityAssetTarget,
        int? unityAssetVersion)
    {
        var bundles = StudioBundlesForScene(scene, saveId);
        if (unityAssetTarget is int target)
            bundles = bundles.Where(b => b.Target == target).ToList();
        if (unityAssetVersion is int requestedVersion && bundles.Any(b => b.Version == requestedVersion))
            bundles = bundles.Where(b => b.Version == requestedVersion).ToList();

        var unityAssetId = scene?.StudioUnityAssetId ?? string.Empty;
        return bundles
            .OrderBy(b => b.Target)
            .ThenByDescending(b => b.Version)
            .Select(b => new BakedUnityAssetWire(
                unityAssetId,
                room.CreatorPlayerId,
                b.Filename,
                b.Target,
                b.Version,
                string.Empty))
            .ToArray();
    }

    private static UnityAssetWire? BuildStudioUnityAsset(
        RoomEntity room,
        RoomSceneEntity? scene,
        BakedUnityAssetWire[] bakedAssets)
    {
        var unityAssetId = scene?.StudioUnityAssetId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(unityAssetId))
            return null;

        var filename = bakedAssets.FirstOrDefault()?.Filename ?? string.Empty;
        return new UnityAssetWire(unityAssetId, room.CreatorPlayerId, filename, bakedAssets);
    }

    private static List<StudioBundleInfo> StudioBundlesForScene(RoomSceneEntity? scene, long saveId)
    {
        return (scene?.StudioAssetBundleNamesCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ParseStudioBundleName)
            .Where(b => b is not null)
            .Cast<StudioBundleInfo>()
            .Where(b => b.SaveId == saveId || scene?.StudioSubRoomDataSaveId == saveId)
            .OrderBy(b => b.Target)
            .ThenByDescending(b => b.Version)
            .ToList();
    }

    private static StudioBundleInfo? ParseStudioBundleName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var match = System.Text.RegularExpressions.Regex.Match(
            name,
            @"^(?<save>\d+)__bundle_t(?<target>\d+)(?:_v(?<version>\d+))?\.assetbundle$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        if (!long.TryParse(match.Groups["save"].Value, out var saveId)) return null;
        if (!int.TryParse(match.Groups["target"].Value, out var target)) return null;
        var version = 0;
        if (match.Groups["version"].Success &&
            !int.TryParse(match.Groups["version"].Value, out version))
            return null;
        return new StudioBundleInfo(saveId, target, version, name);
    }

    [HttpGet("rooms/{roomId:long}/roles/{playerId:long}")]
    public async Task<IActionResult> RoleForPlayer(long roomId, long playerId)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (room.CreatorPlayerId == playerId)
            return Ok(BuildRoomAccountRoleWire(playerId, 30));
        var row = await db.RoomRoles
            .FirstOrDefaultAsync(r => r.RoomId == roomId && r.PlayerId == playerId);
        if (row is null) return NotFound();
        return Ok(BuildRoomRoleGrantWire(row));
    }

    public sealed class GrantRoleRequest { public int? Role { get; set; } }

    /// <summary>POST <c>rooms/{id}/roles/{playerId}</c> — owner grants
    /// a role to a player. Auto-accepted (no invite step).</summary>
    [HttpPut("rooms/{roomId:long}/roles/{playerId:long}")]
    [HttpPost("rooms/{roomId:long}/roles/{playerId:long}")]
    [HttpPut("/api/rooms/v1/rooms/{roomId:long}/roles/{playerId:long}")]
    [HttpPost("/api/rooms/v1/rooms/{roomId:long}/roles/{playerId:long}")]
    [HttpPut("/api/rooms/v2/rooms/{roomId:long}/roles/{playerId:long}")]
    [HttpPost("/api/rooms/v2/rooms/{roomId:long}/roles/{playerId:long}")]
    [Authorize]
    public async Task<IActionResult> GrantRole(long roomId, long playerId)
    {
        var me = this.RequireCurrentPlayerId();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (!await CanManageRoomRolesAsync(room, me)) return Forbid();
        var role = await ReadClientRoomRoleAsync();
        if (role is null) return BadRequest(new { error = "missing_role" });
        if (role.Value == 0)
            return await ClearRoomAccountRoleAsync(room, playerId);

        if (!TryMapClientRoleToRoomRole(role.Value, out var roomRole))
            return BadRequest(new { error = "invalid_role", role });

        var existing = await db.RoomRoles
            .FirstOrDefaultAsync(r => r.RoomId == roomId && r.PlayerId == playerId && r.Role == roomRole);
        if (existing is null)
        {
            await RemoveOtherRoomRolesAsync(roomId, playerId, roomRole);
            db.RoomRoles.Add(new RoomRoleEntity
            {
                RoomId = roomId, PlayerId = playerId, Role = roomRole,
                Accepted = true, GrantedByPlayerId = me,
            });
        }
        else
        {
            existing.Accepted = true;
        }
        await db.SaveChangesAsync();
        return Ok(await BuildRoomServerDetailsWithRolesAsync(roomId));
    }

    /// <summary>POST <c>rooms/{id}/roles/{playerId}/invite</c> — same
    /// as grant but Accepted=false; the target's accept-invite flow
    /// flips the flag (separate endpoint, not yet exposed). For now
    /// invited rows surface in RoomDetails.InvitedCoOwners etc.</summary>
    [HttpPut("rooms/{roomId:long}/roles/{playerId:long}/invite")]
    [HttpPost("rooms/{roomId:long}/roles/{playerId:long}/invite")]
    [HttpPut("/api/rooms/v1/rooms/{roomId:long}/roles/{playerId:long}/invite")]
    [HttpPost("/api/rooms/v1/rooms/{roomId:long}/roles/{playerId:long}/invite")]
    [HttpPut("/api/rooms/v2/rooms/{roomId:long}/roles/{playerId:long}/invite")]
    [HttpPost("/api/rooms/v2/rooms/{roomId:long}/roles/{playerId:long}/invite")]
    [Authorize]
    public async Task<IActionResult> InviteRole(long roomId, long playerId)
    {
        var me = this.RequireCurrentPlayerId();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (!await CanManageRoomRolesAsync(room, me)) return Forbid();
        var role = await ReadClientRoomRoleAsync();
        if (role is null) return BadRequest(new { error = "missing_role" });
        if (role.Value == 0)
            return await ClearRoomAccountRoleAsync(room, playerId);

        if (!TryMapClientRoleToRoomRole(role.Value, out var roomRole))
            return BadRequest(new { error = "invalid_role", role });

        var existing = await db.RoomRoles
            .FirstOrDefaultAsync(r => r.RoomId == roomId && r.PlayerId == playerId && r.Role == roomRole);
        if (existing is null)
        {
            await RemoveOtherRoomRolesAsync(roomId, playerId, roomRole);
            db.RoomRoles.Add(new RoomRoleEntity
            {
                RoomId = roomId, PlayerId = playerId, Role = roomRole,
                Accepted = false, GrantedByPlayerId = me,
            });
        }
        else
        {
            existing.Accepted = false;
        }
        await db.SaveChangesAsync();
        return Ok(await BuildRoomServerDetailsWithRolesAsync(roomId));
    }

    private async Task<IActionResult> ClearRoomAccountRoleAsync(RoomEntity room, long playerId)
    {
        if (room.CreatorPlayerId == playerId)
            return BadRequest(new { error = "cannot_change_creator_role" });

        var rows = await db.RoomRoles
            .Where(r => r.RoomId == room.Id && r.PlayerId == playerId)
            .ToListAsync();
        if (rows.Count > 0)
            db.RoomRoles.RemoveRange(rows);

        await db.SaveChangesAsync();
        return Ok(await BuildRoomServerDetailsWithRolesAsync(room.Id));
    }

    private async Task<bool> CanManageRoomRolesAsync(RoomEntity room, long playerId)
    {
        if (room.CreatorPlayerId == playerId) return true;
        return await db.RoomRoles.AnyAsync(r =>
            r.RoomId == room.Id
            && r.PlayerId == playerId
            && r.Accepted
            && r.Role == 0);
    }

    private async Task<int?> ReadClientRoomRoleAsync()
    {
        int parsed;
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            if (int.TryParse(form["role"].FirstOrDefault(), out parsed)) return parsed;
            if (int.TryParse(form["Role"].FirstOrDefault(), out parsed)) return parsed;
        }

        if (int.TryParse(Request.Query["role"].FirstOrDefault(), out parsed)) return parsed;
        if (int.TryParse(Request.Query["Role"].FirstOrDefault(), out parsed)) return parsed;

        if (Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                var body = await JsonSerializer.DeserializeAsync<GrantRoleRequest>(
                    Request.Body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (body?.Role is int bodyRole) return bodyRole;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return null;
    }

    private async Task RemoveOtherRoomRolesAsync(long roomId, long playerId, int keepRole)
    {
        var rows = await db.RoomRoles
            .Where(r => r.RoomId == roomId && r.PlayerId == playerId && r.Role != keepRole)
            .ToListAsync();
        if (rows.Count > 0)
            db.RoomRoles.RemoveRange(rows);
    }

    private static bool TryMapClientRoleToRoomRole(int clientRole, out int roomRole)
    {
        roomRole = clientRole switch
        {
            30 => 0, // CoOwner
            20 => 1, // Moderator
            10 => 2, // Host
            _ => -1,
        };
        return roomRole >= 0;
    }

    private static object BuildRoomRoleGrantWire(RoomRoleEntity role)
        => BuildRoomAccountRoleWire(
            role.PlayerId,
            ToClientRoomRole(role.Role),
            role.Accepted,
            role.GrantedByPlayerId);

    private static object BuildRoomAccountRoleWire(
        long accountId,
        int role,
        bool accepted = true,
        long? lastChangedByAccountId = null)
        => new
        {
            AccountId = ToClientAccountId(accountId),
            Role = accepted ? role : 0,
            LastChangedByAccountId = ToClientAccountId(lastChangedByAccountId),
            InvitedRole = accepted ? 0 : role,
        };

    private static int ToClientRoomRole(int roomRole) => roomRole switch
    {
        0 => 30, // CoOwner
        1 => 20, // Moderator
        2 => 10, // Host
        _ => 0,
    };

    private static int ToClientAccountId(long accountId)
        => accountId > int.MaxValue
            ? int.MaxValue
            : accountId < int.MinValue
                ? int.MinValue
                : (int)accountId;

    private static int? ToClientAccountId(long? accountId)
        => accountId.HasValue ? ToClientAccountId(accountId.Value) : null;

    private async Task<object> BuildRoomServerDetailsWithRolesAsync(long roomId)
    {
        var room = await db.Rooms.FirstAsync(r => r.Id == roomId);
        var scenes = await db.RoomScenes
            .Where(s => s.RoomId == roomId)
            .OrderBy(s => s.OrderIndex)
            .ToListAsync();
        var roles = await db.RoomRoles
            .Where(r => r.RoomId == roomId)
            .ToListAsync();
        return BuildRoomServerDetails(room, scenes, roles: roles);
    }

    /// <summary>
    /// Synthesise a placeholder Room when the client asks for one we don't
    /// have in the DB. Keeps the deserializer happy — every required key
    /// is populated. Safe defaults: public, AG, default DormRoom location.
    ///
    /// Special case: roomId=1 is the canonical DormRoom id used by
    /// GoToController when the client visits its own dorm. Make sure the
    /// /v4/details/1 follow-up shows "DormRoom" rather than "Room_1".
    /// </summary>
    private static RoomEntity Synthetic(string name, long? id = null)
    {
        var isDorm = (id == 1) || name.Equals("DormRoom", StringComparison.OrdinalIgnoreCase);
        var displayName = isDorm ? "DormRoom" : name;
        return new RoomEntity
        {
            Id = id ?? Math.Abs((long)name.GetHashCode()),
            Name = displayName,
            Description = isDorm
                ? "Your private dorm — yours alone, decorated however you like."
                : $"{displayName} room",
            CreatorPlayerId = 1,
            State = 0,
            Accessibility = isDorm ? 0 /* Private */ : 1 /* Public */,
            IsAGRoom = !isDorm,
            IsDormRoom = isDorm,
            LocationReplicationId = "76d98498-60a1-430c-ab76-b54a29b7a163",
        };
    }
}
