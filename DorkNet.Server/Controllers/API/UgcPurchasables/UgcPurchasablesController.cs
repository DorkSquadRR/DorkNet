using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.UgcPurchasables;

[ApiController]
public class UgcPurchasablesController(DorkNetDbContext db) : ControllerBase
{
    [HttpGet("api/ugcPurchasables")]
    [AllowAnonymous]
    public async Task<IActionResult> List([FromQuery] long roomId = 0)
    {
        var rows = await db.UgcPurchasables
            .Where(i => !i.IsDeleted && (roomId <= 0 || i.RoomId == roomId))
            .OrderBy(i => i.SortOrder)
            .ThenBy(i => i.Name)
            .Take(200)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpPost("api/ugcPurchasables")]
    [Authorize]
    public async Task<IActionResult> Create()
    {
        var fields = await ReadFieldsAsync();
        var roomId = ReadLong(fields, "roomId", "RoomId");
        if (roomId is not long rid || rid <= 0) return BadRequest("missing_room");
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == rid);
        if (room is null) return NotFound("room_not_found");
        var me = this.RequireCurrentPlayerId();
        if (!await CanManageRoomAsync(room, me)) return Forbid();

        var item = new UgcPurchasableEntity
        {
            RoomId = room.Id,
            CreatorPlayerId = me,
            Name = Trim(ReadString(fields, "name", "Name") ?? "Purchasable", 128),
            Description = Trim(ReadString(fields, "description", "Description"), 1024),
            ImageName = Trim(ReadString(fields, "imageName", "ImageName"), 256),
            Price = Math.Max(0, ReadInt(fields, "price", "Price") ?? 0),
            CurrencyType = Math.Max(0, ReadInt(fields, "currencyType", "CurrencyType") ?? 2),
            ItemType = Math.Max(0, ReadInt(fields, "itemType", "ItemType") ?? 0),
            SortOrder = ReadInt(fields, "sortOrder", "SortOrder") ?? 0,
            IsFeatured = ReadBool(fields, "isFeatured", "IsFeatured") ?? false,
        };
        db.UgcPurchasables.Add(item);
        await db.SaveChangesAsync();
        return Ok(ToWire(item));
    }

    [HttpPut("api/ugcPurchasables")]
    [HttpPost("api/ugcPurchasables/update")]
    [Authorize]
    public async Task<IActionResult> Update()
    {
        var fields = await ReadFieldsAsync();
        var item = await FindItemAsync(fields);
        if (item is null) return NotFound();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == item.RoomId);
        if (room is null || !await CanManageRoomAsync(room, this.RequireCurrentPlayerId())) return Forbid();

        if (ReadString(fields, "name", "Name") is { } name) item.Name = Trim(name, 128);
        if (ReadString(fields, "description", "Description") is { } description) item.Description = Trim(description, 1024);
        if (ReadString(fields, "imageName", "ImageName") is { } imageName) item.ImageName = Trim(imageName, 256);
        if (ReadInt(fields, "price", "Price") is int price) item.Price = Math.Max(0, price);
        if (ReadInt(fields, "currencyType", "CurrencyType") is int currencyType) item.CurrencyType = Math.Max(0, currencyType);
        if (ReadInt(fields, "itemType", "ItemType") is int itemType) item.ItemType = Math.Max(0, itemType);
        if (ReadInt(fields, "sortOrder", "SortOrder") is int sortOrder) item.SortOrder = sortOrder;
        if (ReadBool(fields, "isFeatured", "IsFeatured") is bool featured) item.IsFeatured = featured;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(item));
    }

    [HttpDelete("api/ugcPurchasables")]
    [HttpPost("api/ugcPurchasables/delete")]
    [Authorize]
    public async Task<IActionResult> Delete()
    {
        var fields = await ReadFieldsAsync();
        var item = await FindItemAsync(fields);
        if (item is null) return NotFound();
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == item.RoomId);
        if (room is null || !await CanManageRoomAsync(room, this.RequireCurrentPlayerId())) return Forbid();

        item.IsDeleted = true;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(item));
    }

    [HttpGet("api/ugcPurchasables/v1/items/bulk")]
    [HttpPost("api/ugcPurchasables/v1/items/bulk")]
    [AllowAnonymous]
    public async Task<IActionResult> Bulk()
    {
        var fields = await ReadFieldsAsync();
        var ids = ReadGuidList(fields, "ids", "Ids", "itemIds", "ItemIds", "ugcPurchasableIds", "UgcPurchasableIds");
        if (ids.Count == 0) return Ok(Array.Empty<object>());
        var rows = await db.UgcPurchasables
            .Where(i => !i.IsDeleted && ids.Contains(i.PublicId))
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    private async Task<UgcPurchasableEntity?> FindItemAsync(Dictionary<string, string> fields)
    {
        var publicId = ReadGuid(fields, "ugcPurchasableId", "UgcPurchasableId", "itemId", "ItemId", "id", "Id");
        if (publicId is Guid guid)
            return await db.UgcPurchasables.FirstOrDefaultAsync(i => i.PublicId == guid && !i.IsDeleted);
        var localId = ReadLong(fields, "internalId", "ugcPurchasableInternalId");
        if (localId is long id)
            return await db.UgcPurchasables.FirstOrDefaultAsync(i => i.Id == id && !i.IsDeleted);
        return null;
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

    private static bool? ReadBool(Dictionary<string, string> fields, params string[] names) =>
        bool.TryParse(ReadString(fields, names), out var value) ? value : null;

    private static Guid? ReadGuid(Dictionary<string, string> fields, params string[] names) =>
        Guid.TryParse(ReadString(fields, names), out var value) ? value : null;

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

    private static object ToWire(UgcPurchasableEntity item) => new
    {
        UgcPurchasableId = item.PublicId,
        PurchasableItemId = item.PublicId,
        Id = item.PublicId,
        InternalId = item.Id,
        item.RoomId,
        CreatorPlayerId = (int)item.CreatorPlayerId,
        item.Name,
        item.Description,
        item.ImageName,
        item.Price,
        item.CurrencyType,
        item.ItemType,
        item.IsFeatured,
        item.SortOrder,
        item.CreatedAt,
        item.UpdatedAt,
    };
}
