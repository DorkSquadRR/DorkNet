using System.Collections.Concurrent;
using StackExchange.Redis;

namespace DorkNet.Server.Services;

/// <summary>
/// Cross-replica tracker for the set of currently-online players (anyone
/// with at least one active SignalR connection somewhere in the cluster).
///
/// **Replaces the static <c>NotifyHub._byPlayer</c> dictionary** which
/// only tracked connections on the local replica. With horizontal scale,
/// admin tools that asked "is player X online" got a per-replica answer
/// — they'd see a player as offline if that player happened to be
/// connected to a different replica. This service centralises the
/// answer in Redis so all replicas agree.
///
/// **Two Redis keys per player**:
/// <list type="bullet">
/// <item><c>notify:conns:{playerId}</c> — Set of active connection ids
///     for that player on any replica. Connections add themselves on
///     <see cref="Hubs.NotifyHub.OnConnectedAsync"/> and remove on
///     <see cref="Hubs.NotifyHub.OnDisconnectedAsync"/>. The set has
///     a 24h EXPIRE backstop so a replica that crashes mid-flight
///     doesn't leak connection ids forever.</item>
/// <item><c>notify:online</c> — Master set of player ids that have at
///     least one active connection. Maintained explicitly: SADD when
///     the conns set transitions empty→non-empty, SREM when it goes
///     non-empty→empty. <see cref="OnlinePlayerIds"/> returns SMEMBERS
///     in one round-trip.</item>
/// </list>
///
/// Falls back to in-process state when no Redis is configured (local dev).
/// Same semantics — just per-replica visibility.
/// </summary>
public class OnlinePresenceService
{
    private readonly IConnectionMultiplexer? _redis;
    // Local fallback only. The outer dict maps playerId → connection-id set.
    private readonly ConcurrentDictionary<long, HashSet<string>> _localConns = new();

    /// <summary>Aggressive backstop: if a replica vanishes ungracefully,
    /// the conns set ages out instead of leaking forever. Real disconnect
    /// events should remove specific connection ids well before this.</summary>
    public static TimeSpan ConnectionsKeyTtl { get; } = TimeSpan.FromHours(24);

    public OnlinePresenceService(IConnectionMultiplexer? redis = null) => _redis = redis;

    /// <summary>Register one SignalR connection. Returns true when that was the
    /// player's FIRST active connection, i.e. they just came online and friends
    /// should be told. Mirrors <see cref="RemoveConnectionAsync"/>, which
    /// reports the opposite transition.</summary>
    public async Task<bool> AddConnectionAsync(long playerId, string connectionId)
    {
        if (_redis is { } mux)
        {
            var db = mux.GetDatabase();
            var connsKey = ConnsKey(playerId);
            var added = await db.SetAddAsync(connsKey, connectionId);
            await db.KeyExpireAsync(connsKey, ConnectionsKeyTtl);
            // Only flip the master `online` set when this is the FIRST
            // connection for the player — saves a SADD on every retry
            // reconnect and keeps the online set tight.
            if (added)
            {
                var size = await db.SetLengthAsync(connsKey);
                if (size == 1)
                {
                    await db.SetAddAsync(OnlineSetKey, playerId);
                    return true;
                }
            }
            return false;
        }

        var set = _localConns.GetOrAdd(playerId, _ => new HashSet<string>());
        lock (set)
        {
            set.Add(connectionId);
            return set.Count == 1;
        }
    }

    /// <summary>Remove one SignalR connection. Returns true when that was
    /// the player's final active connection and they should now be treated
    /// as offline by friends-list presence.</summary>
    public async Task<bool> RemoveConnectionAsync(long playerId, string connectionId)
    {
        if (_redis is { } mux)
        {
            var db = mux.GetDatabase();
            var connsKey = ConnsKey(playerId);
            var removed = await db.SetRemoveAsync(connsKey, connectionId);
            if (removed)
            {
                var remaining = await db.SetLengthAsync(connsKey);
                if (remaining == 0)
                {
                    // Last connection for this player gone — drop them
                    // from the online set so admin queries see "offline".
                    await db.SetRemoveAsync(OnlineSetKey, playerId);
                    await db.KeyDeleteAsync(connsKey);
                    return true;
                }
            }
            return false;
        }

        if (_localConns.TryGetValue(playerId, out var set))
        {
            lock (set)
            {
                set.Remove(connectionId);
                if (set.Count != 0) return false;
            }

            _localConns.TryRemove(playerId, out _);
            return true;
        }

        return false;
    }

    /// <summary>Snapshot of SignalR connection ids that currently belong
    /// to <paramref name="playerId"/>. Used by <see cref="DorkNet.Server.Hubs.NotifyHub.OnConnectedAsync"/>
    /// to detect a second-session join and kick the existing
    /// connection(s) via a targeted ModerationKick push. Returns an
    /// empty collection if nobody (or only the current caller) is
    /// connected — never null.</summary>
    public IReadOnlyCollection<string> GetConnections(long playerId)
    {
        if (_redis is { } mux)
        {
            try
            {
                var members = mux.GetDatabase().SetMembers(ConnsKey(playerId));
                if (members.Length == 0) return Array.Empty<string>();
                var result = new string[members.Length];
                for (int i = 0; i < members.Length; i++) result[i] = members[i].ToString();
                return result;
            }
            catch
            {
                // Redis sick → fall through to local fallback rather
                // than pretend there are no existing connections.
            }
        }
        if (!_localConns.TryGetValue(playerId, out var set)) return Array.Empty<string>();
        lock (set) return set.ToArray();
    }

    /// <summary>Snapshot of currently-online player ids across the
    /// cluster. Returns an empty array when nobody's online; never null.
    /// Callers typically <c>.ToHashSet()</c> the result for membership
    /// queries inside a tight loop.</summary>
    public IReadOnlyCollection<long> OnlinePlayerIds()
    {
        if (_redis is { } mux)
        {
            var members = mux.GetDatabase().SetMembers(OnlineSetKey);
            var result = new long[members.Length];
            for (int i = 0; i < members.Length; i++)
                result[i] = (long)members[i];
            return result;
        }
        return _localConns.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToArray();
    }

    private const string OnlineSetKey = "notify:online";
    private static string ConnsKey(long playerId) => $"notify:conns:{playerId}";
}
