using System.Collections.Concurrent;
using System.Text.Json;
using DorkNet.Server.Controllers.Match;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DorkNet.Server.Services;

/// <summary>
/// Cross-replica tracker for which RoomInstance each authenticated
/// player most recently went to via /goto. The presence heartbeat
/// reads this back so the client's local state matches what the
/// server says.
///
/// Why this exists: every ~5s the client POSTs /player/heartbeat,
/// expecting a PlayerPresence response that mirrors its own cached
/// presence. If the server returns roomInstance:null but the client
/// knows it's in DormRoom, RecNet.Matchmaking.OnPresenceHeartbeatResponse
/// logs "presence heartbeat response indicates local presence is
/// out-of-sync" repeatedly, the client gives up after a few rounds,
/// and shows the "Error: -2 / We lost the connection to Rec Room"
/// UI even though no network connection actually dropped.
///
/// **Multi-replica**: when <see cref="IConnectionMultiplexer"/> is
/// available, presence lives in Redis as <c>presence:{playerId}</c>
/// (JSON-serialised <see cref="RoomInstanceDto"/>). EXPIRE is set to
/// <see cref="PresenceTtl"/> so a player who silently drops
/// (uninstall, kill -9) ages out automatically; their next /goto
/// refreshes the value AND the TTL. When no Redis is configured,
/// falls back to a process-local <see cref="ConcurrentDictionary"/>
/// — identical to pre-PR-2 behaviour and fine for single-instance
/// dev workflows.
///
/// State is intentionally non-persistent — a server restart drops
/// every presence record, and clients re-establish via their next
/// /goto call. Method signatures stay sync because the call sites
/// (heartbeat, /goto, room-state queries) are too numerous to pay
/// the cost of an async cascade. Sync StackExchange.Redis ops are
/// sub-millisecond on the same network at our QPS.
/// </summary>
public class PlayerPresenceService
{
    private readonly ConcurrentDictionary<long, RoomInstanceDto> _local = new();
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<PlayerPresenceService>? _log;

