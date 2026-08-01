using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Services;
using System.Text.Json.Serialization;

namespace DorkNet.Server.Controllers.Econ;

/// <summary>
/// econ.{rec.net,localhost} — token balance + inventory queries used
/// by older client flows. Reads from <see cref="LevelService"/>
/// (currency type 0 = Token per <c>CurrencyType.cs</c>) and the
/// <see cref="PlayerInventoryEntity"/> table. Note: the primary
/// store/inventory surface is api.*/api/storefronts/* + api/inventory/*;
/// this host is a legacy alias.
/// </summary>
[ApiController]
[Authorize]
public class EconController(DorkNetDbContext db, LevelService level) : ControllerBase
{
    [HttpGet("/econ/v1/balance/{accountId:long}")]
    public async Task<ActionResult<TokenBalance>> GetBalance(long accountId)
    {
        var tokens = await level.GetBalanceAsync(accountId, 0);
        return Ok(new TokenBalance
        {
            AccountId = accountId,
            Tokens = (int)tokens,
            BonusTokens = 0,
        });
    }

    [HttpGet("/econ/v1/ownershipbatch")]
    public async Task<IActionResult> GetOwnershipBatch([FromQuery] string? ids)
    {
        var pid = this.RequireCurrentPlayerId();
        var slugs = (ids ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var q = db.PlayerInventory.Where(p => p.PlayerId == pid);
        if (slugs.Count > 0) q = q.Where(p => slugs.Contains(p.ItemSlug));
        var rows = await q.Select(p => new { p.ItemSlug, p.Quantity }).ToListAsync();
        return Ok(rows);
    }

    [HttpGet("/econ/v1/inventory/{accountId:long}")]
    public async Task<IActionResult> GetInventory(long accountId)
    {
        var rows = await db.PlayerInventory
            .Where(p => p.PlayerId == accountId)
            .Select(p => new { p.ItemSlug, p.Quantity })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet("/econ/v1/products")]
    public async Task<IActionResult> GetProducts()
    {
        var rows = await db.StoreItems
            .Where(s => s.IsActive)
            .Select(s => new
            {
                Id = s.Id,
                Slug = s.Slug,
                Name = s.DisplayName,
                Price = s.Price,
                CurrencyType = s.CurrencyType,
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet("/econ/customAvatarItems")]
    [HttpGet("/econ/customAvatarItems/v1/owned")]
    public async Task<IActionResult> GetOwnedCustomAvatarItems([FromQuery] int skip = 0, [FromQuery] int take = 100)
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
            Results = rows.Select(ToCustomAvatarItemWire),
            TotalResults = total,
        });
    }

    /// <summary>The client reads this body as a BARE integer — the issuing
    /// method is <c>FGLDKEJLAKB&lt;System.Int32&gt; KPOENLDFAJD()</c>
    /// (RecNet.Runtime/MPBLNLMCEDL.txt:2881, route literal on :2955).
    /// Returning a JSON object made the deserialize throw, so the ownership-cap
    /// check that runs before a custom-shirt purchase always errored out.</summary>
    [HttpGet("/econ/customAvatarItems/v1/itemOwnershipLimit")]
    public IActionResult CustomAvatarItemOwnershipLimit()
        => Content("1000", "application/json");

    private static object ToCustomAvatarItemWire(DorkNet.Server.Data.Entities.CustomAvatarItemEntity i) => new
    {
        CustomAvatarItemId = i.PublicId,
        Id = i.PublicId,
        CreatorPlayerId = (int)i.CreatorPlayerId,
        i.Name,
        i.Description,
        i.Price,
        i.ItemType,
        i.ImageName,
        i.AssetName,
        i.Color,
    };
}

public class TokenBalance
{
    [JsonPropertyName("AccountId")] public long AccountId { get; set; }
    [JsonPropertyName("Tokens")] public int Tokens { get; set; }
    [JsonPropertyName("BonusTokens")] public int BonusTokens { get; set; }
}
