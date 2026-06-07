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
    }

    /// <summary>POST <c>api/equipment/v1/update</c> — toggle the
    /// "favorited" flag on a piece of equipment (the watch lets the
    /// player pin commonly-used tools).</summary>
    // The 2018 client posts a JSON ARRAY of equipment objects (RecNet.cs:21812)
    // — each {PrefabName, ModificationGuid, UnlockedLevel, Favorited}. Accept an
    // array OR a single object so both shapes bind. Slug = modification GUID, or
    // the prefab name when no mod is applied. Unowned items are skipped (not a
    // hard reject) so one stale entry doesn't drop the whole batch.
    [HttpPost("api/equipment/v1/update")]
    public async Task<IActionResult> UpdateEquipment([FromBody] System.Text.Json.JsonElement body)
    {
        var pid = Me;
        var items = new List<System.Text.Json.JsonElement>();
        if (body.ValueKind == System.Text.Json.JsonValueKind.Array)
            items.AddRange(body.EnumerateArray());
        else if (body.ValueKind == System.Text.Json.JsonValueKind.Object)
            items.Add(body);

        static string? Str(System.Text.Json.JsonElement e, string k)
            => e.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
                ? v.GetString() : null;
        static bool Bool(System.Text.Json.JsonElement e, string k)
            => e.TryGetProperty(k, out var v) && (v.ValueKind == System.Text.Json.JsonValueKind.True
                || (v.ValueKind == System.Text.Json.JsonValueKind.String && v.GetString() == "true"));

        foreach (var el in items)
        {
            var slug = Str(el, "ModificationGuid") is { Length: > 0 } mod ? mod : Str(el, "PrefabName");
            if (string.IsNullOrWhiteSpace(slug)) continue;
            var row = await db.PlayerInventory
                .FirstOrDefaultAsync(p => p.PlayerId == pid && p.ItemSlug == slug);
            if (row is null) continue; // skip items the player doesn't own
            row.IsActive = Bool(el, "Favorited");
        }
        await db.SaveChangesAsync();
        return Ok(new { Updated = true });
    }

    // ── Consumables ──────────────────────────────────────────────────────

    [HttpGet("api/consumables/v1/getUnlocked")]
    public async Task<IActionResult> GetUnlockedConsumables()
    {
        var pid = Me;
        var rows = await (from inv in db.PlayerInventory
                          join item in db.StoreItems on inv.ItemSlug equals item.Slug
                          where inv.PlayerId == pid
                                && ConsumableCategories.Contains(item.Category)
                          select new { inv, item }).ToListAsync();
        return Ok(rows.Select(r => ToConsumableWire(r.inv, r.item)));
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

    private static object ToEquipmentWire(PlayerInventoryEntity inv, StoreItemEntity item) => new
    {
        PrefabName = item.Slug, // for store-derived items the slug doubles as prefab name
        ModificationGuid = inv.ItemSlug,
        PlatformMask = -1, // All
        IsPlatformLocked = false,
        FriendlyName = item.DisplayName,
        Tooltip = item.Description,
        Rarity = 0, // Common (no rarity column on StoreItem yet)
        Favorited = inv.IsActive,
    };

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