    /// <summary>
    /// Presence must survive long room loads. The 2020 client can go
    /// quiet for ~45 seconds while downloading/deserializing large
    /// uploaded rooms; a 45s Redis TTL expires right before the next
    /// heartbeat, making the server synthesize dorm fallback presence
    /// and effectively bouncing the player back to dorm. Ten minutes
    /// still ages out abandoned sessions, but does not punish slow
    /// room loads or temporary network stalls.
    /// </summary>
    public static TimeSpan PresenceTtl { get; } = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        // Match the wire format used by the matchmaking response so
        // the round-trip is lossless. Properties are already tagged
        // with [JsonPropertyName] for camelCase wire keys.
        PropertyNamingPolicy = null,
    };

    public PlayerPresenceService(
        IConnectionMultiplexer? redis = null,
        ILogger<PlayerPresenceService>? log = null)
    {
        _redis = redis;
        _log = log;
    }

    public void SetRoom(long playerId, RoomInstanceDto room)
    {
        if (_redis is { } mux)
        {
            try
            {
                var json = JsonSerializer.Serialize(room, JsonOpts);
                mux.GetDatabase().StringSet(Key(playerId), json, PresenceTtl);
                _log?.LogDebug("[presence] set redis player={PlayerId} room={Room} bytes={Bytes}",
                    playerId, room?.Name ?? "<null>", json.Length);
            }
            catch (Exception ex)
            {
                // Don't let a Redis blip take down the goto handler — log
                // and fall through. Heartbeat will see no presence and the
                // watch will resync via its next /goto.
                _log?.LogWarning(ex, "[presence] redis SET failed for player={PlayerId}", playerId);
            }
        }
        else
        {
            _local[playerId] = room;
            _log?.LogDebug("[presence] set local player={PlayerId} room={Room}",
                playerId, room?.Name ?? "<null>");
        }
    }

    public RoomInstanceDto? GetRoom(long playerId)
    {
        if (_redis is { } mux)
        {
            try
            {
                var db = mux.GetDatabase();
                var key = Key(playerId);
                var v = db.StringGet(key);
                if (!v.HasValue)
                {
                    _log?.LogDebug("[presence] miss redis player={PlayerId}", playerId);
                    return null;
                }
                db.KeyExpire(key, PresenceTtl);
                return JsonSerializer.Deserialize<RoomInstanceDto>(v.ToString(), JsonOpts);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "[presence] redis GET failed for player={PlayerId}", playerId);
                return null;
            }
        }
        return _local.TryGetValue(playerId, out var room) ? room : null;
    }

    public void Clear(long playerId)
    {
        if (_redis is { } mux) mux.GetDatabase().KeyDelete(Key(playerId));
        else _local.TryRemove(playerId, out _);
    }

    /// <summary>List the currently-active instances of a given room,
    /// grouped by PhotonRoomId, with the player ids currently presenced
    /// in each. Drives the watch's RoomInstanceBrowser
    /// (<c>GET match.*/room/{id}/instances</c>) so players can see
    /// which instance their friends are in.
    ///
    /// Redis caveat: without a secondary index per room we'd need a
    /// SCAN over every presence key — too expensive to do on each
    /// request. For now the Redis path returns empty and the local
    /// in-process dictionary is the source of truth. Single-instance
    /// deployments work; multi-replica Redis deployments need a
    /// per-room set index to be added before the browser is reliable
    /// there.</summary>
    public IEnumerable<(RoomInstanceDto Room, List<long> PlayerIds)> EnumerateActiveInstances(long roomId)
    {
        if (_redis is not null) return Array.Empty<(RoomInstanceDto, List<long>)>();
        var groups = new Dictionary<string, (RoomInstanceDto Room, List<long> PlayerIds)>(StringComparer.Ordinal);
        foreach (var kv in _local)
        {
            var room = kv.Value;
            if (room is null || room.RoomId != roomId) continue;
            // Skip private instances — those are surfaced separately
            // in the controller via PrivateInstanceService so we never
            // leak a photonRoomId for an invite-only match into the
            // public browser.
            if (room.IsPrivate) continue;
            var key = string.IsNullOrEmpty(room.PhotonRoomId)
                ? $"<{room.RoomInstanceId}>"
                : room.PhotonRoomId;
            if (!groups.TryGetValue(key, out var entry))
            {
                entry = (room, new List<long>());
                groups[key] = entry;
            }
            entry.PlayerIds.Add(kv.Key);
        }
        return groups.Values;
    }

    private static string Key(long playerId) => $"presence:{playerId}";

    // ── LoginLock single-session enforcement ─────────────────────────
    // The 2020 watch generates a fresh GUID per session and sends it
    // in the `LoginLock` form field on every match.* request. Real
    // Rec Room rejects heartbeats whose lock doesn't match the
    // most-recent /player/login — that's how a second login on the
    // same account kicks the first session. We mirror that by storing
    // the most-recent lock in Redis (or the in-process fallback)
    // keyed by player id, with the same 45-s TTL as presence.
    //
    // SetLock OVERWRITES regardless of prior value — newest login
    // wins. ValidateLock returns false when the caller's lock is stale
    // or absent, which the heartbeat handler maps to a
    // session-terminated response so the watch logs out.

    private readonly ConcurrentDictionary<long, string> _localLocks = new();

    public bool SetLock(long playerId, string loginLock)
    {
        if (string.IsNullOrWhiteSpace(loginLock)) return false;
        var replacedActiveSession = false;
        if (_redis is { } mux)
        {
            try
            {
                var db = mux.GetDatabase();
                var key = LockKey(playerId);
                var previous = db.StringGet(key);
                replacedActiveSession = previous.HasValue &&
                    !string.Equals(previous.ToString(), loginLock, StringComparison.Ordinal);
                db.StringSet(key, loginLock, PresenceTtl);
            }
            catch (Exception ex) { _log?.LogWarning(ex, "[lock] redis SET failed for player={PlayerId}", playerId); }
        }
        else
        {
            replacedActiveSession = _localLocks.TryGetValue(playerId, out var previous) &&
                !string.Equals(previous, loginLock, StringComparison.Ordinal);
            _localLocks[playerId] = loginLock;
        }

        if (replacedActiveSession)
            _log?.LogWarning("[lock] concurrent login detected for player={PlayerId}; newest LoginLock wins", playerId);
        return replacedActiveSession;
    }

    public bool ValidateLock(long playerId, string? loginLock)
    {
        if (string.IsNullOrWhiteSpace(loginLock)) return false;
        if (_redis is { } mux)
        {
            try
            {
                var v = mux.GetDatabase().StringGet(LockKey(playerId));
                if (!v.HasValue) return false;
                // Refresh TTL on each successful validate so the
                // active session keeps its lock alive across heartbeats.
                if (string.Equals(v.ToString(), loginLock, StringComparison.Ordinal))
                {
                    mux.GetDatabase().KeyExpire(LockKey(playerId), PresenceTtl);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "[lock] redis GET failed for player={PlayerId}", playerId);
                return true; // fail open — better double-session than mass kick on Redis blip
            }
        }
        return _localLocks.TryGetValue(playerId, out var stored)
            && string.Equals(stored, loginLock, StringComparison.Ordinal);
    }

    private static string LockKey(long playerId) => $"loginlock:{playerId}";

    // ── Photon region pings ──────────────────────────────────────────
    // The 2020 watch reports its measured Photon-region latencies in a
    // single POST after the matchmaking init handshake (see
    // MatchPlayerController.PhotonRegionPings). We hold the latest
    // reading per player in process memory only — region preferences
    // change infrequently, lose-on-restart is fine, and pushing this
    // through Redis would just bloat the key space for no gain.

    private readonly ConcurrentDictionary<long, IReadOnlyDictionary<string, int>> _regionPings = new();

    public void SetPhotonRegionPings(long playerId, IReadOnlyDictionary<string, int> pings)
    {
        if (pings.Count == 0) return;
        _regionPings[playerId] = pings;
    }

    /// <summary>Returns the region code with the lowest reported ping,
    /// or <c>null</c> when no pings were ever received for the
    /// player (matchmaking falls back to its default region in that
    /// case).</summary>
    public string? GetPreferredPhotonRegion(long playerId)
    {
        if (!_regionPings.TryGetValue(playerId, out var pings) || pings.Count == 0)
            return null;
        string? best = null;
        var bestMs = int.MaxValue;
        foreach (var kv in pings)
        {
            if (kv.Value < bestMs)
            {
                bestMs = kv.Value;
                best = kv.Key;
            }
        }
        return best;
    }
}
