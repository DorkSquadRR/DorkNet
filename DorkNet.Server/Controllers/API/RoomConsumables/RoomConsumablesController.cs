using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.RoomConsumables;

[ApiController]
public class RoomConsumablesController(DorkNetDbContext db, LevelService level) : ControllerBase
{
    [HttpGet("api/roomconsumables")]
    [HttpGet("api/roomconsumables/v1/roomConsumable")]
    [AllowAnonymous]
    public async Task<IActionResult> List([FromQuery] long? id = null)
    {
        var query = db.StoreItems
            .Where(i => i.IsActive && i.Category.ToLower() == "consumable");
        if (id is long itemId && itemId > 0)
            query = query.Where(i => i.Id == itemId);
        var rows = await query
            .OrderBy(i => i.DisplayName)
            .Take(200)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpPost("api/roomconsumables/v1/roomconsumable/{itemId:long}/purchase/currency")]
    [Authorize]
    public Task<IActionResult> PurchaseCurrency(long itemId, [FromQuery] int? currencyType = null) =>
        Purchase(itemId, currencyType);

    [HttpPost("api/roomconsumables/v1/roomconsumable/{itemId:long}/purchase/tokens")]
    [Authorize]
    public Task<IActionResult> PurchaseTokens(long itemId) => Purchase(itemId, 2);

    private async Task<IActionResult> Purchase(long itemId, int? currencyType)
    {
        var me = this.RequireCurrentPlayerId();
        var item = await db.StoreItems
            .FirstOrDefaultAsync(i => i.Id == itemId && i.IsActive && i.Category.ToLower() == "consumable");
        if (item is null) return NotFound();

        var spendCurrency = currencyType ?? item.CurrencyType;
        var balance = await level.GetBalanceAsync(me, spendCurrency);
        if (balance < item.Price)
            return Ok(new { Success = false, Error = "insufficient_funds", Balance = balance, CurrencyType = spendCurrency });

        var newBalance = await level.GrantCurrencyAsync(me, spendCurrency, -item.Price, $"roomConsumable:{item.Slug}");
        var inventory = await db.PlayerInventory
            .FirstOrDefaultAsync(i => i.PlayerId == me && i.ItemSlug == item.Slug);
        if (inventory is null)
        {
            inventory = new PlayerInventoryEntity
            {
                PlayerId = me,
                ItemSlug = item.Slug,
                Quantity = 0,
            };
            db.PlayerInventory.Add(inventory);
        }
        inventory.Quantity += 1;
        await db.SaveChangesAsync();
        return Ok(new
        {
            Success = true,
            Balance = newBalance,
            CurrencyType = spendCurrency,
            Item = ToWire(item),
            Quantity = inventory.Quantity,
        });
    }

    private static object ToWire(StoreItemEntity item) => new
    {
        RoomConsumableId = item.Id,
        ItemId = item.Id,
        item.Slug,
        Name = item.DisplayName,
        item.Description,
        item.ImageName,
        item.Price,
        item.CurrencyType,
        item.Category,
    };
}
