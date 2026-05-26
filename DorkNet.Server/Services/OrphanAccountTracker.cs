using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace DorkNet.Server.Services;

/// <summary>
/// Cross-replica ledger of "freshly created accounts that haven't logged
/// in yet." Backs the orphan-cleanup pattern around <c>account/create</c>:
///
///   • The 2020 watch boot flow always calls <c>account/create</c>
///     after <c>cachedlogin</c> as a defensive "ensure my account
///     exists" probe, regardless of whether the user is picking a
///     cached account or making a brand-new one. We can't tell which
///     intent it is from the request alone — both flows look identical
///     on the wire.
///
///   • Solution: <c>account/create</c> ALWAYS creates a new row (so the
///     "Create new account" UI is honoured), but the new id gets
///     registered here. When the watch's next call is
///     <c>cached_login</c> with a DIFFERENT account_id (the boot-flow
///     case — user picked their cached account), the cached_login
///     handler reaches into this tracker and deletes the just-created
///     orphan. When cached_login picks the same id (the "create new"
///     case — user is logging in to the new account), we leave it.
///
/// Entries expire after <see cref="Ttl"/> via Redis EXPIRE so a stale
/// boot doesn't keep a row orphaned indefinitely. Keys are namespaced
/// <c>orphan:{deviceId}|{platformId}</c>.
///
/// **Multi-replica**: when <see cref="IConnectionMultiplexer"/> is
/// available (Coolify production), the state lives in Redis and is
/// visible across every replica — so a <c>POST account/create</c> on
/// replica A followed by <c>POST connect/token (cached_login)</c> on
/// replica B sees the same orphan id and cleans it up correctly. When
/// no Redis is configured (local single-instance dev), falls back to
/// a process-local <see cref="ConcurrentDictionary"/> with the same
/// semantics — identical to pre-PR-2 behaviour.
/// </summary>
public class OrphanAccountTracker
{
    private record Entry(long AccountId, DateTime CreatedAt);
    private readonly ConcurrentDictionary<string, Entry> _local = new();
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<OrphanAccountTracker>? _logger;

    public OrphanAccountTracker(IConnectionMultiplexer? redis = null,
                                ILogger<OrphanAccountTracker>? logger = null)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>Time window after creation during which a different
    /// cached_login call counts as a boot-flow orphan.</summary>
    public static TimeSpan Ttl { get; } = TimeSpan.FromMinutes(5);

    /// <summary>Record that <paramref name="accountId"/> was just
    /// created via account/create on the given device. Overwrites any
    /// older pending entry for the same device — we only care about
    /// the most recent in-flight creation.</summary>
    public void TrackCreation(string? deviceId, string? platformId, long accountId)
    {
        var key = MakeKey(deviceId, platformId);
        if (_redis is { } mux)
        {
            try
            {
                // SET key value EX 300 — atomic create-or-overwrite with TTL.
                mux.GetDatabase().StringSet(key, accountId, Ttl);
                return;
            }
            catch (RedisException ex)
            {
                // Redis sick (AOF full, MISCONF, connection drop). Fall
                // through to local tracking so login keeps working — the
                // worst case is that orphan recovery loses cross-replica
                // visibility until Redis recovers.
                _logger?.LogWarning(ex,
                    "[orphan-tracker] Redis SET failed for {Key}; falling back to local", key);
            }
        }
        _local[key] = new Entry(accountId, DateTime.UtcNow);
    }

    /// <summary>Look up the in-flight creation for this device. Returns
    /// null when there's no pending entry, or when the entry has
    /// expired. Read-only — the entry stays around for a possible
    /// later <see cref="Clear"/> call.</summary>
    public long? PeekPending(string? deviceId, string? platformId)
    {
        var key = MakeKey(deviceId, platformId);
        if (_redis is { } mux)
        {
            try
            {
                var v = mux.GetDatabase().StringGet(key);
                // RedisValue is implicitly convertible — null/missing returns
                // RedisValue.Null which TryParse would bail on.
                if (v.HasValue && long.TryParse(v.ToString(), out var id)) return id;
                // Fall through to local check — a previous TrackCreation
                // might have fallen back to local while Redis was sick.
            }
            catch (RedisException ex)
            {
                _logger?.LogWarning(ex,
                    "[orphan-tracker] Redis GET failed for {Key}; falling back to local", key);
            }
        }
        if (!_local.TryGetValue(key, out var entry)) return null;
        if (DateTime.UtcNow - entry.CreatedAt > Ttl)
        {
            _local.TryRemove(key, out _);
            return null;
        }
        return entry.AccountId;
    }

    /// <summary>Drop the pending entry for this device — typically
    /// called after we've decided whether to delete the orphan or keep
    /// it. Idempotent.</summary>
    public void Clear(string? deviceId, string? platformId)
    {
        var key = MakeKey(deviceId, platformId);
        if (_redis is { } mux)
        {
            try { mux.GetDatabase().KeyDelete(key); }
            catch (RedisException ex)
            {
                // Best-effort delete. Stale orphan keys expire after
                // Ttl anyway, so swallowing this is safe.
                _logger?.LogWarning(ex,
                    "[orphan-tracker] Redis DEL failed for {Key}; ignoring (TTL will collect)", key);
            }
        }
        _local.TryRemove(key, out _);
    }

    /// <summary>Composite key. Empty platformId is fine — single-Steam-
    /// emu setups still differentiate by deviceId alone. Redis keys
    /// are namespaced under <c>orphan:</c> so they don't collide with
    /// other tenants in the same instance.</summary>
    private static string MakeKey(string? deviceId, string? platformId) =>
        $"orphan:{deviceId ?? ""}|{platformId ?? ""}";
}
