using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Models.GameSessions;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Match;

/// <summary>
/// match.rec.net — dedicated matchmaking service used by later 2020 builds.
/// Delegates to the Postgres-backed GameSessionService.
/// </summary>
[ApiController]
[Authorize]
public class MatchController(
    IConfiguration config,
    GameSessionService sessionService,
    PlayerPresenceService presence,
    PrivateInstanceService privateInstances,
    JoinTimeoutService joinTimeouts,
    PresenceBroadcastService presenceFanout,
    RoomService rooms,
    DorkNetDbContext db,
    ILogger<MatchController> logger) : ControllerBase
{
    /// <summary>POST <c>match.*/roominstance/{id}/reportjoinresult</c> —
    /// the watch fires this AFTER its Photon connect+join attempt,
    /// reporting how it went via <c>RecNet.Matchmaking+GameJoinResult</c>
    /// (Matchmaking.cs:121):
    ///   0 = Success, 1 = FailedToConnectToRegion,
    ///   2 = FailedToConnectToRoom, 3 = FailedToSpawnPlayer.
    /// Body is form-urlencoded <c>result=N</c>. Watch ignores the
    /// response body and just needs 2xx (Matchmaking.txt:6668-6699), so
    /// we mostly use this for visibility into Photon-side issues —
    /// without it, all we see in the catch-all is an opaque "200, empty
    /// reply" and no clue why a player crashed back to dorm. Failure
    /// reports also clear the pending /goto room so the next matchmaking
    /// attempt starts from a clean presence state.</summary>
    [HttpPost("/roominstance/{instanceId:long}/reportjoinresult")]
    public async Task<IActionResult> ReportJoinResult(long instanceId, [FromForm(Name = "result")] int? result)
    {
        var code = result ?? -1;
        var label = code switch
        {
            0 => "Success",
            1 => "FailedToConnectToRegion",
            2 => "FailedToConnectToRoom",
            3 => "FailedToSpawnPlayer",
            _ => $"Unknown({code})",
        };
        var pid = this.RequireCurrentPlayerId();
        if (code == 0)
        {
            var completed = joinTimeouts.MarkCompleted(pid, instanceId, label);
            if (completed.HasValue)
            {
                presence.SetRoom(pid, completed.Value.TargetRoom);
                presence.MarkActive(pid);
                presence.EndBootDormGrace(pid);
                logger.LogInformation(
                    "[joinresult] committed presence player={Player} room={RoomId}/{RoomName} instance={Instance} deferred={Deferred}",
                    pid,
                    completed.Value.TargetRoom.RoomId,
                    completed.Value.TargetRoom.Name,
                    completed.Value.TargetRoom.RoomInstanceId,
                    completed.Value.DeferPresenceCommit);
                // A deferred join (matchmake/dorm, private-instance hop)
                // commits HERE, not at matchmake time, so this is the only
                // point friends can learn about the move. Immediate commits
                // were already announced by /goto.
                if (completed.Value.DeferPresenceCommit)
                    await presenceFanout.PublishCurrentAsync(pid);
            }
            else
            {
                presence.MarkActive(pid);
                presence.TouchRoom(pid);
                presence.EndBootDormGrace(pid);
            }
            logger.LogInformation(
                "[joinresult] player={Player} instance={Instance} OK ({Label})",
                pid, instanceId, label);
        }
        else
        {
            await joinTimeouts.MarkFailedAsync(pid, instanceId, label);
            logger.LogWarning(
                "[joinresult] player={Player} instance={Instance} FAILED code={Code} ({Label})",
                pid, instanceId, code, label);
        }
        return Ok();
    }

    /// <summary>POST <c>matchmake/none</c> — "leave room". The 2023 client's
    /// only caller is TryGoToOfflineRoom
    /// (RecNet.Runtime/RecNet/Matchmaking.txt:12677-13279): it has already
    /// switched its own cached presence to the local offline instance
    /// (RoomInstanceId -1) before posting this, and it discards the reply
    /// (<c>&lt;TryGoToOfflineRoom&gt;b__119_0</c> returns a constant).
    ///
    /// So the player is now online in no room, and that is what presence must
    /// say. This used to re-commit the previous room (or the dorm) instead:
    /// the next heartbeat then answered with a room while the client held the
    /// offline instance, the client logged "presence heartbeat response
    /// indicates local presence is out-of-sync", cancelled its offline load
    /// ("OnPlayerPresenceUpdate is cancelling room load for -1") and chased the
    /// server's room — and friends kept seeing the room the player had left.
    ///
    /// A join still pending is abandoned too: the client only goes offline
    /// after giving up on it (fallback chain after a Photon timeout), and
    /// echoing its target from the heartbeat would pull the player back.</summary>
    [HttpPost("/matchmake/none")]
    public async Task<IActionResult> MatchmakeNone()
    {
        var pid = this.CurrentPlayerId() ?? 0;
        if (pid == 0) return Unauthorized();

        var previous = presence.GetRoom(pid);
        var pending = joinTimeouts.GetPending(pid);
        joinTimeouts.Cancel(pid, "matchmake-none");
        presence.SetNoRoom(pid);
        presence.MarkActive(pid);
        presence.EndBootDormGrace(pid);
        logger.LogInformation(
            "[matchmake-none] player={Player} left room={Room} instance={Instance} abandonedPending={Pending}; online with no room",
            pid, previous?.RoomId, previous?.RoomInstanceId, pending?.TargetRoom.RoomInstanceId);

        await presenceFanout.PublishCurrentAsync(pid);

        // The client ignores this body; keep a well-formed instance in it so
        // the shared MatchmakingResponse reader never sees a null object.
        return Ok(new MatchmakingResponseDto
        {
            ErrorCode = 0,
            RoomInstance = previous ?? pending?.TargetRoom ?? await BuildDormRoomInstanceAsync(pid),
        });
    }

    /// <summary>GET <c>photon_access_token</c> — read by the room-permissions
    /// runtime as <c>ALIIIAGAPND {RoomInstanceId, PhotonAccessToken,
    /// Permissions}</c> (NLDBPDCNNCF.CEOPCANHPPH, verb 0, issued from the
    /// rooms client, hence the <c>roomserver/</c> twin).
    /// <c>Permissions</c> is the role-permission matrix of the sub-room the
    /// player is in: the defaults plus whatever the room's editors saved with
    /// <c>PUT rooms/{id}/subrooms/{sub}/permissions</c>
    /// (<see cref="RoomPermissionMatrix"/>). It must not be empty: with no
    /// Permissions the client falls into its persisted-role migration path,
    /// which NREs during first spawn.</summary>
    [HttpGet("/photon_access_token")]
    [HttpGet("/roomserver/photon_access_token")]
    public async Task<IActionResult> PhotonAccessToken()
    {
        var pid = this.RequireCurrentPlayerId();
        var appId = config["Photon:AppId"] ?? string.Empty;
        var voiceAppId = config["Photon:VoiceAppId"] ?? string.Empty;
        var region = (config["Photon:CloudRegion"] ?? "us").ToLowerInvariant();
        var currentRoom = presence.GetRoom(pid);
        var permissions = currentRoom is null
            ? RoomPermissionMatrix.Merge(Array.Empty<RoomRolePermissionEntity>())
            : await RoomPermissionMatrix.LoadAsync(db, currentRoom.RoomId, currentRoom.SubRoomId);
        logger.LogInformation(
            "[photon-token] player={PlayerId} room={RoomId} sub={SubRoomId} roomInstance={RoomInstanceId} permissions={PermissionCount}",
            pid, currentRoom?.RoomId ?? 0L, currentRoom?.SubRoomId ?? 0L, currentRoom?.RoomInstanceId ?? 0L, permissions.Count);

        var body = RoomPermissionMatrix.AccessTokenBody(currentRoom?.RoomInstanceId ?? 0L, permissions);
        // Existing custom-auth/app-id shape kept for Photon auth callers; the
        // ALIIIAGAPND reader ignores keys it does not register.
        body["ResultCode"] = 1;
        body["Message"] = string.Empty;
        body["UserId"] = pid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        body["PhotonAppId"] = appId;
        body["PhotonVoiceAppId"] = voiceAppId;
        body["AppId"] = appId;
        body["VoiceAppId"] = voiceAppId;
        body["CloudRegion"] = region;
        body["Region"] = region;
        body["Token"] = string.Empty;
        return Ok(body);
    }

    [HttpPost("/matchmake/dorm")]
    public async Task<IActionResult> MatchmakeDorm()
    {
        var pid = this.CurrentPlayerId() ?? 0;
        if (pid == 0) return Unauthorized();

        var room = await BuildDormRoomInstanceAsync(pid);
        joinTimeouts.MarkPending(pid, room, deferPresenceCommit: true);
        presence.MarkActive(pid);
        await RecordVisitAsync(pid, room.RoomId);
        logger.LogInformation(
            "[matchmake-dorm] player={Player} room={Room} instance={Instance} photon={Photon} deferredPresence=True",
            pid, room.RoomId, room.RoomInstanceId, room.PhotonRoomId);
        return Ok(new MatchmakingResponseDto
        {
            ErrorCode = 0,
            RoomInstance = room,
        });
    }

    private async Task<RoomInstanceDto> BuildDormRoomInstanceAsync(long playerId)
    {
        var personal = await rooms.EnsurePersonalDormAsync(playerId);
        var dataBlob = await rooms.ResolveDormDataBlobNameAsync(playerId, personal.Id);
        if (string.IsNullOrWhiteSpace(dataBlob))
            dataBlob = personal.CurrentDataBlobName;

        var photonRegion = (config["Photon:CloudRegion"] ?? "us").ToLowerInvariant();
        var photonRoomId = $"^dormroom_p{playerId}";
        var dormInstanceName = await BuildDormInstanceNameAsync(playerId);

        var dorm = await privateInstances.EnsureForDormAsync(
            ownerPlayerId: playerId,
            roomId: personal.Id,
            subRoomId: 0,
            baseName: dormInstanceName,
            location: personal.LocationReplicationId,
            dataBlob: dataBlob ?? string.Empty,
            photonRegion: photonRegion,
            maxCapacity: personal.MaxCapacity,
            photonRoomId: photonRoomId);

        return new RoomInstanceDto
        {
            RoomInstanceId = dorm.InstanceId,
            RoomId = personal.Id,
            SubRoomId = 0,
            Location = personal.LocationReplicationId,
            PhotonRegionId = photonRegion,
            PhotonRoomId = photonRoomId,
            Name = dormInstanceName,
            MaxCapacity = personal.MaxCapacity,
            IsFull = false,
            IsPrivate = true,
            IsInProgress = true,
            DataBlob = dataBlob ?? string.Empty,
            EventId = 0,
        };
    }

    private async Task<string> BuildDormInstanceNameAsync(long ownerPlayerId)
    {
        var player = await db.Players.AsNoTracking()
            .Where(p => p.Id == ownerPlayerId)
            .Select(p => new { p.DisplayName, p.Username })
            .FirstOrDefaultAsync();
        var name = !string.IsNullOrWhiteSpace(player?.DisplayName)
            ? player!.DisplayName
            : !string.IsNullOrWhiteSpace(player?.Username)
                ? player!.Username
                : $"Player{ownerPlayerId}";
        var cleaned = new string(name
            .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_')
            .ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(cleaned) ? $"Player{ownerPlayerId}_dorm" : $"{cleaned}_dorm";
    }

    private async Task RecordVisitAsync(long playerId, long roomId)
    {
        var visit = await db.RoomVisits
            .FirstOrDefaultAsync(v => v.RoomId == roomId && v.PlayerId == playerId);
        var isFirst = visit is null;
        if (isFirst)
        {
            visit = new RoomVisitEntity
            {
                RoomId = roomId,
                PlayerId = playerId,
                FirstVisitAt = DateTime.UtcNow,
            };
            db.RoomVisits.Add(visit);
        }
        visit!.LastVisitAt = DateTime.UtcNow;
        visit.VisitCount += 1;

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


    /// <summary>PUT <c>/roominstance/{id}/inprogress</c> — fired by
    /// <c>RecNet.Matchmaking.SetGameInProgress(bool inProgress)</c>
    /// (Matchmaking.cs:333). Body is form-urlencoded
    /// <c>inProgress=true|false</c>. The watch uses this to mark a
    /// match as "in progress" so newcomers are sent to a fresh instance
    /// instead of joining a game already underway (e.g. paintball
    /// during a round). Our matchmaker doesn't gate on this yet — log
    /// + 200 to take it off the catch-all.</summary>
    [HttpPut("/roominstance/{instanceId:long}/inprogress")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public IActionResult SetInProgress(
        long instanceId,
        [FromForm(Name = "inProgress")] bool? inProgress)
    {
        var pid = this.RequireCurrentPlayerId();
        logger.LogInformation(
            "[set-inprogress] player={Player} instance={Instance} inProgress={Flag}",
            pid, instanceId, inProgress);
        return Ok();
    }

    /// <summary>POST <c>/roominstance/{id}/markprivate</c> — promote
    /// the caller's current public instance to an invite-gated
    /// PrivateInstance row. The watch only needs a RecNet result
    /// wrapper, but the server persists the privacy state so later
    /// invite joins are enforced by <see cref="PrivateInstanceService"/>.</summary>
    // The 2023-03-21 client sends PUT (verb constant 3 at
    // RecNet/Matchmaking.txt:15483 and :15774) with an empty body. POST-only
    // returned 405, so a host could never close a public instance to private.
    [HttpPut("/roominstance/{instanceId:long}/markprivate")]
    [HttpPost("/roominstance/{instanceId:long}/markprivate")]
    public async Task<IActionResult> MarkPrivate(long instanceId)
    {
        var pid = this.RequireCurrentPlayerId();
        var existing = await db.PrivateInstances.FirstOrDefaultAsync(p => p.Id == instanceId);
        if (existing is not null)
        {
            if (existing.OwnerPlayerId != pid) return Forbid();
            return Ok(new { Success = true, Error = string.Empty });
        }

        var current = presence.GetRoom(pid);
        if (current is null || current.RoomInstanceId != instanceId)
            return NotFound(new { Success = false, Error = "room instance not found for current player" });

        db.PrivateInstances.Add(new PrivateInstanceEntity
        {
            Id = instanceId,
            RoomId = current.RoomId,
            SubRoomId = current.SubRoomId,
            OwnerPlayerId = pid,
            PhotonRoomId = current.PhotonRoomId,
            Location = current.Location,
            DataBlob = current.DataBlob,
            PhotonRegion = current.PhotonRegionId,
            MaxCapacity = current.MaxCapacity,
            Name = current.Name,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        logger.LogInformation("[mark-private] player={Player} instance={Instance}", pid, instanceId);
        return Ok(new { Success = true, Error = string.Empty });
    }

    /// <summary>GET/POST/PUT <c>/roominstance/{id}/roomcode</c> — the room code
    /// shown by the watch, as a raw JSON string.
    ///
    /// GET returns the instance's code. The 2023-03-21 client also PUTs here to
    /// SET a custom code, sending form fields <c>roomCode</c> and
    /// <c>forceChange</c> (verb 3 at RecNet/Matchmaking.txt:16234). PUT was not
    /// registered, so custom room codes 405'd; and because the handler always
    /// regenerated the deterministic code, a submitted one would have been
    /// discarded anyway. A submitted code is now validated, claimed and
    /// persisted, and the accepted code is echoed back.
    ///
    /// <c>forceChange</c> lets the host take a code already claimed by one of
    /// their own instances; it never steals another player's code.</summary>
    [HttpGet("/roominstance/{instanceId:long}/roomcode")]
    [HttpPost("/roominstance/{instanceId:long}/roomcode")]
    [HttpPut("/roominstance/{instanceId:long}/roomcode")]
    public async Task<IActionResult> RoomCode(
        long instanceId,
        [FromForm(Name = "roomCode")] string? roomCode,
        [FromForm(Name = "forceChange")] bool? forceChange)
    {
        // Same normalisation the join path applies (GoToController.GoToCode →
        // RoomCodeService.Normalize), so a code typed with dashes/lowercase on
        // the watch still matches what we persist.
        var normalized = RoomCodeService.Normalize(
            roomCode ?? Request.Query["roomCode"].FirstOrDefault());

        if (normalized.Length > 0)
        {
            var pid = this.RequireCurrentPlayerId();
            var clash = await db.PrivateInstances
                .Where(p => p.RoomCode == normalized && p.Id != instanceId)
                .Select(p => new { p.Id, p.OwnerPlayerId })
                .FirstOrDefaultAsync();

            var row = await db.PrivateInstances.FirstOrDefaultAsync(p => p.Id == instanceId);

            // Only the instance's owner may set its code (same guard as
            // MarkPrivate), otherwise any player could plant or relocate a
            // code onto someone else's instance. forceChange only lets a host
            // reclaim a code from one of their OWN instances; another
            // player's code is never stolen. On refusal the client just
            // re-reads whatever code we echo back, so we fall through to the
            // read path instead of erroring.
            var blocked = row is null || row.OwnerPlayerId != pid
                || (clash is not null
                    && (clash.OwnerPlayerId != pid || forceChange != true));

            if (!blocked && row is not null)
            {
                if (clash is not null)
                {
                    // Release the caller's older claim first, as its own
                    // statement, so the two rows never hold the code at once.
                    await db.PrivateInstances
                        .Where(p => p.Id == clash.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.RoomCode, string.Empty));
                }
                row.RoomCode = normalized;
                await db.SaveChangesAsync();
                logger.LogInformation(
                    "[roomcode] player={Player} instance={Instance} code={Code} force={Force}",
                    pid, instanceId, normalized, forceChange == true);
                return Content(JsonSerializer.Serialize(normalized), "application/json");
            }

            logger.LogWarning(
                "[roomcode] player={Player} instance={Instance} refused code={Code} taken={Taken} known={Known}",
                pid, instanceId, normalized, clash is not null, row is not null);
        }

        var stored = await db.PrivateInstances
            .Where(p => p.Id == instanceId)
            .Select(p => p.RoomCode)
            .FirstOrDefaultAsync();
        var code = string.IsNullOrEmpty(stored) ? RoomCodeService.Generate(instanceId) : stored;
        return Content(JsonSerializer.Serialize(code), "application/json");
    }

    [HttpPost("/v1/find")]
    public async Task<ActionResult<JoinGameSessionResponse>> Find([FromBody] JoinRandomGameSessionRequest req)
    {
        var session = await sessionService.JoinOrCreateAsync(req.RoomId, req.ActivityLevelId, req.RegionId);
        return Ok(new JoinGameSessionResponse { Result = JoinGameErrorCode.Success, GameSession = session });
    }

    [HttpPost("/v1/join/{sessionId:long}")]
    public async Task<ActionResult<JoinGameSessionResponse>> Join(long sessionId)
    {
        var session = await sessionService.GetByIdAsync(sessionId);
        if (session is null) return Ok(new JoinGameSessionResponse { Result = JoinGameErrorCode.NotFound });
        if (session.IsFull) return Ok(new JoinGameSessionResponse { Result = JoinGameErrorCode.Full });
        return Ok(new JoinGameSessionResponse { Result = JoinGameErrorCode.Success, GameSession = session });
    }

    [HttpPost("/v1/leave/{sessionId:long}")]
    public async Task<IActionResult> Leave(long sessionId)
    {
        await sessionService.PlayerLeftAsync(sessionId);
        return Ok();
    }

    [HttpGet("/v1/session/{sessionId:long}")]
    public async Task<ActionResult<GameSession>> GetSession(long sessionId)
    {
        var session = await sessionService.GetByIdAsync(sessionId);
        if (session is null) return NotFound();
        return Ok(session);
    }

    /// <summary>GET <c>match.*/room/{id}/instances</c> — the room instance
    /// browser.
    ///
    /// <para><b>Client contract.</b> <c>RecNet.Matchmaking.KAIPGDGACGG(long)</c>
    /// (RecNet.Runtime/RecNet/Matchmaking.txt:13966, route at :14237, verb 0 =
    /// GET on the matchmaking host) returns <c>List&lt;PNDCMIMEJLD&gt;</c>, a bare
    /// array. The reader (JPNMBICAMEE.txt) takes exactly RoomInstanceId,
    /// RoomId, SubRoomId, IsFull, CreatedAt and PlayerIds; the post-parse hook
    /// (PNDCMIMEJLD.FKDDCNLJOLF) derives PlayerCount from PlayerIds.Count,
    /// SubroomName from the cached room details' SubRooms by SubRoomId, and
    /// HasModPresent from the room's Roles. The screen
    /// (RoomInstanceBrowserScreen / RoomInstancePagedGrid) shows every row it
    /// is given, current instance first then oldest CreatedAt first, and
    /// renders the age as UtcNow - CreatedAt, so CreatedAt must be UTC.</para>
    ///
    /// <para><b>What is listed.</b> Every live instance of the room: each
    /// distinct Photon room in which at least one player is presenced with a
    /// live activity marker (<see cref="PlayerPresenceService.EnumerateActiveInstances"/>).
    /// Private instances are included only for callers allowed into them —
    /// the room creator, accepted role holders, the instance owner, its
    /// invitees, and players already inside — so another room's invite-only
    /// match never leaks. Empty instances are not listed: there is nothing to
    /// join and they are what made the old list stale.</para>
    ///
    /// <para><b>Fixed defects.</b> The Redis presence path returned nothing at
    /// all, so the browser was empty on every multi-replica deployment;
    /// private instances (every user room is born private and has one
    /// instance per sub-room) were dropped from presence and re-added only
    /// for their owner with an empty PlayerIds, so they showed 0 players or
    /// nothing; expired or crashed presences still counted; IsFull was always
    /// false; and SubRoomId used the OrderIndex, which does not match the
    /// room details' wire ids.</para></summary>
    [HttpGet("/room/{roomId:long}/instances")]
    [AllowAnonymous]
    public async Task<IActionResult> RoomInstances(long roomId)
    {
        var pid = this.CurrentPlayerId() ?? 0;
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return Ok(Array.Empty<object>());

        var live = presence.EnumerateActiveInstances(roomId, includePrivate: true).ToList();

        var privateIds = live.Where(l => l.Room.IsPrivate).Select(l => l.Room.RoomInstanceId).Distinct().ToList();
        var privateRows = privateIds.Count == 0
            ? new Dictionary<long, PrivateInstanceEntity>()
            : await db.PrivateInstances.AsNoTracking()
                .Where(p => privateIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);

        var callerHasRole = pid != 0 && (room.CreatorPlayerId == pid
            || await db.RoomRoles.AsNoTracking().AnyAsync(r =>
                r.RoomId == roomId && r.PlayerId == pid && r.Accepted));
        var invitedTo = pid == 0 || privateIds.Count == 0
            ? new HashSet<long>()
            : (await db.PrivateInstanceInvitees.AsNoTracking()
                .Where(i => i.PlayerId == pid && privateIds.Contains(i.PrivateInstanceId))
                .Select(i => i.PrivateInstanceId)
                .ToListAsync()).ToHashSet();

        var rows = new List<RoomInstanceRow>();
        foreach (var (inst, playerIds) in live)
        {
            privateRows.TryGetValue(inst.RoomInstanceId, out var privateRow);
            if (inst.IsPrivate && !CanSeePrivateInstance(
                    pid, callerHasRole, playerIds, privateRow, invitedTo.Contains(inst.RoomInstanceId)))
                continue;

            var capacity = inst.MaxCapacity > 0 ? inst.MaxCapacity : privateRow?.MaxCapacity ?? 0;
            rows.Add(new RoomInstanceRow(
                RoomInstanceId: inst.RoomInstanceId,
                RoomId: inst.RoomId,
                SubRoomId: inst.SubRoomId,
                IsFull: capacity > 0 && playerIds.Count >= capacity,
                CreatedAt: privateRow is not null
                    ? DateTime.SpecifyKind(privateRow.CreatedAt, DateTimeKind.Utc)
                    : DateTime.UtcNow,
                PlayerIds: playerIds.Distinct().OrderBy(p => p).ToList()));
        }

        logger.LogInformation(
            "[room-instances] room={Room} caller={Caller} live={Live} listed={Listed}",
            roomId, pid, live.Count, rows.Count);
        return Ok(rows.OrderBy(r => r.CreatedAt).Select(ToWire));
    }

    /// <summary>Who may see (and so be offered a join into) a private
    /// instance in the browser.</summary>
    public static bool CanSeePrivateInstance(
        long callerId,
        bool callerHasRoomRole,
        IReadOnlyCollection<long> playersInside,
        PrivateInstanceEntity? privateRow,
        bool callerInvited)
    {
        if (callerId == 0) return false;
        if (callerHasRoomRole) return true;
        if (playersInside.Contains(callerId)) return true;
        if (privateRow is not null && privateRow.OwnerPlayerId == callerId) return true;
        return callerInvited;
    }

    internal sealed record RoomInstanceRow(
        long RoomInstanceId, long RoomId, long SubRoomId, bool IsFull,
        DateTime CreatedAt, List<long> PlayerIds);

    /// <summary>PNDCMIMEJLD wire row. Account ids are Int32 on this DTO;
    /// SubRoomId is the wire id the room details advertise
    /// (<see cref="RoomSceneIds"/>), which the client uses to name the row.
    /// The reader matches PascalCase, camelCase and lowercase keys; camelCase
    /// is what this route has always sent.</summary>
    internal static object ToWire(RoomInstanceRow r) => new
    {
        roomInstanceId = r.RoomInstanceId,
        roomId = r.RoomId,
        subRoomId = RoomSceneIds.ToWire(r.SubRoomId),
        isFull = r.IsFull,
        createdAt = r.CreatedAt,
        playerIds = r.PlayerIds.Select(p => (int)p).ToArray(),
    };

    /// <summary>DELETE <c>match.*/invite/{id}</c> — Matchmaking.cs
    /// DeclineRoomInvite. Verified URL at Matchmaking.txt:6505 (method
    /// enum 4 = DELETE). The id is a MessageEntity row id (V2 game and
    /// party invites are stored as Type 6/7 messages). Mark it read so the inviter's
    /// inbox stops nagging, and remove the invitee from the private
    /// instance's allow-list so they can't change their mind without
    /// a fresh invite.</summary>
    [HttpDelete("/invite/{messageId:long}")]
    public async Task<IActionResult> DeclineInvite(long messageId)
    {
        var pid = this.RequireCurrentPlayerId();
        var msg = await db.Messages
            .FirstOrDefaultAsync(m => m.Id == messageId && m.RecipientPlayerId == pid);
        if (msg is null) return NotFound();

        if (msg.ReadAt is null)
        {
            msg.ReadAt = DateTime.UtcNow;
        }

        // Current invite messages carry the bare roomInstanceId in
        // Data/Body. Older local DBs may still have the legacy
        // "Invite to private match (instance N)" text, so accept both.
        if (TryParseRoomInstanceId(msg.Body, out var instId))
        {
            await privateInstances.RevokeInviteAsync(instId, pid);
        }

        await db.SaveChangesAsync();
        logger.LogInformation("[invite] declined messageId={Id} by player={Player}", messageId, pid);
        return Ok();
    }

    // Same three-shape parser as GoToController.TryParseRoomInstanceId
    // (see that file for the rationale): JSON for current Type-6/7
    // invites, bare int for the pre-fix shape, "Invite to private match
    // (instance N)" text for ancient rows.
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
                    // See GoToController.TryParseRoomInstanceId for the
                    // rationale on accepting both string and number forms.
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

}
