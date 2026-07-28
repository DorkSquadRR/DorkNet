using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.Comments;

/// <summary>
/// Room comments ("feedback"/"idea" notes players leave in a room, shown by
/// the watch's RoomCommentsScreen / RoomCommentsPagedGrid).
///
/// Every route here is issued by RecNet.Runtime's room-comment service
/// <c>OPELMBNJHNO</c> against service host <c>12 = RoomComments</c>
/// (<see cref="Services.ConfigService"/> maps that to the <c>roomcomments</c>
/// subdomain). The five literals, with the <c>BNDIAONDFFF</c> ctor verb
/// ordinal moved into <c>rdx</c> immediately before the builder call
/// (BestHTTP.HTTPMethods: 0=GET, 2=POST, 3=PUT, 4=DELETE):
///
/// <list type="bullet">
/// <item><c>OPELMBNJHNO.txt:485</c> "comments/get/{0}" — rdx=0 (GET), host [rdx+12]</item>
/// <item><c>OPELMBNJHNO.txt:811</c> "comments/unreadcounts" — dynamic GET/POST</item>
/// <item><c>OPELMBNJHNO.txt:1216</c> "comments/create/{0}" — rdx=2 (POST), host [r12+12]</item>
/// <item><c>OPELMBNJHNO.txt:1627</c> "comments/read/{0}/{1}" — rdx=3 (PUT), host [rbx+12]</item>
/// <item><c>OPELMBNJHNO.txt:1935</c> "comments/delete/{0}" — rdx=4 (DELETE), r8=12</item>
/// </list>
///
/// STORAGE. There is no <c>RoomCommentEntity</c> and adding entities
/// needs a migration, so comments live in the <see cref="PlayerSettingEntity"/>
/// side-table exactly like the PlayerEvent extras
/// (<c>PlayerEventsController.EventExtras</c>): one row per comment with
/// <c>PlayerId</c> = the author, <c>Key = roomcomment:{roomId}:{commentId}</c>
/// and <c>Value</c> = the JSON payload. Per-player read state is a second,
/// disjoint namespace — <c>roomcommentread:{roomId}</c> holding the last-read
/// comment id — which is what finally lets
/// <see cref="UnreadCounts"/> report real numbers instead of zeroes.
/// </summary>
[ApiController]
[Authorize]
public class CommentsController(DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    /// <summary>Key prefix for a stored comment: <c>roomcomment:{roomId}:{commentId}</c>.</summary>
    private const string CommentPrefix = "roomcomment:";

    /// <summary>Key prefix for per-player read state: <c>roomcommentread:{roomId}</c>,
    /// value = last-read comment id. Deliberately a different prefix from
    /// <see cref="CommentPrefix"/> (not <c>roomcomment:read:…</c>) so the
    /// suffix match used by <see cref="Delete"/> can never hit a read-state row.</summary>
    private const string ReadPrefix = "roomcommentread:";

    /// <summary>The <c>RecNet.RoomComment</c> members that are not already
    /// implied by the storage row's key/owner. <c>Position</c> is three separate
    /// nullable floats on the wire, not a Vector3: the client's
    /// <c>Nullable&lt;Vector3&gt;</c> property is <c>[IgnoreDataMember]</c> and is
    /// rebuilt after deserialisation from three internal
    /// <c>Nullable&lt;float&gt;</c> fields (RecNet/RoomComment.txt:495
    /// <c>FKDDCNLJOLF</c> reads the HasValue byte at <c>[rbx+0x6C]</c>, i.e. the
    /// first of the three at 0x68/0x70/0x78).
    ///
    /// Public (not private) so System.Text.Json's reflection-based
    /// (de)serialiser can see the type and its accessors.</summary>
    public sealed class CommentPayload
    {
        public long? SubRoomId { get; set; }
        public string Message { get; set; } = string.Empty;
        /// <summary><c>RoomComment.EKHGDABMJOJ</c>: Feedback=0, Idea=1, BugReport=2
        /// (RecNet/RoomComment.cs enum member order).</summary>
        public int Style { get; set; }
        public float? X { get; set; }
        public float? Y { get; set; }
        public float? Z { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    // ── Reads ────────────────────────────────────────────────────────────

    /// <summary>Comment list for a room.
    ///
    /// <c>OPELMBNJHNO.GMBPJHCBPDP(Int64 roomId, Int32 count, Int64 minId)</c>
    /// returns <c>FGLDKEJLAKB&lt;List&lt;RecNet.RoomComment&gt;&gt;</c>
    /// (OPELMBNJHNO.txt:341), so the body is a bare JSON <b>array</b> — not a
    /// paged <c>{Results,TotalResults}</c> container. Verb is GET (rdx=0 at
    /// instr 044) and both parameters go on the query string, because
    /// <c>BNDIAONDFFF.AFGEDDANEKP</c> only promotes fields to a form body for
    /// non-GET verbs: literals "count" (:506) and "minId" (:518).
    ///
    /// <c>minId</c> is a lower bound on the comment id (the only caller,
    /// FeedbackManager.txt:948, passes count=100 and a carried-forward id), so
    /// it is applied inclusively and the newest <c>count</c> matches are
    /// returned newest-first — which is the order the paged grid renders.
    ///
    /// Failure toast if this 404s or the body will not parse:
    /// "Failed to get room comments for room: {0}" (:537).</summary>
    [HttpGet("/comments/get/{roomId:long}")]
    public async Task<IActionResult> Get(long roomId, [FromQuery] int count = 100, [FromQuery] long minId = 0)
    {
        if (count <= 0) count = 100;
        if (count > 500) count = 500;

        var rows = await CommentRowsForRoomAsync(roomId);

        var wire = rows
            .Where(r => r.CommentId >= minId)
            .OrderByDescending(r => r.CommentId)
            .Take(count)
            .Select(r => ToWire(r.CommentId, roomId, r.AuthorPlayerId, r.Payload, r.Unread))
            .ToList();

        return Ok(wire);
    }

    /// <summary>Room-comment unread counts. Client contract (RecNet.Runtime
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
    /// Counts are real now that <see cref="MarkRead"/> persists per-player read
    /// state: a room's count is the number of comments authored by somebody
    /// else whose id is above the caller's last-read id for that room.
    /// </summary>
    [HttpPost("/comments/unreadcounts")]
    [HttpGet("/comments/unreadcounts")]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> UnreadCounts()
    {
        var roomIds = await ReadRoomIdsAsync();
        if (roomIds.Count == 0) return Ok(new List<Dictionary<string, object>>());

        var me = Me;
        var lastRead = await LastReadAsync(me);

        // Key + owner only: the payload column is irrelevant to counting.
        var keys = await db.PlayerSettings
            .Where(s => s.Key.StartsWith(CommentPrefix))
            .Select(s => new { s.Key, s.PlayerId })
            .ToListAsync();

        var counts = new Dictionary<long, uint>();
        foreach (var id in roomIds) counts[id] = 0u;

        foreach (var k in keys)
        {
            if (!TrySplitCommentKey(k.Key, out var roomId, out var commentId)) continue;
            if (!counts.ContainsKey(roomId)) continue;
            if (k.PlayerId == me) continue;                       // your own notes are never unread
            if (commentId <= lastRead.GetValueOrDefault(roomId)) continue;
            counts[roomId]++;
        }

        var result = roomIds
            .Select(id => new Dictionary<string, object>
            {
                ["RoomId"]       = id,
                ["UnreadCount"]  = counts[id],
                ["Count"]        = counts[id],
            })
            .ToList();
        return Ok(result);
    }

    // ── Writes ───────────────────────────────────────────────────────────

    /// <summary>Posts a new comment into a room.
    ///
    /// <c>OPELMBNJHNO.JHABIKEMJHK(Int64 roomId, Int64 subRoomId, String message,
    /// RoomComment/EKHGDABMJOJ style, Nullable&lt;Vector3&gt; position)</c>
    /// (OPELMBNJHNO.txt:940) returns <c>FGLDKEJLAKB&lt;RecNet.RoomComment&gt;</c> —
    /// a <b>single</b> comment object, not a list and not a status envelope.
    ///
    /// Verb is POST (rdx=2 at instr 069), so every <c>AFGEDDANEKP</c> field is
    /// an <c>application/x-www-form-urlencoded</c> body field, never a query
    /// param. Field literals, in emission order: "message" (:1234),
    /// "subRoomId" (:1249, boxed Int64), "style" (:1261, boxed <b>UInt32</b>),
    /// then — only when the <c>Vector3?</c> has a value (the
    /// <c>Compare [rdi+12], r12 / JumpIfEqual</c> at :1268) — "positionX"
    /// (:1292), "positionY" (:1314), "positionZ" (:1336), each formatted with
    /// <c>Single.ToString(CultureInfo.InvariantCulture)</c> (:1285/1289), which
    /// is why the three floats are parsed invariantly here rather than left to
    /// model binding.
    ///
    /// Failure toast: "Failed to Create Comment" (:1359).</summary>
    [HttpPost("/comments/create/{roomId:long}")]
    public async Task<IActionResult> Create(
        long roomId,
        [FromForm] string? message,
        [FromForm] long? subRoomId,
        [FromForm] int style = 0,
        [FromForm] string? positionX = null,
        [FromForm] string? positionY = null,
        [FromForm] string? positionZ = null)
    {
        if (!await db.Rooms.AnyAsync(r => r.Id == roomId)) return NotFound();

        message = (message ?? string.Empty).Trim();
        if (message.Length == 0) return BadRequest();
        // PlayerSettingEntity.Value is MaxLength(1024) and also carries the
        // rest of the payload; keep well clear of the column width.
        if (message.Length > 700) message = message[..700];

        var payload = new CommentPayload
        {
            SubRoomId = subRoomId is > 0 ? subRoomId : null,
            Message   = message,
            Style     = style,
            X         = ParseFloat(positionX),
            Y         = ParseFloat(positionY),
            Z         = ParseFloat(positionZ),
            CreatedAt = DateTime.UtcNow,
        };
        // The client only ever sends all three or none; drop a partial set so
        // the client's Vector3 reassembly (RoomComment.FKDDCNLJOLF) sees a
        // consistent null.
        if (payload.X is null || payload.Y is null || payload.Z is null)
            payload.X = payload.Y = payload.Z = null;

        var me = Me;
        var commentId = await NextCommentIdAsync();

        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = me,
            Key      = $"{CommentPrefix}{roomId}:{commentId}",
            Value    = JsonSerializer.Serialize(payload),
        });
        await db.SaveChangesAsync();

        // Authoring a comment implies you have read up to it.
        await SetLastReadAsync(me, roomId, commentId);
        await db.SaveChangesAsync();

        return Ok(ToWire(commentId, roomId, me, payload, unread: false));
    }

    /// <summary>Records the newest comment the caller has read in a room, which
    /// is what clears the watch's unread badge (and what makes
    /// <see cref="UnreadCounts"/> return non-zero numbers for everyone else).
    ///
    /// <c>OPELMBNJHNO.MCOIDIBFHIP(Int64 roomId, Int64 commentId)</c>
    /// (OPELMBNJHNO.txt:1375) returns the status-only <c>LDGADANDBIO</c>, not a
    /// DTO — the body is never parsed, only the status code. Verb is <b>PUT</b>
    /// (rdx=3 at instr 059, OPELMBNJHNO.txt:1637); both ids are path segments
    /// formatted into "comments/read/{0}/{1}" (:1627) and no field is added, so
    /// the request carries no body at all.
    ///
    /// Failure toast: "Failed to update latest read comment" (:1656).</summary>
    [HttpPut("/comments/read/{roomId:long}/{commentId:long}")]
    public async Task<IActionResult> MarkRead(long roomId, long commentId)
    {
        await SetLastReadAsync(Me, roomId, commentId);
        await db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>Deletes a comment.
    ///
    /// <c>OPELMBNJHNO.NDLOEEHIDED(Int64 commentId)</c> (OPELMBNJHNO.txt:1781)
    /// returns the status-only <c>LDGADANDBIO</c>. Verb is <b>DELETE</b>
    /// (rdx=4 at instr 046) and the host ordinal is the only immediate on the
    /// builder call (r8=12, RoomComments) — see OPELMBNJHNO.txt:1944-1946.
    /// No fields are attached: the comment id is the whole request.
    ///
    /// Because the route carries no room id, the storage key is matched by its
    /// <c>:{commentId}</c> suffix; read-state rows live under a different
    /// prefix precisely so they cannot collide here. Only the author, the room
    /// creator, or an accepted co-owner/moderator of the room may delete —
    /// anyone else gets 403 and the client raises its "Failed to delete
    /// comment" toast (:1962).</summary>
    [HttpDelete("/comments/delete/{commentId:long}")]
    public async Task<IActionResult> Delete(long commentId)
    {
        var suffix = ":" + commentId.ToString(CultureInfo.InvariantCulture);
        var row = await db.PlayerSettings
            .Where(s => s.Key.StartsWith(CommentPrefix) && s.Key.EndsWith(suffix))
            .FirstOrDefaultAsync();
        if (row is null) return NotFound();
        if (!TrySplitCommentKey(row.Key, out var roomId, out _)) return NotFound();

        var me = Me;
        if (row.PlayerId != me && !await CanModerateRoomAsync(roomId, me)) return Forbid();

        db.PlayerSettings.Remove(row);
        await db.SaveChangesAsync();
        return Ok();
    }

    // ── Wire shape ───────────────────────────────────────────────────────

    /// <summary>One <c>RecNet.RoomComment</c>. Member list and CLR types are
    /// exact (RecNet/RoomComment.cs, cross-checked against the 2023.06.21
    /// dump.cs:1241576): <c>long</c> comment id, <c>long</c> room id,
    /// <c>long?</c> sub-room id, <c>int?</c> account id, <c>DateTime</c>
    /// created-at, <c>string</c> message, the <c>EKHGDABMJOJ</c> style enum, a
    /// settable <c>bool</c>, three internal <c>float?</c> position components,
    /// and one computed get-only <c>bool</c> that is never on the wire.
    ///
    /// KEY NAMES. The 2023 DTOs' JSON names live in DataMember attribute blobs
    /// in global-metadata.dat and are not recoverable from Cpp2IL output, so
    /// the names below are taken from the client's own <b>request</b> field
    /// literals for the same values — "message", "subRoomId", "style",
    /// "positionX", "positionY", "positionZ" (OPELMBNJHNO.txt:1234-1336) —
    /// which mirror the server DTO they were serialised from. Json.NET matches
    /// case-insensitively and ignores unknown members, so the aliases for the
    /// two members with no request-side counterpart are inert.
    ///
    /// The settable bool is the unread flag: its only mutator
    /// <c>RoomComment.MCCJIBCJEJC</c> clears the byte at <c>[rcx+0x64]</c> and
    /// fires the change event (RoomComment.txt:471-493), and the computed
    /// get-only bool <c>FOOFBPEMFCB</c> is <c>AccountId == localAccountId ||
    /// !flag</c> (:442-466, comparing against
    /// <c>OPMAPIOEIFG.GGAEMNHEHLO</c>). Its declared name is unknown, hence the
    /// three non-inverting spellings.</summary>
    private static Dictionary<string, object?> ToWire(
        long commentId, long roomId, long authorPlayerId, CommentPayload p, bool unread) => new(StringComparer.Ordinal)
    {
        ["CommentId"] = commentId,
        ["RoomId"]    = roomId,
        ["SubRoomId"] = p.SubRoomId,
        ["AccountId"] = (int)authorPlayerId,
        ["CreatedAt"] = p.CreatedAt,
        ["Message"]   = p.Message,
        ["Style"]     = p.Style,
        ["PositionX"] = p.X,
        ["PositionY"] = p.Y,
        ["PositionZ"] = p.Z,
        ["IsUnread"]  = unread,
        ["Unread"]    = unread,
        ["IsNew"]     = unread,
    };

    // ── Storage helpers ──────────────────────────────────────────────────

    private sealed record StoredComment(long CommentId, long AuthorPlayerId, CommentPayload Payload, bool Unread);

    /// <summary>Every stored comment for a room, with the caller's unread flag
    /// resolved against their read-state row.</summary>
    private async Task<List<StoredComment>> CommentRowsForRoomAsync(long roomId)
    {
        var me = Me;
        var prefix = $"{CommentPrefix}{roomId}:";
        var rows = await db.PlayerSettings
            .Where(s => s.Key.StartsWith(prefix))
            .Select(s => new { s.Key, s.PlayerId, s.Value })
            .ToListAsync();

        var lastRead = await LastReadAsync(me);
        var seen = lastRead.GetValueOrDefault(roomId);

        var list = new List<StoredComment>(rows.Count);
        foreach (var r in rows)
        {
            if (!long.TryParse(r.Key[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var commentId))
                continue;
            CommentPayload? payload;
            try { payload = JsonSerializer.Deserialize<CommentPayload>(r.Value); }
            catch (JsonException) { continue; }
            if (payload is null) continue;
            list.Add(new StoredComment(commentId, r.PlayerId, payload, r.PlayerId != me && commentId > seen));
        }
        return list;
    }

    /// <summary>Comment ids are handed out from the wall clock in milliseconds
    /// so they stay globally unique and monotonically increasing across
    /// restarts — the client's <c>minId</c> filter and the unread comparison
    /// both rely on "higher id == newer".</summary>
    private async Task<long> NextCommentIdAsync()
    {
        var id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var suffix = ":" + id.ToString(CultureInfo.InvariantCulture);
            var taken = await db.PlayerSettings
                .AnyAsync(s => s.Key.StartsWith(CommentPrefix) && s.Key.EndsWith(suffix));
            if (!taken) break;
            id++;
        }
        return id;
    }

    /// <summary>Last-read comment id per room for one player.</summary>
    private async Task<Dictionary<long, long>> LastReadAsync(long playerId)
    {
        var rows = await db.PlayerSettings
            .Where(s => s.PlayerId == playerId && s.Key.StartsWith(ReadPrefix))
            .Select(s => new { s.Key, s.Value })
            .ToListAsync();

        var map = new Dictionary<long, long>();
        foreach (var r in rows)
        {
            if (!long.TryParse(r.Key[ReadPrefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var roomId)) continue;
            if (!long.TryParse(r.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var commentId)) continue;
            if (!map.TryGetValue(roomId, out var cur) || commentId > cur) map[roomId] = commentId;
        }
        return map;
    }

    /// <summary>Read state only ever moves forward — the watch re-sends older
    /// ids as the grid scrolls back up.</summary>
    private async Task SetLastReadAsync(long playerId, long roomId, long commentId)
    {
        var key = $"{ReadPrefix}{roomId}";
        var row = await db.PlayerSettings.FirstOrDefaultAsync(s => s.PlayerId == playerId && s.Key == key);
        if (row is null)
        {
            db.PlayerSettings.Add(new PlayerSettingEntity
            {
                PlayerId = playerId,
                Key      = key,
                Value    = commentId.ToString(CultureInfo.InvariantCulture),
            });
            return;
        }
        if (long.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cur) && cur >= commentId) return;
        row.Value = commentId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Room creator, or an accepted co-owner (Role 0) / moderator
    /// (Role 1) — the same grant model
    /// <see cref="Data.Entities.RoomRoleEntity"/> documents.</summary>
    private async Task<bool> CanModerateRoomAsync(long roomId, long playerId)
    {
        if (await db.Rooms.AnyAsync(r => r.Id == roomId && r.CreatorPlayerId == playerId)) return true;
        return await db.RoomRoles.AnyAsync(r =>
            r.RoomId == roomId && r.PlayerId == playerId && r.Accepted && (r.Role == 0 || r.Role == 1));
    }

    /// <summary>Splits <c>roomcomment:{roomId}:{commentId}</c>.</summary>
    private static bool TrySplitCommentKey(string key, out long roomId, out long commentId)
    {
        roomId = 0;
        commentId = 0;
        if (!key.StartsWith(CommentPrefix, StringComparison.Ordinal)) return false;
        var rest = key[CommentPrefix.Length..];
        var sep = rest.IndexOf(':');
        if (sep <= 0) return false;
        return long.TryParse(rest[..sep], NumberStyles.Integer, CultureInfo.InvariantCulture, out roomId)
            && long.TryParse(rest[(sep + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out commentId);
    }

    private static float? ParseFloat(string? s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

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
