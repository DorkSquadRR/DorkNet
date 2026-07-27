using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using DorkNet.Server.Auth;
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
///   Club            (<c>PLILLKHMNDA</c>): ClubId, Name, Description,
///                   MainImageName, State, CreatorAccountId, Category,
///                   Visibility, Joinability, AllowJuniors, MemberCount,
///                   IsRRO, ClubhouseRoomId, ClubType
///   Announcement    (<c>NFEMLMAFFIP</c>): AnnouncementId,
///                   CreatorAccountId, ClubId, Title, Body, ImageName,
///                   CreatedAt, Meta
///   UnreadResponse  (<c>HPACLJHLHBG</c>) — wraps per-club rows:
///                   { clubId, LastReadAnnouncementId, announcements: [Announcement] }
///   CategoryTags    plain <c>List&lt;String&gt;</c> (per
///                   <c>JDJGIBLMFKK.GetPrimaryTags</c> callback shape).
/// </summary>
[ApiController]
public class ClubsController(ClubService clubs) : ControllerBase
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
        return Ok(GroupByClub(rows));
    }

    [HttpGet("/announcements/v2/subscription/mine/unread")]
    [Authorize]
    public async Task<IActionResult> UnreadSubscription()
    {
        var pid = Me;
        var rows = await clubs.UnreadSubscriptionAsync(pid);
        return Ok(GroupByClub(rows));
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

    [HttpGet("/subscription/details/{accountId:long}")]
    public async Task<IActionResult> SubscriptionDetails(long accountId)
    {
        var viewerId = this.CurrentPlayerId();
        var subscriberCount = await clubs.PlayerSubscriberCountAsync(accountId);
        var subscribedCount = await clubs.PlayerSubscribedCountAsync(accountId);
        var isSubscribed = viewerId is long viewer
            && await clubs.IsSubscribedToPlayerAsync(viewer, accountId);
        return Ok(new
        {
            accountId,
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

    /// <summary>GET <c>/club/{id}/permissions/{role}</c> — bare int of
    /// the permission bitmask for a role on this club. Used by the
    /// watch's role-permissions screen. Returns 0 for unknown roles so
    /// the screen renders an empty checklist instead of erroring.</summary>
    [HttpGet("/club/{clubId:long}/permissions/{role:int}")]
    public async Task<IActionResult> ClubPermissions(long clubId, int role)
    {
        var club = await clubs.GetByIdAsync(clubId);
        if (club is null) return NotFound();
        return Ok(PermissionsForRole(role));
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

    /// <summary>POST <c>/club/{id}/additionalimage/{slot}</c> — the
    /// watch's gallery-image slot setter. The 2020 ClubEntity doesn't
    /// have additional image slots persisted yet, so we just return
    /// the current details unchanged (the gallery renders blank).
    /// Hooked up so the request doesn't 404 — populated once a future
    /// schema patch lands.</summary>
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

    /// <summary>GET <c>/members/bulk?clubId=N&amp;playerIds=A&amp;playerIds=B</c>
    /// — bulk membership lookup. Wire shape is
    /// <c>List&lt;JHMMGLNJHIB&gt;</c> with each entry carrying
    /// <c>AccountId</c> + <c>MembershipType</c>.</summary>
    [HttpGet("/members/bulk")]
    [HttpPost("/members/bulk")]
    public async Task<IActionResult> MembersBulk()
    {
        var form = Request.HasFormContentType ? Request.Form : null;
        if (!long.TryParse(Request.Query["clubId"].ToString(), out var clubId)
            && (form is null || !long.TryParse(form["clubId"].ToString(), out clubId)))
            return BadRequest(new { error = "missing_club_id" });

        var idsRaw = form is null
            ? Request.Query["playerIds"].AsEnumerable()
            : Request.Query["playerIds"].Concat(form["playerIds"]);
        var ids = idsRaw
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(s => long.TryParse(s, out var n) ? n : 0)
            .Where(n => n != 0)
            .ToList();
        if (ids.Count == 0) return Ok(Array.Empty<object>());

        var rows = await clubs.MembershipsBulkAsync(clubId, ids);
        return Ok(rows.Select(m => new
        {
            AccountId = (int)m.PlayerId,
            MembershipType = ClubService.MembershipTypeFromPerms(m.Permissions),
        }));
    }

    // NOTE: POST /api/clubreporting/v1/report is served by
    // CompatibilityFeatureController.ClubReport, which persists the same
    // ReportEntity AND returns the { Success, Message } object the 2023
    // client requires. A second handler used to live here; having both bind
    // the identical verb+template made every club report 500 with
    // AmbiguousMatchException, so this duplicate was removed.

    // ── Announcements (per-club feed + per-announcement) ─────────────

    /// <summary>GET <c>/announcements/club/{clubId}</c> — full
    /// announcement feed for the club. Wire shape mirrors the unread
    /// rollup (<c>HPACLJHLHBG</c> envelope) but holds every visible
    /// announcement, newest first.</summary>
    [HttpGet("/announcements/club/{clubId:long}")]
    public async Task<IActionResult> AnnouncementsForClub(long clubId)
    {
        var rows = await clubs.AnnouncementsForClubAsync(clubId);
        return Ok(GroupByClub(rows));
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
            // PIHMJGCGNLP deserializer at PIHMJGCGNLP.txt:143-194 reads
            // 7 fields off the response dict, not the 5 the parser-keys
            // doc lists. Missing any throws KeyNotFoundException —
            // CustomTags (List<String>) and AdditionalImages
            // (List<FKFAKOKIEGN>) need to be present even if empty.
            Club = ToWireClub(club, memberCount),
            CustomTags = Array.Empty<string>(),
            AdditionalImages = Array.Empty<object>(),
            CoownerPermissions = PermissionsForRole(ClubService.MembershipTypeCoOwner),
            ModeratorPermissions = PermissionsForRole(ClubService.MembershipTypeModerator),
            MemberPermissions = PermissionsForRole(ClubService.MembershipTypeMember),
            MyMembershipType = myType,
        });
    }

    /// <summary>
    /// Bitmask of capabilities a given role has on a club. The bits
    /// are arbitrary (the wire enum <c>JHEEFBMODPG</c> is opaque in
    /// the readable dump) but stay consistent so the watch's
    /// role-permissions screen renders the same checklist on every
    /// refresh. Owner / Coowner can manage all; Moderator can manage
    /// content; Member can read.
    /// </summary>
    private static int PermissionsForRole(int membershipType) => membershipType switch
    {
        ClubService.MembershipTypeOwner    => 0x7FFF, // everything
        ClubService.MembershipTypeCoOwner  => 0x7FFE, // all except disband
        ClubService.MembershipTypeModerator => 0x00FF, // moderation only
        ClubService.MembershipTypeMember   => 0x0007, // post + read + react
        _                                  => 0x0001, // read-only
    };

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
    /// Group a flat list of announcements into the per-club
    /// <c>HPACLJHLHBG</c> envelope shape. Uses
    /// <see cref="Dictionary{TKey, TValue}"/> for the outer object
    /// because the wire mixes lowercase and PascalCase keys for the
    /// same record (<c>clubId</c> + <c>LastReadAnnouncementId</c> +
    /// <c>announcements</c>) and anonymous types can't model that.
    /// </summary>
    private static List<Dictionary<string, object?>> GroupByClub(
        List<ClubAnnouncementEntity> rows)
    {
        return rows
            .GroupBy(a => a.ClubId)
            .Select(g =>
            {
                var ordered = g.OrderByDescending(a => a.CreatedAt).ToList();
                return new Dictionary<string, object?>
                {
                    ["clubId"] = g.Key,
                    ["LastReadAnnouncementId"] = (long?)null,
                    ["announcements"] = ordered.Select(ToWireAnnouncement).ToList(),
                };
            })
            .ToList();
    }
}
