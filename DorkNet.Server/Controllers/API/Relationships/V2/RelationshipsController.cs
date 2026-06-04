using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

// Storage-side enum aliased so the wire mapping stays explicit at
// every callsite. Wire enum values (RelationshipType /
// ReciprocalStatus) live in this file as constants — they're tied
// to the disassembly and not used elsewhere.
using EntityStatus = DorkNet.Server.Data.Entities.RelationshipStatus;

namespace DorkNet.Server.Controllers.API.Relationships.V2;

/// <summary>
/// api/relationships/v2 — friendship + reciprocal-state endpoints.
///
/// All routes / wire shapes verified against the patched 2020 client:
///   Cpp2IL_ISIL/IsilDump/Assembly-CSharp/RecNet/Relationships.txt
///   Cpp2IL_ISIL/IsilDump/Assembly-CSharp/RecNet/Relationship.txt
///
/// Wire response = a Relationship object (or List of them for /get).
/// Relationship.Deserialize strict-reads via Util.GetKey&lt;T&gt;:
///   "PlayerID"          (int) — REQUIRED — the OTHER account's id
///   "RelationshipType"  (int) — REQUIRED — 0..3
///   "Muted"             (int) — REQUIRED — ReciprocalStatus 0..3
///   "Ignored"           (int) — REQUIRED — ReciprocalStatus 0..3
///   "Favorited"         (int) — optional (Util.GetKeyOrDefault, defaults 0)
///
/// RelationshipType (from caller's perspective):
///   0 None, 1 FriendRequestSent, 2 FriendRequestReceived, 3 Friend
/// ReciprocalStatus (symmetric flag — who set it):
///   0 None, 1 Local (just me), 2 Remote (just them), 3 Mutual
///
/// URLs called from the watch (Relationships.txt, all under
/// "api/relationships/"):
///   POST v2/addfriend?id={N}            AddFriend
///   POST v2/removefriend?id={N}         RemoveFriend / cancel-pending
///   POST v2/sendfriendrequest?id={N}    SendFriendRequest
///   POST v2/acceptfriendrequest?id={N}  AcceptFriendRequest
///   POST v1/favorite?id={N}             FavoritePlayer
///   POST v1/unfavorite?id={N}           UnfavoritePlayer
///   POST v1/mute / unmute / ignore / unignore (PostPreferenceChange)
///   GET  v2/get                         GetMyRelationships
/// All POST URLs return one Relationship; GET returns List&lt;Relationship&gt;.
/// </summary>
[ApiController]
[Authorize]
public class RelationshipsController(DorkNetDbContext db, NotificationService notifications, ServerSettingsService settings) : ControllerBase
{
    // RelationshipType — int values match Relationship.RelationshipType
    // in the patched dump.cs.
    private const int RT_None                  = 0;
    private const int RT_FriendRequestSent     = 1;
    private const int RT_FriendRequestReceived = 2;
    private const int RT_Friend                = 3;

    // ReciprocalStatus values — also match the dump.cs enum.
    private const int RS_None   = 0;
    private const int RS_Local  = 1;
    private const int RS_Remote = 2;
    private const int RS_Mutual = 3;

    private long CurrentPlayerId => this.RequireCurrentPlayerId();

    // ── GET /api/relationships/v2/get ────────────────────────────────
    // Watch URL: "api/relationships/v2/get". Returns the caller's full
    // relationship list as List<Relationship>.
    [HttpGet("api/relationships/v2/get")]
    public async Task<ActionResult> GetMyRelationships()
    {
        var me = CurrentPlayerId;

        // Global-friends mode (admin toggle): synthesize a Friend row for
        // every other account WITHOUT persisting anything. Real rows are
        // overlaid so mute/favorite still apply and Blocked stays non-friend.
        if (await settings.IsGlobalFriendsEnabledAsync())
            return Ok(await BuildGlobalFriendListAsync(me));

        var rels = await db.Relationships
            .Where(r => r.RequesterId == me || r.TargetId == me)
            .ToListAsync();
        var wire = rels.Select(r => BuildRelationship(r, me)).ToList();
        return Ok(wire);
    }

