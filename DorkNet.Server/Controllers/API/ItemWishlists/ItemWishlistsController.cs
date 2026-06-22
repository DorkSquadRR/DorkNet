using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.ItemWishlists;

[ApiController]
[Route("api/itemWishlists")]
[Authorize]
public class ItemWishlistsController(DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    [HttpGet]
    [HttpGet("v1/wishlist/me")]
    public async Task<IActionResult> Mine()
    {
        var rows = await db.ItemWishlists
            .Where(w => w.PlayerId == Me)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("v1/wishlist")]
    public async Task<IActionResult> ForPlayer([FromQuery] long? playerId)
    {
        var pid = playerId is > 0 ? playerId.Value : Me;
        var rows = await db.ItemWishlists
            .Where(w => w.PlayerId == pid)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpPost]
    [HttpPost("v1/wishlist")]
    public async Task<IActionResult> Add()
    {
        var req = await ReadRequestAsync();
        var key = (req.ItemKey ?? req.ItemId ?? string.Empty).Trim();
        if (key.Length == 0) return BadRequest(new { error = "missing_item" });
        if (key.Length > 128) key = key[..128];
        var itemType = req.ItemType ?? 0;

        var exists = await db.ItemWishlists.AnyAsync(w =>
            w.PlayerId == Me && w.ItemKey == key && w.ItemType == itemType);
        if (!exists)
        {
            db.ItemWishlists.Add(new ItemWishlistEntity
            {
                PlayerId = Me,
                ItemKey = key,
                ItemType = itemType,
            });
            await db.SaveChangesAsync();
        }

        return await Mine();
    }

    [HttpDelete("v1/wishlist")]
    [HttpPost("v1/wishlist/remove")]
    public async Task<IActionResult> Remove()
    {
        var req = await ReadRequestAsync();
        var key = (req.ItemKey ?? req.ItemId ?? string.Empty).Trim();
        if (key.Length == 0) return BadRequest(new { error = "missing_item" });
        var itemType = req.ItemType ?? 0;
        await db.ItemWishlists
            .Where(w => w.PlayerId == Me && w.ItemKey == key && w.ItemType == itemType)
            .ExecuteDeleteAsync();
        return await Mine();
    }

    private async Task<WishlistRequest> ReadRequestAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            return new WishlistRequest
            {
                ItemKey = form["itemKey"].FirstOrDefault() ?? form["ItemKey"].FirstOrDefault(),
                ItemId = form["itemId"].FirstOrDefault() ?? form["ItemId"].FirstOrDefault(),
                ItemType = int.TryParse(form["itemType"].FirstOrDefault() ?? form["ItemType"].FirstOrDefault(), out var type) ? type : null,
            };
        }

        var queryKey = Request.Query["itemKey"].FirstOrDefault() ?? Request.Query["ItemKey"].FirstOrDefault();
        var queryId = Request.Query["itemId"].FirstOrDefault() ?? Request.Query["ItemId"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(queryKey) || !string.IsNullOrWhiteSpace(queryId))
        {
            return new WishlistRequest
            {
                ItemKey = queryKey,
                ItemId = queryId,
                ItemType = int.TryParse(Request.Query["itemType"].FirstOrDefault() ?? Request.Query["ItemType"].FirstOrDefault(), out var type) ? type : null,
            };
        }

        try
        {
            return await JsonSerializer.DeserializeAsync<WishlistRequest>(
                Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new WishlistRequest();
        }
        catch (JsonException)
        {
            return new WishlistRequest();
        }
    }

    private static object ToWire(ItemWishlistEntity row) => new
    {
        row.Id,
        row.PlayerId,
        row.ItemKey,
        ItemId = row.ItemKey,
        row.ItemType,
        row.CreatedAt,
    };

    public sealed class WishlistRequest
    {
        public string? ItemKey { get; set; }
        public string? ItemId { get; set; }
        public int? ItemType { get; set; }
    }
}
