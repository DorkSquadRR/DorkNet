using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Services;

/// <summary>
/// Backs the <c>clubs.localhost/*</c> surface used by the 2020.12 watch:
///   * <c>GET /club/home/me</c>           — composite home tab payload
///   * <c>GET /club/mine/member</c>       — clubs the player is in
///   * <c>GET /club/mine/created</c>      — clubs the player created
///   * <c>GET /club/categoryTags</c>      — admin-curated tag list
///   * <c>GET /announcements/v2/mine/unread</c>           — direct feed
///   * <c>GET /announcements/v2/subscription/mine/unread</c> — subs feed
///
/// Owns no in-memory state — every method round-trips the DbContext so
/// reads stay consistent across replicas. The legacy
/// <c>GroupsController</c> reads + writes the same <see cref="ClubEntity"/>
/// table, so club rows created via <c>api/groups/v1/</c> show up here
/// automatically without backfill.
/// </summary>
public class ClubService(DorkNetDbContext db)
{
    /// <summary>Clubs the player is a member of (any permissions
    /// value above 0 OR a Member=0 row — i.e. presence in the
    /// <see cref="ClubMembershipEntity"/> table). The owner's
    /// auto-membership row inserted by <c>GroupsController.Create</c>
    /// (Permissions=127) also matches.</summary>
    public Task<List<ClubEntity>> MyClubsAsync(long playerId) =>
        (from m in db.ClubMemberships
         join c in db.Clubs on m.ClubId equals c.Id
         where m.PlayerId == playerId && c.State == 0
         orderby c.Name
         select c).ToListAsync();

    /// <summary>Clubs whose <see cref="ClubEntity.CreatorPlayerId"/>
    /// equals the player. The owner usually also has a membership row,
    /// so <see cref="MyClubsAsync"/> + this overlap — the watch's
    /// "Mine" tab renders the union and de-dupes by ClubId.</summary>
    public Task<List<ClubEntity>> CreatedByAsync(long playerId) =>
        db.Clubs
            .Where(c => c.CreatorPlayerId == playerId && c.State == 0)
            .OrderBy(c => c.Name)
            .ToListAsync();

    /// <summary>
    /// Unread announcements for clubs the player is a MEMBER of. Drives
    /// <c>announcements/v2/mine/unread</c>. The "unread" filter is
    /// LEFT JOIN against <see cref="ClubAnnouncementReadEntity"/> for
    /// the calling player — rows with no read-marker count as unread.
    /// </summary>
    public async Task<List<ClubAnnouncementEntity>> UnreadDirectAsync(long playerId)
    {
        var memberClubIds = await db.ClubMemberships
            .Where(m => m.PlayerId == playerId)
            .Select(m => m.ClubId)
            .ToListAsync();
        if (memberClubIds.Count == 0) return new();
        return await UnreadForClubsAsync(playerId, memberClubIds);
    }

    /// <summary>
    /// Unread announcements for clubs the player SUBSCRIBES to (rows
    /// in <see cref="ClubSubscriptionEntity"/>). Drives
    /// <c>announcements/v2/subscription/mine/unread</c>. Same unread
    /// semantics as the direct feed.
    /// </summary>
    public async Task<List<ClubAnnouncementEntity>> UnreadSubscriptionAsync(long playerId)
    {
        var subClubIds = await db.ClubSubscriptions
            .Where(s => s.PlayerId == playerId)
            .Select(s => s.ClubId)
            .ToListAsync();
        if (subClubIds.Count == 0) return new();
        return await UnreadForClubsAsync(playerId, subClubIds);
    }

    private async Task<List<ClubAnnouncementEntity>> UnreadForClubsAsync(
        long playerId, List<long> clubIds)
    {
        // Pull all candidate announcements + the player's read-markers
        // for them in one pass each. EF Core's LEFT-JOIN over a
        // subquery generates pathological SQL on SQLite, so the
        // anti-join is done in memory after two index-supported reads.
        var candidates = await db.ClubAnnouncements
            .Where(a => clubIds.Contains(a.ClubId))
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
        if (candidates.Count == 0) return candidates;
        var candidateIds = candidates.Select(a => a.Id).ToList();
        var readIds = await db.ClubAnnouncementReads
            .Where(r => r.PlayerId == playerId && candidateIds.Contains(r.AnnouncementId))
            .Select(r => r.AnnouncementId)
            .ToListAsync();
        var readSet = new HashSet<long>(readIds);
        return candidates.Where(a => !readSet.Contains(a.Id)).ToList();
    }

