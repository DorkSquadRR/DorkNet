using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Site;

/// <summary>
/// Apex-host API for the public-facing site at <c>localhost</c> /
/// <c>rec.net</c>. Everything here is anonymous-safe — the site reads
/// the photo feed, looks up players, and browses public rooms without
/// any login. Mirrors the wire shape the SPA expects (camelCase JSON
/// thanks to the default ASP.NET serializer; admin / game endpoints
/// override with PascalCase, but the public site has no legacy clients
/// to placate so we ship modern conventions).
///
/// Lives at the apex so the SPA can same-origin fetch <c>/api/site/v1</c>
/// from the bare domain without CORS gymnastics. Routing is path-only
/// (post [Host]-strip refactor) — every subdomain reaches every
/// controller and the right handler is picked by URL alone.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/site/v1")]
public class PublicSiteController(
    DorkNetDbContext db,
    PlayerService players,
    SignupCodeService signupCodes,
    DomainConfig domain) : ControllerBase
{
    // ── Players ──────────────────────────────────────────────────────

    /// <summary>GET /api/site/v1/players/search?q=…&amp;take=20 — typeahead
    /// account search by display name OR username.</summary>
    [HttpGet("players/search")]
    public async Task<IActionResult> SearchPlayers([FromQuery] string? q, [FromQuery] int take = 20)
    {
        if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<object>());
        var rows = await players.SearchAsync(q.Trim(), Math.Clamp(take, 1, 50));
        return Ok(rows.Select(PlayerCard).ToList());
    }

    /// <summary>GET /api/site/v1/players/{id} — public profile card.</summary>
    [HttpGet("players/{id:long}")]
    public async Task<IActionResult> GetPlayer(long id)
    {
        var p = await db.Players
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.Username, x.DisplayName, x.Bio, x.Level, x.XP,
                x.IsAdmin, x.IsDeveloper, x.IsVerified, x.IsJunior,
                x.CreatedAt, x.ProfileImageName,
            })
            .FirstOrDefaultAsync();
        if (p is null) return NotFound();

        var photoCount = await db.Photos
            .CountAsync(ph => ph.UploaderPlayerId == id && ph.IsPublic && ph.DeletedAt == null);
        var taggedNeedle = $",{id},";
        var photosOfPlayerCount = await db.Photos
            .CountAsync(ph => ph.IsPublic && ph.DeletedAt == null &&
                              ("," + ph.TaggedPlayerIdsCsv + ",").Contains(taggedNeedle));
        var friendCount = await db.Relationships
            .Where(r => r.Status == RelationshipStatus.Friend &&
                        (r.RequesterId == id || r.TargetId == id))
            .Select(r => r.RequesterId == id ? r.TargetId : r.RequesterId)
            .Distinct()
            .CountAsync();
        var publicRoomCount = await PublicPublishedRooms()
            .CountAsync(r => r.CreatorPlayerId == id);

        return Ok(new
        {
            id = p.Id,
            username = p.Username,
            displayName = p.DisplayName,
            bio = p.Bio ?? string.Empty,
            level = p.Level,
            xp = p.XP,
            isAdmin = p.IsAdmin,
            isDeveloper = p.IsDeveloper,
            isVerified = p.IsVerified,
            isJunior = p.IsJunior,
            createdAt = p.CreatedAt,
            profileImageName = p.ProfileImageName,
            photoCount,
            photosTakenCount = photoCount,
            photosOfPlayerCount,
            friendCount,
            publicRoomCount,
        });
    }

    /// <summary>GET /api/site/v1/players/{id}/photos — photos uploaded
    /// by the player. Excludes private + soft-deleted.</summary>
    [HttpGet("players/{id:long}/photos")]
    public async Task<IActionResult> GetPlayerPhotos(long id,
        [FromQuery] int take = 24, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var rows = await db.Photos
            .Where(p => p.UploaderPlayerId == id && p.IsPublic && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
        return Ok(await EnrichPhotosAsync(rows));
    }

    /// <summary>GET /api/site/v1/players/{id}/photos/of — public photos
    /// where this player was tagged by the camera.</summary>
    [HttpGet("players/{id:long}/photos/of")]
    public async Task<IActionResult> GetPhotosOfPlayer(long id,
        [FromQuery] int take = 24, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var needle = $",{id},";
        var rows = await db.Photos
            .Where(p => p.IsPublic && p.DeletedAt == null &&
                        ("," + p.TaggedPlayerIdsCsv + ",").Contains(needle))
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
        return Ok(await EnrichPhotosAsync(rows));
    }

    /// <summary>GET /api/site/v1/players/{id}/rooms — public rooms
    /// published by this player.</summary>
    [HttpGet("players/{id:long}/rooms")]
    public async Task<IActionResult> GetPlayerRooms(long id,
        [FromQuery] int take = 24, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var rows = await PublicPublishedRooms()
            .Where(r => r.CreatorPlayerId == id)
            .OrderByDescending(r => r.HotScore)
            .ThenByDescending(r => r.UpdatedAt)
            .Skip(skip).Take(take)
            .Select(r => new
            {
                id = r.Id,
                name = r.Name,
                description = r.Description,
                creatorPlayerId = r.CreatorPlayerId,
                isDormRoom = r.IsDormRoom,
                isAGRoom = r.IsAGRoom,
                visitCount = r.VisitCount,
                visitorCount = r.VisitorCount,
                cheerCount = r.CheerCount,
                imageName = r.ImageName,
            })
            .ToListAsync();
        return Ok(rows);
    }

    // ── Photos ───────────────────────────────────────────────────────

    /// <summary>GET /api/site/v1/feed — global public feed, newest first.</summary>
    [HttpGet("feed")]
    public async Task<IActionResult> Feed([FromQuery] int take = 24, [FromQuery] int skip = 0)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var rows = await db.Photos
            .Where(p => p.IsPublic && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync();
        return Ok(await EnrichPhotosAsync(rows));
    }

    /// <summary>GET /api/site/v1/photos/{id} — single photo + metadata.
    /// Bumps the ViewCount as a side effect (denormalised trending sort).</summary>
    [HttpGet("photos/{id:long}")]
    public async Task<IActionResult> GetPhoto(long id)
    {
        var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == id);
        if (photo is null || !photo.IsPublic || photo.DeletedAt is not null) return NotFound();

        photo.ViewCount += 1;
        await db.SaveChangesAsync();

        var enriched = await EnrichPhotosAsync(new[] { photo });
        return Ok(enriched.First());
    }

    // ── Rooms ────────────────────────────────────────────────────────

    /// <summary>GET /api/site/v1/rooms/search?q=… — rooms matching name.
    /// Mostly used by the public site's room directory.</summary>
    [HttpGet("rooms/search")]
    public async Task<IActionResult> SearchRooms([FromQuery] string? q, [FromQuery] int take = 24)
    {
        var clamped = Math.Clamp(take, 1, 50);
        var qq = (q ?? string.Empty).Trim();
        // Public rooms list: hide dorms (per-player private space), and
        // hide rooms that have only ever been visited by their creator
        // (VisitorCount < 2). This keeps "^dwadwad" style throwaway
        // builds off the public-facing site without an explicit
        // "publish" toggle. Admins can still see them via the Rooms
        // admin page and delete them from there if they want.
        var rows = await db.Rooms
            .Where(r => !r.IsDormRoom && r.VisitorCount >= 2 &&
                        (qq == "" || r.Name.Contains(qq) || r.Description.Contains(qq)))
            .OrderByDescending(r => r.HotScore)
            .Take(clamped)
            .Select(r => new
            {
                id = r.Id,
                name = r.Name,
                description = r.Description,
                creatorPlayerId = r.CreatorPlayerId,
                isDormRoom = r.IsDormRoom,
                isAGRoom = r.IsAGRoom,
                visitCount = r.VisitCount,
                visitorCount = r.VisitorCount,
                cheerCount = r.CheerCount,
                imageName = r.ImageName,
            })
            .ToListAsync();
        return Ok(rows);
    }

    // ── Site stats (home-page hero) ──────────────────────────────────

    /// <summary>GET /api/site/v1/stats — coarse counters for the
    /// public site's marketing hero (player count, room count, photo
    /// count). Anonymous-safe.</summary>
    [HttpGet("stats")]
    public async Task<IActionResult> Stats()
    {
        var playerCount = await db.Players.CountAsync();
        var roomCount = await db.Rooms.CountAsync(r => !r.IsDormRoom);
        var photoCount = await db.Photos.CountAsync(p => p.IsPublic && p.DeletedAt == null);
        var inventionCount = await db.Inventions.CountAsync(i => !i.IsDeleted);
        return Ok(new { playerCount, roomCount, photoCount, inventionCount });
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>Public-facing player card. Mirrors what the site needs
    /// for the search results list / friend-list row: id, name,
    /// profileImageName (the SPA stitches the img.* URL).</summary>
    private static object PlayerCard(PlayerEntity p) => new
    {
        id = p.Id,
        username = p.Username,
        displayName = p.DisplayName,
        level = p.Level,
        isAdmin = p.IsAdmin,
        isDeveloper = p.IsDeveloper,
        isVerified = p.IsVerified,
        isJunior = p.IsJunior,
        profileImageName = p.ProfileImageName,
    };

    private IQueryable<RoomEntity> PublicPublishedRooms() =>
        db.Rooms.Where(r => r.State == 0 &&
                            r.Accessibility == 1 &&
                            !r.IsDormRoom &&
                            !r.HiddenFromBrowse);

    /// <summary>Materialise photo rows into wire DTOs with display names
    /// + room names + an absolute image URL. The URL apex switches
    /// based on the request host so a localhost visitor gets localhost
    /// CDN links and a rec.net visitor gets rec.net.</summary>
    private async Task<List<object>> EnrichPhotosAsync(IEnumerable<PhotoEntity> photos)
    {
        var list = photos.ToList();
        if (list.Count == 0) return new();

        var uploaderIds = list.Select(p => p.UploaderPlayerId).Distinct().ToList();
        var taggedIds = list
            .SelectMany(p => ParseTaggedPlayerIds(p.TaggedPlayerIdsCsv))
            .Distinct()
            .ToList();
        var playerIds = uploaderIds.Concat(taggedIds).Distinct().ToList();
        var roomIds = list.Select(p => p.RoomId).Where(id => id > 0).Distinct().ToList();

        var playersById = await db.Players
            .Where(p => playerIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Username, p.DisplayName, p.ProfileImageName })
            .ToListAsync();
        var rooms = await db.Rooms
            .Where(r => roomIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Name })
            .ToListAsync();

        var pMap = playersById.ToDictionary(u => u.Id);
        var rMap = rooms.ToDictionary(r => r.Id);

        var cdnHost = domain.Sub("cdn");

        return list.Select(p =>
        {
            pMap.TryGetValue(p.UploaderPlayerId, out var u);
            rMap.TryGetValue(p.RoomId, out var r);
            return (object)new
            {
                id = p.Id,
                uploaderPlayerId = p.UploaderPlayerId,
                uploaderUsername = u?.Username ?? string.Empty,
                uploaderDisplayName = u?.DisplayName ?? $"Player_{p.UploaderPlayerId}",
                uploaderProfileImageName = u?.ProfileImageName,
                blobName = p.BlobName,
                imageUrl = $"https://{cdnHost}/{p.BlobName}",
                caption = p.Caption,
                roomId = p.RoomId,
                roomName = r?.Name ?? string.Empty,
                taggedPlayers = ParseTaggedPlayerIds(p.TaggedPlayerIdsCsv)
                    .Select(id => pMap.TryGetValue(id, out var tagged) ? tagged : null)
                    .Where(tagged => tagged is not null)
                    .Select(tagged => new
                    {
                        id = tagged!.Id,
                        username = tagged.Username,
                        displayName = tagged.DisplayName,
                        profileImageName = tagged.ProfileImageName,
                    })
                    .ToArray(),
                isPublic = p.IsPublic,
                cheerCount = p.CheerCount,
                viewCount = p.ViewCount,
                createdAt = p.CreatedAt,
            };
        }).ToList();
    }

    private static List<long> ParseTaggedPlayerIds(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => long.TryParse(s, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();
    }

    // ── Signup (code redemption) ─────────────────────────────────────

    /// <summary>GET /api/site/v1/join/pending-devices — device ids that
    /// were refused account creation while signups are disabled, seen
    /// from THIS caller's IP. Lets the /join page show a player the
    /// device their own game client just reported so they don't have to
    /// dig it out of Player.log. Best-effort: behind a proxy that
    /// collapses client IPs this can be empty, in which case the player
    /// pastes the id manually.</summary>
    [HttpGet("join/pending-devices")]
    public async Task<IActionResult> JoinPendingDevices()
    {
        var ip = SignupCodeService.ClientIp(HttpContext);
        var rows = await signupCodes.RecentPendingByIpAsync(ip);
        return Ok(rows.Select(d => new
        {
            deviceId = d.DeviceId,
            platform = d.Platform,
            lastSeenAt = d.LastSeenAt,
        }).ToList());
    }

    public sealed record JoinRedeemRequest(string? Code, string? Username, string? Password);

    /// <summary>POST /api/site/v1/join/redeem — redeem a signup code,
    /// minting a username/password account. Returns {ok, error?,
    /// username?}.</summary>
    [HttpPost("join/redeem")]
    public async Task<IActionResult> JoinRedeem([FromBody] JoinRedeemRequest body)
    {
        var result = await signupCodes.RedeemAsync(body.Code, body.Username, body.Password);
        if (!result.Ok)
            return BadRequest(new { ok = false, error = result.Error });
        return Ok(new { ok = true, username = result.Username, playerId = result.PlayerId });
    }
}
