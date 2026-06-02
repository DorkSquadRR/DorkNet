using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using DorkNet.Server.Auth;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Match;

/// <summary>
/// match.rec.net/goto/* — matchmaking endpoints used by
/// RecNet.Matchmaking.GoToHelper / GoToDormRoom (the latter is what
/// BootSequence.LoadDormRoom invokes during initial scene load).
///
/// Response shape verified by disassembling
/// RecNet.Matchmaking.MatchmakingResponse.Deserialize (RVA 0x1447DD0):
///   "errorCode"     - int, optional (Util.GetKeyOrDefault&lt;int&gt;)
///   "roomInstance"  - RoomInstance object, REQUIRED. The deserializer
///                     throws explicitly with the error
///                     "Matchmaking response has no RoomInstance field"
///                     when this key is missing or non-object.
///
/// The nested RoomInstance shape (RVA 0x114DAB0) requires:
///   roomInstanceId, roomId, subRoomId, location, photonRegionId,
///   photonRoomId, maxCapacity, isFull, isPrivate, isInProgress
/// (all required) plus optional dataBlob, eventId, name.
///
/// The client then JOINS the Photon room named by photonRoomId in the
/// region photonRegionId — these two values are what tie our HTTP
/// matchmaking to the actual realtime networking.
/// </summary>
[ApiController]
public class GoToController(
    IConfiguration config,
    PlayerPresenceService presence,
    PrivateInstanceService privateInstances,
    RoomService rooms,
    DorkNet.Server.Data.DorkNetDbContext db,
    NotificationService notifications,
    JoinTimeoutService joinTimeouts,
    ILogger<GoToController> logger) : ControllerBase
{
    [HttpPost("/goto/room/{roomName}")]
    public async Task<ActionResult<MatchmakingResponseDto>> GoToRoom(
        string roomName,
        [FromForm(Name = "CreatePrivateInstance")] bool createPrivateInstance = false)
    {
        // Unknown room name shouldn't silently drop the player into a
        // synth'd dorm scene any more — that masked typo bug reports
        // ("why does /goto/^Crescendoo land me in my dorm?") and let
        // dead links sit unfixed. Surface the real "no such room"
        // error so the watch shows its standard "room not found" toast
        // and the typo is obvious. DormRoom is a special case (per-player
        // lazy-create), everything else MUST exist in db.Rooms.
        if (!roomName.Equals("DormRoom", StringComparison.OrdinalIgnoreCase))
        {
            var exists = await rooms.GetByNameAsync(roomName);
            if (exists is null)
            {
                logger.LogWarning("[goto] unknown room name='{Name}' — returning RoomDoesNotExist", roomName);
                return Ok(new MatchmakingResponseDto
                {
                    // 4 = RoomDoesNotExist on
                    // RecNet.Matchmaking.CreateModifyRoomStatus enum.
                    // The watch reads ErrorCode != 0 and surfaces the
                    // standard "Room not found" toast.
                    ErrorCode = 4,
                    RoomInstance = null,
                });
            }
        }
        var resp = await BuildResponseAsync(roomName);
        if (createPrivateInstance) await ApplyPrivateInstanceAsync(resp, subRoomId: 0);
        await RecordResponseAsync(resp);
        return Ok(resp);
    }

    [HttpPost("/goto/room")]
    public async Task<ActionResult<MatchmakingResponseDto>> GoToRoomFromBody(
        [FromForm(Name = "RoomName")] string? roomNameForm,
        [FromForm(Name = "roomName")] string? roomNameLower,
        [FromForm(Name = "CreatePrivateInstance")] bool createPrivateInstance = false)
    {
        var roomName = roomNameForm
                       ?? roomNameLower
                       ?? Request.Query["RoomName"].FirstOrDefault()
                       ?? Request.Query["roomName"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(roomName))
            return BadRequest(new { error = "missing roomName" });
        return await GoToRoom(roomName, createPrivateInstance);
    }

    [HttpPost("/goto/none")]
    public IActionResult GoToNone() => Ok();

    /// <summary>POST <c>/goto/room/{room}/{subRoom}</c> — sub-room form
    /// from <c>RecNet.Matchmaking.GoToSubRoom</c>
    /// (<c>Cpp2IL_ISIL/.../Matchmaking.txt:4682-4702</c>):
    /// <c>String.Concat("goto/room/", urlEncode(room), "/", urlEncode(subRoom))</c>.
    /// Used by baked AG-Original rooms with multiple scenes (e.g.
    /// <c>StuntRunner/TheMainEvent</c>, the playable gauntlet inside
    /// the StuntRunner lobby).
    ///
    /// We resolve to the parent room's <see cref="RoomEntity"/> for
    /// the canonical RoomId + LocationReplicationId, then expose the
    /// sub-room name via the <c>name</c> field so the client can
    /// render the right scene. If we ever build a per-sub-room scene
    /// location lookup table, switch <see cref="RoomInstanceDto.Location"/>
    /// to the matching scene's ReplicationId.</summary>
    [HttpPost("/goto/room/{roomName}/{subRoomName}")]
    public async Task<ActionResult<MatchmakingResponseDto>> GoToSubRoom(
        string roomName, string subRoomName,
        [FromForm(Name = "CreatePrivateInstance")] bool createPrivateInstance = false)
    {
        // Build (don't record yet) — we need to mutate the RoomInstance
        // for the sub-room before presence is recorded, otherwise the
        // server presence has the lobby's RoomInstance but the client
        // thinks it's in the sub-room → heartbeat sees a mismatch and
        // the watch logs "presence out-of-sync" / Error -2.
        var resp = await BuildResponseAsync(roomName);

        // Imported multi-scene rooms (admin Import Room) get one
        // RoomSceneEntity row per chapter — prefer that when the
        // (room, subRoom) pair matches. Falls back to the hardcoded
        // baked-room map for AG-Originals like StuntRunner/TheMainEvent.
        //
        // Lookup is tolerant: in-game portals don't always pass the
        // canonical scene Name. We match in this order:
        //   1. exact (case-insensitive — RoomScenes.Name is NOCASE collated)
        //   2. EndsWith(subRoomName) — handles portals that omit
        //      a "Ch0_" / "Chapter1_" prefix
        //   3. Contains(subRoomName) — last-resort fuzzy match
        // and fall back to the hardcoded location map only when no
        // RoomScenes row matches at all.
        var allScenes = await db.RoomScenes
            .Where(s => s.RoomId == resp.RoomInstance.RoomId)
            .Select(s => new { s.OrderIndex, s.Name, s.RoomSceneLocationId, s.DataBlobName })
            .ToListAsync();

        var dbScene =
            allScenes.FirstOrDefault(s =>
                string.Equals(s.Name, subRoomName, StringComparison.OrdinalIgnoreCase))
            ?? allScenes.FirstOrDefault(s =>
                s.Name.EndsWith(subRoomName, StringComparison.OrdinalIgnoreCase))
            ?? allScenes.FirstOrDefault(s =>
                s.Name.IndexOf(subRoomName, StringComparison.OrdinalIgnoreCase) >= 0);

        long subId;
        string? subLocation;
        string? subDataBlob;
        if (dbScene is not null)
        {
            logger.LogInformation(
                "[goto-subroom] {Room}/{Requested} → matched scene '{MatchedName}' idx={Idx} loc={Loc} blob={Blob}",
                roomName, subRoomName, dbScene.Name, dbScene.OrderIndex,
                dbScene.RoomSceneLocationId, dbScene.DataBlobName);
            subId = dbScene.OrderIndex;
            subLocation = dbScene.RoomSceneLocationId;
            subDataBlob = dbScene.DataBlobName;
        }
        else
        {
            logger.LogWarning(
                "[goto-subroom] {Room}/{Requested} — no RoomScenes match (room has {Count} scenes: {Names}). Falling back to hardcoded MapSubRoomToLocationId.",
                roomName, subRoomName, allScenes.Count,
                string.Join(", ", allScenes.Select(s => s.Name)));
            subId = Math.Abs((long)subRoomName.GetHashCode());
            subLocation = MapSubRoomToLocationId($"{roomName}/{subRoomName}".ToLowerInvariant());
            subDataBlob = null;
        }

        // Both PhotonRoomId AND RoomInstanceId must differ for the
        // watch's transition state machine to fire. Empirical from
        // comparing the two failure modes:
        //   Same photon + same roomInstanceId → blob downloads (because
        //     dataBlob field changed) but watch never transitions; the
        //     "Going to X" dialog hangs because nothing the state
        //     machine tracks signalled "I'm in a different match".
        //   Different photon + same roomInstanceId → watch doesn't
        //     even fetch the new dataBlob; it sees roomInstanceId
        //     unchanged so treats the response as a no-op presence
        //     update and skips the join flow.
        // Folding subId into RoomInstanceId AND forking PhotonRoomId
        // gives the watch the two signals it needs: "new instance"
        // (roomInstanceId differs → reset state machine) plus "new
        // photon match" (photonRoomId differs → leave/rejoin →
        // SceneLoaded events that ultimately dismiss the dialog).
        resp.RoomInstance.RoomInstanceId = resp.RoomInstance.RoomId * 100_000L + subId;
        resp.RoomInstance.PhotonRoomId = $"{resp.RoomInstance.PhotonRoomId}_sub{subId}";
        resp.RoomInstance.Name = subRoomName;
        resp.RoomInstance.SubRoomId = subId;
        if (subLocation is not null) resp.RoomInstance.Location = subLocation;
        // dataBlob on the matchmaking response tells the client which
        // PersistedRoomData blob to download for the entered scene.
        // RoomInstance.Deserialize at RVA 0x114DAB0 reads it via the
        // string-with-default reader (call 0x180EE6210), so empty is
        // technically legal — but if it's empty the watch falls back
        // to the parent room's RoomDataBlobName, which is the lobby's
        // blob, so the sub-scene visually never changes.
        if (!string.IsNullOrEmpty(subDataBlob)) resp.RoomInstance.DataBlob = subDataBlob;

        // Private instance mutation goes AFTER sub-room mutation so the
        // PhotonRoomId we register includes the _sub{N} suffix as its
        // base — invitees coming in via /goto/instance/{id} land in the
        // exact same sub-room photon match, not the lobby's.
        if (createPrivateInstance) await ApplyPrivateInstanceAsync(resp, subRoomId: subId);

        await RecordResponseAsync(resp);
        return Ok(resp);
    }

    /// <summary>Per-sub-room scene location overrides for baked
    /// AG-Original rooms with multiple scenes. Each entry maps a
    /// <c>"{room}/{subRoom}"</c> key (lowercase) to the
    /// <c>RoomSceneLocationId</c> GUID baked into the client's
    /// AGRoomRuntimeConfig.Locations table. Without an entry the
    /// parent room's location is reused (lobby loads, door opens
    /// but transition no-ops).
    ///
    /// Add entries here as you confirm each sub-scene's GUID — the
    /// client's <c>AGRoomSettings.TryGetRoomSceneLocationById</c>
    /// rejects unknown GUIDs with "RecNet game session contains
    /// unknown scene location ID" and refuses to load.</summary>
    /// <summary>Sub-room scene GUIDs extracted from the bundled
    /// <c>resources.assets</c> AGRoomRuntimeConfig.Rooms[].Scenes[]
    /// using <c>tools/extract-rooms-binary.py</c>. Each entry maps
    /// <c>"{room}/{subRoom}"</c> (lowercase) to the target
    /// <c>RoomSceneLocationId</c> GUID. Missing sub-rooms reuse the
    /// parent room's lobby location (door opens but transition no-ops).</summary>
    private static string? MapSubRoomToLocationId(string subKey) => subKey switch
    {
        // ── StuntRunner ──
        // Lobby is the implicit Scene[0]; TheMainEvent is the
        // playable gauntlet behind the door.
        "stuntrunner/themainevent"     => "3a636bd2-f896-424c-9225-c184522c0d87",

        // ── Paintball (River map + variants) ──
        // The Paintball Room has nested sub-rooms for each map; the
        // lobby acts as a hub and players pick a map from a board.
        "paintball/river"              => "e122fe98-e7db-49e8-a1b1-105424b6e1f0",
        "paintball/homestead"          => "a785267d-c579-42ea-be43-fec1992d1ca7",
        "paintball/quarry"             => "ff4c6427-7079-4f59-b22a-69b089420827",
        "paintball/clearcut"           => "380d18b5-de9c-49f3-80f7-f4a95c1de161",
        "paintball/spillway"           => "58763055-2dfb-4814-80b8-16fac5c85709",
        "paintballvr/river"            => "e122fe98-e7db-49e8-a1b1-105424b6e1f0",
        "paintballvr/homestead"        => "a785267d-c579-42ea-be43-fec1992d1ca7",
        "paintballvr/quarry"           => "ff4c6427-7079-4f59-b22a-69b089420827",
        "paintballvr/clearcut"         => "380d18b5-de9c-49f3-80f7-f4a95c1de161",
        "paintballvr/spillway"         => "58763055-2dfb-4814-80b8-16fac5c85709",

        // ── LaserTag ──
        "lasertag/cyberjunkcity"       => "9d6456ce-6264-48b4-808d-2d96b3d91038",

        _ => null,
    };

    [HttpPost("/goto/event/{eventId:long}")]
    public async Task<ActionResult<MatchmakingResponseDto>> GoToEvent(long eventId)
    {
        var resp = await BuildResponseAsync("DormRoom");
        await RecordResponseAsync(resp);
        return Ok(resp);
    }

    [HttpPost("/goto/club/{clubId:long}")]
    public async Task<ActionResult<MatchmakingResponseDto>> GoToClub(long clubId)
    {
        var club = await db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clubId);
        if (club is null)
        {
            return Ok(new MatchmakingResponseDto
            {
                ErrorCode = 4,
                RoomInstance = new RoomInstanceDto(),
            });
        }

        var resp = await BuildResponseAsync("RecCenter");
        resp.RoomInstance!.ClubId = club.Id;
        await RecordResponseAsync(resp);
        return Ok(resp);
    }

    [HttpPost("/goto/code")]
    [HttpPost("/goto/code/{code}")]
    public async Task<ActionResult<MatchmakingResponseDto>> GoToCode(string? code = null)
    {
        code ??= Request.HasFormContentType
            ? (await Request.ReadFormAsync())["code"].FirstOrDefault()
            : Request.Query["code"].FirstOrDefault();
        code = RoomCodeService.Normalize(code);
        if (string.IsNullOrWhiteSpace(code))
        {
            return Ok(new MatchmakingResponseDto
            {
                ErrorCode = 40,
                RoomInstance = new RoomInstanceDto(),
            });
        }

        var privateIds = await db.PrivateInstances.AsNoTracking()
            .Select(p => p.Id)
            .ToListAsync();
        var instanceId = privateIds.FirstOrDefault(id => RoomCodeService.Generate(id) == code);
        if (instanceId == 0)
        {
            return Ok(new MatchmakingResponseDto
            {
                ErrorCode = 40,
                RoomInstance = new RoomInstanceDto(),
            });
        }

        var resp = await BuildPrivateInstanceJoinResponseAsync(instanceId);
        await RecordResponseAsync(resp);
        return Ok(resp);
    }

    [HttpPost("/goto/playlist")]
    [HttpPost("/goto/playlist/{playlist}")]
    public async Task<ActionResult<MatchmakingResponseDto>> GoToPlaylist(string? playlist = null)
    {
        playlist ??= Request.Query["playlist"].FirstOrDefault()
                     ?? Request.Query["playlistId"].FirstOrDefault();
        var value = (playlist ?? string.Empty).Trim();
        var roomName = "RecCenter";
        if (!string.IsNullOrWhiteSpace(value))
        {
            var lowered = value.ToLowerInvariant();
            var room = await db.Rooms.AsNoTracking()
                .Where(r => r.Accessibility == 2
                            && (r.Name.ToLower().Contains(lowered)
                                || r.TagsCsv.ToLower().Contains(lowered)))
                .OrderByDescending(r => r.HotScore)
                .FirstOrDefaultAsync();
            if (room is not null) roomName = room.Name;
        }

        var resp = await BuildResponseAsync(roomName);
        await RecordResponseAsync(resp);
        return Ok(resp);
    }

    /// <summary>POST <c>/goto/instance/{roomInstanceId}</c> — invite-
    /// accept route. The watch posts here when joining a private
    /// match via a Message-attached invite. We resolve the instance
    /// id to the registered <see cref="PrivateInstanceService.PrivateInstance"/>
    /// and return a RoomInstance pinned to the inviter's PhotonRoomId
    /// so both players land in the same Photon match. Falls back to
    /// dorm if the instance was forgotten (server restart, owner
    /// already left and nobody re-created it).</summary>
    [HttpPost("/goto/instance/{roomInstanceId:long}")]
    public async Task<ActionResult<MatchmakingResponseDto>> GoToInstance(long roomInstanceId)
    {
        var resp = await BuildPrivateInstanceJoinResponseAsync(roomInstanceId);
        await RecordResponseAsync(resp);
        return Ok(resp);
    }

    [HttpPost("/goto/invite/{inviteId:long}")]
    public async Task<ActionResult<MatchmakingResponseDto>> GoToInvite(long inviteId)
    {
        var callerId = this.CurrentPlayerId();
        if (callerId is null)
        {
            var unauth = await BuildResponseAsync("DormRoom");
            await RecordResponseAsync(unauth);
            return Ok(unauth);
        }

        var invite = await db.Messages.FirstOrDefaultAsync(m =>
            m.Id == inviteId
            && m.RecipientPlayerId == callerId.Value
            && (m.Type == 6 || m.Type == 7));

        long? roomInstanceId = null;
        if (invite is not null && TryParseRoomInstanceId(invite.Body, out var parsed))
            roomInstanceId = parsed;

        // The 2020 watch's accept-invite flow fires
        // POST /api/messages/v3/delete AND POST /goto/invite/{id} in
        // parallel — verified in the live server log:
        //   09:53:47.161Z DELETE messageIds=[60]
        //   09:53:47.245Z /goto/invite/60 → [goto-invite] invite 60 not found
        // DELETE usually wins the race, so the message row is gone
        // by the time we run FirstOrDefaultAsync. The
        // (instance, player) → LatestInviteMessageId mapping that
        // SendInvite writes onto the PrivateInstanceInviteeEntity
        // survives the message deletion and lets us recover
        // roomInstanceId here.
        if (roomInstanceId is null)
        {
            var inviteeRoomInstance = await db.PrivateInstanceInvitees
                .Where(i => i.PlayerId == callerId.Value && i.LatestInviteMessageId == inviteId)
                .Select(i => (long?)i.PrivateInstanceId)
                .FirstOrDefaultAsync();
            if (inviteeRoomInstance is long inst)
            {
                roomInstanceId = inst;
                logger.LogInformation(
                    "[goto-invite] message {InviteId} missing (likely raced with DELETE); resolved roomInstance={Instance} via invitee fallback for caller {Caller}",
                    inviteId, inst, callerId);
            }
        }

        if (roomInstanceId is null)
        {
            logger.LogWarning(
                "[goto-invite] invite {InviteId} not found and no invitee fallback for caller {Caller}",
                inviteId, callerId);
            return Ok(new MatchmakingResponseDto
            {
                ErrorCode = 40, // RoomInviteExpired
                RoomInstance = new RoomInstanceDto(),
            });
        }

        // Mark the message read. The watch's parallel DELETE may have
        // wiped the row between our initial read and this save —
        // swallow the resulting DbUpdateConcurrencyException since the
        // accept has already succeeded at this point.
        if (invite is not null && invite.ReadAt is null)
        {
            invite.ReadAt = DateTime.UtcNow;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                db.Entry(invite).State = EntityState.Detached;
            }
        }

        // Two routes:
        //   PRIVATE instance: roomInstanceId resolves to a registered
        //   PrivateInstance → build a join response pinned to its
        //   photonRoomId + invite-list enforcement.
        //
        //   PUBLIC room: no PrivateInstance for that id (sender was in
        //   a public room / a dorm / etc.). Use the message's RoomId
        //   to look up the room name and route via the standard public
        //   matchmaking path so the recipient lands in the same room
        //   (different instance is fine — public rooms aren't pinned
        //   to one Photon match).
        var isPrivate = await privateInstances.GetAsync(roomInstanceId.Value) is not null;
        MatchmakingResponseDto resp;
        if (isPrivate)
        {
            resp = await BuildPrivateInstanceJoinResponseAsync(roomInstanceId.Value);
        }
        else if (invite?.RoomId is long publicRoomId && publicRoomId > 0)
        {
            var roomName = await db.Rooms
                .Where(r => r.Id == publicRoomId)
                .Select(r => r.Name)
                .FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(roomName))
            {
                logger.LogWarning(
                    "[goto-invite] invite {InviteId} → no PrivateInstance and Message.RoomId={RoomId} doesn't resolve to a Rooms row; routing to dorm",
                    inviteId, publicRoomId);
                resp = await BuildResponseAsync("DormRoom");
            }
            else
            {
                logger.LogInformation(
                    "[goto-invite] public-room invite {InviteId} → routing caller {Caller} to {RoomName} (RoomId={RoomId})",
                    inviteId, callerId, roomName, publicRoomId);
                resp = await BuildResponseAsync(roomName);
            }
        }
        else
        {
            // No private instance and the message has no RoomId. This
            // shouldn't happen with the current SendInvite (it always
            // stamps RoomId), but legacy rows from before the public-
            // room invite fix may not have one. Fall back to dorm.
            logger.LogWarning(
                "[goto-invite] invite {InviteId} → no PrivateInstance and no Message.RoomId; routing to dorm",
                inviteId);
            resp = await BuildResponseAsync("DormRoom");
        }
        await RecordResponseAsync(resp);
        return Ok(resp);
    }

    private async Task<MatchmakingResponseDto> BuildPrivateInstanceJoinResponseAsync(long roomInstanceId)
    {
        var inst = await privateInstances.GetAsync(roomInstanceId);
        if (inst is null)
        {
            logger.LogWarning(
                "[goto-instance] {Id} → no registered private instance; routing to dorm",
                roomInstanceId);
            return await BuildResponseAsync("DormRoom");
        }

        // Enforce invite-list membership. Owner is always allowed (so
        // they can rejoin their own match after a disconnect). Anyone
        // else needs to have been invited via POST /invite. We refuse
        // with errorCode != 0 so the watch surfaces a "join failed"
        // toast instead of silently routing to the wrong photon room.
        if (this.CurrentPlayerId() is long callerId
            && !await privateInstances.CanJoinAsync(roomInstanceId, callerId))
        {
            logger.LogWarning(
                "[goto-instance] {Id} → caller {Caller} is not on the invite list (owner={Owner}); rejecting",
                roomInstanceId, callerId, inst.OwnerPlayerId);
            // MatchmakingErrorCode 26 = RoomInstanceIsPrivate — the
            // dedicated "you're not invited" error from the
            // RecNet.Matchmaking+MatchmakingErrorCode enum (verified in
            // Cpp2IL_CS/.../Matchmaking.cs line 166). Surfaces a clearer
            // "this is a private match" message on the watch than
            // 9 = RoomNotJoinable does.
            return new MatchmakingResponseDto
            {
                ErrorCode = 26,
                RoomInstance = new RoomInstanceDto(),
            };
        }

        return new MatchmakingResponseDto
        {
            ErrorCode = 0,
            RoomInstance = new RoomInstanceDto
            {
                RoomInstanceId = inst.InstanceId,
                RoomId = inst.RoomId,
                SubRoomId = inst.SubRoomId,
                Location = inst.Location,
                PhotonRegionId = inst.PhotonRegion,
                PhotonRoomId = inst.PhotonRoomId,
                Name = inst.Name,
                MaxCapacity = inst.MaxCapacity,
                IsPrivate = true,
                IsInProgress = true,
                DataBlob = inst.DataBlob,
            },
        };
    }

    // Invite-message Body can be any of three shapes depending on how /
    // when it was written:
    //   1. Modern (current): JSON {"inviteId":N,"name":"…","roomInstanceId":N}
    //      — extract roomInstanceId from the JSON property.
    //   2. Legacy (pre-V2 fix): bare integer = roomInstanceId.
    //   3. Ancient (pre-Type-column): text "Invite to private match
    //      (instance N)" — parse the trailing integer out of the literal.
    // Honour all three so old rows in the DB still resolve.
    private static bool TryParseRoomInstanceId(string body, out long instanceId)
    {
        instanceId = 0;
        if (string.IsNullOrWhiteSpace(body)) return false;
        if (body.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("roomInstanceId", out var v))
                {
                    // String form first — the SendInvite serializer now
                    // emits roomInstanceId as a quoted string because
                    // LitJson on the watch fails to parse a JSON object
                    // that contains a 64-bit numeric literal larger than
                    // int32 (returns null from the dict cast → NRE in
                    // Util.GetKey downstream). Number form stays
                    // supported for any old rows that pre-dated the
                    // stringify fix.
                    if (v.ValueKind == System.Text.Json.JsonValueKind.String
                        && long.TryParse(v.GetString(), System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var strInst))
                    { instanceId = strInst; return true; }
                    if (v.ValueKind == System.Text.Json.JsonValueKind.Number
                        && v.TryGetInt64(out var numInst))
                    { instanceId = numInst; return true; }
                }
            }
            catch { /* fall through to legacy formats */ }
        }
        if (long.TryParse(body, out instanceId)) return true;
        var open = body.IndexOf("instance ", StringComparison.Ordinal);
        if (open < 0) return false;
        var tail = body[(open + "instance ".Length)..].TrimEnd(')', '.', ' ');
        return long.TryParse(tail, out instanceId);
    }

    /// <summary>POST <c>/goto/player/{playerId}</c> — join the room
    /// the target player is currently in.
    ///
    /// For PUBLIC rooms: mirror the target's live RoomInstance. The
    /// 2020.12 client reconciles joined players against the exact
    /// roomInstanceId/photonRoomId pair it got from matchmaking; rebuilding
    /// a fresh instance here can leave the caller in a different Photon
    /// shard than the target and null-ref during the follow-join callback.
    ///
    /// For PRIVATE rooms: this route must NOT bypass the invite list.
    /// Earlier we mirrored the target's RoomInstance verbatim under
    /// the assumption "Go to player only shows up for friends" —
    /// that's wrong: friend ≠ invited, and the owner picked Private
    /// specifically to control who joins. Caller must be the owner
    /// or on the invite list per <see cref="PrivateInstanceService.CanJoin"/>;
    /// otherwise we refuse with errorCode=9 (RoomNotJoinable).</summary>
    [HttpPost("/goto/player/{playerId:long}")]
    public async Task<ActionResult<MatchmakingResponseDto>> GoToPlayer(long playerId)
    {
        var theirRoom = presence.GetRoom(playerId);
        MatchmakingResponseDto resp;
        if (theirRoom is null)
        {
            resp = await BuildResponseAsync("DormRoom");
        }
        else if (theirRoom.IsPrivate)
        {
            // RoomInstanceId IS the registry key for private matches —
            // generated by PrivateInstanceService.Register. Caller must
            // be invited (or owner) to land in the same Photon room.
            var callerId = this.CurrentPlayerId();
            if (callerId is null
                || !await privateInstances.CanJoinAsync(theirRoom.RoomInstanceId, callerId.Value))
            {
                logger.LogWarning(
                    "[goto-player] caller {Caller} tried to follow {Target} into private instance {Id} but isn't invited; rejecting",
                    callerId, playerId, theirRoom.RoomInstanceId);
                // Notify the OWNER that someone tried to follow them in.
                // Helps surface the rejection so the owner knows there's
                // an interested party — they can choose to send an invite.
                await notifications.PrivateInstanceJoinRejected(
                    ownerPlayerId: playerId, attemptedByPlayerId: callerId ?? 0);
                return Ok(new MatchmakingResponseDto
                {
                    ErrorCode = 26, // RoomInstanceIsPrivate
                    RoomInstance = new RoomInstanceDto(),
                });
            }
            // Allowed — mirror the target's RoomInstance so we land
            // in the same Photon match.
            resp = new MatchmakingResponseDto
            {
                ErrorCode = 0,
                RoomInstance = CloneRoomInstance(theirRoom),
            };
        }
        else
        {
            // Public follow-joins still need to land in the target's
            // specific live instance, not just the same room name. This
            // preserves sub-room and Photon shard identity for PUN's
            // player-enter reconciliation.
            resp = new MatchmakingResponseDto
            {
                ErrorCode = 0,
                RoomInstance = CloneRoomInstance(theirRoom),
            };
        }
        await RecordResponseAsync(resp);
        return Ok(resp);
    }

    private static RoomInstanceDto CloneRoomInstance(RoomInstanceDto room) => new()
    {
        RoomInstanceId = room.RoomInstanceId,
        RoomId = room.RoomId,
        SubRoomId = room.SubRoomId,
        Location = room.Location,
        PhotonRegionId = room.PhotonRegionId,
        PhotonRoomId = room.PhotonRoomId,
        MaxCapacity = room.MaxCapacity,
        IsFull = room.IsFull,
        IsPrivate = room.IsPrivate,
        IsInProgress = room.IsInProgress,
        DataBlob = room.DataBlob,
        EventId = room.EventId,
        Name = room.Name,
        RoomInstanceType = room.RoomInstanceType,
        ClubId = room.ClubId,
        RoomCode = room.RoomCode,
        EncryptVoiceChat = room.EncryptVoiceChat,
    };

    /// <summary>Record the just-built response against the bearer-token
    /// player so the next heartbeat reflects the room they joined.
    /// Anonymous /goto calls (no JWT) skip recording — the heartbeat is
    /// authenticated, so a match only happens when the same JWT shows
    /// up there. Without this, heartbeats return roomInstance:null and
    /// the watch trips its "presence out-of-sync" detector → "Error: -2".
    /// Caller is responsible for any sub-room or private-instance
    /// mutations BEFORE handing the response in here.</summary>
    private async Task RecordResponseAsync(MatchmakingResponseDto response)
    {
        if (this.CurrentPlayerId() is not long playerId) return;
        if (response.RoomInstance is null) return;

        presence.SetRoom(playerId, response.RoomInstance);
        joinTimeouts.MarkPending(playerId, response.RoomInstance);

        // Real visit tracking: upsert the per-(room, player) row so
        // we get distinct VisitorCount (unique players ever) AND the
        // gross VisitCount (every join, including rejoins). Skip the
        // canonical dorm (RoomId=1) so per-day boots don't skew it.
        if (response.RoomInstance.RoomId != 1)
            await RecordVisitAsync(playerId, response.RoomInstance.RoomId);

        // Push a presence-update notification to every accepted-friend
        // so their watch's "friends" list shows the new room. Cheap
        // query — Relationships is small, and we only fan out to the
        // actually-connected friends.
        await NotifyFriendsOfMoveAsync(playerId, response.RoomInstance);

        // NOTE: we INTENTIONALLY do NOT self-push SubscriptionUpdateRoom
        // from /goto. The watch's Rooms.OnSubscriptionUpdateRoom
        // (Cpp2IL_ISIL/.../RecNet/Rooms.txt:2401-2496) is gated on
        // (Rooms.cachedRoomId == roomId && Rooms.cachedSubRoomId == subRoomId):
        // the FIRST push for a (room, subRoom) tuple does the fetch +
        // set_LocalRoomDetails; subsequent pushes for the same tuple
        // exit early at line 055, NEVER firing the
        // RoomDetailsUpdated event. The save coroutine's
        // RoomDetailsUpdatedCallback is registered to that event
        // (RoomPersistenceManager+DisplayClass174_0:14-48) and only
        // sets the roomDetailsUpdated flag when the event fires —
        // which is the ONLY thing that flips the b__4 predicate true
        // and dismisses the save spinner. Pre-pushing here primes the
        // cached tuple so the save's own push gets deduplicated → no
        // event → spinner wedges forever. Let the save's push be the
        // first one for any (room, subRoom).
    }

    /// <summary>Mutate the response so it represents a fresh invite-only
    /// match: forks the PhotonRoomId with a per-(player, nonce) suffix
    /// so randos can't matchmake into it, sets isPrivate=true, and
    /// registers the instance in <see cref="PrivateInstanceService"/>
    /// so invitees coming in via /goto/instance/{id} can find this
    /// exact Photon match.</summary>
    private async Task ApplyPrivateInstanceAsync(MatchmakingResponseDto resp, long subRoomId)
    {
        if (this.CurrentPlayerId() is not long ownerId)
        {
            // Anonymous private-instance creation makes no sense — the
            // owner is the keying signal for the photon room name and
            // for invite resolution. Fall through with isPrivate flag
            // only; the watch won't be able to invite anyone but at
            // least won't mismatch the requested form value.
            resp.RoomInstance.IsPrivate = true;
            return;
        }

        var inst = await privateInstances.RegisterAsync(
            roomId: resp.RoomInstance.RoomId,
            subRoomId: subRoomId,
            ownerPlayerId: ownerId,
            baseName: resp.RoomInstance.Name.ToLowerInvariant(),
            location: resp.RoomInstance.Location,
            dataBlob: resp.RoomInstance.DataBlob,
            photonRegion: resp.RoomInstance.PhotonRegionId,
            maxCapacity: resp.RoomInstance.MaxCapacity);

        resp.RoomInstance.RoomInstanceId = inst.InstanceId;
        resp.RoomInstance.PhotonRoomId = inst.PhotonRoomId;
        resp.RoomInstance.IsPrivate = true;
        logger.LogInformation(
            "[goto-private] room={Room} sub={Sub} owner={Owner} → instId={Id} photon={Photon}",
            resp.RoomInstance.RoomId, subRoomId, ownerId, inst.InstanceId, inst.PhotonRoomId);
    }

    /// <summary>Upsert the (room, player) <see cref="RoomVisitEntity"/>
    /// row for this join. New rows bump
    /// <see cref="RoomEntity.VisitorCount"/> (unique-visitor metric);
    /// every join — new or repeat — bumps
    /// <see cref="RoomEntity.VisitCount"/> + the per-player counter.</summary>
    private async Task RecordVisitAsync(long playerId, long roomId)
    {
        var visit = await db.RoomVisits
            .FirstOrDefaultAsync(v => v.RoomId == roomId && v.PlayerId == playerId);
        var isFirst = visit is null;
        if (isFirst)
        {
            visit = new DorkNet.Server.Data.Entities.RoomVisitEntity
            {
                RoomId = roomId,
                PlayerId = playerId,
                FirstVisitAt = DateTime.UtcNow,
            };
            db.RoomVisits.Add(visit);
        }
        visit!.LastVisitAt = DateTime.UtcNow;
        visit.VisitCount += 1;

        // Counter bumps go through ExecuteUpdate to avoid re-reading
        // the room — atomic increment under concurrent /goto calls.
        // Also bumps HotScore so the "Hot" tab self-reorders as
        // players actually use rooms. Weight: each first-time visit
        // adds 2.0, each repeat adds 0.25 (revisits matter less than
        // new players). Cheers also feed in via PlayerStateController
        // / MissingEndpointsController by the same factor.
        if (isFirst)
        {
            await db.Rooms
                .Where(r => r.Id == roomId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.VisitCount, r => r.VisitCount + 1)
                    .SetProperty(r => r.VisitorCount, r => r.VisitorCount + 1)
                    .SetProperty(r => r.HotScore, r => r.HotScore + 2.0));
        }
        else
        {
            await db.Rooms
                .Where(r => r.Id == roomId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.VisitCount, r => r.VisitCount + 1)
                    .SetProperty(r => r.HotScore, r => r.HotScore + 0.25));
        }
        await db.SaveChangesAsync();
    }

    private async Task NotifyFriendsOfMoveAsync(long playerId, RoomInstanceDto room)
    {
        var friendIds = await db.Relationships
            .Where(r => r.Status == DorkNet.Server.Data.Entities.RelationshipStatus.Friend &&
                        (r.RequesterId == playerId || r.TargetId == playerId))
            .Select(r => r.RequesterId == playerId ? r.TargetId : r.RequesterId)
            .ToListAsync();
        logger.LogInformation(
            "[goto-fanout] player={PlayerId} moved to room={RoomId}/{RoomName} → notifying {FriendCount} friend(s)",
            playerId, room.RoomId, room.Name, friendIds.Count);
        if (friendIds.Count == 0) return;
        await notifications.PlayerEnteredRoom(friendIds, playerId, room);
    }

    private async Task<MatchmakingResponseDto> BuildResponseAsync(string roomName)
    {
        // Try to resolve from the Rooms DB first — that gives us the real
        // RoomId (so subsequent /api/rooms/v4/details/{id} hits the same
        // entity) and the canonical LocationReplicationId. Fall back to the
        // baked-in DormRoom mapping for unknown names.
        long roomId;
        string locationId;
        if (roomName.Equals("DormRoom", StringComparison.OrdinalIgnoreCase))
        {
            // Each player has their own dorm row (created at signup,
            // lazy-created here for legacy accounts that pre-date the
            // per-player dorm work). Resolve to the caller's personal
            // dorm so /api/rooms/v4/details/{id} hits a real Rooms.id
            // and storage.rec.net/upload's room lookup succeeds.
            if (this.CurrentPlayerId() is long callerPid)
            {
                var personal = await rooms.EnsurePersonalDormAsync(callerPid);
                roomId = personal.Id;
                locationId = personal.LocationReplicationId;
            }
            else
            {
                // Anonymous /goto/room/DormRoom shouldn't happen in the
                // 2020 watch (the watch always authenticates first), but
                // fall back to the canonical scene id for safety.
                roomId = 1;
                locationId = "76d98498-60a1-430c-ab76-b54a29b7a163";
            }
        }
        else
        {
            var room = await rooms.GetByNameAsync(roomName);
            if (room is not null)
            {
                roomId = room.Id;
                locationId = room.LocationReplicationId;
            }
            else
            {
                roomId = MapRoomNameToId(roomName);
                locationId = MapRoomNameToReplicationId(roomName);
            }
        }
        var photonRegion = (config["Photon:CloudRegion"] ?? "us").ToLowerInvariant();

        // DormRoom is per-player (each account has its own dorm); Rec Center
        // and other public rooms are shared by name. Including the player's
        // id in the dorm room name avoids the case where rebuilding the DB
        // gives a fresh accountId but Photon still considers the old
        // "recroom_dormroom_1" room locked from the previous account's
        // session — manifested as a successful /goto but no actual scene
        // load (black screen).
        var isDorm = roomName.Equals("DormRoom", StringComparison.OrdinalIgnoreCase);
        var dormOwnerId = this.CurrentPlayerId();
        var photonRoom = (isDorm && dormOwnerId is long ownerForName)
            ? $"^dormroom_p{ownerForName}"
            : $"^{roomName.ToLowerInvariant()}_{roomId}";
        var dataBlob = await ResolveInitialDataBlobAsync(roomId, isDorm, dormOwnerId);

        var resp = new MatchmakingResponseDto
        {
            ErrorCode = 0,
            RoomInstance = new RoomInstanceDto
            {
                // Deterministic: same name → same instance id, every time.
                RoomInstanceId = roomId * 100_000L,
                RoomId = roomId,
                SubRoomId = 0,
                // location is a STRING replicationId, parsed via
                // RoomSceneLocations.TryGetRoomSceneLocationById (RVA 0x10F57D0).
                // The value is matched against AGRoomRuntimeConfig.Room.ReplicationId
                // entries baked into resources.assets. Sending an int (or wrong
                // string) makes RoomInstance.Deserialize log "RecNet game session
                // contains unknown scene location ID" and the scene never loads.
                Location = locationId,
                // photonRegionId is a STRING that maps to CloudRegionCode enum
                // (eu, us, asia, jp, none, au, usw, sa, cae, kr, in, ru, rue).
                // Lowercase enum names — that's the wire format Photon uses too.
                PhotonRegionId = photonRegion,
                PhotonRoomId = photonRoom,
                Name = roomName,
                MaxCapacity = 8,
                IsFull = false,
                IsPrivate = false,
                IsInProgress = true,
                DataBlob = dataBlob,
            },
        };
        logger.LogInformation(
            "[goto] room instance room={RoomId} name={RoomName} player={PlayerId} dataBlob={DataBlob}",
            roomId, roomName, dormOwnerId, dataBlob);

        // Dorms are always private — the per-player photon-room name
        // (^dormroom_p{owner}) prevents matchmaking, but without
        // IsPrivate=true the /goto/player path mirrors the room
        // verbatim and lets uninvited friends drop straight in. Stamp
        // the response with a deterministic-id PrivateInstance owned
        // by the dorm's owner so CanJoinAsync gates non-owners.
        if (isDorm && dormOwnerId is long ownerPid)
        {
            var dorm = await privateInstances.EnsureForDormAsync(
                ownerPlayerId: ownerPid,
                roomId: resp.RoomInstance.RoomId,
                subRoomId: 0,
                baseName: resp.RoomInstance.Name,
                location: resp.RoomInstance.Location,
                dataBlob: resp.RoomInstance.DataBlob,
                photonRegion: resp.RoomInstance.PhotonRegionId,
                maxCapacity: resp.RoomInstance.MaxCapacity,
                photonRoomId: resp.RoomInstance.PhotonRoomId);
            resp.RoomInstance.RoomInstanceId = dorm.InstanceId;
            resp.RoomInstance.IsPrivate = true;
        }

        return resp;
    }

    private async Task<string> ResolveInitialDataBlobAsync(long roomId, bool isDorm, long? dormOwnerId)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return string.Empty;

        if (isDorm && dormOwnerId is long ownerId)
        {
            var dormBlob = await db.DormStates.AsNoTracking()
                .Where(d => d.PlayerId == ownerId)
                .Select(d => d.CurrentDataBlobName)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(dormBlob)) return dormBlob;
        }

        if (!string.IsNullOrWhiteSpace(room.CurrentDataBlobName)) return room.CurrentDataBlobName;

        var customisable = room.IsDormRoom || room.CreatorPlayerId != 1;
        return customisable ? $"room_{room.Id}_v1.dat" : string.Empty;
    }

    /// <summary>
    /// Stable id-per-room-name map for well-known rooms; unknown names get
    /// a hash-based id so each room name routes to its own roomId space.
    /// </summary>
    private static long MapRoomNameToId(string name) => name.ToLowerInvariant() switch
    {
        "dormroom" => 1,
        "rec_center" or "reccenter" => 2,
        "lobby" => 3,
        _ => Math.Abs(name.GetHashCode()) % 100_000 + 1000,
    };

    /// <summary>
    /// Maps a friendly room name to the RoomScene.RoomSceneLocationId GUID
    /// baked into resources.assets's AGRoomRuntimeConfig.Rooms[].Scenes[0].
    /// This is the foreign key that links a Room's "Home" scene to an
    /// AGRoomRuntimeConfig.Locations entry — and is what
    /// AGRoomSettings.TryGetRoomSceneLocationById (RVA 0x10F57D0) matches
    /// against Location.ReplicationId.
    ///
    /// The asset stores each Room as [Name] [Room.ReplicationId]
    /// [Description] [Scene.Name="Home"] [Scene.ReplicationId]
    /// [Scene.RoomSceneLocationId] — so the THIRD GUID after the room name
    /// is the one we want. Earlier I sent the FIRST (Room.ReplicationId)
    /// which is a different namespace entirely; the deserializer logged
    /// "unknown scene location ID" because no Location had that GUID.
    /// </summary>
    private static string MapRoomNameToReplicationId(string name) => name.ToLowerInvariant() switch
    {
        "dormroom"            => "76d98498-60a1-430c-ab76-b54a29b7a163",
        "rec_center" or "reccenter" => "cbad71af-0831-44d8-b8ef-69edafa841f6",
        "3dcharades" or "charades"  => "4078dfed-24bb-4db7-863f-578ba48d726b",
        "discgolflake" or "lake"    => "f6f7256c-e438-4299-b99e-d20bef8cf7e0",
        "discgolfpropulsion" or "propulsion" => "d9378c9f-80bc-46fb-ad1e-1bed8a674f55",
        "dodgeball"           => "3d474b26-26f7-45e9-9a36-9b02847d5e6f",
        "paddleball"          => "d89f74fa-d51e-477a-a425-025a891dd499",
        "river"               => "e122fe98-e7db-49e8-a1b1-105424b6e1f0",
        "homestead"           => "a785267d-c579-42ea-be43-fec1992d1ca7",
        "quarry"              => "ff4c6427-7079-4f59-b22a-69b089420827",
        "clearcut"            => "380d18b5-de9c-49f3-80f7-f4a95c1de161",
        "spillway"            => "58763055-2dfb-4814-80b8-16fac5c85709",
        "drive-in" or "drivein" => "65ddbb48-5a01-4e3e-972d-e5c7419e2bc3",
        "soccer"              => "7e01cfe0-820a-406f-b1b3-0a5bf575235c",
        "lasertag"            => "6d5eea4b-f069-4ed0-9916-0e2f07df0d03",
        "cyberjunkcity" or "lasertagcyberjunk" => "9d6456ce-6264-48b4-808d-2d96b3d91038",
        "recroyalesquads"     => "253fa009-6e65-4c90-91a1-7137a56a267f",
        "recroyalesolos"      => "85b43509-77f7-3884-3a6b-9a4e737d6d11",
        _ => "76d98498-60a1-430c-ab76-b54a29b7a163", // DormRoom
    };
}