    /// <summary>Synthesize the caller's friend list as "everyone" for the
    /// global-friends toggle. One Friend entry per other account; real rows
    /// are overlaid so a Block stays None and mute/favorite carry through.</summary>
    private async Task<List<Dictionary<string, object>>> BuildGlobalFriendListAsync(long me)
    {
        var realRows = await db.Relationships
            .Where(r => r.RequesterId == me || r.TargetId == me)
            .ToListAsync();
        var realByOther = new Dictionary<long, RelationshipEntity>();
        foreach (var r in realRows)
            realByOther[r.RequesterId == me ? r.TargetId : r.RequesterId] = r;

        var others = await db.Players
            .Where(p => p.Id != me && p.Id != RelationshipQueries.SystemAccountId)
            .Select(p => p.Id)
            .ToListAsync();

        var wire = new List<Dictionary<string, object>>(others.Count);
        foreach (var otherId in others)
        {
            if (realByOther.TryGetValue(otherId, out var row))
            {
                // A real block wins — that pair is genuinely not friends.
                if (row.Status == EntityStatus.Blocked) { wire.Add(EmptyRelationship(otherId)); continue; }
                wire.Add(SynthFriend(otherId, row, me));
            }
            else
            {
                wire.Add(SynthFriend(otherId, null, me));
            }
        }
        return wire;
    }

    /// <summary>A synthetic Friend relationship for global-friends mode,
    /// carrying mute/ignore/favorite from the caller's real row when present.</summary>
    private static Dictionary<string, object> SynthFriend(long other, RelationshipEntity? real, long me)
    {
        var mineDir = real is not null && real.RequesterId == me;
        return new()
        {
            ["PlayerID"]         = (int)other,
            ["RelationshipType"] = RT_Friend,
            ["Muted"]            = mineDir && real!.Muted     ? 1 : 0,
            ["Ignored"]          = mineDir && real!.Ignored   ? 1 : 0,
            ["Favorited"]        = mineDir && real!.Favorited ? 1 : 0,
        };
    }

    /// <summary>GET <c>api/relationships/v2/personaldetails/{playerId}</c>
    /// — single relationship row from the caller's perspective. Used
    /// by the watch's profile-card view to determine whether to show
    /// the "Add Friend" or "Remove Friend" affordance. Returns the
    /// same Relationship wire shape as <c>v2/get</c>'s list elements.</summary>
    [HttpGet("api/relationships/v2/personaldetails/{playerId:long}")]
    public async Task<ActionResult> PersonalDetails(long playerId)
    {
        var me = CurrentPlayerId;
        if (playerId <= 0 || playerId == me) return Ok(EmptyRelationship(playerId));
        var row = await FindAsync(me, playerId);

        // Global-friends mode: report Friend for any real account that isn't
        // the system account and isn't explicitly blocked.
        if (playerId != RelationshipQueries.SystemAccountId
            && (row is null || row.Status != EntityStatus.Blocked)
            && await settings.IsGlobalFriendsEnabledAsync())
            return Ok(SynthFriend(playerId, row, me));

        return Ok(row is null ? EmptyRelationship(playerId) : BuildRelationship(row, me));
    }

    // ── /api/relationships/v2/sendfriendrequest?id={N} ───────────────
    // Watch URL from Relationships.txt: "{0}v2/sendfriendrequest?id={1}".
    // The ISIL labels the call site Core.Post but the live trace shows
    // the watch sending GET — accepting both is harmless and the
    // safest cross-build choice.
    [HttpGet("api/relationships/v2/sendfriendrequest")]
    [HttpPost("api/relationships/v2/sendfriendrequest")]
    public async Task<ActionResult> SendFriendRequest([FromQuery] long id)
    {
        var me = CurrentPlayerId;
        if (id <= 0 || id == me)
            return Ok(EmptyRelationship(id));

        var existing = await FindAsync(me, id);
        if (existing is not null)
            return Ok(BuildRelationship(existing, me));

        var row = new RelationshipEntity
        {
            RequesterId = me,
            TargetId    = id,
            Status      = EntityStatus.PendingSent,
        };
        db.Relationships.Add(row);
        await db.SaveChangesAsync();

        await notifications.FriendRequestReceived(id, me);
        return Ok(BuildRelationship(row, me));
    }

    // ── /api/relationships/v2/acceptfriendrequest?id={N} ────────────
    [HttpGet("api/relationships/v2/acceptfriendrequest")]
    [HttpPost("api/relationships/v2/acceptfriendrequest")]
    public async Task<ActionResult> AcceptFriendRequest([FromQuery] long id)
    {
        var me = CurrentPlayerId;
        if (id <= 0)
            return Ok(EmptyRelationship(id));

        // Only an incoming pending row can be accepted. The other side
        // must have requested us — RequesterId=them, TargetId=me.
        var rel = await db.Relationships.FirstOrDefaultAsync(r =>
            r.RequesterId == id && r.TargetId == me &&
            r.Status == EntityStatus.PendingSent);
        if (rel is null)
            return Ok(EmptyRelationship(id));

        rel.Status    = EntityStatus.Friend;
        rel.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await notifications.FriendRequestAccepted(id, me);
        return Ok(BuildRelationship(rel, me));
    }

