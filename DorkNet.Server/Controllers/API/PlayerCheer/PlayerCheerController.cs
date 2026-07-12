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
    SystemNotificationService systemNotifications,
    LevelService level) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    /// <summary>POST <c>api/PlayerCheer/v1/create</c> — caller cheers the
    /// target player. Request fields (verified from RecNet.Runtime
    /// KFJKDMGHKHE.EMKBFCLIOGN, KFJKDMGHKHE.txt:696-743, all posted via
    /// BestHTTP AddField → form): <c>PlayerIdTo</c> (target account id),
    /// <c>CheerCategory</c> (the cheer enum), <c>RoomId</c> (optional room
    /// context), <c>Anonymous</c> (bool). The OLD binding read
    /// <c>TargetAccountId</c>/<c>Type</c>, which never matched — the target
    /// bound to null, the handler reported failure, and the watch surfaced
    /// "Failed to cheer player — Something went wrong". The client parses the
    /// response into PHMHCPEMABG { bool, string } and gates on the bool, so
    /// the body must report success. Idempotent on (caller, target, room,
    /// category).</summary>
    [HttpPost("api/PlayerCheer/v1/create")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> Create(
        [FromForm(Name = "PlayerIdTo")] long? playerIdTo,
        [FromForm(Name = "CheerCategory")] int cheerCategory = 0,
        [FromForm(Name = "RoomId")] long? roomId = null,
        [FromForm(Name = "Anonymous")] bool anonymous = false)
    {
        if (playerIdTo is not long target || target <= 0)
            return CheerResult(false, "Invalid cheer target.");
        if (Me == target)
            return CheerResult(false, "You can't cheer yourself.");

        var targetRoomId = roomId ?? 0;
        var existing = await db.Cheers.FirstOrDefaultAsync(c =>
            c.FromPlayerId == Me && c.TargetPlayerId == target &&
            c.TargetRoomId == targetRoomId && c.Type == cheerCategory);
        if (existing is not null)
            // Already cheered — still a success from the client's POV (the
            // string carries the client's own "very recently" phrasing).
            return CheerResult(true, "You have already cheered this player very recently.");

        db.Cheers.Add(new CheerEntity
        {
            FromPlayerId   = Me,
            TargetPlayerId = target,
            TargetRoomId   = targetRoomId,
            Type           = cheerCategory,
        });
        await db.SaveChangesAsync();
        await level.AwardXpAsync(target, LevelService.CheerReceivedXp, $"cheer_from:{Me}");
        // Real notification: a PlayerCheer message the recipient renders as
        // "X cheered you". Anonymous cheers use the anonymous message type and
        // don't reveal the cheerer.
        await systemNotifications.SendAsync(target,
            anonymous
                ? SystemNotificationService.MessageType.PlayerCheerAnonymous
                : SystemNotificationService.MessageType.PlayerCheer,
            fromPlayerId: anonymous ? 0 : Me);
        return CheerResult(true, "");
    }

    /// <summary>The create response deserializes into PHMHCPEMABG
    /// { bool GJLFIFEJDEH, string ABGLLJPIMIO } (KFJKDMGHKHE.txt maps the raw
    /// body via Func&lt;PHMHCPEMABG, LDGADANDBIO&gt;). The JSON keys are
    /// obfuscated with no literals in the dump, so emit the success bool and
    /// message under the common key spellings via a Dictionary (literal keys
    /// bypass the camelCase policy; LitJson ignores the ones it doesn't
    /// read).</summary>
    private IActionResult CheerResult(bool success, string message) =>
        Ok(new Dictionary<string, object?>
        {
            ["Success"] = success,
            ["success"] = success,
            ["Message"] = message,
            ["message"] = message,
            ["Error"] = success ? string.Empty : message,
            ["error"] = success ? string.Empty : message,
        });

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