public class MatchmakingResponseDto
{
    [JsonPropertyName("errorCode")]
    public int ErrorCode { get; set; }

    [JsonPropertyName("roomInstance")]
    public RoomInstanceDto? RoomInstance { get; set; } = new();
}

/// <summary>
/// Mirrors RecNet.RoomInstance — Deserialize at RVA 0x114DAB0. Keys
/// verified by disassembly. JSON casing is camelCase throughout.
/// </summary>
public class RoomInstanceDto
{
    [JsonPropertyName("roomInstanceId")]
    public long RoomInstanceId { get; set; }

    [JsonPropertyName("roomId")]
    public long RoomId { get; set; }

    [JsonPropertyName("subRoomId")]
    public long SubRoomId { get; set; }

    // STRING RoomSceneLocationId GUID — foreign-keys to
    // AGRoomRuntimeConfig.Locations[].ReplicationId. Resolved via
    // AGRoomSettings.TryGetRoomSceneLocationById (RVA 0x10F57D0). Default
    // = DormRoom's home-scene location ID extracted from resources.assets
    // at offset 0x65107ec.
    [JsonPropertyName("location")]
    public string Location { get; set; } = "76d98498-60a1-430c-ab76-b54a29b7a163";

    // STRING enum-name — CloudRegionCode is parsed by name: eu, us, asia,
    // jp, none, au, usw, sa, cae, kr, in, ru, rue. Lowercase.
    [JsonPropertyName("photonRegionId")]
    public string PhotonRegionId { get; set; } = "us";

