using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Auth;

namespace DorkNet.Server.Controllers.API.Comments;

/// <summary>
/// Room-comment unread counts. Client contract (RecNet.Runtime
/// <c>OPELMBNJHNO.DJLNMANKMAA(IReadOnlyCollection&lt;long&gt;)</c>).
///
/// The response is a JSON <b>ARRAY</b>, not an object. The method's return
/// type is <c>Dictionary&lt;long,uint&gt;</c>, but that dictionary is built
/// client-side: OPELMBNJHNO.txt:889 constructs a
/// <c>Func&lt;List&lt;UnreadRoomComments&gt;, Dictionary&lt;long,uint&gt;&gt;</c>
/// projection that runs over the deserialised payload. So the wire type is
/// <c>List&lt;RecNet.UnreadRoomComments&gt;</c> — each element an object with a
/// room id (Int64) and a count (UInt32), per the two properties on
/// RecNet/UnreadRoomComments.txt.
///
/// This previously returned an object map keyed by room id. Json.NET cannot
/// read a JSON object into a <c>List&lt;T&gt;</c>, so every call threw and the
/// client logged "Failed to get unread room comment counts"
/// (OPELMBNJHNO.txt:937) and fell back to an empty dictionary — the watch's
/// room-comment unread badge never appeared.
///
/// The element's exact property names are attribute-driven and are not
/// recoverable from the ISIL (see the note on key aliasing below), so each
/// element carries the plausible spellings side by side. Json.NET matches
/// case-insensitively and ignores unknown members, so the extra aliases are
/// inert whichever name the DTO actually declares.
///
/// Counts are all zero until per-player comment read-state is tracked; the
/// shape is what matters for the client not to throw.
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
        var result = roomIds
            .Select(id => new Dictionary<string, object>
            {
                ["RoomId"]       = id,
                ["UnreadCount"]  = 0u,
                ["Count"]        = 0u,
            })
            .ToList();
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
