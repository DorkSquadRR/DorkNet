using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Inventory;

/// <summary>
/// api.rec.net/api/{equipment,consumables}/* — backs the watch's
/// Backpack tab. Wire DTOs match the decompiled client classes:
///
/// • <c>RecNet.Equipment</c> — fields: <c>PrefabName, ModificationGuid,
///   PlatformMask, IsPlatformLocked, FriendlyName, Tooltip, Rarity,
///   Favorited</c>.
/// • <c>RecNet.Consumable</c> — fields: <c>Id(long), ConsumableType,
///   PlatformMask, Count, InitialCount, UnlockedLevel, CreatedAt,
///   ActiveDurationMinutes(int?), ConsumableItem(nested), Category,
///   IsActive, IsPlatformLocked</c>.
///
/// Persistence: one <see cref="PlayerInventoryEntity"/> row per
/// (player, item). For equipment, Quantity=1 + IsActive tracks
/// favourite. For consumables, Quantity is the stack count + IsActive
/// tracks "currently activated".
/// </summary>
[ApiController]
[Authorize]
public class InventoryController(
    DorkNetDbContext db,
    NotificationService notifications) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    // Categories the StoreItem table uses for equipment vs
    // consumables. The seed data tags items per these strings.
    private static readonly HashSet<string> EquipmentCategories = new()
    {
        "equipment", "tool", "maker_pen", "weapon",
    };
    private static readonly HashSet<string> ConsumableCategories = new()
    {
        "consumable",
    };

    // ── Equipment ────────────────────────────────────────────────────────

    [HttpGet("api/equipment/v1/getUnlocked")]
    [HttpGet("api/equipment/v2/getUnlocked")]
    [HttpGet("api/equipment/v3/getUnlocked")]
    public async Task<IActionResult> GetUnlockedEquipment()
    {
        var pid = Me;
        var rows = await (from inv in db.PlayerInventory
                          join item in db.StoreItems on inv.ItemSlug equals item.Slug
                          where inv.PlayerId == pid
                                && EquipmentCategories.Contains(item.Category)
                          select new { inv, item }).ToListAsync();
        return Ok(rows.Select(r => ToEquipmentWire(r.inv, r.item)));
    }

    public sealed class EquipmentUpdateRequest
    {
        public string PrefabName { get; set; } = string.Empty;
        public string ModificationGuid { get; set; } = string.Empty;
        public bool Favorited { get; set; }
        // The 2023 client sends Equipped/IsEquipped when a weapon skin is
        // equipped (distinct from Favorited). We only have one IsActive flag,
        // and for skins "active" == "equipped", so honor whichever the client
        // sent. Without this the equip was dropped (only Favorited was read)
        // and the skin never stuck.
        public bool? Equipped { get; set; }
        public bool? IsEquipped { get; set; }
    }

    /// <summary>POST <c>api/equipment/v1/update</c> — toggle the
    /// "favorited" flag on a piece of equipment (the watch lets the
    /// player pin commonly-used tools).</summary>
    [HttpPost("api/equipment/v1/update")]
    public async Task<IActionResult> UpdateEquipment([FromBody] EquipmentUpdateRequest req)
    {
        var pid = Me;
        // Catalog-backed skins use a compound slug so we can round-trip
        // the client's separate PrefabName + ModificationGuid fields.
        var slug = !string.IsNullOrWhiteSpace(req.ModificationGuid)
            ? StoreService.EquipmentSkinSlug(req.PrefabName, req.ModificationGuid)
            : req.PrefabName;
        if (string.IsNullOrWhiteSpace(slug)) return BadRequest("missing prefab/mod identifier");

        var row = await db.PlayerInventory
            .FirstOrDefaultAsync(p => p.PlayerId == pid && p.ItemSlug == slug);
        if (row is null && !string.IsNullOrWhiteSpace(req.ModificationGuid))
        {
            // Legacy rows created before catalog-backed equipment skins
            // used the raw modification GUID as ItemSlug.
            row = await db.PlayerInventory
                .FirstOrDefaultAsync(p => p.PlayerId == pid && p.ItemSlug == req.ModificationGuid);
        }
        if (row is null && !string.IsNullOrWhiteSpace(req.PrefabName))
        {
            row = await db.PlayerInventory
                .FirstOrDefaultAsync(p => p.PlayerId == pid && p.ItemSlug == req.PrefabName);
        }
        if (row is null)
        {
            // Player doesn't own this; reject rather than silently
            // creating ownership (avoids letting clients grant items
            // to themselves).
            return Forbid();
        }
        // "equipped" is the meaningful state for weapon skins; prefer it,
        // fall back to Favorited for plain tool-pin updates.
        var active = req.Equipped ?? req.IsEquipped ?? req.Favorited;

        // One skin equipped per weapon: when equipping a catalog-backed skin,
        // clear the equipped flag on the player's OTHER skins for the SAME
        // prefab (slugs share the "{prefix}{prefab}:" head) so the weapon
        // doesn't end up with two "equipped" skins.
        if (active && !string.IsNullOrWhiteSpace(req.ModificationGuid)
            && !string.IsNullOrWhiteSpace(req.PrefabName))
        {
            var prefabPrefix = StoreService.EquipmentSkinSlug(req.PrefabName, string.Empty);
            var siblings = await db.PlayerInventory
                .Where(inv => inv.PlayerId == pid && inv.IsActive
                              && inv.ItemSlug != slug
                              && inv.ItemSlug.StartsWith(prefabPrefix))
                .ToListAsync();
            foreach (var sib in siblings) sib.IsActive = false;
        }

        row.IsActive = active;
        await db.SaveChangesAsync();
        return Ok(new { Updated = true });
    }

    // ── Consumables ──────────────────────────────────────────────────────

    // 2020.12 client bumped the URL to v2 AND changed the response shape from
    // per-item to BulkConsumable: each entry groups N instances of the same
    // ConsumableItemDesc with parallel Ids[] / CreatedAts[] arrays. Deserializer
    // is PLHFAEBLDAE.PPGFHEDFBEA reading PASCALCASE keys: Ids, CreatedAts,
    // ConsumableItemDesc, Count, InitialCount, IsActive, ActiveDurationMinutes,
    // IsTransferable. Returning the v1 per-item shape on v2 throws "Malformed
    // Response" and the chain calls BootSequence.OINIBKFNFAJ — fatal kick.
    [HttpGet("api/consumables/v1/getUnlocked")]
    public async Task<IActionResult> GetUnlockedConsumablesV1()
    {
        var pid = Me;
        var rows = await (from inv in db.PlayerInventory
                          join item in db.StoreItems on inv.ItemSlug equals item.Slug
                          where inv.PlayerId == pid
                                && ConsumableCategories.Contains(item.Category)
                          select new { inv, item }).ToListAsync();
        return Ok(rows.Select(r => ToConsumableWire(r.inv, r.item)));
    }

    [HttpGet("api/consumables/v2/getUnlocked")]
    public async Task<IActionResult> GetUnlockedConsumablesV2()
    {
        var pid = Me;
        var rows = await (from inv in db.PlayerInventory
                          join item in db.StoreItems on inv.ItemSlug equals item.Slug
                          where inv.PlayerId == pid
                                && ConsumableCategories.Contains(item.Category)
                          select new { inv, item }).ToListAsync();

        // Group by ConsumableItemDesc — one BulkConsumable per consumable type.
        var bulk = rows
            .GroupBy(r => StoreService.TryGetConsumableItemDesc(r.item.Slug, out var d) ? d : r.item.Slug)
            .Select(g => new
            {
                Ids                   = g.Select(r => r.inv.Id).ToArray(),
                CreatedAts            = g.Select(r => r.inv.AcquiredAt).ToArray(),
                ConsumableItemDesc    = g.Key,
                Count                 = g.Sum(r => r.inv.Quantity),
                InitialCount          = g.Sum(r => r.inv.Quantity),
                IsActive              = false,
                ActiveDurationMinutes = 0,
                IsTransferable        = false,
            });
        return Ok(bulk);
    }

    public sealed class ConsumableItemRequest
    {
        public long Id { get; set; }
        public int DeltaCount { get; set; }
    }

    [HttpPost("api/consumables/v1/consume")]
    public async Task<IActionResult> Consume([FromBody] ConsumableItemRequest req)
    {
        var pid = Me;
        var row = await db.PlayerInventory
            .FirstOrDefaultAsync(p => p.PlayerId == pid && p.Id == req.Id);
        if (row is null) return NotFound();
        var delta = Math.Max(1, Math.Abs(req.DeltaCount));
        row.Quantity = Math.Max(0, row.Quantity - delta);
        if (row.Quantity == 0) db.PlayerInventory.Remove(row);
        await db.SaveChangesAsync();

        await notifications.NotifyAsync(pid,
            row.Quantity > 0
                ? PushNotificationId.ConsumableMappingAdded
                : PushNotificationId.ConsumableMappingRemoved,
            new { row.Id, row.ItemSlug, row.Quantity });
        return Ok(new { row.Id, Count = row.Quantity });
    }

    [HttpPost("api/consumables/v1/transfer")]
    public async Task<IActionResult> TransferConsumable(
        [FromForm] long? id,
        [FromForm] long? recipientPlayerId,
        [FromForm] int quantity = 1)
    {
        var pid = Me;
        var inventoryId = id ?? (long.TryParse(Request.Query["id"], out var qId) ? qId : 0);
        var recipient = recipientPlayerId ?? (long.TryParse(Request.Query["recipientPlayerId"], out var qRecipient) ? qRecipient : 0);
        var amount = Math.Max(1, quantity);
        if (inventoryId <= 0 || recipient <= 0 || recipient == pid)
            return BadRequest("invalid_transfer");

        var source = await db.PlayerInventory.FirstOrDefaultAsync(p => p.PlayerId == pid && p.Id == inventoryId);
        if (source is null || source.Quantity < amount) return NotFound();
        var target = await db.PlayerInventory
            .FirstOrDefaultAsync(p => p.PlayerId == recipient && p.ItemSlug == source.ItemSlug);
        if (target is null)
        {
            target = new PlayerInventoryEntity
            {
                PlayerId = recipient,
                ItemSlug = source.ItemSlug,
                Quantity = 0,
            };
            db.PlayerInventory.Add(target);
        }

        source.Quantity -= amount;
        target.Quantity += amount;
        if (source.Quantity == 0) db.PlayerInventory.Remove(source);
        await db.SaveChangesAsync();
        await notifications.NotifyAsync(recipient,
            PushNotificationId.ConsumableMappingAdded,
            new { target.Id, target.ItemSlug, target.Quantity });
        return Ok(new { Success = true, SourceCount = Math.Max(0, source.Quantity), RecipientCount = target.Quantity });
    }

    public sealed class ActivateConsumableRequest
    {
        public long Id { get; set; }
        public bool IsActive { get; set; }
    }

    [HttpPost("api/consumables/v1/updateActive")]
    public async Task<IActionResult> UpdateActive([FromBody] ActivateConsumableRequest req)
    {
        var pid = Me;
        var row = await db.PlayerInventory
            .FirstOrDefaultAsync(p => p.PlayerId == pid && p.Id == req.Id);
        if (row is null) return NotFound();
        row.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return Ok(new { row.Id, row.IsActive });
    }

    // ── Wire serializers ─────────────────────────────────────────────────

    private static object ToEquipmentWire(PlayerInventoryEntity inv, StoreItemEntity item)
    {
        var prefabName = item.Slug;
        var modificationGuid = inv.ItemSlug;
        var rarity = 0;
        if (StoreService.TryGetEquipmentPayload(item.Slug, out var prefab, out var mod, out var skin))
        {
            prefabName = prefab;
            modificationGuid = mod;
            rarity = skin?.rarity ?? 0;
        }

        return new
        {
            PrefabName = prefabName,
            ModificationGuid = modificationGuid,
            PlatformMask = -1, // All
            IsPlatformLocked = false,
            FriendlyName = item.DisplayName,
            Tooltip = item.Description,
            Rarity = rarity,
            // IsActive is the equipped/active flag; surface it under every key
            // the 2023 client reads so the equipped skin shows on reload.
            Favorited = inv.IsActive,
            Equipped = inv.IsActive,
            IsEquipped = inv.IsActive,
        };
    }

    private static object ToConsumableWire(PlayerInventoryEntity inv, StoreItemEntity item)
    {
        var type = StoreService.TryGetConsumableItemDesc(item.Slug, out var consumableDesc)
            ? consumableDesc
            : item.Slug;
        var category = StoreService.TryGetHairDyePayload(item.Slug, out _)
            ? 6
            : 0;

        return new
        {
            inv.Id,
            ConsumableItemDesc = type,
            ConsumableType = type,
            PlatformMask = -1,
            Count = inv.Quantity,
            InitialCount = inv.Quantity, // we don't track original purchase count
            UnlockedLevel = 0,
            CreatedAt = inv.AcquiredAt,
            ActiveDurationMinutes = (int?)null,
            ConsumableItem = new
            {
                Id = inv.Id,
                Type = type,
                FriendlyName = item.DisplayName,
                Tooltip = item.Description,
                ImageName = item.ImageName,
            },
            Category = category,
            inv.IsActive,
            IsPlatformLocked = false,
        };
    }
}
