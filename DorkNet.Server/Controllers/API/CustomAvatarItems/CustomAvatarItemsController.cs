using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.CustomAvatarItems;

[ApiController]
[Route("api/customAvatarItems")]
[Route("api/customAvatarItems/v1")]
public class CustomAvatarItemsController(
    DorkNetDbContext db,
    LevelService level,
    DomainConfig domain) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(
        [FromQuery] string? q = null,
        [FromQuery] string? query = null,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        skip = Math.Max(0, skip);
        var needle = (q ?? query ?? string.Empty).Trim().ToLowerInvariant();
        IQueryable<CustomAvatarItemEntity> rows = db.CustomAvatarItems
            .Where(i => i.IsPublic);
        if (needle.Length > 0)
        {
            rows = rows.Where(i =>
                i.Name.ToLower().Contains(needle) ||
                i.Description.ToLower().Contains(needle));
        }

        var result = await rows
            .OrderByDescending(i => i.UpdatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        return Ok(result.Select(ToWire));
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> Get(Guid id)
    {
        var item = await db.CustomAvatarItems.FirstOrDefaultAsync(i => i.PublicId == id);
        if (item is null) return NotFound();
        if (!item.IsPublic && item.CreatorPlayerId != this.CurrentPlayerId()) return Forbid();
        return Ok(ToWire(item));
    }

    [HttpGet("bulk")]
    [HttpPost("bulk")]
    [AllowAnonymous]
    public async Task<IActionResult> Bulk([FromBody] BulkRequest? body)
    {
        var ids = await ReadIdsAsync(body);
        if (ids.Count == 0) return Ok(Array.Empty<object>());
        var rows = await db.CustomAvatarItems
            .Where(i => ids.Contains(i.PublicId) && (i.IsPublic || i.CreatorPlayerId == this.CurrentPlayerId()))
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("featured")]
    [AllowAnonymous]
    public async Task<IActionResult> Featured([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        var rows = await db.CustomAvatarItems
            .Where(i => i.IsPublic && i.IsFeatured)
            .OrderByDescending(i => i.UpdatedAt)
            .Take(take)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("hot")]
    [AllowAnonymous]
    public async Task<IActionResult> Hot([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        var rows = await db.CustomAvatarItems
            .Where(i => i.IsPublic)
            .OrderByDescending(i => i.CheerCount)
            .ThenByDescending(i => i.PurchaseCount)
            .ThenByDescending(i => i.UpdatedAt)
            .Take(take)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("new")]
    [AllowAnonymous]
    public async Task<IActionResult> New([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        var rows = await db.CustomAvatarItems
            .Where(i => i.IsPublic)
            .OrderByDescending(i => i.CreatedAt)
            .Take(take)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me([FromQuery] int skip = 0, [FromQuery] int take = 100)
    {
        var pid = this.RequireCurrentPlayerId();
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var createdQuery = db.CustomAvatarItems
            .Where(i => i.CreatorPlayerId == pid)
            .OrderByDescending(i => i.UpdatedAt);
        var total = await createdQuery.CountAsync();
        var created = await createdQuery
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        var ownedIds = await db.CustomAvatarItemOwnership
            .Where(o => o.PlayerId == pid)
            .Select(o => o.CustomAvatarItemId)
            .ToListAsync();
        var owned = ownedIds.Count == 0
            ? new List<CustomAvatarItemEntity>()
            : await db.CustomAvatarItems
                .Where(i => ownedIds.Contains(i.Id))
                .OrderByDescending(i => i.UpdatedAt)
                .ToListAsync();

        return Ok(new
        {
            Results = created.Select(ToWire),
            TotalResults = total,
            Created = created.Select(ToWire),
            Owned = owned.Select(ToWire),
        });
    }

    [HttpGet("owned")]
    [Authorize]
    public async Task<IActionResult> Owned([FromQuery] int skip = 0, [FromQuery] int take = 100)
    {
        var pid = this.RequireCurrentPlayerId();
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 100);

        var query = db.CustomAvatarItemOwnership
            .Where(o => o.PlayerId == pid)
            .Join(db.CustomAvatarItems, o => o.CustomAvatarItemId, i => i.Id, (o, i) => i)
            .OrderByDescending(i => i.UpdatedAt);
        var total = await query.CountAsync();
        var rows = await query
            .Skip(skip)
            .Take(take)
            .ToListAsync();
        return Ok(new
        {
            Results = rows.Select(ToWire),
            TotalResults = total,
        });
    }

    [HttpGet("isCreationAllowedForAccount")]
    [Authorize]
    public async Task<IActionResult> IsCreationAllowedForAccount()
    {
        var pid = this.RequireCurrentPlayerId();
        var player = await db.Players
            .Where(p => p.Id == pid)
            .Select(p => new { p.IsJunior, p.BannedUntil })
            .FirstOrDefaultAsync();
        var allowed = player is not null
            && !player.IsJunior
            && (player.BannedUntil is null || player.BannedUntil <= DateTime.UtcNow);
        return Ok(new { IsCreationAllowed = allowed, Allowed = allowed });
    }

    [HttpGet("isCreationEnabled")]
    [AllowAnonymous]
    public IActionResult IsCreationEnabled() => Ok(new { IsCreationEnabled = true, Enabled = true });

    [HttpGet("isRenderingEnabled")]
    [AllowAnonymous]
    public IActionResult IsRenderingEnabled() => Ok(new { IsRenderingEnabled = true, Enabled = true });

    [HttpGet("minPriceForPublicItem")]
    [AllowAnonymous]
    public IActionResult MinPriceForPublicItem() => Ok(new { MinPrice = 100, Price = 100 });

    [HttpPost("design")]
    [Authorize]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> Design()
    {
        var pid = this.RequireCurrentPlayerId();
        var req = await ReadDesignRequestAsync();
        var name = (req.Name ?? string.Empty).Trim();
        if (name.Length < 3) return BadRequest(new { error = "name_too_short" });
        if (name.Length > 128) name = name[..128];

        var description = (req.Description ?? string.Empty).Trim();
        if (description.Length > 1024) description = description[..1024];

        CustomAvatarItemEntity item;
        if (req.Id is Guid id)
        {
            item = await db.CustomAvatarItems.FirstOrDefaultAsync(i => i.PublicId == id)
                   ?? new CustomAvatarItemEntity { PublicId = id, CreatorPlayerId = pid };
            if (item.Id != 0 && item.CreatorPlayerId != pid) return Forbid();
            if (item.Id == 0) db.CustomAvatarItems.Add(item);
        }
        else
        {
            item = new CustomAvatarItemEntity { CreatorPlayerId = pid };
            db.CustomAvatarItems.Add(item);
        }

        item.Name = name;
        item.Description = description;
        item.Price = Math.Max(100, req.Price ?? item.Price);
        item.ItemType = req.ItemType ?? item.ItemType;
        item.BaseAvatarItemId = req.BaseAvatarItemId ?? item.BaseAvatarItemId;
        item.Color = Trim(req.Color, 64);
        item.ImageName = Trim(req.ImageName, 256);
        item.AssetName = Trim(req.AssetName, 256);
        item.IsPublic = req.IsPublic ?? item.IsPublic;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        await EnsureOwnershipAsync(pid, item.Id);
        return Ok(ToWire(item));
    }

    [HttpPost("{id:guid}/purchase")]
    [Authorize]
    public async Task<IActionResult> Purchase(Guid id)
    {
        var pid = this.RequireCurrentPlayerId();
        var item = await db.CustomAvatarItems.FirstOrDefaultAsync(i => i.PublicId == id && i.IsPublic);
        if (item is null) return NotFound();
        if (await db.CustomAvatarItemOwnership.AnyAsync(o => o.PlayerId == pid && o.CustomAvatarItemId == item.Id))
            return Ok(new { Success = true, AlreadyOwned = true, Item = ToWire(item) });

        var balance = await level.GetBalanceAsync(pid, 2);
        if (balance < item.Price)
            return Ok(new { Success = false, Error = "insufficient_funds", Balance = balance });

        var newBalance = await level.GrantCurrencyAsync(pid, 2, -item.Price, $"customAvatarItem:{item.PublicId}");
        await EnsureOwnershipAsync(pid, item.Id);
        item.PurchaseCount += 1;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Balance = newBalance, Item = ToWire(item) });
    }

    private async Task EnsureOwnershipAsync(long playerId, long itemId)
    {
        if (await db.CustomAvatarItemOwnership.AnyAsync(o =>
            o.PlayerId == playerId && o.CustomAvatarItemId == itemId))
        {
            return;
        }

        db.CustomAvatarItemOwnership.Add(new CustomAvatarItemOwnershipEntity
        {
            PlayerId = playerId,
            CustomAvatarItemId = itemId,
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<Guid>> ReadIdsAsync(BulkRequest? body)
    {
        var result = new List<Guid>();
        if (body?.Ids is { Count: > 0 })
            result.AddRange(body.Ids);
        if (body?.ItemIds is { Count: > 0 })
            result.AddRange(body.ItemIds);

        foreach (var pair in Request.Query)
        foreach (var value in pair.Value)
        foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Guid.TryParse(part, out var id)) result.Add(id);

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var key in new[] { "ids", "Ids", "itemIds", "ItemIds" })
            foreach (var value in form[key])
            foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (Guid.TryParse(part, out var id)) result.Add(id);
        }

        return result.Distinct().Take(200).ToList();
    }

    private async Task<DesignRequest> ReadDesignRequestAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            return new DesignRequest
            {
                Id = Guid.TryParse(form["id"].FirstOrDefault() ?? form["Id"].FirstOrDefault(), out var id) ? id : null,
                Name = form["name"].FirstOrDefault() ?? form["Name"].FirstOrDefault(),
                Description = form["description"].FirstOrDefault() ?? form["Description"].FirstOrDefault(),
                Price = int.TryParse(form["price"].FirstOrDefault() ?? form["Price"].FirstOrDefault(), out var price) ? price : null,
                ItemType = int.TryParse(form["itemType"].FirstOrDefault() ?? form["ItemType"].FirstOrDefault(), out var itemType) ? itemType : null,
                BaseAvatarItemId = int.TryParse(form["baseAvatarItemId"].FirstOrDefault() ?? form["BaseAvatarItemId"].FirstOrDefault(), out var baseId) ? baseId : null,
                Color = form["color"].FirstOrDefault() ?? form["Color"].FirstOrDefault(),
                ImageName = form["imageName"].FirstOrDefault() ?? form["ImageName"].FirstOrDefault(),
                AssetName = form["assetName"].FirstOrDefault() ?? form["AssetName"].FirstOrDefault(),
                IsPublic = bool.TryParse(form["isPublic"].FirstOrDefault() ?? form["IsPublic"].FirstOrDefault(), out var pub) ? pub : null,
            };
        }

        try
        {
            return await JsonSerializer.DeserializeAsync<DesignRequest>(
                Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new DesignRequest();
        }
        catch (JsonException)
        {
            return new DesignRequest();
        }
    }

    private object ToWire(CustomAvatarItemEntity item) => new
    {
        Id = item.PublicId,
        CustomAvatarItemId = item.PublicId,
        CreatorId = (int)item.CreatorPlayerId,
        CreatorPlayerId = (int)item.CreatorPlayerId,
        item.Name,
        item.Description,
        item.Price,
        item.ItemType,
        item.BaseAvatarItemId,
        item.Color,
        item.ImageName,
        ImageUrl = string.IsNullOrWhiteSpace(item.ImageName) ? string.Empty : $"https://{domain.Sub("cdn")}/{item.ImageName}",
        item.AssetName,
        item.IsPublic,
        item.IsFeatured,
        item.CheerCount,
        item.PurchaseCount,
        item.CreatedAt,
        item.UpdatedAt,
        Status = item.IsPublic ? 1 : 0,
    };

    private static string Trim(string? value, int max)
    {
        var s = (value ?? string.Empty).Trim();
        return s.Length <= max ? s : s[..max];
    }

    public sealed class BulkRequest
    {
        public List<Guid>? Ids { get; set; }
        public List<Guid>? ItemIds { get; set; }
    }

    public sealed class DesignRequest
    {
        public Guid? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int? Price { get; set; }
        public int? ItemType { get; set; }
        public int? BaseAvatarItemId { get; set; }
        public string? Color { get; set; }
        public string? ImageName { get; set; }
        public string? AssetName { get; set; }
        public bool? IsPublic { get; set; }
    }
}
