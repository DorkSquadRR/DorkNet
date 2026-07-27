using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.RoomConsumables;

/// <summary>
/// api.rec.net/api/roomconsumables/v1 — the 2023 client's in-room shop
/// (creator-made consumables, <c>RecRoom.Systems.RoomConsumablesManager</c>).
///
/// Wire shapes verified against the 2023.03.21 ISIL dump
/// (RecNet.Runtime formatters CICEHLNDLDE / HJPEABLEEKL / JEBCHNDADKK /
/// LPFAFIGAIOE / HJOKFJKKEJM / IDBFKCFEAHM / PECGEJAAMHB / DPNLANKOHON):
///
///   desc      = { RoomConsumableId, RoomId, Name, Description, ImageName,
///                 PriceAndCurrency: { Price, CurrencyId } }
///   inventory = { RoomConsumableId, AccountId, Count, ConcurrencyCode,
///                 ModifiedAt, Consumable: desc }
///
/// <c>Consumable</c> must never be null on an inventory row — the
/// client's RoomConsumablesManager dereferences it unconditionally while
/// processing the room-join inventory fetch and NREs otherwise (that NRE
/// is an unobserved task exception that kills the shop and uploads a
/// crash report).
///
/// Purchase requests carry the client-generated inventory concurrency
/// token: { ConcurrencyCodes: { CurrentConcurrencyCode,
/// NewConcurrencyCode }, ExpectedPriceAndCurrency: { Price, CurrencyId } }.
/// The server must store NewConcurrencyCode verbatim, because the client
/// adopts the code it sent regardless of what comes back — anything else
/// makes the next consume fail with ConcurrencyCodeMismatch.
///
/// Both purchase responses derive from the same base DTO (OKHNOPLBOFP),
/// whose sole field <c>OperationResult</c> is the object
/// <c>{ Status, InventoryItem }</c> — see TokenResponse below.
/// </summary>
[ApiController]
public class RoomConsumablesController(DorkNetDbContext db, LevelService level) : ControllerBase
{
    /// <summary>RoomConsumableStatus (client enum DLDNGEDFMOJ).</summary>
    private static class Status
    {
        public const int Success = 0;
        public const int RoomConsumableNotFound = 4;
        public const int PlayerDoesntHavePermission = 7;
        public const int MaxConsumablesInRoom = 13;
        public const int RoomIdMissing = 15;
        public const int PriceOrCurrencyMissing = 17;
        public const int CurrencyNotFound = 18;
        public const int PriceTooLowTokens = 21;
        public const int PriceTooHighTokens = 22;
        public const int NameTooShort = 23;
        public const int NameTooLong = 24;
        public const int DescriptionTooLong = 27;
        public const int ConcurrencyCodeMismatch = 32;
        public const int PlayerDoesNotOwnConsumable = 33;
        public const int PurchaseFailed = 35;
        public const int RoomNotFound = 36;
        public const int RequestedPriceDoesNotMatch = 38;
        public const int RequestedCurrencyDoesNotMatch = 39;
        public const int ConsumableCannotBePurchasedWithRoomCurrency = 40;
        public const int ConsumableCannotBePurchasedWithTokens = 41;
    }

    /// <summary>Token-purchase OperationResult (client enum CABBDKFODEC).</summary>
    private static class TokenOp
    {
        public const int OK = 0;
        public const int NotEnoughCredit = 2;
        public const int RequestedPriceDoesNotMatch = 6;
    }

    /// <summary>Currency-purchase OperationResult (client enum GACBLALELBP).</summary>
    private static class CurrencyOp
    {
        public const int Success = 0;
        public const int NotEnoughCredit = 1;
    }

    private const int TokenCurrencyType = 2; // internal RecCenterTokens row
    private const int MaxConsumablesPerRoom = 50;

