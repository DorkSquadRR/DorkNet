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

    [HttpGet("api/roomkeys/v1/mine")]
    public async Task<IActionResult> Mine()
    {
        var pid = Me;
        var rows = await db.RoomKeys
            .Where(k => k.CreatorPlayerId == pid && !k.IsDeleted)
            .OrderByDescending(k => k.UpdatedAt)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
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

    [HttpGet("api/roomkeys/v1/purchased/{roomKeyId:long}")]
    public async Task<IActionResult> Purchased(long roomKeyId)
    {
        var pid = Me;
        var owns = await db.RoomKeyPurchases.AnyAsync(p => p.RoomKeyId == roomKeyId && p.PlayerId == pid);
        return Content(owns ? "true" : "false", "application/json");
    }

    public static object RoomKeyResponse(RoomKeyStatus status, RoomKeyEntity? key) => new
    {
        Status = (int)status,
        RoomKey = key is null ? null : ToWire(key),
    };

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

    private async Task<T> ReadBodyAsync<T>() where T : new()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            if (typeof(T) == typeof(NewRoomKeyRequest))
            {
                object req = new NewRoomKeyRequest
                {
                    RoomId = long.TryParse(form["roomId"].FirstOrDefault()
                                           ?? form["RoomId"].FirstOrDefault(), out var roomId)
                        ? roomId
                        : 0,
                    Name = form["name"].FirstOrDefault() ?? form["Name"].FirstOrDefault(),
                    Description = form["description"].FirstOrDefault() ?? form["Description"].FirstOrDefault(),
                    Price = int.TryParse(form["price"].FirstOrDefault()
                                         ?? form["Price"].FirstOrDefault(), out var price)
                        ? price
                        : 0,
                };
                return (T)req;
            }

            if (typeof(T) == typeof(UpdateRoomKeyRequest))
            {
                object req = new UpdateRoomKeyRequest
                {
                    RoomKeyId = long.TryParse(form["roomKeyId"].FirstOrDefault()
                                              ?? form["RoomKeyId"].FirstOrDefault(), out var roomKeyId)
                        ? roomKeyId
                        : 0,
                    Name = form["name"].FirstOrDefault() ?? form["Name"].FirstOrDefault(),
                    Description = form["description"].FirstOrDefault() ?? form["Description"].FirstOrDefault(),
                    Price = int.TryParse(form["price"].FirstOrDefault()
                                         ?? form["Price"].FirstOrDefault(), out var price)
                        ? price
                        : null,
                };
                return (T)req;
            }

            return new T();
        }

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
