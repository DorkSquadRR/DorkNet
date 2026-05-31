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
/// Wire DTO matches <c>RecNet.Invention</c>
/// (<c>Cpp2IL_CS/.../RecNet/Invention.cs</c>) byte-for-byte: keys
/// <c>InventionId, ReplicationId, CreatorPlayerId, Name, Description,
/// ImageName, CurrentVersionNumber, IsPublished, ModifiedAt,
/// CreatedAt, FirstPublishedAt, CreationRoomId,
/// NumPlayersHaveUsedInRoom, NumDownloads, CheerCount,
/// CreatorPermission, GeneralPermission, IsAgInvention</c>.
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
    [Authorize]
    public Task<ActionResult> Mine() => Saved(100);

    [HttpGet("api/inventions/v1/search")]
    public async Task<ActionResult> Search([FromQuery] string value = "")
    {
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
                .Take(50)
                .ToListAsync();
            return Ok(byCreator.Select(ToWire));
        }

        var v = value.ToLowerInvariant();
        var rows = await db.Inventions
            .Where(i => !i.IsDeleted && i.IsPublished
                        && (i.Name.ToLower().Contains(v)
                            || i.TagsCsv.ToLower().Contains(v)))
            .OrderByDescending(i => i.CheerCount)
            .Take(50)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    public sealed record InventionBatchRequest(List<long> InventionIds);

    [HttpPost("api/inventions/v1/batch")]
    public async Task<ActionResult> Batch([FromBody] InventionBatchRequest req)
    {
        if (req?.InventionIds is null || req.InventionIds.Count == 0)
            return Ok(Array.Empty<object>());
        var ids = req.InventionIds.Take(200).ToList();
        var rows = await db.Inventions
            .Where(i => !i.IsDeleted && ids.Contains(i.Id))
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
    /// dorm-load destabilisation we've been hunting.</summary>
    [HttpGet("api/inventions/v1/tagfilters")]
    public IActionResult TagFilters()
    {
        var tags = new[]
        {
            "sport", "game", "vehicle", "weapon", "decor", "tool",
            "art", "music", "puzzle", "combat", "build", "race",
        };
        return Ok(new
        {
            PinnedFilters = tags,
            PopularFilters = tags,
        });
    }

    /// <summary>GET <c>api/inventions/v1/creatorIds</c> — the distinct
    /// creator account ids of every invention placed in a room
    /// (<c>GetCreatorIdsOfInventionsInRoom</c>, called by the 2020.03
    /// watch on room load). It uses these to resolve which creators it
    /// must request invention permissions for before spawning a room's
    /// invention-based geometry. Returns <c>List&lt;int&gt;</c> — empty
    /// when the room has no tracked inventions.</summary>
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

    // ── Single fetch ─────────────────────────────────────────────────────

    [HttpGet("api/inventions/v1")]
    public async Task<ActionResult> SingleByQuery([FromQuery] long inventionId)
    {
        var i = await db.Inventions.FirstOrDefaultAsync(x => x.Id == inventionId && !x.IsDeleted);
        if (i is null) return NotFound();
        if (!i.IsPublished && i.CreatorPlayerId != CurrentPlayerIdOrNull)
            return Forbid();
        return Ok(ToWire(i));
    }

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
            Invention = ToWire(i),
            Versions = versions.Select(ToVersionWire),
        });
    }

    [HttpGet("api/inventions/v1/personaldetails/{id:long}")]
    [Authorize]
    public async Task<ActionResult> PersonalDetails(long id)
    {
        var i = await db.Inventions.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (i is null) return NotFound();
        return Ok(new
        {
            Invention = ToWire(i),
            CanEdit = i.CreatorPlayerId == CurrentPlayerId,
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

    public sealed record SaveInventionRequest(
        string Name, string? Description, string? ImageName,
        int? Permission, string? Tags, string BlobName,
        long? CreationRoomId, int? InstantiationCost, int? LightsCost);

    [HttpPost("api/inventions/v3/save")]
    [Authorize]
    public async Task<ActionResult> Save([FromBody] SaveInventionRequest req)
    {
        var pid = CurrentPlayerId;
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("missing name");
        if (string.IsNullOrWhiteSpace(req.BlobName)) return BadRequest("missing BlobName");

        var inv = new InventionEntity
        {
            CreatorPlayerId = pid,
            ReplicationId = Guid.NewGuid().ToString("D"),
            Name = req.Name.Trim(),
            Description = req.Description ?? string.Empty,
            ImageName = req.ImageName ?? string.Empty,
            Permission = Math.Clamp(req.Permission ?? 0, 0, 2),
            CurrentBlobName = req.BlobName,
            TagsCsv = req.Tags ?? string.Empty,
            CreationRoomId = req.CreationRoomId,
            CreatorPermission = 100, // Unlimited for the creator
            GeneralPermission = 0, // Unassigned (private) until publish
            IsPublished = false,
            CurrentVersionNumber = 1,
        };
        db.Inventions.Add(inv);
        await db.SaveChangesAsync();

        // Snapshot the v1 version row.
        var v1 = new InventionVersionEntity
        {
            InventionId = inv.Id,
            ReplicationId = Guid.NewGuid().ToString("D"),
            VersionNumber = 1,
            BlobName = req.BlobName,
            InstantiationCost = req.InstantiationCost ?? 0,
            LightsCost = req.LightsCost ?? 0,
        };
        db.InventionVersions.Add(v1);
        await db.SaveChangesAsync();

        await level.AwardXpAsync(pid, LevelService.InventionSavedXp, $"invention_save:{inv.Id}");
        return Ok(ToWire(inv));
    }

    public sealed record AddVersionRequest(
        long InventionId, string BlobName,
        int? InstantiationCost, int? LightsCost);

    [HttpPost("api/inventions/v3/addversion")]
    [Authorize]
    public async Task<ActionResult> AddVersion([FromBody] AddVersionRequest req)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == req.InventionId && !x.IsDeleted);
        if (inv is null) return NotFound();
        if (inv.CreatorPlayerId != pid) return Forbid();
        if (string.IsNullOrWhiteSpace(req.BlobName)) return BadRequest("missing BlobName");

        var nextVer = inv.CurrentVersionNumber + 1;
        inv.CurrentBlobName = req.BlobName;
        inv.CurrentVersionNumber = nextVer;
        inv.UpdatedAt = DateTime.UtcNow;

        db.InventionVersions.Add(new InventionVersionEntity
        {
            InventionId = inv.Id,
            ReplicationId = Guid.NewGuid().ToString("D"),
            VersionNumber = nextVer,
            BlobName = req.BlobName,
            InstantiationCost = req.InstantiationCost ?? 0,
            LightsCost = req.LightsCost ?? 0,
        });
        await db.SaveChangesAsync();
        return Ok(ToWire(inv));
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
        return Ok(ToWire(inv));
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
        return Ok(ToWire(inv));
    }

    [HttpGet("api/inventions/v2/publish")]
    [Authorize]
    public async Task<ActionResult> Publish(
        [FromQuery] long inventionId, [FromQuery] int permissionLevel)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == inventionId && !x.IsDeleted);
        if (inv is null) return NotFound();
        if (inv.CreatorPlayerId != pid) return Forbid();
        var perm = ClampInventionPermission(permissionLevel);
        if (perm == 0) return BadRequest("permissionLevel must be > 0 (Unassigned)");
        inv.GeneralPermission = perm;
        inv.IsPublished = true;
        inv.FirstPublishedAt ??= DateTime.UtcNow;
        inv.Permission = perm >= 60 ? 2 : (perm >= 20 ? 1 : 0);
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(inv));
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
        return Ok(ToWire(inv));
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

    public sealed record SetTagsRequest(long InventionId, string? AutoTags, string? PlayerAddedTags);

    [HttpPost("api/inventions/v1/settags")]
    [Authorize]
    public async Task<ActionResult> SetTags([FromBody] SetTagsRequest req)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == req.InventionId && !x.IsDeleted);
        if (inv is null) return NotFound();
        if (inv.CreatorPlayerId != pid) return Forbid();
        var combined = string.Join(',', new[] { req.AutoTags, req.PlayerAddedTags }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim(',');
        inv.TagsCsv = combined;
        inv.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(inv));
    }

    public sealed record CheerRequest(long InventionId);

    [HttpPost("api/inventions/v1/cheer")]
    [Authorize]
    public async Task<ActionResult> Cheer([FromBody] CheerRequest req)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == req.InventionId && !x.IsDeleted);
        if (inv is null) return NotFound();

        // Idempotent: one cheer per (player, invention).
        var existing = await db.Cheers.FirstOrDefaultAsync(c =>
            c.FromPlayerId == pid && c.TargetInventionId == req.InventionId);
        if (existing is null)
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
        return Ok(new { inv.Id, inv.CheerCount });
    }

    public sealed record ReportInventionRequest(
        long InventionId, int? ReportCategory, string? Details);

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
        return Ok(new { Reported = true });
    }

    // ── Wire serializers ─────────────────────────────────────────────────

    private static int ClampInventionPermission(int v) => v switch
    {
        0 or 10 or 20 or 40 or 60 or 80 or 100 => v,
        _ => 0,
    };

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
        ModifiedAt = i.UpdatedAt,
        i.CreatedAt,
        i.FirstPublishedAt,
        i.CreationRoomId,
        i.NumPlayersHaveUsedInRoom,
        NumDownloads = i.SpawnCount,
        i.CheerCount,
        i.CreatorPermission,
        i.GeneralPermission,
        i.IsAgInvention,
    };

    private static object ToVersionWire(InventionVersionEntity v) => new
    {
        v.InventionId,
        ReplicationId = string.IsNullOrEmpty(v.ReplicationId)
            ? Guid.Empty.ToString("D") : v.ReplicationId,
        v.VersionNumber,
        v.InstantiationCost,
        v.LightsCost,
        v.BlobName,
    };
}
