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

        /// <summary>The price the tile displayed, sent as a stale-price guard.
        /// The field was missing here, so buyTier/buyElite had nothing to
        /// validate against and fell back to a flat cost. Verified on the
        /// 2023-03-21 build: <c>Nullable&lt;Int64&gt;</c> at +32 in
        /// <c>RecNet.Runtime/DCFKEFHJAGC_NestedType_RequestPurchaseItemDTO.txt:123-137</c>,
        /// wire key <c>"RequestedPrice"</c> in the generated JSON contract
        /// <c>RecNet.Runtime/FPLGJGDJIJG.txt:534</c>.</summary>
        public long? RequestedPrice { get; set; }

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
        // The client matches this gift's item against api/avatar/v4/items by
        // EXACT AvatarItemDesc string in OutfitManager.UnlockAvatarItemAndMarkNew
        // (fired after the buyer consumes the post-purchase gift box). v4/items
        // emits outfit items as the 4-part "{guid},,," desc (the bare guid
        // crashes the client's AvatarItem parser), so the gift must carry the
        // SAME 4-part desc — sending the stripped bare guid made Find() miss and
        // NRE, which surfaced as items vanishing on every buy. Hair dyes match
        // by their bare colour guid (inventoryAvatarDesc), unchanged.
        var giftWireDesc = string.IsNullOrEmpty(avatarItemDesc) ? inventoryAvatarDesc : avatarItemDesc;
        var gift = new GiftPackageEntity
        {
            RecipientPlayerId           = recipient,
            FromPlayerId                = isGift && !giftTarget!.Anonymous ? (int?)pid : null,
            AvatarItemType              = string.IsNullOrEmpty(giftWireDesc) ? null : avatarItemType,
            AvatarItemDescOrHairDyeDesc = giftWireDesc,
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

    /// <summary>POST <c>api/storefronts/v2/buyTier</c> — season-pass "skip a
    /// tier" button. The tier is an ordinary StoreItems row on the
    /// <c>season:{n}</c> storefront (see StorefrontsController.Season), so its
    /// real price is charged rather than a flat 1 token, and the purchase is
    /// recorded as a <c>seasonTier:*</c> progress row so the season page can
    /// count how many tiers the player bought.</summary>
    [HttpPost("api/storefronts/v2/buyTier")]
    public async Task<IActionResult> BuyTier([FromBody] RequestPurchaseItemDto req)
    {
        if (req is null) return BadRequest("missing body");
        var pid = Me;
        var item = await db.StoreItems
            .FirstOrDefaultAsync(s => s.Id == req.PurchasableItemId && s.IsActive);
        // Tiers that aren't seeded as StoreItems still cost the catalogue
        // default so the button isn't free on a bare install.
        var cost = Math.Max(0, item?.Price ?? StoreService.StoreItemPrice);

        var balance = await level.GetBalanceAsync(pid, req.CurrencyType);
        // Stale-price guard: the client sends the price its tile showed.
        if (req.RequestedPrice is long requested && requested != cost)
            return Ok(BalanceUpdateResponse(balance, req.CurrencyType));
        if (balance < cost) return Ok(BalanceUpdateResponse(balance, req.CurrencyType));

        var newBalance = cost == 0
            ? balance
            : await level.GrantCurrencyAsync(pid, req.CurrencyType, -cost,
                $"buyTier:{req.PurchasableItemId}");
        await RecordSeasonProgressAsync(pid,
            $"seasonTier:{req.StorefrontType}:{req.PurchasableItemId}");
        return Ok(BalanceUpdateResponse(newBalance, req.CurrencyType));
    }

    /// <summary>POST <c>api/storefronts/v2/buyElite</c> — season "elite
    /// upgrade" (the paid premium track). There is no real-money flow on a
    /// private server, so it is priced in tokens off the same StoreItems row
    /// the season storefront advertised. The flag is persisted as a
    /// <c>seasonElite:*</c> progress row.</summary>
    [HttpPost("api/storefronts/v2/buyElite")]
    public async Task<IActionResult> BuyElite([FromBody] RequestPurchaseItemDto req)
    {
        if (req is null) return BadRequest("missing body");
        var pid = Me;
        var eliteKey = $"seasonElite:{req.StorefrontType}";
        var balance = await level.GetBalanceAsync(pid, req.CurrencyType);

        // Already upgraded — ack without charging again so a replayed tap
        // can't drain the wallet.
        if (await db.ObjectiveProgress.AnyAsync(o => o.PlayerId == pid && o.Key == eliteKey))
            return Ok(BalanceUpdateResponse(balance, req.CurrencyType));

        var item = await db.StoreItems
            .FirstOrDefaultAsync(s => s.Id == req.PurchasableItemId && s.IsActive);
        var cost = Math.Max(0, item?.Price ?? StoreService.StoreItemPrice);
        if (req.RequestedPrice is long requested && requested != cost)
            return Ok(BalanceUpdateResponse(balance, req.CurrencyType));
        if (balance < cost) return Ok(BalanceUpdateResponse(balance, req.CurrencyType));

        var newBalance = cost == 0
            ? balance
            : await level.GrantCurrencyAsync(pid, req.CurrencyType, -cost,
                $"buyElite:{req.PurchasableItemId}");
        await RecordSeasonProgressAsync(pid, eliteKey);
        return Ok(BalanceUpdateResponse(newBalance, req.CurrencyType));
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

    public sealed class PurchaseRoomKeyWithCurrencyRequest
    {
        public long RoomKeyId { get; set; }
        public long RequestedPrice { get; set; }
        public Guid RequestedPurchaseCurrencyId { get; set; }
    }

    /// <summary>POST <c>api/storefronts/v1/PurchaseRoomKeyWithCurrency</c> —
    /// buying a room key with a ROOM currency instead of tokens.
    ///
    /// This was only registered as a <c>[HttpGet]</c> alias of BuyRoomKey, so
    /// every attempt 405'd. The 2023-03-21 client issues it with verb 2 (POST —
    /// <c>RecNet.Runtime/DCFKEFHJAGC.txt:7426</c>) and three form fields,
    /// <c>RoomKeyId</c> (Int64, :7439), <c>RequestedPrice</c> (Int64, :7451) and
    /// <c>RequestedPurchaseCurrencyId</c> (Guid, :7464); the DTO signature is
    /// <c>INBGDNMFJBP(Int64, Int64, Guid)</c> at :7206.
    ///
    /// The response is <c>RoomKeyPurchaseWithCurrencyResponseDTO</c>, whose
    /// generated contract has exactly two members — <c>Balance</c> and
    /// <c>RoomKeyResponse</c> (<c>RecNet.Runtime/EDMKBBPGJAB.txt:203/222</c>) —
    /// where Balance is the ROOM-currency balance row
    /// {AccountId, CurrencyId, Balance, ModifiedAt}
    /// (<c>RecNet.Runtime/DPNLANKOHON.txt:331-414</c>), NOT the token
    /// BalanceUpdateResponse that the GET alias returned.</summary>
    [HttpPost("api/storefronts/v1/PurchaseRoomKeyWithCurrency")]
    public async Task<IActionResult> PurchaseRoomKeyWithCurrency(
        [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))]
        PurchaseRoomKeyWithCurrencyRequest req)
    {
        var pid = Me;
        var currency = await db.RoomCurrencies.FirstOrDefaultAsync(
            c => c.PublicId == req.RequestedPurchaseCurrencyId && !c.IsDeleted);
        var key = await db.RoomKeys.FirstOrDefaultAsync(
            k => k.Id == req.RoomKeyId && !k.IsDeleted);

        RoomCurrencyBalanceEntity? row = null;
        if (currency is not null)
        {
            var currencyRowId = currency.Id;
            row = await db.RoomCurrencyBalances.FirstOrDefaultAsync(
                b => b.PlayerId == pid && b.RoomCurrencyId == currencyRowId);
        }

        if (currency is null)
            return Ok(RoomKeyCurrencyPurchaseResponse(
                RoomKeyStatus.InvalidParameters, key, pid, req.RequestedPurchaseCurrencyId, null));
        if (key is null)
            return Ok(RoomKeyCurrencyPurchaseResponse(
                RoomKeyStatus.DoesNotExist, null, pid, currency.PublicId, row));
        if (key.CreatorPlayerId == pid)
            return Ok(RoomKeyCurrencyPurchaseResponse(
                RoomKeyStatus.PlayerAlreadyOwns, key, pid, currency.PublicId, row));
        var roomKeyRowId = key.Id;
        if (await db.RoomKeyPurchases.AnyAsync(p => p.RoomKeyId == roomKeyRowId && p.PlayerId == pid))
            return Ok(RoomKeyCurrencyPurchaseResponse(
                RoomKeyStatus.PlayerAlreadyOwns, key, pid, currency.PublicId, row));
        if (req.RequestedPrice != key.Price)
            return Ok(RoomKeyCurrencyPurchaseResponse(
                RoomKeyStatus.InvalidParameters, key, pid, currency.PublicId, row));

        var balance = row?.Balance ?? 0;
        if (balance < key.Price)
            return Ok(RoomKeyCurrencyPurchaseResponse(
                RoomKeyStatus.PurchaseFailed, key, pid, currency.PublicId, row));

        if (row is null)
        {
            row = new RoomCurrencyBalanceEntity
            {
                PlayerId = pid,
                RoomCurrencyId = currency.Id,
                Balance = 0,
            };
            db.RoomCurrencyBalances.Add(row);
        }
        row.Balance = Math.Max(0, row.Balance - key.Price);
        row.UpdatedAt = DateTime.UtcNow;
        db.RoomKeyPurchases.Add(new RoomKeyPurchaseEntity
        {
            RoomKeyId = key.Id,
            PlayerId = pid,
            PaidPrice = key.Price,
        });
        await db.SaveChangesAsync();

        return Ok(RoomKeyCurrencyPurchaseResponse(
            RoomKeyStatus.Success, key, pid, currency.PublicId, row));
    }

    /// <summary>GET <c>api/storefronts/v2/buyInvention</c> — MakerPen store
    /// purchase of a paid invention. Verb is 0/GET
    /// (<c>RecNet.Runtime/DCFKEFHJAGC.txt:6772</c>) with query fields
    /// <c>inventionId</c> (Int64, :6785) and <c>requestedPrice</c> (Int32,
    /// :6797) — the latter was being ignored.
    ///
    /// The response type is <c>InventionPurchaseResponseDTO</c>, whose contract
    /// is {<c>InventionResponse</c>, <c>BalanceUpdateResponse</c>}
    /// (<c>RecNet.Runtime/MAHHAAEGEEP.txt:215/242</c>). The handler used to
    /// return the bare token-balance wrapper, so <c>InventionResponse</c> was
    /// absent and the client got Status/Invention/InventionVersion = null —
    /// it could never confirm the purchase or spawn what it just bought.
    /// InventionResponse itself is {Status, Invention, InventionVersion}
    /// (<c>RecNet.Runtime/GMBGBPNMGBA.txt:255/274/290</c>) — the same triple
    /// InventionsController already returns from addversion.</summary>
    [HttpGet("api/storefronts/v2/buyInvention")]
    [HttpPost("api/storefronts/v2/buyInvention")]
    public async Task<IActionResult> BuyInvention(
        [FromQuery] long inventionId,
        [FromQuery] long? requestedPrice = null,
        [FromQuery] int currencyType = 2)
    {
        var pid = Me;
        var balance = await level.GetBalanceAsync(pid, currencyType);
        var inv = await db.Inventions.FirstOrDefaultAsync(
            i => i.Id == inventionId && !i.IsDeleted && i.IsPublished);
        if (inv is null)
            return Ok(InventionPurchaseResponse(
                InventionStatusDoesNotExist, null, null, balance, currencyType));

        var version = await db.InventionVersions
            .Where(v => v.InventionId == inv.Id)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync();
        var price = Math.Max(0, inv.Price);

        // Stale-price guard — the MakerPen tile sends the price it rendered.
        if (requestedPrice is long asked && asked != price)
            return Ok(InventionPurchaseResponse(
                InventionStatusInvalidParameters, inv, version, balance, currencyType));

        var key = $"invention_purchase:{inv.Id}";
        var alreadyOwns = inv.CreatorPlayerId == pid
            || await db.ObjectiveProgress.AnyAsync(o => o.PlayerId == pid && o.Key == key);
        if (alreadyOwns)
        {
            // Deliberately Success rather than PlayerAlreadyOwns (=19): the
            // MakerPen re-issues this call on every spawn of a paid creation,
            // and a non-zero status blocks the spawn.
            return Ok(InventionPurchaseResponse(
                InventionStatusSuccess, inv, version, balance, currencyType));
        }

        if (balance < price)
            return Ok(InventionPurchaseResponse(
                InventionStatusPurchaseFailed, inv, version, balance, currencyType));

        var newBalance = price == 0
            ? balance
            : await level.GrantCurrencyAsync(pid, currencyType, -price, $"buyInvention:{inv.Id}");
        db.ObjectiveProgress.Add(new ObjectiveProgressEntity
        {
            PlayerId = pid,
            Key = key,
            IsCompleted = true,
            ClearedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        if (price > 0 && inv.CreatorPlayerId != pid)
            await level.GrantCurrencyAsync(inv.CreatorPlayerId, currencyType, price,
                $"sellInvention:{inv.Id}");

        return Ok(InventionPurchaseResponse(
            InventionStatusSuccess, inv, version, newBalance, currencyType));
    }

    /// <summary>Body of <c>buyForFreeGiftButton</c>. Unlike its siblings this
    /// route does NOT use form fields: the client serialises a
    /// <c>RequestPurchaseForFreeGiftButtonDTO</c> and posts it as the whole
    /// body (<c>RecNet.Runtime/DCFKEFHJAGC.txt:5910-5924</c>, verb 2 at :5912).
    /// Field offsets +16/+24/+40/+44/+48 in
    /// <c>DCFKEFHJAGC_NestedType_RequestPurchaseForFreeGiftButtonDTO.txt</c>,
    /// wire keys PurchasableItemId / CouponConsumablePlayerMappingId / Count /
    /// CreatorPlayerId / RequestedPrice in <c>RecNet.Runtime/IAPMDDFENDD.txt:395-502</c>.
    /// </summary>
    public sealed class FreeGiftButtonRequest
    {
        public int PurchasableItemId { get; set; }
        public long? CouponConsumablePlayerMappingId { get; set; }
        public int Count { get; set; } = 1;
        public int CreatorPlayerId { get; set; }
        public long? RequestedPrice { get; set; }
    }

    /// <summary>POST <c>api/storefronts/v1/buyForFreeGiftButton</c> — the
    /// in-room "free gift button" gadget handing an item to whoever pressed it.
    ///
    /// The handler bound <c>[FromQuery] currencyType/price</c> while the client
    /// sends everything in the JSON body, so the requested item id never
    /// arrived and a hardcoded 25-token "FreeGiftButton" package was inserted
    /// instead — the gadget paid out the wrong reward on every press. Now the
    /// body is read and the requested StoreItems row is granted directly.
    ///
    /// The response is the plain <c>BalanceResponseDTO</c>
    /// (<c>DCFKEFHJAGC.txt:6008/6055</c>) whose contract is exactly
    /// {Balance, CurrencyType, Platform} — <c>RecNet.Runtime/PECGEJAAMHB.txt:255/274/298</c>.
    /// There is no gift-package member, so nothing can be handed back through a
    /// gift box here; the item has to land in the inventory server-side.</summary>
    [HttpPost("api/storefronts/v1/buyForFreeGiftButton")]
    [HttpGet("api/storefronts/v1/buyForFreeGiftButton")]
    public async Task<IActionResult> BuyForFreeGiftButton(
        [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))]
        FreeGiftButtonRequest req,
        [FromQuery] int currencyType = 2)
    {
        var pid = Me;
        var balance = await level.GetBalanceAsync(pid, currencyType);
        var item = await db.StoreItems
            .FirstOrDefaultAsync(s => s.Id == req.PurchasableItemId && s.IsActive);
        if (item is null) return Ok(BalanceResponse(balance, currencyType));

        // RequestedPrice is what the gadget quoted, and it is the total for the
        // press — the gadget is normally free, so the item's catalogue price is
        // deliberately NOT charged here. CouponConsumablePlayerMappingId is
        // carried by the DTO but there is no coupon-mapping table to redeem it
        // against yet, so it is accepted and ignored.
        var price = Math.Max(0, req.RequestedPrice ?? 0);
        if (balance < price) return Ok(BalanceResponse(balance, currencyType));
        var newBalance = price == 0
            ? balance
            : await level.GrantCurrencyAsync(pid, currencyType, -price,
                $"freeGiftButton:{item.Slug}");

        await GrantPurchasedItemAsync(pid, item, Math.Clamp(req.Count, 1, 50));
        return Ok(BalanceResponse(newBalance, currencyType));
    }

    /// <summary>Form body of <c>buyProgressionEventXpBoost</c>, verified at
    /// <c>RecNet.Runtime/DCFKEFHJAGC.txt:12431</c> (progressionEventId, Int64),
    /// :12444 (purchasableXpBoostId, Guid), :12456 (requestedPrice, Int32) and
    /// :12468 (expectedXp, Int32).</summary>
    public sealed class ProgressionEventXpBoostRequest
    {
        public long ProgressionEventId { get; set; }
        public Guid PurchasableXpBoostId { get; set; }
        public int RequestedPrice { get; set; }
        public int ExpectedXp { get; set; }
    }

    /// <summary>POST <c>api/storefronts/v1/buyProgressionEventXpBoost</c>.
    ///
    /// Two defects: the handler bound <c>[FromQuery] currencyType/price</c> so
    /// none of the four form fields arrived (nothing charged, no XP granted),
    /// and it reused the shared BalanceUpdateResponse whose
    /// <c>BalanceUpdates[].Data</c> is a LIST. This route's response is
    /// <c>BalanceUpdateResponseDTO`1&lt;PurchasedProgressionEventXpBoostDTO&gt;</c>
    /// (signature at <c>DCFKEFHJAGC.txt:12118</c>) and that family's inner Data
    /// is a SINGLE object — <c>get_Data</c> returns <c>DataTypeDTO</c>, not
    /// <c>List&lt;DataTypeDTO&gt;</c>, in
    /// <c>DCFKEFHJAGC_NestedType_BalanceUpdateResponseDTO`1_NestedType_DEJIAFEKFCK.txt:83</c>
    /// (contrast the Purchase* variant at
    /// <c>..._PurchaseBalanceUpdateResponseDTO`1_NestedType_PKKNEGNNABE.txt:83</c>,
    /// which IS a List). An array there makes the reader throw.
    /// The payload object is {Xp:Int32} — <c>RecNet.Runtime/NNEJHCKLOHL.txt:139</c>.</summary>
    [HttpPost("api/storefronts/v1/buyProgressionEventXpBoost")]
    [HttpGet("api/storefronts/v1/buyProgressionEventXpBoost")]
    public async Task<IActionResult> BuyProgressionEventXpBoost(
        [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))]
        ProgressionEventXpBoostRequest req,
        [FromQuery] int currencyType = 2)
    {
        var pid = Me;
        var price = Math.Max(0, req.RequestedPrice);
        var balance = await level.GetBalanceAsync(pid, currencyType);
        if (balance < price) return Ok(XpBoostResponse(balance, currencyType, 0));

        var newBalance = price == 0
            ? balance
            : await level.GrantCurrencyAsync(pid, currencyType, -price,
                $"progressionEventXpBoost:{req.ProgressionEventId}");

        // The client tells us how much XP the boost it just paid for is worth;
        // clamped so a spoofed field can't mint arbitrary levels.
        var xp = Math.Clamp(req.ExpectedXp, 0, 100_000);
        if (xp > 0)
            await level.AwardXpAsync(pid, xp, $"progressionEventXpBoost:{req.ProgressionEventId}");

        // Upsert: (PlayerId, Key) is unique, and a boost can legitimately be
        // bought more than once — a blind Add threw
        // "UNIQUE constraint failed" and turned the second purchase into a 500
        // AFTER the currency had already been taken.
        var progressKey = $"progressionEvent:{req.ProgressionEventId}:xpBoost:{req.PurchasableXpBoostId:N}";
        var progress = await db.ObjectiveProgress
            .FirstOrDefaultAsync(o => o.PlayerId == pid && o.Key == progressKey);
        if (progress is null)
        {
            db.ObjectiveProgress.Add(new ObjectiveProgressEntity
            {
                PlayerId = pid,
                Key = progressKey,
                IsCompleted = true,
                ClearedAt = DateTime.UtcNow,
            });
        }
        else
        {
            progress.IsCompleted = true;
            progress.ClearedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        return Ok(XpBoostResponse(newBalance, currencyType, xp));
    }

    /// <summary>Form body of <c>buyPurchaseReminder</c>, verified at
    /// <c>RecNet.Runtime/DCFKEFHJAGC.txt:10203</c> (purchaseReminderId, Int32)
    /// and :10215 (requestedPrice, Int64).</summary>
    public sealed class PurchaseReminderRequest
    {
        public int PurchaseReminderId { get; set; }
        public long RequestedPrice { get; set; }
    }

    /// <summary>POST <c>api/storefronts/v1/buyPurchaseReminder</c> — buying the
    /// item a "you were looking at this" reminder popup advertises.
    ///
    /// Was a stub returning <c>{Success:true}</c>; the client deserialises
    /// <c>JNPGKDJJPPM</c> = {Balance:Int64, CurrencyType, BalanceType, Data:
    /// List&lt;GiftPackage&gt;} (signature at <c>DCFKEFHJAGC.txt:9928</c>, keys at
    /// <c>RecNet.Runtime/OLIHBGPDPHF.txt:319/338/362/386</c>), so every field
    /// defaulted and the wallet read 0. Note this DTO really does spell its
    /// third member <c>BalanceType</c>, unlike the BalanceUpdateResponse family
    /// which renames the same property to <c>Platform</c>.
    ///
    /// There is no reminder catalogue on a private server — the offer feed
    /// (<c>reminder/currentTokenBundles/v2</c>) is genuinely empty — so the
    /// reminder id is resolved as the StoreItems row it points at. Unknown ids
    /// charge nothing and return an empty Data list.</summary>
    [HttpPost("api/storefronts/v1/buyPurchaseReminder")]
    [HttpGet("api/storefronts/v1/buyPurchaseReminder")]
    public async Task<IActionResult> BuyPurchaseReminder(
        [ModelBinder(typeof(DorkNet.Server.Binding.FormOrJsonModelBinder))]
        PurchaseReminderRequest req,
        [FromQuery] int currencyType = 2)
    {
        var pid = Me;
        var balance = await level.GetBalanceAsync(pid, currencyType);
        var item = await db.StoreItems
            .FirstOrDefaultAsync(s => s.Id == req.PurchaseReminderId && s.IsActive);
        if (item is null)
            return Ok(PurchaseReminderResponse(balance, currencyType, null, null));
        // Stale-price guard; 0 means the popup didn't quote a price.
        if (req.RequestedPrice > 0 && req.RequestedPrice != item.Price)
            return Ok(PurchaseReminderResponse(balance, currencyType, null, null));
        if (balance < item.Price)
            return Ok(PurchaseReminderResponse(balance, currencyType, null, null));

        var newBalance = item.Price == 0
            ? balance
            : await level.GrantCurrencyAsync(pid, currencyType, -item.Price,
                $"buyPurchaseReminder:{item.Slug}");
        var giftId = await GrantWithGiftBoxAsync(pid, item, currencyType);

        db.ObjectiveProgress.Add(new ObjectiveProgressEntity
        {
            PlayerId = pid,
            Key = $"purchaseReminder:{req.PurchaseReminderId}",
            IsCompleted = true,
            ClearedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        return Ok(PurchaseReminderResponse(newBalance, currencyType, item.Slug, giftId));
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

        return new
        {
            // BalanceResponseDTO required trio.
            Balance = balance,
            CurrencyType = currencyType,
            // The 2023-03-21 build renames the base DTO's third member to
            // "Platform" on the whole BalanceUpdateResponse family — its
            // generated contract table lists exactly BalanceUpdates / Balance /
            // CurrencyType / Platform and has NO "BalanceType"
            // (RecNet.Runtime/KILGEFOENFN.txt:319/346/362/386, identical in
            // BBEFAELEANL/IBAPHBCOBHL/ALGKCAKCFAJ/AIEJFLGKIGN). Emitting only
            // BalanceType left Platform defaulted to 0 on the client. Both are
            // shipped: the 2020.12 watch reads BalanceType and unknown members
            // are ignored on both readers.
            Platform = 0,
            BalanceType = 0,
            // PurchaseBalanceUpdateResponseDTO.BalanceUpdates =
            // List<BalanceUpdateDTO<GiftPackage>>. Note the Data member here is
            // a LIST (…_PurchaseBalanceUpdateResponseDTO`1_NestedType_PKKNEGNNABE.txt:83)
            // — the plain BalanceUpdateResponseDTO family uses a single object.
            BalanceUpdates = new[]
            {
                new
                {
                    UpdateResponse = 0, // UpdateResponseTypes.Success
                    Data           = grantedSlug is null
                        ? Array.Empty<object>()
                        : new object[] { GiftPackageWire(giftId, currencyType, grantedSlug) },
                },
            },
        };
    }

    /// <summary>One <c>FFFIMAGLKEG/FHMABOHAEED</c> gift package.
    ///
    /// REQUIRED via Util.GetKey — KeyNotFoundException on miss. Verified keys at
    /// NLEKGNENMCO_NestedType_LOCNECLOHCA.txt:792-946 and re-confirmed against
    /// the 2023-03-21 contract table RecNet.Runtime/FKANCKLCCDI.txt:1051-1374:
    ///   Id, Message, FromPlayerId, AvatarItemType, AvatarItemDesc,
    ///   ConsumableItemDesc, EquipmentPrefabName, EquipmentModificationGuid,
    ///   Platform, PlatformsToSpawnOn, BalanceType, CurrencyType, Currency, Xp,
    ///   GiftRarity, GiftContext. Missing any → "Failed to download gifts:
    ///   Malformed Response" (and on a purchase path, the buy times out because
    ///   the response never deserializes).</summary>
    private static object GiftPackageWire(long giftId, int currencyType, string? grantedSlug)
    {
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

        return new
        {
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
    }

    /// <summary>Bare <c>BalanceResponseDTO</c> — {Balance, CurrencyType,
    /// Platform} and nothing else, per the generated contract at
    /// <c>RecNet.Runtime/PECGEJAAMHB.txt:255/274/298</c>. BalanceType is kept
    /// alongside Platform for the 2020.12 watch, which reads that spelling.
    /// </summary>
    private static object BalanceResponse(long balance, int currencyType) => new
    {
        Balance = balance,
        CurrencyType = currencyType,
        Platform = 0,
        BalanceType = 0,
    };

    /// <summary><c>BalanceUpdateResponseDTO`1&lt;PurchasedProgressionEventXpBoostDTO&gt;</c>.
    /// Data is a SINGLE object here, not a list — see the BuyProgressionEventXpBoost
    /// remarks for the ISIL citation.</summary>
    private static object XpBoostResponse(long balance, int currencyType, int xp) => new
    {
        Balance = balance,
        CurrencyType = currencyType,
        Platform = 0,
        BalanceType = 0,
        BalanceUpdates = new[]
        {
            new
            {
                UpdateResponse = 0, // UpdateResponseTypes.Success
                Data = new { Xp = xp },
            },
        },
    };

    /// <summary><c>JNPGKDJJPPM</c> — the buyPurchaseReminder response.
    /// {Balance, CurrencyType, BalanceType, Data:List&lt;GiftPackage&gt;}
    /// (<c>RecNet.Runtime/OLIHBGPDPHF.txt:319/338/362/386</c>). "Platform" is
    /// emitted as an inert alias because this one DTO is the odd one out that
    /// really does use "BalanceType".</summary>
    private static object PurchaseReminderResponse(long balance, int currencyType,
        string? grantedSlug, long? giftPackageId) => new
    {
        Balance = balance,
        CurrencyType = currencyType,
        BalanceType = 0,
        Platform = 0,
        Data = grantedSlug is null
            ? Array.Empty<object>()
            : new[] { GiftPackageWire(giftPackageId ?? DateTime.UtcNow.Ticks, currencyType, grantedSlug) },
    };

    // InventionResponse.Status values, from the name-preserved enum
    // ENOCILAAKGN in the 2023.06.21 dump
    // (Recnet-old/dist/RecRoom-2023.06.21-steam/il2cppdump/dump.cs:1268793-1268840).
    private const int InventionStatusSuccess = 0;
    private const int InventionStatusInvalidParameters = 1;
    private const int InventionStatusDoesNotExist = 7;
    private const int InventionStatusPurchaseFailed = 30;

    private static object InventionPurchaseResponse(int status,
        InventionEntity? inv, InventionVersionEntity? version,
        long balance, int currencyType)
    {
        object? inventionWire = inv is null ? null : InventionWire(inv);
        object? versionWire = version is null ? null : InventionVersionWire(version);
        return new
        {
            InventionResponse = new
            {
                Status = status,
                Invention = inventionWire,
                InventionVersion = versionWire,
            },
            // InventionPurchaseResponseDTO/EPFIAEMPKJK is
            // BalanceUpdateResponseDTO`1<Invention> — it extends
            // BalanceResponseDTO (BalanceUpdates at +32) and its inner Data is
            // an Invention, not a balance delta, so the list is left empty and
            // the client takes the balance off the outer trio.
            BalanceUpdateResponse = new
            {
                Balance = balance,
                CurrencyType = currencyType,
                Platform = 0,
                BalanceType = 0,
                BalanceUpdates = Array.Empty<object>(),
            },
        };
    }

    /// <summary>Local copy of InventionsController's invention wire shape —
    /// that builder is private to its own controller, and the buyInvention
    /// response has to carry the same object. Keep the two in sync; the field
    /// list is documented there (RecNet.Runtime/IOEPPCKGBFL.txt:1447-1939 is the
    /// 2023-03-21 key table).</summary>
    private static object InventionWire(InventionEntity i) => new
    {
        InventionId = i.Id,
        ReplicationId = string.IsNullOrEmpty(i.ReplicationId)
            ? Guid.Empty.ToString("D") : i.ReplicationId,
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

    /// <summary>Local copy of InventionsController's version wire shape.
    /// The 2023-03-21 reader also looks for ChipsCost, CloudVariablesCost and
    /// BlobHash (RecNet.Runtime/OLPHKLCPFEF.txt:786/810/858); none has a column
    /// on InventionVersionEntity, so they are omitted rather than emitted as
    /// permanent zeros/nulls.</summary>
    private static object InventionVersionWire(InventionVersionEntity v) => new
    {
        v.InventionId,
        ReplicationId = string.IsNullOrEmpty(v.ReplicationId)
            ? Guid.Empty.ToString("D") : v.ReplicationId,
        v.VersionNumber,
        v.InstantiationCost,
        v.LightsCost,
        v.BlobName,
    };

    private static object RoomKeyPurchaseResponse(RoomKeyStatus status, RoomKeyEntity? key, long balance) => new
    {
        RoomKeyResponse = RoomKeysController.RoomKeyResponse(status, key),
        BalanceUpdateResponse = new
        {
            Balance = balance,
            CurrencyType = 2,
            Platform = 0,
            BalanceType = 0,
            BalanceUpdates = Array.Empty<object>(),
        },
    };

    /// <summary><c>RoomKeyPurchaseWithCurrencyResponseDTO</c> — {Balance,
    /// RoomKeyResponse} where Balance is the ROOM-currency balance row
    /// (<c>RecNet.Runtime/EDMKBBPGJAB.txt:203/222</c>, member shape at
    /// <c>DPNLANKOHON.txt:331-414</c>), matching what
    /// RoomCurrenciesController already emits for the same row.</summary>
    private static object RoomKeyCurrencyPurchaseResponse(
        RoomKeyStatus status, RoomKeyEntity? key, long playerId, Guid currencyId,
        RoomCurrencyBalanceEntity? row) => new
    {
        Balance = new
        {
            AccountId = (int)playerId,
            CurrencyId = currencyId,
            Balance = row?.Balance ?? 0L,
            ModifiedAt = row is null
                ? DateTime.UtcNow
                : DateTime.SpecifyKind(row.UpdatedAt, DateTimeKind.Utc),
        },
        RoomKeyResponse = RoomKeysController.RoomKeyResponse(status, key),
    };

    /// <summary>Grants a purchased StoreItems row. Wardrobe unlocks are a set,
    /// so a multi-count press still yields one entry; everything else stacks in
    /// PlayerInventory.</summary>
    private async Task GrantPurchasedItemAsync(long pid, StoreItemEntity item, int count = 1)
    {
        if (StoreService.TryGetAvatarItemPayload(item.Slug, out _, out var avatarItemDesc))
        {
            await GrantWardrobeAsync(pid, StoreService.InventoryAvatarItemDesc(avatarItemDesc));
            return;
        }
        for (var n = 0; n < Math.Max(1, count); n++)
            await GrantInventoryAsync(pid, item.Slug);
    }

    /// <summary>Grants <paramref name="item"/> and inserts the matching
    /// gift-box row, mirroring BuyItem: the watch pops a gift box after the
    /// purchase and POSTs the package id to /api/avatar/v2/gifts/consume/, so
    /// the id in the response has to be a real GiftPackageEntity.</summary>
    private async Task<long> GrantWithGiftBoxAsync(long pid, StoreItemEntity item, int currencyType)
    {
        await GrantPurchasedItemAsync(pid, item);

        StoreService.TryGetAvatarItemPayload(item.Slug, out var avatarItemType, out var avatarItemDesc);
        StoreService.TryGetConsumableItemDesc(item.Slug, out var consumableItemDesc);
        StoreService.TryGetEquipmentPayload(item.Slug, out var equipmentPrefabName, out var equipmentModificationGuid);
        var inventoryAvatarDesc = StoreService.InventoryAvatarItemDesc(avatarItemDesc);
        if (StoreService.TryGetHairDyePayload(item.Slug, out var hairDyeConsumableDesc, out var hairDyeColorGuid))
        {
            avatarItemType = 1;
            inventoryAvatarDesc = hairDyeColorGuid;
            consumableItemDesc = hairDyeConsumableDesc;
        }
        // Same 4-part "{guid},,," desc BuyItem ships — the bare guid makes the
        // client's AvatarItem parser miss on unlock.
        var giftWireDesc = string.IsNullOrEmpty(avatarItemDesc) ? inventoryAvatarDesc : avatarItemDesc;

        var gift = new GiftPackageEntity
        {
            RecipientPlayerId           = pid,
            AvatarItemType              = string.IsNullOrEmpty(giftWireDesc) ? null : avatarItemType,
            AvatarItemDescOrHairDyeDesc = giftWireDesc,
            ConsumableItemDesc          = consumableItemDesc,
            EquipmentPrefabName         = equipmentPrefabName,
            EquipmentModificationGuid   = equipmentModificationGuid,
            CurrencyType                = currencyType,
            Currency                    = 0,
            Xp                          = 0,
            Level                       = 1,
            GiftContext                 = 0,
            GiftRarity                  = 0,
            Message                     = string.Empty,
            Platform                    = -1,
            PackageVariant              = "Standard",
            PackageMaterial             = string.Empty,
            Consumed                    = false,
            IsValid                     = true,
            SupportsCurrentPlatform     = true,
        };
        db.GiftPackages.Add(gift);
        await db.SaveChangesAsync();
        return gift.Id;
    }

    /// <summary>Idempotently records a season-pass purchase. There is no
    /// SeasonProgressEntity, so tier/elite state lives on ObjectiveProgress
    /// under <c>seasonTier:{storefrontType}:{purchasableItemId}</c> and
    /// <c>seasonElite:{storefrontType}</c> — the same table the season
    /// objectives feed already uses.</summary>
    private async Task RecordSeasonProgressAsync(long pid, string key)
    {
        if (await db.ObjectiveProgress.AnyAsync(o => o.PlayerId == pid && o.Key == key)) return;
        db.ObjectiveProgress.Add(new ObjectiveProgressEntity
        {
            PlayerId = pid,
            Key = key,
            IsCompleted = true,
            ClearedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }
}
