using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.FreeGifts;

[ApiController]
[Authorize]
public class FreeGiftsController(DorkNetDbContext db, NotificationService notifications) : ControllerBase
{
    [HttpPost("api/freegifts/v1/sendmultiple")]
    public async Task<IActionResult> SendMultiple()
    {
        var fields = await ReadFieldsAsync();
        var sender = this.RequireCurrentPlayerId();
        var recipients = ReadLongList(fields, "recipientPlayerIds", "RecipientPlayerIds", "playerIds", "PlayerIds");
        if (recipients.Count == 0) return BadRequest("missing_recipients");

        var currencyType = ReadInt(fields, "currencyType", "CurrencyType") ?? 2;
        var currency = Math.Max(0, ReadInt(fields, "currency", "Currency") ?? 0);
        var xp = Math.Max(0, ReadInt(fields, "xp", "Xp") ?? 0);
        var message = ReadString(fields, "message", "Message") ?? string.Empty;
        var created = new List<(long RecipientPlayerId, GiftPackageEntity Gift)>();

        foreach (var recipient in recipients.Distinct().Take(100))
        {
            if (!await db.Players.AnyAsync(p => p.Id == recipient)) continue;
            var gift = new GiftPackageEntity
            {
                RecipientPlayerId = recipient,
                FromPlayerId = (int)sender,
                CurrencyType = currency > 0 ? currencyType : 0,
                Currency = currency,
                Xp = xp,
                Level = 1,
                GiftContext = 0,
                GiftRarity = 0,
                Message = message,
                Platform = -1,
                PackageVariant = "FreeGift",
                PackageMaterial = string.Empty,
                Consumed = false,
                IsValid = true,
                SupportsCurrentPlatform = true,
            };
            db.GiftPackages.Add(gift);
            created.Add((recipient, gift));
        }

        await db.SaveChangesAsync();
        foreach (var row in created)
        {
            await notifications.NotifyAsync(row.RecipientPlayerId,
                PushNotificationId.GiftPackageReceived,
                new { FromPlayerId = sender, CurrencyType = currencyType, Currency = currency, Xp = xp, Message = message });
        }

        return Ok(new { Success = true, Sent = created.Count });
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

    private static int? ReadInt(Dictionary<string, string> fields, params string[] names) =>
        int.TryParse(ReadString(fields, names), out var value) ? value : null;

    private static List<long> ReadLongList(Dictionary<string, string> fields, params string[] names) =>
        names.SelectMany(name => fields.TryGetValue(name, out var value)
                ? value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : Array.Empty<string>())
            .Select(v => long.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .Take(100)
            .ToList();
}
