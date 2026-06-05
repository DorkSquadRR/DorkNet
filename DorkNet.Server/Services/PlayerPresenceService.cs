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
/// server says, and is the only passive path that extends the room
/// TTL.
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
/// refreshes the value and heartbeat refreshes the TTL. Read-only
/// presence queries intentionally do not refresh it, otherwise admin
/// pages and friends-list polls keep stale rooms alive forever. When
/// no Redis is configured, falls back to a process-local
/// <see cref="ConcurrentDictionary"/>
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
    private readonly ConcurrentDictionary<long, DateTimeOffset> _localRoomExpires = new();
    private readonly ConcurrentDictionary<long, DateTimeOffset> _localActiveExpires = new();
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

    /// <summary>
    /// Short online-status marker refreshed by heartbeat/join activity.
    /// This smooths over flaky SignalR notify sockets without letting a
    /// stale room presence make someone look online for the full room TTL.
    /// </summary>
    public static TimeSpan ActivityTtl { get; } = TimeSpan.FromSeconds(90);

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
            _localRoomExpires[playerId] = DateTimeOffset.UtcNow.Add(PresenceTtl);
            _log?.LogDebug("[presence] set local player={PlayerId} room={Room}",
                playerId, room?.Name ?? "<null>");
        }
    }

    public void TouchRoom(long playerId)
    {
        if (_redis is { } mux)
        {
            try
            {
                mux.GetDatabase().KeyExpire(Key(playerId), PresenceTtl);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "[presence] redis EXPIRE failed for player={PlayerId}", playerId);
            }
            return;
        }

        if (_local.ContainsKey(playerId))
            _localRoomExpires[playerId] = DateTimeOffset.UtcNow.Add(PresenceTtl);
    }

    public void MarkActive(long playerId)
    {
        if (_redis is { } mux)
        {
            try
            {
                mux.GetDatabase().StringSet(ActiveKey(playerId), "1", ActivityTtl);
                mux.GetDatabase().SetAdd(ActiveSetKey, playerId);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "[presence] redis active SET failed for player={PlayerId}", playerId);
            }
            return;
        }

        _localActiveExpires[playerId] = DateTimeOffset.UtcNow.Add(ActivityTtl);
    }

    public IReadOnlyCollection<long> RecentlyActivePlayerIds()
    {
        if (_redis is { } mux)
        {
            try
            {
                var db = mux.GetDatabase();
                var members = db.SetMembers(ActiveSetKey);
                if (members.Length == 0) return Array.Empty<long>();

                var activeIds = new List<long>(members.Length);
                var stale = new List<RedisValue>();
                foreach (var member in members)
                {
                    if (!long.TryParse(member.ToString(), out var playerId))
                    {
                        stale.Add(member);
                        continue;
                    }

                    if (db.KeyExists(ActiveKey(playerId)))
                        activeIds.Add(playerId);
                    else
                        stale.Add(member);
                }

                if (stale.Count > 0)
                    db.SetRemove(ActiveSetKey, stale.ToArray());
                return activeIds;
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "[presence] redis active scan failed");
                return Array.Empty<long>();
            }
        }

        var now = DateTimeOffset.UtcNow;
        var active = new List<long>();
        foreach (var kv in _localActiveExpires)
        {
            if (kv.Value > now)
            {
                active.Add(kv.Key);
            }
            else
            {
                _localActiveExpires.TryRemove(kv.Key, out _);
            }
        }
        return active;
    }

    public bool IsRecentlyActive(long playerId)
    {
        if (_redis is { } mux)
        {
            try
            {
                return mux.GetDatabase().KeyExists(ActiveKey(playerId));
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "[presence] redis active GET failed for player={PlayerId}", playerId);
                return false;
            }
        }

        if (!_localActiveExpires.TryGetValue(playerId, out var expires)) return false;
        if (expires > DateTimeOffset.UtcNow) return true;
        _localActiveExpires.TryRemove(playerId, out _);
        return false;
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
                return JsonSerializer.Deserialize<RoomInstanceDto>(v.ToString(), JsonOpts);
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "[presence] redis GET failed for player={PlayerId}", playerId);
                return null;
            }
        }
        if (!_local.TryGetValue(playerId, out var room)) return null;
        if (_localRoomExpires.TryGetValue(playerId, out var expires)
            && expires <= DateTimeOffset.UtcNow)
        {
            _local.TryRemove(playerId, out _);
            _localRoomExpires.TryRemove(playerId, out _);
            _localActiveExpires.TryRemove(playerId, out _);
            return null;
        }
        return room;
    }

    public void Clear(long playerId)
    {
        if (_redis is { } mux)
        {
            var db = mux.GetDatabase();
            db.KeyDelete(Key(playerId));
            db.KeyDelete(ActiveKey(playerId));
            db.SetRemove(ActiveSetKey, playerId);
        }
        else
        {
            _local.TryRemove(playerId, out _);
            _localRoomExpires.TryRemove(playerId, out _);
            _localActiveExpires.TryRemove(playerId, out _);
        }
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
    private static string ActiveKey(long playerId) => $"presence-active:{playerId}";
    private const string ActiveSetKey = "presence-active";

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
