using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Avatar.V2;

/// <summary>
/// api.rec.net/api/avatar/v2/gifts — wardrobe gift inbox. Mirrors
/// the client's <c>RecNet.Avatars.DowloadGiftPackages</c> +
/// <c>LocalRequestGiftPackage</c> + <c>LocalConsumeGiftPackage</c>
/// flow. Wire DTO matches <c>RecNet.Avatars+GiftPackage</c>
/// (<c>Cpp2IL_CS/.../RecNet/Avatars.cs:192-555</c>) field-for-field
/// so the watch's deserialiser doesn't crash.
///
/// Server semantics:
/// - <c>GET v2/gifts</c> returns unconsumed gifts for caller.
/// - <c>POST v2/gifts/generate</c> rolls a random unlocked-avatar
///   item and stamps a <see cref="GiftPackageEntity"/> for caller —
///   used by drop tiles and "free daily" prompts.
/// - <c>POST v2/gifts/consume/{id}</c> marks consumed and appends
///   the <see cref="GiftPackageEntity.AvatarItemDescOrHairDyeDesc"/>
///   to the player's <see cref="AvatarEntity.InventoryJson"/> so it
///   shows up in their backpack on next load.
/// </summary>
[ApiController]
[Authorize]
public class AvatarGiftsController(
    DorkNetDbContext db,
    NotificationService notifications,
    LevelService level) : ControllerBase
{
    [HttpGet("api/avatar/v2/gifts")]
    [HttpGet("api/avatar/v3/gifts")]
    [HttpGet("api/avatar/v4/gifts")]
    public async Task<IActionResult> List()
    {
        var pid = this.RequireCurrentPlayerId();
        var rows = await db.GiftPackages
            .Where(g => g.RecipientPlayerId == pid && !g.Consumed)
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    public sealed record GenerateGiftRequest(int? GiftContext);

    /// <summary>POST <c>api/avatar/v2/gifts/generate</c> — server picks
    /// a random unlocked-avatar item and queues a gift package. The
    /// 2020 client posts to this when the user clicks a free-gift
    /// tile or hits a context the client maps to <c>GiftContext</c>.</summary>
    [HttpPost("api/avatar/v2/gifts/generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateGiftRequest? req)
    {
        var pid = this.RequireCurrentPlayerId();
        // Pick a random wardrobe StoreItem (Slug starts with
        // "wardrobe-" — that's the convention StoreService uses for
        // catalogue rows derived from outfits_assets bundles).
        var pool = await db.StoreItems
            .Where(s => s.IsActive && s.Slug.StartsWith("wardrobe-"))
            .Select(s => new { s.Slug, s.DisplayName })
            .ToListAsync();
        if (pool.Count == 0) return StatusCode(503, "no wardrobe items in catalogue");

        var rng = new Random();
        var pick = pool[rng.Next(pool.Count)];
        var guid = pick.Slug["wardrobe-".Length..];

        // Bare GUIDs crash AvatarItem.FromRecNetString — append `,,,`
        // so the watch's parse-and-iterate doesn't blow up in
        // GiftManager.DequeueGift. See AvatarItemsController.cs:31-40
        // for the canonical wire-format note.
        var gift = new GiftPackageEntity
        {
            RecipientPlayerId = pid,
            FromPlayerId = null,
            AvatarItemType = 0, // Outfit
            AvatarItemDescOrHairDyeDesc = guid + ",,,",
            CurrencyType = 0, // Invalid (no currency in this gift)
            GiftContext = req?.GiftContext ?? 0,
            GiftRarity = RollRarity(rng),
            Message = $"Enjoy your new {pick.DisplayName}!",
            Platform = -1,
            PackageVariant = "Standard",
            PackageMaterial = string.Empty,
            IsValid = true,
            SupportsCurrentPlatform = true,
        };
        db.GiftPackages.Add(gift);
        await db.SaveChangesAsync();

        // Push so the watch shows the new-gift toast.
        await notifications.NotifyAsync(pid,
            PushNotificationId.GiftPackageReceived, ToWire(gift));
        return Ok(ToWire(gift));
    }

    [HttpPost("api/avatar/v2/gifts/consume/{id:long}")]
    public Task<IActionResult> ConsumeViaPath(long id) => ConsumeImpl(id);

    /// <summary>POST <c>/api/avatar/v2/gifts/consume/</c> (note trailing
    /// slash, no id in path). The watch's <c>LocalConsumeGiftPackage</c>
    /// (verified at <c>Cpp2IL_ISIL/.../RecNet/Avatars_NestedType__LocalConsumeGiftPackage_d__19.txt:276-301</c>)
    /// posts to this exact URL with a form-urlencoded body containing
    /// <c>Id</c> and <c>UnlockedLevel</c> fields. The previous
    /// implementation took <c>[FromBody] ConsumeGiftRequest</c> which
    /// only accepts <c>application/json</c> — the watch's form-encoded
    /// body got 415 Unsupported Media Type
    /// (<c>output_log.txt:'Failed to consume gift … HTTP Error 415'</c>),
    /// which left the gift unconsumed → the avatar item never landed
    /// in the player's inventory → mirror-unequip rendered a missing
    /// item and crashed.
    ///
    /// Accept both JSON and form-urlencoded by reading the raw body
    /// and pulling <c>Id</c> from whichever container we got.</summary>
    [HttpPost("api/avatar/v2/gifts/consume")]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data", "text/plain")]
    public async Task<IActionResult> ConsumeViaBody()
    {
        long id = 0;
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            var raw = form["Id"].ToString();
            if (string.IsNullOrWhiteSpace(raw)) raw = form["id"].ToString();
            long.TryParse(raw, out id);
        }
        else
        {
            using var reader = new System.IO.StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("Id", out var v) ||
                        doc.RootElement.TryGetProperty("id", out v))
                    {
                        if (v.ValueKind == System.Text.Json.JsonValueKind.Number)
                            v.TryGetInt64(out id);
                        else if (v.ValueKind == System.Text.Json.JsonValueKind.String)
                            long.TryParse(v.GetString(), out id);
                    }
                }
                catch (System.Text.Json.JsonException) { /* fall through; id stays 0 */ }
            }
        }
        if (id == 0)
        {
            var raw = Request.Query["Id"].ToString();
            if (string.IsNullOrWhiteSpace(raw)) raw = Request.Query["id"].ToString();
            long.TryParse(raw, out id);
        }
        if (id == 0) return BadRequest("missing Id");
        return await ConsumeImpl(id);
    }

    private async Task<IActionResult> ConsumeImpl(long id)
    {
        var pid = this.RequireCurrentPlayerId();
        var gift = await db.GiftPackages.FirstOrDefaultAsync(g => g.Id == id);
        if (gift is null) return NotFound();
        if (gift.RecipientPlayerId != pid) return Forbid();
        if (gift.Consumed) return Ok(ToWire(gift)); // idempotent

        // Append avatar item to player's inventory list (JSON list of
        // GUID strings). Defensive parse — corrupted JSON resets to
        // empty list rather than 500.
        if (!string.IsNullOrEmpty(gift.AvatarItemDescOrHairDyeDesc))
        {
            var avatar = await db.Avatars.FirstOrDefaultAsync(a => a.PlayerId == pid);
            if (avatar is null)
            {
                avatar = new AvatarEntity { PlayerId = pid };
                db.Avatars.Add(avatar);
            }
            try
            {
                var list = System.Text.Json.JsonSerializer
                    .Deserialize<List<string>>(avatar.InventoryJson) ?? new();
                if (!list.Contains(gift.AvatarItemDescOrHairDyeDesc))
                {
                    list.Add(gift.AvatarItemDescOrHairDyeDesc);
                    avatar.InventoryJson = System.Text.Json.JsonSerializer.Serialize(list);
                    avatar.UpdatedAt = DateTime.UtcNow;
                }
            }
            catch
            {
                avatar.InventoryJson = System.Text.Json.JsonSerializer.Serialize(
                    new[] { gift.AvatarItemDescOrHairDyeDesc });
                avatar.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Apply currency / XP / level rewards bundled with the gift.
        // The previous implementation only handled the avatar item path,
        // so token gifts from the admin SPA marked themselves consumed
        // but never moved the player's wallet — the player tapped the
        // gift, the box played its animation, and the balance stayed
        // unchanged. CurrencyType is the canonical RecNet enum
        // (1 = Tokens, 2 = Coins; 0 means "no currency in this gift").
        if (gift.CurrencyType > 0 && gift.Currency > 0)
        {
            await level.GrantCurrencyAsync(pid, gift.CurrencyType, gift.Currency,
                $"gift_consumed:{gift.Id}");
        }
        if (gift.Xp > 0)
        {
            await level.AwardXpAsync(pid, gift.Xp, $"gift_consumed:{gift.Id}");
        }
        // Direct level grants bypass XP curves — same pattern
        // /api/admin/v1/players/{id}/levelup uses. We don't surface
        // that as a separate helper; bump the column inline.
        if (gift.Level > 0)
        {
            var player = await db.Players.FirstOrDefaultAsync(p => p.Id == pid);
            if (player is not null) player.Level += gift.Level;
        }

        gift.Consumed = true;
        gift.ConsumedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Profile-update push so the watch's wallet readout refreshes
        // without waiting for the next /balance poll. Mirrors what
        // /api/admin/v1/players/{id}/gift does after the inbox push so
        // the toast number animates up the moment the box closes.
        if (gift.CurrencyType > 0 || gift.Xp > 0 || gift.Level > 0)
        {
            await notifications.NotifyAsync(pid,
                PushNotificationId.SubscriptionUpdateProfile,
                new { Reason = "GiftConsumed", gift.CurrencyType, gift.Currency, gift.Xp, gift.Level });
        }

        return Ok(ToWire(gift));
    }

    /// <summary>Weighted rarity roll — Common 65, Uncommon 25,
    /// Rare 8, Epic 1.8, Legendary 0.2 percent.</summary>
    private static int RollRarity(Random rng) => rng.NextDouble() switch
    {
        < 0.65 => 0,    // Common
        < 0.90 => 10,   // Uncommon
        < 0.98 => 20,   // Rare
        < 0.998 => 30,  // Epic
        _ => 50,        // Legendary
    };

    // Wire shape verified against the 2020.12 watch's
    // NLEKGNENMCO+LOCNECLOHCA gift DTO deserializer (Cpp2IL ISIL
    // NLEKGNENMCO_NestedType_LOCNECLOHCA.txt:792-946). All keys read
    // via Util.GetKey<T> (mandatory): Id, Message, FromPlayerId,
    // AvatarItemType, AvatarItemDesc, ConsumableItemDesc,
    // EquipmentPrefabName, EquipmentModificationGuid, Platform,
    // PlatformsToSpawnOn, BalanceType, CurrencyType, Currency, Xp,
    // GiftRarity, GiftContext. Missing any one → KeyNotFoundException
    // surfaces as "Failed to download gifts: Malformed Response" from
    // BootSequence and BLOCKS LOGIN.
    public static object ToWire(GiftPackageEntity g) => new
    {
        g.Id,
        g.FromPlayerId,
        g.ConsumableItemDesc,
        g.AvatarItemType,
        AvatarItemDesc = g.AvatarItemDescOrHairDyeDesc,
        g.AvatarItemDescOrHairDyeDesc,
        g.EquipmentPrefabName,
        g.EquipmentModificationGuid,
        g.CurrencyType,
        g.Currency,
        g.Xp,
        g.Level,
        g.GiftContext,
        g.GiftRarity,
        g.Message,
        g.Platform,
        // PlatformsToSpawnOn — required by the watch's gift DTO
        // deserializer. This is a SINGLE int (4-byte field at offset
        // 0x6C in LOCNECLOHCA, separate enum from Platform itself).
        // The watch reads it with strict Util.GetKey<int>, so sending
        // an empty array crashes Convert.ChangeType([], typeof(int))
        // with "Object must implement IConvertible". -1 mirrors the
        // sibling Platform field's "every platform" sentinel — no
        // platform-gated gift restrictions on a private server.
        PlatformsToSpawnOn = -1,
        // BalanceType — required mandatory key on the gift DTO. The
        // enum identifies whether the gift's payload is a Currency
        // balance update (0) vs Inventory item (1) vs XP (2). For
        // currency-bearing gifts we'd send 0; the safe default for
        // our seed gifts (which are item-only) is 0 too since the
        // watch only reads it to dispatch the balance-update side
        // and we never carry one. Wrong type doesn't crash.
        BalanceType = 0,
        g.PackageMaterial,
        g.PackageVariant,
        g.Consumed,
        g.IsValid,
        g.ErrorMessage,
        g.SupportsCurrentPlatform,
        g.IsGifted,
    };
}
