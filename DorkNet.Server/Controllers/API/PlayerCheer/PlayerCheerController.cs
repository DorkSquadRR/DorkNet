using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.PlayerCheer;

/// <summary>
/// api.{rec.net,localhost}/api/PlayerCheer/v1/* — peer cheer flow.
/// Cheers between players are stored as <see cref="CheerEntity"/>
/// rows keyed by <c>TargetPlayerId</c>. The "selected cheer" badge
/// each player can pin to their profile is stored as a
/// <see cref="PlayerSettingEntity"/> with key <c>SelectedCheer</c>.
/// </summary>
[ApiController]
[Authorize]
public class PlayerCheerController(
    DorkNetDbContext db,
    NotificationService notifications,
    LevelService level) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    /// <summary>POST <c>api/PlayerCheer/v1/create</c> — caller cheers
    /// the target player. <c>Type</c> matches the same enum as
    /// <c>CheerEntity.Type</c>. Idempotent on (caller, target, type)
    /// to match <c>PlayerStateController.CheerPlayer</c>, so the same
    /// category click doesn't inflate the count.</summary>
    [HttpPost("api/PlayerCheer/v1/create")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> Create(
        [FromForm(Name = "TargetAccountId")] long? targetAccountId,
        [FromForm(Name = "Type")] int type = 0)
    {
        if (targetAccountId is not long target || target <= 0)
            return Ok(new { success = false, error = "missing_target" });
        if (Me == target)
            return Ok(new { success = false, error = "cannot_cheer_self" });

        var existing = await db.Cheers.FirstOrDefaultAsync(c =>
            c.FromPlayerId == Me && c.TargetPlayerId == target &&
            c.TargetRoomId == 0 && c.Type == type);
        if (existing is not null) return Ok(new { success = true, already_cheered = true });

        db.Cheers.Add(new CheerEntity
        {
            FromPlayerId   = Me,
            TargetPlayerId = target,
            Type           = type,
        });
        await db.SaveChangesAsync();
        await level.AwardXpAsync(target, LevelService.CheerReceivedXp, $"cheer_from:{Me}");
        await notifications.NotifyAsync(target,
            PushNotificationId.SubscriptionUpdateProfile,
            new { Reason = "CheerReceived", From = Me, Type = type });
        return Ok(new { success = true, error = "" });
    }

    /// <summary>POST <c>api/PlayerCheer/v1/SetSelectedCheer</c> — pin
    /// a cheer category as the caller's selected badge. Stored on the
    /// PlayerSetting key <c>SelectedCheer</c> (string-form int).
    ///
    /// Form field is <c>CheerCategory</c> — NOT <c>Cheer</c>. The 2020.12
    /// watch's AIGGBBFKAKL.SetSelectedCheer (at
    /// <c>AIGGBBFKAKL.txt:1083-1084</c>) does
    /// <c>AddField("CheerCategory", value)</c> when POSTing to
    /// <c>api/PlayerCheer/v1/SetSelectedCheer</c>. A mismatched binding
    /// silently parses to 0, which the watch reads back as "no badge",
    /// so the player's pinned cheer reverts every time.
    ///
    /// The posted value is the watch's PIDFCOGLFHN category enum (e.g.
    /// <c>9000</c> for a specific category) — we store it verbatim and
    /// round-trip through <see cref="Reputation.SelectedCheer"/>; the
    /// watch maps it back to the visual badge.</summary>
    [HttpPost("api/PlayerCheer/v1/SetSelectedCheer")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> SetSelectedCheer(
        [FromForm(Name = "CheerCategory")] int? cheerCategory = null,
        [FromForm(Name = "Cheer")] int? legacyCheer = null)
    {
        // Accept either field name — `CheerCategory` for 2020.12, `Cheer`
        // as a defensive fallback for older / re-skinned clients.
        var value = cheerCategory ?? legacyCheer ?? 0;
        var existing = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == Me && s.Key == "SelectedCheer");
        if (existing is null)
            db.PlayerSettings.Add(new PlayerSettingEntity { PlayerId = Me, Key = "SelectedCheer", Value = value.ToString() });
        else
            existing.Value = value.ToString();
        await db.SaveChangesAsync();
        return Ok(new { success = true, error = "" });
    }

    /// <summary>GET <c>api/PlayerCheer/v1/all</c> — every cheer the
    /// caller has received. Drives the profile's "X cheers" badge.</summary>
    [HttpGet("api/PlayerCheer/v1/all")]
    public async Task<IActionResult> All()
    {
        var rows = await db.Cheers
            .Where(c => c.TargetPlayerId == Me)
            .OrderByDescending(c => c.CheeredAt)
            .Take(200)
            .Select(c => new
            {
                Id = c.Id,
                FromPlayerId = c.FromPlayerId,
                Type = c.Type,
                CheeredAt = c.CheeredAt,
            })
            .ToListAsync();
        return Ok(rows);
    }
}
