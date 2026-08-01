using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.PlayerEvents;

/// <summary>
/// api.rec.net/api/playerevents/v1|v2/* — create, browse, edit, RSVP and
/// broadcast for player-scheduled gatherings.
///
/// The 2023-03-21 client's event API surface is <c>RecNet.Runtime/CBKANFIOBCF</c>
/// (route literals at <c>CBKANFIOBCF.txt:161-4156</c>). Three wire types drive
/// nearly every handler here:
///   * <b>HPIOAGDJHDH</b> (PlayerEvent) — 17 keys, formatter
///     <c>HPMMKFGDAEC.txt:1139-1518</c>.
///   * <b>MDCBEPJCJPO</b> (PlayerEvent + <c>Tags:[{Tag,Type}]</c>) — formatter
///     <c>CENEMCMGDKG.txt:1191-1586</c>, tag entry keys
///     <c>PCMLHLIBLNJ.txt:191-218</c>.
///   * <b>PHHAKLPGNGC</b> (<c>{PlayerEvent,Result,TagModifyResult}</c>) —
///     formatter <c>EEAAGIJHLOJ.txt:267-326</c>, <c>TagModifyResult</c> keys
///     <c>ALJPHDEAHBK.txt:191-218</c>. Every v2 create/edit/delete/field
///     mutation deserialises this wrapper, so a flat event body leaves the
///     client's <c>PlayerEvent</c> null after a save.
/// </summary>
[ApiController]
[Authorize]
public class PlayerEventsController(
    DorkNetDbContext db,
    NotificationService notifications,
    PlayerPresenceService presence) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    /// <summary>PlayerEventResponseType.NotGoing — the one RSVP state that
    /// does not count towards AttendeeCount.</summary>
    private const int NotGoingResponse = 2;

    // ── Browse ───────────────────────────────────────────────────────────

    /// <summary>GET <c>api/playerevents/v2</c> (and <c>v1</c>) — upcoming
    /// events list, a BARE JSON array of PlayerEvent.
    ///
    /// The 2023 watch's Events tab reads the unversioned path: HIHEFBMPOGC
    /// returns <c>List&lt;HPIOAGDJHDH&gt;</c> and dispatches with verb 0
    /// (<c>CBKANFIOBCF.txt:1305</c>, route literal <c>:1304</c>). Only
    /// <c>[HttpPost]</c> was registered on <c>api/playerevents/v1</c>, so the
    /// whole tab 405'd.</summary>
    [AllowAnonymous]
    [HttpGet("api/playerevents/v2")]
    [HttpGet("api/playerevents/v1")]
    public async Task<ActionResult> ListUpcoming(
        [FromQuery] int skip = 0, [FromQuery] int take = 50)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);
        var rows = await db.PlayerEvents
            .Where(e => e.EndsAt > DateTime.UtcNow)
            .OrderBy(e => e.StartsAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        return Ok(await ToWireManyAsync(rows));
    }

    /// <summary>GET <c>api/playerevents/v1|v2/{id}</c>. The client calls this
    /// three ways — plain (NLEFILEAMIP → HPIOAGDJHDH) and with
    /// <c>?includeDetails=</c> / <c>?clubId=</c> (HLJBCGMONEH / OAKKDBLNKLG →
    /// MDCBEPJCJPO, query keys at
    /// <c>CBKANFIOBCF_NestedType_DEIBMMABHIK.txt:86,99</c>). Both DTOs are the
    /// same object, the detail one just adds <c>Tags</c>, and the Utf8Json
    /// formatters skip unknown keys — so we always emit the superset and no
    /// branch is needed.</summary>
    [AllowAnonymous]
    [HttpGet("api/playerevents/v2/{eventId:long}")]
    [HttpGet("api/playerevents/v1/{eventId:long}")]
    public async Task<ActionResult> GetOne(long eventId)
    {
        var ev = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev is null) return NotFound();
        return Ok(await ToWireOneAsync(ev));
    }

    /// <summary>GET <c>api/playerevents/v1/search</c>. Query keys are
    /// <c>query</c>, <c>sort</c> and <c>scheduleFilter</c>
    /// (<c>CBKANFIOBCF_NestedType_IONCOJJGHNJ.txt:99,113,126</c>); the latter
    /// two are enum ordinals (PJBLEKMMACM / BJBLPLKMLBE).</summary>
    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/search")]
    public async Task<ActionResult> Search(
        [FromQuery] string? q = null,
        [FromQuery] string? query = null,
        [FromQuery] int? sort = null,
        [FromQuery] int? scheduleFilter = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        var needle = (q ?? query ?? string.Empty).Trim().ToLowerInvariant();
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var rows = ApplyScheduleFilter(db.PlayerEvents.AsQueryable(), scheduleFilter);
        if (needle.Length > 0)
        {
            rows = rows.Where(e =>
                e.Title.ToLower().Contains(needle) ||
                e.Description.ToLower().Contains(needle));
        }

        var result = await ApplySort(rows, sort)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        return Ok(await ToWireManyAsync(result));
    }

    /// <summary>GET <c>api/playerevents/v1/searchlive</c> — "happening now".
    /// Separate handler from <see cref="Search"/> because the response element
    /// type differs: FIKHCEPDIBF returns <c>List&lt;CLDDIKOJMAM&gt;</c>, which
    /// is the PlayerEvent object plus <c>PlayerCount</c> (Int32) and
    /// <c>IsFull</c> (Boolean) — formatter <c>PDIFGFKAMBG.txt:1267,1294</c>.
    /// Head-count comes from the room's live GameSessions rows (their RoomId is
    /// the numeric room id as a string, see
    /// <c>Services/GameSessionService.cs:91-96</c>).</summary>
    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/searchlive")]
    public async Task<ActionResult> SearchLive(
        [FromQuery] string? q = null,
        [FromQuery] string? query = null,
        [FromQuery] int take = 50)
    {
        var needle = (q ?? query ?? string.Empty).Trim().ToLowerInvariant();
        take = Math.Clamp(take, 1, 100);

        var now = DateTime.UtcNow;
        var live = db.PlayerEvents.Where(e => e.StartsAt <= now && e.EndsAt > now);
        if (needle.Length > 0)
        {
            live = live.Where(e =>
                e.Title.ToLower().Contains(needle) ||
                e.Description.ToLower().Contains(needle));
        }

        var rows = await live.OrderBy(e => e.StartsAt).Take(take).ToListAsync();
        var wire = await ToWireManyAsync(rows);

        var roomKeys = rows.Select(r => r.RoomId.ToString()).Distinct().ToList();
        var sessions = await db.GameSessions
            .Where(s => roomKeys.Contains(s.RoomId))
            .GroupBy(s => s.RoomId)
            .Select(g => new
            {
                RoomId = g.Key,
                Players = g.Sum(s => s.PlayerCount),
                Capacity = g.Sum(s => s.MaxCapacity),
            })
            .ToListAsync();
        var byRoom = sessions.ToDictionary(s => s.RoomId, s => s);

        for (var i = 0; i < rows.Count; i++)
        {
            var players = 0;
            var capacity = 0;
            if (byRoom.TryGetValue(rows[i].RoomId.ToString(), out var agg))
            {
                players = agg.Players;
                capacity = agg.Capacity;
            }
            wire[i]["PlayerCount"] = players;
            wire[i]["IsFull"] = capacity > 0 && players >= capacity;
        }

        return Ok(wire);
    }

    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/room/{roomId:long}")]
    public async Task<ActionResult> ForRoom(long roomId)
    {
        var rows = await db.PlayerEvents
            .Where(e => e.RoomId == roomId && e.EndsAt > DateTime.UtcNow)
            .OrderBy(e => e.StartsAt)
            .Take(100)
            .ToListAsync();
        return Ok(await ToWireManyAsync(rows));
    }

    /// <summary>GET <c>api/playerevents/v1/club/{clubId}</c>. PHNEDJAPIKI
    /// returns IOKLNPFOLGI — an OBJECT
    /// <c>{Events:[PlayerEvent],ContinuationToken:String}</c>
    /// (<c>GHBIGGJKPFI.txt:203-238</c>), not a bare array, and passes
    /// <c>take</c> / <c>continuationToken</c> query params
    /// (<c>CBKANFIOBCF_NestedType_PKDDOCBDBIM.txt:80,89</c>). The token here is
    /// just the next skip offset; empty string means "no more pages" (the
    /// client's field is a plain String, so never emit null).</summary>
    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/club/{clubId:long}")]
    public async Task<ActionResult> ForClub(
        long clubId,
        [FromQuery] int? take = null,
        [FromQuery] string? continuationToken = null)
    {
        var pageSize = Math.Clamp(take ?? 50, 1, 100);
        var skip = int.TryParse(continuationToken, out var parsed) && parsed > 0 ? parsed : 0;

        // Fetch one extra row to find out whether another page exists.
        var rows = await ClubEventsAsync(new[] { clubId }, skip, pageSize + 1);
        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        return Ok(new
        {
            Events = await ToWireManyAsync(rows),
            ContinuationToken = hasMore ? (skip + pageSize).ToString() : string.Empty,
        });
    }

    /// <summary>GET <c>api/playerevents/v1/clubs</c> — events for a specific
    /// set of clubs. IEGLMOOPLED appends one repeated <c>id</c> query value per
    /// club (<c>CBKANFIOBCF_NestedType_LKJFABHPIAN.txt:59</c>); ignoring them
    /// returned every club's events and the watch could not bucket the list.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/clubs")]
    public async Task<ActionResult> ClubEvents([FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 200);
        var clubIds = new List<long>();
        foreach (var value in Request.Query["id"]) AddDelimitedIds(clubIds, value);
        var rows = await ClubEventsAsync(clubIds.Distinct().ToList(), 0, take);
        return Ok(await ToWireManyAsync(rows));
    }

    public sealed class EventBulkRequest
    {
        public List<long>? PlayerEventIds { get; set; }
        public List<long>? EventIds { get; set; }
        public List<long>? Ids { get; set; }
    }

    /// <summary>POST <c>api/playerevents/v1/bulk</c> — verb 2 at
    /// <c>CBKANFIOBCF.txt:660</c>; the form field is <c>Ids</c>
    /// (<c>CBKANFIOBCF_NestedType_PIKPJPBHOAC.txt:64</c>). GET is kept for the
    /// 2020 client, which puts the ids in the query string.</summary>
    [AllowAnonymous]
    [HttpPost("api/playerevents/v1/bulk")]
    [HttpGet("api/playerevents/v1/bulk")]
    public async Task<ActionResult> Bulk()
    {
        var ids = await ReadEventIdsAsync();
        if (ids.Count == 0) return Ok(Array.Empty<object>());
        var rows = await db.PlayerEvents
            .Where(e => ids.Contains(e.Id))
            .ToListAsync();
        return Ok(await ToWireManyAsync(rows));
    }

    /// <summary>GET <c>api/playerevents/v1/tagfilters</c> — AKCLLEJNFFD reads
    /// three String lists: <c>PinnedFilters</c>, <c>PopularFilters</c> and
    /// <c>TrendingFilters</c> (<c>HMDONHHKKGA.txt:279-346</c>). A missing key
    /// leaves the list null and the chips UI NREs when it enumerates.</summary>
    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/tagfilters")]
    public IActionResult TagFilters() => Ok(new
    {
        PinnedFilters = new[] { "game", "social", "competition", "class", "club" },
        PopularFilters = new[] { "game", "social", "competition", "class", "club" },
        TrendingFilters = new[] { "game", "social", "competition", "class", "club" },
    });

    /// <summary>GET <c>api/playerevents/v1/all</c> — the watch's
    /// <c>LocalPlayerEventInfo</c> fetch (caller's created + RSVP'd events).
    /// PCELKKHNPHJ is <c>{Created:[PlayerEvent], Responses:[COAGAJPELCG]}</c>
    /// (<c>ENJDEONHNMO.txt:191-218</c>) where each response entry is
    /// <c>{PlayerEvent, PlayerEventResponse}</c> (<c>HJKLIIPPPEM.txt:215-258</c>)
    /// — a flat response row leaves <c>PlayerEvent</c> null and the My Events
    /// page NREs.</summary>
    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/all")]
    public async Task<ActionResult> AllPlayerEvents()
    {
        var pid = this.CurrentPlayerId();
        if (pid is not long me)
            return Ok(new { Created = Array.Empty<object>(), Responses = Array.Empty<object>() });
        return await AllForPlayerAsync(me);
    }

    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/all/{accountId:long}")]
    public Task<ActionResult> AllPlayerEventsForAccount(long accountId)
        => AllForPlayerAsync(accountId);

    private async Task<ActionResult> AllForPlayerAsync(long playerId)
    {
        var created = await db.PlayerEvents
            .Where(e => e.CreatorPlayerId == playerId)
            .OrderByDescending(e => e.StartsAt)
            .Take(200)
            .ToListAsync();

        var responses = await db.PlayerEventResponses
            .Where(r => r.PlayerId == playerId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(200)
            .ToListAsync();

        // Each response entry embeds its own event, which may not be one the
        // caller created — pull those in too so PlayerEvent is never null.
        var createdIds = created.Select(e => e.Id).ToHashSet();
        var missingIds = responses.Select(r => r.EventId).Where(id => !createdIds.Contains(id)).Distinct().ToList();
        var extraEvents = new List<PlayerEventEntity>();
        if (missingIds.Count > 0)
            extraEvents = await db.PlayerEvents.Where(e => missingIds.Contains(e.Id)).ToListAsync();

        var all = created.Concat(extraEvents).ToList();
        var wire = await ToWireMapAsync(all);

        return Ok(new
        {
            Created = created.Select(e => wire[e.Id]).ToList(),
            Responses = responses
                .Where(r => wire.ContainsKey(r.EventId))
                .Select(r => new
                {
                    PlayerEvent = wire[r.EventId],
                    PlayerEventResponse = ResponseWire(r),
                })
                .ToList(),
        });
    }

    /// <summary>GET <c>api/playerevents/v1/{eventId}/responses</c> — the event
    /// detail page's attendee list. DFABBIDLDPE dispatches with verb 0
    /// (<c>CBKANFIOBCF.txt:1699</c>, route literal <c>:1689</c>) and reads
    /// <c>List&lt;JPIKCIGABBI&gt;</c>; the path was registered POST-only, so
    /// the list 405'd.</summary>
    [AllowAnonymous]
    [HttpGet("api/playerevents/v1/{eventId:long}/responses")]
    public async Task<ActionResult> ResponsesForEvent(long eventId)
    {
        var rows = await db.PlayerEventResponses
            .Where(r => r.EventId == eventId)
            .OrderBy(r => r.CreatedAt)
            .Take(500)
            .ToListAsync();
        return Ok(rows.Select(ResponseWire).ToList());
    }

    // ── Event instance browser ───────────────────────────────────────────

    /// <summary>GET <c>event/{eventId}/instances</c> — the "which instance of
    /// this event are my friends in" browser behind the event card's join
    /// button. Served from the MATCHMAKING-HOST root (no <c>api/</c> prefix),
    /// which is why it lives on an absolute route here even though the feature
    /// is a player-event one.
    ///
    /// Binary evidence: <c>Matchmaking+&lt;GetEventInstanceBrowser&gt;b__0</c>
    /// (<c>Matchmaking_NestedType_CBMHJMNIHNN.txt:14</c>) formats the literal
    /// <c>"event/{0}/instances"</c> at <c>:296</c> with <c>[rdi+16]</c> — the
    /// PlayerEventId off the HPIOAGDJHDH the lambda closes over — then news up
    /// BNDIAONDFFF with <c>rdx = 0</c> at <c>:124</c>, i.e.
    /// <c>BestHTTP.HTTPMethods.Get</c>. The declared return type is
    /// <c>FGLDKEJLAKB&lt;List&lt;PNDCMIMEJLD&gt;&gt;</c>, so the body is a BARE
    /// JSON ARRAY. PNDCMIMEJLD's getters (<c>PNDCMIMEJLD.txt:3-197</c>) are, in
    /// order, Int64/Int64/Int64/Boolean/DateTime/List&lt;Int32&gt;/String/Int32/
    /// Boolean/String = RoomInstanceId, RoomId, SubRoomId, IsFull, CreatedAt,
    /// PlayerIds, SubroomName, PlayerCount, HasModPresent, HashedInstanceId —
    /// byte-for-byte the SimpleRoomInstance the <c>room/{id}/instances</c>
    /// browser already returns (<c>Matchmaking.txt:14237</c> issues the room
    /// variant through the identical BNDIAONDFFF/verb-0 sequence). The last
    /// four are filled in locally on the watch and are not read back off the
    /// wire, so we emit the same six camelCase keys
    /// (<c>Controllers/Match/MatchController.cs:545-553</c>); the 2023 Utf8Json
    /// formatters carry a camelCase variant per key, so both client
    /// generations parse it.
    ///
    /// The event itself only supplies the room to browse: an event is pinned to
    /// one RoomId, plus the SubRoomId the host picked in the v2 create/edit body
    /// (persisted in <see cref="EventExtras"/>). Sub-room hops allocate their
    /// own instance ids and photon rooms, so when the event names a sub-room we
    /// only surface instances sitting in it.</summary>
    [AllowAnonymous]
    [HttpGet("/event/{eventId:long}/instances")]
    public async Task<IActionResult> EventInstances(long eventId)
    {
        var evt = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == eventId);
        if (evt is null) return NotFound();

        var extras = (await LoadExtrasAsync(new[] { evt }))[evt.Id];
        var subRoomId = extras.SubRoomId is long sub && sub > 0 ? sub : (long?)null;

        // Same sourcing as the room browser: presence rows are the live truth
        // (one instance per distinct PhotonRoomId), and the caller's OWN private
        // instances are merged in so the host can still see the instance they
        // created before anyone has joined it. Another player's private
        // PhotonRoomId is never listed.
        var pid = this.CurrentPlayerId() ?? 0;
        var privates = pid == 0
            ? new List<PrivateInstanceEntity>()
            : await db.PrivateInstances
                .Where(p => p.RoomId == evt.RoomId && p.OwnerPlayerId == pid)
                .ToListAsync();

        var byKey = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (room, playerIds) in presence.EnumerateActiveInstances(evt.RoomId))
        {
            if (string.IsNullOrEmpty(room.PhotonRoomId)) continue;
            if (subRoomId is long want && room.SubRoomId != want) continue;
            byKey[room.PhotonRoomId] = new
            {
                roomInstanceId = room.RoomInstanceId,
                roomId = room.RoomId,
                subRoomId = room.SubRoomId,
                // MaxCapacity is the per-instance cap the matchmaker handed
                // out, so this is a real answer rather than a constant false.
                isFull = room.MaxCapacity > 0 && playerIds.Count >= room.MaxCapacity,
                // Presence rows are a TTL cache with no creation timestamp —
                // there is no stored "instance opened at" for a live instance,
                // so report the observation time. Private-instance rows below
                // do carry a real CreatedAt and use it.
                createdAt = DateTime.UtcNow,
                playerIds = playerIds.Select(p => (int)p).ToArray(),
            };
        }

        foreach (var p in privates)
        {
            if (string.IsNullOrEmpty(p.PhotonRoomId) || byKey.ContainsKey(p.PhotonRoomId)) continue;
            if (subRoomId is long want && p.SubRoomId != want) continue;
            byKey[p.PhotonRoomId] = new
            {
                roomInstanceId = p.Id,
                roomId = p.RoomId,
                subRoomId = p.SubRoomId,
                isFull = false,
                createdAt = DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc),
                playerIds = Array.Empty<int>(),
            };
        }

        return Ok(byKey.Values.ToList());
    }

    // ── Wire shape ───────────────────────────────────────────────────────

    /// <summary>The PlayerEvent fields the 2023 client carries that
    /// <see cref="PlayerEventEntity"/> has no column for. Adding columns needs
    /// a migration in <c>Data/Entities</c>, so these live in the PlayerSettings
    /// side-table: one JSON row per event under
    /// <c>playerevent:{id}:wire</c>, plus a flat <c>playerevent:{id}:club</c>
    /// row that keeps club lookups a single indexed query.
    ///
    /// Public (not private) purely so System.Text.Json's reflection-based
    /// (de)serialiser can see the type and its accessors.</summary>
    public sealed class EventExtras
    {
        public long? SubRoomId { get; set; }
        public long? ClubId { get; set; }
        public string ImageName { get; set; } = string.Empty;
        /// <summary>BAMLHAODDOG ordinal. The enum's member order is not
        /// recoverable from the ISIL (enums carry no method bodies), so events
        /// that never had accessibility set keep the historical default of 1
        /// rather than guessing a new one; once the client PUTs a value we
        /// echo exactly what it sent.</summary>
        public int Accessibility { get; set; } = 1;
        public bool IsMultiInstance { get; set; }
        public bool SupportMultiInstanceRoomChat { get; set; }
        public int DefaultBroadcastPermissions { get; set; }
        public int CanRequestBroadcastPermissions { get; set; }
        public long? BroadcastingRoomInstanceId { get; set; }
        public List<string> Tags { get; set; } = [];
    }

    private static string ExtrasKey(long eventId) => $"playerevent:{eventId}:wire";
    private static string ClubKey(long eventId) => $"playerevent:{eventId}:club";

    /// <summary>Wire shape verified against the client's Utf8Json formatter
    /// <c>HPMMKFGDAEC.txt:1139-1518</c> (and the details variant
    /// <c>CENEMCMGDKG.txt:1191-1586</c>): PascalCase keys <c>PlayerEventId,
    /// CreatorPlayerId, RoomId, SubRoomId, ClubId, Name, Description,
    /// ImageName, StartTime, EndTime, AttendeeCount, Accessibility,
    /// IsMultiInstance, SupportMultiInstanceRoomChat,
    /// DefaultBroadcastPermissions, CanRequestBroadcastPermissions,
    /// BroadcastingRoomInstanceId</c> — property types read off the getters in
    /// <c>HPIOAGDJHDH.txt</c>. Server-side we store some of these under other
    /// names (Title, StartsAt, EndsAt), so remap explicitly.</summary>
    private static Dictionary<string, object?> ToWire(
        PlayerEventEntity ev, EventExtras extras, int attendeeCount) => new()
    {
        ["PlayerEventId"] = ev.Id,
        ["CreatorPlayerId"] = (int)ev.CreatorPlayerId,
        ["RoomId"] = ev.RoomId,
        ["SubRoomId"] = extras.SubRoomId,
        ["ClubId"] = extras.ClubId,
        ["Name"] = ev.Title,
        ["Description"] = ev.Description,
        ["ImageName"] = extras.ImageName,
        ["StartTime"] = ev.StartsAt,
        ["EndTime"] = ev.EndsAt,
        ["AttendeeCount"] = attendeeCount,
        ["Accessibility"] = extras.Accessibility,
        ["IsMultiInstance"] = extras.IsMultiInstance,
        ["SupportMultiInstanceRoomChat"] = extras.SupportMultiInstanceRoomChat,
        ["DefaultBroadcastPermissions"] = extras.DefaultBroadcastPermissions,
        ["CanRequestBroadcastPermissions"] = extras.CanRequestBroadcastPermissions,
        ["BroadcastingRoomInstanceId"] = extras.BroadcastingRoomInstanceId,
        // MDCBEPJCJPO (the includeDetails DTO) is this same object plus
        // Tags:[{Tag,Type}] (entry keys PCMLHLIBLNJ.txt:191-218). The tagless
        // formatter skips unknown keys, so emitting Tags unconditionally is
        // inert there and saves branching on includeDetails. Type is the
        // KIKIEHKBHNM ordinal — we only ever store free-form tags, so 0.
        ["Tags"] = extras.Tags.Select(t => new { Tag = t, Type = 0 }).ToList(),
    };

    /// <summary>JPIKCIGABBI — <c>{PlayerEventResponseId, PlayerEventId,
    /// PlayerId(Int32), CreatedAt, Type}</c>, keys at
    /// <c>DDDLCGDEPNH.txt:395-502</c>, types off the getters in
    /// <c>JPIKCIGABBI.txt</c>.</summary>
    private static object ResponseWire(PlayerEventResponseEntity r) => new
    {
        PlayerEventResponseId = r.Id,
        PlayerEventId = r.EventId,
        PlayerId = (int)r.PlayerId,
        r.CreatedAt,
        Type = r.Response,
    };

    /// <summary>PHHAKLPGNGC — the wrapper every v2 mutation deserialises.
    /// </summary>
    private static object Wrapper(PlayerEventEntity ev, EventExtras extras, int attendeeCount) => new
    {
        PlayerEvent = ToWire(ev, extras, attendeeCount),
        Result = 0, // BNCFHOOCHAI success
        TagModifyResult = new { Result = 0, Tags = extras.Tags },
    };

    private async Task<object> WrapperAsync(PlayerEventEntity ev)
    {
        var extras = (await LoadExtrasAsync(new[] { ev }))[ev.Id];
        var counts = await AttendeeCountsAsync(new[] { ev.Id });
        return Wrapper(ev, extras, counts.GetValueOrDefault(ev.Id));
    }

    private async Task<List<Dictionary<string, object?>>> ToWireManyAsync(IReadOnlyList<PlayerEventEntity> rows)
    {
        if (rows.Count == 0) return [];
        var extras = await LoadExtrasAsync(rows);
        var counts = await AttendeeCountsAsync(rows.Select(r => r.Id).ToList());
        return rows
            .Select(r => ToWire(r, extras[r.Id], counts.GetValueOrDefault(r.Id)))
            .ToList();
    }

    private async Task<Dictionary<long, Dictionary<string, object?>>> ToWireMapAsync(
        IReadOnlyList<PlayerEventEntity> rows)
    {
        var wire = await ToWireManyAsync(rows);
        var map = new Dictionary<long, Dictionary<string, object?>>();
        for (var i = 0; i < rows.Count; i++) map[rows[i].Id] = wire[i];
        return map;
    }

    private async Task<Dictionary<string, object?>> ToWireOneAsync(PlayerEventEntity ev)
        => (await ToWireManyAsync(new[] { ev }))[0];

    private async Task<Dictionary<long, EventExtras>> LoadExtrasAsync(IReadOnlyCollection<PlayerEventEntity> events)
    {
        var map = new Dictionary<long, EventExtras>();
        foreach (var ev in events) map[ev.Id] = new EventExtras();
        if (map.Count == 0) return map;

        var keys = map.Keys.Select(ExtrasKey).ToList();
        var rows = await db.PlayerSettings
            .AsNoTracking()
            .Where(s => keys.Contains(s.Key))
            .Select(s => new { s.Key, s.Value })
            .ToListAsync();

        foreach (var row in rows)
        {
            if (EventIdFromKey(row.Key) is not long id || !map.ContainsKey(id)) continue;
            var parsed = DeserializeExtras(row.Value);
            if (parsed is not null) map[id] = parsed;
        }
        return map;
    }

    private static long? EventIdFromKey(string key)
    {
        var parts = key.Split(':');
        return parts.Length >= 3 && long.TryParse(parts[1], out var id) ? id : null;
    }

    private static EventExtras? DeserializeExtras(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<EventExtras>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task SaveExtrasAsync(PlayerEventEntity evt, EventExtras extras)
    {
        // PlayerSettingEntity.Value is MaxLength(1024); bound the tag list so a
        // long one can never truncate the JSON blob into unparseable garbage.
        if (extras.Tags.Count > 12) extras.Tags = extras.Tags.Take(12).ToList();
        extras.Tags = extras.Tags.Select(t => Clamp(t, 32)).ToList();

        await UpsertSettingAsync(evt.CreatorPlayerId, ExtrasKey(evt.Id),
            JsonSerializer.Serialize(extras));
        // Flat index row so "events for club X" stays one indexed lookup
        // instead of a scan through the JSON blobs.
        await UpsertSettingAsync(evt.CreatorPlayerId, ClubKey(evt.Id),
            extras.ClubId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);

        evt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private async Task UpsertSettingAsync(long playerId, string key, string value)
    {
        var row = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == playerId && s.Key == key);
        if (row is null)
        {
            db.PlayerSettings.Add(new PlayerSettingEntity
            {
                PlayerId = playerId,
                Key = key,
                Value = value,
            });
        }
        else
        {
            row.Value = value;
        }
    }

    private async Task<Dictionary<long, int>> AttendeeCountsAsync(IReadOnlyCollection<long> eventIds)
    {
        if (eventIds.Count == 0) return new Dictionary<long, int>();
        var ids = eventIds.ToList();
        var rows = await db.PlayerEventResponses
            .Where(r => ids.Contains(r.EventId) && r.Response != NotGoingResponse)
            .GroupBy(r => r.EventId)
            .Select(g => new { EventId = g.Key, Count = g.Count() })
            .ToListAsync();
        return rows.ToDictionary(r => r.EventId, r => r.Count);
    }

    // ── Query shaping ────────────────────────────────────────────────────

    /// <summary>'scheduleFilter' (BJBLPLKMLBE) is an enum ordinal. Only the
    /// query-key literal survives in the ISIL — enums have no method bodies to
    /// disassemble — so the member order below is UNVERIFIED. Ordinal 0 (and
    /// absent) reproduces the previous behaviour exactly, and a wrong guess for
    /// the others still returns a valid, non-empty event list rather than an
    /// error.</summary>
    private static IQueryable<PlayerEventEntity> ApplyScheduleFilter(
        IQueryable<PlayerEventEntity> rows, int? scheduleFilter)
    {
        var now = DateTime.UtcNow;
        return scheduleFilter switch
        {
            1 => rows.Where(e => e.StartsAt <= now && e.EndsAt > now),
            2 => rows.Where(e => e.EndsAt > now && e.StartsAt < now.AddDays(1)),
            3 => rows.Where(e => e.EndsAt > now && e.StartsAt < now.AddDays(7)),
            _ => rows.Where(e => e.EndsAt > now),
        };
    }

    /// <summary>'sort' (PJBLEKMMACM) — same UNVERIFIED-ordinal caveat as
    /// <see cref="ApplyScheduleFilter"/>; 0/absent keeps soonest-first.</summary>
    private static IQueryable<PlayerEventEntity> ApplySort(
        IQueryable<PlayerEventEntity> rows, int? sort) => sort switch
    {
        1 => rows.OrderByDescending(e => e.CreatedAt),
        2 => rows.OrderBy(e => e.EndsAt),
        _ => rows.OrderBy(e => e.StartsAt),
    };

    /// <summary>Events attached to any of <paramref name="clubIds"/>. Explicit
    /// ClubId (persisted since the v2 create/edit body carries it) wins; when
    /// nothing carries one we fall back to the historical heuristic of "events
    /// in rooms owned by club members" so pre-existing data still populates the
    /// tab.</summary>
    private async Task<List<PlayerEventEntity>> ClubEventsAsync(
        IReadOnlyCollection<long> clubIds, int skip, int take)
    {
        var explicitIds = await EventIdsForClubsAsync(clubIds);
        IQueryable<PlayerEventEntity> q;
        if (explicitIds.Count > 0)
        {
            q = db.PlayerEvents.Where(e => explicitIds.Contains(e.Id) && e.EndsAt > DateTime.UtcNow);
        }
        else
        {
            var roomIds = await ClubRoomIdsAsync(clubIds);
            if (roomIds.Count == 0) return [];
            q = db.PlayerEvents.Where(e => roomIds.Contains(e.RoomId) && e.EndsAt > DateTime.UtcNow);
        }

        return await q.OrderBy(e => e.StartsAt).Skip(skip).Take(take).ToListAsync();
    }

    private async Task<List<long>> EventIdsForClubsAsync(IReadOnlyCollection<long> clubIds)
    {
        if (clubIds.Count == 0) return [];
        var wanted = clubIds.Select(c => c.ToString(CultureInfo.InvariantCulture)).ToList();
        var keys = await db.PlayerSettings
            .AsNoTracking()
            .Where(s => wanted.Contains(s.Value)
                     && s.Key.StartsWith("playerevent:")
                     && s.Key.EndsWith(":club"))
            .Select(s => s.Key)
            .ToListAsync();

        return keys
            .Select(EventIdFromKey)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
    }

    private async Task<List<long>> ClubRoomIdsAsync(IReadOnlyCollection<long> clubIds)
    {
        var memberships = db.ClubMemberships.AsQueryable();
        if (clubIds.Count > 0)
        {
            var ids = clubIds.ToList();
            memberships = memberships.Where(m => ids.Contains(m.ClubId));
        }

        var memberIds = await memberships.Select(m => m.PlayerId).Distinct().ToListAsync();
        if (memberIds.Count == 0) return [];
        return await db.Rooms
            .Where(r => memberIds.Contains(r.CreatorPlayerId))
            .Select(r => r.Id)
            .ToListAsync();
    }

    // ── Create / edit (v2) ───────────────────────────────────────────────

    /// <summary>POST <c>api/playerevents/v2</c> — the 2023 create-event flow.
    /// KKMMBCDGFFN posts BIEFKAOABMP as a RawJsonForm body (verb 2 at
    /// <c>CBKANFIOBCF.txt:2390</c>) whose 14 keys are
    /// <c>RoomId, SubRoomId, ClubId, Name, Description, Tags, ImageName,
    /// StartTime, EndTime, Accessibility, IsMultiInstance,
    /// SupportMultiInstanceRoomChat, DefaultBroadcastPermissions,
    /// CanRequestBroadcastPermissions</c> (<c>ACKNGEEHHAE.txt:935-1234</c>),
    /// and reads the PHHAKLPGNGC wrapper back. Only <c>[HttpGet]</c> was
    /// registered on this path, so creating an event 405'd.</summary>
    [HttpPost("api/playerevents/v2")]
    public async Task<IActionResult> CreateV2()
    {
        var name = (await ReadStringFieldAsync("Name", "name", "title"))?.Trim() ?? string.Empty;
        if (name.Length == 0) return BadRequest("missing_name");

        var start = await ReadDateFieldAsync("StartTime", "startTime", "startsAt") ?? DateTime.UtcNow;
        var end = await ReadDateFieldAsync("EndTime", "endTime", "endsAt") ?? start.AddHours(1);
        var description = (await ReadStringFieldAsync("Description", "description"))?.Trim() ?? string.Empty;

        var evt = new PlayerEventEntity
        {
            CreatorPlayerId = Me,
            RoomId = await ReadLongFieldAsync("RoomId", "roomId") ?? 0,
            Title = Clamp(name, 128),
            Description = Clamp(description, 2000),
            StartsAt = start,
            EndsAt = end > start ? end : start.AddHours(1),
        };
        db.PlayerEvents.Add(evt);
        await db.SaveChangesAsync();

        var extras = new EventExtras();
        await ApplyBodyToExtrasAsync(extras);
        await SaveExtrasAsync(evt, extras);
        return Ok(await WrapperAsync(evt));
    }

    /// <summary>POST <c>api/playerevents/v2/{eventId}</c> — edit-event save.
    /// IOEJKIPJFOI posts the same BIEFKAOABMP body with verb 2
    /// (<c>CBKANFIOBCF.txt:2595</c>, route literal <c>:2579</c>); the path was
    /// GET-only, so editing 405'd.</summary>
    [HttpPost("api/playerevents/v2/{eventId:long}")]
    public async Task<IActionResult> EditV2(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();

        var name = (await ReadStringFieldAsync("Name", "name", "title"))?.Trim();
        if (!string.IsNullOrEmpty(name)) evt.Title = Clamp(name, 128);

        var description = (await ReadStringFieldAsync("Description", "description"))?.Trim();
        if (description is not null) evt.Description = Clamp(description, 2000);

        var roomId = await ReadLongFieldAsync("RoomId", "roomId");
        if (roomId is long room && room > 0) evt.RoomId = room;

        var start = await ReadDateFieldAsync("StartTime", "startTime", "startsAt");
        var end = await ReadDateFieldAsync("EndTime", "endTime", "endsAt");
        if (start is DateTime s) evt.StartsAt = s;
        if (end is DateTime e) evt.EndsAt = e > evt.StartsAt ? e : evt.StartsAt.AddHours(1);

        var extras = (await LoadExtrasAsync(new[] { evt }))[evt.Id];
        await ApplyBodyToExtrasAsync(extras);
        await SaveExtrasAsync(evt, extras);
        await NotifyAttendeesAsync(evt, PushNotificationId.PlayerEventUpdated);
        return Ok(await WrapperAsync(evt));
    }

    /// <summary>Copies whatever subset of the BIEFKAOABMP body is present onto
    /// <paramref name="extras"/>. Absent keys are left alone so the same helper
    /// serves both create and edit.</summary>
    private async Task ApplyBodyToExtrasAsync(EventExtras extras)
    {
        var fields = await ReadBodyAsync();

        if (fields.Fields.ContainsKey("SubRoomId"))
            extras.SubRoomId = await ReadLongFieldAsync("SubRoomId", "subRoomId");
        if (fields.Fields.ContainsKey("ClubId"))
            extras.ClubId = await ReadLongFieldAsync("ClubId", "clubId");

        var imageName = await ReadStringFieldAsync("ImageName", "imageName");
        if (imageName is not null) extras.ImageName = Clamp(imageName.Trim(), 128);

        if (await ReadIntFieldAsync("Accessibility", "accessibility") is int accessibility)
            extras.Accessibility = accessibility;
        if (await ReadBoolFieldAsync("IsMultiInstance", "isMultiInstance") is bool multi)
            extras.IsMultiInstance = multi;
        if (await ReadBoolFieldAsync("SupportMultiInstanceRoomChat", "supportMultiInstanceRoomChat",
                "supportsMultiInstanceRoomChat") is bool chat)
            extras.SupportMultiInstanceRoomChat = chat;
        if (await ReadIntFieldAsync("DefaultBroadcastPermissions", "defaultBroadcastPermissions") is int def)
            extras.DefaultBroadcastPermissions = def;
        if (await ReadIntFieldAsync("CanRequestBroadcastPermissions", "canRequestBroadcastPermissions") is int req)
            extras.CanRequestBroadcastPermissions = req;

        var tags = await ReadStringListFieldAsync("Tags", "tags");
        if (tags is not null) extras.Tags = tags;
    }

    // ── Create / RSVP (v1, shared with the 2020 client) ──────────────────

    public sealed record CreateEventRequest(
        string Title, string? Description, long RoomId,
        DateTime StartsAt, DateTime EndsAt, int Capacity);

    [HttpPost("api/playerevents/v1/create")]
    [HttpPost("api/playerevents/v1")]
    public async Task<ActionResult> Create([FromBody] CreateEventRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest("missing_title");
        var ev = new PlayerEventEntity
        {
            CreatorPlayerId = Me,
            RoomId = req.RoomId,
            Title = req.Title.Trim(),
            Description = (req.Description ?? string.Empty).Trim(),
            StartsAt = req.StartsAt,
            EndsAt = req.EndsAt > req.StartsAt ? req.EndsAt : req.StartsAt.AddHours(1),
            Capacity = Math.Max(0, req.Capacity),
        };
        db.PlayerEvents.Add(ev);
        await db.SaveChangesAsync();
        return Ok(await ToWireOneAsync(ev));
    }

    public sealed record RsvpRequest(int Response);

    [HttpPost("api/playerevents/v1/{eventId:long}/rsvp")]
    public async Task<ActionResult> Rsvp(long eventId, [FromBody] RsvpRequest body)
    {
        if (body.Response < 0 || body.Response > 2)
            return BadRequest("invalid_response");
        var ev = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev is null) return NotFound();

        var row = await db.PlayerEventResponses
            .FirstOrDefaultAsync(r => r.EventId == eventId && r.PlayerId == Me);
        if (row is null)
        {
            row = new PlayerEventResponseEntity { EventId = eventId, PlayerId = Me };
            db.PlayerEventResponses.Add(row);
        }
        row.Response = body.Response;
        await db.SaveChangesAsync();

        // Tell the creator someone RSVP'd. They can ignore but the
        // watch will refresh their event detail card.
        if (ev.CreatorPlayerId != Me)
            await notifications.NotifyAsync(ev.CreatorPlayerId,
                PushNotificationId.PlayerEventResponseChanged,
                new { ev.Id, From = Me, body.Response });
        // Left as the historical shape on purpose: this route is 2020-only
        // (the 2023 client GETs .../responses and POSTs v1/respond instead),
        // so there is no 2023 DTO to match here.
        return Ok(new { row.EventId, row.PlayerId, row.Response });
    }

    /// <summary>POST <c>api/playerevents/v1/{eventId}/responses</c> —
    /// alternate RSVP route the 2020 watch uses for the response-list
    /// flow (older / sub-room view). Body is the same RsvpRequest
    /// as <c>v1/{id}/rsvp</c>; behaves identically. The 2023 client GETs this
    /// path instead — see <see cref="ResponsesForEvent"/>.</summary>
    [HttpPost("api/playerevents/v1/{eventId:long}/responses")]
    public Task<ActionResult> RespondList(long eventId, [FromBody] RsvpRequest body)
        => Rsvp(eventId, body);

    /// <summary>POST <c>api/playerevents/v1/respond</c> — set the caller's RSVP
    /// state. Two client generations share this route with different
    /// contracts:
    ///   * 2020.12 posts a form and reads a BARE int (DGOPHENCPOC); returning
    ///     an object throws InvalidCastException there — see
    ///     <c>recroom-2020-client-response-contracts.md</c>.
    ///   * 2023-03 posts a RawJsonForm body <c>{PlayerEventId, Type}</c>
    ///     (NKIFBKJALEJ, keys at <c>GFHNJMMBHFD.txt:203-238</c>; body build at
    ///     <c>CBKANFIOBCF_NestedType_NFCEOKLHDAG.txt:81-112</c>) and reads
    ///     CEKABGOIOAF, an OBJECT <c>{Result:int}</c>
    ///     (<c>GJJGDENPOAP.txt:139-150</c>).
    /// Branch the response shape on the body encoding the caller actually
    /// used — that is the only signal that distinguishes them.</summary>
    [HttpPost("api/playerevents/v1/respond")]
    public async Task<IActionResult> RespondForm()
    {
        var legacyForm = Request.HasFormContentType;
        var eventId = await ReadLongFieldAsync("PlayerEventId", "playerEventId", "EventId", "eventId");
        if (eventId is not long evt || evt <= 0) return RespondResult(legacyForm, 2 /*NoSuchEvent*/);

        var response = await ReadIntFieldAsync("Type", "type", "Response", "response") ?? 0;
        if (!await db.PlayerEvents.AnyAsync(e => e.Id == evt))
            return RespondResult(legacyForm, 2 /*NoSuchEvent*/);

        var existing = await db.PlayerEventResponses
            .FirstOrDefaultAsync(r => r.PlayerId == Me && r.EventId == evt);
        if (existing is null)
            db.PlayerEventResponses.Add(new PlayerEventResponseEntity { PlayerId = Me, EventId = evt, Response = response });
        else
            existing.Response = response;
        await db.SaveChangesAsync();
        return RespondResult(legacyForm, 0 /*Success*/);
    }

    /// <summary>POST <c>api/playerevents/v1/deleteResponse</c> — drop the
    /// caller's RSVP row. Same two-generation split as
    /// <see cref="RespondForm"/>. The 2023 body is
    /// <c>JsonUtility.ToJson(RecNet.Events.DeleteResponseRequest)</c>
    /// (<c>CBKANFIOBCF_NestedType_MDJMHFHPOBO.txt:65-78</c>) whose single field
    /// is name-preserved: <c>public long PlayerEventId</c>
    /// (2023.06.21 <c>dump.cs:1291301</c>).</summary>
    [HttpPost("api/playerevents/v1/deleteResponse")]
    public async Task<IActionResult> DeleteResponseForm()
    {
        var legacyForm = Request.HasFormContentType;
        var eventId = await ReadLongFieldAsync("PlayerEventId", "playerEventId", "EventId", "eventId");
        if (eventId is not long evt) return RespondResult(legacyForm, 2 /*NoSuchEvent*/);

        await db.PlayerEventResponses
            .Where(r => r.PlayerId == Me && r.EventId == evt)
            .ExecuteDeleteAsync();
        return RespondResult(legacyForm, 0 /*Success*/);
    }

    private IActionResult RespondResult(bool legacyForm, int code)
        => legacyForm ? Ok(code) : Ok(new { Result = code });

    /// <summary>DELETE/POST <c>api/playerevents/v2/delete/{id}</c> — hard
    /// delete an event the caller created. GJMCEDALKJO dispatches with verb 2
    /// (<c>CBKANFIOBCF.txt:4166</c>, route literal <c>:4156</c>) and reads the
    /// PHHAKLPGNGC wrapper, so POST plus the wrapper body are both required;
    /// DELETE stays for the 2020 client.</summary>
    [HttpPost("api/playerevents/v2/delete/{id:long}")]
    [HttpDelete("api/playerevents/v2/delete/{id:long}")]
    public async Task<IActionResult> DeleteByPath(long id)
    {
        var evt = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == id && e.CreatorPlayerId == Me);
        if (evt is null) return NotFound();

        // Snapshot the wire shape before the rows go away — the wrapper still
        // has to echo the event that was deleted.
        var extras = (await LoadExtrasAsync(new[] { evt }))[id];
        var attendees = (await AttendeeCountsAsync(new[] { id })).GetValueOrDefault(id);

        db.PlayerEvents.Remove(evt);
        await db.PlayerEventResponses.Where(r => r.EventId == id).ExecuteDeleteAsync();
        await db.PlayerSettings
            .Where(s => s.Key == ExtrasKey(id) || s.Key == ClubKey(id))
            .ExecuteDeleteAsync();
        await db.SaveChangesAsync();
        return Ok(Wrapper(evt, extras, attendees));
    }

    // ── Field mutations (v2) ─────────────────────────────────────────────
    // Every one of these is a PUT (verb 3, e.g. CBKANFIOBCF.txt:2744) whose
    // fields arrive as x-www-form-urlencoded, and every one returns the
    // PHHAKLPGNGC wrapper — a flat event body leaves the client's PlayerEvent
    // null right after a successful save. POST is kept alongside for the 2020
    // client.

    [HttpPost("api/playerevents/v2/{eventId:long}/name")]
    [HttpPut("api/playerevents/v2/{eventId:long}/name")]
    public async Task<IActionResult> UpdateName(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var value = await ReadStringFieldAsync("name", "Name", "value");
        if (string.IsNullOrWhiteSpace(value)) return BadRequest("missing_name");
        evt.Title = Clamp(value.Trim(), 128);
        evt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(await WrapperAsync(evt));
    }

    [HttpPost("api/playerevents/v2/{eventId:long}/description")]
    [HttpPut("api/playerevents/v2/{eventId:long}/description")]
    public async Task<IActionResult> UpdateDescription(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var value = await ReadStringFieldAsync("description", "Description", "value") ?? string.Empty;
        evt.Description = Clamp(value.Trim(), 2000);
        evt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(await WrapperAsync(evt));
    }

    /// <summary>PUT <c>api/playerevents/v2/{id}/room</c> — FPPNHCMPAKK sends
    /// <c>roomId</c> AND an optional <c>subRoomId</c>
    /// (<c>CBKANFIOBCF_NestedType_AKPPDNLCACI.txt:88,101</c>); dropping the
    /// latter meant the event never pointed at the sub-room the host picked.
    /// </summary>
    [HttpPost("api/playerevents/v2/{eventId:long}/room")]
    [HttpPut("api/playerevents/v2/{eventId:long}/room")]
    public async Task<IActionResult> UpdateRoom(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var roomId = await ReadLongFieldAsync("roomId", "RoomId", "value");
        if (roomId is not long id || id <= 0) return BadRequest("missing_room");
        if (!await db.Rooms.AnyAsync(r => r.Id == id)) return NotFound("room_not_found");
        evt.RoomId = id;

        var extras = (await LoadExtrasAsync(new[] { evt }))[evt.Id];
        extras.SubRoomId = await ReadLongFieldAsync("subRoomId", "SubRoomId");
        await SaveExtrasAsync(evt, extras);
        return Ok(await WrapperAsync(evt));
    }

    [HttpPost("api/playerevents/v2/{eventId:long}/time")]
    [HttpPut("api/playerevents/v2/{eventId:long}/time")]
    public async Task<IActionResult> UpdateTime(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var start = await ReadDateFieldAsync("startTime", "StartTime", "startsAt", "StartsAt");
        var end = await ReadDateFieldAsync("endTime", "EndTime", "endsAt", "EndsAt");
        if (start is DateTime s) evt.StartsAt = s;
        if (end is DateTime e) evt.EndsAt = e > evt.StartsAt ? e : evt.StartsAt.AddHours(1);
        if (start is null && end is null) return BadRequest("missing_time");
        evt.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(await WrapperAsync(evt));
    }

    [HttpPost("api/playerevents/v2/{eventId:long}/image")]
    [HttpPut("api/playerevents/v2/{eventId:long}/image")]
    public async Task<IActionResult> UpdateImage(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var value = await ReadStringFieldAsync("imageName", "ImageName", "value") ?? string.Empty;
        var extras = (await LoadExtrasAsync(new[] { evt }))[evt.Id];
        extras.ImageName = Clamp(value.Trim(), 128);
        await SaveExtrasAsync(evt, extras);
        return Ok(await WrapperAsync(evt));
    }

    [HttpPost("api/playerevents/v2/{eventId:long}/accessibility")]
    [HttpPut("api/playerevents/v2/{eventId:long}/accessibility")]
    public async Task<IActionResult> UpdateAccessibility(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var value = await ReadIntFieldAsync("accessibility", "Accessibility", "value");
        if (value is not int accessibility) return BadRequest("missing_accessibility");
        var extras = (await LoadExtrasAsync(new[] { evt }))[evt.Id];
        extras.Accessibility = accessibility;
        await SaveExtrasAsync(evt, extras);
        return Ok(await WrapperAsync(evt));
    }

    /// <summary>PUT <c>api/playerevents/v2/{id}/tags</c>. JHLMDMGELFD does NOT
    /// send form fields — it serialises <c>List&lt;String&gt;</c> straight into
    /// a RawJsonForm body, i.e. a BARE JSON ARRAY
    /// (<c>CBKANFIOBCF_NestedType_PNFGKCKCGEL.txt:63-81</c>). The old field
    /// reader only looked inside JSON objects, so every save stored an empty
    /// string and silently cleared the tags. The wrapper's
    /// <c>TagModifyResult.Tags</c> is what the client re-reads afterwards.
    /// </summary>
    [HttpPost("api/playerevents/v2/{eventId:long}/tags")]
    [HttpPut("api/playerevents/v2/{eventId:long}/tags")]
    public async Task<IActionResult> UpdateTags(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var tags = await ReadStringListFieldAsync("tags", "Tags", "value") ?? new List<string>();
        var extras = (await LoadExtrasAsync(new[] { evt }))[evt.Id];
        extras.Tags = tags;
        await SaveExtrasAsync(evt, extras);
        return Ok(await WrapperAsync(evt));
    }

    /// <summary>PUT <c>api/playerevents/v2/{id}/multiinstance</c>. IEGDKKAKHBD
    /// sends FOUR form fields — <c>isMultiInstance</c>,
    /// <c>supportsMultiInstanceRoomChat</c>, <c>defaultBroadcastPermissions</c>,
    /// <c>canRequestBroadcastPermissions</c>
    /// (<c>CBKANFIOBCF_NestedType_EONGJBMLEFO.txt:122-161</c>). The handler used
    /// to look for a field named <c>multiInstance</c>, which the client never
    /// sends, so the toggle always stored false and the other three were
    /// dropped. Note the request spells it "supports…" while the response key
    /// is "Support…" — both spellings are accepted here.</summary>
    [HttpPost("api/playerevents/v2/{eventId:long}/multiinstance")]
    [HttpPut("api/playerevents/v2/{eventId:long}/multiinstance")]
    public async Task<IActionResult> UpdateMultiInstance(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var extras = (await LoadExtrasAsync(new[] { evt }))[evt.Id];

        extras.IsMultiInstance =
            await ReadBoolFieldAsync("isMultiInstance", "IsMultiInstance", "multiInstance", "value") ?? false;
        extras.SupportMultiInstanceRoomChat =
            await ReadBoolFieldAsync("supportsMultiInstanceRoomChat", "supportMultiInstanceRoomChat",
                "SupportMultiInstanceRoomChat") ?? false;
        extras.DefaultBroadcastPermissions =
            await ReadIntFieldAsync("defaultBroadcastPermissions", "DefaultBroadcastPermissions") ?? 0;
        extras.CanRequestBroadcastPermissions =
            await ReadIntFieldAsync("canRequestBroadcastPermissions", "CanRequestBroadcastPermissions") ?? 0;

        await SaveExtrasAsync(evt, extras);
        return Ok(await WrapperAsync(evt));
    }

    /// <summary>PUT <c>api/playerevents/v2/{id}/club</c>. JLHJHGANFDM takes a
    /// <c>Nullable&lt;Int64&gt;</c> and boxes it into the single form field
    /// <c>clubId</c> (<c>CBKANFIOBCF_NestedType_DJLDOCJBFPO.txt:60-71</c>) — a
    /// null therefore arrives as an absent/empty field, which is the client's
    /// only way to DETACH the club. Rejecting that with 400 made detaching
    /// impossible.</summary>
    [HttpPost("api/playerevents/v2/{eventId:long}/club")]
    [HttpPut("api/playerevents/v2/{eventId:long}/club")]
    public async Task<IActionResult> UpdateClub(long eventId)
    {
        var evt = await GetOwnedEventAsync(eventId);
        if (evt is null) return NotFound();
        var extras = (await LoadExtrasAsync(new[] { evt }))[evt.Id];

        var clubId = await ReadLongFieldAsync("clubId", "ClubId", "value");
        if (clubId is long id && id > 0)
        {
            if (!await db.ClubMemberships.AnyAsync(m => m.ClubId == id && m.PlayerId == Me))
                return Forbid();
            extras.ClubId = id;
        }
        else
        {
            extras.ClubId = null;
        }

        await SaveExtrasAsync(evt, extras);
        return Ok(await WrapperAsync(evt));
    }

    [HttpDelete("api/playerevents/v1/{eventId:long}")]
    public async Task<ActionResult> Delete(long eventId)
    {
        var ev = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == eventId);
        if (ev is null) return NotFound();
        if (ev.CreatorPlayerId != Me) return Forbid();
        db.PlayerEvents.Remove(ev);
        // Cascade-clean the responses; SQLite has no FK cascade
        // configured here so do it manually.
        var responses = db.PlayerEventResponses.Where(r => r.EventId == eventId);
        db.PlayerEventResponses.RemoveRange(responses);
        await db.SaveChangesAsync();
        await db.PlayerSettings
            .Where(s => s.Key == ExtrasKey(eventId) || s.Key == ClubKey(eventId))
            .ExecuteDeleteAsync();
        return Ok();
    }

    // ── Bulk-invite ──────────────────────────────────────────────────────

    public sealed class BulkInviteRequest
    {
        public long PlayerEventId { get; set; }
        public List<int>? InvitedPlayerIds { get; set; }
    }

    /// <summary>POST <c>/api/playerevents/v1/bulkInvite</c>. Request is
    /// <c>JsonUtility.ToJson(RecNet.Events.BulkInviteRequest)</c> —
    /// name-preserved fields <c>PlayerEventId</c> / <c>InvitedPlayerIds</c>
    /// (2023.06.21 <c>dump.cs:1288323-1288324</c>, body build at
    /// <c>CBKANFIOBCF_NestedType_MIKAHKKCIEE.txt:69-96</c>). Response is
    /// IDELKAJIOLI <c>{FailedInvites, Result}</c>
    /// (<c>GAODOHODBDE.txt:203-238</c>) where each failed entry is
    /// <c>{InvitedPlayerId, Result}</c> (<c>MOJLGDKFECO.txt:203-238</c>) — the
    /// entries used to be <c>{PlayerId, Error}</c> and deserialised as all
    /// defaults. Error paths return the same JSON shape rather than a
    /// NotFound()/Forbid() body the client cannot parse.</summary>
    [HttpPost("api/playerevents/v1/bulkInvite")]
    public async Task<IActionResult> BulkInvite([FromBody] BulkInviteRequest req)
    {
        if (req?.InvitedPlayerIds is null || req.InvitedPlayerIds.Count == 0)
            return Ok(new { FailedInvites = Array.Empty<object>(), Result = 0 });

        var ev = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == req.PlayerEventId);
        // BNCFHOOCHAI's member order isn't recoverable (enums have no method
        // bodies to disassemble); any non-zero code reads as "failed" on the
        // client, which is all the invite sheet branches on.
        if (ev is null) return Ok(new { FailedInvites = Array.Empty<object>(), Result = 1 });
        if (ev.CreatorPlayerId != Me) return Ok(new { FailedInvites = Array.Empty<object>(), Result = 1 });

        var failed = new List<object>();
        var sender = Me;
        var inviteText = $"You're invited to '{ev.Title}' starting {ev.StartsAt:u}.";
        foreach (var rid in req.InvitedPlayerIds.Distinct())
        {
            var exists = await db.Players.AnyAsync(p => p.Id == rid);
            if (!exists)
            {
                failed.Add(new { InvitedPlayerId = rid, Result = 1 });
                continue;
            }
            db.Messages.Add(new MessageEntity
            {
                SenderPlayerId = sender,
                RecipientPlayerId = rid,
                Body = inviteText,
            });
            await notifications.NotifyAsync(rid,
                PushNotificationId.PlayerEventCreated,
                new { ev.Id, ev.Title, ev.StartsAt, From = sender });
        }
        await db.SaveChangesAsync();
        return Ok(new { FailedInvites = failed, Result = 0 });
    }

    /// <summary>POST <c>api/playerevents/v1/broadcast</c> — set or clear the
    /// room instance an event is being broadcast from. JBHPMONELAO posts
    /// <c>RecNet.Events.BroadcastRoomInstanceRequest</c>
    /// (<c>CBKANFIOBCF_NestedType_EHHMGKFGGCC.txt:81</c>) whose JSON keys are
    /// <c>PlayerEventId</c> and the nullable <c>BroadcastRoomInstanceId</c>
    /// (<c>EOFEKNIHNNC.txt:215-258</c>), and reads the PHHAKLPGNGC wrapper.
    /// This used to be implemented as a text-message blast to every RSVP,
    /// which both spammed attendees and returned an unparseable body.</summary>
    [HttpPost("api/playerevents/v1/broadcast")]
    public async Task<IActionResult> Broadcast()
    {
        var eventId = await ReadLongFieldAsync("PlayerEventId", "playerEventId", "EventId", "eventId");
        if (eventId is not long id) return BadRequest("missing_event");
        var evt = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == id);
        if (evt is null) return NotFound();
        if (evt.CreatorPlayerId != Me) return Forbid();

        var extras = (await LoadExtrasAsync(new[] { evt }))[evt.Id];
        extras.BroadcastingRoomInstanceId = await ReadLongFieldAsync(
            "BroadcastRoomInstanceId", "broadcastRoomInstanceId",
            "BroadcastingRoomInstanceId", "broadcastingRoomInstanceId");
        await SaveExtrasAsync(evt, extras);

        // Attendees' event cards key off BroadcastingRoomInstanceId, so nudge
        // them to re-fetch instead of sending a chat message.
        await NotifyAttendeesAsync(evt, PushNotificationId.PlayerEventStateChanged);
        return Ok(await WrapperAsync(evt));
    }

    private async Task NotifyAttendeesAsync(PlayerEventEntity evt, PushNotificationId id)
    {
        var recipients = await db.PlayerEventResponses
            .Where(r => r.EventId == evt.Id && r.Response != NotGoingResponse)
            .Select(r => r.PlayerId)
            .Distinct()
            .ToListAsync();
        foreach (var playerId in recipients.Where(p => p != evt.CreatorPlayerId))
            await notifications.NotifyAsync(playerId, id, new { PlayerEventId = evt.Id, evt.Title });
    }

    // ── Report ───────────────────────────────────────────────────────────

    public sealed class PlayerEventReportRequest
    {
        public int ReportCategory { get; set; }
        public long PlayerEventId { get; set; }
        public string? Details { get; set; }
    }

    /// <summary>POST <c>api/playerevents/v1/report</c> — file a
    /// moderation report against an event. Wire response is
    /// <c>KLAMKCBENEA{Success, Message}</c>; returning a bare
    /// <c>{Reported:true}</c> throws on the strict deserialiser.</summary>
    [HttpPost("api/playerevents/v1/report")]
    public async Task<IActionResult> Report([FromBody] PlayerEventReportRequest req)
    {
        var ev = await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == req.PlayerEventId);
        if (ev is null) return Ok(new { Success = false, Message = "no_such_event" });
        var msg = (req.Details ?? string.Empty).Trim();
        db.Reports.Add(new ReportEntity
        {
            ReporterPlayerId = Me,
            TargetPlayerId = ev.CreatorPlayerId,
            TargetEventId = req.PlayerEventId,
            Category = req.ReportCategory,
            Message = msg[..Math.Min(1000, msg.Length)],
        });
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Message = string.Empty });
    }

    // ── Request parsing ──────────────────────────────────────────────────

    private static string Clamp(string value, int max)
        => value.Length <= max ? value : value[..max];

    private async Task<PlayerEventEntity?> GetOwnedEventAsync(long eventId)
        => await db.PlayerEvents.FirstOrDefaultAsync(e => e.Id == eventId && e.CreatorPlayerId == Me);

    private async Task<List<long>> ReadEventIdsAsync()
    {
        var ids = new List<long>();

        foreach (var value in Request.Query.SelectMany(q => q.Value))
            AddDelimitedIds(ids, value);

        var body = await ReadBodyAsync();
        foreach (var key in new[] { "playerEventIds", "eventIds", "ids" })
        {
            if (body.Repeated.TryGetValue(key, out var values))
                foreach (var value in values) AddDelimitedIds(ids, value);
        }
        if (body.RootArray is not null)
            foreach (var value in body.RootArray) AddDelimitedIds(ids, value);

        return ids.Distinct().Take(200).ToList();
    }

    private static void AddDelimitedIds(List<long> ids, string? value)
    {
        foreach (var part in (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(part, out var id) && id > 0) ids.Add(id);
        }
    }

    private async Task<string?> ReadStringFieldAsync(params string[] names)
    {
        var body = await ReadBodyAsync();
        foreach (var name in names)
            if (body.Fields.TryGetValue(name, out var value))
                return value;
        return null;
    }

    /// <summary>Reads a list-valued field. Handles all three encodings the
    /// client uses: a bare JSON array body (the tags PUT), a JSON array nested
    /// under a key (the v2 create/edit body's <c>Tags</c>), and repeated /
    /// comma-joined form values.</summary>
    private async Task<List<string>?> ReadStringListFieldAsync(params string[] names)
    {
        var body = await ReadBodyAsync();
        if (body.RootArray is not null) return body.RootArray;

        foreach (var name in names)
        {
            if (body.Repeated.TryGetValue(name, out var repeated) && repeated.Count > 1)
                return repeated.Select(v => v.Trim()).Where(v => v.Length > 0).ToList();

            if (!body.Fields.TryGetValue(name, out var raw)) continue;
            raw = raw.Trim();
            if (raw.Length == 0) return new List<string>();
            if (raw.StartsWith('['))
            {
                var parsed = ParseJsonStringArray(raw);
                if (parsed is not null) return parsed;
            }
            return raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
        return null;
    }

    private static List<string>? ParseJsonStringArray(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            return ElementToStrings(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> ElementToStrings(JsonElement array)
    {
        var list = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText();
            if (!string.IsNullOrWhiteSpace(value)) list.Add(value.Trim());
        }
        return list;
    }

    private async Task<long?> ReadLongFieldAsync(params string[] names)
    {
        var value = await ReadStringFieldAsync(names);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private async Task<int?> ReadIntFieldAsync(params string[] names)
    {
        var value = await ReadStringFieldAsync(names);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private async Task<bool?> ReadBoolFieldAsync(params string[] names)
    {
        var value = await ReadStringFieldAsync(names);
        if (bool.TryParse(value, out var parsed)) return parsed;
        // Form-encoded booleans arrive as "True"/"False" from the client's
        // boxed-bool ToString, but a JSON body may carry 1/0.
        return value switch { "1" => true, "0" => false, _ => null };
    }

    private async Task<DateTime?> ReadDateFieldAsync(params string[] names)
    {
        var value = await ReadStringFieldAsync(names);
        // The client always sends UTC (Utf8Json ISO-8601 with a Z, or
        // DateTime.ToString() on an already-UTC value). Force the parse to land
        // on Kind=Utc so it compares correctly against DateTime.UtcNow.
        return DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Everything the request carries, read once and cached — the body
    /// stream can only be consumed a single time.</summary>
    private sealed class ParsedBody
    {
        public Dictionary<string, string> Fields { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Repeated { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Set when the whole body is a bare JSON array — the shape
        /// the tags PUT sends.</summary>
        public List<string>? RootArray { get; set; }
    }

    private const string BodyItemKey = "__playerevent_body";

    private async Task<ParsedBody> ReadBodyAsync()
    {
        if (HttpContext.Items.TryGetValue(BodyItemKey, out var cached) && cached is ParsedBody existing)
            return existing;

        var body = new ParsedBody();
        foreach (var pair in Request.Query)
        {
            body.Fields[pair.Key] = pair.Value.FirstOrDefault() ?? string.Empty;
            body.Repeated[pair.Key] = pair.Value.Where(v => v is not null).Select(v => v!).ToList();
        }

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var pair in form)
            {
                body.Fields[pair.Key] = pair.Value.FirstOrDefault() ?? string.Empty;
                body.Repeated[pair.Key] = pair.Value.Where(v => v is not null).Select(v => v!).ToList();
            }
        }
        else if ((Request.ContentLength ?? 0) > 0)
        {
            // Read the raw text rather than gating on Content-Type: the client's
            // RawJsonForm does set application/json, but the JSON path is also
            // the fallback for anything else that isn't a form.
            using var reader = new StreamReader(
                Request.Body, System.Text.Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var raw = await reader.ReadToEndAsync();
            try
            {
                using var doc = JsonDocument.Parse(raw);
                switch (doc.RootElement.ValueKind)
                {
                    case JsonValueKind.Object:
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            body.Fields[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                                ? prop.Value.GetString() ?? string.Empty
                                : prop.Value.GetRawText();
                            if (prop.Value.ValueKind == JsonValueKind.Array)
                                body.Repeated[prop.Name] = ElementToStrings(prop.Value);
                        }
                        break;
                    case JsonValueKind.Array:
                        body.RootArray = ElementToStrings(doc.RootElement);
                        break;
                }
            }
            catch (JsonException)
            {
                // Empty or non-JSON body → query/form values only.
            }
        }

        HttpContext.Items[BodyItemKey] = body;
        return body;
    }
}
