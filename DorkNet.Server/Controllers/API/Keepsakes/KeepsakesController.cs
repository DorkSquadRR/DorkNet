using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        var eventKey = eventId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rows = await db.Keepsakes
            .Where(k => k.PlayerId == Me
                && k.Category == "event"
                && (k.EventKey == eventKey
                    || k.EventKey == $"event:{eventKey}"
                    || k.EventKey == $"event/{eventKey}"
                    || EF.Functions.Like(k.EventKey, $"event:{eventKey}:%")
                    || EF.Functions.Like(k.EventKey, $"event/{eventKey}/%")))
            .OrderByDescending(k => k.EarnedAt)
            .ToListAsync();

        return Ok(new
        {
            KeepsakeProgressionEventId = eventId,
            Instances = rows.Select(ToWire),
            CollectionRecords = Array.Empty<object>(),
            KeepsakeProgressionEventIds = new[] { eventId },
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
                Category = form["category"].FirstOrDefault() ?? form["Category"].FirstOrDefault(),
                EventKey = form["eventKey"].FirstOrDefault() ?? form["EventKey"].FirstOrDefault(),
                Title = form["title"].FirstOrDefault() ?? form["Title"].FirstOrDefault(),
                Description = form["description"].FirstOrDefault() ?? form["Description"].FirstOrDefault(),
                ImageName = form["imageName"].FirstOrDefault() ?? form["ImageName"].FirstOrDefault(),
                EarnedAt = DateTime.TryParse(form["earnedAt"].FirstOrDefault() ?? form["EarnedAt"].FirstOrDefault(), out var earned) ? earned : null,
            };
        }

        try
        {
            return await JsonSerializer.DeserializeAsync<KeepsakeRequest>(
                Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new KeepsakeRequest();
        }
        catch (JsonException)
        {
            return new KeepsakeRequest();
        }
    }

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
        return new
        {
            KeepsakeInstanceId = StableKeepsakeInstanceId(row.Id).ToString("D"),
            KeepsakeCategoryConfigId = CategoryId(row.Category),
            PlacedByAccountId = placedByAccountId,
            RoomId = roomId,
            SubRoomId = (long?)null,
        };
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
        public string? Category { get; set; }
        public string? EventKey { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ImageName { get; set; }
        public DateTime? EarnedAt { get; set; }
    }
}