    [JsonPropertyName("photonRoomId")]
    public string PhotonRoomId { get; set; } = string.Empty;

    [JsonPropertyName("maxCapacity")]
    public int MaxCapacity { get; set; } = 8;

    [JsonPropertyName("isFull")]
    public bool IsFull { get; set; } = false;

    [JsonPropertyName("isPrivate")]
    public bool IsPrivate { get; set; } = false;

    [JsonPropertyName("isInProgress")]
    public bool IsInProgress { get; set; } = true;

    // Optional keys per disassembly (Util.GetKeyOrDefault).
    [JsonPropertyName("dataBlob")]
    public string DataBlob { get; set; } = string.Empty;

    [JsonPropertyName("eventId")]
    public long EventId { get; set; } = 0;

    // Raw room name (NO leading "^"). Kept clean because server-side code
    // derives other values from it — most importantly
    // PrivateInstanceService builds the photonRoomId as "^{baseName}_…"
    // from this, and MessagesController uses it verbatim as the invite
    // message's display name (the "just the event name" case, no "^").
    [JsonIgnore]
    public string Name { get; set; } = string.Empty;

    // Wire "name" field. The 2020.12 watch's PlayerPresence.FriendlyPresence
    // (EFAJBHGDIDD.JDICJBJBMMI → BaseAccountModel.get_FriendlyPresence) shows
    // roomInstance.name VERBATIM and its built-in default is the literal
    // "^DormRoom" — i.e. a player's current room is expected to carry a
    // leading "^". So we prefix on serialize and strip on deserialize
    // (PlayerPresenceService round-trips this DTO through Redis), keeping the
    // C# Name raw. Empty stays empty so the watch falls back to its own
    // "^DormRoom" default. Invite MESSAGES keep the plain name — that's a
    // separate field built from the raw Name in MessagesController.
    [JsonPropertyName("name")]
    public string NameWire
    {
        get => string.IsNullOrEmpty(Name) || Name.StartsWith('^') ? Name : "^" + Name;
        set => Name = !string.IsNullOrEmpty(value) && value.StartsWith('^') ? value[1..] : value;
    }