    /// <summary>GET <c>api/roomconsumables/v1/roomConsumable/room/{roomId}</c>
    /// — every consumable for sale in a room; the shop UI's catalog.</summary>
    [HttpGet("api/roomconsumables/v1/roomConsumable/room/{roomId:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> ListForRoom(long roomId)
    {
        var rows = await db.RoomConsumables
            .Where(c => c.RoomId == roomId && !c.IsDeleted)
            .OrderBy(c => c.CreatedAt)
            .Take(200)
            .ToListAsync();
        return Ok(rows.Select(ToDescWire));
    }

    /// <summary>GET <c>api/roomconsumables/v1/roomConsumable/room/{roomId}/me</c>
    /// — the caller's consumable inventory for a room, fetched on every
    /// room join.</summary>
    [HttpGet("api/roomconsumables/v1/roomConsumable/room/{roomId:long}/me")]
    [Authorize]
    public async Task<IActionResult> MineForRoom(long roomId)
    {
        var me = this.RequireCurrentPlayerId();
        var rows = await (from own in db.RoomConsumableOwnership
                          join item in db.RoomConsumables on own.RoomConsumableId equals item.Id
                          where own.PlayerId == me && item.RoomId == roomId && !item.IsDeleted
                          orderby item.CreatedAt
                          select new { own, item })
            .Take(200)
            .ToListAsync();
        return Ok(rows.Select(r => ToInventoryWire(r.own, r.item)));
    }

    /// <summary>GET <c>api/roomconsumables/v1/roomConsumable/{id}</c> —
    /// single consumable desc.</summary>
    [HttpGet("api/roomconsumables/v1/roomConsumable/{publicId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetOne(Guid publicId)
    {
        var item = await FindAsync(publicId);
        return item is null ? NotFound() : Ok(ToDescWire(item));
    }

    /// <summary>GET <c>api/roomconsumables/v1/roomConsumable/{id}/isOwned</c>
    /// — "do players other than the creator hold this in inventory";
    /// the client asks before edits/deletes to warn about invalidating
    /// sold stock. Bare boolean body.</summary>
    [HttpGet("api/roomconsumables/v1/roomConsumable/{publicId:guid}/isOwned")]
    [Authorize]
    public async Task<IActionResult> IsOwned(Guid publicId)
    {
        var item = await FindAsync(publicId);
        if (item is null) return Content("false", "application/json");
        var owned = await db.RoomConsumableOwnership.AnyAsync(o =>
            o.RoomConsumableId == item.Id && o.Count > 0 && o.PlayerId != item.CreatorPlayerId);
        return Content(owned ? "true" : "false", "application/json");
    }

    /// <summary>POST/PUT <c>api/roomconsumables/v1/roomConsumable</c> —
    /// create (no <c>RoomConsumableId</c> in the body) or update (id
    /// present) a consumable. Body is the desc DTO itself; response is
    /// <c>{ Status, Consumable }</c>.</summary>
    [HttpPost("api/roomconsumables/v1/roomConsumable")]
    [HttpPut("api/roomconsumables/v1/roomConsumable")]
    [Authorize]
    public async Task<IActionResult> CreateOrUpdate()
    {
        var me = this.RequireCurrentPlayerId();
        var body = await ReadBodyAsync();
        var name = (ReadString(body, "Name") ?? string.Empty).Trim();
        var description = (ReadString(body, "Description") ?? string.Empty).Trim();
        var imageName = (ReadString(body, "ImageName") ?? string.Empty).Trim();
        var (price, currencyId, hasPrice) = ReadPriceAndCurrency(body);

        var publicId = ReadGuid(body, "RoomConsumableId");
        var existing = publicId is Guid id && id != Guid.Empty ? await FindAsync(id) : null;

        if (existing is null)
        {
            var roomId = ReadLong(body, "RoomId");
            if (roomId is not long rid || rid <= 0) return Ok(EditResponse(Status.RoomIdMissing));
            var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == rid);
            if (room is null) return Ok(EditResponse(Status.RoomNotFound));
            if (!await CanManageRoomAsync(room, me)) return Ok(EditResponse(Status.PlayerDoesntHavePermission));

            if (ValidateFields(name, description, hasPrice, price) is int fieldError)
                return Ok(EditResponse(fieldError));
            if (currencyId is Guid cur
                && !await db.RoomCurrencies.AnyAsync(c => c.PublicId == cur && c.RoomId == rid && !c.IsDeleted))
                return Ok(EditResponse(Status.CurrencyNotFound));
            if (await db.RoomConsumables.CountAsync(c => c.RoomId == rid && !c.IsDeleted) >= MaxConsumablesPerRoom)
                return Ok(EditResponse(Status.MaxConsumablesInRoom));

            var created = new RoomConsumableEntity
            {
                RoomId = rid,
                CreatorPlayerId = me,
                Name = name,
                Description = description,
                ImageName = imageName,
                Price = price,
                CurrencyId = currencyId,
            };
            db.RoomConsumables.Add(created);
            await db.SaveChangesAsync();
            return Ok(EditResponse(Status.Success, created));
        }

        var owningRoom = await db.Rooms.FirstOrDefaultAsync(r => r.Id == existing.RoomId);
        if (owningRoom is null || !await CanManageRoomAsync(owningRoom, me))
            return Ok(EditResponse(Status.PlayerDoesntHavePermission, existing));

        if (ValidateFields(name, description, hasPrice, price) is int updateError)
            return Ok(EditResponse(updateError, existing));
        if (currencyId is Guid updateCur
            && !await db.RoomCurrencies.AnyAsync(c => c.PublicId == updateCur && c.RoomId == existing.RoomId && !c.IsDeleted))
            return Ok(EditResponse(Status.CurrencyNotFound, existing));

        existing.Name = name;
        existing.Description = description;
        existing.ImageName = imageName;
        existing.Price = price;
        existing.CurrencyId = currencyId;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(EditResponse(Status.Success, existing));
    }

    /// <summary>DELETE <c>api/roomconsumables/v1/roomConsumable/{id}</c> —
    /// soft-delete. The client deserialises the response body as a bare
    /// RoomConsumableStatus int.</summary>
    [HttpDelete("api/roomconsumables/v1/roomConsumable/{publicId:guid}")]
    [HttpPost("api/roomconsumables/v1/roomConsumable/{publicId:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid publicId)
    {
        var me = this.RequireCurrentPlayerId();
        var item = await FindAsync(publicId);
        if (item is null) return StatusBody(Status.RoomConsumableNotFound);
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == item.RoomId);
        if (room is null || !await CanManageRoomAsync(room, me))
            return StatusBody(Status.PlayerDoesntHavePermission);

        item.IsDeleted = true;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return StatusBody(Status.Success);
    }

    /// <summary>PUT <c>api/roomconsumables/v1/roomconsumable/{id}/purchase/tokens</c>
    /// — buy one stack unit with tokens. Response is
    /// <c>{ OperationResult: { Status, InventoryItem }, BalanceUpdateResult,
    /// TokenBalanceResponse: { CurrencyType, Balance, Platform } }</c>.</summary>
    // The 2023-03-21 client PUTs this (verb literal 3 = PUT, at the route
    // construction in RecNet.Runtime/DCFKEFHJAGC.txt instrs 097-108), so a
    // POST-only mapping returned 405 and token-priced consumables were
    // unbuyable. Both verbs are accepted now.
    [HttpPut("api/roomconsumables/v1/roomconsumable/{publicId:guid}/purchase/tokens")]
    [HttpPost("api/roomconsumables/v1/roomconsumable/{publicId:guid}/purchase/tokens")]
    [Authorize]
    public async Task<IActionResult> PurchaseTokens(Guid publicId)
    {
        var me = this.RequireCurrentPlayerId();
        var item = await FindAsync(publicId);
        // Failures answer 200 + the DTO rather than a bare status code: the
        // client only ever parses this route's body through HJOKFJKKEJM, so a
        // 404/400 surfaces as the opaque "Failed to purchase room consumable".
        if (item is null) return Ok(TokenResponse(null, Status.RoomConsumableNotFound, 0));

        var body = await ReadBodyAsync();
        var (price, _, hasPrice) = ReadPriceAndCurrency(body, "ExpectedPriceAndCurrency");
        var newCode = ReadConcurrency(body).NewCode;
        var balance = await level.GetBalanceAsync(me, TokenCurrencyType);

        // A token purchase is only valid for a token-priced item at the
        // price the client showed the buyer.
        if (item.CurrencyId is not null)
            return Ok(TokenResponse(null, Status.ConsumableCannotBePurchasedWithTokens, balance));
        if (!hasPrice || price != item.Price)
            return Ok(TokenResponse(
                TokenOp.RequestedPriceDoesNotMatch, Status.RequestedPriceDoesNotMatch, balance));
        if (balance < item.Price)
            return Ok(TokenResponse(TokenOp.NotEnoughCredit, Status.PurchaseFailed, balance));

        var newBalance = item.Price == 0
            ? balance
            : await level.GrantCurrencyAsync(me, TokenCurrencyType, -item.Price, $"roomConsumable:{item.PublicId}");
        if (item.Price > 0 && item.CreatorPlayerId != me)
            await level.GrantCurrencyAsync(item.CreatorPlayerId, TokenCurrencyType, item.Price,
                $"sellRoomConsumable:{item.PublicId}");

        var own = await AddToInventoryAsync(me, item, newCode);
        await db.SaveChangesAsync();
        return Ok(TokenResponse(TokenOp.OK, Status.Success, newBalance, ToInventoryWire(own, item)));
    }

    /// <summary>PUT <c>api/roomconsumables/v1/roomconsumable/{id}/purchase/currency</c>
    /// — buy with a room currency. Response is
    /// <c>{ OperationResult: { Status, InventoryItem }, BalanceUpdateResult,
    /// CurrencyBalanceResponse: { AccountId, CurrencyId, Balance,
    /// ModifiedAt } }</c>.</summary>
    // PUT per the client (verb literal 3 at DCFKEFHJAGC.txt instrs 112-123);
    // POST-only returned 405 and currency-priced consumables were unbuyable.
    [HttpPut("api/roomconsumables/v1/roomconsumable/{publicId:guid}/purchase/currency")]
    [HttpPost("api/roomconsumables/v1/roomconsumable/{publicId:guid}/purchase/currency")]
    [Authorize]
    public async Task<IActionResult> PurchaseCurrency(Guid publicId)
    {
        var me = this.RequireCurrentPlayerId();
        var item = await FindAsync(publicId);
        // As with the token route, every rejection is a 200 + DTO carrying a
        // real RoomConsumableStatus; the old BadRequest("price_mismatch")
        // bodies were not DTOs at all, so the client could only report the
        // generic purchase failure.
        if (item is null)
            return Ok(CurrencyResponse(null, Status.RoomConsumableNotFound, me, Guid.Empty, 0));
        if (item.CurrencyId is not Guid currencyPublicId)
            return Ok(CurrencyResponse(
                null, Status.ConsumableCannotBePurchasedWithRoomCurrency, me, Guid.Empty, 0));

        var currency = await db.RoomCurrencies
            .FirstOrDefaultAsync(c => c.PublicId == currencyPublicId && !c.IsDeleted);
        if (currency is null)
            return Ok(CurrencyResponse(null, Status.CurrencyNotFound, me, currencyPublicId, 0));

        var body = await ReadBodyAsync();
        var (price, requestedCurrency, hasPrice) = ReadPriceAndCurrency(body, "ExpectedPriceAndCurrency");
        var newCode = ReadConcurrency(body).NewCode;

        var balanceRow = await db.RoomCurrencyBalances
            .FirstOrDefaultAsync(b => b.PlayerId == me && b.RoomCurrencyId == currency.Id);
        var balance = balanceRow?.Balance ?? 0;

        if (!hasPrice || price != item.Price)
            return Ok(CurrencyResponse(
                null, Status.RequestedPriceDoesNotMatch, me, currencyPublicId, balance));
        if (requestedCurrency is Guid reqCur && reqCur != currencyPublicId)
            return Ok(CurrencyResponse(
                null, Status.RequestedCurrencyDoesNotMatch, me, currencyPublicId, balance));
        if (balance < item.Price)
            return Ok(CurrencyResponse(
                CurrencyOp.NotEnoughCredit, Status.PurchaseFailed, me, currencyPublicId, balance));

        if (item.Price > 0)
        {
            balanceRow!.Balance -= item.Price;
            balanceRow.UpdatedAt = DateTime.UtcNow;
        }
        var own = await AddToInventoryAsync(me, item, newCode);
        await db.SaveChangesAsync();
        return Ok(CurrencyResponse(CurrencyOp.Success, Status.Success, me, currencyPublicId,
            balanceRow?.Balance ?? 0, ToInventoryWire(own, item)));
    }

    /// <summary>PUT <c>api/roomconsumables/v1/roomConsumable/{id}/consume</c>
    /// — spend one unit. Body is <c>{ CurrentConcurrencyCode,
    /// NewConcurrencyCode }</c>; response <c>{ Status, InventoryItem }</c>.
    /// On a code mismatch the current row is returned so the client can
    /// resync.</summary>
    // PUT per the client (route format "{0}/v1/roomConsumable/{1}/consume"
    // with verb literal 3 at RecNet.Runtime/LPNHMEFDAAG.txt:1449-1460);
    // POST-only returned 405, so no consumable could ever be consumed.
    [HttpPut("api/roomconsumables/v1/roomConsumable/{publicId:guid}/consume")]
    [HttpPost("api/roomconsumables/v1/roomConsumable/{publicId:guid}/consume")]
    [Authorize]
    public async Task<IActionResult> Consume(Guid publicId)
    {
        var me = this.RequireCurrentPlayerId();
        var item = await FindAsync(publicId);
        if (item is null) return Ok(new { Status = Status.RoomConsumableNotFound, InventoryItem = (object?)null });

        var own = await db.RoomConsumableOwnership
            .FirstOrDefaultAsync(o => o.PlayerId == me && o.RoomConsumableId == item.Id);
        if (own is null || own.Count <= 0)
            return Ok(new
            {
                Status = Status.PlayerDoesNotOwnConsumable,
                InventoryItem = own is null ? null : ToInventoryWire(own, item),
            });

        var (currentCode, newCode) = ReadConcurrency(await ReadBodyAsync());
        if (currentCode is Guid current && current != own.ConcurrencyCode)
            return Ok(new { Status = Status.ConcurrencyCodeMismatch, InventoryItem = ToInventoryWire(own, item) });

        own.Count -= 1;
        own.ConcurrencyCode = newCode ?? Guid.NewGuid();
        own.ModifiedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { Status = Status.Success, InventoryItem = ToInventoryWire(own, item) });
    }

    private async Task<RoomConsumableEntity?> FindAsync(Guid publicId) =>
        await db.RoomConsumables.FirstOrDefaultAsync(c => c.PublicId == publicId && !c.IsDeleted);

    /// <summary>Grant one unit and hand back the row, so the purchase
    /// responses can embed it as <c>OperationResult.InventoryItem</c>.</summary>
    private async Task<RoomConsumableOwnershipEntity> AddToInventoryAsync(
        long playerId, RoomConsumableEntity item, Guid? newCode)
    {
        var own = await db.RoomConsumableOwnership
            .FirstOrDefaultAsync(o => o.PlayerId == playerId && o.RoomConsumableId == item.Id);
        if (own is null)
        {
            own = new RoomConsumableOwnershipEntity { PlayerId = playerId, RoomConsumableId = item.Id };
            db.RoomConsumableOwnership.Add(own);
        }
        own.Count += 1;
        // The client adopts the NewConcurrencyCode it sent regardless of what
        // the response echoes back — store it verbatim or the next consume
        // hits ConcurrencyCodeMismatch.
        if (newCode is Guid code && code != Guid.Empty) own.ConcurrencyCode = code;
        own.ModifiedAt = DateTime.UtcNow;
        return own;
    }

    private async Task<bool> CanManageRoomAsync(RoomEntity room, long playerId)
    {
        if (room.CreatorPlayerId == playerId) return true;
        if (await db.RoomRoles.AnyAsync(r => r.RoomId == room.Id && r.PlayerId == playerId && r.Role == 0 && r.Accepted))
            return true;
        return await db.Players.Where(p => p.Id == playerId).Select(p => p.IsAdmin).FirstOrDefaultAsync();
    }

    private static int? ValidateFields(string name, string description, bool hasPrice, long price)
    {
        if (name.Length == 0) return Status.NameTooShort;
        if (name.Length > 128) return Status.NameTooLong;
        if (description.Length > 1024) return Status.DescriptionTooLong;
        if (!hasPrice) return Status.PriceOrCurrencyMissing;
        if (price < 0) return Status.PriceTooLowTokens;
        if (price > 1_000_000) return Status.PriceTooHighTokens;
        return null;
    }

    private IActionResult StatusBody(int status) => Content(status.ToString(), "application/json");

    private static object EditResponse(int status, RoomConsumableEntity? item = null) => new
    {
        Status = status,
        Consumable = item is null ? null : ToDescWire(item),
    };

    /// <summary>Token-purchase response (client type MJKFEHPIDME, formatter
    /// HJOKFJKKEJM).
    ///
    /// <c>OperationResult</c> is an OBJECT, not a scalar. MJKFEHPIDME only
    /// declares BalanceUpdateResult (+24) and TokenBalanceResponse (+32)
    /// itself (MJKFEHPIDME.txt:3,23); OperationResult (+16) comes from its
    /// base OKHNOPLBOFP, whose single field is typed BGHBAILNNJJ
    /// (OKHNOPLBOFP.txt:3) — the same <c>{ Status, InventoryItem }</c> pair
    /// the consume route returns (BGHBAILNNJJ.txt:3,83). HJOKFJKKEJM's key
    /// map puts "OperationResult" at index 2 (HJOKFJKKEJM.txt:330) and its
    /// reader resolves index 2 through JECENNBIMEI&lt;BGHBAILNNJJ&gt;
    /// (HJOKFJKKEJM.txt:1104). A bare int there made the generated reader
    /// hit a number where it verifies '{', faulting the whole request with
    /// "Failed to purchase room consumable" (DCFKEFHJAGC.txt:8294) — so
    /// purchases were dead even once the PUT verb was accepted.
    ///
    /// The scalar operation enum belongs in <c>BalanceUpdateResult</c>
    /// (Nullable&lt;CABBDKFODEC&gt;, index 0) — that is what the client turns
    /// into the "Not enough …" / "The price for this … has changed" toast
    /// (DCFKEFHJAGC.txt:12735-12760). It is null when the balance was never
    /// touched.</summary>
    private static object TokenResponse(
        int? balanceUpdateResult, int status, long balance, object? inventoryItem = null) => new
    {
        OperationResult = new { Status = status, InventoryItem = inventoryItem },
        BalanceUpdateResult = balanceUpdateResult,
        TokenBalanceResponse = new
        {
            CurrencyType = TokenCurrencyType,
            Balance = balance,
            Platform = 0,
        },
    };

    /// <summary>Currency-purchase response (client type DEILBLCDNEA,
    /// formatter IDBFKCFEAHM). Same base type as the token response, so
    /// <c>OperationResult</c> is the same <c>{ Status, InventoryItem }</c>
    /// object; index 2 resolves through JECENNBIMEI&lt;BGHBAILNNJJ&gt;
    /// (IDBFKCFEAHM.txt:330 key map, :1161 reader). BalanceUpdateResult is
    /// Nullable&lt;GACBLALELBP&gt; — an enum with only Success=0 and
    /// NotEnoughCredit=1, so every other failure has to be reported through
    /// OperationResult.Status and leave this null.</summary>
    private static object CurrencyResponse(
        int? balanceUpdateResult, int status, long playerId, Guid currencyId, long balance,
        object? inventoryItem = null) => new
    {
        OperationResult = new { Status = status, InventoryItem = inventoryItem },
        BalanceUpdateResult = balanceUpdateResult,
        CurrencyBalanceResponse = new
        {
            AccountId = (int)playerId,
            CurrencyId = currencyId,
            Balance = balance,
            ModifiedAt = DateTime.UtcNow,
        },
    };

    /// <summary>Consumable descriptor as the client READS it.
    ///
    /// The price is FLAT here — the client's response formatter FCIBLPCOODP
    /// reads <c>Price</c>, <c>PurchaseCurrencyId</c> and <c>ModifiedAt</c>
    /// directly off the descriptor (RecNet.Runtime/FCIBLPCOODP.txt:670-710).
    /// Emitting a nested <c>PriceAndCurrency</c> object left every item in
    /// every room shop reading back Price=0 with a null currency, so the whole
    /// catalogue looked token-priced and free.
    ///
    /// Note the asymmetry: REQUEST bodies really do nest PriceAndCurrency, and
    /// ReadBodyAsync below still flattens that on the way in. Only the response
    /// shape changed.</summary>
    private static object ToDescWire(RoomConsumableEntity c) => new
    {
        RoomConsumableId = c.PublicId,
        c.RoomId,
        c.Name,
        c.Description,
        c.ImageName,
        c.Price,
        PurchaseCurrencyId = c.CurrencyId,
        ModifiedAt = DateTime.SpecifyKind(c.UpdatedAt, DateTimeKind.Utc),
    };

    private static object ToInventoryWire(RoomConsumableOwnershipEntity own, RoomConsumableEntity item) => new
    {
        RoomConsumableId = item.PublicId,
        AccountId = (int)own.PlayerId,
        Count = Math.Max(0, own.Count),
        own.ConcurrencyCode,
        ModifiedAt = DateTime.SpecifyKind(own.ModifiedAt, DateTimeKind.Utc),
        Consumable = ToDescWire(item),
    };

    /// <summary>Flatten the JSON body one level: nested objects
    /// (<c>ExpectedPriceAndCurrency</c>, <c>ConcurrencyCodes</c>,
    /// <c>PriceAndCurrency</c>) contribute their inner keys, so readers
    /// can be shape-agnostic about the exact nesting.</summary>
    private async Task<Dictionary<string, JsonElement>> ReadBodyAsync()
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (Request.ContentLength is 0) return fields;
        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return fields;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                fields[prop.Name] = prop.Value.Clone();
                if (prop.Value.ValueKind == JsonValueKind.Object)
                    foreach (var inner in prop.Value.EnumerateObject())
                        fields.TryAdd(inner.Name, inner.Value.Clone());
            }
        }
        catch (JsonException)
        {
        }
        return fields;
    }