    // ── /api/relationships/v2/removefriend?id={N} ────────────────────
    // Symmetrical: undoes a friendship OR cancels a pending request OR
    // declines an incoming one. Block rows are left alone — the watch
    // has a separate explicit unblock path. Live trace shows the watch
    // sending GET (BestHTTP queueing oddity vs the ISIL Core.Post call);
    // accept both verbs.
    [HttpGet("api/relationships/v2/removefriend")]
    [HttpPost("api/relationships/v2/removefriend")]
    public async Task<ActionResult> RemoveFriend([FromQuery] long id)
    {
        var me = CurrentPlayerId;
        if (id <= 0)
            return Ok(EmptyRelationship(id));

        var rel = await FindAsync(me, id);
        if (rel is null)
            return Ok(EmptyRelationship(id));
        if (rel.Status == EntityStatus.Blocked)
            return Ok(BuildRelationship(rel, me));

        db.Relationships.Remove(rel);
        await db.SaveChangesAsync();
        await notifications.FriendRemoved(id, me);
        return Ok(EmptyRelationship(id));
    }

    // ── /api/relationships/v2/addfriend?id={N} ───────────────────────
    // Per the disassembly, AddFriend is its own endpoint distinct from
    // SendFriendRequest. Live trace shows the watch sending GET; ISIL
    // says Core.Post. Accept both verbs.
    [HttpGet("api/relationships/v2/addfriend")]
    [HttpPost("api/relationships/v2/addfriend")]
    public async Task<ActionResult> AddFriend([FromQuery] long id)
    {
        var me = CurrentPlayerId;
        if (id <= 0 || id == me)
            return Ok(EmptyRelationship(id));

        var existing = await FindAsync(me, id);
        if (existing is null)
        {
            existing = new RelationshipEntity
            {
                RequesterId = me,
                TargetId    = id,
                Status      = EntityStatus.Friend,
            };
            db.Relationships.Add(existing);
        }
        else if (existing.Status != EntityStatus.Blocked)
        {
            existing.Status    = EntityStatus.Friend;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return Ok(BuildRelationship(existing, me));
    }

    // ── v1 favorite / unfavorite ─────────────────────────────────────
    // Watch URLs: "api/relationships/v1/favorite?id={N}" and
    // "api/relationships/v1/unfavorite?id={N}". These return a
    // boolean-ish RecNetResult per the FavoritePlayer/UnfavoritePlayer
    // disassembly (Core.Post … ExpectHttpStatusSuccess), not a
    // Relationship — they don't surface any state change in the
    // 2020 storage model so we just acknowledge.
    [HttpPost("api/relationships/v1/favorite")]
    public Task<IActionResult> Favorite([FromQuery] long id) => SetPreference(id, favorited: true);

    [HttpPost("api/relationships/v1/unfavorite")]
    public Task<IActionResult> Unfavorite([FromQuery] long id) => SetPreference(id, favorited: false);

    [HttpPost("api/relationships/v1/mute")]
    public Task<IActionResult> Mute([FromQuery] long id, [FromForm(Name = "PlayerId")] long? formId)
        => SetPreference(id != 0 ? id : formId ?? 0, muted: true);

    [HttpPost("api/relationships/v1/unmute")]
    public Task<IActionResult> Unmute([FromQuery] long id, [FromForm(Name = "PlayerId")] long? formId)
        => SetPreference(id != 0 ? id : formId ?? 0, muted: false);

    [HttpPost("api/relationships/v1/ignore")]
    public Task<IActionResult> Ignore([FromQuery] long id, [FromForm(Name = "PlayerId")] long? formId)
        => SetPreference(id != 0 ? id : formId ?? 0, ignored: true);

    [HttpPost("api/relationships/v1/unignore")]
    public Task<IActionResult> Unignore([FromQuery] long id, [FromForm(Name = "PlayerId")] long? formId)
        => SetPreference(id != 0 ? id : formId ?? 0, ignored: false);

    /// <summary>Update favorited/muted/ignored on the caller's row.
    /// Creates a placeholder row when none exists (so an "ignore"
    /// before any friend request is still recorded).</summary>
    private async Task<IActionResult> SetPreference(long otherId,
        bool? favorited = null, bool? muted = null, bool? ignored = null)
    {
        var me = this.RequireCurrentPlayerId();
        if (otherId <= 0 || otherId == me)
            return Ok(new Models.Auth.RecNetResult { Success = false, Error = "invalid_id" });

        var row = await db.Relationships.FirstOrDefaultAsync(r =>
            r.RequesterId == me && r.TargetId == otherId);
        if (row is null)
        {
            row = new RelationshipEntity
            {
                RequesterId = me,
                TargetId = otherId,
                Status = EntityStatus.Friend, // placeholder; favorite/mute/ignore don't require friend status
            };
            db.Relationships.Add(row);
        }
        if (favorited is bool f) row.Favorited = f;
        if (muted is bool m) row.Muted = m;
        if (ignored is bool i) row.Ignored = i;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new Models.Auth.RecNetResult { Success = true, Error = string.Empty });
    }

