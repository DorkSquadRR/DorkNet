using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.RoomCurrencies;

[ApiController]
[Authorize]
public class RoomCurrenciesController(DorkNetDbContext db, LevelService level) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    [HttpGet("api/roomcurrencies/v1/currencies")]
    [AllowAnonymous]
    public async Task<IActionResult> Currencies([FromQuery] long roomId)
    {
        var rows = await db.RoomCurrencies
            .Where(c => !c.IsDeleted && (roomId <= 0 || c.RoomId == roomId))
            .OrderBy(c => c.Name)
            .Take(100)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpPost("api/roomcurrencies/v1/createCurrency")]
    public async Task<IActionResult> CreateCurrency()
    {
        var fields = await ReadFieldsAsync();
        var roomId = ReadLong(fields, "roomId", "RoomId");
        if (roomId is not long rid || rid <= 0) return BadRequest("missing_room");
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == rid);
        if (room is null) return NotFound("room_not_found");
        if (!await CanManageRoomAsync(room, Me)) return Forbid();

        var name = Trim(ReadString(fields, "name", "Name") ?? "Room Currency", 64);
        var currency = new RoomCurrencyEntity
        {
            RoomId = room.Id,
            CreatorPlayerId = Me,
            Name = name,
            Description = Trim(ReadString(fields, "description", "Description"), 512),
            ImageName = Trim(ReadString(fields, "imageName", "ImageName"), 256),
            DailyLimit = Math.Max(0, ReadInt(fields, "limit", "Limit", "dailyLimit", "DailyLimit") ?? 0),
        };
        db.RoomCurrencies.Add(currency);
        await db.SaveChangesAsync();
        return Ok(ToWire(currency));
    }

    [HttpPost("api/roomcurrencies/v1/updateCurrency")]
    [HttpPut("api/roomcurrencies/v1/updateCurrency")]
    public async Task<IActionResult> UpdateCurrency()
    {
        var fields = await ReadFieldsAsync();
        var currency = await FindCurrencyAsync(fields);
        if (currency is null) return NotFound();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == currency.RoomId);
        if (room is null || !await CanManageRoomAsync(room, Me)) return Forbid();

        if (ReadString(fields, "name", "Name") is { } name) currency.Name = Trim(name, 64);
        if (ReadString(fields, "description", "Description") is { } description) currency.Description = Trim(description, 512);
        if (ReadString(fields, "imageName", "ImageName") is { } imageName) currency.ImageName = Trim(imageName, 256);
        if (ReadInt(fields, "limit", "Limit", "dailyLimit", "DailyLimit") is int dailyLimit) currency.DailyLimit = Math.Max(0, dailyLimit);
        currency.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(currency));
    }

    [HttpGet("api/roomcurrencies/v1/getBalance")]
    public async Task<IActionResult> GetBalance()
    {
        var fields = await ReadFieldsAsync();
        var currency = await FindCurrencyAsync(fields);
        if (currency is null) return NotFound();
        // The client sends "accountId" and can ask about ANOTHER player
        // (circuits read other players' balances); reading only playerId made
        // every such query silently answer for the caller instead.
        var playerId = ReadLong(fields, "accountId", "AccountId", "playerId", "PlayerId") ?? Me;
        var balance = await GetBalanceAsync(playerId, currency.Id);
        return Ok(new
        {
            AccountId = (int)playerId,
            CurrencyId = currency.PublicId,
            Balance = balance,
            ModifiedAt = DateTime.UtcNow,
            // legacy aliases
            PlayerId = (int)playerId,
            RoomCurrencyId = currency.PublicId,
        });
    }

    [HttpGet("api/roomcurrencies/v1/getAllBalances")]
    public async Task<IActionResult> GetAllBalances([FromQuery] long? roomId = null)
    {
        var playerId = Me;
        var query = db.RoomCurrencyBalances
            .Where(b => b.PlayerId == playerId)
            .Join(db.RoomCurrencies, b => b.RoomCurrencyId, c => c.Id, (b, c) => new { b, c })
            .Where(x => !x.c.IsDeleted);
        if (roomId is long rid && rid > 0)
            query = query.Where(x => x.c.RoomId == rid);

        var rows = await query
            .OrderBy(x => x.c.RoomId)
            .ThenBy(x => x.c.Name)
            .ToListAsync();
        return Ok(rows.Select(x => new
        {
            AccountId = (int)playerId,
            CurrencyId = x.c.PublicId,
            x.b.Balance,
            ModifiedAt = DateTime.SpecifyKind(x.b.UpdatedAt, DateTimeKind.Utc),
            // legacy aliases
            PlayerId = (int)playerId,
            RoomCurrencyId = x.c.PublicId,
        }));
    }

    [HttpPost("api/roomcurrencies/v1/awardCurrency/bulk")]
    public async Task<IActionResult> AwardCurrencyBulk()
    {
        var fields = await ReadFieldsAsync();
        var currency = await FindCurrencyAsync(fields);
        if (currency is null) return NotFound();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == currency.RoomId);
        if (room is null || !await CanManageRoomAsync(room, Me)) return Forbid();

        var playerIds = ReadLongList(fields, "playerIds", "PlayerIds", "accountIds", "AccountIds");
        var amount = ReadLong(fields, "amount", "Amount") ?? 0;
        if (playerIds.Count == 0 || amount == 0) return BadRequest("missing_awards");

        var results = new List<object>();
        foreach (var playerId in playerIds)
        {
            var balance = await AddBalanceAsync(playerId, currency.Id, amount);
            results.Add(new { PlayerId = (int)playerId, RoomCurrencyId = currency.PublicId, Balance = balance });
        }

        await db.SaveChangesAsync();
        return Ok(results);
    }

    [HttpPost("api/roomcurrencies/v1/createPurchaseOffer")]
    public async Task<IActionResult> CreatePurchaseOffer()
    {
        var fields = await ReadFieldsAsync();
        var currency = await FindCurrencyAsync(fields);
        if (currency is null) return NotFound();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == currency.RoomId);
        if (room is null || !await CanManageRoomAsync(room, Me)) return Forbid();

        var offer = new RoomCurrencyPurchaseOfferEntity
        {
            RoomCurrencyId = currency.Id,
            Name = Trim(ReadString(fields, "name", "Name") ?? currency.Name, 64),
            Amount = Math.Max(1, ReadInt(fields, "amount", "Amount") ?? 1),
            Price = Math.Max(0, ReadInt(fields, "price", "Price") ?? 0),
            CurrencyType = Math.Max(0, ReadInt(fields, "currencyType", "CurrencyType") ?? 2),
        };
        db.RoomCurrencyPurchaseOffers.Add(offer);
        await db.SaveChangesAsync();
        return Ok(ToOfferWire(offer, currency));
    }

    [HttpPost("api/roomcurrencies/v1/updatePurchaseOffer")]
    [HttpPut("api/roomcurrencies/v1/updatePurchaseOffer")]
    public async Task<IActionResult> UpdatePurchaseOffer()
    {
        var fields = await ReadFieldsAsync();
        var offer = await FindOfferAsync(fields);
        if (offer is null) return NotFound();
        var currency = await db.RoomCurrencies.FirstAsync(c => c.Id == offer.RoomCurrencyId);
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == currency.RoomId);
        if (room is null || !await CanManageRoomAsync(room, Me)) return Forbid();

        if (ReadString(fields, "name", "Name") is { } name) offer.Name = Trim(name, 64);
        if (ReadInt(fields, "amount", "Amount") is int amount) offer.Amount = Math.Max(1, amount);
        if (ReadInt(fields, "price", "Price") is int price) offer.Price = Math.Max(0, price);
        if (ReadInt(fields, "currencyType", "CurrencyType") is int currencyType) offer.CurrencyType = Math.Max(0, currencyType);
        offer.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToOfferWire(offer, currency));
    }

    [HttpPost("api/roomcurrencies/v1/deletePurchaseOffer")]
    [HttpDelete("api/roomcurrencies/v1/deletePurchaseOffer")]
    public async Task<IActionResult> DeletePurchaseOffer()
    {
        var fields = await ReadFieldsAsync();
        var offer = await FindOfferAsync(fields);
        if (offer is null) return NotFound();
        var currency = await db.RoomCurrencies.FirstAsync(c => c.Id == offer.RoomCurrencyId);
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == currency.RoomId);
        if (room is null || !await CanManageRoomAsync(room, Me)) return Forbid();

        offer.IsDeleted = true;
        offer.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToOfferWire(offer, currency));
    }

    [HttpPost("api/roomcurrencies/v1/getPurchaseOffersBatch")]
    [HttpGet("api/roomcurrencies/v1/getPurchaseOffersBatch")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPurchaseOffersBatch()
    {
        var fields = await ReadFieldsAsync();
        var offerIds = ReadGuidList(fields, "purchaseOfferIds", "PurchaseOfferIds", "offerIds", "OfferIds");
        var currencyIds = ReadGuidList(fields, "roomCurrencyIds", "RoomCurrencyIds", "currencyIds", "CurrencyIds");

        var query = db.RoomCurrencyPurchaseOffers
            .Where(o => !o.IsDeleted)
            .Join(db.RoomCurrencies, o => o.RoomCurrencyId, c => c.Id, (o, c) => new { o, c })
            .Where(x => !x.c.IsDeleted);
        if (offerIds.Count > 0) query = query.Where(x => offerIds.Contains(x.o.PublicId));
        if (currencyIds.Count > 0) query = query.Where(x => currencyIds.Contains(x.c.PublicId));

        var rows = await query.Take(200).ToListAsync();
        return Ok(rows.Select(x => ToOfferWire(x.o, x.c)));
    }

    [HttpPost("api/roomCurrencies/v2/purchase")]
    [HttpGet("api/roomCurrencies/v2/purchase")]
    public async Task<IActionResult> Purchase()
    {
        var fields = await ReadFieldsAsync();
        var offer = await FindOfferAsync(fields);
        if (offer is null) return NotFound();
        var currency = await db.RoomCurrencies.FirstAsync(c => c.Id == offer.RoomCurrencyId);
        var pid = Me;
        var balance = await level.GetBalanceAsync(pid, offer.CurrencyType);
        if (balance < offer.Price)
            return Ok(new { Success = false, Error = "insufficient_funds", Balance = balance, offer.CurrencyType });
        var newBalance = offer.Price == 0
            ? balance
            : await level.GrantCurrencyAsync(pid, offer.CurrencyType, -offer.Price, $"roomCurrency:{offer.PublicId}");
        var customBalance = await AddBalanceAsync(pid, currency.Id, offer.Amount);
        await db.SaveChangesAsync();
        return Ok(new
        {
            Success = true,
            Balance = newBalance,
            offer.CurrencyType,
            RoomCurrencyId = currency.PublicId,
            RoomCurrencyBalance = customBalance,
        });
    }

    private async Task<RoomCurrencyEntity?> FindCurrencyAsync(Dictionary<string, string> fields)
    {
        var guid = ReadGuid(fields, "roomCurrencyId", "RoomCurrencyId", "currencyId", "CurrencyId", "id", "Id");
        if (guid is Guid publicId)
            return await db.RoomCurrencies.FirstOrDefaultAsync(c => c.PublicId == publicId && !c.IsDeleted);
        var localId = ReadLong(fields, "roomCurrencyInternalId", "internalId");
        if (localId is long id)
            return await db.RoomCurrencies.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        return null;
    }

    private async Task<RoomCurrencyPurchaseOfferEntity?> FindOfferAsync(Dictionary<string, string> fields)
    {
        var guid = ReadGuid(fields, "purchaseOfferId", "PurchaseOfferId", "offerId", "OfferId", "id", "Id");
        if (guid is Guid publicId)
            return await db.RoomCurrencyPurchaseOffers.FirstOrDefaultAsync(o => o.PublicId == publicId && !o.IsDeleted);
        var localId = ReadLong(fields, "purchaseOfferInternalId", "internalId");
        if (localId is long id)
            return await db.RoomCurrencyPurchaseOffers.FirstOrDefaultAsync(o => o.Id == id && !o.IsDeleted);
        return null;
    }

    private async Task<long> GetBalanceAsync(long playerId, long currencyId) =>
        await db.RoomCurrencyBalances
            .Where(b => b.PlayerId == playerId && b.RoomCurrencyId == currencyId)
            .Select(b => b.Balance)
            .FirstOrDefaultAsync();

    private async Task<long> AddBalanceAsync(long playerId, long currencyId, long amount)
    {
        var row = await db.RoomCurrencyBalances
            .FirstOrDefaultAsync(b => b.PlayerId == playerId && b.RoomCurrencyId == currencyId);
        if (row is null)
        {
            row = new RoomCurrencyBalanceEntity { PlayerId = playerId, RoomCurrencyId = currencyId };
            db.RoomCurrencyBalances.Add(row);
        }

        row.Balance = Math.Max(0, row.Balance + amount);
        row.UpdatedAt = DateTime.UtcNow;
        return row.Balance;
    }

    private async Task<bool> CanManageRoomAsync(RoomEntity room, long playerId)
    {
        if (room.CreatorPlayerId == playerId) return true;
        if (await db.RoomRoles.AnyAsync(r => r.RoomId == room.Id && r.PlayerId == playerId && r.Role == 0 && r.Accepted))
            return true;
        return await db.Players.Where(p => p.Id == playerId).Select(p => p.IsAdmin).FirstOrDefaultAsync();
    }

    private async Task<Dictionary<string, string>> ReadFieldsAsync()
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Request.Query)
            fields[pair.Key] = pair.Value.FirstOrDefault() ?? string.Empty;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var pair in form)
                fields[pair.Key] = pair.Value.FirstOrDefault() ?? string.Empty;
        }
        else if ((Request.ContentLength ?? 0) > 0
                 && Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(Request.Body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        fields[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.Array => string.Join(",", prop.Value.EnumerateArray().Select(v => v.ToString())),
                            JsonValueKind.String => prop.Value.GetString() ?? string.Empty,
                            _ => prop.Value.GetRawText(),
                        };
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return fields;
    }

    private static string? ReadString(Dictionary<string, string> fields, params string[] names)
    {
        foreach (var name in names)
            if (fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        return null;
    }

    private static long? ReadLong(Dictionary<string, string> fields, params string[] names) =>
        long.TryParse(ReadString(fields, names), out var value) ? value : null;

    private static int? ReadInt(Dictionary<string, string> fields, params string[] names) =>
        int.TryParse(ReadString(fields, names), out var value) ? value : null;

    private static Guid? ReadGuid(Dictionary<string, string> fields, params string[] names) =>
        Guid.TryParse(ReadString(fields, names), out var value) ? value : null;

    private static List<long> ReadLongList(Dictionary<string, string> fields, params string[] names) =>
        names.SelectMany(name => fields.TryGetValue(name, out var value)
                ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : Array.Empty<string>())
            .Select(v => long.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .Take(200)
            .ToList();

    private static List<Guid> ReadGuidList(Dictionary<string, string> fields, params string[] names) =>
        names.SelectMany(name => fields.TryGetValue(name, out var value)
                ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : Array.Empty<string>())
            .Select(v => Guid.TryParse(v, out var id) ? id : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(200)
            .ToList();

    private static string Trim(string? value, int max)
    {
        var s = (value ?? string.Empty).Trim();
        return s.Length <= max ? s : s[..max];
    }

    /// <summary>Room currency as the client READS it (formatter PEOAOMGOMGC):
    /// <c>CurrencyId</c>, <c>RoomId</c>, <c>Name</c>, <c>Description</c>,
    /// <c>CurrencyType</c>, <c>Limit</c>, <c>ImageName</c>, <c>CreatedAt</c>,
    /// <c>ModifiedAt</c>.
    ///
    /// The previous names were server inventions (<c>RoomCurrencyId</c>,
    /// <c>DailyLimit</c>, <c>UpdatedAt</c>) that shared almost no keys with
    /// what the client reads, so every currency came back with an empty
    /// CurrencyId and a zero limit — the room currency HUD could not match a
    /// balance to a currency. The legacy aliases are kept alongside the correct
    /// names because unknown members are ignored client-side and other DorkNet
    /// surfaces (admin UI) still read them.</summary>
    private static object ToWire(RoomCurrencyEntity currency) => new
    {
        CurrencyId = currency.PublicId,
        currency.RoomId,
        currency.Name,
        currency.Description,
        CurrencyType = 300,
        Limit = (long)currency.DailyLimit,
        currency.ImageName,
        CreatedAt = DateTime.SpecifyKind(currency.CreatedAt, DateTimeKind.Utc),
        ModifiedAt = DateTime.SpecifyKind(currency.UpdatedAt, DateTimeKind.Utc),

        // legacy aliases
        RoomCurrencyId = currency.PublicId,
        Id = currency.PublicId,
        InternalId = currency.Id,
        CreatorPlayerId = (int)currency.CreatorPlayerId,
        currency.DailyLimit,
        currency.UpdatedAt,
    };

    /// <summary>Purchase offer as the client READS it (formatter AALDCAANEMM):
    /// <c>CurrencyPurchaseOfferId</c>, <c>CurrencyId</c>, <c>Name</c>,
    /// <c>CurrencyAmount</c>, <c>Price</c>, <c>CurrencyType</c>,
    /// <c>CreatedAt</c>, <c>ModifiedAt</c>. Legacy aliases retained as in
    /// <see cref="ToWire"/>.</summary>
    private static object ToOfferWire(RoomCurrencyPurchaseOfferEntity offer, RoomCurrencyEntity currency) => new
    {
        CurrencyPurchaseOfferId = offer.PublicId,
        CurrencyId = currency.PublicId,
        offer.Name,
        CurrencyAmount = offer.Amount,
        offer.Price,
        offer.CurrencyType,
        CreatedAt = DateTime.SpecifyKind(offer.CreatedAt, DateTimeKind.Utc),
        ModifiedAt = DateTime.SpecifyKind(offer.UpdatedAt, DateTimeKind.Utc),

        // legacy aliases
        PurchaseOfferId = offer.PublicId,
        RoomCurrencyPackageId = offer.PublicId,
        Id = offer.PublicId,
        InternalId = offer.Id,
        RoomCurrencyId = currency.PublicId,
        offer.Amount,
        offer.UpdatedAt,
    };
}
