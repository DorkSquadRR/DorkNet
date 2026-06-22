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
    [HttpGet("api/inventions/v1/featureddormskins")]
    [HttpGet("api/inventions/v1/toptoday")]
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
    [HttpGet("api/inventions/v2/mine")]
    [Authorize]
    public Task<ActionResult> Mine() => Saved(100);

    [HttpGet("api/inventions/v1/search")]
    [HttpGet("api/inventions/v2/search")]
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
    [HttpPost("api/inventions/v2/batch")]
    [HttpPost("api/inventions/v1/dormskinsfromids")]
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

    [HttpGet("api/inventions/v1/fromcreators")]
    public async Task<ActionResult> FromCreators()
    {
        var creatorIds = Request.Query
            .SelectMany(q => q.Value)
            .SelectMany(v => (v ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(v => long.TryParse(v, out var id) ? id : 0L)
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToList();

        if (creatorIds.Count == 0) return Ok(Array.Empty<object>());
        var rows = await db.Inventions
            .Where(i => !i.IsDeleted && i.IsPublished && creatorIds.Contains(i.CreatorPlayerId))
            .OrderByDescending(i => i.UpdatedAt)
            .Take(200)
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

    public sealed class SaveInventionV4Request
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? ImageName { get; set; }
        public int InstantiationCost { get; set; }
        public int LightsCost { get; set; }
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
            InventionVersion = ToVersionWire(v1),
        });
    }

    public sealed record AddVersionRequest(
        long InventionId, string BlobName,
        int? InstantiationCost, int? LightsCost);

    [HttpPost("api/inventions/v3/addversion")]
    [HttpPost("api/inventions/v4/addversion")]
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

    [HttpGet("api/inventions/v1/updateprice")]
    [Authorize]
    public async Task<ActionResult> UpdatePrice(
        [FromQuery] long inventionId,
        [FromQuery] int price)
    {
        var pid = CurrentPlayerId;
        var inv = await db.Inventions.FirstOrDefaultAsync(x => x.Id == inventionId && !x.IsDeleted);
        if (inv is null) return NotFound();
        if (inv.CreatorPlayerId != pid) return Forbid();
        inv.Price = Math.Clamp(price, 0, 1000000);
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

    [HttpGet("api/inventions/v3/publish")]
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
        i.Price,
        HideFromPlayer = false,
    };

    private static object ToWireV4(InventionEntity i) => ToWire(i);

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