    /// <summary>
    /// Active category tags in display order. The 2020.12 watch
    /// deserialises the response as <c>List&lt;String&gt;</c>, so the
    /// controller projects <see cref="ClubCategoryTagEntity.Name"/>
    /// out — the Id/OrderIndex/Active columns are admin-facing only.
    /// </summary>
    public Task<List<ClubCategoryTagEntity>> CategoryTagsAsync() =>
        db.ClubCategoryTags
            .Where(t => t.Active)
            .OrderBy(t => t.OrderIndex)
            .ThenBy(t => t.Name)
            .ToListAsync();

    /// <summary>
    /// Discover-tab search for <c>GET /club/search</c>. The 2020.12
    /// watch can send either a category-only browse request or a text
    /// search; the rows are projected by the controller into the
    /// <c>CAJHIEENJJD</c> search envelope.
    /// </summary>
    public async Task<(List<ClubEntity> Clubs, int TotalClubs)> SearchAsync(
        string? query, string? category, int sort, int count)
    {
        var take = Math.Clamp(count <= 0 ? 30 : count, 1, 100);
        var q = db.Clubs.Where(c => c.State == 0);

        if (!string.IsNullOrWhiteSpace(category))
        {
            var categoryLower = category.Trim().ToLower();
            q = q.Where(c => c.Category.ToLower() == categoryLower);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var queryLower = query.Trim().ToLower();
            q = q.Where(c =>
                c.Name.ToLower().Contains(queryLower) ||
                c.Description.ToLower().Contains(queryLower));
        }

        var total = await q.CountAsync();

        q = sort switch
        {
            1 => q.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Name),
            2 => q.OrderBy(c => c.Name),
            _ => q.OrderByDescending(c => c.ClubType)
                  .ThenByDescending(c => c.UpdatedAt)
                  .ThenBy(c => c.Name),
        };

