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
///   v1/personaldetails/{id}, v2/personaldetails/{id}.
///
/// All endpoints serialize Rooms via RoomService.ToWireRoom which matches
/// Room.Deserialize at RVA 0x114E430 (PascalCase keys, all 16 required
/// fields plus optional VR-low / mobile / mic-mute flags).
/// </summary>
[ApiController]
public class RoomsController(
    RoomService rooms,
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
    public async Task<IActionResult> Hot(
        [FromQuery] string? roomScoreType,
        [FromQuery] string? tags)
        => Ok((await rooms.HotAsync(tags)).Select(RoomService.ToWireRoom).ToList());

    /// <summary>
    /// `Rooms.SearchForRooms(query)` — free-text room search.
    /// </summary>
    [HttpGet("api/rooms/v1/search")]
    [HttpGet("api/rooms/v2/search")]
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

    /// <summary>GET api/rooms/v1/datahistory/{roomId} — returns the
    /// version history for a room as a list of saves. Drives the
    /// watch's "Restore to old version" UI in the room-settings tab.
    ///
    /// The watch sometimes calls this with <c>roomId=0</c>, which we
    /// interpret as "the caller's currently-presence-tracked room"
    /// (typically their dorm). Anything else is a literal room id.
    ///
    /// Wire shape: <c>List&lt;RoomDataHistoryDTO&gt;</c>. Deserializer
    /// reads EXACTLY three PascalCase keys (verified at
    /// <c>Cpp2IL_ISIL/.../RecNet/Rooms_NestedType_RoomDataHistoryDTO.txt</c>):
    /// <c>RoomDataHistoryId</c> (long), <c>DataBlobName</c> (string),
    /// <c>CreatedAt</c> (DateTime). Extra/wrong keys aren't ignored —
    /// missing required keys throw <c>KeyNotFoundException</c> in
    /// LitJson and the watch shows an empty history list. Don't add
    /// helper fields here; pipe them through a separate admin endpoint
    /// if needed.</summary>
    [HttpGet("api/rooms/v1/datahistory/{roomId:long}")]
    [HttpGet("api/rooms/v2/datahistory/{roomId:long}")]
    public async Task<IActionResult> DataHistory(long roomId)
    {
        // 0 is a sentinel — fall back to the caller's current room
        // from PlayerPresenceService.
        if (roomId == 0 && CurrentPlayerId is long pid &&
            presence.GetRoom(pid)?.RoomId is long currentId)
        {
            roomId = currentId;
        }
        if (roomId == 0) return Ok(Array.Empty<object>());

        var blobs = await db.RoomDataBlobs
            .Where(b => b.RoomId == roomId)
            .OrderByDescending(b => b.UploadedAt)
            .Take(50)
            .Select(b => new
            {
                RoomDataHistoryId = b.Id,
                DataBlobName = b.BlobName,
                CreatedAt = b.UploadedAt,
            })
            .ToListAsync();
        return Ok(blobs);
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
                    roomInstanceId = currentPresence.RoomInstanceId,
                    roomId         = currentPresence.RoomId,
                    subRoomId      = currentPresence.SubRoomId,
                    location       = currentPresence.Location,
                    photonRegionId = currentPresence.PhotonRegionId,
                    photonRoomId   = currentPresence.PhotonRoomId,
                    maxCapacity    = currentPresence.MaxCapacity,
                    isFull         = currentPresence.IsFull,
                    isPrivate      = currentPresence.IsPrivate,
                    isInProgress   = currentPresence.IsInProgress,
                    dataBlob       = currentPresence.DataBlob,
                    eventId        = currentPresence.EventId,
                    name           = currentPresence.Name,
                },
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

    [HttpGet("api/rooms/v4/details/{roomId:long}")]
    [HttpGet("api/rooms/v3/details/{roomId:long}")]
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
        return Ok(BuildRoomDetails(room, scenes, roles: roomRoles));
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

    [HttpGet("api/rooms/v2/myrooms")]
    [Authorize]
    public async Task<IActionResult> MyRooms()
    {
        var pid = CurrentPlayerId;
        if (pid is null) return Ok(Array.Empty<object>());
        return Ok((await rooms.CreatedByAsync(pid.Value)).Select(RoomService.ToWireRoom).ToList());
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
