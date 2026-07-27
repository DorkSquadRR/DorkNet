using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.RoomKeys;

[ApiController]
[Authorize]
public class RoomKeysController(DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    // Boot-sequence call — the 2020.12 client hits this with roomId=-1
    // before the user has authenticated. Class-level [Authorize] would
    // 401 and the client treats that as fatal, kicking back to LogoutScene.
    [AllowAnonymous]
    [HttpGet("api/roomkeys/v1/room")]
    public async Task<IActionResult> ForRoom([FromQuery] long roomId)
    {
        if (roomId <= 0) return Ok(Array.Empty<object>());
        var rows = await db.RoomKeys
            .Where(k => k.RoomId == roomId && !k.IsDeleted)
            .OrderBy(k => k.Price)
            .ThenBy(k => k.Name)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    /// <summary>The watch key ring — keys the caller HOLDS. This used to return
    /// keys the caller CREATED, which is a different set: a creator never buys
    /// their own key, so players saw none of the keys they had actually
    /// purchased. Keys the caller created are unioned in so room owners still
    /// see their own.</summary>
    [HttpGet("api/roomkeys/v1/mine")]
    public async Task<IActionResult> Mine()
    {
        var pid = Me;
        var purchasedIds = await db.RoomKeyPurchases
            .Where(p => p.PlayerId == pid)
            .Select(p => p.RoomKeyId)
            .ToListAsync();
        var rows = await db.RoomKeys
            .Where(k => !k.IsDeleted && (purchasedIds.Contains(k.Id) || k.CreatorPlayerId == pid))
            .OrderByDescending(k => k.UpdatedAt)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("api/roomkeys")]
    [HttpGet("api/roomkeys/v1")]
    public async Task<IActionResult> List([FromQuery] long? roomId = null)
    {
        var rows = db.RoomKeys.Where(k => !k.IsDeleted);
        if (roomId is long id && id > 0)
            rows = rows.Where(k => k.RoomId == id);

        var result = await rows
            .OrderBy(k => k.RoomId)
            .ThenBy(k => k.Price)
            .Take(200)
            .ToListAsync();
        return Ok(result.Select(ToWire));
    }

    [HttpPost("api/roomkeys/v1/create")]
    public async Task<IActionResult> Create()
    {
        var req = await ReadBodyAsync<NewRoomKeyRequest>();
        var status = ValidateText(req.Name, req.Description, req.Price);
        if (status != RoomKeyStatus.Success)
            return Ok(RoomKeyResponse(status, null));

        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == req.RoomId);
        if (room is null) return Ok(RoomKeyResponse(RoomKeyStatus.RoomDoesNotExist, null));
        if (!await CanManageRoomKeysAsync(room, Me))
            return Ok(RoomKeyResponse(RoomKeyStatus.PermissionDenied, null));

        var activeCount = await db.RoomKeys.CountAsync(k => k.RoomId == room.Id && !k.IsDeleted);
        if (activeCount >= 100)
            return Ok(RoomKeyResponse(RoomKeyStatus.RoomKeyLimitReached, null));

        var duplicate = await db.RoomKeys.AnyAsync(k =>
            k.RoomId == room.Id && !k.IsDeleted && k.Name == req.Name!.Trim());
        if (duplicate) return Ok(RoomKeyResponse(RoomKeyStatus.DuplicateName, null));

        var key = new RoomKeyEntity
        {
            RoomId = room.Id,
            CreatorPlayerId = Me,
            Name = req.Name!.Trim(),
            Description = req.Description!.Trim(),
            Price = req.Price,
            ReplicationId = Guid.NewGuid().ToString("D"),
        };
        db.RoomKeys.Add(key);
        await db.SaveChangesAsync();
        return Ok(RoomKeyResponse(RoomKeyStatus.Success, key));
    }

    /// <summary>The 2023-03-21 client does not call <c>update</c>. It builds
    /// <c>"api/roomkeys/v1/" + action</c> with verb PUT and a <c>RoomKeyId</c>
    /// field, where action is one of <c>updateAll</c>, <c>updateName</c>,
    /// <c>updateDescription</c> or <c>updatePrice</c>
    /// (RecNet.Runtime/AHMBBJNANBP.txt: prefix concat at instr 058, verb 3 at
    /// 067, <c>"RoomKeyId"</c> at 079; action literals at :1345, :1559, :1772,
    /// :2043). None were registered, so every room-key edit 405'd.
    ///
    /// All four share this handler: the request DTO's fields are already
    /// optional, so a partial update naturally leaves the others untouched.</summary>
    [HttpPut("api/roomkeys/v1/updateAll")]
    [HttpPost("api/roomkeys/v1/updateAll")]
    [HttpPut("api/roomkeys/v1/updateName")]
    [HttpPost("api/roomkeys/v1/updateName")]
    [HttpPut("api/roomkeys/v1/updateDescription")]
    [HttpPost("api/roomkeys/v1/updateDescription")]
    [HttpPut("api/roomkeys/v1/updatePrice")]
    [HttpPost("api/roomkeys/v1/updatePrice")]
    [HttpPut("api/roomkeys/v1/update")]
    [HttpPost("api/roomkeys/v1/update")]
    public async Task<IActionResult> Update()
    {
        var req = await ReadBodyAsync<UpdateRoomKeyRequest>();
        var key = await db.RoomKeys.FirstOrDefaultAsync(k => k.Id == req.RoomKeyId && !k.IsDeleted);
        if (key is null) return Ok(RoomKeyResponse(RoomKeyStatus.DoesNotExist, null));
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == key.RoomId);
        if (room is null) return Ok(RoomKeyResponse(RoomKeyStatus.RoomDoesNotExist, null));
        if (!await CanManageRoomKeysAsync(room, Me))
            return Ok(RoomKeyResponse(RoomKeyStatus.PermissionDenied, key));

        var nextName = req.Name?.Trim() ?? key.Name;
        var nextDescription = req.Description?.Trim() ?? key.Description;
        var nextPrice = req.Price ?? key.Price;
        var status = ValidateText(nextName, nextDescription, nextPrice);
        if (status != RoomKeyStatus.Success)
            return Ok(RoomKeyResponse(status, key));

        var duplicate = await db.RoomKeys.AnyAsync(k =>
            k.Id != key.Id && k.RoomId == key.RoomId && !k.IsDeleted && k.Name == nextName);
        if (duplicate) return Ok(RoomKeyResponse(RoomKeyStatus.DuplicateName, key));

        key.Name = nextName;
        key.Description = nextDescription;
        key.Price = nextPrice;
        key.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(RoomKeyResponse(RoomKeyStatus.Success, key));
    }

    [HttpDelete("api/roomkeys/v1/delete/{roomKeyId:long}")]
    [HttpPost("api/roomkeys/v1/delete/{roomKeyId:long}")]
    public async Task<IActionResult> Delete(long roomKeyId)
    {
        var key = await db.RoomKeys.FirstOrDefaultAsync(k => k.Id == roomKeyId && !k.IsDeleted);
        if (key is null) return Content(((int)RoomKeyStatus.DoesNotExist).ToString(), "application/json");
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == key.RoomId);
        if (room is null) return Content(((int)RoomKeyStatus.RoomDoesNotExist).ToString(), "application/json");
        if (!await CanManageRoomKeysAsync(room, Me))
            return Content(((int)RoomKeyStatus.PermissionDenied).ToString(), "application/json");

        key.IsDeleted = true;
        key.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Content(((int)RoomKeyStatus.Success).ToString(), "application/json");
    }

    /// <summary>"Has anyone bought this key?" — the creator calls this before a
    /// destructive edit. It previously answered "has the CALLER bought it",
    /// which is always false for the creator, so the guard never fired and
    /// creators could silently invalidate keys players had paid for.</summary>
    [HttpGet("api/roomkeys/v1/purchased/{roomKeyId:long}")]
    public async Task<IActionResult> Purchased(long roomKeyId)
    {
        var anyPurchase = await db.RoomKeyPurchases.AnyAsync(p => p.RoomKeyId == roomKeyId);
        return Content(anyPurchase ? "true" : "false", "application/json");
    }

    /// <summary>The client sends both <c>roomKeyId</c> and <c>playerId</c> — key
    /// doors evaluate OTHER players' ownership, not just the caller's. Ignoring
    /// playerId meant a door asked about someone else and got the caller's
    /// answer.</summary>
    [HttpGet("api/roomkeys/v1/owns")]
    public async Task<IActionResult> Owns([FromQuery] long roomKeyId, [FromQuery] long? playerId = null)
    {
        var subject = playerId is long p && p > 0 ? p : Me;
        var owns = await db.RoomKeyPurchases.AnyAsync(x => x.RoomKeyId == roomKeyId && x.PlayerId == subject);
        return Content(owns ? "true" : "false", "application/json");
    }

    /// <summary>Bulk key-ownership probe, used by key doors to gate a whole
    /// room of players at once.
    ///
    /// The 2023-03-21 client POSTs a JSON <b>array</b> of
    /// <c>{RoomKeyId, AccountId}</c> pairs as a raw body
    /// (RecNet.Runtime/AHMBBJNANBP_NestedType_JIPDBBEFAPN.txt instrs 044-065),
    /// so each element names the player it is asking about. The old
    /// query/form-only reader saw an empty id list and answered for the caller,
    /// meaning key doors evaluated the wrong player — or nobody at all.</summary>
    [HttpGet("api/roomkeys/v1/owns/bulk")]
    [HttpPost("api/roomkeys/v1/owns/bulk")]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> OwnsBulk()
    {
        var pairs = await ReadOwnershipPairsAsync();
        if (pairs.Count == 0)
        {
            // Legacy query/form path: ids only, always about the caller.
            var ids = await ReadRoomKeyIdsAsync();
            if (ids.Count == 0) return Ok(Array.Empty<object>());
            pairs = ids.Select(id => (RoomKeyId: id, AccountId: Me)).ToList();
        }

        var keyIds = pairs.Select(p => p.RoomKeyId).Distinct().ToList();
        var accountIds = pairs.Select(p => p.AccountId).Distinct().ToList();
        var owned = (await db.RoomKeyPurchases
                .Where(p => keyIds.Contains(p.RoomKeyId) && accountIds.Contains(p.PlayerId))
                .Select(p => new { p.RoomKeyId, p.PlayerId })
                .ToListAsync())
            .Select(p => (p.RoomKeyId, p.PlayerId))
            .ToHashSet();

        return Ok(pairs.Select(p => new
        {
            p.RoomKeyId,
            AccountId = (int)p.AccountId,
            DoesPlayerOwnRoomKey = owned.Contains((p.RoomKeyId, p.AccountId)),
            // legacy alias
            Owns = owned.Contains((p.RoomKeyId, p.AccountId)),
        }).ToList());
    }

    /// <summary>Parse the 2023 client's JSON array of
    /// <c>{RoomKeyId, AccountId}</c> pairs. Returns empty for a form post or a
    /// body that isn't an array, so the legacy path still applies.</summary>
    private async Task<List<(long RoomKeyId, long AccountId)>> ReadOwnershipPairsAsync()
    {
        var pairs = new List<(long, long)>();
        if (Request.HasFormContentType) return pairs;
        try
        {
            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(Request.Body);
            Request.Body.Position = 0;
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return pairs;

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                long? Read(params string[] names)
                {
                    foreach (var n in names)
                        if (el.TryGetProperty(n, out var v) &&
                            v.ValueKind == System.Text.Json.JsonValueKind.Number &&
                            v.TryGetInt64(out var x)) return x;
                    return null;
                }
                var keyId = Read("RoomKeyId", "roomKeyId");
                if (keyId is not long k || k <= 0) continue;
                pairs.Add((k, Read("AccountId", "accountId", "PlayerId", "playerId") ?? Me));
            }
        }
        catch (System.Text.Json.JsonException) { /* not a JSON array body */ }
        return pairs;
    }

    public static object RoomKeyResponse(RoomKeyStatus status, RoomKeyEntity? key) => new
    {
        Status = (int)status,
        RoomKey = key is null ? null : ToWire(key),
    };

    /// <summary>The 2023-03-21 room-key formatter reads nine members —
    /// RoomKeyId, ReplicationId, RoomId, Name, Description, Price,
    /// PurchaseCurrencyId, CreatedAt, ImageName
    /// (RecNet.Runtime/ANHGLIFGACE.txt:627-814 name table, :829-993 reader).
    /// CreatedAt was missing and silently defaulted to DateTime.MinValue in the
    /// key ring. PurchaseCurrencyId (Nullable&lt;Guid&gt;) and ImageName have no
    /// column on RoomKeyEntity yet, so they are still omitted rather than
    /// emitted as permanent nulls — both are absent-tolerant on the client.</summary>
    public static object ToWire(RoomKeyEntity key) => new
    {
        RoomKeyId = key.Id,
        ReplicationId = Guid.TryParse(key.ReplicationId, out var guid)
            ? guid
            : Guid.Empty,
        key.RoomId,
        key.Name,
        key.Description,
        key.Price,
        key.CreatedAt,
    };

    private static RoomKeyStatus ValidateText(string? name, string? description, int price)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3) return RoomKeyStatus.NameTooShort;
        if (name.Trim().Length > 40) return RoomKeyStatus.NameTooLong;
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length < 10) return RoomKeyStatus.DescriptionTooShort;
        if (description.Trim().Length > 174) return RoomKeyStatus.DescriptionTooLong;
        if (price < 10) return RoomKeyStatus.PriceTooLow;
        if (price > 10000) return RoomKeyStatus.PriceTooHigh;
        return RoomKeyStatus.Success;
    }

    private async Task<bool> CanManageRoomKeysAsync(RoomEntity room, long playerId)
    {
        if (room.CreatorPlayerId == playerId) return true;
        var isCoOwner = await db.RoomRoles.AnyAsync(r =>
            r.RoomId == room.Id && r.PlayerId == playerId && r.Role == 0 && r.Accepted);
        if (isCoOwner) return true;
        return await db.Players
            .Where(p => p.Id == playerId)
            .Select(p => p.IsAdmin)
            .FirstOrDefaultAsync();
    }

    private async Task<List<long>> ReadRoomKeyIdsAsync()
    {
        var ids = new List<long>();
        foreach (var value in Request.Query.SelectMany(q => q.Value))
        foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (long.TryParse(part, out var id) && id > 0) ids.Add(id);

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            foreach (var key in new[] { "roomKeyIds", "RoomKeyIds", "ids", "Ids" })
            foreach (var value in form[key])
            foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (long.TryParse(part, out var id) && id > 0) ids.Add(id);
        }

        return ids.Distinct().Take(200).ToList();
    }

    /// <summary>Read a room-key write request.
    ///
    /// The 2023-03-21 client puts every scalar of create/update in the QUERY
    /// STRING, not in a body: <c>BNDIAONDFFF.AFGEDDANEKP</c> only appends to the
    /// param dictionary at field +0x20 (BNDIAONDFFF.txt:492-506), and the send
    /// path joins that dictionary with <c>"&amp;"</c> and glues it onto the URL
    /// behind <c>"?"</c> (BNDIAONDFFF.txt:3196, 3238, 3248-3251). So
    /// <c>POST api/roomkeys/v1/create</c> and
    /// <c>PUT api/roomkeys/v1/update*</c> arrive with an EMPTY body — reading
    /// form/JSON only left Name null and RoomId 0, i.e. every create answered
    /// NameTooShort and every edit DoesNotExist.
    ///
    /// Query wins, form second, and a JSON body is only parsed when neither
    /// carried anything (older clients and the admin UI still post JSON).</summary>
    private async Task<T> ReadBodyAsync<T>() where T : new()
    {
        var form = Request.HasFormContentType ? await Request.ReadFormAsync() : null;

        string? Field(params string[] names)
        {
            foreach (var name in names)
            {
                var q = Request.Query[name].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(q)) return q;
                if (form is not null)
                {
                    var f = form[name].FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(f)) return f;
                }
            }
            return null;
        }

        if (typeof(T) == typeof(NewRoomKeyRequest))
        {
            var roomId = Field("roomId", "RoomId");
            var name = Field("name", "Name");
            var description = Field("description", "Description");
            var price = Field("price", "Price");
            if (roomId is not null || name is not null || description is not null || price is not null)
            {
                object req = new NewRoomKeyRequest
                {
                    RoomId = long.TryParse(roomId, out var parsedRoomId) ? parsedRoomId : 0,
                    Name = name,
                    Description = description,
                    Price = int.TryParse(price, out var parsedPrice) ? parsedPrice : 0,
                };
                return (T)req;
            }
        }
        else if (typeof(T) == typeof(UpdateRoomKeyRequest))
        {
            // updateName sends only Name, updatePrice only Price, etc. — every
            // field the client omitted stays null here and the handler keeps the
            // stored value (AHMBBJNANBP.txt:1300-1345, 1550-1559, 1763-1772,
            // 2018-2043).
            var roomKeyId = Field("roomKeyId", "RoomKeyId");
            var name = Field("name", "Name");
            var description = Field("description", "Description");
            var price = Field("price", "Price");
            if (roomKeyId is not null || name is not null || description is not null || price is not null)
            {
                object req = new UpdateRoomKeyRequest
                {
                    RoomKeyId = long.TryParse(roomKeyId, out var parsedKeyId) ? parsedKeyId : 0,
                    Name = name,
                    Description = description,
                    Price = int.TryParse(price, out var parsedPrice) ? parsedPrice : null,
                };
                return (T)req;
            }
        }

        if (form is not null) return new T();

        try
        {
            return await JsonSerializer.DeserializeAsync<T>(Request.Body, JsonOptions) ?? new T();
        }
        catch (JsonException)
        {
            return new T();
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public sealed class NewRoomKeyRequest
    {
        [JsonPropertyName("roomId")] public long RoomId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("price")] public int Price { get; set; }
    }

    public sealed class UpdateRoomKeyRequest
    {
        // The 2023 client spells this "RoomKeyId"; System.Text.Json is
        // configured case-insensitively so both land on this property.
        [JsonPropertyName("roomKeyId")] public long RoomKeyId { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("price")] public int? Price { get; set; }
    }
}

public enum RoomKeyStatus
{
    Success = 0,
    InvalidParameters = 1,
    DoesNotExist = 2,
    NameTooShort = 3,
    NameTooLong = 4,
    DuplicateName = 5,
    InappropriateName = 6,
    DescriptionTooShort = 7,
    DescriptionTooLong = 8,
    InappropriateDescription = 9,
    PriceTooLow = 10,
    PriceTooHigh = 11,
    PermissionDenied = 12,
    PlayerHasRoomUnderModerationReview = 13,
    JuniorStatusFail = 14,
    PlayerIsNotCoOwner = 15,
    RoomKeyLimitReached = 16,
    PlayerAlreadyOwns = 17,
    RoomUnderModerationReview = 18,
    PurchaseFailed = 19,
    RoomDoesNotExist = 20,
    PaidKeyPurchasingDisabled = 21,
    CreateOrModifyKeysDisabled = 22,
    RoomKeyUnderModerationReview = 23,
    PlayerRestrictedFromP2PSelling = 24,
    PlayerNotRecRoomPlusMember = 25,
}
