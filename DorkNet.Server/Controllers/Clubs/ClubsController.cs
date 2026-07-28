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
///   GET    /club/mine/member
///   GET    /club/mine/created
///   GET    /club/categoryTags
///   GET    /announcements/v2/mine/unread
///   GET    /announcements/v2/subscription/mine/unread
///   POST   /announcements/club/{clubId}/{announcementId}/read
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