    public sealed class BulkIgnoreRequest
    {
        public int Platform { get; set; }
        public List<string>? PlatformUserIds { get; set; }
    }

    /// <summary>POST <c>api/relationships/v1/bulkignoreplatformusers</c>
    /// — bulk-import Steam/Oculus blocks. Persists to
    /// <see cref="PlatformIgnoreEntity"/> so when those users sign
    /// up later we can auto-set their relationship to Ignored.</summary>
    [HttpPost("api/relationships/v1/bulkignoreplatformusers")]
    [Authorize]
    public async Task<IActionResult> BulkIgnorePlatformUsers([FromBody] BulkIgnoreRequest req)
    {
        var me = this.RequireCurrentPlayerId();
        if (req?.PlatformUserIds is null || req.PlatformUserIds.Count == 0)
            return Ok(new { Imported = 0 });

        var added = 0;
        foreach (var puid in req.PlatformUserIds.Distinct())
        {
            if (string.IsNullOrWhiteSpace(puid)) continue;
            var exists = await db.PlatformIgnores.AnyAsync(i =>
                i.PlayerId == me && i.Platform == req.Platform && i.PlatformUserId == puid);
            if (exists) continue;
            db.PlatformIgnores.Add(new PlatformIgnoreEntity
            {
                PlayerId = me,
                Platform = req.Platform,
                PlatformUserId = puid,
            });
            added++;
        }
        await db.SaveChangesAsync();
        return Ok(new { Imported = added });
    }

    // ── helpers ──────────────────────────────────────────────────────

    private Task<RelationshipEntity?> FindAsync(long me, long other) =>
        db.Relationships.FirstOrDefaultAsync(r =>
            (r.RequesterId == me && r.TargetId == other) ||
            (r.RequesterId == other && r.TargetId == me));

    /// <summary>Map a storage row to the watch's wire RelationshipType
    /// from <paramref name="me"/>'s perspective. PendingSent flips
    /// direction depending on which side originated.</summary>
    private static int MapType(RelationshipEntity r, long me) => r.Status switch
    {
        EntityStatus.Friend            => RT_Friend,
        EntityStatus.Blocked           => RT_None, // Blocked is not a friendship; the watch reads block state via a separate flag we don't track yet.
        EntityStatus.PendingSent       => r.RequesterId == me ? RT_FriendRequestSent : RT_FriendRequestReceived,
        EntityStatus.PendingReceived   => r.RequesterId == me ? RT_FriendRequestReceived : RT_FriendRequestSent,
        _                              => RT_None,
    };

    /// <summary>
    /// Build the wire object the patched <c>Relationship.Deserialize</c>
    /// reads. Keys are case-sensitive (Util.GetKey is a Dictionary
    /// TryGetValue) — we ship exactly what's in the disassembly.
    /// Implemented as a Dictionary because System.Text.Json's
    /// case-insensitive collision check rejects anonymous types with
    /// PascalCase property names like "PlayerID" + "Muted" if any
    /// other property could collide on lowercase — using a Dictionary
    /// avoids that whole class of trouble.
    /// </summary>
    private static Dictionary<string, object> BuildRelationship(RelationshipEntity r, long me)
    {
        var other = r.RequesterId == me ? r.TargetId : r.RequesterId;
        // ReciprocalStatus enum: 0=None, 1=ByMe, 2=ByThem, 3=Both.
        // Our schema only tracks "by me" (the requester's perspective)
        // so we can answer ByMe / None; ByThem / Both would require
        // looking up the OTHER direction's row too. Cheap extra query
        // omitted — the watch's UI only consumes the ByMe bit.
        var mineDir = r.RequesterId == me;
        var muted    = mineDir && r.Muted     ? 1 : 0;
        var ignored  = mineDir && r.Ignored   ? 1 : 0;
        var favored  = mineDir && r.Favorited ? 1 : 0;
        return new()
        {
            ["PlayerID"]         = (int)other,
            ["RelationshipType"] = MapType(r, me),
            ["Muted"]            = muted,
            ["Ignored"]          = ignored,
            ["Favorited"]        = favored,
        };
    }

    /// <summary>"None" relationship for a target the caller has nothing
    /// on file with — used as a no-op success response so the watch's
    /// Relationship.Deserialize stays satisfied.</summary>
    private static Dictionary<string, object> EmptyRelationship(long other) => new()
    {
        ["PlayerID"]         = (int)other,
        ["RelationshipType"] = RT_None,
        ["Muted"]            = RS_None,
        ["Ignored"]          = RS_None,
        ["Favorited"]        = RS_None,
    };
}
