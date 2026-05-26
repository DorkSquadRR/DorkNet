using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using StackExchange.Redis;

namespace DorkNet.Server.Services;

/// <summary>
/// Rolling per-player request log used by the admin UI's "Player logs"
/// tab. The HTTP trace middleware in <c>Program.cs</c> appends one entry
/// per authenticated request; the admin endpoint reads the most-recent
/// N entries back for a single player so we can spot what API call a
/// player was making when they hit a bug.
///
/// Storage:
/// - When Redis is configured, entries live in a Redis LIST keyed by
///   <c>playerlog:{id}</c>. <c>LPUSH</c> + <c>LTRIM</c> keeps it at the
///   most recent <see cref="MaxEntriesPerPlayer"/> records, so memory
///   per player is bounded regardless of traffic.
/// - Without Redis, falls back to an in-process per-player ring buffer
///   capped to the same size; lost on restart and only visible on the
///   replica that handled the request, but fine for single-instance
///   dev workflows.
///
/// Entries are intentionally ephemeral — there's no Postgres mirror,
/// no long retention, no cross-replica fan-out beyond Redis. Treat
/// this as a "what just happened" diagnostic, not an audit trail.
/// </summary>
public class PlayerLogService
{
    /// <summary>Cap per player. Redis list takes ~250 bytes per entry
    /// with truncated bodies, so ~125 KB per player at 500 entries.
    /// At 5-second heartbeat cadence that's ~40 minutes of recent
    /// activity, plenty for "what was the player doing when X
    /// happened" diagnostics.</summary>
    public const int MaxEntriesPerPlayer = 500;

    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<PlayerLogService>? _log;
    // Process-local fallback when Redis isn't configured. Each player
    // gets a bounded queue we trim on append. ConcurrentQueue itself
    // has no built-in cap so we trim explicitly under a per-player lock.
    private readonly ConcurrentDictionary<long, ConcurrentQueue<PlayerLogEntry>> _local = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public PlayerLogService(
        IConnectionMultiplexer? redis = null,
        ILogger<PlayerLogService>? log = null)
    {
        _redis = redis;
        _log = log;
    }

    /// <summary>Append one entry for a player. Fire-and-forget — never
    /// throws so a Redis blip can't 500 a request that just succeeded.</summary>
    public void Record(PlayerLogEntry entry)
    {
        if (entry.PlayerId <= 0) return;
        if (_redis is { } mux)
        {
            try
            {
                var json = JsonSerializer.Serialize(entry, JsonOpts);
                var db = mux.GetDatabase();
                var key = Key(entry.PlayerId);
                // LPUSH + LTRIM 0..(N-1) keeps the list at most N long
                // with the newest entry at index 0. The two ops are
                // batched so a concurrent reader never sees an
                // intermediate over-long list.
                var batch = db.CreateBatch();
                _ = batch.ListLeftPushAsync(key, json);
                _ = batch.ListTrimAsync(key, 0, MaxEntriesPerPlayer - 1);
                // 7-day TTL so abandoned-account log keys age out.
                _ = batch.KeyExpireAsync(key, TimeSpan.FromDays(7));
                batch.Execute();
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "[playerlog] redis append failed for player={PlayerId}", entry.PlayerId);
            }
            return;
        }
        // Process-local fallback: append + trim under per-player lock.
        var queue = _local.GetOrAdd(entry.PlayerId, _ => new ConcurrentQueue<PlayerLogEntry>());
        queue.Enqueue(entry);
        while (queue.Count > MaxEntriesPerPlayer && queue.TryDequeue(out _)) { }
    }

    /// <summary>Most recent <paramref name="take"/> entries for a player,
    /// newest-first. Returns an empty list when the player has no
    /// recorded activity (or when Redis is down on the read path —
    /// admin UI shows "no entries" instead of erroring out).</summary>
    public IReadOnlyList<PlayerLogEntry> GetRecent(long playerId, int take)
    {
        take = Math.Clamp(take, 1, MaxEntriesPerPlayer);
        if (_redis is { } mux)
        {
            try
            {
                var raw = mux.GetDatabase().ListRange(Key(playerId), 0, take - 1);
                var result = new List<PlayerLogEntry>(raw.Length);
                foreach (var v in raw)
                {
                    var e = JsonSerializer.Deserialize<PlayerLogEntry>(v.ToString(), JsonOpts);
                    if (e is not null) result.Add(e);
                }
                return result;
            }
            catch (Exception ex)
            {
                _log?.LogWarning(ex, "[playerlog] redis read failed for player={PlayerId}", playerId);
                return Array.Empty<PlayerLogEntry>();
            }
        }
        if (!_local.TryGetValue(playerId, out var queue)) return Array.Empty<PlayerLogEntry>();
        // ConcurrentQueue enumerates oldest-first; reverse + take to
        // match Redis newest-first ordering.
        return queue.ToArray().Reverse().Take(take).ToList();
    }

    private static string Key(long playerId) => $"playerlog:{playerId}";
}

/// <summary>One captured request. Bodies are pre-truncated by the
/// caller so the JSON we serialise into Redis stays bounded.</summary>
public sealed class PlayerLogEntry
{
    [JsonPropertyName("ts")] public DateTime Timestamp { get; set; }
    [JsonPropertyName("playerId")] public long PlayerId { get; set; }
    [JsonPropertyName("method")] public string Method { get; set; } = string.Empty;
    [JsonPropertyName("host")] public string Host { get; set; } = string.Empty;
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("query")] public string Query { get; set; } = string.Empty;
    [JsonPropertyName("status")] public int Status { get; set; }
    [JsonPropertyName("elapsedMs")] public long ElapsedMs { get; set; }
    [JsonPropertyName("reqBody")] public string? ReqBody { get; set; }
    [JsonPropertyName("respBody")] public string? RespBody { get; set; }
}
