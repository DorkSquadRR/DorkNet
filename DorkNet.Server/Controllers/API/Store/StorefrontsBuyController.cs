using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Controllers.API.RoomKeys;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Store;

/// <summary>
/// api.rec.net/api/storefronts/v2/{buyItem,buyTier,buyElite} — the
/// secondary purchase URL family used by the watch's gift-drop
/// tiles and season-pass tier buttons. Wire request matches
/// <c>RecNet.RequestPurchaseItemDTO</c>:
/// <c>StorefrontType, PurchasableItemId, CurrencyType,
/// CouponConsumablePlayerMappingId, Gift</c>.
///
/// Response is <c>PurchaseBalanceUpdateResponseDTO</c>:
/// <c>BalanceUpdates: List&lt;BalanceUpdateDTO&gt;</c>. The client
/// applies these balance deltas optimistically + reads the optional
/// gift package from inventory after.
///
/// The primary purchase URL (<c>api/purchase/v1/*</c>) is handled
/// elsewhere by PurchaseController; this just covers the v2 surface.
/// </summary>
[ApiController]
[Authorize]
public class StorefrontsBuyController(
    DorkNetDbContext db,
    LevelService level,
    NotificationService notifications) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    public sealed class RequestPurchaseItemDto
    {
        public int StorefrontType { get; set; }
        public int PurchasableItemId { get; set; }
        public int CurrencyType { get; set; } = 2;
        public long? CouponConsumablePlayerMappingId { get; set; }
        public GiftDto? Gift { get; set; }
    }

    public sealed class GiftDto
    {
        public long ToPlayerId { get; set; }
        public string? Message { get; set; }
        public bool Anonymous { get; set; }
        public int GiftContext { get; set; }
    }

    [HttpPost("api/storefronts/v2/buyItem")]
    public async Task<IActionResult> BuyItem([FromBody] RequestPurchaseItemDto req)
    {
        if (req is null) return BadRequest("missing body");
        var pid = Me;
        var item = await db.StoreItems.FirstOrDefaultAsync(s => s.Id == req.PurchasableItemId && s.IsActive);
        if (item is null) return NotFound(BalanceUpdateResponse(0, 2 /* CurrencyType */, null, null));

        var balance = await level.GetBalanceAsync(pid, req.CurrencyType);
        if (balance < item.Price)
        {
            // Insufficient funds — return current balance, no item granted.
            return Ok(BalanceUpdateResponse(balance, req.CurrencyType, null, null));
        }

        // Gift block (when present) means the BUYER spends the tokens but
        // the ITEM goes to Gift.ToPlayerId. Self-gifting is disallowed —
        // the watch's gift UI prevents it, but guard server-side too.
        var giftTarget = req.Gift is { ToPlayerId: > 0 } g && g.ToPlayerId != pid
            ? g
            : null;
        var recipient = giftTarget?.ToPlayerId ?? pid;
        var isGift = giftTarget is not null;

        var newBalance = await level.GrantCurrencyAsync(pid, req.CurrencyType, -item.Price,
            isGift ? $"giftItem:{item.Slug}->{recipient}" : $"buyItem:{item.Slug}");

        // Grant the item to the RECIPIENT (= buyer for self-purchase, gift
        // target for gift purchase). Avatar payloads append to
        // AvatarEntity.InventoryJson; consumables and everything else
        // insert a PlayerInventory row.
        if (StoreService.TryGetAvatarItemPayload(item.Slug, out _, out var avatarItemDesc))
        {
            await GrantWardrobeAsync(recipient, StoreService.InventoryAvatarItemDesc(avatarItemDesc));
        }
        else
        {
            await GrantInventoryAsync(recipient, item.Slug);
        }

        // Insert a real GiftPackageEntity for the gift-box flow. The watch
        // shows a gift-box animation after a purchase, then POSTs the
        // package Id to /api/avatar/v2/gifts/consume/ to claim it. If the
        // Id isn't a real row, that consume returns 404 and the watch
        // logs 'Server failed to consume gift … HTTP Error 404' — the
        // user's symptom on every successful buy. By writing the row
        // synchronously and returning its DB-assigned Id in the
        // BalanceUpdate response, the subsequent consume succeeds.
        StoreService.TryGetAvatarItemPayload(item.Slug, out var avatarItemType, out avatarItemDesc);
        var inventoryAvatarDesc = StoreService.InventoryAvatarItemDesc(avatarItemDesc);
        StoreService.TryGetConsumableItemDesc(item.Slug, out var consumableItemDesc);
        StoreService.TryGetEquipmentPayload(item.Slug, out var equipmentPrefabName, out var equipmentModificationGuid);
        if (StoreService.TryGetHairDyePayload(item.Slug, out var hairDyeConsumableDesc, out var hairDyeColorGuid))
        {
            avatarItemType = 1;
            inventoryAvatarDesc = hairDyeColorGuid;
            consumableItemDesc = hairDyeConsumableDesc;
        }
        var gift = new GiftPackageEntity
        {
            RecipientPlayerId           = recipient,
            FromPlayerId                = isGift && !giftTarget!.Anonymous ? (int?)pid : null,
            AvatarItemType              = string.IsNullOrEmpty(inventoryAvatarDesc) ? null : avatarItemType,
            AvatarItemDescOrHairDyeDesc = inventoryAvatarDesc,
            ConsumableItemDesc          = consumableItemDesc,
            EquipmentPrefabName         = equipmentPrefabName,
            EquipmentModificationGuid   = equipmentModificationGuid,
            CurrencyType                = req.CurrencyType,
            Currency                    = 0,
            Xp                          = 0,
            Level                       = 1,
            GiftContext                 = isGift ? giftTarget!.GiftContext : 0,
            GiftRarity                  = 0,
            Message                     = isGift ? (giftTarget!.Message ?? string.Empty) : string.Empty,
            Platform                    = -1,
            PackageVariant              = "Standard",
            PackageMaterial             = string.Empty,
            Consumed                    = false,
            IsValid                     = true,
            SupportsCurrentPlatform     = true,
        };
        db.GiftPackages.Add(gift);
        await db.SaveChangesAsync();

        // Push the buyer's balance update so their wallet UI refreshes.
        await notifications.NotifyAsync(pid,
            PushNotificationId.StorefrontBalancePurchase,
            new { req.CurrencyType, Balance = newBalance, ItemId = item.Id, item.Slug });

        if (isGift)
        {
            // Tell the recipient a gift is waiting. The watch dispatches
            // GiftPackageReceived (id=30) to refresh /api/avatar/v2/gifts
            // and pop the "you got a gift" toast; the gift box itself
            // appears next time the recipient opens their inventory.
            await notifications.NotifyAsync(recipient,
                PushNotificationId.GiftPackageReceived,
                new
                {
                    Id           = gift.Id,
                    Platform     = -1,
                    Xp           = 0,
                    Level        = 1,
                    GiftRarity   = 0,
                    GiftContext  = gift.GiftContext,
                    FromPlayerId = gift.FromPlayerId,
                    AvatarItemType = gift.AvatarItemType,
                    AvatarItemDesc = avatarItemDesc,
                    ConsumableItemDesc = gift.ConsumableItemDesc ?? string.Empty,
                    EquipmentPrefabName = gift.EquipmentPrefabName,
                    EquipmentModificationGuid = gift.EquipmentModificationGuid,
                    Message      = gift.Message ?? string.Empty,
                });
        }

        // For gift purchases the buyer doesn't get a box — their flow
        // ends with the balance update. Returning an empty Data array
        // (giftPackageId = null) makes the watch's BuyItem callback
        // skip the gift-receive animation on the sender's screen.
        return Ok(BalanceUpdateResponse(newBalance, req.CurrencyType,
            isGift ? null : item.Slug,
            isGift ? null : gift.Id));
    }

    [HttpPost("api/storefronts/v2/buyTier")]
    public async Task<IActionResult> BuyTier([FromBody] RequestPurchaseItemDto req)
    {
        // Season-pass tier: no per-item table, so we just deduct a
        // small fixed cost (1 token) per tier and ack. A full
        // season-pass system would have a SeasonProgressEntity.
        var pid = Me;
        const int TierCost = (int)StoreService.StoreItemPrice;
        var balance = await level.GetBalanceAsync(pid, req.CurrencyType);
        if (balance < TierCost) return Ok(BalanceUpdateResponse(balance, req.CurrencyType));
        var newBalance = await level.GrantCurrencyAsync(pid, req.CurrencyType, -TierCost,
            $"buyTier:{req.PurchasableItemId}");
        return Ok(BalanceUpdateResponse(newBalance, req.CurrencyType));
    }

    [HttpPost("api/storefronts/v2/buyElite")]
    public async Task<IActionResult> BuyElite([FromBody] RequestPurchaseItemDto req)
    {
        // Elite-tier purchase (paid premium track). Without real
        // money flow on a private server we just ack.
        var pid = Me;
        var balance = await level.GetBalanceAsync(pid, req.CurrencyType);
        return Ok(BalanceUpdateResponse(balance, req.CurrencyType));
    }

    /// <summary>GET <c>api/storefronts/v1/buyRoomKey</c> — purchase a
    /// persisted room key. The 2020.12 client sends roomKeyId and the
    /// price it displayed so we reject stale-price purchases.</summary>
    [HttpGet("api/storefronts/v1/buyRoomKey")]
    public async Task<IActionResult> BuyRoomKey(
        [FromQuery] long roomKeyId,
        [FromQuery] int requestedPrice)
    {
        var pid = Me;
        var key = await db.RoomKeys.FirstOrDefaultAsync(k => k.Id == roomKeyId && !k.IsDeleted);
        if (key is null)
            return Ok(RoomKeyPurchaseResponse(RoomKeyStatus.DoesNotExist, null, await level.GetBalanceAsync(pid, 2)));
        if (key.CreatorPlayerId == pid)
            return Ok(RoomKeyPurchaseResponse(RoomKeyStatus.PlayerAlreadyOwns, key, await level.GetBalanceAsync(pid, 2)));
        if (requestedPrice != key.Price)
            return Ok(RoomKeyPurchaseResponse(RoomKeyStatus.InvalidParameters, key, await level.GetBalanceAsync(pid, 2)));

        var existing = await db.RoomKeyPurchases
            .FirstOrDefaultAsync(p => p.RoomKeyId == key.Id && p.PlayerId == pid);
        if (existing is not null)
            return Ok(RoomKeyPurchaseResponse(RoomKeyStatus.PlayerAlreadyOwns, key, await level.GetBalanceAsync(pid, 2)));

        var balance = await level.GetBalanceAsync(pid, 2);
        if (balance < key.Price)
            return Ok(RoomKeyPurchaseResponse(RoomKeyStatus.PurchaseFailed, key, balance));

        var newBalance = await level.GrantCurrencyAsync(pid, 2, -key.Price, $"buyRoomKey:{key.Id}");
        db.RoomKeyPurchases.Add(new RoomKeyPurchaseEntity
        {
            RoomKeyId = key.Id,
            PlayerId = pid,
            PaidPrice = key.Price,
        });
        await db.SaveChangesAsync();

        if (key.CreatorPlayerId != pid && key.Price > 0)
            await level.GrantCurrencyAsync(key.CreatorPlayerId, 2, key.Price, $"sellRoomKey:{key.Id}");

        return Ok(RoomKeyPurchaseResponse(RoomKeyStatus.Success, key, newBalance));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task GrantWardrobeAsync(long pid, string guid)
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
            if (!list.Contains(guid))
            {
                list.Add(guid);
                avatar.InventoryJson = System.Text.Json.JsonSerializer.Serialize(list);
                avatar.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch
        {
            avatar.InventoryJson = System.Text.Json.JsonSerializer.Serialize(new[] { guid });
            avatar.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    private async Task GrantInventoryAsync(long pid, string slug)
    {
        var row = await db.PlayerInventory
            .FirstOrDefaultAsync(p => p.PlayerId == pid && p.ItemSlug == slug);
        if (row is null)
        {
            db.PlayerInventory.Add(new PlayerInventoryEntity
            {
                PlayerId = pid,
                ItemSlug = slug,
                Quantity = 1,
            });
        }
        else
        {
            row.Quantity += 1;
        }
        await db.SaveChangesAsync();
    }

    private static object BalanceUpdateResponse(long balance, int currencyType,
        string? grantedSlug = null, long? giftPackageId = null)
    {
        // PurchaseBalanceUpdateResponseDTO<GiftPackage> wire shape, verified
        // against Cpp2IL_ISIL/.../RecNet/Avatars_NestedType_GiftPackage.txt:
        //
        //   PurchaseBalanceUpdateResponseDTO EXTENDS BalanceResponseDTO,
        //   so the watch first runs BalanceResponseDTO.Deserialize which
        //   does Util.GetKey<long>("Balance") / GetKey<int>("CurrencyType")
        //   / GetKey<int>("BalanceType") — REQUIRED, KeyNotFoundException
        //   on miss. Mirror the trio on the OUTER object.
        //
        //   Then it reads BalanceUpdates: List<BalanceUpdateDTO<GiftPackage>>.
        //   Each inner BalanceUpdateDTO has UpdateResponse + Data, where
        //   Data is List<GiftPackage>. GiftPackage.Deserialize REQUIRES:
        //     "Id" (long)         | Util.GetKey
        //     "Platform" (int)    | Util.GetKey
        //     "Xp" (int)          | Util.GetKey
        //     "Level" (int)       | Util.GetKey
        //     "GiftRarity" (int)  | Util.GetKey
        //     "GiftContext" (int) | Util.GetKey
        //   Plus optional FromPlayerId / AvatarItemType / AvatarItemDesc /
        //   ConsumableItemDesc / EquipmentPrefabName / EquipmentModificationGuid /
        //   CurrencyType / Currency / Message.
        //
        // The previous shape shipped {CurrencyType,Balance,BalanceType,Platform}
        // in the Data array — those don't match GiftPackage's required keys,
        // so Util.GetKey<long>("Id") threw KeyNotFoundException, the
        // ApiCallback received a non-null error string, and StorePurchaseItem
        // Screen logged the error as "Null". The user's
        // output_log.txt:920 shows that callback path firing on every
        // purchase attempt. New shape ships a real (synthetic) GiftPackage
        // with all required keys + the granted item's GUID under
        // AvatarItemDesc so the watch's gift-receive flow can preview the
        // unlocked item.
        //
        // CRITICAL: the watch follows up every purchase with a POST to
        // /api/avatar/v2/gifts/consume/ containing this Id. If the Id
        // isn't a real GiftPackageEntity row in the DB, consume returns
        // 404 and the watch's <RunConsumeGift> logs 'Server failed to
        // consume gift … HTTP Error 404', which the player-destroy
        // cascade picks up on (the user's symptom on every buy:
        // game crashes after opening the gift box). Caller MUST pass
        // <paramref name="giftPackageId"/> = the saved
        // GiftPackageEntity.Id. The previous code used
        // DateTime.UtcNow.Ticks as a fake id.
        var giftId = giftPackageId ?? DateTime.UtcNow.Ticks;
        // Pull the GUID off the slug for wardrobe-* purchases so the
        // watch's GiftPackage receives the AvatarItem GUID and can
        // render the unlock animation. dorknet-* slugs leave the desc
        // empty (those are non-wardrobe SKUs).
        var avatarItemType = 0;
        var avatarItemDesc = string.Empty;
        var consumableItemDesc = string.Empty;
        var equipmentPrefabName = string.Empty;
        var equipmentModificationGuid = string.Empty;
        if (grantedSlug is not null)
        {
            StoreService.TryGetAvatarItemPayload(grantedSlug, out avatarItemType, out avatarItemDesc);
            StoreService.TryGetConsumableItemDesc(grantedSlug, out consumableItemDesc);
            StoreService.TryGetEquipmentPayload(grantedSlug, out equipmentPrefabName, out equipmentModificationGuid);
            if (StoreService.TryGetHairDyePayload(grantedSlug, out var hairDyeConsumableDesc, out var hairDyeColorGuid))
            {
                avatarItemType = 1;
                avatarItemDesc = hairDyeColorGuid;
                consumableItemDesc = hairDyeConsumableDesc;
            }
        }

        var giftPackage = new
        {
            // REQUIRED via Util.GetKey — KeyNotFoundException on miss.
            // Verified keys at NLEKGNENMCO_NestedType_LOCNECLOHCA.txt:792-946:
            //   Id, Message, FromPlayerId, AvatarItemType, AvatarItemDesc,
            //   ConsumableItemDesc, EquipmentPrefabName,
            //   EquipmentModificationGuid, Platform, PlatformsToSpawnOn,
            //   BalanceType, CurrencyType, Currency, Xp, GiftRarity,
            //   GiftContext. Missing any → "Failed to download gifts:
            //   Malformed Response" (and on a purchase path, the buy
            //   times out because the response never deserializes).
            Id          = giftId,
            Platform    = 0,
            PlatformsToSpawnOn = -1,
            BalanceType = 0,
            Xp          = 0,
            Level       = 1,
            GiftRarity  = 0,
            GiftContext = 0,
            // Optional — surfaced to the watch's gift-preview UI.
            FromPlayerId              = (int?)null,
            AvatarItemType            = string.IsNullOrEmpty(avatarItemDesc) ? (int?)null : avatarItemType,
            AvatarItemDesc            = avatarItemDesc,
            AvatarItemDescOrHairDyeDesc = StoreService.InventoryAvatarItemDesc(avatarItemDesc),
            ConsumableItemDesc        = consumableItemDesc,
            EquipmentPrefabName       = equipmentPrefabName,
            EquipmentModificationGuid = equipmentModificationGuid,
            CurrencyType              = currencyType,
            Currency                  = 0,
            Message                   = string.Empty,
            Consumed                  = false,
            ErrorMessage              = string.Empty,
        };

        return new
        {
            // BalanceResponseDTO required trio.
            Balance = balance,
            CurrencyType = currencyType,
            BalanceType = 0,
            // PurchaseBalanceUpdateResponseDTO.BalanceUpdates =
            // List<BalanceUpdateDTO<GiftPackage>>.
            BalanceUpdates = new[]
            {
                new
                {
                    UpdateResponse = 0, // UpdateResponseTypes.Success
                    Data           = grantedSlug is null
                        ? Array.Empty<object>()
                        : new object[] { giftPackage },
                },
            },
        };
    }

    private static object RoomKeyPurchaseResponse(RoomKeyStatus status, RoomKeyEntity? key, long balance) => new
    {
        RoomKeyResponse = RoomKeysController.RoomKeyResponse(status, key),
        BalanceUpdateResponse = new
        {
            Balance = balance,
            CurrencyType = 2,
            BalanceType = 0,
            BalanceUpdates = Array.Empty<object>(),
        },
    };
}
