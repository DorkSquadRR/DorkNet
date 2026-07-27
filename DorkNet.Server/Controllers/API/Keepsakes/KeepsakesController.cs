using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Keepsakes;

[ApiController]
[Route("api/keepsakes")]
[Authorize]
public class KeepsakesController(DorkNetDbContext db, DomainConfig domain) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    [HttpGet]
    public async Task<IActionResult> Mine()
    {
        await EnsureLoginKeepsakeAsync(Me);
        var rows = await db.Keepsakes
            .Where(k => k.PlayerId == Me)
            .OrderByDescending(k => k.EarnedAt)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("categories")]
    [AllowAnonymous]
    public IActionResult Categories()
    {
        var results = new[]
        {
            new { KeepsakeCategoryId = 0, VisualId = "account", LimitPerRoom = 0, XpValue = 0, IconOutlineImageName = string.Empty, IconFilledImageName = string.Empty },
            new { KeepsakeCategoryId = 1, VisualId = "event", LimitPerRoom = 0, XpValue = 0, IconOutlineImageName = string.Empty, IconFilledImageName = string.Empty },
            new { KeepsakeCategoryId = 2, VisualId = "room", LimitPerRoom = 64, XpValue = 0, IconOutlineImageName = string.Empty, IconFilledImageName = string.Empty },
        };
        return Ok(new
        {
            Results = results,
            TotalResults = results.Length,
        });
    }

    [HttpGet("events")]
    public async Task<IActionResult> Events()
    {
        var rows = await db.Keepsakes
            .Where(k => k.PlayerId == Me && k.Category == "event")
            .OrderByDescending(k => k.EarnedAt)
            .ToListAsync();
        return Ok(rows.Select(ToWire));
    }

    [HttpGet("events/{eventId:long}")]
    public async Task<IActionResult> EventInstances(long eventId)
    {
        var eventKey = eventId.ToString(CultureInfo.InvariantCulture);
        // NOT filtered by PlayerId: Instances is the whole event's placement set (the hunt map),
        // CollectionRecords is only what the caller has picked up.
        var rows = await db.Keepsakes
            .Where(k => k.Category == "event"
                && (k.EventKey == eventKey
                    || k.EventKey == $"event:{eventKey}"
                    || k.EventKey == $"event/{eventKey}"
                    || EF.Functions.Like(k.EventKey, $"event:{eventKey}:%")
                    || EF.Functions.Like(k.EventKey, $"event/{eventKey}/%")))
            .OrderByDescending(k => k.EarnedAt)
            .ToListAsync();

        // RecNet.KeepsakeProgressionEventInstancesDTO has exactly two members, both
        // List<KeepsakeRoomInstanceIdsDTO> (= {Int64, List<Guid>}) - dump.cs:1234798-1234810,
        // ctor(List<KeepsakeRoomInstanceIdsDTO>, List<KeepsakeRoomInstanceIdsDTO>) in
        // IsilDump/RecNet.Runtime/RecNet/KeepsakeProgressionEventInstancesDTO.txt:14.
        // Emitting the flat keepsake rows here left RoomId=0 / KeepsakeInstanceIds=null per entry.
        // Key names follow the same RecNet naming the room endpoint below already uses; the literal
        // JSON names live in Newtonsoft attribute metadata the dumps do not render, so they are
        // unverified - Json.NET matching is case-insensitive and ignores unknown members.
        return Ok(new
        {
            Instances = GroupInstanceIdsByRoom(rows),
            CollectionRecords = GroupInstanceIdsByRoom(rows.Where(r => r.PlayerId == Me)),
        });
    }

    [HttpGet("rooms/{roomId:long}")]
    public async Task<IActionResult> RoomKeepsakes(long roomId)
    {
        var roomExists = await db.Rooms.AsNoTracking().AnyAsync(r => r.Id == roomId);
        if (!roomExists) return NotFound();

        var roomIdText = roomId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rows = await db.Keepsakes
            .Where(k => k.Category == "room"
                && (k.EventKey == $"room:{roomIdText}"
                    || k.EventKey == $"room/{roomIdText}"
                    || k.EventKey == roomIdText
                    || EF.Functions.Like(k.EventKey, $"room:{roomIdText}:%")
                    || EF.Functions.Like(k.EventKey, $"room/{roomIdText}/%")))
            .OrderByDescending(k => k.EarnedAt)
            .ToListAsync();
        var instances = rows.Select(row => ToRoomInstanceWire(row, roomId)).ToList();
        return Ok(new
        {
            Instances = instances,
            CollectionRecords = Array.Empty<object>(),
            KeepsakeProgressionEventIds = Array.Empty<long>(),
        });
    }

    [HttpGet("globalconfig")]
    [AllowAnonymous]
    public IActionResult GlobalConfig() => Ok(new
    {
        KeepsakeFeatureEnabled = true,
        KeepsakeRoomLimit = 64,
        SocialXpBoostEnabled = false,
    });

    [HttpPost]
    public async Task<IActionResult> Create()
    {
        var req = await ReadRequestAsync();

        // 2023 client: FGLDKEJLAKB<System.Guid> OPPHCOJHEJI(HMECHOKOCBB category)
        // (NCCLEJPIABA.txt:1146) POSTs a RecNet.AddKeepsakeInstanceRequest
        // {Int64 RoomId, Int64? SubRoomId, Int32 KeepsakeCategory} - dump.cs:1234509-1234525,
        // field offsets 0x10/0x18/0x28 in AddKeepsakeInstanceRequest.txt - and deserialises the
        // response as a BARE Guid, not an object. The request is refused client-side for offline
        // rooms ("Cannot add keepsakes in offline rooms", NCCLEJPIABA.txt:1533), so RoomId is
        // always a real room here; its presence is what selects this path over the legacy DTO.
        if (req.RoomId is > 0)
        {
            var roomId = req.RoomId.Value;
            if (!await db.Rooms.AsNoTracking().AnyAsync(r => r.Id == roomId)) return NotFound();

            var instanceId = Guid.NewGuid();
            db.Keepsakes.Add(new KeepsakeEntity
            {
                PlayerId = Me,
                Category = "room",
                EventKey = BuildRoomInstanceKey(roomId, req.SubRoomId, req.KeepsakeCategory ?? 0, instanceId),
                EarnedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return Ok(instanceId);
        }

        var key = Trim(req.EventKey, 128);
        if (key.Length == 0) key = $"manual:{Guid.NewGuid():N}";
        var category = Trim(req.Category, 64);
        if (category.Length == 0) category = "event";

        var existing = await db.Keepsakes.FirstOrDefaultAsync(k =>
            k.PlayerId == Me && k.EventKey == key);
        if (existing is null)
        {
            existing = new KeepsakeEntity
            {
                PlayerId = Me,
                EventKey = key,
            };
            db.Keepsakes.Add(existing);
        }

        existing.Category = category;
        existing.Title = Trim(req.Title, 128);
        existing.Description = Trim(req.Description, 1024);
        existing.ImageName = Trim(req.ImageName, 256);
        existing.EarnedAt = req.EarnedAt ?? DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(ToWire(existing));
    }

    private async Task EnsureLoginKeepsakeAsync(long playerId)
    {
        const string key = "first-login";
        if (await db.Keepsakes.AnyAsync(k => k.PlayerId == playerId && k.EventKey == key))
            return;
        db.Keepsakes.Add(new KeepsakeEntity
        {
            PlayerId = playerId,
            Category = "account",
            EventKey = key,
            Title = "First Login",
            Description = "Joined this server.",
        });
        await db.SaveChangesAsync();
    }

    private async Task<KeepsakeRequest> ReadRequestAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            return new KeepsakeRequest
            {
                RoomId = long.TryParse(form["RoomId"].FirstOrDefault() ?? form["roomId"].FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var formRoom) ? formRoom : null,
                SubRoomId = long.TryParse(form["SubRoomId"].FirstOrDefault() ?? form["subRoomId"].FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var formSubRoom) ? formSubRoom : null,
                KeepsakeCategory = int.TryParse(form["KeepsakeCategory"].FirstOrDefault() ?? form["keepsakeCategory"].FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var formCategory) ? formCategory : null,
                Category = form["category"].FirstOrDefault() ?? form["Category"].FirstOrDefault(),
                EventKey = form["eventKey"].FirstOrDefault() ?? form["EventKey"].FirstOrDefault(),
                Title = form["title"].FirstOrDefault() ?? form["Title"].FirstOrDefault(),
                Description = form["description"].FirstOrDefault() ?? form["Description"].FirstOrDefault(),
                ImageName = form["imageName"].FirstOrDefault() ?? form["ImageName"].FirstOrDefault(),
                EarnedAt = DateTime.TryParse(form["earnedAt"].FirstOrDefault() ?? form["EarnedAt"].FirstOrDefault(), out var earned) ? earned : null,
            };
        }

        // The 2023 client sends a RAW JSON body, not a form: NCCLEJPIABA.txt:1458-1465 serialises the
        // whole AddKeepsakeInstanceRequest in one call and hands the string to BNDIAONDFFF's body
        // setter. Read it as a document rather than binding a DTO so the numeric KeepsakeCategory and
        // the legacy string Category can share one wire slot, and so key-name aliases are cheap.
        try
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body);
            var root = doc.RootElement;
            return new KeepsakeRequest
            {
                RoomId = ReadInt64(root, "RoomId"),
                SubRoomId = ReadInt64(root, "SubRoomId"),
                // "Category" is listed as an alias only for the numeric form; when it carries the
                // legacy string ("event"/"room") ReadInt64 declines it and ReadString picks it up.
                KeepsakeCategory = (int?)ReadInt64(root, "KeepsakeCategory", "KeepsakeCategoryConfigId", "Category"),
                Category = ReadString(root, "Category"),
                EventKey = ReadString(root, "EventKey"),
                Title = ReadString(root, "Title"),
                Description = ReadString(root, "Description"),
                ImageName = ReadString(root, "ImageName"),
                EarnedAt = DateTime.TryParse(ReadString(root, "EarnedAt"), out var earnedAt) ? earnedAt : null,
            };
        }
        catch (JsonException)
        {
            return new KeepsakeRequest();
        }
    }

    private static bool TryFindMember(JsonElement root, string[] names, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                foreach (var name in names)
                {
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = prop.Value;
                        return true;
                    }
                }
            }
        }

        value = default;
        return false;
    }

    private static long? ReadInt64(JsonElement root, params string[] names)
    {
        if (!TryFindMember(root, names, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var number)) return number;
        if (el.ValueKind == JsonValueKind.String
            && long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    private static string? ReadString(JsonElement root, params string[] names)
        => TryFindMember(root, names, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private object ToWire(KeepsakeEntity row) => new
    {
        row.Id,
        row.PlayerId,
        row.Category,
        row.EventKey,
        row.Title,
        row.Description,
        row.ImageName,
        ImageUrl = string.IsNullOrWhiteSpace(row.ImageName) ? string.Empty : $"https://{domain.Sub("cdn")}/{row.ImageName}",
        row.EarnedAt,
    };

    private static object ToRoomInstanceWire(KeepsakeEntity row, long roomId)
    {
        var placedByAccountId = row.PlayerId is > 0 and <= int.MaxValue
            ? (int)row.PlayerId
            : 0;
        var parsed = TryParseInstanceKey(row.EventKey, out var keyRoomId, out var subRoomId, out var categoryConfigId, out var instanceId);
        return new
        {
            KeepsakeInstanceId = (parsed ? instanceId : StableKeepsakeInstanceId(row.Id)).ToString("D"),
            // KeepsakeCategory is the visual config id (enum PIHCLHIKEPH: Explore=0, GreenPowerCore=1,
            // Present=2, PurplePowerCore=3, ... - dump.cs:1199524), NOT our account/event/room bucket,
            // so replay what the client sent; CategoryId() is only the fallback for legacy rows.
            KeepsakeCategoryConfigId = parsed ? categoryConfigId : CategoryId(row.Category),
            PlacedByAccountId = placedByAccountId,
            RoomId = parsed ? keyRoomId : roomId,
            SubRoomId = subRoomId,
        };
    }

    private static List<object> GroupInstanceIdsByRoom(IEnumerable<KeepsakeEntity> rows) => rows
        .Select(row => TryParseInstanceKey(row.EventKey, out var roomId, out _, out _, out var instanceId)
            ? (RoomId: roomId, InstanceId: instanceId)
            : (RoomId: 0L, InstanceId: StableKeepsakeInstanceId(row.Id)))
        .GroupBy(x => x.RoomId)
        .Select(group => (object)new
        {
            RoomId = group.Key,
            KeepsakeInstanceIds = group.Select(x => x.InstanceId).Distinct().Select(id => id.ToString("D")).ToList(),
        })
        .ToList();

    // KeepsakeEntity has no room/sub-room/instance-id columns, so a placed instance is encoded into
    // the existing 128-char EventKey. Layouts:
    //   room:{roomId}:{subRoomId|-}:{categoryConfigId}:{instanceGuid:N}   (client placements)
    //   event:{eventId}:{roomId}:{subRoomId|-}:{categoryConfigId}:{instanceGuid:N}
    // Both stay inside the LIKE prefixes the room/event queries above already use. Legacy short keys
    // ("room:{roomId}", "event:{eventId}", "first-login") fail to parse and fall back to the
    // MD5-derived instance id, which is what those rows were already reported with.
    private static string BuildRoomInstanceKey(long roomId, long? subRoomId, int categoryConfigId, Guid instanceId)
    {
        var room = roomId.ToString(CultureInfo.InvariantCulture);
        var subRoom = subRoomId?.ToString(CultureInfo.InvariantCulture) ?? "-";
        var category = categoryConfigId.ToString(CultureInfo.InvariantCulture);
        return $"room:{room}:{subRoom}:{category}:{instanceId:N}";
    }

    private static bool TryParseInstanceKey(string? eventKey, out long roomId, out long? subRoomId, out int categoryConfigId, out Guid instanceId)
    {
        roomId = 0;
        subRoomId = null;
        categoryConfigId = 0;
        instanceId = Guid.Empty;

        var parts = (eventKey ?? string.Empty).Split(':');
        int i;
        if (parts.Length == 5 && string.Equals(parts[0], "room", StringComparison.OrdinalIgnoreCase)) i = 1;
        else if (parts.Length == 6 && string.Equals(parts[0], "event", StringComparison.OrdinalIgnoreCase)) i = 2;
        else return false;

        if (!Guid.TryParseExact(parts[i + 3], "N", out instanceId)) return false;
        if (!long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out roomId)) return false;
        if (long.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sub)) subRoomId = sub;
        if (!int.TryParse(parts[i + 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out categoryConfigId)) categoryConfigId = 0;
        return true;
    }

    private static int CategoryId(string? category) =>
        string.Equals(category, "event", StringComparison.OrdinalIgnoreCase) ? 1 :
        string.Equals(category, "room", StringComparison.OrdinalIgnoreCase) ? 2 :
        0;

    private static Guid StableKeepsakeInstanceId(long id)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"keepsakeInstance:{id}"));
        return new Guid(hash);
    }

    private static string Trim(string? value, int max)
    {
        var s = (value ?? string.Empty).Trim();
        return s.Length <= max ? s : s[..max];
    }

    public sealed class KeepsakeRequest
    {
        // RecNet.AddKeepsakeInstanceRequest (2023 client).
        public long? RoomId { get; set; }
        public long? SubRoomId { get; set; }
        public int? KeepsakeCategory { get; set; }

        public string? Category { get; set; }
        public string? EventKey { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ImageName { get; set; }
        public DateTime? EarnedAt { get; set; }
    }
}