    private static string? ReadString(Dictionary<string, JsonElement> fields, string name) =>
        fields.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long? ReadLong(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var n) => n,
            JsonValueKind.String when long.TryParse(v.GetString(), out var n) => n,
            _ => null,
        };
    }

    private static Guid? ReadGuid(Dictionary<string, JsonElement> fields, string name) =>
        fields.TryGetValue(name, out var v) && v.ValueKind == JsonValueKind.String
        && Guid.TryParse(v.GetString(), out var guid)
            ? guid
            : null;

    /// <summary>Read <c>{ Price, CurrencyId }</c> from the body, whether it
    /// arrives nested under <paramref name="wrapper"/> /
    /// <c>PriceAndCurrency</c> or flattened at the root.</summary>
    private static (long Price, Guid? CurrencyId, bool HasPrice) ReadPriceAndCurrency(
        Dictionary<string, JsonElement> fields, string wrapper = "PriceAndCurrency")
    {
        var scope = fields;
        if (fields.TryGetValue(wrapper, out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            scope = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in nested.EnumerateObject()) scope[prop.Name] = prop.Value;
        }
        var price = ReadLong(scope, "Price");
        return (price ?? 0, ReadGuid(scope, "CurrencyId"), price is not null);
    }

    private static (Guid? CurrentCode, Guid? NewCode) ReadConcurrency(Dictionary<string, JsonElement> fields)
    {
        var scope = fields;
        if (fields.TryGetValue("ConcurrencyCodes", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            scope = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in nested.EnumerateObject()) scope[prop.Name] = prop.Value;
        }
        return (ReadGuid(scope, "CurrentConcurrencyCode"), ReadGuid(scope, "NewConcurrencyCode"));
    }
}