        var clubs = await q.Take(take).ToListAsync();
        return (clubs, total);
    }

    /// <summary>
    /// Clubs the player is subscribed to, in the lowercase-key wire
    /// shape <c>LNIKPLKOBDK</c> expects per
    /// <c>dist/.../IsilDump/Assembly-CSharp/LNIKPLKOBDK.txt:110-124</c>:
    /// <c>{accountId, clubId, subscriberCount}</c>. One row per
    /// (player, club) subscription row. <c>subscriberCount</c> is the
    /// total subscribers for that club across all players — derived
    /// live from the same table so it stays in sync without a cached
    /// counter that could drift.
    /// </summary>
    public async Task<List<object>> MySubscriptionsAsync(long playerId)
    {
        var mine = await db.ClubSubscriptions
            .Where(s => s.PlayerId == playerId)
            .Select(s => s.ClubId)
            .ToListAsync();
        if (mine.Count == 0) return new List<object>();

        var counts = await db.ClubSubscriptions
            .Where(s => mine.Contains(s.ClubId))
            .GroupBy(s => s.ClubId)
            .Select(g => new { ClubId = g.Key, Count = g.Count() })
            .ToListAsync();
        var byClub = counts.ToDictionary(c => c.ClubId, c => c.Count);

        return mine
            .Select(clubId => (object)new
            {
                accountId = playerId,
                clubId = clubId,
                subscriberCount = byClub.TryGetValue(clubId, out var c) ? c : 0,
            })
            .ToList();
    }

    /// <summary>
    /// Composite home-tab payload for <c>GET /club/home/me</c>. The
    /// watch's deserialiser for this endpoint expects a single
    /// <c>RecNet.Club</c> object (<c>typeof(System.Action`1&lt;PLILLKHMNDA&gt;)</c>
    /// per ISIL) — typically the player's "home club" (set via
    /// <c>SetMyHomeClub</c>) rendered as the headline card. We pick the
    /// first club the player is a member of, ordered by membership
    /// recency. Returns null when the player has no club memberships
    /// — the controller turns that into <see cref="Microsoft.AspNetCore.Mvc.NoContentResult"/>
    /// so the watch knows to render the "join a club" empty state.
    /// </summary>
    public async Task<ClubEntity?> HomeClubAsync(long playerId)
    {
        // Most-recent-joined club wins. Owners are members too (the
        // /create flow inserts an owner-permissions=127 membership
        // row), so a brand-new club creator sees their fresh club here.
        var pick = await (from m in db.ClubMemberships
                          join c in db.Clubs on m.ClubId equals c.Id
                          where m.PlayerId == playerId && c.State == 0
                          orderby m.JoinedAt descending
                          select c).FirstOrDefaultAsync();
        return pick;
    }

    /// <summary>
    /// Upserts a read-marker so subsequent <see cref="UnreadDirectAsync"/>
    /// / <see cref="UnreadSubscriptionAsync"/> calls skip the
    /// announcement. Idempotent — calling twice is a no-op (the unique
    /// index on <c>(AnnouncementId, PlayerId)</c> guarantees one row).
    /// </summary>
    public async Task MarkAnnouncementReadAsync(long playerId, long announcementId)
    {
        var existing = await db.ClubAnnouncementReads
            .FirstOrDefaultAsync(r => r.PlayerId == playerId
                                   && r.AnnouncementId == announcementId);
        if (existing is not null) return;
        db.ClubAnnouncementReads.Add(new ClubAnnouncementReadEntity
        {
            AnnouncementId = announcementId,
            PlayerId = playerId,
            ReadAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Live count of approved members for a club (used by the wire
    /// <c>MemberCount</c> int — see <see cref="ClubEntity"/> doc).
    /// </summary>
    public Task<int> MemberCountAsync(long clubId) =>
        db.ClubMemberships.CountAsync(m => m.ClubId == clubId);

    /// <summary>Subscribers TO a club (everyone who hit subscribe on
    /// the club page). Backs <c>/subscription/subscriberCount/{id}</c>
    /// when the id resolves to a club.</summary>
    public Task<int> ClubSubscriberCountAsync(long clubId) =>
        db.ClubSubscriptions.CountAsync(s => s.ClubId == clubId);

    /// <summary>Subscribers TO a player (everyone who hit subscribe on
    /// the player's profile — the pre-club subscription model in
    /// <see cref="DorkNetDbContext.Subscriptions"/>). Backs
    /// <c>/subscription/subscriberCount/{id}</c> when the id resolves
    /// to a player account.</summary>
    public Task<int> PlayerSubscriberCountAsync(long playerId) =>
        db.Subscriptions.CountAsync(s => s.TargetPlayerId == playerId);

    /// <summary>How many player/profile subscriptions this account has
    /// made.</summary>
    public Task<int> PlayerSubscribedCountAsync(long playerId) =>
        db.Subscriptions.CountAsync(s => s.SubscriberPlayerId == playerId);

    public Task<bool> IsSubscribedToPlayerAsync(long subscriberId, long targetPlayerId) =>
        db.Subscriptions.AnyAsync(s => s.SubscriberPlayerId == subscriberId
                                    && s.TargetPlayerId == targetPlayerId);

    /// <summary>
    /// Bulk member-count lookup so the controller can project a list
    /// of clubs in one round-trip. Returns a dict keyed by ClubId; any
    /// club without members maps to 0 in the caller's projection.
    /// </summary>
    public async Task<Dictionary<long, int>> MemberCountsAsync(IEnumerable<long> clubIds)
    {
        var ids = clubIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var rows = await db.ClubMemberships
            .Where(m => ids.Contains(m.ClubId))
            .GroupBy(m => m.ClubId)
            .Select(g => new { ClubId = g.Key, Count = g.Count() })
            .ToListAsync();
        return rows.ToDictionary(r => r.ClubId, r => r.Count);
    }

    /// <summary>Single club lookup by id. Returns null for missing
    /// or disbanded (State != 0) clubs so the controller surfaces 404
    /// before any wire-shape projection runs.</summary>
    public Task<ClubEntity?> GetByIdAsync(long clubId) =>
        db.Clubs.FirstOrDefaultAsync(c => c.Id == clubId && c.State == 0);

    /// <summary>Single club lookup by exact display name. Used by
    /// <c>GET /club?name=...</c>; case-insensitive matching keeps the
    /// browse/name-resolve path stable across SQLite and Postgres
    /// collations.</summary>
    public Task<ClubEntity?> GetByNameAsync(string name)
    {
        var trimmed = name.Trim().ToLower();
        return db.Clubs.FirstOrDefaultAsync(c => c.State == 0 && c.Name.ToLower() == trimmed);
    }

    /// <summary>Single membership row lookup. Used by both the
    /// /club/{id}/members/{playerId} read and every member-mutation
    /// endpoint to verify the target row exists before acting.</summary>
    public Task<ClubMembershipEntity?> MembershipForAsync(long clubId, long playerId) =>
        db.ClubMemberships.FirstOrDefaultAsync(m => m.ClubId == clubId && m.PlayerId == playerId);

    /// <summary>Bulk member-row lookup for <c>members/bulk</c>. Returns
    /// only rows that exist; missing playerIds are silently dropped (the
    /// watch handles a shorter list fine — it just renders no row).</summary>
    public Task<List<ClubMembershipEntity>> MembershipsBulkAsync(long clubId, IEnumerable<long> playerIds)
    {
        var ids = playerIds.Distinct().ToList();
        if (ids.Count == 0) return Task.FromResult(new List<ClubMembershipEntity>());
        return db.ClubMemberships
            .Where(m => m.ClubId == clubId && ids.Contains(m.PlayerId))
            .ToListAsync();
    }

    /// <summary>List clubs an arbitrary player is a MEMBER of. Drives
    /// <c>account/{playerId}/clubs</c> on a profile card; uses the same
    /// State=0 filter as the my-clubs read so disbanded clubs stay
    /// hidden.</summary>
    public Task<List<ClubEntity>> ClubsForPlayerAsync(long playerId) =>
        (from m in db.ClubMemberships
         join c in db.Clubs on m.ClubId equals c.Id
         where m.PlayerId == playerId && c.State == 0
         orderby c.Name
         select c).ToListAsync();

    /// <summary>
    /// 2020.12 <c>PPGPAHNMGEC MembershipType</c> wire enum verified
    /// against <c>dist\RecRoom-2020.12.18-dump\DiffableCs\Assembly-CSharp\PPGPAHNMGEC.cs</c>.
    /// These ARE the values the watch decodes — earlier 0..N guesses
    /// were wrong: <c>MyMembershipType=4</c> would have rendered as
    /// "Pending_Denied" instead of "Creator" and disabled every owner
    /// affordance in the watch UI.
    /// </summary>
    public const int MembershipTypeBanned = -1;
    public const int MembershipTypeNotMember = 0;
    public const int MembershipTypeRequested = 1;   // Pending_Requested
    public const int MembershipTypeInvited = 2;     // Pending_Invited
    public const int MembershipTypeDenied = 3;      // Pending_Denied
    public const int MembershipTypeMember = 10;
    public const int MembershipTypeModerator = 20;
    public const int MembershipTypeCoOwner = 30;
    public const int MembershipTypeOwner = 100;     // Creator on the wire

    public static int MembershipTypeFromPerms(int perms)
    {
        // 256 = ban marker (set by /members/ban handler).
        if ((perms & 256) != 0) return MembershipTypeBanned;
        // 128 = pending (set by /members/invite + /requesttojoin handlers).
        // We don't yet distinguish invite-from vs request-to on the perms
        // int; surface Requested by default so the caller's "I asked to
        // join" UI lights up. (Outgoing invites from owners use the same
        // marker; the watch shows them under "pending invites" either
        // way because the deserializer only branches on
        // Member/Moderator/CoOwner/Creator vs Pending_*.)
        if ((perms & 128) != 0) return MembershipTypeRequested;
        if (perms >= 127) return MembershipTypeOwner;
        if (perms >= 124) return MembershipTypeCoOwner;
        if (perms >= 24) return MembershipTypeModerator;
        return MembershipTypeMember;
    }

    /// <summary>Inverse of <see cref="MembershipTypeFromPerms"/> for
    /// <c>changetype</c>. Returns the canonical perms int to assign
    /// when the caller sets a new role. Owner (Creator=100 on the wire)
    /// is intentionally excluded — promotion to Owner is a
    /// transfer-ownership flow, not a changetype call.</summary>
    public static int PermsFromMembershipType(int membershipType) => membershipType switch
    {
        MembershipTypeCoOwner => 124,
        MembershipTypeModerator => 24,
        MembershipTypeMember => 0,
        _ => 0,
    };

    /// <summary>
    /// Persist a club-targeted moderation report. Stores into the same
    /// <see cref="ReportEntity"/> table the admin queue reads from, with
    /// <see cref="ReportEntity.TargetRoomId"/> carrying the clubId (we
    /// don't have a separate TargetClubId column yet — the admin SPA
    /// renders any non-zero target field on the report card, so this
    /// surfaces as a club report there too via the Message prefix).
    /// </summary>
    public async Task AddClubReportAsync(long clubId, long reporterId, int category, string message)
    {
        db.Reports.Add(new ReportEntity
        {
            ReporterPlayerId = reporterId,
            TargetPlayerId = 0,
            TargetRoomId = clubId,
            Category = category,
            Message = $"[club {clubId}] {message ?? string.Empty}",
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Create a new club + the owner's membership row in one go. Per
    /// the 2020.12 wire <c>club/create</c> response shape, the caller
    /// projects the resulting <see cref="ClubEntity"/> into the
    /// <c>PIHMJGCGNLP</c> envelope via the controller's wire mappers.
    /// Returns null when the name collides with an existing club so
    /// the caller can surface a 409.
    /// </summary>
    public async Task<ClubEntity?> CreateAsync(long creatorId, string name, string? description, string? imageName, string? category)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) return null;
        if (await db.Clubs.AnyAsync(c => c.Name == trimmed)) return null;
        var club = new ClubEntity
        {
            Name = trimmed,
            Description = description ?? string.Empty,
            ImageName = imageName ?? string.Empty,
            Category = category ?? string.Empty,
            CreatorPlayerId = creatorId,
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync();
        db.ClubMemberships.Add(new ClubMembershipEntity
        {
            ClubId = club.Id,
            PlayerId = creatorId,
            Permissions = 127, // Owner
        });
        await db.SaveChangesAsync();
        return club;
    }

    /// <summary>Apply a free-form mutation to a club row after
    /// verifying the caller is owner/co-owner. Returns the updated
    /// entity on success, null on missing club, or throws
    /// <see cref="UnauthorizedAccessException"/> when the caller can't
    /// modify. Used by every <c>club/{id}/modify*</c> endpoint variant.</summary>
    public async Task<ClubEntity?> ModifyAsync(long clubId, long callerId, Action<ClubEntity> mutator)
    {
        var club = await db.Clubs.FirstOrDefaultAsync(c => c.Id == clubId && c.State == 0);
        if (club is null) return null;
        if (!await CanManageAsync(clubId, callerId, club))
            throw new UnauthorizedAccessException();
        mutator(club);
        club.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return club;
    }

    /// <summary>
    /// Check whether the caller can perform admin-class mutations on a
    /// club (owner, co-owner, or moderator depending on the action).
    /// Owner always wins; co-owner and moderator are looked up via the
    /// membership perms int. Used by every club/{id}/modify* and
    /// member mutation route.
    /// </summary>
    public async Task<bool> CanManageAsync(long clubId, long callerId, ClubEntity? club = null)
    {
        club ??= await db.Clubs.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clubId);
        if (club is null) return false;
        if (club.CreatorPlayerId == callerId) return true;
        var membership = await db.ClubMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ClubId == clubId && m.PlayerId == callerId);
        if (membership is null) return false;
        // Pending invites/requests (128) and ban markers (256) must NOT
        // grant moderation rights. Without this mask a pending invitee
        // with perms=128 evaluates `>= 24` true and inherits the ability
        // to ban/promote — full moderation hijack via "I'd like to join".
        var perms = membership.Permissions;
        if ((perms & 128) != 0 || (perms & 256) != 0) return false;
        return perms >= 24;
    }

    /// <summary>Upsert a membership row at a target role. Used by
    /// invite/request/accept/changetype/directJoin/etc. Returns the
    /// row for callers that need to inspect or surface it; for void
    /// endpoints the caller just discards the result.</summary>
    public async Task<ClubMembershipEntity> UpsertMembershipAsync(long clubId, long targetPid, int perms)
    {
        var row = await db.ClubMemberships
            .FirstOrDefaultAsync(m => m.ClubId == clubId && m.PlayerId == targetPid);
        if (row is null)
        {
            row = new ClubMembershipEntity
            {
                ClubId = clubId,
                PlayerId = targetPid,
                Permissions = perms,
            };
            db.ClubMemberships.Add(row);
        }
        else
        {
            row.Permissions = perms;
        }
        await db.SaveChangesAsync();
        return row;
    }

    /// <summary>Remove a membership row entirely — used by leave,
    /// remove, ban (after stamping a separate ban row if/when we
    /// introduce one), declineinvite, denyrequest. Idempotent: missing
    /// row → no-op.</summary>
    public async Task RemoveMembershipAsync(long clubId, long targetPid)
    {
        var row = await db.ClubMemberships
            .FirstOrDefaultAsync(m => m.ClubId == clubId && m.PlayerId == targetPid);
        if (row is null) return;
        db.ClubMemberships.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>List the announcements for a single club in newest-first
    /// order. Drives <c>announcements/club/{cid}</c> when the watch
    /// renders the club's full announcement feed (not just the unread
    /// rollup the home tab uses).</summary>
    public Task<List<ClubAnnouncementEntity>> AnnouncementsForClubAsync(long clubId, int take = 50) =>
        db.ClubAnnouncements
            .Where(a => a.ClubId == clubId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .ToListAsync();

    /// <summary>Single announcement lookup by (clubId, announcementId).
    /// Returns null when either id is unknown so the controller can
    /// 404 cleanly.</summary>
    public Task<ClubAnnouncementEntity?> AnnouncementAsync(long clubId, long announcementId) =>
        db.ClubAnnouncements.FirstOrDefaultAsync(a => a.Id == announcementId && a.ClubId == clubId);

    /// <summary>Create a club announcement. Returns the new row's id, or null
    /// when the caller may not manage the club.</summary>
    public async Task<long?> CreateAnnouncementAsync(
        long clubId, long callerId, string title, string body, string imageName)
    {
        if (!await CanManageAsync(clubId, callerId)) return null;
        var row = new ClubAnnouncementEntity
        {
            ClubId = clubId,
            AuthorPlayerId = callerId,
            Title = Truncate(title, 200),
            Body = Truncate(body, 4000),
            ImageName = Truncate(imageName, 256),
        };
        db.ClubAnnouncements.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    /// <summary>Edit an existing announcement in place. Null arguments leave
    /// the corresponding field untouched. Returns false when the caller may
    /// not manage the club or the row is unknown.</summary>
    public async Task<bool> UpdateAnnouncementAsync(
        long clubId, long announcementId, long callerId,
        string? title, string? body, string? imageName)
    {
        if (!await CanManageAsync(clubId, callerId)) return false;
        var row = await db.ClubAnnouncements
            .FirstOrDefaultAsync(a => a.Id == announcementId && a.ClubId == clubId);
        if (row is null) return false;
        if (title is not null) row.Title = Truncate(title, 200);
        if (body is not null) row.Body = Truncate(body, 4000);
        if (imageName is not null) row.ImageName = Truncate(imageName, 256);
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    private static string Truncate(string? value, int max)
    {
        value = (value ?? string.Empty).Trim();
        return value.Length <= max ? value : value[..max];
    }

    /// <summary>Delete an announcement after verifying the caller can
    /// manage the club. Idempotent: missing row → no-op.</summary>
    public async Task<bool> DeleteAnnouncementAsync(long clubId, long announcementId, long callerId)
    {
        if (!await CanManageAsync(clubId, callerId)) return false;
        var row = await db.ClubAnnouncements
            .FirstOrDefaultAsync(a => a.Id == announcementId && a.ClubId == clubId);
        if (row is null) return true; // idempotent — caller treats as success
        db.ClubAnnouncements.Remove(row);
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Idempotent seed of the default category tags. Inserts the
    /// canonical Sports / Creative / Roleplay / Hangout / Education
    /// tags on first launch; re-running on a populated table bails
    /// without touching existing rows so admin edits are never
    /// clobbered.
    /// </summary>
    public async Task SeedDefaultsAsync()
    {
        if (await db.ClubCategoryTags.AnyAsync()) return;

        var defaults = new (string Name, int Order)[]
        {
            ("Sports", 0),
            ("Creative", 1),
            ("Roleplay", 2),
            ("Hangout", 3),
            ("Education", 4),
        };
        foreach (var (name, order) in defaults)
        {
            db.ClubCategoryTags.Add(new ClubCategoryTagEntity
            {
                Name = name,
                OrderIndex = order,
                Active = true,
            });
        }
        await db.SaveChangesAsync();
    }
}
