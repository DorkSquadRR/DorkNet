using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
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
    [HttpGet("api/roomconsumables/v1/roomConsumable/room/{roomId:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> List([FromRoute] long? roomId = null, [FromQuery] long? id = null)
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

    [HttpGet("api/roomconsumables/v1/roomConsumable/room/{roomId:long}/me")]
    [Authorize]
    public async Task<IActionResult> MineForRoom(long roomId)
    {
        var me = this.RequireCurrentPlayerId();
        var rows = await (from inventory in db.PlayerInventory
                          join item in db.StoreItems on inventory.ItemSlug equals item.Slug
                          where inventory.PlayerId == me
                              && inventory.Quantity > 0
                              && item.IsActive
                              && item.Category.ToLower() == "consumable"
                          orderby item.DisplayName
                          select new { Inventory = inventory, Item = item })
            .Take(200)
            .ToListAsync();
        return Ok(rows.Select(row => ToInventoryWire(row.Item, row.Inventory)));
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

    private static object ToWire(StoreItemEntity item)
    {
        var roomConsumableId = StableRoomConsumableId(item.Id).ToString("D");
        return new
        {
            RoomConsumableId = roomConsumableId,
            Name = item.DisplayName,
            item.ImageName,
            item.Description,
            item.Price,
            CurrencyId = (Guid?)null,
            CreatedAt = DateTimeOffset.UnixEpoch,
            ItemId = item.Id,
            item.Slug,
            item.CurrencyType,
            item.Category,
        };
    }

    private static object ToInventoryWire(StoreItemEntity item, PlayerInventoryEntity inventory)
    {
        var createdAt = inventory.AcquiredAt == default
            ? DateTimeOffset.UnixEpoch
            : new DateTimeOffset(DateTime.SpecifyKind(inventory.AcquiredAt, DateTimeKind.Utc));
        return new
        {
            Id = StableRoomConsumableId(item.Id).ToString("D"),
            ConsumableItemDesc = ToWire(item),
            Count = Math.Max(0, inventory.Quantity),
            InitialCount = Math.Max(0, inventory.Quantity),
            CreatedAt = createdAt,
            ActiveDurationMinutes = 0,
            IsActive = inventory.IsActive,
            IsTransferable = false,
        };
    }

    private static Guid StableRoomConsumableId(long itemId)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"roomConsumable:{itemId}"));
        return new Guid(hash);
    }
}
