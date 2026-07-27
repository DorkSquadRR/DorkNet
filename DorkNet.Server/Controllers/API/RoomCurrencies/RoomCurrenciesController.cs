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
            // "Limit" is an Int64 on the wire — the client boxes a System.Int64
            // immediately before pushing the param (MIHKAJFMIPE.txt:757-768).
            // ReadInt would fail to parse anything above int.MaxValue and quietly
            // store 0, so parse as long and clamp into the int column.
            DailyLimit = ClampToInt(ReadLong(fields, "limit", "Limit", "dailyLimit", "DailyLimit") ?? 0),
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
        // Int64 on the wire (MIHKAJFMIPE.txt:983 block, same boxing as createCurrency).
        if (ReadLong(fields, "limit", "Limit", "dailyLimit", "DailyLimit") is long dailyLimit) currency.DailyLimit = ClampToInt(dailyLimit);
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

    /// <summary>Bulk currency award — the CircuitsV2 "award currency" chip.
    ///
    /// The client body is a BARE JSON ARRAY, not an object: the issuing method is
    /// <c>FGLDKEJLAKB&lt;List&lt;KBPKAAIDCIJ&gt;&gt; FGBCMAINCLD(List&lt;KOEIABFLEPM&gt;)</c>
    /// (MIHKAJFMIPE.txt:1073); it POSTs (verb 2, MIHKAJFMIPE.txt:1262) to the route
    /// at MIHKAJFMIPE.txt:1260 and pushes the serialised list straight into the
    /// body via <c>BNDIAONDFFF.FJLLPHFOOJJ</c> (MIHKAJFMIPE.txt:1284). Every
    /// element carries its OWN CurrencyId/RecipientId/Amount/TransactionId
    /// (request formatter GFKKOADFJKC.txt:331-398) — this is NOT "one currency,
    /// one amount, many players", which is what the old handler assumed. The old
    /// handler also went through <see cref="ReadFieldsAsync"/>, which only walks
    /// an OBJECT root, so an array body produced zero fields and every award
    /// 404'd.
    ///
    /// A per-element failure is reported in-band rather than as an HTTP error so
    /// one bad row cannot void a whole batch — the response formatter
    /// (JCPFCOEEELK.txt:371-454) reads exactly AccountId/CurrencyId/Success/
    /// Error/Response, and the nested Response (GAGIJHJGFJD.txt:395-486) reads
    /// AccountId/CurrencyId/Balance/AmountAwarded/AwardedAt.</summary>
    [HttpPost("api/roomcurrencies/v1/awardCurrency/bulk")]
    [HttpPut("api/roomcurrencies/v1/awardCurrency/bulk")]
    public async Task<IActionResult> AwardCurrencyBulk()
    {
        // The body stream is single-read: pull the raw text once and hand it to
        // the field reader for the legacy fallback below.
        var body = await ReadJsonBodyAsync();
        var awards = ParseAwards(body);

        if (awards.Count == 0)
        {
            // Legacy DorkNet/admin shape: one currency + playerIds + a single amount.
            var fields = await ReadFieldsAsync(body);
            var legacyCurrency = await FindCurrencyAsync(fields);
            var legacyAmount = ReadLong(fields, "amount", "Amount") ?? 0;
            if (legacyCurrency is not null && legacyAmount != 0)
                foreach (var playerId in ReadLongList(fields, "playerIds", "PlayerIds", "accountIds", "AccountIds"))
                    awards.Add(new AwardRequest(Guid.Empty, playerId, legacyAmount, legacyCurrency.PublicId));
        }

        if (awards.Count == 0) return BadRequest("missing_awards");

        var results = new List<object>();
        var currencyCache = new Dictionary<Guid, RoomCurrencyEntity?>();
        var manageCache = new Dictionary<long, bool>();
        var awardedAt = DateTime.UtcNow;

        foreach (var award in awards)
        {
            if (!currencyCache.TryGetValue(award.CurrencyId, out var currency))
            {
                currency = await db.RoomCurrencies
                    .FirstOrDefaultAsync(c => c.PublicId == award.CurrencyId && !c.IsDeleted);
                currencyCache[award.CurrencyId] = currency;
            }

            if (currency is null)
            {
                results.Add(AwardFailure(award, "currency_not_found"));
                continue;
            }

            if (!manageCache.TryGetValue(currency.RoomId, out var canManage))
            {
                var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == currency.RoomId);
                canManage = room is not null && await CanManageRoomAsync(room, Me);
                manageCache[currency.RoomId] = canManage;
            }

            if (!canManage)
            {
                results.Add(AwardFailure(award, "forbidden"));
                continue;
            }

            var balance = await AddBalanceAsync(award.RecipientId, currency.Id, award.Amount);
            results.Add(new
            {
                AccountId = (int)award.RecipientId,
                CurrencyId = currency.PublicId,
                Success = true,
                Error = (string?)null,
                Response = new
                {
                    AccountId = (int)award.RecipientId,
                    CurrencyId = currency.PublicId,
                    Balance = balance,
                    AmountAwarded = award.Amount,
                    AwardedAt = awardedAt,
                },
            });
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
            // Amount/Price are Int64 on the wire (MIHKAJFMIPE.txt:2351-2374 boxes
            // System.Int64 for both) — parse long, clamp into the int columns.
            Amount = Math.Max(1, ClampToInt(ReadLong(fields, "amount", "Amount") ?? 1)),
            Price = ClampToInt(ReadLong(fields, "price", "Price") ?? 0),
            // The client never sends currencyType for an offer (its params are
            // CurrencyId/Name/Amount/Price/Order, MIHKAJFMIPE.txt:2336-2383); the
            // default 2 = tokens is what the v2/purchase path debits.
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
        // Int64 on the wire, as in createPurchaseOffer.
        if (ReadLong(fields, "amount", "Amount") is long amount) offer.Amount = Math.Max(1, ClampToInt(amount));
        if (ReadLong(fields, "price", "Price") is long price) offer.Price = ClampToInt(price);
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

    /// <summary>Both verbs are mapped on purpose: the client picks GET or POST at
    /// runtime from the id count (<c>ALHIJCJOLCB.JIECAFGCODK(ids, 100)</c>,
    /// MIHKAJFMIPE_NestedType_OBLJADOFIED.txt:210-212) — GET under 100 ids, POST
    /// at 100+.</summary>
    [HttpPost("api/roomcurrencies/v1/getPurchaseOffersBatch")]
    [HttpGet("api/roomcurrencies/v1/getPurchaseOffersBatch")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPurchaseOffersBatch()
    {
        var fields = await ReadFieldsAsync();
        var offerIds = ReadGuidList(fields, "purchaseOfferIds", "PurchaseOfferIds", "offerIds", "OfferIds");
        // The client calls the list param plainly "ids"
        // (MIHKAJFMIPE_NestedType_OBLJADOFIED.txt:228) and it holds ROOM CURRENCY
        // ids — the response is grouped per currency. Without this alias both
        // filters stayed empty and the handler dumped 200 arbitrary offers.
        var currencyIds = ReadGuidList(fields, "ids", "Ids", "roomCurrencyIds", "RoomCurrencyIds", "currencyIds", "CurrencyIds");

        var query = db.RoomCurrencyPurchaseOffers
            .Where(o => !o.IsDeleted)
            .Join(db.RoomCurrencies, o => o.RoomCurrencyId, c => c.Id, (o, c) => new { o, c })
            .Where(x => !x.c.IsDeleted);
        if (offerIds.Count > 0) query = query.Where(x => offerIds.Contains(x.o.PublicId));
        if (currencyIds.Count > 0) query = query.Where(x => currencyIds.Contains(x.c.PublicId));

        var rows = await query.Take(200).ToListAsync();

        // The response is GROUPED: List<{CurrencyId, PurchaseOffers}> (formatter
        // LKBGCOELLIN.txt:215-258 reads only those two keys). A flat offer list
        // deserialised into rows with CurrencyId=Guid.Empty and
        // PurchaseOffers=null, so the "This Room" pack list was always empty.
        var groups = rows
            .GroupBy(x => x.c.PublicId)
            .ToDictionary(g => g.Key, g => g.Select(x => ToOfferWire(x.o, x.c)).ToList());

        // Answer for every requested currency, including ones with no offers, so
        // the client can cache a definite "no packs" instead of retrying.
        var keys = currencyIds.Count > 0 ? currencyIds : groups.Keys.ToList();
        return Ok(keys.Select(id => new
        {
            CurrencyId = id,
            PurchaseOffers = groups.TryGetValue(id, out var offers) ? offers : new List<object>(),
        }));
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

        // The client states the price and amount it agreed to — the request pushes
        // PurchaseOfferId (Guid), RequestedAmount (Int64) and RequestedPrice
        // (Int64) in that order (DCFKEFHJAGC.txt:7810, :7822, :7834). Treat them
        // as a price agreement so an offer edited mid-purchase cannot overcharge.
        var requestedPrice = ReadLong(fields, "requestedPrice", "RequestedPrice");
        var requestedAmount = ReadLong(fields, "requestedAmount", "RequestedAmount");
        if ((requestedPrice is long rp && rp != offer.Price)
            || (requestedAmount is long ra && ra != offer.Amount))
            return Conflict(new { Error = "offer_changed" });

        var balance = await level.GetBalanceAsync(pid, offer.CurrencyType);

        // The success DTO has room for exactly CurrencyBalanceResponse and
        // TokenBalanceResponse (formatter KNGMIABFJHK.txt:215/242) — there is no
        // Success flag to carry a failure — so failures must go out as non-2xx and
        // take the client's own error path ("Failed to purchase currency offer",
        // DCFKEFHJAGC.txt:7850). A 200 carrying {Success=false,...} deserialised
        // to two null balances and looked like a successful, free purchase.
        if (balance < offer.Price)
            return Conflict(new { Error = "insufficient_funds" });

        var newBalance = offer.Price == 0
            ? balance
            : await level.GrantCurrencyAsync(pid, offer.CurrencyType, -offer.Price, $"roomCurrency:{offer.PublicId}");
        var customBalance = await AddBalanceAsync(pid, currency.Id, offer.Amount);
        await db.SaveChangesAsync();
        return Ok(new
        {
            // Nested shapes: DPNLANKOHON.txt:331-398 {AccountId, CurrencyId,
            // Balance, ModifiedAt} and PECGEJAAMHB.txt:255-298 {Balance,
            // CurrencyType, Platform}.
            CurrencyBalanceResponse = new
            {
                AccountId = (int)pid,
                CurrencyId = currency.PublicId,
                Balance = customBalance,
                ModifiedAt = DateTime.UtcNow,
            },
            TokenBalanceResponse = new
            {
                Balance = newBalance,
                offer.CurrencyType,
                Platform = 0,
            },
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
        // Check the change tracker first: a bulk award can name the same
        // (player, currency) pair twice, and an unsaved row is invisible to a
        // fresh query — without this the second hit would insert a duplicate
        // balance row and lose the first award.
        var row = db.RoomCurrencyBalances.Local
                      .FirstOrDefault(b => b.PlayerId == playerId && b.RoomCurrencyId == currencyId)
                  ?? await db.RoomCurrencyBalances
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

    /// <param name="jsonBody">Pre-read request body. The body stream can only be
    /// consumed once, so a caller that already had to look at the raw text
    /// (<see cref="AwardCurrencyBulk"/> needs to see its ARRAY root) hands it back
    /// in here instead of letting this re-read <c>Request.Body</c>.</param>
    private async Task<Dictionary<string, string>> ReadFieldsAsync(string? jsonBody = null)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in Request.Query)
            fields[pair.Key] = pair.Value.FirstOrDefault() ?? string.Empty;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var pair in form)
                fields[pair.Key] = pair.Value.FirstOrDefault() ?? string.Empty;
            return fields;
        }

        var raw = jsonBody ?? await ReadJsonBodyAsync();
        if (string.IsNullOrWhiteSpace(raw)) return fields;

        try
        {
            using var doc = JsonDocument.Parse(raw);
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

        return fields;
    }

    private async Task<string?> ReadJsonBodyAsync()
    {
        if ((Request.ContentLength ?? 0) <= 0) return null;
        if (Request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true) return null;
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    /// <summary>One element of the bulk-award array
    /// (request formatter GFKKOADFJKC.txt:331-398: CurrencyId, RecipientId,
    /// Amount, TransactionId). TransactionId is the client's idempotency token;
    /// DorkNet has no transaction ledger to dedupe against, so it is parsed and
    /// echoed nowhere — the response formatter does not read it back either.</summary>
    private sealed record AwardRequest(Guid TransactionId, long RecipientId, long Amount, Guid CurrencyId);

    /// <summary>Parses the bare JSON array the client posts. Also tolerates an
    /// <c>{"awards":[...]}</c> envelope for DorkNet's own tooling.</summary>
    private static List<AwardRequest> ParseAwards(string? body)
    {
        var awards = new List<AwardRequest>();
        if (string.IsNullOrWhiteSpace(body)) return awards;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (JsonProp(root, "awards", "Awards") is { ValueKind: JsonValueKind.Array } envelope)
                    root = envelope;
                else
                    return awards;
            }

            if (root.ValueKind != JsonValueKind.Array) return awards;
            foreach (var element in root.EnumerateArray())
                if (ToAward(element) is { } award)
                    awards.Add(award);
        }
        catch (JsonException)
        {
        }

        return awards;
    }

    private static AwardRequest? ToAward(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        if (JsonGuid(element, "CurrencyId", "currencyId", "RoomCurrencyId", "roomCurrencyId") is not Guid currencyId)
            return null;
        if (JsonLong(element, "RecipientId", "recipientId", "AccountId", "accountId", "PlayerId", "playerId") is not long recipientId
            || recipientId <= 0)
            return null;
        if (JsonLong(element, "Amount", "amount") is not long amount || amount == 0) return null;
        return new AwardRequest(
            JsonGuid(element, "TransactionId", "transactionId") ?? Guid.Empty,
            recipientId,
            amount,
            currencyId);
    }

    private static object AwardFailure(AwardRequest award, string error) => new
    {
        AccountId = (int)award.RecipientId,
        CurrencyId = award.CurrencyId,
        Success = false,
        Error = error,
        Response = (object?)null,
    };

    // JsonDocument property lookup is case-sensitive, so every alias is listed in
    // both casings the client could plausibly emit.
    private static JsonElement? JsonProp(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
                return value;
        return null;
    }

    private static Guid? JsonGuid(JsonElement element, params string[] names) =>
        JsonProp(element, names) is { ValueKind: JsonValueKind.String } value
        && Guid.TryParse(value.GetString(), out var guid)
            ? guid
            : null;

    private static long? JsonLong(JsonElement element, params string[] names) => JsonProp(element, names) switch
    {
        { ValueKind: JsonValueKind.Number } number => number.TryGetInt64(out var value) ? value : null,
        { ValueKind: JsonValueKind.String } text => long.TryParse(text.GetString(), out var value) ? value : null,
        _ => null,
    };

    /// <summary>Several wire fields are Int64 while the backing columns are int;
    /// clamp instead of failing the parse and silently storing 0.</summary>
    private static int ClampToInt(long value) => (int)Math.Clamp(value, 0, int.MaxValue);

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

    /// <summary>Purchase offer as the client READS it (formatter
    /// AALDCAANEMM.txt:499-638): exactly <c>CurrencyPurchaseOfferId</c>,
    /// <c>CurrencyId</c>, <c>Order</c>, <c>Name</c>, <c>CurrencyAmount</c>,
    /// <c>Price</c>, <c>ModifiedAt</c>. <c>CurrencyType</c>/<c>CreatedAt</c> are
    /// not read by the client and are kept only for DorkNet's admin UI, as are
    /// the legacy aliases (see <see cref="ToWire"/>).
    ///
    /// <c>Order</c> (Int32, sent by the client on createPurchaseOffer —
    /// MIHKAJFMIPE.txt:2379-2383) has no column on
    /// <c>RoomCurrencyPurchaseOfferEntity</c> yet, so it always reports 0 and the
    /// pack list falls back to insertion order. Persisting it needs a schema
    /// change in Data/Entities/RoomEconomyEntities.cs.</summary>
    private static object ToOfferWire(RoomCurrencyPurchaseOfferEntity offer, RoomCurrencyEntity currency) => new
    {
        CurrencyPurchaseOfferId = offer.PublicId,
        CurrencyId = currency.PublicId,
        Order = 0,
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
