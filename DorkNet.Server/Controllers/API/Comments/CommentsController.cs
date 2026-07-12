using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Auth;

namespace DorkNet.Server.Controllers.API.Comments;

/// <summary>
/// Room-comment unread counts. Client contract (RecNet.Runtime
/// <c>DJLNMANKMAA(IReadOnlyCollection&lt;long&gt;)</c>): the caller sends a
/// collection of roomIds and expects a top-level JSON OBJECT
/// <c>Dictionary&lt;long, uint&gt;</c> mapping roomId → unread count.
/// We don't track per-player comment read-state yet, so every requested
/// room maps to 0 — a correct, non-crashing "nothing unread" answer that
/// matches the wire shape (an object, never an array).
/// </summary>
[ApiController]
[Authorize]
public class CommentsController : ControllerBase
{
    [HttpPost("/comments/unreadcounts")]
    [HttpGet("/comments/unreadcounts")]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> UnreadCounts()
    {
        var roomIds = await ReadRoomIdsAsync();
        // Keys must be strings in JSON; values are the (currently always 0)
        // unread counts.
        var result = new Dictionary<string, uint>();
        foreach (var id in roomIds) result[id.ToString()] = 0;
        return Ok(result);
    }

    private async Task<HashSet<long>> ReadRoomIdsAsync()
    {
        var ids = new HashSet<long>();

        void AddCsv(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (long.TryParse(part, out var v) && v > 0) ids.Add(v);
        }

        // Query / form: roomIds=1,2,3
        foreach (var k in new[] { "roomIds", "roomId", "ids", "id" })
        {
            AddCsv(Request.Query[k].ToString());
            if (Request.HasFormContentType) AddCsv(Request.Form[k].ToString());
        }

        // JSON body: either a bare array [1,2,3] or { "roomIds": [1,2,3] }.
        if (!Request.HasFormContentType)
        {
            try
            {
                Request.EnableBuffering();
                Request.Body.Position = 0;
                using var doc = await JsonDocument.ParseAsync(Request.Body);
                var root = doc.RootElement;
                JsonElement arr = default;
                var haveArr = false;
                if (root.ValueKind == JsonValueKind.Array) { arr = root; haveArr = true; }
                else if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var k in new[] { "roomIds", "RoomIds", "ids", "Ids" })
                        if (root.TryGetProperty(k, out arr) && arr.ValueKind == JsonValueKind.Array) { haveArr = true; break; }
                }
                if (haveArr)
                    foreach (var el in arr.EnumerateArray())
                        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var v) && v > 0) ids.Add(v);
            }
            catch { /* non-JSON / empty body */ }
        }
        return ids;
    }
}
