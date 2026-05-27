using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    [HttpGet("rooms/hot")]
    public async Task<IActionResult> Hot(
        [FromQuery] string? roomScoreType,
        [FromQuery] string? tags)
        => Ok((await rooms.HotAsync(tags)).Select(RoomService.ToWireRoom).ToList());

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
    public IActionResult Filters() => Ok(new
    {
        PinnedFilters = new[] { "community", "recroomoriginal", "featured", "quest" },
        PopularFilters = new[] { "paintball", "dodgeball", "soccer", "lasertag", "discgolf" },
    });

    [HttpGet("api/rooms/v1/tags")]
    public IActionResult Tags() => Ok(new[] {
        "community", "recroomoriginal", "featured", "quest",
        "paintball", "dodgeball", "soccer", "lasertag", "discgolf",
    });

    [HttpGet("api/rooms/v1/pinnedtags")]
    public IActionResult PinnedTags() => Ok(new[] { "community", "recroomoriginal", "featured" });

    [HttpGet("api/rooms/v1/populartags")]
    public IActionResult PopularTags() => Ok(new[] { "paintball", "dodgeball", "soccer" });

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
    /// `Rooms.GetById(roomId)` — same shape as ByName.
    /// </summary>
    [HttpGet("api/rooms/v2/{roomId:long}")]
    [HttpGet("api/rooms/v3/{roomId:long}")]
    [HttpGet("rooms/{roomId:long}")]
    public async Task<IActionResult> ById(long roomId)
    {
        var r = await rooms.GetByIdAsync(roomId);
        return Ok(RoomService.ToWireRoom(r ?? Synthetic($"Room_{roomId}", roomId)));
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

        sceneRow.DataBlobName = filename;
        sceneRow.DataModifiedAt = DateTime.UtcNow;

        // Only stamp the room-level CurrentDataBlobName when restoring the
        // entry scene (OrderIndex 0). Restoring a non-entry sub-room
        // shouldn't rewrite what the room loads at the front door.
        if (sceneRow.OrderIndex == 0)
        {
            await db.Rooms
                .Where(r => r.Id == roomId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.CurrentDataBlobName, filename)
                    .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));
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

        // Watch's response deserializer is PPENFJMFPNE — the full Room DTO
        // (verified at EJDCNGBEICB:6029 in the 2020.12.18 dump). Returning
        // a bare RoomScene wrapper makes its dict-based decoder throw
        // KeyNotFoundException ("Failed to restore subroom save: Malformed
        // Response"). Mirror the /roomserver/rooms/{id} shape — same
        // payload BuildRoomDetails generates for /v4/details.
        return Ok(roomDetailsPayload);
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
    {
        var pid = this.RequireCurrentPlayerId();

        // Resolve the room. The watch sends RoomSceneId (the scene's
        // OrderIndex per BuildRoomDetails above — 0 for single-scene
        // rooms). To find the parent room we cross-reference the
        // player's current presence (which RecordResponseAsync stamps
        // on every /goto). This is the only signal we have without a
        // RoomId on the request body.
        var current = presence.GetRoom(pid);
        if (current is null)
        {
            // No active room — without it we can't pick a target row.
            // Return a degenerate RoomScene so the deserializer doesn't
            // throw, but flag CanMatchmakeInto=false so the watch
            // doesn't think the save succeeded.
            return Ok(new
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
            });
        }

        var roomId = current.RoomId;
        var room = await rooms.GetByIdAsync(roomId);
        if (room is null) return NotFound(new { error = "room_not_found" });

        var newBlob = body.RoomDataFilename?.Trim() ?? string.Empty;

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
            dormState.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // Owned rooms: only the creator can save. Admin override
            // intentionally not included — admins should use /restore
            // when they need to roll a room back.
            if (room.CreatorPlayerId != pid) return Forbid();
        }

        if (sceneRow is not null)
        {
            sceneRow.DataBlobName = newBlob;
            sceneRow.DataModifiedAt = DateTime.UtcNow;
        }
        await db.Rooms
            .Where(r => r.Id == roomId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CurrentDataBlobName, newBlob)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));
        await db.SaveChangesAsync();

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
                scene.DataModifiedAt = DateTime.UtcNow;
            }
        }
        var savedSceneId = sceneRow?.OrderIndex ?? body.RoomSceneId;
        var roomDetailsPayload = BuildRoomDetails(pushRoom, sceneRowsForRoom, savedSceneId, newBlob);
        logger.LogInformation(
            "[rooms-save] saveData player={PlayerId} room={RoomId} requestedScene={RequestedSceneId} savedScene={SavedSceneId} blob={BlobName} sceneRow={SceneRowFound} scenes={SceneCount}",
            pid, roomId, body.RoomSceneId, savedSceneId, newBlob, sceneRow is not null, sceneRowsForRoom.Count);
        var playersInInstance = onlinePresence.OnlinePlayerIds()
            .Where(playerId =>
            {
                var playerRoom = presence.GetRoom(playerId);
                return playerRoom is not null
                    && playerRoom.RoomId == roomId
                    && playerRoom.RoomInstanceId == current.RoomInstanceId;
            })
            .Append(pid)
            .Distinct()
            .ToArray();
        logger.LogInformation(
            "[rooms-save] fanout room update player={PlayerId} room={RoomId} instance={InstanceId} blob={BlobName} recipients={RecipientCount}",
            pid, roomId, current.RoomInstanceId, newBlob, playersInInstance.Length);

        var updatedRoomInstances = new Dictionary<long, RoomInstanceDto>();
        foreach (var playerId in playersInInstance)
        {
            var currentPresence = presence.GetRoom(playerId);
            if (currentPresence is null && playerId == pid)
            {
                currentPresence = current;
            }
            if (currentPresence is null) continue;

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
                    name             = currentPresence.Name,
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

        return Ok(new
        {
            RoomSceneId = savedSceneId,
            RoomId = roomId,
            RoomSceneLocationId = sceneRow?.RoomSceneLocationId ?? room.LocationReplicationId,
            Name = sceneRow?.Name ?? "Home",
            IsSandbox = sceneRow?.IsSandbox ?? false,
            DataBlobName = newBlob,
            MaxPlayers = sceneRow?.MaxPlayers ?? 8,
            CanMatchmakeInto = sceneRow?.CanMatchmakeInto ?? true,
            DataModifiedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        });
    }

    public class SaveRoomSceneRequest
    {
        public long RoomSceneId { get; set; }
        public string? RoomDataFilename { get; set; }
        public List<long>? InventionUsages { get; set; }
        public CreatorActionContextDto? CreatorActionContext { get; set; }
        public long RequestPlayerId { get; set; }
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
            var dormBlobName = await db.DormStates
                .Where(d => d.PlayerId == room.CreatorPlayerId)
                .Select(d => d.CurrentDataBlobName)
                .FirstOrDefaultAsync()
                ?? string.Empty;
            room = CloneWithCreator(room, room.CreatorPlayerId, dormBlobName);
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
            var dormBlobName = await db.DormStates
                .Where(d => d.PlayerId == room.CreatorPlayerId)
                .Select(d => d.CurrentDataBlobName)
                .FirstOrDefaultAsync()
                ?? string.Empty;
            room = CloneWithCreator(room, room.CreatorPlayerId, dormBlobName);
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

    /// <summary>GET <c>/roomserver/rooms/bulk?name=X&amp;name=Y</c> — bulk
    /// lookup by room name. Used by EJDCNGBEICB's room cache to resolve
    /// well-known names (RecCenter, etc.) to their RoomServerDetails
    /// shape in one round-trip. Returns the same per-room object that
    /// <c>/roomserver/rooms/{id}</c> emits, one per requested name; names
    /// we can't resolve are silently skipped (watch handles a shorter
    /// list fine, but a 404 wedges the room browser).</summary>
    [HttpGet("roomserver/rooms/bulk")]
    [HttpGet("rooms/bulk")]
    public async Task<IActionResult> RoomServerBulkByName()
    {
        var names = Request.Query["name"].Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
        if (names.Length == 0) return Ok(Array.Empty<object>());

        var pid = CurrentPlayerId ?? 0;
        var results = new List<object>(names.Length);
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
                var dormBlobName = await db.DormStates
                    .Where(d => d.PlayerId == room.CreatorPlayerId)
                    .Select(d => d.CurrentDataBlobName)
                    .FirstOrDefaultAsync()
                    ?? string.Empty;
                room = CloneWithCreator(room, room.CreatorPlayerId, dormBlobName);
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
    public static RoomEntity CloneWithCreator(
        RoomEntity src, long creatorId, string? overrideBlobName = null) => new()
    {
        Id = src.Id,
        Name = src.Name,
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
        RoomWarningMask = src.RoomWarningMask,
        CustomRoomWarning = src.CustomRoomWarning,
        DisableMicAutoMute = src.DisableMicAutoMute,
        LocationReplicationId = src.LocationReplicationId,
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
                .Select(t => (object)new { Type = 0, Tag = t })
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
        //   • Dorm / customisable rooms: DataBlobName is either the
        //     latest uploaded blob name (RoomEntity.CurrentDataBlobName,
        //     written by StorageController on /upload) or, before any
        //     save, "room_<id>_v1.dat" — which the catch-all serves
        //     from RoomDataBlobService (the all-perms default blob).
        //   • AG-Original rooms: DataBlobName="" → completed-promise
        //     short-circuit → no download → no leaked stale-blob
        //     behaviour → master flow proceeds → spawn fires. Maker
        //     Pen stays locked, which matches public-server semantics
        //     (you can't edit the rec center).
        //
        // Heuristic: "customisable" iff it's the dorm OR a user-created
        // room (CreatorPlayerId != 1, the seeded system account that
        // owns AG-Originals like the rec center). User clones inherit
        // the all-perms default blob until their first save replaces
        // it via StorageController; the rec center / paintball / etc.
        // serve "" so the persistence flow short-circuits.
        var customisable = room.IsDormRoom || room.CreatorPlayerId != 1;
        var dataBlobName = !string.IsNullOrEmpty(room.CurrentDataBlobName)
            ? room.CurrentDataBlobName
            : customisable ? $"room_{room.Id}_v1.dat" : "";

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
                        : s.DataBlobName,
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
                    MaxPlayers = 8,
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
        var roleList = roles ?? Array.Empty<RoomRoleEntity>();
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
        IReadOnlyList<RoomRoleEntity>? roles = null)
    {
        var customisable = room.IsDormRoom || room.CreatorPlayerId != 1;
        var dataBlobName = !string.IsNullOrEmpty(room.CurrentDataBlobName)
            ? room.CurrentDataBlobName
            : customisable ? $"room_{room.Id}_v1.dat" : "";
        var updatedAt = (room.UpdatedAt == default ? DateTime.UtcNow : room.UpdatedAt)
            .ToString("yyyy-MM-ddTHH:mm:ssZ");

        object[] subRooms = sceneRows is { Count: > 0 }
            ? sceneRows.Select(s => (object)new
            {
                SubRoomId = (long)s.OrderIndex,
                RoomId = room.Id,
                Name = s.Name,
                DataBlob = !string.IsNullOrWhiteSpace(overrideDataBlobName) && sceneRows.Count == 1
                    ? overrideDataBlobName
                    : s.DataBlobName,
                DataSavedAt = (s.DataModifiedAt == default ? DateTime.UtcNow : s.DataModifiedAt)
                    .ToString("yyyy-MM-ddTHH:mm:ssZ"),
                IsSandbox = s.IsSandbox,
                MaxPlayers = s.MaxPlayers,
                Accessibility = room.Accessibility,
                UnitySceneId = s.RoomSceneLocationId,
            }).ToArray()
            : new object[]
            {
                new
                {
                    SubRoomId = 0L,
                    RoomId = room.Id,
                    Name = "Home",
                    DataBlob = dataBlobName,
                    DataSavedAt = updatedAt,
                    IsSandbox = false,
                    MaxPlayers = 8,
                    Accessibility = room.Accessibility,
                    UnitySceneId = room.LocationReplicationId,
                },
            };

        var roleList = roles ?? Array.Empty<RoomRoleEntity>();
        static int WireRole(int role) => role switch
        {
            0 => 30, // CoOwner
            1 => 20, // Moderator
            2 => 10, // Host
            _ => 0,
        };
        var wireRoles = new List<object>
        {
            new { AccountId = room.CreatorPlayerId, Role = 255, InvitedRole = 0 },
        };
        wireRoles.AddRange(roleList.Select(r =>
        {
            var role = WireRole(r.Role);
            return (object)new
            {
                AccountId = r.PlayerId,
                Role = r.Accepted ? role : 0,
                InvitedRole = r.Accepted ? 0 : role,
            };
        }));

        var tags = string.IsNullOrEmpty(room.TagsCsv)
            ? Array.Empty<object>()
            : room.TagsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => (object)new { Type = 0, Tag = t })
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
            CloningAllowed = room.CloningAllowed,
            DisableMicAutoMute = room.DisableMicAutoMute,
            DisableRoomComments = false,
            EncryptVoiceChat = false,
            SubRooms = subRooms,
            Roles = wireRoles,
            LoadScreens = Array.Empty<object>(),
            PromoImages = Array.Empty<string>(),
            PromoExternalContent = Array.Empty<object>(),
            Tags = tags,
        };
    }


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
    [HttpGet("rooms/createdby/{otherPlayerId:long}")]
    public async Task<IActionResult> CreatedByOther(long otherPlayerId)
        => Ok((await rooms.CreatedByAsync(otherPlayerId)).Select(RoomService.ToWireRoom).ToList());

    [HttpGet("api/rooms/v1/visitedby/me")]
    [HttpGet("api/rooms/v2/visitedby/me")]
    [HttpGet("api/rooms/v3/visitedby/me")]
    [HttpGet("roomserver/rooms/visitedby/me")]
    [HttpGet("rooms/visitedby/me")]
    [Authorize]
    public IActionResult VisitedByMe() => Ok(Array.Empty<object>());

    [HttpGet("api/rooms/v1/moderatedby/me")]
    [HttpGet("api/rooms/v2/moderatedby/me")]
    [HttpGet("api/rooms/v3/moderatedby/me")]
    [HttpGet("roomserver/rooms/moderatedby/me")]
    [HttpGet("rooms/moderatedby/me")]
    [Authorize]
    public IActionResult ModeratedByMe() => Ok(Array.Empty<object>());

    [HttpGet("api/rooms/v1/favoritedby/me")]
    [HttpGet("api/rooms/v2/favoritedby/me")]
    [HttpGet("api/rooms/v3/favoritedby/me")]
    [HttpGet("roomserver/rooms/favoritedby/me")]
    [HttpGet("rooms/favoritedby/me")]
    [Authorize]
    public IActionResult FavoritedByMe() => Ok(Array.Empty<object>());

    [HttpGet("api/rooms/v1/cheeredby/me")]
    [HttpGet("api/rooms/v2/cheeredby/me")]
    [HttpGet("api/rooms/v3/cheeredby/me")]
    [HttpGet("roomserver/rooms/cheeredby/me")]
    [HttpGet("rooms/cheeredby/me")]
    [Authorize]
    public IActionResult CheeredByMe() => Ok(Array.Empty<object>());

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
    public async Task<IActionResult> FeaturedRoomsCurrent() => Ok(new
    {
        FeaturedRoomGroupId = 1L,
        Name = "Featured",
        // NMPFCIJPODA.PPGFHEDFBEA (NMPFCIJPODA.txt:100-125) reads
        // 3 strict keys, not 2 — FeaturedRoomGroupId, Name, AND
        // Rooms (List<PPKJFAAAGDO>). Returning without the third
        // key throws KeyNotFoundException on the watch and the
        // Featured carousel stays empty.
        Rooms = await rooms.FeaturedAgRoomIdsAsync(12),
    });

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
                .Select(t => (object)new { Type = 0, Tag = t })
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

    /// <summary>POST <c>rooms/{id}/subrooms/{sub}/data</c> — bare-path
    /// equivalent of <c>api/rooms/v4/saveData</c>. Delegates to the
    /// existing save logic so the watch's two URL variants land on the
    /// same persistence path.</summary>
    [HttpPost("rooms/{roomId:long}/subrooms/{subRoomId:long}/data")]
    [Authorize]
    public Task<IActionResult> SubRoomData(long roomId, long subRoomId,
        [FromBody] SaveRoomSceneRequest body)
    {
        body.RoomSceneId = subRoomId;
        return SaveData(body);
    }

    private async Task<IActionResult> MutateScene(long roomId, long subRoomId, Action<RoomSceneEntity> mutator)
    {
        var pid = this.RequireCurrentPlayerId();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (room.CreatorPlayerId != pid) return Forbid();
        var scene = await db.RoomScenes
            .FirstOrDefaultAsync(s => s.RoomId == roomId && s.OrderIndex == subRoomId);
        if (scene is null) return NotFound();
        mutator(scene);
        scene.DataModifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(SceneWire(scene));
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

    /// <summary>GET <c>rooms/{id}/roles</c> — accepted role grants on
    /// the room. Owner is implicit (CreatorPlayerId) and surfaces under
    /// Role=30; co-owner/mod/host rows come from RoomRoles.</summary>
    [HttpGet("rooms/{roomId:long}/roles")]
    public async Task<IActionResult> RolesList(long roomId)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        var rows = await db.RoomRoles.Where(r => r.RoomId == roomId).ToListAsync();
        var list = new List<object>(rows.Count + 1)
        {
            new { RoomId = roomId, PlayerId = (int)room.CreatorPlayerId, Role = 30 /*Owner*/, Accepted = true },
        };
        list.AddRange(rows.Select(r => (object)new
        {
            RoomId = roomId,
            PlayerId = (int)r.PlayerId,
            Role = r.Role,
            Accepted = r.Accepted,
        }));
        return Ok(list);
    }

    [HttpGet("rooms/{roomId:long}/roles/{playerId:long}")]
    public async Task<IActionResult> RoleForPlayer(long roomId, long playerId)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (room.CreatorPlayerId == playerId)
            return Ok(new { RoomId = roomId, PlayerId = (int)playerId, Role = 30, Accepted = true });
        var row = await db.RoomRoles
            .FirstOrDefaultAsync(r => r.RoomId == roomId && r.PlayerId == playerId);
        if (row is null) return NotFound();
        return Ok(new { RoomId = roomId, PlayerId = (int)playerId, Role = row.Role, Accepted = row.Accepted });
    }

    public sealed class GrantRoleRequest { public int? Role { get; set; } }

    /// <summary>POST <c>rooms/{id}/roles/{playerId}</c> — owner grants
    /// a role to a player. Auto-accepted (no invite step).</summary>
    [HttpPost("rooms/{roomId:long}/roles/{playerId:long}")]
    [Authorize]
    public async Task<IActionResult> GrantRole(long roomId, long playerId, [FromBody] GrantRoleRequest req)
    {
        var me = this.RequireCurrentPlayerId();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (room.CreatorPlayerId != me) return Forbid();
        var role = req.Role ?? 0;
        var existing = await db.RoomRoles
            .FirstOrDefaultAsync(r => r.RoomId == roomId && r.PlayerId == playerId && r.Role == role);
        if (existing is null)
        {
            db.RoomRoles.Add(new RoomRoleEntity
            {
                RoomId = roomId, PlayerId = playerId, Role = role,
                Accepted = true, GrantedByPlayerId = me,
            });
        }
        else
        {
            existing.Accepted = true;
        }
        await db.SaveChangesAsync();
        return Ok(new { RoomId = roomId, PlayerId = (int)playerId, Role = role, Accepted = true });
    }

    /// <summary>POST <c>rooms/{id}/roles/{playerId}/invite</c> — same
    /// as grant but Accepted=false; the target's accept-invite flow
    /// flips the flag (separate endpoint, not yet exposed). For now
    /// invited rows surface in RoomDetails.InvitedCoOwners etc.</summary>
    [HttpPost("rooms/{roomId:long}/roles/{playerId:long}/invite")]
    [Authorize]
    public async Task<IActionResult> InviteRole(long roomId, long playerId, [FromBody] GrantRoleRequest req)
    {
        var me = this.RequireCurrentPlayerId();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (room.CreatorPlayerId != me) return Forbid();
        var role = req.Role ?? 0;
        var existing = await db.RoomRoles
            .FirstOrDefaultAsync(r => r.RoomId == roomId && r.PlayerId == playerId && r.Role == role);
        if (existing is null)
        {
            db.RoomRoles.Add(new RoomRoleEntity
            {
                RoomId = roomId, PlayerId = playerId, Role = role,
                Accepted = false, GrantedByPlayerId = me,
            });
        }
        else
        {
            existing.Accepted = false;
        }
        await db.SaveChangesAsync();
        return Ok(new { RoomId = roomId, PlayerId = (int)playerId, Role = role, Accepted = false });
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