    // 2020.12 added these keys to RoomInstance.Deserialize. Verified via
    // Cpp2IL ISIL on CIIBGMBOFEI.PPGFHEDFBEA (the obfuscated RoomInstance
    // class). Missing → "Malformed Response" in matchmaking.
    [JsonPropertyName("roomInstanceType")]
    public int RoomInstanceType { get; set; } = 0;

    [JsonPropertyName("clubId")]
    public long ClubId { get; set; } = 0;

    [JsonPropertyName("roomCode")]
    public string RoomCode { get; set; } = string.Empty;

    // NOTE: PascalCase 'E'. The 2020.12 ISIL literally loads "EncryptVoiceChat"
    // (not "encryptVoiceChat"). LitJson is case-sensitive.
    [JsonPropertyName("EncryptVoiceChat")]
    public bool EncryptVoiceChat { get; set; } = false;

    /// <summary>
    /// Returns a copy of <paramref name="room"/> with <c>photonRoomId</c>
    /// and <c>dataBlob</c> blanked out — safe to send to OTHER players
    /// (friend-list presence, push updates). The watch only needs
    /// <c>roomId</c> / <c>name</c> / <c>isPrivate</c> to render the
    /// friend-list row; it joins via <c>/goto/player/{id}</c> or
    /// <c>/goto/instance/{id}</c>, both of which enforce
    /// <see cref="PrivateInstanceService.CanJoinAsync"/>.
    ///
    /// Without this redaction, anyone could read another player's
    /// presence and use the leaked <c>photonRoomId</c> to JoinByName
    /// in Photon directly, bypassing our invite-list check.
    /// </summary>
    public static RoomInstanceDto? Redact(RoomInstanceDto? room)
    {
        if (room is null) return null;
        return new RoomInstanceDto
        {
            RoomInstanceId = room.RoomInstanceId,
            RoomId         = room.RoomId,
            SubRoomId      = room.SubRoomId,
            Location       = room.Location,
            PhotonRegionId = room.PhotonRegionId,
            PhotonRoomId   = string.Empty,
            MaxCapacity    = room.MaxCapacity,
            IsFull         = room.IsFull,
            IsPrivate      = room.IsPrivate,
            IsInProgress   = room.IsInProgress,
            DataBlob       = string.Empty,
            EventId        = room.EventId,
            Name           = room.Name,
        };
    }
}
