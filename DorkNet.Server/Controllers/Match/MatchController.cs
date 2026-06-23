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
    /// reports also clear the pending /goto room and push a ModerationKick
    /// so the watch leaves the "Joining room" overlay instead of softlocking.</summary>
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
                logger.LogInformation(
                    "[joinresult] committed presence player={Player} room={RoomId}/{RoomName} instance={Instance} deferred={Deferred}",
                    pid,
                    completed.Value.TargetRoom.RoomId,
                    completed.Value.TargetRoom.Name,
                    completed.Value.TargetRoom.RoomInstanceId,
                    completed.Value.DeferPresenceCommit);
            }
            else
            {
                presence.MarkActive(pid);
                presence.TouchRoom(pid);
            }
            logger.LogInformation(
                "[joinresult] player={Player} instance={Instance} OK ({Label})",
                pid, instanceId, label);
        }
        else
        {
            await joinTimeouts.MarkFailedAsync(pid, instanceId, label);
            logger.LogWarning(
                "[joinresult] player={Player} instance={Instance} FAILED code={Code} ({Label}) — Photon-side error, check Photon AppId/region/auth",
                pid, instanceId, code, label);
        }
        return Ok();
    }

    [HttpPost("/matchmake/none")]
    public async Task<IActionResult> MatchmakeNone()
    {
        var pid = this.CurrentPlayerId() ?? 0;
        if (pid == 0) return Unauthorized();

        var current = presence.GetRoom(pid) ?? await BuildDormRoomInstanceAsync(pid);
        presence.SetRoom(pid, current);
        presence.MarkActive(pid);
        logger.LogInformation("[matchmake-none] player={Player}", pid);
        return Ok(new MatchmakingResponseDto
        {
            ErrorCode = 0,
            RoomInstance = current,
        });
    }

    [HttpGet("/photon_access_token")]
    public IActionResult PhotonAccessToken()
    {
        var pid = this.RequireCurrentPlayerId();
        var appId = config["Photon:AppId"] ?? string.Empty;
        var voiceAppId = config["Photon:VoiceAppId"] ?? string.Empty;
        var region = (config["Photon:CloudRegion"] ?? "us").ToLowerInvariant();
        return Ok(new Dictionary<string, object>
        {
            ["ResultCode"] = 1,
            ["Message"] = string.Empty,
            ["UserId"] = pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["PhotonAppId"] = appId,
            ["PhotonVoiceAppId"] = voiceAppId,
            ["AppId"] = appId,
            ["VoiceAppId"] = voiceAppId,
            ["CloudRegion"] = region,
            ["Region"] = region,
            ["Token"] = string.Empty,
        });
    }

    [HttpPost("/matchmake/dorm")]
    public async Task<IActionResult> MatchmakeDorm()
    {
        var pid = this.CurrentPlayerId() ?? 0;
        if (pid == 0) return Unauthorized();

        var room = await BuildDormRoomInstanceAsync(pid);
        presence.SetRoom(pid, room);
        presence.MarkActive(pid);
        await RecordVisitAsync(pid, room.RoomId);
        logger.LogInformation(
            "[matchmake-dorm] player={Player} room={Room} instance={Instance} photon={Photon}",
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

    /// <summary>GET/POST <c>/roominstance/{id}/roomcode</c> — raw
    /// JSON string room code shown by the watch. Codes are stable for
    /// an instance id and do not expose the numeric id directly.</summary>
    [HttpGet("/roominstance/{instanceId:long}/roomcode")]
    [HttpPost("/roominstance/{instanceId:long}/roomcode")]
    public IActionResult RoomCode(long instanceId)
    {
        var code = RoomCodeService.Generate(instanceId);
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

    /// <summary>GET <c>match.*/room/{id}/instances</c> — the
    /// "RoomInstanceBrowser" the watch shows when you tap a room tile.
    /// Returns the currently-active instances so the player can see
    /// which one their friends are in (and join it) instead of being
    /// matchmade into a fresh empty room. Verified URL at
    /// Matchmaking.txt:6286; response is a
    /// <c>List&lt;SimpleRoomInstance&gt;</c> per
    /// <c>SimpleRoomInstance.cs</c>: RoomInstanceId/RoomId/SubRoomId,
    /// IsFull, CreatedAt, PlayerIds, SubroomName, PlayerCount,
    /// HasModPresent, HashedInstanceId.
    ///
    /// We derive instances from PlayerPresenceService's per-player
    /// RoomInstanceDto: every distinct PhotonRoomId in the same RoomId
    /// is one instance, with the players currently presenced there.
    /// PrivateInstances are surfaced as well so the inviter and
    /// invitee see the same room in the browser.</summary>
    [HttpGet("/room/{roomId:long}/instances")]
    [AllowAnonymous]
    public async Task<IActionResult> RoomInstances(long roomId)
    {
        var presenced = presence.EnumerateActiveInstances(roomId).ToList();
        // Only surface the caller's OWN private instances — we never
        // leak someone else's PhotonRoomId into the browser. The Photon
        // join check still requires CanJoinAsync, but redacting at the
        // list level matches the existing RoomInstanceDto.Redact policy.
        var pid = this.CurrentPlayerId() ?? 0;
        var privates = pid == 0
            ? new List<Data.Entities.PrivateInstanceEntity>()
            : await db.PrivateInstances
                .Where(p => p.RoomId == roomId && p.OwnerPlayerId == pid)
                .ToListAsync();

        // Merge: presence rows are authoritative (player currently
        // there); add any owned private instance the inviter created
        // but nobody's joined yet so the inviter can still see it.
        var byKey = new Dictionary<string, SimpleInstance>(StringComparer.Ordinal);
        foreach (var (room, playerIds) in presenced)
        {
            if (string.IsNullOrEmpty(room.PhotonRoomId)) continue;
            byKey[room.PhotonRoomId] = new SimpleInstance(
                RoomInstanceId: room.RoomInstanceId,
                RoomId: room.RoomId,
                SubRoomId: room.SubRoomId,
                IsFull: false,
                CreatedAt: DateTime.UtcNow,
                PlayerIds: playerIds.Select(p => (int)p).ToList(),
                SubroomName: room.Name ?? string.Empty,
                PlayerCount: playerIds.Count,
                HasModPresent: false,
                HashedInstanceId: room.PhotonRoomId);
        }
        foreach (var p in privates)
        {
            if (string.IsNullOrEmpty(p.PhotonRoomId) || byKey.ContainsKey(p.PhotonRoomId)) continue;
            byKey[p.PhotonRoomId] = new SimpleInstance(
                RoomInstanceId: p.Id,
                RoomId: p.RoomId,
                SubRoomId: p.SubRoomId,
                IsFull: false,
                CreatedAt: DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc),
                PlayerIds: new List<int>(),
                SubroomName: p.Name,
                PlayerCount: 0,
                HasModPresent: false,
                HashedInstanceId: p.PhotonRoomId);
        }

        // Wire shape: SimpleRoomInstance.Deserialize reads CAMELCASE
        // keys — verified at Cpp2IL_ISIL/.../RecNet/SimpleRoomInstance.txt
        // lines 765-808 ({roomInstanceId, roomId, subRoomId, isFull,
        // createdAt, playerIds}). SubroomName / PlayerCount /
        // HasModPresent / HashedInstanceId are populated locally on the
        // watch (PlayerIds.Count, local hash, etc.) — not read from
        // JSON, so we don't send them. Earlier PascalCase response made
        // every field default to 0/null on the watch, breaking the
        // browser.
        return Ok(byKey.Values.Select(i => new
        {
            roomInstanceId = i.RoomInstanceId,
            roomId = i.RoomId,
            subRoomId = i.SubRoomId,
            isFull = i.IsFull,
            createdAt = i.CreatedAt,
            playerIds = i.PlayerIds.ToArray(),
        }));
    }

    private sealed record SimpleInstance(
        long RoomInstanceId, long RoomId, long SubRoomId, bool IsFull,
        DateTime CreatedAt, List<int> PlayerIds, string SubroomName,
        int PlayerCount, bool HasModPresent, string HashedInstanceId);

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
