using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Clubs;

/// <summary>
/// clubs.localhost — the 2020.12 watch's club home tab and announcement
/// inbox. Path-based routing per the host-filter strategy in
/// Program.cs (HostFilteringMiddleware gates which subdomains reach
/// us, individual controllers don't need [Host] filters).
///
/// URL surface (verb + template) verified against
/// <c>Cpp2IL_ISIL/.../JDJGIBLMFKK.txt</c>:
///   GET    /club/home/me
///   PUT    /club/home/me                       (2023, form clubId)
///   DELETE /club/home/me                       (2023)
///   GET    /club/mine/member
///   GET    /club/mine/created
///   GET    /club/categoryTags
///   DELETE /club/{clubId}                      (2023, disband)
///   GET    /club/{clubId}/hasDisabledClubChat  (2023, bare bool)
///   DELETE /club/{clubId}/mainimage            (2023)
///   GET    /club/{clubId}/members              (2023, bare array)
///   GET    /club/{clubId}/members/banned       (2023, bare array)
///   GET    /club/{clubId}/members/requests     (2023, bare array)
///   GET    /club/{clubId}/members/search       (2023, paged envelope)
///   GET    /club/{clubId}/members/requests/search (2023, paged envelope)
///   GET    /announcements/mine                 (2023, flat array)
///   GET    /announcements/subscription/mine    (2023, flat array)
///   GET    /announcements/v2/mine/unread
///   GET    /announcements/v2/subscription/mine/unread
///   POST   /announcements/club/{clubId}/{announcementId}/read
///
/// Still unserved for want of schema (see the per-route notes in
/// <c>docs/recroom-2023-client-api-complete.md</c>): PUT
/// <c>club/{id}/minlevel</c>, PUT <c>club/{id}/clubChatEnabled</c>, PUT
/// <c>club/{id}/permissions/{role}</c> and DELETE
/// <c>club/{id}/additionalimage/{slot}</c> — each needs storage that does
/// not exist yet (ClubEntity.MinLevel, ClubEntity.ClubChatEnabled, a
/// per-role club-permission table, a club additional-image table).
///
/// Wire types (deserialiser JSON keys, per ISIL):
///   Club            (2020 <c>PLILLKHMNDA</c> / 2023 <c>FOIJDINBPFG</c>):
///                   ClubId, Name, Description, MainImageName, State,
///                   CreatorAccountId, Category, Visibility, Joinability,
///                   AllowJuniors, MemberCount, MinLevel, IsRRO,
///                   ClubChatEnabled, ClubhouseRoomId, ClubType
///   Announcement    (<c>NFEMLMAFFIP</c> / <c>JDPPAFLFNBD</c>): AnnouncementId,
///                   CreatorAccountId, ClubId, Title, Body, ImageName,
///                   CreatedAt, Meta
///   UnreadResponse  (<c>EIKPFIDCKNE</c> / <c>NCHLBFPHFJE</c>) — a LIST of
///                   per-club rows { clubId, LastAnnouncementId,
///                   LastReadAnnouncementId, announcements: [Announcement] }
///   ClubFeed        (<c>HPACLJHLHBG</c> / <c>FIAKMDGGIHH</c>) — ONE such row,
///                   not a list; see <c>AnnouncementsForClub</c>.
///   CategoryTags    plain <c>List&lt;String&gt;</c> (per
///                   <c>JDJGIBLMFKK.GetPrimaryTags</c> callback shape).
/// </summary>
[ApiController]
public class ClubsController(ClubService clubs, DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    [HttpGet("/club/home/me")]
    [Authorize]
    public async Task<IActionResult> HomeMe()
    {
        var pid = Me;
        var home = await clubs.HomeClubAsync(pid);
        if (home is null)
        {
            // Watch tolerates an empty/default Club object on the home
            // tab — it shows the "no home club" empty state. Returning
            // 204 No Content would short-circuit the deserialiser
            // (<c>Action`1&lt;PLILLKHMNDA&gt;</c>) so we send a minimal
            // sentinel row with ClubId=0 instead; the watch's null-check
            // path keys off ClubId.
            return Ok(ToWireClub(EmptyClub(), 0));
        }
        var memberCount = await clubs.MemberCountAsync(home.Id);
        return Ok(ToWireClub(home, memberCount));
    }

    /// <summary>PUT <c>/club/home/me</c> — "Set Home Clubhouse Room". The
    /// 2023 client sends the target club in a form-urlencoded
    /// <c>clubId</c> field (boxed <c>Nullable&lt;Int64&gt;</c> pushed through
    /// <c>BNDIAONDFFF.AFGEDDANEKP</c> at <c>IKMMOCKDKAF.txt:16923-16927</c>)
    /// and parses NO response DTO — the issuing method is
    /// <c>LDGADANDBIO JOPMBFIFFBB(Nullable&lt;Int64&gt;)</c>, the status-only
    /// promise type, so an empty 200 is the contract. Verb ordinal 3 (PUT)
    /// is the HasValue branch of the same method at :16911; the null branch
    /// is the DELETE below (:16838).</summary>
    [HttpPut("/club/home/me")]
    [Authorize]
    public async Task<IActionResult> HomeMeSet()
    {
        var pid = Me;
        long clubId = 0;
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var key in new[] { "clubId", "ClubId" })
                if (long.TryParse(form[key].FirstOrDefault(), out var fromForm) && fromForm > 0)
                {
                    clubId = fromForm;
                    break;
                }
        }
        if (clubId == 0 && long.TryParse(Request.Query["clubId"].FirstOrDefault(), out var fromQuery))
            clubId = fromQuery;
        if (clubId <= 0) return BadRequest(new { error = "missing_club_id" });
        if (!await clubs.SetHomeClubAsync(pid, clubId)) return NotFound();
        return Ok();
    }

    /// <summary>DELETE <c>/club/home/me</c> — "Remove Home Clubhouse Room".
    /// No body, no response DTO (<c>IKMMOCKDKAF.txt:16830-16841</c>: route
    /// literal, host 13, verb ordinal 4, error string "Failed to Remove Home
    /// Clubhouse Room"). Idempotent so a double-tap is harmless.</summary>
    [HttpDelete("/club/home/me")]
    [Authorize]
    public async Task<IActionResult> HomeMeClear()
    {
        await clubs.ClearHomeClubAsync(Me);
        return Ok();
    }

    [HttpGet("/club/mine/member")]
    [Authorize]
    public async Task<IActionResult> MineMember()
    {
        var pid = Me;
        var rows = await clubs.MyClubsAsync(pid);
        var counts = await clubs.MemberCountsAsync(rows.Select(r => r.Id));
        return Ok(rows.Select(c => ToWireClub(c, counts.GetValueOrDefault(c.Id))));
    }

    [HttpGet("/club/mine/created")]
    [Authorize]
    public async Task<IActionResult> MineCreated()
    {
        var pid = Me;
        var rows = await clubs.CreatedByAsync(pid);
        var counts = await clubs.MemberCountsAsync(rows.Select(r => r.Id));
        return Ok(rows.Select(c => ToWireClub(c, counts.GetValueOrDefault(c.Id))));
    }

    /// <summary>GET <c>/club/search</c> — browse/search clubs by
    /// optional category and text query. The watch issues this while
    /// building the Clubs discover tab, e.g.
    /// <c>?sort=0&amp;category=Creative&amp;count=30</c>, and expects
    /// a <c>CAJHIEENJJD</c> envelope: <c>Clubs</c>,
    /// <c>TotalClubs</c>, and <c>ContinuationToken</c>.</summary>
    [HttpGet("/club/search")]
    public async Task<IActionResult> ClubSearch(
        [FromQuery] string? query,
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] int? sort,
        [FromQuery] int? count)
    {
        var result = await clubs.SearchAsync(query ?? search, category, sort ?? 0, count ?? 30);
        var counts = await clubs.MemberCountsAsync(result.Clubs.Select(r => r.Id));
        return Ok(new
        {
            Clubs = result.Clubs.Select(c => ToWireClub(c, counts.GetValueOrDefault(c.Id))),
            TotalClubs = result.TotalClubs,
            ContinuationToken = string.Empty,
        });
    }

    /// <summary>GET <c>/club/mostactivetoday</c> — the Clubs discover tab's
    /// "most active today" carousel. Client contract is a bare
    /// <c>IReadOnlyList&lt;Club&gt;</c> (not the search envelope), so this
    /// returns a flat array of the canonical <see cref="ToWireClub"/> shape,
    /// ordered by the club service's default activity sort.</summary>
    [HttpGet("/club/mostactivetoday")]
    public async Task<IActionResult> MostActiveToday([FromQuery] int? count)
    {
        var result = await clubs.SearchAsync(null, null, sort: 0, count: Math.Clamp(count ?? 30, 1, 100));
        var counts = await clubs.MemberCountsAsync(result.Clubs.Select(r => r.Id));
        return Ok(result.Clubs.Select(c => ToWireClub(c, counts.GetValueOrDefault(c.Id))).ToList());
    }

    /// <summary>GET <c>/club?name=Foo</c> — direct name lookup used by
    /// the client when resolving a club by display name. Returns the
    /// single <c>PLILLKHMNDA</c> Club wire shape.</summary>
    [HttpGet("/club")]
    public async Task<IActionResult> ClubByName([FromQuery] string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "missing_name" });
        var club = await clubs.GetByNameAsync(name);
        if (club is null) return NotFound();
        var members = await clubs.MemberCountAsync(club.Id);
        return Ok(ToWireClub(club, members));
    }

    /// <summary>The 2020.12 watch deserialises this as
    /// <c>List&lt;String&gt;</c> (per <c>JDJGIBLMFKK.GetPrimaryTags</c>
    /// callback signature), so we project just the tag Name column.</summary>
    [HttpGet("/club/categoryTags")]
    public async Task<IActionResult> CategoryTags()
    {
        var rows = await clubs.CategoryTagsAsync();
        return Ok(rows.Select(t => t.Name));
    }

    /// <summary>
    /// Unread direct-feed announcements grouped by club into
    /// <c>HPACLJHLHBG</c> wire rows ({ clubId, LastReadAnnouncementId,
    /// announcements }). One row per club that has at least one unread
    /// item — clubs with nothing new are omitted so the watch's bell
    /// badge counts correctly.
    /// </summary>
    [HttpGet("/announcements/v2/mine/unread")]
    [Authorize]
    public async Task<IActionResult> UnreadMine()
    {
        var pid = Me;
        var rows = await clubs.UnreadDirectAsync(pid);
        return Ok(GroupByClub(rows, await LastReadByClubAsync(pid, rows.Select(a => a.ClubId))));
    }

    /// <summary>GET <c>/announcements/mine</c> — every announcement across the
    /// clubs the caller is a member of, newest first. Distinct from the v2
    /// unread rollup above in BOTH shape and semantics: the issuing method is
    /// <c>FGLDKEJLAKB&lt;List&lt;JDPPAFLFNBD&gt;&gt; IOCFHCFLIMJ()</c>
    /// (<c>IKMMOCKDKAF.txt:996</c>), i.e. a FLAT array of Announcement objects
    /// with no per-club envelope and no unread filter — the request literals
    /// at :1075 carry the description "get all announcements for clubs I'm
    /// in".</summary>
    [HttpGet("/announcements/mine")]
    [Authorize]
    public async Task<IActionResult> AnnouncementsMine()
    {
        var rows = await clubs.AnnouncementsForMemberClubsAsync(Me);
        return Ok(rows.Select(ToWireAnnouncement).ToList());
    }

    /// <summary>GET <c>/announcements/subscription/mine</c> — same flat
    /// <c>List&lt;JDPPAFLFNBD&gt;</c> shape as
    /// <see cref="AnnouncementsMine"/> (<c>IIOMOLFOLPD</c> at
    /// <c>IKMMOCKDKAF.txt:1083</c>, route literal at :1162) but scoped to the
    /// clubs the caller SUBSCRIBES to rather than belongs to.</summary>
    [HttpGet("/announcements/subscription/mine")]
    [Authorize]
    public async Task<IActionResult> AnnouncementsSubscriptionMine()
    {
        var rows = await clubs.AnnouncementsForSubscribedClubsAsync(Me);
        return Ok(rows.Select(ToWireAnnouncement).ToList());
    }

    [HttpGet("/announcements/v2/subscription/mine/unread")]
    [Authorize]
    public async Task<IActionResult> UnreadSubscription()
    {
        var pid = Me;
        var rows = await clubs.UnreadSubscriptionAsync(pid);
        return Ok(GroupByClub(rows, await LastReadByClubAsync(pid, rows.Select(a => a.ClubId))));
    }

    /// <summary>GET <c>/subscription/mine/member</c> — list of clubs the
    /// caller is subscribed to. Watch deserializes as
    /// <c>List&lt;LNIKPLKOBDK&gt;</c>; per the dump at
    /// <c>LNIKPLKOBDK.txt:110-124</c> the keys are lowercase
    /// <c>accountId</c>, <c>clubId</c>, <c>subscriberCount</c>. One row
    /// per subscription. Empty array is legal — the watch just hides the
    /// "Subscribed Clubs" section when the list is empty.</summary>
    [HttpGet("/subscription/mine/member")]
    [Authorize]
    public async Task<IActionResult> MySubscriptions()
    {
        var pid = Me;
        var rows = await clubs.MySubscriptionsAsync(pid);
        return Ok(rows);
    }

    /// <summary>
    /// GET <c>/subscription/subscriberCount/{accountId}</c> — bare int
    /// of how many subscribers an account has. Watch caller is
    /// <c>JDJGIBLMFKK.PNFHJBIAPBM(int)</c> at
    /// <c>JDJGIBLMFKK.txt:5533,5774</c> with signature
    /// <c>IPromise&lt;Int32&gt;</c>. Each Rec Room account has an
    /// implicit personal "creator club" so we union both possible
    /// counts:
    ///   * player→player subscriptions where TargetPlayerId == id, and
    ///   * club subscriptions where ClubId == id (if a club happens to
    ///     share the id).
    /// Anonymous-safe — used by profile cards the watch shows before
    /// the viewer is logged in to anyone they're not friends with.
    /// </summary>
    [HttpGet("/subscription/subscriberCount/{accountId:long}")]
    public async Task<IActionResult> SubscriberCount(long accountId)
    {
        var playerSubs = await clubs.PlayerSubscriberCountAsync(accountId);
        var clubSubs = await clubs.ClubSubscriberCountAsync(accountId);
        return Ok(playerSubs + clubSubs);
    }

    /// <summary>GET <c>/subscription/details/{accountId}</c> — the creator's
    /// subscription card. Both clients read the same three keys: the 2020 watch
    /// via <c>LNIKPLKOBDK</c> (exact-match lowercase <c>accountId</c>,
    /// <c>clubId</c>, <c>subscriberCount</c>) and 2023 via <c>DDFNNBDLBKF</c>
    /// (tri-cased AccountId/ClubId/SubscriberCount, <c>DDFNNBDLBKF.txt:279-346</c>)
    /// — camelCase satisfies both, so the casing here is load-bearing for 2020
    /// and must not be Pascal-ised.
    ///
    /// <c>clubId</c> used to be missing entirely. The 2023 caller is literally
    /// <c>GetCreatorClubIdForSubscription</c>, so the field defaulted to 0 and
    /// profile→creator-club navigation went nowhere.</summary>
    [HttpGet("/subscription/details/{accountId:long}")]
    public async Task<IActionResult> SubscriptionDetails(long accountId)
    {
        var viewerId = this.CurrentPlayerId();
        var subscriberCount = await clubs.PlayerSubscriberCountAsync(accountId);
        var subscribedCount = await clubs.PlayerSubscribedCountAsync(accountId);
        var isSubscribed = viewerId is long viewer
            && await clubs.IsSubscribedToPlayerAsync(viewer, accountId);
        // A creator's "creator club" is the club they own; oldest created wins
        // when an account somehow owns several.
        var created = await clubs.CreatedByAsync(accountId);
        var clubId = created.Count > 0 ? created.Min(c => c.Id) : 0L;
        return Ok(new
        {
            accountId,
            clubId,
            subscriberCount,
            subscribedCount,
            isSubscribed,
        });
    }

    /// <summary>
    /// POST endpoint the watch hits when the player taps "mark read".
    /// Idempotent — the upsert in the service quietly returns if a
    /// read-marker already exists.
    /// </summary>
    [HttpPost("/announcements/club/{clubId:long}/{announcementId:long}/read")]
    [Authorize]
    public async Task<IActionResult> MarkRead(long clubId, long announcementId)
    {
        var pid = Me;
        await clubs.MarkAnnouncementReadAsync(pid, announcementId);
        return Ok(new { Read = true });
    }

    // ── Single-club reads ─────────────────────────────────────────────

    /// <summary>GET <c>/club/{id}</c> — single Club lookup. Wire shape
    /// is <c>PLILLKHMNDA</c> (same as the lists). Used by the watch's
    /// link-resolver and the "club tile" widget on profile cards.</summary>
    [HttpGet("/club/{clubId:long}")]
    public async Task<IActionResult> ClubById(long clubId)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        var members = await clubs.MemberCountAsync(clubId);
        return Ok(ToWireClub(club, members));
    }

    /// <summary>DELETE <c>/club/{id}</c> — "Delete Club" (disband). Issuing
    /// method is <c>LDGADANDBIO MACDNDJNODA(Int64, String)</c> — the string is
    /// the club NAME used only for the client-side confirmation copy and is
    /// never transmitted; the request is a bare DELETE with no body
    /// (<c>IKMMOCKDKAF.txt:17107-17120</c>, verb ordinal 4, host 13, error
    /// string "Failed to Delete Club"). <c>LDGADANDBIO</c> is the status-only
    /// promise so an empty 200 is the whole contract.
    ///
    /// Owner-only, and a soft delete — see
    /// <see cref="ClubService.DisbandAsync"/>.</summary>
    [HttpDelete("/club/{clubId:long}")]
    [Authorize]
    public async Task<IActionResult> ClubDelete(long clubId)
    {
        try
        {
            var disbanded = await clubs.DisbandAsync(clubId, Me);
            if (disbanded is null) return NotFound();
            return Ok();
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    /// <summary>GET <c>/club/{id}/hasDisabledClubChat</c> — the gate the club
    /// chat flow checks before it will even request the club's thread. The
    /// issuing method is <c>FGLDKEJLAKB&lt;System.Boolean&gt; OFOJAHBKMOJ(Int64)</c>
    /// (<c>IKMMOCKDKAF.txt:25422</c>, route + verb ordinal 0 at :25540-25547),
    /// so the body is a BARE JSON boolean — not an object, not a wrapper.
    ///
    /// The answer is the negation of the Club wire field
    /// <c>ClubChatEnabled</c> that <see cref="ToWireClub"/> emits. That field
    /// has no column on <see cref="ClubEntity"/> yet and its setter
    /// (<c>PUT club/{id}/clubChatEnabled</c>) is therefore not implemented, so
    /// no club can currently be in the disabled state and <c>false</c> is the
    /// true answer for every club. Both sites must flip together when the
    /// column lands.</summary>
    [HttpGet("/club/{clubId:long}/hasDisabledClubChat")]
    public async Task<IActionResult> ClubHasDisabledChat(long clubId)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        return Ok(false);
    }

    /// <summary>GET <c>/club/{id}/details</c> — the central club page
    /// payload. Wire shape is <c>PIHMJGCGNLP</c> with required keys
    /// <c>Club</c>, <c>CoownerPermissions</c>, <c>ModeratorPermissions</c>,
    /// <c>MemberPermissions</c>, <c>MyMembershipType</c> per
    /// <c>PIHMJGCGNLP.cs</c>. Returning just the Club shape throws
    /// KeyNotFoundException on the deserializer.</summary>
    [HttpGet("/club/{clubId:long}/details")]
    public Task<IActionResult> ClubDetails(long clubId) => BuildDetailsResponseAsync(clubId);

    /// <summary><c>/club/{id}/clubhouse</c> — returns the same
    /// <c>PIHMJGCGNLP</c> shape as /details. The watch's clubhouse
    /// setter passes an optional roomId; clearing the clubhouse uses
    /// DELETE. We accept GET / PUT / DELETE so whichever verb the
    /// request-builder chose lands on the handler, then route the
    /// roomId into ClubhouseRoomId when present (set/clear) and reply
    /// with the refreshed details envelope.</summary>
    [HttpGet("/club/{clubId:long}/clubhouse")]
    [HttpPut("/club/{clubId:long}/clubhouse")]
    [HttpDelete("/club/{clubId:long}/clubhouse")]
    public async Task<IActionResult> ClubClubhouse(long clubId, [FromQuery(Name = "roomId")] long? roomId)
    {
        if (HttpMethods.IsPut(Request.Method) || HttpMethods.IsDelete(Request.Method))
        {
            // The client sends roomId in the form-urlencoded BODY, not the query
            // string. Reading only the query bound null, and the PUT branch then
            // wrote that null straight through — so "set clubhouse" silently
            // CLEARED the clubhouse instead of setting it.
            if (roomId is null && Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (var key in new[] { "roomId", "RoomId" })
                    if (long.TryParse(form[key].FirstOrDefault(), out var fromForm) && fromForm > 0)
                    {
                        roomId = fromForm;
                        break;
                    }
            }
            try
            {
                var updated = await clubs.ModifyAsync(clubId, Me, c =>
                    c.ClubhouseRoomId = HttpMethods.IsDelete(Request.Method) ? null : roomId);
                if (updated is null) return NotFound();
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }
        return await BuildDetailsResponseAsync(clubId);
    }

    /// <summary>GET <c>/club/{id}/permissions/{role}</c> — the permission
    /// record for one role on this club. This used to answer a bare int; both
    /// clients model it as an OBJECT (2023 <c>MMOCDPPONNG</c>, 2020
    /// <c>JHEEFBMODPG</c> — identical layout: long ClubId, membership-type
    /// enum, six bools), so see <see cref="PermissionsForRole"/>. Neither
    /// client actually issues the GET (both only PUT this template), but the
    /// shapes now agree.</summary>
    [HttpGet("/club/{clubId:long}/permissions/{role:int}")]
    public async Task<IActionResult> ClubPermissions(long clubId, int role)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        return Ok(PermissionsForRole(clubId, role));
    }

    // ── Account / created lists ──────────────────────────────────────

    /// <summary>GET <c>/account/{playerId}/clubs</c> — list of Clubs
    /// the player is a member of. Wire shape is
    /// <c>List&lt;PLILLKHMNDA&gt;</c>. Anonymous-safe — used on profile
    /// cards.</summary>
    [HttpGet("/account/{playerId:long}/clubs")]
    public async Task<IActionResult> AccountClubs(long playerId)
    {
        var rows = await clubs.ClubsForPlayerAsync(playerId);
        var counts = await clubs.MemberCountsAsync(rows.Select(r => r.Id));
        return Ok(rows.Select(c => ToWireClub(c, counts.GetValueOrDefault(c.Id))));
    }

    /// <summary>GET <c>/club/account/{playerId}/created</c> — list of
    /// Clubs the player created. Wire shape is
    /// <c>List&lt;PLILLKHMNDA&gt;</c>. Anonymous-safe.</summary>
    [HttpGet("/club/account/{playerId:long}/created")]
    public async Task<IActionResult> ClubAccountCreated(long playerId)
    {
        var rows = await clubs.CreatedByAsync(playerId);
        var counts = await clubs.MemberCountsAsync(rows.Select(r => r.Id));
        return Ok(rows.Select(c => ToWireClub(c, counts.GetValueOrDefault(c.Id))));
    }

    // ── Club create / modify ─────────────────────────────────────────

    public sealed class CreateClubRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? MainImageName { get; set; }
        public string? Category { get; set; }
    }

    /// <summary>POST <c>/club/create</c> — create a new club + the
    /// owner's membership row. Returns the <c>PIHMJGCGNLP</c> envelope
    /// (same shape as <c>/club/{id}/details</c>) so the watch's
    /// post-create navigation can render the club page without a
    /// separate fetch.</summary>
    [HttpPost("/club/create")]
    [Authorize]
    public async Task<IActionResult> ClubCreate()
    {
        var req = await ReadCreateClubRequestAsync();
        var club = await clubs.CreateAsync(Me, req.Name ?? string.Empty,
            req.Description, req.MainImageName, req.Category);
        if (club is null) return Conflict(new { error = "club_name_taken_or_empty" });
        return await BuildDetailsResponseAsync(club.Id);
    }

    public sealed class ModifyClubRequest
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Category { get; set; }
        public int? Visibility { get; set; }
        public int? Joinability { get; set; }
        public bool? AllowJuniors { get; set; }
        public long? ClubhouseRoomId { get; set; }

        /// <summary>Repeated <c>customTags</c> form field sent by
        /// <c>club/{0}/modifydetails</c> alongside visibility / joinability /
        /// allowJuniors (<c>IKMMOCKDKAF_NestedType_MAFIJOPDGHK.txt:139-185</c>).
        /// Null means "not supplied"; an empty list clears the club's tags.
        /// Note the form path can only ever deliver null or a non-empty list —
        /// <c>FormOrJsonModelBinder.Convert</c> collapses an all-blank repeated
        /// field to null — so a client that clears every tag over
        /// form-urlencoded leaves the existing set in place. Only the JSON path
        /// can send an explicit <c>[]</c>.</summary>
        public List<string>? CustomTags { get; set; }
    }

    /// <summary>POST <c>/club/{id}/modify</c> + <c>/modifydetails</c> —
    /// the watch sends two different request DTOs to two URLs, but
    /// both mutate the same Club row and both expect the
    /// <c>PIHMJGCGNLP</c> response. Single handler covers both
    /// because the response shape is identical and we only persist
    /// fields the request includes.</summary>
    [HttpPost("/club/{clubId:long}/modify")]
    [HttpPut("/club/{clubId:long}/modify")]
    [HttpPost("/club/{clubId:long}/modifydetails")]
    [HttpPut("/club/{clubId:long}/modifydetails")]
    [Authorize]
    public async Task<IActionResult> ClubModify(long clubId, [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] ModifyClubRequest req)
    {
        try
        {
            var updated = await clubs.ModifyAsync(clubId, Me, c =>
            {
                if (!string.IsNullOrWhiteSpace(req.Name)) c.Name = req.Name.Trim();
                if (req.Description is not null) c.Description = req.Description;
                if (req.Category is not null) c.Category = req.Category;
                if (req.Visibility is int v) c.Visibility = v;
                if (req.Joinability is int j) c.Joinability = j;
                if (req.AllowJuniors is bool aj) c.AllowJuniors = aj;
                if (req.ClubhouseRoomId is long ch) c.ClubhouseRoomId = ch;
            });
            if (updated is null) return NotFound();
            // Tags live in the club↔tag junction, not on the club row, so they
            // are written after ModifyAsync has already vetted the caller.
            if (req.CustomTags is not null) await ReplaceCustomTagsAsync(updated.Id, req.CustomTags);
            return await BuildDetailsResponseAsync(updated.Id);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    public sealed class MainImageRequest { public string? ImageName { get; set; } }

    /// <summary>POST <c>/club/{id}/mainimage</c> — replace the club's
    /// MainImageName. Same <c>PIHMJGCGNLP</c> response so the watch can
    /// refresh the club page in one round-trip.</summary>
    [HttpPost("/club/{clubId:long}/mainimage")]
    [HttpPut("/club/{clubId:long}/mainimage")]
    [Authorize]
    public async Task<IActionResult> ClubMainImage(long clubId, [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] MainImageRequest req)
    {
        try
        {
            var updated = await clubs.ModifyAsync(clubId, Me,
                c => c.ImageName = req.ImageName ?? string.Empty);
            if (updated is null) return NotFound();
            return await BuildDetailsResponseAsync(updated.Id);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    /// <summary>DELETE <c>/club/{id}/mainimage</c> — "Remove Club Image".
    /// One method, <c>BLEDOCHHHJM(Int64, String imageName)</c>, drives both
    /// verbs: <c>IKMMOCKDKAF.txt:16010</c> tests the captured imageName and
    /// branches to verb ordinal 4 with no body when it is null (:16019-16024,
    /// description "Remove Club Image") or verb 3 with the form field when it
    /// is not (:16046-16050). Both branches go through
    /// <c>IBAKMFKEEDJ</c>, so the response is the full <c>LCLFBBPEMIH</c>
    /// details envelope, NOT a status-only 200.
    ///
    /// Registered as its own action rather than a third binding on
    /// <see cref="ClubMainImage"/> so the delete semantics are explicit —
    /// aliasing an edit verb onto a destructive handler is the exact bug that
    /// made <c>PUT /announcements/club/{c}/{a}</c> destroy posts.</summary>
    [HttpDelete("/club/{clubId:long}/mainimage")]
    [Authorize]
    public async Task<IActionResult> ClubMainImageDelete(long clubId)
    {
        try
        {
            var updated = await clubs.ModifyAsync(clubId, Me, c => c.ImageName = string.Empty);
            if (updated is null) return NotFound();
            return await BuildDetailsResponseAsync(updated.Id);
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
    }

    /// <summary>POST/PUT <c>/club/{id}/additionalimage/{slot}</c> — the club
    /// gallery's per-slot image setter (<c>IKMMOCKDKAF.txt:16214</c>).
    ///
    /// The request now binds (the client sends form-urlencoded
    /// <c>imageName</c>, which <c>[FromBody]</c> used to reject with 415), but
    /// the slot itself STILL is not persisted: there is no additional-image
    /// table, and the envelope's <c>AdditionalImages</c> is the client's
    /// <c>HIKCHBLAMLP</c> — {ImageName, Slot} — with nowhere to come from. So
    /// the call is accepted and permission-checked, the details envelope comes
    /// back refreshed, and the gallery renders blank until a
    /// <c>ClubAdditionalImageEntity</c> lands.</summary>
    [HttpPost("/club/{clubId:long}/additionalimage/{slot:int}")]
    [HttpPut("/club/{clubId:long}/additionalimage/{slot:int}")]
    [Authorize]
    public async Task<IActionResult> ClubAdditionalImage(long clubId, int slot, [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] MainImageRequest req)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        if (!await clubs.CanManageAsync(clubId, Me, club)) return Forbid();
        return await BuildDetailsResponseAsync(clubId);
    }

    // ── Single-member read ───────────────────────────────────────────

    /// <summary>GET <c>/club/{id}/members/{playerId}</c> — single
    /// membership row. Wire shape is <c>MFOAODGNGKB</c>: AccountId,
    /// ClubId, MembershipType, CreatedAt.</summary>
    [HttpGet("/club/{clubId:long}/members/{playerId:long}")]
    public async Task<IActionResult> ClubMember(long clubId, long playerId)
    {
        var row = await clubs.MembershipForAsync(clubId, playerId);
        if (row is null) return NotFound();
        return Ok(ToWireMembership(row));
    }

    // ── Member lists (roster / banned / requests, + their searches) ──

    /// <summary>GET <c>/club/{id}/members</c> — the club page's member roster.
    /// Issuing method is
    /// <c>FGLDKEJLAKB&lt;List&lt;CADEIMCFIIG&gt;&gt; GFOBOIIGDJF(Int64, GNLOJEONFIG?, MJOOBDNCHBO?, Int32?, Int32?)</c>
    /// (<c>IKMMOCKDKAF.txt:19444</c>) so the body is a bare JSON ARRAY of the
    /// same membership row <see cref="ClubMember"/> already serves — no paged
    /// envelope. Verb ordinal 0 (GET) at :19746, query keys verbatim from
    /// :19753/:19777/:19809/:19822: <c>membershipType</c>, <c>sortBy</c>,
    /// <c>skip</c>, <c>take</c>.
    ///
    /// <c>membershipType</c> is the MJOOBDNCHBO wire value, not the stored
    /// perms int, so it is compared after
    /// <see cref="ClubService.MembershipTypeFromPerms"/>. With no filter the
    /// roster is real members only — ban markers and pending rows have their
    /// own endpoints below and must not leak into the member list.</summary>
    [HttpGet("/club/{clubId:long}/members")]
    public async Task<IActionResult> ClubMembers(
        [FromRoute] long clubId,
        [FromQuery] int? membershipType,
        [FromQuery] int? sortBy,
        [FromQuery] int? skip,
        [FromQuery] int? take)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();

        var rows = await LoadMemberRowsAsync(clubId);
        rows = membershipType is int wanted
            ? rows.Where(r => r.MembershipType == wanted).ToList()
            : rows.Where(r => r.MembershipType >= ClubService.MembershipTypeMember).ToList();

        return Ok(Page(SortMembers(rows, sortBy ?? 0), skip ?? 0, take ?? 0)
            .Select(r => ToWireMembership(r.Row))
            .ToList());
    }

    /// <summary>GET <c>/club/{id}/members/banned</c> — the banned list.
    /// <c>MBKDPCIOMDD(Int64, GNLOJEONFIG?, Int32?, Int32?)</c> returns
    /// <c>List&lt;ICPNBOOIDLI&gt;</c> (<c>IKMMOCKDKAF.txt:18692</c>): a bare
    /// array whose rows carry only <c>AccountId</c> (int), <c>ClubId</c>
    /// (long) and <c>CreatedAt</c> — there is no MembershipType field on
    /// ICPNBOOIDLI (recnet-runtime-decomp/ICPNBOOIDLI.cs). GET at :18978,
    /// query keys <c>sortBy</c>/<c>skip</c>/<c>take</c> at :18985/:19016/:19028.
    ///
    /// Rows are the perms=256 ban markers stamped by
    /// <see cref="MemberBan"/>. Moderator-gated: the ban list is staff-only
    /// information.</summary>
    [HttpGet("/club/{clubId:long}/members/banned")]
    [Authorize]
    public async Task<IActionResult> ClubMembersBanned(
        [FromRoute] long clubId,
        [FromQuery] int? sortBy,
        [FromQuery] int? skip,
        [FromQuery] int? take)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        if (!await clubs.CanManageAsync(clubId, Me, club)) return Forbid();

        var rows = (await LoadMemberRowsAsync(clubId))
            .Where(r => (r.Row.Permissions & 256) != 0)
            .ToList();

        return Ok(Page(SortMembers(rows, sortBy ?? 0), skip ?? 0, take ?? 0)
            .Select(r => ToWireBan(r.Row))
            .ToList());
    }

    /// <summary>GET <c>/club/{id}/members/requests</c> — pending join
    /// requests / outstanding invites.
    /// <c>GBJMBMCGCDL(Int64, Int32?, Int32?)</c> returns
    /// <c>List&lt;AADIHDCMEDB&gt;</c> (<c>IKMMOCKDKAF.txt:21086</c>) — a bare
    /// array, GET at :21325, only <c>skip</c> + <c>take</c> query keys
    /// (:21338, :21350). Error copy "Failed to get pending club member
    /// requests" at :21394 confirms the screen.</summary>
    [HttpGet("/club/{clubId:long}/members/requests")]
    [Authorize]
    public async Task<IActionResult> ClubMemberRequests(
        [FromRoute] long clubId,
        [FromQuery] int? skip,
        [FromQuery] int? take)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        if (!await clubs.CanManageAsync(clubId, Me, club)) return Forbid();

        var rows = PendingRows(await LoadMemberRowsAsync(clubId));
        return Ok(Page(SortMembers(rows, 1), skip ?? 0, take ?? 0)
            .Select(r => ToWireMemberRequest(r.Row))
            .ToList());
    }

    /// <summary>GET <c>/club/{id}/members/search</c> — the member-list search
    /// box. <c>PGMGPEKCNJP</c> returns <c>MBMNHFAPFCJ</c>
    /// (<c>IKMMOCKDKAF.txt:18108</c>), a PAGED envelope —
    /// <c>List&lt;CADEIMCFIIG&gt;</c> + Int32 total + String continuation
    /// token (recnet-runtime-decomp/MBMNHFAPFCJ.cs) — not the bare array the
    /// unsearched roster returns. GET at :18475; query keys are DOT-PREFIXED
    /// and must be read off <see cref="HttpRequest.Query"/> rather than bound
    /// as properties: <c>parameters.name</c> (:18484),
    /// <c>parameters.type</c> (:18492), <c>parameters.sortBy</c> (:18521),
    /// <c>parameters.maxCount</c> (:18554), plus an unprefixed
    /// <c>continuationToken</c> (:18563).
    ///
    /// The envelope's own key names are the one thing the binary cannot give
    /// up (they live in DataMember blobs in global-metadata.dat), so the
    /// entity-named pair validated on the sibling <c>club/search</c> envelope
    /// — <c>Clubs</c>/<c>TotalClubs</c>/<c>ContinuationToken</c> — is mirrored
    /// here as <c>Members</c>/<c>TotalMembers</c>, with <c>Results</c>/
    /// <c>TotalResults</c> alongside as the alternate spelling. Json.NET
    /// ignores whichever pair the DTO doesn't declare.</summary>
    [HttpGet("/club/{clubId:long}/members/search")]
    public async Task<IActionResult> ClubMembersSearch(long clubId)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();

        var name = QueryValue("parameters.name", "name");
        var type = QueryInt("parameters.type", "type", "membershipType");
        var sortBy = QueryInt("parameters.sortBy", "sortBy");
        var maxCount = QueryInt("parameters.maxCount", "maxCount", "take");
        var offset = QueryInt("continuationToken") ?? 0;

        var rows = await LoadMemberRowsAsync(clubId);
        rows = type is int wanted
            ? rows.Where(r => r.MembershipType == wanted).ToList()
            : rows.Where(r => r.MembershipType >= ClubService.MembershipTypeMember).ToList();
        rows = FilterByName(rows, name);

        var ordered = SortMembers(rows, sortBy ?? 0).ToList();
        var (page, nextToken) = Slice(ordered, offset, maxCount);
        return Ok(PagedEnvelope("Members", "TotalMembers",
            page.Select(r => ToWireMembership(r.Row)).ToList(), ordered.Count, nextToken));
    }

    /// <summary>GET <c>/club/{id}/members/requests/search</c> — search inside
    /// the pending-requests screen. <c>EDANLDCJECM</c> returns
    /// <c>EDFOCLNECPM</c> (<c>IKMMOCKDKAF.txt:21448</c>): the paged envelope
    /// over <c>List&lt;AADIHDCMEDB&gt;</c>. GET at :21812; dot-prefixed query
    /// keys <c>parameters.name</c> (:21821), <c>parameters.sortBy</c>
    /// (:21829, the NAHBJCGNJKA enum — Default=0, RequestDate_Asc=1,
    /// RequestDate_Desc=2, Username_Asc=3, Username_Desc=4),
    /// <c>parameters.maxCount</c> (:21862), <c>parameters.status</c> (:21867,
    /// MDFFODMAIGJ — Invited=0, Requested=1, Denied=2) and unprefixed
    /// <c>continuationToken</c> (:21896).
    ///
    /// RequestDate_Asc/Desc index the same JoinedAt column JoinDate_Asc/Desc
    /// do on the roster sort, so the two enums share
    /// <see cref="SortMembers"/>. The <c>status</c> filter can only ever
    /// match Requested — see <see cref="ToWireMemberRequest"/>.</summary>
    [HttpGet("/club/{clubId:long}/members/requests/search")]
    [Authorize]
    public async Task<IActionResult> ClubMemberRequestsSearch(long clubId)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        if (!await clubs.CanManageAsync(clubId, Me, club)) return Forbid();

        var name = QueryValue("parameters.name", "name");
        var sortBy = QueryInt("parameters.sortBy", "sortBy");
        var maxCount = QueryInt("parameters.maxCount", "maxCount", "take");
        var status = QueryInt("parameters.status", "status");
        var offset = QueryInt("continuationToken") ?? 0;

        var rows = FilterByName(PendingRows(await LoadMemberRowsAsync(clubId)), name);
        if (status is int wantedStatus)
            rows = rows.Where(r => RequestStatusFor(r.Row) == wantedStatus).ToList();

        var ordered = SortMembers(rows, sortBy ?? 0).ToList();
        var (page, nextToken) = Slice(ordered, offset, maxCount);
        return Ok(PagedEnvelope("Requests", "TotalRequests",
            page.Select(r => ToWireMemberRequest(r.Row)).ToList(), ordered.Count, nextToken));
    }

    /// <summary>A membership row paired with the account's username, which
    /// every list endpoint above needs for the Username_Asc/Desc sorts and
    /// the <c>parameters.name</c> filter. Loaded once per request rather than
    /// per row.</summary>
    private sealed record MemberRow(ClubMembershipEntity Row, string Username)
    {
        public int MembershipType => ClubService.MembershipTypeFromPerms(Row.Permissions);
    }

    private async Task<List<MemberRow>> LoadMemberRowsAsync(long clubId)
    {
        var rows = await db.ClubMemberships.AsNoTracking()
            .Where(m => m.ClubId == clubId)
            .ToListAsync();
        if (rows.Count == 0) return new List<MemberRow>();

        var ids = rows.Select(r => r.PlayerId).Distinct().ToList();
        var names = await db.Players.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Username, p.DisplayName })
            .ToListAsync();
        var byId = names.ToDictionary(
            n => n.Id,
            n => string.IsNullOrWhiteSpace(n.Username) ? n.DisplayName : n.Username);

        return rows
            .Select(r => new MemberRow(r, byId.GetValueOrDefault(r.PlayerId, string.Empty)))
            .ToList();
    }

    /// <summary>Rows carrying the pending marker (128) and NOT the ban marker
    /// — the join-request / invite queue.</summary>
    private static List<MemberRow> PendingRows(List<MemberRow> rows) => rows
        .Where(r => (r.Row.Permissions & 128) != 0 && (r.Row.Permissions & 256) == 0)
        .ToList();

    private static List<MemberRow> FilterByName(List<MemberRow> rows, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return rows;
        var needle = name.Trim();
        return rows
            .Where(r => r.Username.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Shared ordering for GNLOJEONFIG (roster/banned) and
    /// NAHBJCGNJKA (requests) — both enums are Default=0, date asc=1,
    /// date desc=2, Username_Asc=3, Username_Desc=4 over the same JoinedAt
    /// column (recnet-runtime-decomp/GNLOJEONFIG.cs, NAHBJCGNJKA.cs).
    /// Default puts the most privileged roles first, which is the order the
    /// club page renders its roster in.</summary>
    private static IEnumerable<MemberRow> SortMembers(IEnumerable<MemberRow> rows, int sortBy) => sortBy switch
    {
        1 => rows.OrderBy(r => r.Row.JoinedAt).ThenBy(r => r.Row.Id),
        2 => rows.OrderByDescending(r => r.Row.JoinedAt).ThenByDescending(r => r.Row.Id),
        3 => rows.OrderBy(r => r.Username, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Row.Id),
        4 => rows.OrderByDescending(r => r.Username, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Row.Id),
        _ => rows.OrderByDescending(r => r.MembershipType)
                 .ThenBy(r => r.Row.JoinedAt)
                 .ThenBy(r => r.Row.Id),
    };

    /// <summary>skip/take paging for the three bare-array lists. take=0 means
    /// "unspecified" (the client omits the param), so it falls back to a
    /// 100-row page rather than returning nothing.</summary>
    private static List<MemberRow> Page(IEnumerable<MemberRow> rows, int skip, int take) => rows
        .Skip(Math.Max(skip, 0))
        .Take(take <= 0 ? 100 : Math.Clamp(take, 1, 500))
        .ToList();

    /// <summary>Continuation-token paging for the two search envelopes. The
    /// token is opaque to the client (a String on the DTO), so it carries the
    /// next row offset; an empty string means "no more pages", which is what
    /// the sibling <c>club/search</c> envelope already returns.</summary>
    private static (List<MemberRow> Page, string NextToken) Slice(
        List<MemberRow> ordered, int offset, int? maxCount)
    {
        var start = Math.Clamp(offset, 0, ordered.Count);
        var size = maxCount is int m && m > 0 ? Math.Clamp(m, 1, 500) : 30;
        var page = ordered.Skip(start).Take(size).ToList();
        var next = start + page.Count;
        return (page, next < ordered.Count ? next.ToString() : string.Empty);
    }

    private static Dictionary<string, object?> PagedEnvelope(
        string listKey, string totalKey, List<object> rows, int total, string continuationToken) =>
        new()
        {
            [listKey] = rows,
            ["Results"] = rows,
            [totalKey] = total,
            ["TotalResults"] = total,
            ["ContinuationToken"] = continuationToken,
        };

    /// <summary>First non-empty value across a set of query-string aliases.
    /// The 2023 client prefixes the search DTO's fields with
    /// <c>parameters.</c>, which ASP.NET cannot bind to a plain property, so
    /// these are read straight off the query collection.</summary>
    private string? QueryValue(params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Request.Query[key].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private int? QueryInt(params string[] keys) =>
        int.TryParse(QueryValue(keys), out var parsed) ? parsed : null;

    /// <summary><c>ICPNBOOIDLI</c> — the banned-list row. Three fields only
    /// (recnet-runtime-decomp/ICPNBOOIDLI.cs): int AccountId, long ClubId,
    /// DateTime. CreatedAt is the membership row's JoinedAt, which
    /// <see cref="MemberBan"/> leaves at the original join time when it
    /// stamps the ban marker onto an existing row.</summary>
    private static object ToWireBan(ClubMembershipEntity row) => new
    {
        AccountId = (int)row.PlayerId,
        ClubId = row.ClubId,
        CreatedAt = row.JoinedAt,
    };

    /// <summary><c>AADIHDCMEDB</c> — the pending-request row. Field list from
    /// recnet-runtime-decomp/AADIHDCMEDB.cs: long id, int AccountId, int?
    /// inviter account id, long ClubId, MJOOBDNCHBO MembershipType,
    /// MDFFODMAIGJ status, DateTime.
    ///
    /// Two fields the current perms model genuinely cannot fill, rather than
    /// invent: the inviter is not recorded anywhere (invites and requests
    /// both collapse to the single 128 marker on the target's own row) so it
    /// is emitted as null — the DTO declares it nullable — and the status is
    /// always Requested for the same reason. Nothing in the accept/deny flow
    /// reads either field: those calls key off <c>accountId</c>.</summary>
    private static object ToWireMemberRequest(ClubMembershipEntity row) => new
    {
        Id = row.Id,
        RequestId = row.Id,
        AccountId = (int)row.PlayerId,
        InviterAccountId = (int?)null,
        ClubId = row.ClubId,
        MembershipType = ClubService.MembershipTypeFromPerms(row.Permissions),
        Status = RequestStatusFor(row),
        CreatedAt = row.JoinedAt,
    };

    /// <summary>MDFFODMAIGJ: Invited=0, Requested=1, Denied=2. Only Requested
    /// is reachable — see <see cref="ToWireMemberRequest"/>.</summary>
    private static int RequestStatusFor(ClubMembershipEntity row) => 1;

    // ── Member mutations (void / IPromise) ───────────────────────────

    /// <summary>POST <c>/club/{id}/members/requesttojoin</c> — caller
    /// asks to join. For Open clubs we promote straight to Member; for
    /// RequestToJoin we mark Pending; InviteOnly clubs reject.</summary>
    [HttpPost("/club/{clubId:long}/members/requesttojoin")]
    [HttpPut("/club/{clubId:long}/members/requesttojoin")]
    [Authorize]
    public async Task<IActionResult> MemberRequestToJoin(long clubId)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        // Joinability enum, confirmed from the 2023 dump (enum FEHIHCMDOLN):
        // Open = 0, InviteOnly = 1, AskToJoin = 2.
        // This used to treat 1 as request-to-join and 2 as invite-only, i.e.
        // exactly backwards: "Ask to join" clubs rejected every request while
        // invite-only clubs quietly accepted them.
        var perms = club.Joinability switch
        {
            0 => 0,   // Open: instant member
            2 => 128, // AskToJoin: pending approval
            _ => -1,  // InviteOnly: rejected
        };
        if (perms == -1) return Forbid();
        await clubs.UpsertMembershipAsync(clubId, Me, perms);
        return Ok();
    }

    /// <summary>POST <c>/club/{id}/members/directJoin</c> — same as
    /// requesttojoin for an Open club but bypasses the pending flow
    /// even on RequestToJoin clubs (used by the "accept invite" flow
    /// after a successful invite acceptance).</summary>
    [HttpPost("/club/{clubId:long}/members/directJoin")]
    [Authorize]
    public async Task<IActionResult> MemberDirectJoin(long clubId)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        await clubs.UpsertMembershipAsync(clubId, Me, 0);
        return Ok();
    }

    /// <summary>POST <c>/club/{id}/members/leave</c> — caller leaves
    /// the club. Owner cannot leave without first transferring
    /// ownership (mirrors the GroupsController.Leave guard).</summary>
    [HttpPost("/club/{clubId:long}/members/leave")]
    [Authorize]
    public async Task<IActionResult> MemberLeave(long clubId)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        if (club.CreatorPlayerId == Me)
            return BadRequest(new { error = "owner_must_transfer_first" });
        await clubs.RemoveMembershipAsync(clubId, Me);
        return Ok();
    }

    /// <summary>POST <c>/club/{id}/members/invite</c> — caller invites
    /// a target player. Pending=128 marker; the invited player's
    /// <c>acceptinvite</c> flips it to Member.</summary>
    public sealed class MemberInviteRequest
    {
        public int? PlayerId { get; set; }
        public int? AccountId { get; set; }
        public int? MembershipType { get; set; }
    }

    [HttpPost("/club/{clubId:long}/members/invite")]
    [HttpPut("/club/{clubId:long}/members/invite")]
    [Authorize]
    public async Task<IActionResult> MemberInvite(long clubId, [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] MemberInviteRequest req)
    {
        if (!await clubs.CanManageAsync(clubId, Me)) return Forbid();
        var target = req.PlayerId ?? req.AccountId ?? 0;
        if (target == 0) return BadRequest(new { error = "missing_player_id" });
        await clubs.UpsertMembershipAsync(clubId, target, 128); // Pending
        return Ok();
    }

    /// <summary>POST <c>/club/{id}/members/invitemembers</c> — bulk
    /// invite. Body is <c>{playerIds: [...]}</c> + optional
    /// membership-type override.</summary>
    public sealed class InviteMembersRequest
    {
        public List<int>? PlayerIds { get; set; }
        public List<int>? AccountIds { get; set; }
        public int? MembershipType { get; set; }
    }

    [HttpPost("/club/{clubId:long}/members/invitemembers")]
    [Authorize]
    public async Task<IActionResult> MemberInviteMembers(long clubId, [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] InviteMembersRequest req)
    {
        if (!await clubs.CanManageAsync(clubId, Me)) return Forbid();
        var ids = (req.PlayerIds ?? req.AccountIds ?? new List<int>()).Distinct().ToList();
        foreach (var pid in ids)
            await clubs.UpsertMembershipAsync(clubId, pid, 128);
        return Ok();
    }

    /// <summary>POST <c>/club/{id}/members/acceptinvite</c> — caller
    /// accepts an outstanding invite (their own pending row).</summary>
    [HttpPost("/club/{clubId:long}/members/acceptinvite")]
    [Authorize]
    public async Task<IActionResult> MemberAcceptInvite(long clubId)
    {
        var row = await clubs.MembershipForAsync(clubId, Me);
        if (row is null) return NotFound();
        if ((row.Permissions & 128) == 0) return BadRequest(new { error = "no_pending_invite" });
        await clubs.UpsertMembershipAsync(clubId, Me, 0); // Member
        return Ok();
    }

    /// <summary>POST <c>/club/{id}/members/declineinvite</c> — caller
    /// rejects an outstanding invite (removes their pending row).</summary>
    [HttpPost("/club/{clubId:long}/members/declineinvite")]
    [Authorize]
    public async Task<IActionResult> MemberDeclineInvite(long clubId)
    {
        await clubs.RemoveMembershipAsync(clubId, Me);
        return Ok();
    }

    public sealed class MemberTargetRequest
    {
        public int? PlayerId { get; set; }
        public int? AccountId { get; set; }
        public int? MembershipType { get; set; }
    }

    /// <summary>POST <c>/club/{id}/members/acceptrequest</c> — owner /
    /// moderator approves a single pending join request, promoting the
    /// target's row from Pending to Member.</summary>
    [HttpPost("/club/{clubId:long}/members/acceptrequest")]
    [Authorize]
    public async Task<IActionResult> MemberAcceptRequest(long clubId, [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] MemberTargetRequest req)
    {
        if (!await clubs.CanManageAsync(clubId, Me)) return Forbid();
        var target = req.PlayerId ?? req.AccountId ?? 0;
        if (target == 0) return BadRequest(new { error = "missing_player_id" });
        var row = await clubs.MembershipForAsync(clubId, target);
        if (row is null) return NotFound();
        if ((row.Permissions & 128) == 0) return BadRequest(new { error = "no_pending_request" });
        await clubs.UpsertMembershipAsync(clubId, target, 0);
        return Ok();
    }

    public sealed class BulkTargetRequest
    {
        public List<int>? PlayerIds { get; set; }
        public List<int>? AccountIds { get; set; }
    }

    [HttpPost("/club/{clubId:long}/members/acceptrequests")]
    [Authorize]
    public async Task<IActionResult> MemberAcceptRequests(long clubId, [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] BulkTargetRequest req)
    {
        if (!await clubs.CanManageAsync(clubId, Me)) return Forbid();
        var ids = (req.PlayerIds ?? req.AccountIds ?? new List<int>()).Distinct().ToList();
        foreach (var pid in ids)
        {
            var row = await clubs.MembershipForAsync(clubId, pid);
            if (row is null || (row.Permissions & 128) == 0) continue;
            await clubs.UpsertMembershipAsync(clubId, pid, 0);
        }
        return Ok();
    }

    [HttpPost("/club/{clubId:long}/members/denyrequest")]
    [Authorize]
    public async Task<IActionResult> MemberDenyRequest(long clubId, [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] MemberTargetRequest req)
    {
        if (!await clubs.CanManageAsync(clubId, Me)) return Forbid();
        var target = req.PlayerId ?? req.AccountId ?? 0;
        if (target == 0) return BadRequest(new { error = "missing_player_id" });
        await clubs.RemoveMembershipAsync(clubId, target);
        return Ok();
    }

    [HttpPost("/club/{clubId:long}/members/denyrequests")]
    [Authorize]
    public async Task<IActionResult> MemberDenyRequests(long clubId, [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] BulkTargetRequest req)
    {
        if (!await clubs.CanManageAsync(clubId, Me)) return Forbid();
        var ids = (req.PlayerIds ?? req.AccountIds ?? new List<int>()).Distinct().ToList();
        foreach (var pid in ids)
            await clubs.RemoveMembershipAsync(clubId, pid);
        return Ok();
    }

    [HttpPost("/club/{clubId:long}/members/remove")]
    [Authorize]
    public async Task<IActionResult> MemberRemove(long clubId, [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] MemberTargetRequest req)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        if (!await clubs.CanManageAsync(clubId, Me, club)) return Forbid();
        var target = req.PlayerId ?? req.AccountId ?? 0;
        if (target == 0) return BadRequest(new { error = "missing_player_id" });
        if (target == club.CreatorPlayerId)
            return BadRequest(new { error = "cannot_remove_owner" });
        await clubs.RemoveMembershipAsync(clubId, target);
        return Ok();
    }

    /// <summary>POST <c>/club/{id}/members/ban</c> — kick + prevent
    /// re-join. We persist with perms=256 as a ban marker bit so the
    /// request-to-join flow can refuse them later.</summary>
    [HttpPost("/club/{clubId:long}/members/ban")]
    [Authorize]
    public async Task<IActionResult> MemberBan(long clubId, [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] MemberTargetRequest req)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        if (!await clubs.CanManageAsync(clubId, Me, club)) return Forbid();
        var target = req.PlayerId ?? req.AccountId ?? 0;
        if (target == 0) return BadRequest(new { error = "missing_player_id" });
        if (target == club.CreatorPlayerId)
            return BadRequest(new { error = "cannot_ban_owner" });
        await clubs.UpsertMembershipAsync(clubId, target, 256); // Ban marker
        return Ok();
    }

    [HttpPost("/club/{clubId:long}/members/unban")]
    [Authorize]
    public async Task<IActionResult> MemberUnban(long clubId, [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] MemberTargetRequest req)
    {
        if (!await clubs.CanManageAsync(clubId, Me)) return Forbid();
        var target = req.PlayerId ?? req.AccountId ?? 0;
        if (target == 0) return BadRequest(new { error = "missing_player_id" });
        await clubs.RemoveMembershipAsync(clubId, target);
        return Ok();
    }

    /// <summary>POST <c>/club/{id}/members/changetype</c> — promote /
    /// demote a member's role. Body: PlayerId + MembershipType (the
    /// wire enum, NOT the perms int — we translate via
    /// <see cref="ClubService.PermsFromMembershipType"/>).</summary>
    [HttpPost("/club/{clubId:long}/members/changetype")]
    [HttpPut("/club/{clubId:long}/members/changetype")]
    [Authorize]
    public async Task<IActionResult> MemberChangeType(long clubId, [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))] MemberTargetRequest req)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        if (club.CreatorPlayerId != Me) return Forbid(); // Owner-only — perms transitions are sensitive
        var target = req.PlayerId ?? req.AccountId ?? 0;
        var newType = req.MembershipType ?? ClubService.MembershipTypeMember;
        if (target == 0) return BadRequest(new { error = "missing_player_id" });
        if (target == club.CreatorPlayerId)
            return BadRequest(new { error = "cannot_change_owner_role" });
        await clubs.UpsertMembershipAsync(clubId, target, ClubService.PermsFromMembershipType(newType));
        return Ok();
    }

    // ── members/bulk + clubreporting ─────────────────────────────────

    /// <summary>GET/POST <c>/members/bulk?id=A&amp;id=B</c> — bulk membership
    /// lookup for nameplate club badges. Response rows are
    /// <c>FCKGOFHNDNJ</c>: <c>AccountId</c> + <c>MembershipType</c>
    /// (reader <c>FMKALNIKMKF.txt</c>).
    ///
    /// Two things the original handler got wrong, both visible at
    /// <c>IKMMOCKDKAF.txt:20976-21005</c>:
    ///   * the account ids ride in repeated <c>id</c> params (:20997), not
    ///     <c>playerIds</c>;
    ///   * <c>clubId</c> is NEVER transmitted — it is only the client's local
    ///     cache key — so hard-requiring it 400'd every single call.
    /// The verb is picked at runtime by a cmov (:20984-20985,
    /// <c>cmp rdi,100 / cmovge edx,eax</c> with eax=2): GET under 100 ids,
    /// POST at 100+. Both are registered.
    ///
    /// With no club scope the answer is each account's most privileged
    /// membership across all clubs, which is what the badge renders anyway;
    /// an explicit <c>clubId</c> (2020 watch, admin tools) still narrows it.
    /// </summary>
    [HttpGet("/members/bulk")]
    [HttpPost("/members/bulk")]
    public async Task<IActionResult> MembersBulk()
    {
        var form = Request.HasFormContentType ? await Request.ReadFormAsync() : null;

        IEnumerable<string?> Values(string key) => form is null
            ? Request.Query[key].AsEnumerable()
            : Request.Query[key].Concat(form[key]);

        var ids = new[] { "id", "playerIds", "accountIds" }
            .SelectMany(Values)
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(s => long.TryParse(s, out var n) ? n : 0)
            .Where(n => n != 0)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return Ok(Array.Empty<object>());

        long? clubId = long.TryParse(Request.Query["clubId"].ToString(), out var q) ? q
            : form is not null && long.TryParse(form["clubId"].ToString(), out var f) ? f
            : null;

        var rows = clubId is long scope
            ? await clubs.MembershipsBulkAsync(scope, ids)
            : await db.ClubMemberships.AsNoTracking()
                .Where(m => ids.Contains(m.PlayerId))
                .ToListAsync();

        // One row per account: highest membership type wins (Creator > CoOwner >
        // Moderator > Member), and ban/pending markers lose to a real role.
        return Ok(rows
            .GroupBy(m => m.PlayerId)
            .Select(g => new
            {
                AccountId = (int)g.Key,
                MembershipType = g.Max(m => ClubService.MembershipTypeFromPerms(m.Permissions)),
            })
            .ToList());
    }

    // NOTE: POST /api/clubreporting/v1/report is served by
    // CompatibilityFeatureController.ClubReport, which persists the same
    // ReportEntity AND returns the { Success, Message } object the 2023
    // client requires. A second handler used to live here; having both bind
    // the identical verb+template made every club report 500 with
    // AmbiguousMatchException, so this duplicate was removed.

    // ── Announcements (per-club feed + per-announcement) ─────────────

    /// <summary>GET <c>/announcements/club/{clubId}</c> — full announcement
    /// feed for one club, newest first.
    ///
    /// This returns a SINGLE envelope object, never an array: 2023 reads it as
    /// <c>FGLDKEJLAKB&lt;FIAKMDGGIHH&gt;</c>
    /// (<c>IKMMOCKDKAF.txt:1170</c>, route at :1452) and the 2020 watch as
    /// <c>IPromise&lt;HPACLJHLHBG&gt;</c> (<c>JDJGIBLMFKK.txt:1012,1167</c>).
    /// Handing either of them the per-club LIST that the unread rollup uses
    /// failed the reader, so the club announcement board never rendered — and
    /// a club with no announcements produced <c>[]</c>, not an object at
    /// all.</summary>
    [HttpGet("/announcements/club/{clubId:long}")]
    public async Task<IActionResult> AnnouncementsForClub(long clubId)
    {
        var rows = await clubs.AnnouncementsForClubAsync(clubId);
        var lastRead = ControllerBaseExtensions.CurrentPlayerId(User) is long pid
            ? await LastReadByClubAsync(pid, new[] { clubId })
            : new Dictionary<long, long>();
        return Ok(AnnouncementEnvelope(clubId, rows, lastRead.GetValueOrDefault(clubId)));
    }

    /// <summary>GET <c>/announcements/club/{clubId}/{aid}</c> — single
    /// announcement read. Returns the inner Announcement wire object
    /// (<c>NFEMLMAFFIP</c>) without the per-club envelope.</summary>
    [HttpGet("/announcements/club/{clubId:long}/{announcementId:long}")]
    public async Task<IActionResult> AnnouncementSingle(long clubId, long announcementId)
    {
        var row = await clubs.AnnouncementAsync(clubId, announcementId);
        if (row is null) return NotFound();
        return Ok(ToWireAnnouncement(row));
    }

    /// <summary><c>/announcements/club/{clubId}/{aid}</c> delete —
    /// register DELETE, PUT, and POST so the watch's mutation verb
    /// reaches the same handler regardless of how its request-builder
    /// shaped the call. The service is idempotent on missing rows so
    /// double-fires are safe.</summary>
    [HttpDelete("/announcements/club/{clubId:long}/{announcementId:long}")]
    [HttpPost("/announcements/club/{clubId:long}/{announcementId:long}")]
    [Authorize]
    public async Task<IActionResult> AnnouncementDelete(long clubId, long announcementId)
    {
        if (!await clubs.DeleteAnnouncementAsync(clubId, announcementId, Me)) return Forbid();
        return Ok();
    }

    /// <summary>PUT <c>/announcements/club/{clubId}/{announcementId}</c> — EDIT
    /// an announcement.
    ///
    /// PUT used to be a third binding on <see cref="AnnouncementDelete"/>, so
    /// editing an announcement silently DELETED it — the edit form's save
    /// button destroyed the post it was editing. It is its own handler now.</summary>
    [HttpPut("/announcements/club/{clubId:long}/{announcementId:long}")]
    [Authorize]
    public async Task<IActionResult> AnnouncementEdit(long clubId, long announcementId)
    {
        var (title, body, imageName) = await ReadAnnouncementFieldsAsync();
        if (!await clubs.UpdateAnnouncementAsync(clubId, announcementId, Me, title, body, imageName))
            return Forbid();
        return Ok();
    }

    /// <summary>POST <c>/announcements/club/{clubId}</c> — CREATE an
    /// announcement. The client posts the fields to the COLLECTION url and
    /// reads the response as a bare Int64 (the new announcement id). No POST
    /// was registered at that template, so posting a club announcement 404'd.</summary>
    [HttpPost("/announcements/club/{clubId:long}")]
    [Authorize]
    public async Task<IActionResult> AnnouncementCreate(long clubId)
    {
        var (title, body, imageName) = await ReadAnnouncementFieldsAsync();
        var id = await clubs.CreateAnnouncementAsync(
            clubId, Me, title ?? string.Empty, body ?? string.Empty, imageName ?? string.Empty);
        if (id is null) return Forbid();
        return Content(id.Value.ToString(), "application/json");
    }

    /// <summary>Announcement fields as the client sends them: form-urlencoded,
    /// with a JSON body accepted as a fallback. Null means "not supplied", so
    /// an edit can leave a field alone.</summary>
    private async Task<(string? Title, string? Body, string? ImageName)> ReadAnnouncementFieldsAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            string? F(params string[] keys)
            {
                foreach (var k in keys)
                    if (form.TryGetValue(k, out var v) && v.Count > 0) return v.ToString();
                return null;
            }
            return (F("title", "Title"), F("body", "Body"), F("imageName", "ImageName"));
        }

        try
        {
            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(Request.Body);
            Request.Body.Position = 0;
            string? P(params string[] keys)
            {
                foreach (var k in keys)
                    if (doc.RootElement.TryGetProperty(k, out var v) &&
                        v.ValueKind == System.Text.Json.JsonValueKind.String) return v.GetString();
                return null;
            }
            return (P("title", "Title"), P("body", "Body"), P("imageName", "ImageName"));
        }
        catch (System.Text.Json.JsonException) { return (null, null, null); }
    }

    // ── Wire-shape mappers ──────────────────────────────────────────

    /// <summary>
    /// Build the PIHMJGCGNLP envelope around a single club row. Used
    /// by /club/{id}/details, /clubhouse, and every modify* handler.
    /// MyMembershipType is derived from the caller's row (or
    /// NotAMember sentinel when the caller has no row).
    /// </summary>
    private async Task<IActionResult> BuildDetailsResponseAsync(long clubId)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        var memberCount = await clubs.MemberCountAsync(clubId);
        var pid = ControllerBaseExtensions.CurrentPlayerId(User);
        var myType = ClubService.MembershipTypeNotMember;
        if (pid is long me)
        {
            var membership = await clubs.MembershipForAsync(clubId, me);
            if (membership is not null)
                myType = ClubService.MembershipTypeFromPerms(membership.Permissions);
            // Owner shortcut — the creator row may not exist (legacy data) but the
            // creator should always read as Owner.
            if (club.CreatorPlayerId == me) myType = ClubService.MembershipTypeOwner;
        }
        return Ok(new
        {
            // All seven keys are mandatory — 2020's PIHMJGCGNLP deserializer
            // (PIHMJGCGNLP.txt:143-194) throws KeyNotFoundException on a missing
            // one, and 2023's LCLFBBPEMIH registers exactly these names
            // (JAHJGFHFKIB.txt:523-678). AdditionalImages stays empty until the
            // gallery slots get a table; see ClubAdditionalImage.
            Club = ToWireClub(club, memberCount),
            CustomTags = await CustomTagsAsync(clubId),
            AdditionalImages = Array.Empty<object>(),
            CoownerPermissions = PermissionsForRole(clubId, ClubService.MembershipTypeCoOwner),
            ModeratorPermissions = PermissionsForRole(clubId, ClubService.MembershipTypeModerator),
            MemberPermissions = PermissionsForRole(clubId, ClubService.MembershipTypeMember),
            MyMembershipType = myType,
        });
    }

    /// <summary>The club's player-authored tags, for the envelope's
    /// <c>CustomTags</c> (<c>List&lt;String&gt;</c> on both clients). These used
    /// to be a hardcoded empty array; they are stored in the existing club↔tag
    /// junction so the value the client PUT actually comes back.</summary>
    private Task<List<string>> CustomTagsAsync(long clubId) =>
        (from a in db.ClubCategoryAssignments
         join t in db.ClubCategoryTags on a.CategoryTagId equals t.Id
         where a.ClubId == clubId
         orderby t.OrderIndex, t.Name
         select t.Name).ToListAsync();

    /// <summary>Replace a club's tag assignments wholesale — the client always
    /// PUTs the complete <c>customTags</c> set, never a delta.</summary>
    private async Task ReplaceCustomTagsAsync(long clubId, List<string> tags)
    {
        var wanted = tags
            .Select(t => (t ?? string.Empty).Trim())
            .Where(t => t.Length > 0 && t.Length <= 64)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var stale = await db.ClubCategoryAssignments
            .Where(a => a.ClubId == clubId)
            .ToListAsync();
        db.ClubCategoryAssignments.RemoveRange(stale);

        foreach (var name in wanted)
        {
            var lower = name.ToLower();
            var tag = await db.ClubCategoryTags.FirstOrDefaultAsync(t => t.Name.ToLower() == lower);
            if (tag is null)
            {
                // A player-authored tag is not part of the admin-curated
                // category list, so it lands Active=false: assignments still
                // resolve by id, but /club/categoryTags keeps returning only
                // the curated set the browse tab is built from.
                tag = new ClubCategoryTagEntity { Name = name, OrderIndex = 1000, Active = false };
                db.ClubCategoryTags.Add(tag);
                await db.SaveChangesAsync();
            }
            db.ClubCategoryAssignments.Add(new ClubCategoryAssignmentEntity
            {
                ClubId = clubId,
                CategoryTagId = tag.Id,
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The permission record for one role on a club.
    ///
    /// These three envelope keys used to carry a plain bitmask int. Both
    /// clients declare them as a COMPLEX OBJECT — 2023
    /// <c>LCLFBBPEMIH.JNOKOILEFHD/OMMHPBFEHBH/IJKKPOBDJGG</c> are
    /// <c>MMOCDPPONNG</c> (recnet-runtime-decomp/LCLFBBPEMIH.cs:64-115) and the
    /// 2020 watch's <c>PIHMJGCGNLP</c> uses the identically-laid-out
    /// <c>JHEEFBMODPG</c> — so feeding an integer into the object reader threw
    /// and took the whole club page (and club creation) down with it.
    ///
    /// Key names are verbatim from the generated reader
    /// <c>BPHCOIBNCDP.txt:587-766</c>: ClubId, <b>Type</b> (not
    /// "MembershipType"), EditDetails, ApproveMember, CreateEvent,
    /// PostAnnouncement, EditPermissionSettings, BanUnban. The same six names
    /// are the form keys the client PUTs back on the permissions screen
    /// (<c>IKMMOCKDKAF_NestedType_BOIMHOCCOEI.txt:173-253</c>).
    ///
    /// The bools are derived from the role rather than stored: there is no
    /// per-role permission table yet, so every club uses the default policy —
    /// Owner/CoOwner do everything, Moderator moderates but cannot re-write the
    /// permission settings, Member and below are read-only.
    /// </summary>
    private static object PermissionsForRole(long clubId, int membershipType)
    {
        var admin = membershipType is ClubService.MembershipTypeOwner
                                   or ClubService.MembershipTypeCoOwner;
        var moderator = admin || membershipType == ClubService.MembershipTypeModerator;
        return new
        {
            ClubId = clubId,
            Type = membershipType,
            EditDetails = admin,
            ApproveMember = moderator,
            CreateEvent = moderator,
            PostAnnouncement = moderator,
            EditPermissionSettings = admin,
            BanUnban = moderator,
        };
    }

    private static object ToWireMembership(ClubMembershipEntity row) => new
    {
        AccountId = (int)row.PlayerId,
        ClubId = row.ClubId,
        MembershipType = ClubService.MembershipTypeFromPerms(row.Permissions),
        CreatedAt = row.JoinedAt,
    };

    private static ClubEntity EmptyClub() => new()
    {
        Id = 0,
        Name = string.Empty,
        Description = string.Empty,
        CreatorPlayerId = 0,
        ImageName = string.Empty,
    };

    /// <summary>
    /// Project a <see cref="ClubEntity"/> to the 2020.12 Club wire
    /// shape. JSON keys + types match the
    /// <c>PLILLKHMNDA.Deserialize</c> body in ISIL exactly:
    /// ClubId/State/CreatorAccountId/Visibility/Joinability/MemberCount
    /// are ints, Name/Description/MainImageName/Category are strings,
    /// AllowJuniors/IsRRO are bools, ClubhouseRoomId is nullable long.
    /// </summary>
    private static object ToWireClub(ClubEntity c, int memberCount) => new
    {
        ClubId = c.Id,
        c.Name,
        c.Description,
        MainImageName = c.ImageName,
        State = c.State,
        CreatorAccountId = (int)c.CreatorPlayerId,
        Category = c.Category,
        Visibility = c.Visibility,
        Joinability = c.Joinability,
        AllowJuniors = c.AllowJuniors,
        MemberCount = memberCount,
        // MinLevel + ClubChatEnabled are registered by the 2023 Club reader
        // (BLIBJIHOENF.txt:1234-1322) and were never emitted, so every club came
        // back with chat disabled on the profile Clubs tab. Neither has a column
        // on ClubEntity yet — the client-facing setters are club/{0}/minlevel
        // and club/{0}/clubChatEnabled (IKMMOCKDKAF.txt:16561,25779) — so they
        // report the permissive defaults until the schema catches up.
        MinLevel = 0,
        ClubChatEnabled = true,
        IsRRO = c.IsRRO,
        ClubhouseRoomId = c.ClubhouseRoomId,
        ClubType = c.ClubType,
    };

    /// <summary>
    /// Project a <see cref="ClubAnnouncementEntity"/> to the 2020.12
    /// Announcement wire shape. Keys + types per the
    /// <c>NFEMLMAFFIP.Deserialize</c> body in ISIL.
    /// </summary>
    private static object ToWireAnnouncement(ClubAnnouncementEntity a) => new
    {
        AnnouncementId = a.Id,
        CreatorAccountId = (int)a.AuthorPlayerId,
        ClubId = a.ClubId,
        a.Title,
        a.Body,
        a.ImageName,
        a.CreatedAt,
        Meta = string.Empty,
    };

    private async Task<CreateClubRequest> ReadCreateClubRequestAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            return new CreateClubRequest
            {
                Name = FirstFormValue(form, "name", "Name"),
                Description = FirstFormValue(form, "description", "Description"),
                MainImageName = FirstFormValue(form,
                    "mainImageName", "MainImageName", "imageName", "ImageName"),
                Category = FirstFormValue(form, "category", "Category"),
            };
        }

        try
        {
            return await Request.ReadFromJsonAsync<CreateClubRequest>(JsonOptions)
                ?? new CreateClubRequest();
        }
        catch (JsonException)
        {
            return new CreateClubRequest();
        }
    }

    private static string? FirstFormValue(IFormCollection form, params string[] names)
    {
        foreach (var name in names)
        {
            var value = form[name].ToString();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Group a flat list of announcements into per-club envelopes — the shape
    /// the two unread rollups return as a LIST (2023
    /// <c>List&lt;NCHLBFPHFJE&gt;</c>, 2020 <c>List&lt;EIKPFIDCKNE&gt;</c>).
    /// </summary>
    private static List<Dictionary<string, object?>> GroupByClub(
        List<ClubAnnouncementEntity> rows, Dictionary<long, long> lastReadByClub)
    {
        return rows
            .GroupBy(a => a.ClubId)
            .Select(g => AnnouncementEnvelope(g.Key, g.ToList(), lastReadByClub.GetValueOrDefault(g.Key)))
            .ToList();
    }

    /// <summary>
    /// One club's announcement envelope. Uses
    /// <see cref="Dictionary{TKey, TValue}"/> rather than an anonymous type
    /// because the wire mixes casings inside a single record: the 2020 readers
    /// match key names EXACTLY and want <c>clubId</c> + <c>announcements</c>
    /// lowercase but <c>LastAnnouncementId</c>/<c>LastReadAnnouncementId</c>
    /// PascalCase (<c>EIKPFIDCKNE.txt</c>, <c>HPACLJHLHBG.txt:270-280</c>).
    /// The 2023 readers are tri-cased (<c>CAKCKAMAAPP.txt:331-414</c>,
    /// <c>AECJMONHBHM.txt:267-326</c>) so camelCase satisfies them too.
    ///
    /// Two fixes over the original projection:
    ///   * <c>LastAnnouncementId</c> was omitted entirely though both readers
    ///     register it;
    ///   * <c>LastReadAnnouncementId</c> was an explicit JSON <c>null</c> into
    ///     a NON-nullable Int64 field on both clients. Always a number now.
    /// </summary>
    private static Dictionary<string, object?> AnnouncementEnvelope(
        long clubId, List<ClubAnnouncementEntity> rows, long lastReadId)
    {
        var ordered = rows.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).ToList();
        return new Dictionary<string, object?>
        {
            ["clubId"] = clubId,
            ["LastAnnouncementId"] = ordered.Count == 0 ? 0L : ordered.Max(a => a.Id),
            ["LastReadAnnouncementId"] = lastReadId,
            ["announcements"] = ordered.Select(ToWireAnnouncement).ToList(),
        };
    }

    /// <summary>
    /// Highest announcement id the caller has actually read, per club — the
    /// <c>LastReadAnnouncementId</c> the envelopes above must carry. Done as
    /// two index-supported reads plus an in-memory join for the same reason
    /// <see cref="ClubService"/> avoids the LEFT JOIN: EF Core generates
    /// pathological SQL for the anti-join on SQLite.
    /// </summary>
    private async Task<Dictionary<long, long>> LastReadByClubAsync(
        long playerId, IEnumerable<long> clubIds)
    {
        var ids = clubIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, long>();

        var readIds = await db.ClubAnnouncementReads
            .Where(r => r.PlayerId == playerId)
            .Select(r => r.AnnouncementId)
            .ToListAsync();
        if (readIds.Count == 0) return new Dictionary<long, long>();

        var read = await db.ClubAnnouncements
            .Where(a => ids.Contains(a.ClubId) && readIds.Contains(a.Id))
            .Select(a => new { a.ClubId, a.Id })
            .ToListAsync();

        return read
            .GroupBy(a => a.ClubId)
            .ToDictionary(g => g.Key, g => g.Max(a => a.Id));
    }
}
