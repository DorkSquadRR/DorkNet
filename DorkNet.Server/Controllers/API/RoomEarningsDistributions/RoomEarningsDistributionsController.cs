using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.RoomEarningsDistributions;

/// <summary>
/// Co-owner earnings-split configuration for a room. NOT gross-earnings
/// totals — this is how a room's token earnings are divided among its
/// co-owners. Client contract (RecNet.Runtime HPEBLAHNBLC / FEJOMKJBOOE):
/// <list type="bullet">
///   <item>GET <c>api/roomEarningsDistributions/v1/earningsDistribution/{roomId}</c>
///   → a SINGLE object <c>{ RoomId, EarningsDistributionMapping:
///   Dictionary&lt;accountId,percent&gt;, EarningsDistributionMethod: 0|1 }</c>
///   (0 = Equal, 1 = Custom).</item>
///   <item>POST the bare <c>.../v1/earningsDistribution</c> with that object
///   to save.</item>
/// </list>
/// Persisted as JSON on the room creator's settings row, keyed per room.
/// </summary>
[ApiController]
[Authorize]
public class RoomEarningsDistributionsController(DorkNetDbContext db) : ControllerBase
{
    private const int MethodEqual = 0;
    private const int MethodCustom = 1;
    private static string SettingKey(long roomId) => $"room:earningsdist:{roomId}";

    /// <summary>GET the split config for one room (client's per-room GET).</summary>
    [HttpGet("api/roomEarningsDistributions/v1/earningsDistribution/{roomId:long}")]
    public async Task<IActionResult> GetDistribution(long roomId)
    {
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        var (method, mapping) = await LoadAsync(room.CreatorPlayerId, roomId, room.CreatorPlayerId);
        return Ok(BuildDto(roomId, method, mapping));
    }

    /// <summary>Bare-URL GET kept for compatibility: returns the caller's
    /// own rooms' split configs as a list. The 2023 client doesn't hit this
    /// (it GETs the per-room path above), but tooling may.</summary>
    [HttpGet("api/roomEarningsDistributions")]
    [HttpGet("api/roomEarningsDistributions/v1/earningsDistribution")]
    public async Task<IActionResult> ListMine([FromQuery] long? roomId = null)
    {
        var me = this.RequireCurrentPlayerId();
        var q = db.Rooms.Where(r => r.CreatorPlayerId == me);
        if (roomId is long rid && rid > 0) q = q.Where(r => r.Id == rid);
        var rooms = await q.Select(r => new { r.Id, r.CreatorPlayerId }).ToListAsync();

        var result = new List<object>();
        foreach (var r in rooms)
        {
            var (method, mapping) = await LoadAsync(me, r.Id, r.CreatorPlayerId);
            result.Add(BuildDto(r.Id, method, mapping));
        }
        return Ok(result);
    }

    /// <summary>POST the bare URL to save a room's split config (client's
    /// save method). Only the room creator may change it.</summary>
    [HttpPost("api/roomEarningsDistributions/v1/earningsDistribution")]
    [HttpPut("api/roomEarningsDistributions/v1/earningsDistribution")]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> SaveDistribution()
    {
        var me = this.RequireCurrentPlayerId();
        var (roomId, method, mapping) = await ReadBodyAsync();
        if (roomId <= 0) return BadRequest(new { error = "missing_room_id" });

        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();
        if (room.CreatorPlayerId != me) return Forbid();

        var payload = JsonSerializer.Serialize(new StoredDistribution
        {
            Method = method,
            Mapping = mapping,
        });
        var key = SettingKey(roomId);
        var row = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == room.CreatorPlayerId && s.Key == key);
        if (row is null)
            db.PlayerSettings.Add(new PlayerSettingEntity
            {
                PlayerId = room.CreatorPlayerId, Key = key, Value = payload,
            });
        else
            row.Value = payload;
        await db.SaveChangesAsync();

        return Ok(BuildDto(roomId, method, mapping));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private async Task<(int Method, Dictionary<long, int> Mapping)> LoadAsync(
        long ownerId, long roomId, long creatorId)
    {
        var row = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == ownerId && s.Key == SettingKey(roomId));
        if (row is not null)
        {
            try
            {
                var stored = JsonSerializer.Deserialize<StoredDistribution>(row.Value);
                if (stored is not null)
                    return (stored.Method, stored.Mapping ?? new Dictionary<long, int>());
            }
            catch { /* fall through to default */ }
        }
        // Default: Equal method, creator takes 100% (solo room).
        return (MethodEqual, new Dictionary<long, int> { [creatorId] = 100 });
    }

    private static object BuildDto(long roomId, int method, Dictionary<long, int> mapping)
    {
        // Mapping is keyed by accountId (as a string in JSON) → percent.
        var mappingWire = mapping.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value);
        // A Dictionary preserves literal key casing (unlike anonymous objects,
        // which the server serializes camelCase). The client dump carries BOTH
        // "EarningsDistributionMapping"/"earningsDistributionMapping" (and the
        // Method variants), so emit both cases to be safe.
        return new Dictionary<string, object?>
        {
            ["RoomId"] = roomId,
            ["roomId"] = roomId,
            ["EarningsDistributionMethod"] = method,
            ["earningsDistributionMethod"] = method,
            ["EarningsDistributionMapping"] = mappingWire,
            ["earningsDistributionMapping"] = mappingWire,
        };
    }

    private async Task<(long RoomId, int Method, Dictionary<long, int> Mapping)> ReadBodyAsync()
    {
        long roomId = 0;
        int method = MethodEqual;
        var mapping = new Dictionary<long, int>();

        if (Request.HasFormContentType)
        {
            if (long.TryParse(Request.Form["RoomId"], out var rf) ||
                long.TryParse(Request.Form["roomId"], out rf)) roomId = rf;
            if (int.TryParse(Request.Form["EarningsDistributionMethod"], out var mf) ||
                int.TryParse(Request.Form["earningsDistributionMethod"], out mf)) method = mf;
        }
        else
        {
            try
            {
                Request.EnableBuffering();
                Request.Body.Position = 0;
                using var doc = await JsonDocument.ParseAsync(Request.Body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    roomId = ReadLong(root, "RoomId", "roomId") ?? 0;
                    method = (int)(ReadLong(root, "EarningsDistributionMethod", "earningsDistributionMethod") ?? MethodEqual);
                    if (TryGetProp(root, out var map, "EarningsDistributionMapping", "earningsDistributionMapping")
                        && map.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in map.EnumerateObject())
                            if (long.TryParse(p.Name, out var acc) && p.Value.TryGetInt32(out var pct))
                                mapping[acc] = pct;
                    }
                }
            }
            catch { /* non-JSON / empty */ }
        }

        if (roomId == 0 && long.TryParse(Request.Query["roomId"], out var rq)) roomId = rq;
        if (mapping.Count == 0 && method == MethodCustom) method = MethodEqual;
        return (roomId, method, mapping);
    }

    private static long? ReadLong(JsonElement root, params string[] names)
    {
        if (TryGetProp(root, out var v, names) &&
            v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        return null;
    }

    private static bool TryGetProp(JsonElement root, out JsonElement value, params string[] names)
    {
        foreach (var n in names)
            if (root.TryGetProperty(n, out value)) return true;
        value = default;
        return false;
    }

    private sealed class StoredDistribution
    {
        public int Method { get; set; }
        public Dictionary<long, int>? Mapping { get; set; }
    }
}
