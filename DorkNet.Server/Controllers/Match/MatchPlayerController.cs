using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Services;
using System.Text.Json.Serialization;

namespace DorkNet.Server.Controllers.Match;

/// <summary>
/// match.rec.net/player/* — endpoints that operate on the local player's
/// matchmaking session. These were previously falling through to the
/// GlobalCatchAllController which returned the kitchen-sink "RecNetResult
/// shotgun" body. That body lacks the required keys for the actual response
/// types and the client crashes with KeyNotFoundException.
///
/// The endpoints here use the same camelCase PlayerPresence shape as
/// PlayerPresenceController (verified at RVA 0x1458920).
/// </summary>
[ApiController]
public class MatchPlayerController(
    PlayerPresenceService presence,
    RoomService rooms,
    PrivateInstanceService privateInstances,
    IConfiguration config,
    DorkNetDbContext db,
    ILogger<MatchPlayerController> log) : ControllerBase
{
    /// <summary>
    /// POST <c>/player/login</c> — the 2020 watch starts every session by
    /// posting here with a fresh <c>LoginLock</c> GUID it generated client-side.
    /// We persist the lock as the canonical "active session" marker for this
    /// player so subsequent heartbeats can be validated against it; a NEW
    /// login overwrites the lock and any older session's heartbeats start
    /// failing, which is the intended kick-the-first-session behaviour.
    /// </summary>
    [HttpPost("/player/login")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public IActionResult PlayerLogin([FromForm(Name = "LoginLock")] string? loginLock)
    {
        var playerId = TryGetCurrentPlayerId();
        if (playerId != 0 && !string.IsNullOrWhiteSpace(loginLock))
        {
            var replaced = presence.SetLock(playerId, loginLock);
            if (replaced)
            {
                log.LogWarning(
                    "[player-login] concurrent session marker replaced for player={PlayerId}; stale SignalR connections will be evicted on hub connect",
                    playerId);
                // Do not broadcast ModerationKick to the whole player group
                // here. During reconnects the active watch can already have a
                // SignalR connection in that group, and the 2020 client crashes
                // hard if it receives a malformed/stale moderation payload.
                // NotifyHub.OnConnectedAsync snapshots existing connection ids
                // before adding the new connection and sends a targeted kick to
                // those ids only.
            }
        }
        return Ok(Array.Empty<object>());
    }

    /// <summary>
    /// POST /player/logout — fires when the client gives up on a session
    /// (e.g. Photon connection couldn't be established, client aborts).
    /// Clear the cached presence so the next /goto starts fresh.
    /// </summary>
    [HttpPost("/player/logout")]
    public IActionResult PlayerLogout()
    {
        var playerId = TryGetCurrentPlayerId();
        if (playerId != 0) presence.Clear(playerId);
        return Ok(new { success = true, error = "" });
    }

    /// <summary>
    /// POST <c>/player/notifydisconnect</c> — fired by
    /// <c>RecNet.Matchmaking.NotifyPlayerDisconnected(int otherPlayerId)</c>
    /// (Matchmaking.cs:326). Body is form-urlencoded
    /// <c>otherPlayerId=&lt;id&gt;</c>. The watch uses this to tell the
    /// server "I just saw this other Photon participant disappear" —
    /// e.g. when someone leaves a room via the menu. Real Rec Room
    /// likely used it to GC sticky presence rows; we just acknowledge
    /// and log so it stops falling through to GlobalCatchAll.
    ///
    /// Returns 200 with no body (BestHTTP ignores the response per
    /// Matchmaking.txt:6668-6699).
    /// </summary>
    [HttpPost("/player/notifydisconnect")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public IActionResult NotifyDisconnect([FromForm(Name = "otherPlayerId")] int? otherPlayerId)
    {
        var caller = TryGetCurrentPlayerId();
        log.LogInformation(
            "[player-notify-disconnect] caller={Caller} reported other={Other} left",
            caller, otherPlayerId);
        return Ok();
    }

    /// <summary>
    /// POST /player/heartbeat — every ~5–30s while connected. Client expects
    /// a PlayerPresence response that mirrors its own cached state.
    /// CRITICAL: must include the player's CURRENT roomInstance, not null.
    /// Returning null while the client knows it's in DormRoom triggers
    ///   "presence heartbeat response indicates local presence is out-of-sync"
    /// repeatedly → eventual "Error: -2" disconnect dialog.
    ///
    /// We pull the current room from PlayerPresenceService, which records
    /// each successful /goto/room/* call.
    /// </summary>
    [HttpPost("/player/heartbeat")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<ActionResult<PresenceDto>> Heartbeat([FromForm(Name = "LoginLock")] string? loginLock)
    {
        // We track the LoginLock per player in /player/login (still useful
        // for admin diagnostics + future session-management) but
        // intentionally DO NOT 401 here on stale locks. Returning 401 on
        // a heartbeat causes the watch to yank the player out of the
        // current Photon room mid-session — symptoms include
        // "Cannot send messages when not connected" + "Head/LeftHand/
        // RightHand being destroyed" + "presence heartbeat response
        // indicates local presence is out-of-sync" — which is worse than
        // letting two sessions co-exist briefly. If we want true
        // single-session enforcement we'll do it via SignalR push to the
        // older session and a graceful client-side disconnect, not via
        // an HTTP 401 mid-heartbeat.
        var playerId = TryGetCurrentPlayerId();
        var room = await GetPresenceRoomForResponseAsync(playerId);
        if (playerId != 0)
        {
            presence.MarkActive(playerId);
            if (room is not null) presence.TouchRoom(playerId);
        }
        return Ok(new PresenceDto
        {
            PlayerId = playerId,
            StatusVisibility = 1,
            DeviceClass = 0,
            VrMovementMode = 0,
            RoomInstance = room,
        });
    }

    [HttpPut("/player/statusvisibility")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<ActionResult<PresenceDto>> SetStatusVisibility([FromForm(Name = "statusVisibility")] int? value)
    {
        var playerId = TryGetCurrentPlayerId();
        var room = await GetPresenceRoomForResponseAsync(playerId);
        return Ok(new PresenceDto
        {
            PlayerId = playerId,
            StatusVisibility = value ?? 1,
            DeviceClass = 0,
            VrMovementMode = 0,
            RoomInstance = room,
        });
    }

    [HttpPut("/player/vrmovementmode")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<ActionResult<PresenceDto>> SetVrMovementMode([FromForm(Name = "vrMovementMode")] int? value)
    {
        var playerId = TryGetCurrentPlayerId();
        var room = await GetPresenceRoomForResponseAsync(playerId);
        return Ok(new PresenceDto
        {
            PlayerId = playerId,
            StatusVisibility = 1,
            DeviceClass = 0,
            VrMovementMode = value ?? 0,
            RoomInstance = room,
        });
    }

    /// <summary>
    /// GET <c>/player/avoidjuniors</c> — returns a primitive boolean.
    /// Verified against
    /// Cpp2IL_ISIL/.../RecNet/Matchmaking.txt:7728 (URL) and 7737
    /// (<c>PromiseExtensions.ExpectPrimitiveResponse</c>) — the watch
    /// expects a bare JSON <c>true</c>/<c>false</c>, not an array or
    /// object. Returning <c>[]</c> from the catch-all triggered
    /// "Failed to get avoid juniors status: Malformed Response" + a
    /// Convert.ChangeType InvalidCastException, which left the
    /// matchmaking init in a never-completes state on some boot flows.
    /// </summary>
    [HttpGet("/player/avoidjuniors")]
    public ActionResult<bool> GetAvoidJuniors() => Ok(false);

    /// <summary>PUT <c>/player/avoidjuniors</c> — body
    /// <c>avoidJuniors=true|false</c> (Matchmaking.txt:7883). Stub for
    /// now; the multi-tenant server doesn't currently track this
    /// preference per-player. Returns the new value as a JSON
    /// primitive so the watch's ExpectPrimitiveResponse is happy.</summary>
    [HttpPut("/player/avoidjuniors")]
    [HttpPost("/player/avoidjuniors")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public ActionResult<bool> SetAvoidJuniors([FromForm(Name = "avoidJuniors")] bool? value)
        => Ok(value ?? false);

    /// <summary>POST <c>/player/photonregionpings</c> — watch reports
    /// its measured Photon region ping vector so the server can pick
    /// the best region for this player's matchmaking. Body is form-
    /// urlencoded: <c>region=eu&amp;ping=42&amp;region=us&amp;ping=80&amp;...</c>
    /// (one (region, ping) pair per row, repeated). Persists the
    /// freshest measurement per (player, region) pair in
    /// <see cref="PlayerPresenceService"/>'s in-memory map — the
    /// matchmaking GoTo flow reads this to bias instance creation
    /// toward the player's lowest-ping region.
    /// Verified URL at Matchmaking.txt:7280.</summary>
    // 2020.12 calls this with the BestHTTP method enum value 3 (= PUT, not POST).
    // Server-side route was POST → 405 Method Not Allowed → "Failed to upload
    // photon region pings: HTTP Error 405". Accept GET/POST/PUT to cover both
    // 2020.03 (POST) and 2020.12 (PUT) without guessing wrong on the enum.
    [HttpPost("/player/photonregionpings")]
    [HttpPut("/player/photonregionpings")]
    [HttpGet("/player/photonregionpings")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public IActionResult PhotonRegionPings()
    {
        var playerId = TryGetCurrentPlayerId();
        if (playerId == 0) return Ok();

        var form = Request.Form;
        var regions = form["region"].ToArray();
        var pings = form["ping"].ToArray();
        var pairs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < Math.Min(regions.Length, pings.Length); i++)
        {
            var r = regions[i]?.Trim();
            if (string.IsNullOrEmpty(r)) continue;
            if (!int.TryParse(pings[i], out var p)) continue;
            // Sanity bound — discard wild values from misbehaving clients.
            pairs[r] = Math.Clamp(p, 0, 2000);
        }
        if (pairs.Count > 0)
            presence.SetPhotonRegionPings(playerId, pairs);
        log.LogInformation("[player-region-pings] player={Player} regions={Count}", playerId, pairs.Count);
        return Ok();
    }

    // Heartbeat / logout are unauthenticated endpoints (the client hits them
    // before any token is acquired in some boot flows), so we use the
    // optional `CurrentPlayerId()` extension and treat 0 as "anonymous".
    // Cast to int because the wire shape's PlayerId field is an int — fits
    // every accountId we mint (random in [100_000, 9_999_999)).
    private int TryGetCurrentPlayerId() =>
        this.CurrentPlayerId() is long id ? (int)id : 0;

    private async Task<RoomInstanceDto?> GetPresenceRoomForResponseAsync(int playerId)
    {
        if (playerId == 0) return null;

        var room = presence.GetRoom(playerId);
        if (room is null)
        {
            // Boot-time race: the watch hits /player/heartbeat before
            // /goto/room/DormRoom has finished recording presence. The
            // watch's OnPresenceHeartbeatResponse compares
            // (cachedRoomInstance != null) XOR (serverRoomInstance != null)
            // (Cpp2IL_ISIL/.../RecNet/Matchmaking.txt:8584) — if its local
            // cache already has a dorm but we hand back null, it fires
            // "RecNet presence heartbeat response indicates local presence
            // is out-of-sync!" and tears down the local player (the
            // RigidbodyEx Head/Hand "Cannot set the parent" warnings at
            // [line 585] are the destruction shrapnel). Synthesise a
            // default dorm RoomInstance instead so the heartbeat never
            // returns null for an authenticated player. Matches what
            // /goto/room/DormRoom builds — same RoomId, same
            // PhotonRoomId pattern, same location GUID — so the watch's
            // RoomInstancesAreDifferent finds them identical.
            var fallback = await BuildDefaultDormPresenceAsync(playerId);
            if (fallback is not null)
            {
                log.LogInformation(
                    "[player-heartbeat] synthesised dorm fallback player={PlayerId} room={RoomId} dataBlob={DataBlob} (presence cache miss)",
                    playerId, fallback.RoomId, fallback.DataBlob);
                presence.SetRoom(playerId, fallback);
                presence.MarkActive(playerId);
                return fallback;
            }
            log.LogInformation("[player-heartbeat] player={PlayerId} room=null", playerId);
            return null;
        }

        var resolvedDataBlob = await ResolveDataBlobForPresenceAsync(room.RoomId, playerId, room.RoomInstanceId);
        if (!string.IsNullOrWhiteSpace(resolvedDataBlob)
            && !string.Equals(room.DataBlob, resolvedDataBlob, StringComparison.Ordinal))
        {
            log.LogWarning(
                "[player-heartbeat] repairing stale presence dataBlob player={PlayerId} room={RoomId} instance={InstanceId} cached={CachedDataBlob} resolved={ResolvedDataBlob}",
                playerId, room.RoomId, room.RoomInstanceId, room.DataBlob, resolvedDataBlob);
            room.DataBlob = resolvedDataBlob;
            presence.SetRoom(playerId, room);
        }
        presence.MarkActive(playerId);
        presence.TouchRoom(playerId);

        log.LogInformation(
            "[player-heartbeat] player={PlayerId} room={RoomId} instance={InstanceId} subRoom={SubRoomId} location={Location} photon={PhotonRoomId} dataBlob={DataBlob}",
            playerId, room.RoomId, room.RoomInstanceId, room.SubRoomId, room.Location, room.PhotonRoomId, room.DataBlob);
        return room;
    }

    /// <summary>Build a RoomInstance that matches the shape
    /// <c>/goto/room/DormRoom</c> would have produced for this player
    /// — used as a heartbeat fallback when presence is empty (boot
    /// race, replica restart, Redis TTL expiry).
    ///
    /// CRITICAL: this MUST reproduce <c>/goto</c>'s RoomInstance
    /// byte-for-byte (RoomInstanceId, PhotonRoomId, Location, DataBlob),
    /// or the watch's RoomInstancesAreDifferent fires
    /// "presence heartbeat response indicates local presence is
    /// out-of-sync!" and boot-loops the player. The previous version
    /// computed <c>RoomInstanceId = roomId * 100_000</c>, but
    /// <c>GoToController</c> stamps the dorm with
    /// <c>EnsureForDormAsync</c>'s id (<c>0xDD0000 ^ …</c>) — so the two
    /// disagreed and every cache-miss heartbeat (e.g. right after a
    /// server restart) kicked the player. We now go through the exact
    /// same <see cref="RoomService.EnsurePersonalDormAsync"/> +
    /// <see cref="PrivateInstanceService.EnsureForDormAsync"/> path
    /// <c>/goto/room/DormRoom</c> uses, so the id math can never drift
    /// again. Returns null only for anonymous callers.</summary>
    private async Task<RoomInstanceDto?> BuildDefaultDormPresenceAsync(int playerId)
    {
        if (playerId == 0) return null;

        // Same resolution /goto does: the caller's personal dorm row is
        // the source of truth for RoomId + LocationReplicationId.
        var personal = await rooms.EnsurePersonalDormAsync(playerId);
        var roomId = personal.Id;
        var location = personal.LocationReplicationId;

        var dataBlob = await ResolveDataBlobForPresenceAsync(roomId, playerId);
        var photonRegion = (config["Photon:CloudRegion"] ?? "us").ToLowerInvariant();
        var photonRoomId = $"^dormroom_p{playerId}";
        var dormInstanceName = await BuildDormInstanceNameAsync(playerId);

        // EnsureForDormAsync owns the deterministic dorm instance-id
        // formula; reusing it here guarantees the heartbeat's
        // RoomInstanceId equals the one /goto handed the watch.
        var dorm = await privateInstances.EnsureForDormAsync(
            ownerPlayerId: playerId,
            roomId: roomId,
            subRoomId: 0,
            baseName: dormInstanceName,
            location: location,
            dataBlob: dataBlob,
            photonRegion: photonRegion,
            maxCapacity: personal.MaxCapacity,
            photonRoomId: photonRoomId);

        return new RoomInstanceDto
        {
            RoomInstanceId = dorm.InstanceId,
            RoomId = roomId,
            SubRoomId = 0,
            Location = location,
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

    private async Task<string> ResolveDataBlobForPresenceAsync(
        long roomId,
        int playerId,
        long? roomInstanceId = null)
    {
        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return string.Empty;

        if (room.IsDormRoom)
        {
            var dormBlob = await rooms.ResolveDormDataBlobNameAsync(playerId, room.Id);
            if (!string.IsNullOrWhiteSpace(dormBlob)) return dormBlob;
        }

        if (roomInstanceId is > 0)
        {
            var instanceBlob = await db.PrivateInstances.AsNoTracking()
                .Where(instance => instance.Id == roomInstanceId && instance.RoomId == roomId)
                .Select(instance => instance.DataBlob)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(instanceBlob)) return instanceBlob;
        }

        if (!string.IsNullOrWhiteSpace(room.CurrentDataBlobName)) return room.CurrentDataBlobName;

        if (room.IsDormRoom) return string.Empty;

        var customisable = room.CreatorPlayerId != 1;
        return customisable ? $"room_{room.Id}_v1.dat" : string.Empty;
    }
}

/// <summary>
/// Wire shape of RecNet.PlayerPresence (RVA 0x1458920). Lives here as a
/// local DTO; same fields as PlayerPresenceController.PlayerPresenceDto.
/// </summary>
public class PresenceDto
{
    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("statusVisibility")]
    public int StatusVisibility { get; set; }

    [JsonPropertyName("deviceClass")]
    public int DeviceClass { get; set; }

    [JsonPropertyName("vrMovementMode")]
    public int VrMovementMode { get; set; }

    [JsonPropertyName("roomInstance")]
    public object? RoomInstance { get; set; } = null;

    // 2020.12 PlayerPresence.Deserialize reads these as required keys.
    [JsonPropertyName("isOnline")]
    public bool IsOnline { get; set; } = true;

    [JsonPropertyName("appVersion")]
    public int AppVersion { get; set; } = 20201210;
}
