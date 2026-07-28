using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Inventions;

/// <summary>
/// api.rec.net/api/inventions/* — Maker-Pen creations the player can
/// re-spawn or share. URL surface + verbs verified against
/// <c>Cpp2IL_ISIL/.../RecNet/Inventions.txt</c>: the "update*"
/// routes use <c>Core.Get</c> (GET) despite being mutations, so we
/// expose them as [HttpGet]; only <c>cheer</c>, <c>report</c>,
/// <c>settags</c>, <c>save</c>, <c>addversion</c>, and <c>batch</c>
/// are POST. <c>delete</c>, <c>publish</c>, <c>unpublish</c>,
/// <c>download</c> are also GET.
///
/// Wire DTO matches the watch's <c>OBBBPCBIMME.PPGFHEDFBEA</c>
/// deserializer (Cpp2IL ISIL <c>OBBBPCBIMME.txt:588-704</c>): keys
/// <c>InventionId, ReplicationId, CreatorPlayerId, Name, Description,
/// ImageName, CurrentVersionNumber, IsPublished, AllowTrial,
/// ModifiedAt, CreatedAt, FirstPublishedAt, CreationRoomId,
/// NumPlayersHaveUsedInRoom, NumDownloads, CheerCount,
/// CreatorPermission, GeneralPermission, IsAGInvention, Price,
/// HideFromPlayer</c>. Casing is significant — note IsAGInvention
/// has a capital G.
/// </summary>
[ApiController]
public class InventionsController(
    DorkNetDbContext db,
    LevelService level,
    NotificationService notifications) : ControllerBase
{
    private long? CurrentPlayerIdOrNull => this.CurrentPlayerId();
    private long CurrentPlayerId => this.RequireCurrentPlayerId();

    // ── Browse ───────────────────────────────────────────────────────────

    [HttpGet("api/inventions/v1/featured")]
    [HttpGet("api/inventions/v3/popular")]
    public async Task<ActionResult> Popular([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        var rows = await db.Inventions
            .Where(i => !i.IsDeleted && i.IsPublished)
            .OrderByDescending(i => i.CheerCount)
            .Take(take)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    /// <summary>GET <c>api/inventions/v1/toptoday</c> — the client fires
    /// this as its own no-arg shelf query
    /// (OEGFNFEAAGO_NestedType___c_NestedType___GetTopInventionsToday_b__43_0_d.txt:147,
    /// verb 0) and shows it NEXT TO the featured shelf, so aliasing it onto
    /// the all-time popular query rendered the same twelve tiles twice.
    /// Ranked by cheers received in the last 24h, then backfilled with
    /// all-time popular so a quiet day still fills the shelf.</summary>
    [HttpGet("api/inventions/v1/toptoday")]
    public async Task<ActionResult> TopToday([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        var since = DateTime.UtcNow.AddDays(-1);
        var hot = await db.Cheers
            .Where(c => c.TargetInventionId > 0 && c.CheeredAt >= since)
            .GroupBy(c => c.TargetInventionId)
            .Select(g => new { InventionId = g.Key, Score = g.Count() })
            .OrderByDescending(x => x.Score)
            .Take(take)
            .ToListAsync();

        var order = hot.Select(h => h.InventionId).ToList();
        var hits = order.Count == 0
            ? new List<InventionEntity>()
            : await db.Inventions
                .Where(i => !i.IsDeleted && i.IsPublished && order.Contains(i.Id))
                .ToListAsync();

        var ranked = order
            .Select(id => hits.FirstOrDefault(r => r.Id == id))
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        if (ranked.Count < take)
        {
            var have = ranked.Select(r => r.Id).ToList();
            var filler = await db.Inventions
                .Where(i => !i.IsDeleted && i.IsPublished && !have.Contains(i.Id))
                .OrderByDescending(i => i.CheerCount)
                .Take(take - ranked.Count)
                .ToListAsync();
            ranked.AddRange(filler);
        }
        return Ok(ranked.Select(ToWire));
    }

    /// <summary>GET <c>api/inventions/v1/featureddormskins</c> — separate
    /// shelf in the client
    /// (OEGFNFEAAGO_NestedType___c_NestedType___GetFeaturedDormSkins_b__45_0_d.txt:147,
    /// verb 0, no params) which previously returned generic popular
    /// inventions. <see cref="InventionEntity"/> has no dorm-skin column, so
    /// the marker lives in <see cref="InventionEntity.TagsCsv"/> — see
    /// <see cref="IsDormSkinTags"/>. The filter runs in memory because
    /// TagsCsv is a packed list no provider can split.</summary>
    [HttpGet("api/inventions/v1/featureddormskins")]
    public async Task<ActionResult> FeaturedDormSkins([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        var rows = await db.Inventions
            .Where(i => !i.IsDeleted && i.IsPublished && i.TagsCsv != "")
            .OrderByDescending(i => i.CheerCount)
            .Take(500)
            .ToListAsync();
        return Ok(rows.Where(i => IsDormSkinTags(i.TagsCsv)).Take(take).Select(ToWire));
    }

    /// <summary>Dorm-skin marker. There is no IsDormSkin column on
    /// <see cref="InventionEntity"/>, so a tag decides: any tag that reads
    /// "dormskin" / "dorm skin" (case- and space-insensitive).</summary>
    private static bool IsDormSkinTags(string? tagsCsv) =>
        !string.IsNullOrEmpty(tagsCsv)
        && tagsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(t => t.Replace(" ", string.Empty)
                       .Equals("dormskin", StringComparison.OrdinalIgnoreCase));

    [HttpGet("api/inventions/v3/saved")]
    [Authorize]
    public async Task<ActionResult> Saved([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        var pid = CurrentPlayerId;
        var rows = await db.Inventions
            .Where(i => !i.IsDeleted && i.CreatorPlayerId == pid)
            .OrderByDescending(i => i.UpdatedAt)
            .Take(take)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("api/inventions/v3/created")]
    [Authorize]
    public Task<ActionResult> Created([FromQuery] int take = 50) => Saved(take);

    /// <summary><c>GetLocalPlayerInventions</c> — same as Saved but
    /// at v1 path. Watch hits both depending on tab.</summary>
    [HttpGet("api/inventions/v1/mine")]
    [HttpGet("api/inventions/v2/mine")]
    [Authorize]
    public Task<ActionResult> Mine() => Saved(100);

    /// <summary>The 2023 client sends value + skip + take on every store
    /// search (OEGFNFEAAGO.txt:10805/10820/10832 — three AFGEDDANEKP pairs
    /// on a verb-0 request). Ignoring skip/take pinned every page to the
    /// first 50 rows, so scrolling the store re-showed the same tiles.</summary>
    [HttpGet("api/inventions/v1/search")]
    [HttpGet("api/inventions/v2/search")]
    public async Task<ActionResult> Search(
        [FromQuery] string value = "",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);

        if (string.IsNullOrWhiteSpace(value))
            return Ok(Array.Empty<object>());

        // The watch's @-prefix means "search by player". Two callers
        // use this with different payload shapes — both end up here:
        //   * PlayerDetailsWatchUIFlow.SetAccount → @<accountId> (numeric)
        //     loads the Inventions tab on a player profile. Verified
        //     in Cpp2IL — see PlayerDetailsWatchUIFlow.txt:1867,1950.
        //   * Inventions search UI → @<username> (text) free-text
        //     prefix match on Username.
        if (value.StartsWith('@'))
        {
            var tail = value[1..];
            List<long> creators;
            if (long.TryParse(tail, out var accountId))
            {
                creators = new List<long> { accountId };
            }
            else
            {
                creators = await db.Players
                    .Where(p => p.Username.StartsWith(tail))
                    .Select(p => p.Id).Take(50).ToListAsync();
            }
            var byCreator = await db.Inventions
                .Where(i => !i.IsDeleted && i.IsPublished
                            && creators.Contains(i.CreatorPlayerId))
                .OrderByDescending(i => i.CheerCount)
                .Skip(skip)
                .Take(take)
                .ToListAsync();
            return Ok(byCreator.Select(ToWire));
        }

        var v = value.ToLowerInvariant();
        var rows = await db.Inventions
            .Where(i => !i.IsDeleted && i.IsPublished
                        && (i.Name.ToLower().Contains(v)
                            || i.TagsCsv.ToLower().Contains(v)))
            .OrderByDescending(i => i.CheerCount)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    // ── Id-list endpoints (runtime GET/POST) ─────────────────────────────
    //
    // The 2023 client picks the verb at RUNTIME for every id-list route:
    // ALHIJCJOLCB.JIECAFGCODK (IsilDump/RecNet.Runtime/ALHIJCJOLCB.txt:3198)
    // is `count < limit ? GET : POST` with limit=100. On a non-GET the
    // key/value pairs move out of the query string and into an
    // HTTPUrlEncodedForm body — BNDIAONDFFF.txt:2971-3010 branches on the
    // verb (GET → "?a=b&…", otherwise → HTTPUrlEncodedForm). So the POST
    // actions must NOT declare a complex parameter, or [ApiController]
    // infers [FromBody] and answers 415 before the handler runs; we read
    // query + form by hand instead.
    //
    // Field names are literal:
    //   "id"  → v2/batch      (OEGFNFEAAGO_NestedType_LANMKGKDNDC.txt:228)
    //   "id"  → fromcreators  (…CDILMPCKMEO…GetInventionsByCreators_b__0_d.txt:330)
    //   "ids" → dormskinsfromids (OEGFNFEAAGO.txt:5913)

    /// <summary>Body shape for non-2023 callers, which still send a JSON
    /// <c>{"InventionIds":[…]}</c> envelope rather than repeated fields.</summary>
    private sealed class InventionIdsBody
    {
        public List<long>? InventionIds { get; set; }
        public List<long>? Ids { get; set; }
        public List<long>? Id { get; set; }
    }

    private static readonly System.Text.Json.JsonSerializerOptions IdsJson =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>Gather ids from the query string, the url-encoded form and
    /// (as a fallback) a JSON id envelope. See the block comment above for
    /// why all three have to be accepted on one route.</summary>
    private async Task<List<long>> CollectIdsAsync(params string[] keys)
    {
        var ids = new List<long>();

        void Take(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var part in raw.Split(
                         ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (long.TryParse(part, out var id) && id > 0) ids.Add(id);
            }
        }

        var form = Request.HasFormContentType ? await Request.ReadFormAsync() : null;
        foreach (var key in keys)
        {
            foreach (var v in Request.Query[key]) Take(v);
            if (form is not null)
                foreach (var v in form[key]) Take(v);
        }

        if (ids.Count == 0 && form is null
            && (Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            try
            {
                var body = await System.Text.Json.JsonSerializer
                    .DeserializeAsync<InventionIdsBody>(Request.Body, IdsJson);
                foreach (var list in new[] { body?.InventionIds, body?.Ids, body?.Id })
                {
                    if (list is not null) ids.AddRange(list.Where(id => id > 0));
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Not an id envelope — treat as "no ids" rather than 400.
            }
        }

        return ids.Distinct().Take(200).ToList();
    }

    /// <summary>Read one scalar field from the query string, falling back to
    /// the url-encoded form body (POST variants — see the block comment
    /// above).</summary>
    private async Task<string?> ReadFieldAsync(string key)
    {
        var fromQuery = Request.Query[key].FirstOrDefault();
        if (!string.IsNullOrEmpty(fromQuery)) return fromQuery;
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            var fromForm = form[key].FirstOrDefault();
            if (!string.IsNullOrEmpty(fromForm)) return fromForm;
        }
        return null;
    }

    private async Task<int?> ReadIntFieldAsync(string key)
        => int.TryParse(await ReadFieldAsync(key), out var v) ? v : null;

    private async Task<long?> ReadLongFieldAsync(string key)
        => long.TryParse(await ReadFieldAsync(key), out var v) ? v : null;

    [HttpGet("api/inventions/v1/batch")]
    [HttpGet("api/inventions/v2/batch")]
    [HttpPost("api/inventions/v1/batch")]
    [HttpPost("api/inventions/v2/batch")]
    public async Task<ActionResult> Batch()
        => await BatchIdsAsync(await CollectIdsAsync("id", "ids", "InventionIds"));

    /// <summary><c>api/inventions/v1/dormskinsfromids</c> — the client
    /// deserialises a BARE <c>List&lt;Int64&gt;</c> here, not a list of
    /// invention objects: the continuation at OEGFNFEAAGO.txt:5941 is typed
    /// <c>FGLDKEJLAKB&lt;List`1&lt;Int64&gt;&gt;</c> and is then projected by a
    /// <c>Func&lt;List&lt;Int64&gt;, List&lt;Int64&gt;&gt;</c> (:5966). The reply is the
    /// SUBSET of the posted ids that are dorm skins; aliasing this onto the
    /// generic batch handler fed objects to a number reader and killed
    /// dorm-skin filtering outright.</summary>
    [HttpGet("api/inventions/v1/dormskinsfromids")]
    [HttpPost("api/inventions/v1/dormskinsfromids")]
    public async Task<ActionResult> DormSkinsFromIds()
    {
        var ids = await CollectIdsAsync("ids", "id", "InventionIds");
        if (ids.Count == 0) return Ok(Array.Empty<long>());
        var rows = await db.Inventions
            .Where(i => !i.IsDeleted && ids.Contains(i.Id))
            .Select(i => new { i.Id, i.TagsCsv })
            .ToListAsync();
        return Ok(rows.Where(r => IsDormSkinTags(r.TagsCsv)).Select(r => r.Id).ToList());
    }

    private async Task<ActionResult> BatchIdsAsync(IReadOnlyCollection<long> requestedIds)
    {
        if (requestedIds.Count == 0)
            return Ok(Array.Empty<object>());
        var ids = requestedIds.Take(200).ToList();
        var rows = await db.Inventions
            .Where(i => !i.IsDeleted && ids.Contains(i.Id))
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("api/inventions/v1/fromcreators")]
    [HttpPost("api/inventions/v1/fromcreators")]
    public async Task<ActionResult> FromCreators()
    {
        // Only the literal "id" key carries creator ids. The previous
        // SelectMany-over-every-query-field also swallowed the client's
        // "skip"/"take" pairs, so take=100 quietly added player 100's
        // inventions to the shelf.
        var creatorIds = await CollectIdsAsync("id");
        if (creatorIds.Count == 0) return Ok(Array.Empty<object>());

        var skip = Math.Max(0, await ReadIntFieldAsync("skip") ?? 0);
        var take = Math.Clamp(await ReadIntFieldAsync("take") ?? 100, 1, 200);

        var rows = await db.Inventions
            .Where(i => !i.IsDeleted && i.IsPublished && creatorIds.Contains(i.CreatorPlayerId))
            .OrderByDescending(i => i.UpdatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    /// <summary>GET <c>api/inventions/v1/tagfilters</c> — drives
    /// <c>RecNet.Inventions.GetTags</c>. The watch deserialises the
    /// response as <c>RecNet.GetFiltersResponse</c> (an
    /// <c>IRecNetObject</c> with <c>PinnedFilters</c> +
    /// <c>PopularFilters</c> string-list properties), then a
    /// <c>Func&lt;GetFiltersResponse, List&lt;string&gt;&gt;</c>
    /// projects whichever list the caller wanted. Verified at
    /// Cpp2IL_ISIL/.../RecNet/Inventions.txt:4834-4852 — the URL
    /// literal is followed by a <c>typeof(Func`2&lt;GetFiltersResponse,
    /// List`1&lt;String&gt;&gt;)</c>. Returning a bare string array
    /// (the previous shape) tripped the LitJson importer's
    /// <c>List → Dictionary</c> cast inside <c>Util.Deserialize&lt;T&gt;</c>
    /// and surfaced as "Failed to get tags: Malformed Response"
    /// (output_log.txt:1602+1639) — and that bubbles up as the
    /// dorm-load destabilisation we've been hunting.
    ///
    /// The 2023 DTO reads a THIRD list, <c>TrendingFilters</c>
    /// (HMDONHHKKGA.txt:099/169), which we never sent — callers projecting that
    /// list got an empty filter row. Popular/Trending are now derived from the
    /// tags actually in use (<see cref="InventionEntity.TagsCsv"/>) rather than
    /// echoing the pinned array three times; the curated list is the fallback
    /// so a fresh server still shows filters. The 2020.12 watch's
    /// <c>AEBEPCMAABC</c> reads only Pinned/Popular, so the extra key is
    /// inert there.</summary>
    [HttpGet("api/inventions/v1/tagfilters")]
    public async Task<IActionResult> TagFilters()
    {
        string[] pinned =
        [
            "sport", "game", "vehicle", "weapon", "decor", "tool",
            "art", "music", "puzzle", "combat", "build", "race",
        ];

        // TagsCsv is a packed list no provider can split, so the frequency count
        // runs in memory over a bounded window of the most recently touched
        // tagged inventions.
        var rows = await db.Inventions
            .Where(i => !i.IsDeleted && i.IsPublished && i.TagsCsv != "")
            .OrderByDescending(i => i.UpdatedAt)
            .Select(i => new { i.TagsCsv, i.UpdatedAt })
            .Take(2000)
            .ToListAsync();

        static IEnumerable<string> SplitTags(string csv) => csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        static List<string> Rank(IEnumerable<string> tags) => tags
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key)
            .Take(12)
            .ToList();

        var since = DateTime.UtcNow.AddDays(-7);
        var popular = Rank(rows.SelectMany(r => SplitTags(r.TagsCsv)));
        var trending = Rank(rows.Where(r => r.UpdatedAt >= since)
            .SelectMany(r => SplitTags(r.TagsCsv)));

        if (popular.Count == 0) popular = [.. pinned];
        if (trending.Count == 0) trending = popular;

        return Ok(new
        {
            PinnedFilters = pinned,
            PopularFilters = popular,
            TrendingFilters = trending,
        });
    }

    /// <summary>GET <c>api/inventions/v1/creatorIds</c> — the distinct
    /// creator account ids of every invention placed in a room
    /// (<c>GetCreatorIdsOfInventionsInRoom</c>). The watch uses this to
    /// resolve which creators it must request invention permissions for
    /// before spawning a room's invention-based geometry. Returns
    /// <c>List&lt;int&gt;</c> — empty when the room has no tracked
    /// inventions.</summary>
    [HttpGet("api/inventions/v1/creatorIds")]
    public async Task<IActionResult> CreatorIdsInRoom([FromQuery] long roomId)
    {
        var inventions = await ResolveRoomInventionsAsync(roomId);
        var creatorIds = inventions
            .Select(i => (int)i.CreatorPlayerId)
            .Distinct()
            .ToList();
        return Ok(creatorIds);
    }

    /// <summary>Resolve the inventions that belong to a room. Direct
    /// matches are inventions whose
    /// <see cref="InventionEntity.CreationRoomId"/> equals the room's
    /// local id (RR-Originals, dorms, anything created in-place).
    /// Zip-imported rooms instead carry each invention's ORIGINAL Rec
    /// Room CreationRoomId, which won't equal our local id and can't be
    /// reverse-resolved without an OriginalRoomId column; we approximate
    /// that cluster as the most-frequent non-local CreationRoomId among
    /// the room creator's inventions. The two sets are unioned so a room
    /// with both kinds resolves fully.</summary>
    private async Task<List<InventionEntity>> ResolveRoomInventionsAsync(long roomId)
    {
        var creatorId = await db.Rooms
            .Where(r => r.Id == roomId)
            .Select(r => (long?)r.CreatorPlayerId)
            .FirstOrDefaultAsync();

        long? importedRoomId = null;
        if (creatorId is long creator)
        {
            importedRoomId = await db.Inventions
                .Where(i => !i.IsDeleted && i.CreatorPlayerId == creator
                    && i.CreationRoomId != null && i.CreationRoomId != roomId)
                .GroupBy(i => i.CreationRoomId)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefaultAsync();
        }

        return await db.Inventions
            .Where(i => !i.IsDeleted
                && (i.CreationRoomId == roomId
                    || (importedRoomId != null && i.CreationRoomId == importedRoomId)))
            .ToListAsync();
    }

    /// <summary>GET <c>api/inventions/v1/fulllineageowner</c> —
    /// raw Boolean. The client uses this to decide whether a set of
    /// referenced inventions can be saved together under one owner.
    /// DorkNet tracks current creator ownership, so the concrete rule
    /// here is: every requested invention id must exist and share the
    /// same CreatorPlayerId.</summary>
    [HttpGet("api/inventions/v1/fulllineageowner")]
    public async Task<IActionResult> FullLineageOwner()
    {
        var ids = Request.Query
            .SelectMany(q => q.Value)
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => long.TryParse(v, out var id) ? id : 0L)
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToList();

        if (ids.Count == 0) return Content("true", "application/json");

        var owners = await db.Inventions
            .Where(i => !i.IsDeleted && ids.Contains(i.Id))
            .Select(i => new { i.Id, i.CreatorPlayerId })
            .ToListAsync();
        var ok = owners.Count == ids.Count
                 && owners.Select(o => o.CreatorPlayerId).Distinct().Count() <= 1;
        return Content(ok ? "true" : "false", "application/json");
    }

    // ── Single fetch ─────────────────────────────────────────────────────

    /// <summary>GET <c>/api/inventions/v1/room?id={roomId}</c> — every
    /// invention used by the room. The 2020.12 watch fires this on
    /// EVERY room load right after fetching the room data blob; it uses
    /// the response to pre-resolve invention references the blob will
    /// spawn. Without this, imported rooms with invention-based
    /// geometry (floors, walls, custom shapes) render with everything
    /// missing — the watch silently skips invention spawns when it
    /// has no list, and you fall through the void.
    ///
    /// We match on <see cref="InventionEntity.CreationRoomId"/> which
    /// the zip importer populates from each Invention.json's
    /// <c>CreationRoomId</c> field. Returns an array of
    /// <see cref="ToWire"/>-shaped Invention objects (same wire shape
    /// the single-fetch endpoint returns) so the watch's existing
    /// deserializer works.</summary>
    [HttpGet("api/inventions/v1/room")]
    public async Task<ActionResult> ByRoom([FromQuery] long id)
    {
        var inventions = await ResolveRoomInventionsAsync(id);
        return Ok(inventions.Select(ToWire).ToList());
    }

    [HttpGet("api/inventions/v1")]
    public async Task<ActionResult> SingleByQuery([FromQuery] long inventionId)
    {
        var i = await db.Inventions.FirstOrDefaultAsync(x => x.Id == inventionId && !x.IsDeleted);
        if (i is null) return NotFound();
        if (!i.IsPublished && i.CreatorPlayerId != CurrentPlayerIdOrNull)
            return Forbid();
        return Ok(ToWire(i));
    }

    /// <summary>POST <c>api/storefronts/v1/trialInvention</c> — start a free
    /// trial of a store invention, spawned for the window returned by
    /// <c>trialInvention/duration</c>. Hosted here — not in
    /// StorefrontsController — so it can reuse the invention wire builder.
    /// We record the trial start so it's an auditable action.
    ///
    /// TWO wire corrections. (1) The id is a FORM field, not a query param:
    /// DCFKEFHJAGC.txt:9037 sets verb 2 (POST) and :9050 adds the pair via
    /// <c>AFGEDDANEKP("inventionId", …)</c>, and on a non-GET those pairs go
    /// into an HTTPUrlEncodedForm body (see the id-list block comment above), so
    /// <c>[FromQuery]</c> bound 0 and every trial 404'd. (2) The reply is the
    /// Status/Invention/InventionVersion envelope: the continuation at
    /// DCFKEFHJAGC.txt:9086 is a
    /// <c>Func&lt;BDNCJIPHHOK, FGLDKEJLAKB&lt;IFJONDCAKKM&gt;&gt;</c> which
    /// projects <c>.Invention</c> — a bare invention left that null.</summary>
    [HttpPost("api/storefronts/v1/trialInvention")]
    [Authorize]
    public async Task<ActionResult> TrialInvention()
    {
        var inventionId = await ReadLongFieldAsync("inventionId") ?? 0;
        var i = await db.Inventions.FirstOrDefaultAsync(x => x.Id == inventionId && !x.IsDeleted);
        if (i is null) return NotFound();
        if (!i.IsPublished && i.CreatorPlayerId != CurrentPlayerIdOrNull)
            return Forbid();

        if (CurrentPlayerIdOrNull is long me)
        {
            var key = $"invention:trial:{inventionId}";
            var row = await db.PlayerSettings.FirstOrDefaultAsync(s => s.PlayerId == me && s.Key == key);
            if (row is null)
                db.PlayerSettings.Add(new Data.Entities.PlayerSettingEntity
                {
                    PlayerId = me, Key = key, Value = DateTime.UtcNow.ToString("O"),
                });
            else
                row.Value = DateTime.UtcNow.ToString("O");
            await db.SaveChangesAsync();
        }
        return Ok(await EnvelopeAsync(i));
    }

    /// <summary>GET <c>api/inventions/v1/details</c> — the ONLY key either
    /// client reads off this response is <c>Tags</c>: the 2023 continuation is
    /// <c>Action&lt;OIABGAKJABE&gt;</c> (OEGFNFEAAGO.txt:6312) whose reader takes
    /// exactly one literal, "Tags" (DBAHPLOPFIO.txt:034), and the 2020.12 watch's
    /// HJPDBNLCGIB does the same. Each element is
    /// <c>{Tag:String, Type:Int32}</c> (PCMLHLIBLNJ.txt:038/057). Without the key
    /// the invention detail page's tag row was permanently empty. Invention +
    /// Versions stay on the wire — unknown members are ignored by both readers.
    /// <c>Type</c> is always 0: <see cref="SetTags"/> merges the client's
    /// AutoTags and CustomTags into one <see cref="InventionEntity.TagsCsv"/>
    /// column, so the auto/custom distinction is not recoverable without a
    /// second column.</summary>
    [HttpGet("api/inventions/v1/details")]
    public async Task<ActionResult> Details([FromQuery] long inventionId)
    {
        var i = await db.Inventions.FirstOrDefaultAsync(x => x.Id == inventionId && !x.IsDeleted);
        if (i is null) return NotFound();
        if (!i.IsPublished && i.CreatorPlayerId != CurrentPlayerIdOrNull)
            return Forbid();
        var versions = await db.InventionVersions
            .Where(v => v.InventionId == inventionId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync();
        return Ok(new
        {
            Tags = i.TagsCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => new { Tag = t, Type = 0 }),
            Invention = ToWire(i),
            Versions = versions.Select(ToVersionWire),
        });
    }

    /// <summary>GET <c>api/inventions/v1/personaldetails/{id}</c> — the caller's
    /// own relationship to the invention. Both clients read a single key here:
    /// the 2023 continuation is <c>FGLDKEJLAKB&lt;CEAFHBOOBKL&gt;</c>
    /// (OEGFNFEAAGO.txt:8995) and CEAFHBOOBKL's reader takes only "IsCheering"
    /// (BCLCHNGENDE.txt:036); the 2020.12 watch's OEGPIPBKHCN is identical.
    /// Without it the detail page never lit the player's own cheer.</summary>
    [HttpGet("api/inventions/v1/personaldetails/{id:long}")]
    [Authorize]
    public async Task<ActionResult> PersonalDetails(long id)
    {
        var i = await db.Inventions.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (i is null) return NotFound();
        var pid = CurrentPlayerId;
        return Ok(new
        {
            IsCheering = await db.Cheers.AnyAsync(c =>
                c.FromPlayerId == pid && c.TargetInventionId == id),
            Invention = ToWire(i),
            CanEdit = i.CreatorPlayerId == pid,
        });
    }

    [HttpGet("api/inventions/v1/versions")]
    public async Task<ActionResult> Versions([FromQuery] long inventionId)
    {
        var rows = await db.InventionVersions
            .Where(v => v.InventionId == inventionId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync();
        return Ok(rows.Select(ToVersionWire));
    }

    [HttpGet("api/inventions/v1/version")]
    public async Task<ActionResult> Version(
        [FromQuery] long inventionId, [FromQuery] int version)
    {
        var v = await db.InventionVersions
            .FirstOrDefaultAsync(x => x.InventionId == inventionId && x.VersionNumber == version);
        if (v is null) return NotFound();
        return Ok(ToVersionWire(v));
    }

    // ── Create / version ─────────────────────────────────────────────────

    public sealed class SaveInventionV4Request
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? ImageName { get; set; }
        public int InstantiationCost { get; set; }
        public int LightsCost { get; set; }
        /// <summary>RecNet.NewInventionRequestDTO carries <c>chipsCost</c> and
        /// <c>cloudVariablesCost</c> too (2023.06 dump.cs:1283258-1283259).
        /// <see cref="InventionVersionEntity"/> has no column for either, so
        /// they cannot be persisted — but binding them lets the save response
        /// echo back the budget the client just reported instead of two
        /// zeroes.</summary>
        public int ChipsCost { get; set; }
        public int CloudVariablesCost { get; set; }
        public int AiCost { get; set; }
        public long? CreationRoomId { get; set; }
        public string? InventionDataFilename { get; set; }
        public List<long>? ReferencedInventions { get; set; }
        public int CreatorAccountRole { get; set; }
    }

    /// <summary>POST <c>api/inventions/v4/save</c> — 2020.12 maker-pen
    /// save flow. v4 wraps the new invention plus its first version in
    /// a Status/Invention/InventionVersion object.</summary>
    [HttpPost("api/inventions/v4/save")]
    [HttpPost("api/inventions/v6/save")]
    [Authorize]
    public async Task<ActionResult> SaveV4([FromBody] SaveInventionV4Request req)
    {
        var pid = CurrentPlayerId;
        var name = (req.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { Status = 1, Error = "missing name" });

        var blobName = (req.InventionDataFilename ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(blobName)) return BadRequest(new { Status = 1, Error = "missing inventionDataFilename" });

        var inv = new InventionEntity
        {
            CreatorPlayerId = pid,
            ReplicationId = Guid.NewGuid().ToString("D"),
            Name = name,
            Description = req.Description ?? string.Empty,
            ImageName = req.ImageName ?? string.Empty,
            Permission = 0,
            CurrentBlobName = blobName,
            TagsCsv = string.Empty,
            CreationRoomId = req.CreationRoomId,
            CreatorPermission = 100,
            GeneralPermission = 0,
            IsPublished = false,
            CurrentVersionNumber = 1,
        };
        db.Inventions.Add(inv);
        await db.SaveChangesAsync();

        var v1 = new InventionVersionEntity
        {
            InventionId = inv.Id,
            ReplicationId = Guid.NewGuid().ToString("D"),
            VersionNumber = 1,
            BlobName = blobName,
            InstantiationCost = Math.Max(0, req.InstantiationCost),
            LightsCost = Math.Max(0, req.LightsCost),
        };
        db.InventionVersions.Add(v1);

        if (req.ReferencedInventions is { Count: > 0 })
        {
            foreach (var referencedId in req.ReferencedInventions.Distinct().Take(200))
            {
                db.ObjectiveProgress.Add(new ObjectiveProgressEntity
                {
                    PlayerId = pid,
                    Key = $"invention:{inv.Id}:references:{referencedId}",
                    IsCompleted = true,
                    ClearedAt = DateTime.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync();
        await level.AwardXpAsync(pid, LevelService.InventionSavedXp, $"invention_save:{inv.Id}");

        return Ok(new
        {
            Status = 0,
            Invention = ToWireV4(inv),
            InventionVersion = ToVersionWire(
                v1, Math.Max(0, req.ChipsCost), Math.Max(0, req.CloudVariablesCost)),
        });
    }

    /// <summary>The 2023-03-21 client names the blob field
    /// <c>inventionDataFilename</c> and sends four extra cost fields plus the
    /// creation room and referenced inventions. The record only had
    /// <c>BlobName</c>, which the client never sends, so the required-parameter
    /// check rejected every publish with 400 and no new invention version could
    /// ever be saved.</summary>
    public sealed record AddVersionRequest(
        long InventionId,
        string? BlobName,
        string? InventionDataFilename,
        int? InstantiationCost, int? LightsCost,
        int? ChipsCost, int? CloudVariablesCost, int? AiCost,
        long? CreationRoomId,
        List<long>? ReferencedInventions)
    {
        public string? Blob => !string.IsNullOrWhiteSpace(InventionDataFilename)
            ? InventionDataFilename
            : BlobName;
    }

    [HttpPost("api/inventions/v3/addversion")]
    [HttpPost("api/inventions/v4/addversion")]
    [Authorize]
    public async Task<ActionResult> AddVersion([FromBody] AddVersionRequest req)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == req.InventionId && !x.IsDeleted);
        if (inv is null) return NotFound();
        if (inv.CreatorPlayerId != pid) return Forbid();
        var blobName = req.Blob;
        if (string.IsNullOrWhiteSpace(blobName)) return BadRequest("missing inventionDataFilename");

        var nextVer = inv.CurrentVersionNumber + 1;
        inv.CurrentBlobName = blobName;
        inv.CurrentVersionNumber = nextVer;
        inv.UpdatedAt = DateTime.UtcNow;

        var version = new InventionVersionEntity
        {
            InventionId = inv.Id,
            ReplicationId = Guid.NewGuid().ToString("D"),
            VersionNumber = nextVer,
            BlobName = blobName,
            InstantiationCost = req.InstantiationCost ?? 0,
            LightsCost = req.LightsCost ?? 0,
        };
        db.InventionVersions.Add(version);
        await db.SaveChangesAsync();

        // Envelope, not a bare invention — see EnvelopeAsync. The version we
        // just wrote is the one the client wants back, and the two cost fields
        // it sent are echoed because no column stores them.
        return Ok(new
        {
            Status = 0,
            Invention = ToWire(inv),
            InventionVersion = ToVersionWire(
                version, Math.Max(0, req.ChipsCost ?? 0), Math.Max(0, req.CloudVariablesCost ?? 0)),
        });
    }

    // ── GET-based mutations (Core.Get pattern) ───────────────────────────

    /// <summary>Watch sends ONE of name/description/imgName/permission
    /// per request — `UpdateInvention*` ISIL builds different
    /// query strings. Pull whichever ones came in and apply.</summary>
    [HttpGet("api/inventions/v1/update")]
    [Authorize]
    public async Task<ActionResult> Update(
        [FromQuery] long inventionId,
        [FromQuery] string? name,
        [FromQuery] string? description,
        [FromQuery] string? imgName,
        [FromQuery] int? permission)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == inventionId && !x.IsDeleted);
        if (inv is null) return NotFound();
        if (inv.CreatorPlayerId != pid) return Forbid();

        if (!string.IsNullOrWhiteSpace(name)) inv.Name = name.Trim();
        if (description is not null) inv.Description = description;
        if (imgName is not null) inv.ImageName = imgName;
        if (permission.HasValue)
        {
            inv.GeneralPermission = ClampInventionPermission(permission.Value);
            if (inv.GeneralPermission > 0 && !inv.IsPublished)
            {
                inv.IsPublished = true;
                inv.FirstPublishedAt = DateTime.UtcNow;
            }
            // Mirror legacy 0/1/2 column for back-compat.
            inv.Permission = inv.GeneralPermission >= 60 ? 2 : (inv.GeneralPermission >= 20 ? 1 : 0);
        }
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(await EnvelopeAsync(inv));
    }

    /// <summary>Body shape for the POST form of <c>updateprice</c> —
    /// RecNet.UpdatePriceRequest, real field names preserved in the 2023.06
    /// dump (dump.cs:1283355-1283356). Settable properties + a parameterless
    /// ctor because <see cref="Binding.FormOrJsonModelBinder"/> constructs the
    /// instance itself.</summary>
    public sealed class UpdatePriceRequest
    {
        public long InventionId { get; set; }
        public int Price { get; set; }
    }

    /// <summary>Both clients POST <c>updateprice</c> with a body; only the GET
    /// query form was registered, so ASP.NET answered 405 and re-pricing a paid
    /// invention always failed. 2023: OEGFNFEAAGO.txt:9850 sets verb 2 and
    /// :9870-9876 hands <c>JsonUtility.ToJson(UpdatePriceRequest)</c> to
    /// <c>FJLLPHFOOJJ</c>, which wraps it in a BestHTTP RawJsonForm
    /// (<c>application/json</c>, RawJsonForm.txt:40). December POSTs the same
    /// route form-encoded (2020.12 BBHENFCNLAB.txt:6480). The binder accepts
    /// both; the GET stays for callers that still use the query string.</summary>
    [HttpGet("api/inventions/v1/updateprice")]
    [Authorize]
    public Task<ActionResult> UpdatePrice(
        [FromQuery] long inventionId,
        [FromQuery] int price)
        => ApplyPriceAsync(inventionId, price);

    [HttpPost("api/inventions/v1/updateprice")]
    [Authorize]
    public Task<ActionResult> UpdatePricePost(
        [ModelBinder(typeof(Binding.FormOrJsonModelBinder))] UpdatePriceRequest req)
        => ApplyPriceAsync(req.InventionId, req.Price);

    private async Task<ActionResult> ApplyPriceAsync(long inventionId, int price)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == inventionId && !x.IsDeleted);
        if (inv is null) return NotFound();
        if (inv.CreatorPlayerId != pid) return Forbid();
        inv.Price = Math.Clamp(price, 0, 1000000);
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(await EnvelopeAsync(inv));
    }

    [HttpGet("api/inventions/v1/delete")]
    [Authorize]
    public async Task<ActionResult> Delete([FromQuery] long inventionId)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == inventionId);
        if (inv is null) return NotFound();
        if (inv.CreatorPlayerId != pid) return Forbid();
        inv.IsDeleted = true;
        inv.GeneralPermission = 0;
        inv.IsPublished = false;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(await EnvelopeAsync(inv));
    }

    /// <summary>The 2023 publish call carries FOUR query params, not two:
    /// OEGFNFEAAGO.txt:9437/9449/9461/9473 add <c>inventionId</c>,
    /// <c>permissionLevel</c>, <c>accessibility</c> and a
    /// <c>Nullable&lt;Int32&gt;</c> <c>price</c> — the wrapper signature is
    /// OEGFNFEAAGO.txt:9216. Dropping accessibility meant "publish as Private /
    /// Unlisted" still went fully public, and a paid publish lost its price.
    /// Accessibility constants: Private=0, Public=1, Unlisted=2
    /// (AEFFFPIJDHG.GAKJKOGJEEH, 2023.06 dump.cs:1282644-1282646). With no
    /// Accessibility column on the entity only the Private/not-Private half can
    /// be stored, so Unlisted currently behaves as Public. The param is
    /// NULLABLE on purpose — the 2020.12 watch sends only inventionId +
    /// permissionLevel (2020.12 BBHENFCNLAB.txt:6171), and a missing value must
    /// not be read as Private or December could never publish.</summary>
    [HttpGet("api/inventions/v3/publish")]
    [Authorize]
    public async Task<ActionResult> Publish(
        [FromQuery] long inventionId,
        [FromQuery] int permissionLevel,
        [FromQuery] int? accessibility,
        [FromQuery] int? price)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == inventionId && !x.IsDeleted);
        if (inv is null) return NotFound();
        if (inv.CreatorPlayerId != pid) return Forbid();
        var perm = ClampInventionPermission(permissionLevel);
        if (perm == 0) return BadRequest("permissionLevel must be > 0 (Unassigned)");
        var access = accessibility ?? 1 /* Public */;
        inv.GeneralPermission = perm;
        inv.IsPublished = access != 0;
        if (inv.IsPublished) inv.FirstPublishedAt ??= DateTime.UtcNow;
        inv.Permission = perm >= 60 ? 2 : (perm >= 20 ? 1 : 0);
        if (price.HasValue) inv.Price = Math.Clamp(price.Value, 0, 1000000);
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(await EnvelopeAsync(inv));
    }

    [HttpGet("api/inventions/v1/unpublish")]
    [Authorize]
    public async Task<ActionResult> Unpublish([FromQuery] long inventionId)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == inventionId && !x.IsDeleted);
        if (inv is null) return NotFound();
        if (inv.CreatorPlayerId != pid) return Forbid();
        inv.GeneralPermission = 0;
        inv.IsPublished = false;
        inv.Permission = 0;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(await EnvelopeAsync(inv));
    }

    [HttpGet("api/inventions/v1/download")]
    public async Task<ActionResult> Download([FromQuery] long inventionId)
    {
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == inventionId && !x.IsDeleted);
        if (inv is null) return NotFound();
        inv.SpawnCount += 1;
        var pid = CurrentPlayerIdOrNull;
        if (pid is long me && me != inv.CreatorPlayerId)
        {
            // NumPlayersHaveUsedInRoom — increment per-distinct-player.
            // Approximate by distinct cheer/check; for now bump once
            // per download by non-creator.
            inv.NumPlayersHaveUsedInRoom += 1;
        }
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(inv));
    }

    // ── POST mutations ───────────────────────────────────────────────────

    /// <summary>The client sends <c>AutoTags</c> and <c>CustomTags</c> as JSON
    /// ARRAYS of strings. Binding them to <c>string</c> made deserialization
    /// throw, so every tag edit 400'd. <c>PlayerAddedTags</c> is kept as an
    /// alias for older callers. Field names are literal in the 2023.06 dump —
    /// RecNet.ModifyTagsRequest keeps real names (dump.cs:1283295-1283301) —
    /// and the body is a RAW JSON document
    /// (<c>JsonUtility.ToJson</c> → <c>BNDIAONDFFF.FJLLPHFOOJJ</c>,
    /// OEGFNFEAAGO.txt:3082-3088), so [FromBody] is right here.</summary>
    public sealed record SetTagsRequest(
        long InventionId,
        List<string>? AutoTags,
        List<string>? CustomTags,
        List<string>? PlayerAddedTags);

    /// <summary>POST <c>api/inventions/v1/settags</c> — the reply is NOT an
    /// invention. The 2023 issuing method is typed
    /// <c>FGLDKEJLAKB&lt;PNGLFHEAJIH&gt;</c> (OEGFNFEAAGO.txt:2744) and
    /// PNGLFHEAJIH exposes exactly two members — an int-backed result enum and
    /// <c>List&lt;String&gt;</c> (PNGLFHEAJIH.txt:3/83) — read from the literals
    /// "Result" and "Tags" (ALJPHDEAHBK.txt:191/210). The 2020.12 watch is
    /// identical: NJMAEIPIOAP.PPGFHEDFBEA pulls "Result" then "Tags"
    /// (2020.12 NJMAEIPIOAP.txt:85/90) with the throwing Util.GetKey, so the
    /// old bare-invention reply killed every tag edit on December too.
    /// <c>Result = 0</c> is success: the enum's message formatter jump-tables on
    /// the value and case 0 returns "Success!" (PNGLFHEAJIH.txt:334-341).
    /// <c>Tags</c> is a flat string list — NOT the {Tag,Type} pairs
    /// <see cref="Details"/> returns.</summary>
    [HttpPost("api/inventions/v1/settags")]
    [Authorize]
    public async Task<ActionResult> SetTags([FromBody] SetTagsRequest req)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == req.InventionId && !x.IsDeleted);
        if (inv is null) return NotFound();
        if (inv.CreatorPlayerId != pid) return Forbid();
        var tags = new[] { req.AutoTags, req.CustomTags, req.PlayerAddedTags }
            .Where(list => list is not null)
            .SelectMany(list => list!)
            .Select(t => (t ?? string.Empty).Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        inv.TagsCsv = string.Join(',', tags);
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { Result = 0, Tags = tags });
    }

    /// <summary>RecNet.CheerRequest carries a SECOND field — <c>Cheer</c>, the
    /// desired state — whose real name survives in the 2023.06 dump
    /// (dump.cs:1283338-1283342: <c>long InventionId</c> @0x10,
    /// <c>bool Cheer</c> @0x18). Both writes are visible in the ISIL: 2023 sets
    /// [rdi+16] and [rdi+24] before <c>JsonUtility.ToJson</c>
    /// (OEGFNFEAAGO.txt:11228-11233) and December does the same
    /// (2020.12 BBHENFCNLAB.txt:7624-7628) — its wrapper signature is
    /// <c>ANJHOOPIAKM(Int64, Boolean)</c> (BBHENFCNLAB.txt:7503). Ignoring the
    /// flag made un-cheering a no-op: the button toggled off in the UI and the
    /// cheer stayed on the invention forever.</summary>
    public sealed record CheerRequest(long InventionId, bool Cheer = true);

    /// <summary>POST <c>api/inventions/v1/cheer</c> — answers with the
    /// Status/Invention/InventionVersion envelope like every other invention
    /// mutation: the 2023 method is <c>FGLDKEJLAKB&lt;BDNCJIPHHOK&gt;</c>
    /// (OEGFNFEAAGO.txt:11065) and December's is
    /// <c>IPromise&lt;AHEPPAEOLOD&gt;</c> (2020.12 BBHENFCNLAB.txt:7503). The old
    /// <c>{Id, CheerCount}</c> reply left Invention null on 2023 and threw
    /// KeyNotFoundException on December, so the tile never refreshed its
    /// count.</summary>
    [HttpPost("api/inventions/v1/cheer")]
    [Authorize]
    public async Task<ActionResult> Cheer([FromBody] CheerRequest req)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == req.InventionId && !x.IsDeleted);
        if (inv is null) return NotFound();

        // Idempotent in both directions: one cheer row per (player, invention).
        var existing = await db.Cheers.FirstOrDefaultAsync(c =>
            c.FromPlayerId == pid && c.TargetInventionId == req.InventionId);

        if (req.Cheer && existing is null)
        {
            db.Cheers.Add(new CheerEntity
            {
                FromPlayerId = pid,
                TargetInventionId = req.InventionId,
                CheeredAt = DateTime.UtcNow,
            });
            inv.CheerCount += 1;
            await db.SaveChangesAsync();

            if (inv.CreatorPlayerId != pid)
                await notifications.NotifyAsync(inv.CreatorPlayerId,
                    PushNotificationId.InventionModerationStateChanged,
                    new { Type = "cheer", inv.Id, From = pid, inv.CheerCount });
        }
        else if (!req.Cheer && existing is not null)
        {
            db.Cheers.Remove(existing);
            inv.CheerCount = Math.Max(0, inv.CheerCount - 1);
            await db.SaveChangesAsync();
        }

        return Ok(await EnvelopeAsync(inv));
    }

    public sealed record ReportInventionRequest(
        long InventionId, int? ReportCategory, string? Details);

    /// <summary>POST <c>api/inventions/v1/report</c> — the reply is a
    /// success/message pair, not a status flag of our own invention. The 2023
    /// method is <c>FGLDKEJLAKB&lt;PHMHCPEMABG&gt;</c> (OEGFNFEAAGO.txt:10917)
    /// and PHMHCPEMABG holds a Boolean + a String (PHMHCPEMABG.txt:3/23) read
    /// from the literals "Success"/"Message" (GBPDOLJBABB.txt:191/210); the
    /// 2020.12 watch's KLAMKCBENEA.PPGFHEDFBEA pulls the same two literals
    /// (2020.12 KLAMKCBENEA.txt:85/90). Our old <c>{Reported:true}</c> left
    /// Success at default false, so the report UI reported failure even though
    /// the row persisted. <c>Message</c> must be a string — December's
    /// Util.GetKey casts it — so send "" rather than null.</summary>
    [HttpPost("api/inventions/v1/report")]
    [Authorize]
    public async Task<ActionResult> Report([FromBody] ReportInventionRequest req)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == req.InventionId && !x.IsDeleted);
        if (inv is null) return NotFound();

        db.Reports.Add(new ReportEntity
        {
            ReporterPlayerId = pid,
            TargetPlayerId = inv.CreatorPlayerId,
            TargetInventionId = req.InventionId,
            Category = req.ReportCategory ?? 5 /* Other */,
            Message = (req.Details ?? string.Empty)[..Math.Min(1000, (req.Details ?? string.Empty).Length)],
        });
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Message = string.Empty });
    }

    // ── Rec Room Studio remote run ───────────────────────────────────────

    /// <summary>One uploaded blob as the client describes it: three strings.
    /// Wire keys are literal in the generated serializer — "Filename", "Hash",
    /// "OwnershipProof" (FLELPHJDLNG.txt:255/274/290, with camelCase and
    /// all-lowercase aliases registered alongside each). The backing type is
    /// <c>CFPEPOJAMHH/IFIBBJDIHJH</c>, three <c>System.String</c> accessors
    /// (CFPEPOJAMHH_NestedType_IFIBBJDIHJH.txt:3/25/47). Same triple the room
    /// save commit sends on <c>rooms/{id}/subrooms/{id}/data</c>; declared here
    /// so this controller binds its own body.</summary>
    public sealed class StudioBlobRef
    {
        public string? Filename { get; set; }
        public string? Hash { get; set; }
        public string? OwnershipProof { get; set; }
    }

    /// <summary>Body of <c>remote-run/push-to-studio</c> — RecNet type
    /// <c>FNAGBPCAGJD</c>. Field order/types come from the request builder
    /// (EHIOLHBGODG.txt:045-070 writes +16 String, +24 Int64, +32 Int64,
    /// +40 String, +48/+56 the two blob refs, +64 a <c>Nullable&lt;Int32&gt;</c>)
    /// and the property accessors on FNAGBPCAGJD.txt:3-141. The JSON key for
    /// each is literal in the generated serializer OFMNNCMPEPA.txt:535-698:
    /// <c>SessionId, RoomId, SubRoomId, UnityAssetId, RoomData, SubRoomData,
    /// SavedByAccountId</c> (camel/lower aliases registered too).</summary>
    public sealed class PushToStudioRequest
    {
        public string? SessionId { get; set; }
        public long RoomId { get; set; }
        public long SubRoomId { get; set; }
        public string? UnityAssetId { get; set; }
        public StudioBlobRef? RoomData { get; set; }
        public StudioBlobRef? SubRoomData { get; set; }
        public int? SavedByAccountId { get; set; }
    }

    /// <summary>POST <c>remote-run/push-to-studio</c> — hands the room the
    /// player just uploaded to the paired Rec Room Studio session, WITHOUT
    /// committing it as the room's live save (that is the separate
    /// <c>rooms/{id}/subrooms/{id}/data</c> path; the client's two flows are
    /// sibling methods OGPDOMCNIFM.txt:271 vs :449).
    ///
    /// <para><b>Verb.</b> POST. The request builder at EHIOLHBGODG.txt:411-417
    /// moves the route literal <c>"remote-run/push-to-studio"</c> into r9 and
    /// <c>2</c> (BestHTTP HTTPMethods.Post) into rdx before
    /// <c>BNDIAONDFFF..ctor(verb, host, route)</c>. No cmov — one verb only.
    /// The body is RAW JSON, not form fields: :433 hands the serialized DTO to
    /// <c>BNDIAONDFFF.FJLLPHFOOJJ</c> (RawJsonForm, <c>application/json</c>),
    /// so this binds via <see cref="Binding.FormOrJsonModelBinder"/> — the
    /// explicit [ModelBinder] is required or [ApiController] re-infers
    /// [FromBody] and 415s a form-shaped caller.</para>
    ///
    /// <para><b>Response.</b> <c>FGLDKEJLAKB&lt;CEELGOLBHIL&gt;</c>
    /// (EHIOLHBGODG.txt:217) — a single RemoteRunDetails object, EIGHT keys,
    /// all literal in its generated reader PPHLKNGCGOE.txt:599-786:
    /// <c>SessionId</c> (String), <c>RoomId</c> and <c>SubRoomId</c>
    /// (<c>Nullable&lt;Int64&gt;</c> — CEELGOLBHIL.txt:25/51), then
    /// <c>UnityAssetId, RoomDataFilename, RoomDataHash, SubRoomDataFilename,
    /// SubRoomDataHash</c> (String). Note the response FLATTENS the two blob
    /// refs the request nests, and drops OwnershipProof. A failed/malformed
    /// reply surfaces the client's "Failed to push to Rec Room Studio" toast
    /// (EHIOLHBGODG.txt:445).</para>
    ///
    /// <para><b>What it persists.</b> The pushed blobs were already stored by
    /// <c>/upload</c> (every FileType there inserts a
    /// <see cref="RoomDataBlobEntity"/>), so the push is a registration step:
    /// it stamps the sub-room save blob with the room + sub-room it belongs to
    /// — that is what makes it appear in the per-sub-room
    /// <c>datahistory</c>/"Restore to old version" list, which the FileType=6
    /// room-metadata upload path (RoomId=0, SubRoomId=null) cannot do on its
    /// own — writes the session id into
    /// <see cref="RoomEntity.StudioSessionId"/> (the column that exists for
    /// exactly this and is echoed in room details), records the pushed Unity
    /// asset id on the sub-room's <see cref="RoomSceneEntity"/>, and keeps the
    /// whole push under <c>remoterun:{SessionId}</c> in
    /// <see cref="PlayerSettingEntity"/> so the details survive for the paired
    /// Studio session to resolve. <see cref="RoomEntity.IsRoomLinkedToRecRoomStudio"/>
    /// is deliberately NOT flipped: it changes in-room MakerPen UI and the
    /// client has no unlink call, so a push must not one-way-toggle it.</para>
    ///
    /// <para><b>Not implemented:</b> live relay of the details to a second
    /// logged-in session. The client's receive side
    /// (<c>EHIOLHBGODG.MCPBJLHFOBC(CEELGOLBHIL)</c> → the
    /// <c>Action&lt;CEELGOLBHIL&gt;</c> that MBOJJFBIAGE.txt:366/978 subscribes)
    /// has no call site anywhere in the IsilDump, so the notification id that
    /// carries it cannot be established from the binary — inventing one would
    /// be a guess. The HTTP round-trip is complete and the push is durable
    /// either way.</para></summary>
    [HttpPost("remote-run/push-to-studio")]
    [HttpPost("roomserver/remote-run/push-to-studio")]
    [Authorize]
    public async Task<ActionResult> PushToStudio(
        [ModelBinder(typeof(Binding.FormOrJsonModelBinder))] PushToStudioRequest req)
    {
        var pid = CurrentPlayerId;

        var sessionId = Clamp((req.SessionId ?? string.Empty).Trim(), 100);
        if (sessionId.Length == 0)
            return BadRequest(new { Error = "missing SessionId" });

        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == req.RoomId);
        if (room is null) return NotFound();

        // Same gate the room-save upload uses (StorageController.UploadRoomSave):
        // creator, admin, or an accepted CoOwner (Role 0).
        var canPush = room.CreatorPlayerId == pid
            || await db.Players.AnyAsync(p => p.Id == pid && p.IsAdmin)
            || await db.RoomRoles.AnyAsync(r =>
                r.RoomId == room.Id && r.PlayerId == pid && r.Accepted && r.Role == 0);
        if (!canPush) return Forbid();

        var subRoomId = req.SubRoomId;
        var scene = await db.RoomScenes
            .FirstOrDefaultAsync(s => s.RoomId == room.Id && s.OrderIndex == subRoomId);

        var subRoomDataFilename = Clamp((req.SubRoomData?.Filename ?? string.Empty).Trim(), 128);
        if (subRoomDataFilename.Length == 0)
            return BadRequest(new { Error = "missing SubRoomData.Filename" });

        // The push names blobs the client uploaded moments ago; if the row is
        // absent the bytes are not on this server and Studio could never fetch
        // them. Fail loudly rather than answering with a filename that 404s.
        var subRoomBlob = await db.RoomDataBlobs
            .FirstOrDefaultAsync(b => b.BlobName == subRoomDataFilename);
        if (subRoomBlob is null)
            return BadRequest(new { Error = "unknown SubRoomData.Filename" });
        if (subRoomBlob.RoomId == 0) subRoomBlob.RoomId = room.Id;
        subRoomBlob.SubRoomId ??= subRoomId;

        var roomDataFilename = Clamp((req.RoomData?.Filename ?? string.Empty).Trim(), 128);
        if (roomDataFilename.Length > 0)
        {
            // FileType=6 (RoomMetadata) uploads land with RoomId=0 — the push
            // is the first point at which the owning room is known.
            var roomBlob = await db.RoomDataBlobs
                .FirstOrDefaultAsync(b => b.BlobName == roomDataFilename);
            if (roomBlob is null)
                return BadRequest(new { Error = "unknown RoomData.Filename" });
            if (roomBlob.RoomId == 0) roomBlob.RoomId = room.Id;
        }

        var unityAssetId = Clamp((req.UnityAssetId ?? string.Empty).Trim(), 64);
        if (unityAssetId.Length == 0)
        {
            unityAssetId = scene?.StudioUnityAssetId ?? string.Empty;
        }
        else if (scene is not null && scene.StudioUnityAssetId != unityAssetId)
        {
            scene.StudioUnityAssetId = unityAssetId;
            scene.DataModifiedAt = DateTime.UtcNow;
        }

        var roomDataHash = Clamp((req.RoomData?.Hash ?? string.Empty).Trim(), 128);
        var subRoomDataHash = Clamp((req.SubRoomData?.Hash ?? string.Empty).Trim(), 128);

        room.StudioSessionId = sessionId;

        var record = System.Text.Json.JsonSerializer.Serialize(new
        {
            RoomId = room.Id,
            SubRoomId = subRoomId,
            UnityAssetId = unityAssetId,
            RoomDataFilename = roomDataFilename,
            RoomDataHash = roomDataHash,
            SubRoomDataFilename = subRoomDataFilename,
            SubRoomDataHash = subRoomDataHash,
            SavedByAccountId = req.SavedByAccountId ?? (int)pid,
            PushedAt = DateTime.UtcNow,
        });
        if (record.Length > 1024)
        {
            // PlayerSettingEntity.Value is varchar(1024); truncating JSON would
            // store an unparseable fragment, so drop the optional hashes first.
            record = System.Text.Json.JsonSerializer.Serialize(new
            {
                RoomId = room.Id,
                SubRoomId = subRoomId,
                UnityAssetId = unityAssetId,
                RoomDataFilename = roomDataFilename,
                SubRoomDataFilename = subRoomDataFilename,
                SavedByAccountId = req.SavedByAccountId ?? (int)pid,
                PushedAt = DateTime.UtcNow,
            });
        }

        var key = $"remoterun:{sessionId}";
        var setting = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == pid && s.Key == key);
        if (setting is null)
            db.PlayerSettings.Add(new PlayerSettingEntity
            {
                PlayerId = pid, Key = key, Value = record,
            });
        else
            setting.Value = record;

        await db.SaveChangesAsync();

        return Ok(new
        {
            SessionId = sessionId,
            RoomId = (long?)room.Id,
            SubRoomId = (long?)subRoomId,
            UnityAssetId = unityAssetId,
            RoomDataFilename = roomDataFilename,
            RoomDataHash = roomDataHash,
            SubRoomDataFilename = subRoomDataFilename,
            SubRoomDataHash = subRoomDataHash,
        });
    }

    /// <summary>Trim a wire string to a column's length. Every string that
    /// reaches a MaxLength column here comes straight off the request.</summary>
    private static string Clamp(string value, int max)
        => value.Length <= max ? value : value[..max];

    // ── Wire serializers ─────────────────────────────────────────────────

    private static int ClampInventionPermission(int v) => v switch
    {
        0 or 10 or 20 or 40 or 60 or 80 or 100 => v,
        _ => 0,
    };

    // Wire shape verified against the 2020.12 watch's
    // OBBBPCBIMME.PPGFHEDFBEA deserializer (Cpp2IL ISIL OBBBPCBIMME.txt
    // lines 588-704). All keys read via Util.GetKey<T> are required —
    // a missing key throws KeyNotFoundException → the watch logs
    // "Received malformed RecNet response" and aborts the join/fetch.
    //
    // Required (in disasm order):
    //   InventionId, ReplicationId, CreatorPlayerId, Name, Description,
    //   ImageName, CurrentVersionNumber, IsPublished, AllowTrial,
    //   ModifiedAt, CreatedAt, NumPlayersHaveUsedInRoom, NumDownloads,
    //   CheerCount, CreatorPermission, GeneralPermission, IsAGInvention,
    //   Price, HideFromPlayer.
    // Optional (Util.GetKeyOrDefault): FirstPublishedAt, CreationRoomId.
    //
    // Casing is significant — LitJson + Util.GetKey are case-sensitive.
    // In particular IsAGInvention has a capital G (matches the asset's
    // "IsAGInvention" property, not the entity column IsAgInvention).
    private static object ToWire(InventionEntity i) => new
    {
        InventionId = i.Id,
        ReplicationId = string.IsNullOrEmpty(i.ReplicationId)
            ? Guid.Empty.ToString("D") : i.ReplicationId,
        // Wire field is int — IDs in this server are <2^31 so safe.
        CreatorPlayerId = (int)i.CreatorPlayerId,
        i.Name,
        i.Description,
        i.ImageName,
        i.CurrentVersionNumber,
        i.IsPublished,
        AllowTrial = true,
        ModifiedAt = i.UpdatedAt,
        i.CreatedAt,
        i.FirstPublishedAt,
        i.CreationRoomId,
        i.NumPlayersHaveUsedInRoom,
        NumDownloads = i.SpawnCount,
        i.CheerCount,
        i.CreatorPermission,
        i.GeneralPermission,
        IsAGInvention = i.IsAgInvention,
        // The 2023-03-21 DTO dropped IsPublished and reads "Accessibility"
        // instead (IOEPPCKGBFL.txt:309 / :1602 — the member switch has no
        // IsPublished arm at all). Constants come from the nested enum
        // AEFFFPIJDHG.GAKJKOGJEEH in the 2023.06 dump
        // (dump.cs:1282644-1282646): Private=0, Public=1, Unlisted=2. With the
        // key absent the reader left the field at its default, so EVERY
        // published store invention presented as Private in the 2023 UI.
        // InventionEntity has no Accessibility column, so only the
        // Private/Public bit round-trips; Unlisted needs a real column.
        // IsPublished stays on the wire because the 2020.12 watch's
        // OBBBPCBIMME reader still requires it.
        Accessibility = i.IsPublished ? 1 : 0,
        // IOEPPCKGBFL.txt:565 — DorkNet runs no certification programme.
        IsCertifiedInvention = false,
        i.Price,
        HideFromPlayer = false,
    };

    private static object ToWireV4(InventionEntity i) => ToWire(i);

    private static object ToVersionWire(InventionVersionEntity v)
        => ToVersionWire(v, 0, 0);

    /// <summary>The 2023 InventionVersion DTO reads NINE keys
    /// (OLPHKLCPFEF.txt:084-295): the six we already emitted plus
    /// <c>ChipsCost</c>, <c>CloudVariablesCost</c> and <c>BlobHash</c>.
    /// <see cref="InventionVersionEntity"/> has no column for any of the three,
    /// so stored rows report 0/empty and only the save/addversion round-trip can
    /// echo back the costs the client just sent. An empty BlobHash is safe:
    /// PLIKEBBPJGI's hash getter (<c>EHENFDMIAIM</c>, OLPHKLCPFEF's owner type
    /// PLIKEBBPJGI.txt:167) has no call site anywhere in the IsilDump — the
    /// client only ever writes it — so the key's presence is what matters.</summary>
    private static object ToVersionWire(
        InventionVersionEntity v, int chipsCost, int cloudVariablesCost) => new
    {
        v.InventionId,
        ReplicationId = string.IsNullOrEmpty(v.ReplicationId)
            ? Guid.Empty.ToString("D") : v.ReplicationId,
        v.VersionNumber,
        v.InstantiationCost,
        v.LightsCost,
        ChipsCost = chipsCost,
        CloudVariablesCost = cloudVariablesCost,
        v.BlobName,
        BlobHash = string.Empty,
    };

    /// <summary>Every invention MUTATION route answers with the
    /// Status/Invention/InventionVersion envelope, never a bare invention: the
    /// 2023 client deserialises <c>BDNCJIPHHOK</c> (key literals at
    /// GMBGBPNMGBA.txt:044-095) and the 2020.12 watch deserialises
    /// <c>AHEPPAEOLOD</c>, whose reader pulls the same three literals
    /// (2020.12 IsilDump AHEPPAEOLOD.txt:110/115/120). Returning the invention
    /// flat left <c>Invention</c> null on 2023 and threw KeyNotFoundException on
    /// December. <c>Status = 0</c> is success —
    /// EnterInventionNameDialog.txt:622-624 loads the boxed Status field and
    /// does <c>test eax,eax / je &lt;success&gt;</c>.</summary>
    private async Task<object> EnvelopeAsync(InventionEntity inv)
    {
        var current = await db.InventionVersions
            .Where(x => x.InventionId == inv.Id)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync();
        object? versionWire = current is null ? null : ToVersionWire(current);
        return new
        {
            Status = 0,
            Invention = ToWire(inv),
            InventionVersion = versionWire,
        };
    }
}
