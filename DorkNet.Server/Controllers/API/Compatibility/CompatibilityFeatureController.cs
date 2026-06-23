using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.Compatibility;

[ApiController]
public class CompatibilityFeatureController(DorkNetDbContext db) : ControllerBase
{
    [HttpPost("api/banappeal/generateCode")]
    [Authorize]
    public async Task<IActionResult> GenerateBanAppealCode()
    {
        var pid = this.RequireCurrentPlayerId();
        var code = $"BA-{RandomNumberGenerator.GetInt32(100000, 999999)}";
        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = pid,
            Key = $"banappeal:{DateTime.UtcNow:yyyyMMddHHmmss}",
            Value = code,
        });
        await db.SaveChangesAsync();
        return Ok(new { value = code });
    }

    [HttpPost("api/CampusCard/v1/UpdateAndGetSubscription")]
    [Authorize]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> UpdateAndGetCampusCardSubscription()
    {
        var pid = this.RequireCurrentPlayerId();
        var req = await ReadCampusCardRequestAsync();
        var targetId = req.PlatformAccountSubscribedPlayerId > 0
            ? req.PlatformAccountSubscribedPlayerId
            : pid;

        if (targetId != pid && req.Subscription)
        {
            var exists = await db.Subscriptions.AnyAsync(s =>
                s.SubscriberPlayerId == pid && s.TargetPlayerId == targetId);
            if (!exists)
            {
                db.Subscriptions.Add(new SubscriptionEntity
                {
                    SubscriberPlayerId = pid,
                    TargetPlayerId = targetId,
                });
                await db.SaveChangesAsync();
            }
        }
        else if (targetId != pid && !req.Subscription)
        {
            await db.Subscriptions
                .Where(s => s.SubscriberPlayerId == pid && s.TargetPlayerId == targetId)
                .ExecuteDeleteAsync();
        }

        var active = targetId == pid || await db.Subscriptions.AnyAsync(s =>
            s.SubscriberPlayerId == pid && s.TargetPlayerId == targetId);
        var now = DateTime.UtcNow;
        var subscriptionId = unchecked((pid * 397L) ^ targetId);
        var subscription = new Dictionary<string, object?>
        {
            ["subscriptionId"] = subscriptionId,
            ["recNetPlayerId"] = pid,
            ["platformAccountSubscribedPlayerId"] = targetId,
            ["isActive"] = active,
            ["startedAt"] = now,
            ["currentPeriodEnd"] = active ? now.AddYears(10) : now,
            ["renewalDate"] = active ? now.AddYears(10) : now,
            ["SubscriptionId"] = subscriptionId,
            ["RecNetPlayerId"] = pid,
            ["PlatformAccountSubscribedPlayerId"] = targetId,
            ["IsActive"] = active,
            ["StartedAt"] = now,
            ["CurrentPeriodEnd"] = active ? now.AddYears(10) : now,
            ["RenewalDate"] = active ? now.AddYears(10) : now,
        };

        return Ok(new Dictionary<string, object?>
        {
            ["subscription"] = subscription,
            ["platformAccountSubscribedPlayerId"] = targetId,
            ["Subscription"] = subscription,
            ["PlatformAccountSubscribedPlayerId"] = targetId,
        });
    }

    [HttpGet("api/CampusCard/PS5RecRoomPlusEnabledForAllPlayers")]
    [AllowAnonymous]
    public IActionResult Ps5RecRoomPlusEnabledForAllPlayers()
        => Content("true", "application/json");

    [HttpPost("api/clubreporting/v1/report")]
    [Authorize]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> ClubReport()
    {
        var pid = this.RequireCurrentPlayerId();
        var req = await ReadClubReportAsync();
        var club = req.ClubId > 0
            ? await db.Clubs.FirstOrDefaultAsync(c => c.Id == req.ClubId)
            : null;
        if (club is null) return Ok(new { Success = false, Message = "club_not_found" });

        var message = $"Club report: {req.Message ?? string.Empty}".Trim();
        if (message.Length > 1000) message = message[..1000];
        db.Reports.Add(new ReportEntity
        {
            ReporterPlayerId = pid,
            TargetPlayerId = club.CreatorPlayerId,
            Category = req.ReportCategory,
            Message = message,
        });
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Message = string.Empty });
    }

    private async Task<CampusCardRequest> ReadCampusCardRequestAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            return new CampusCardRequest
            {
                Subscription = bool.TryParse(form["subscription"].FirstOrDefault(), out var s) && s,
                PlatformAccountSubscribedPlayerId = long.TryParse(
                    form["platformAccountSubscribedPlayerId"].FirstOrDefault(), out var id) ? id : 0,
            };
        }
        try
        {
            return await JsonSerializer.DeserializeAsync<CampusCardRequest>(
                Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new CampusCardRequest();
        }
        catch (JsonException) { return new CampusCardRequest(); }
    }

    private async Task<ClubReportRequest> ReadClubReportAsync()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            return new ClubReportRequest
            {
                ClubId = long.TryParse(form["clubId"].FirstOrDefault()
                                       ?? form["ClubId"].FirstOrDefault(), out var clubId) ? clubId : 0,
                ReportCategory = int.TryParse(form["reportCategory"].FirstOrDefault()
                                              ?? form["ReportCategory"].FirstOrDefault(), out var cat) ? cat : 5,
                Message = form["message"].FirstOrDefault() ?? form["Message"].FirstOrDefault(),
            };
        }
        try
        {
            return await JsonSerializer.DeserializeAsync<ClubReportRequest>(
                Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ClubReportRequest();
        }
        catch (JsonException) { return new ClubReportRequest(); }
    }

    private sealed class CampusCardRequest
    {
        public bool Subscription { get; set; }
        public long PlatformAccountSubscribedPlayerId { get; set; }
    }

    private sealed class ClubReportRequest
    {
        public long ClubId { get; set; }
        public int ReportCategory { get; set; } = 5;
        public string? Message { get; set; }
    }
}
