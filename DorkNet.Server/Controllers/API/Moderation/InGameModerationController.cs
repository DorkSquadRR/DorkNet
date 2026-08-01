using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Moderation;

/// <summary>
/// In-game moderation endpoints invoked by the watch's player-action
/// dialogs. URLs / form-fields verified against the Cpp2IL ISIL dump:
///
///   POST api/PlayerReporting/v3/create
///     PlayerReporting.txt:1696 + 1712-1822
///     Form: PlayerIdReported (int), ReportCategory (int), Details (string),
///           HeightReporter (float "F2"), HeightReported (float "F2"),
///           RoomId (long).
///
///   POST api/PlayerReporting/v3/voteToKick
///     PlayerReporting.txt:3175 + 3215-3237
///     Form: PlayerId (long), Response (bool), GameSessionId (long).
///
///   POST api/PlayerReporting/v1/instantKick
///     PlayerReporting.txt:3417 (single) + 3590 (bulk)
///     Body: PlayerId(s) — one or many. Admin/dev only.
///
///   POST api/PlayerReporting/v1/deviceId
///     PlayerReporting.txt:3736 + 3749
///     Form: oldDeviceId (string).
///
///   POST api/PlayersBanned/v2/ban
///     Moderation.txt:623-702
///     Form: PlayerId (long), Reason (int), BanType (int),
///           DisplayReason (string), BannedUntil (string MM/dd/yyyy HH:mm).
///     Admin only.
///
/// Without these handlers the watch fell through to the JSON catch-all
/// returning <c>{}</c>, which the OkResponse parser tolerates but the
/// admin-action then has no observable effect — i.e. "the admin
/// system doesn't work at all".
/// </summary>
[ApiController]
[Authorize]
public class InGameModerationController(
    DorkNetDbContext db,
    NotificationService notifications,
    ILogger<InGameModerationController> logger) : ControllerBase
{
    // ── /v3/create — file a player report ────────────────────────────
    [HttpPost("api/PlayerReporting/v3/create")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> CreateReport(
        [FromForm(Name = "PlayerIdReported")] long? playerIdReported,
        [FromForm(Name = "ReportCategory")] int reportCategory = 0,
        [FromForm(Name = "Details")] string? details = null,
        [FromForm(Name = "HeightReporter")] float? heightReporter = null,
        [FromForm(Name = "HeightReported")] float? heightReported = null,
        [FromForm(Name = "RoomId")] long roomId = 0)
    {
        if (playerIdReported is not long target || target <= 0)
            return Ok(new { success = false, error = "missing_target" });

        var reporter = this.RequireCurrentPlayerId();
        if (reporter == target)
            return Ok(new { success = false, error = "cannot_report_self" });

        var trimmed = (details ?? string.Empty).Trim();
        if (trimmed.Length > 1000) trimmed = trimmed[..1000];

        db.Reports.Add(new ReportEntity
        {
            ReporterPlayerId = reporter,
            TargetPlayerId   = target,
            Category         = reportCategory,
            Message          = trimmed,
            RoomId           = roomId,
        });
        await db.SaveChangesAsync();
        logger.LogInformation(
            "[mod] report filed: reporter={Reporter} target={Target} category={Cat} room={Room}",
            reporter, target, reportCategory, roomId);
        return Ok(new { success = true, error = "" });
    }

    // ── /v3/voteToKick — room-vote kick ──────────────────────────────
    [HttpPost("api/PlayerReporting/v3/voteToKick")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public IActionResult VoteToKick(
        [FromForm(Name = "PlayerId")] long? playerId,
        [FromForm(Name = "Response")] bool? response,
        [FromForm(Name = "GameSessionId")] long gameSessionId = 0)
    {
        var caller = this.RequireCurrentPlayerId();
        logger.LogInformation(
            "[mod] vote-to-kick by {Caller}: target={Target} yes={Yes} session={Session}",
            caller, playerId, response, gameSessionId);
        // Real implementation would tally votes per (session, target) and
        // push a kick once the threshold tripped. For now we accept the
        // vote without side-effects so the watch UI's "voted" indicator
        // shows.
        return Ok(new { success = true, error = "" });
    }

    // ── /v1/instantKick — admin one-shot ─────────────────────────────
    /// <summary>
    /// Admin/dev one-shot kick. The 2020.12 watch posts either
    /// <c>?playerId=N</c> for a single target (PlayerReporting.txt:3417)
    /// or a list body for bulk kicks (3590).
    ///
    /// The 2023-03-21 client posts neither: it serialises a
    /// <c>RecNet.KickPlayerDTO</c> with <c>JsonUtility.ToJson</c> and sends it
    /// as a RAW JSON body (RecNet.Runtime/FPIBGPIAOBI.txt:5012 JsonUtility.ToJson,
    /// :5018 BNDIAONDFFF.FJLLPHFOOJJ raw-body setter, :5034 verb rdx=2).
    /// KickPlayerDTO's fields are not obfuscated — <c>public long GameSessionId</c>
    /// and <c>public List&lt;int&gt; PlayerIds</c> — so the body is exactly
    /// <c>{"GameSessionId":123,"PlayerIds":[456]}</c>. Unity's JsonUtility emits
    /// field names verbatim.
    ///
    /// None of the query/form parameters bound against that body, so the target
    /// list came back empty and every 2023 instant-kick returned
    /// "missing_target" and kicked nobody.
    ///
    /// Gated to <see cref="PlayerEntity.IsAdmin"/> /
    /// <see cref="PlayerEntity.IsDeveloper"/>.
    /// </summary>
    [HttpPost("api/PlayerReporting/v1/instantKick")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> InstantKick(
        [FromQuery(Name = "playerId")] long? singleId,
        [FromForm(Name = "PlayerId")] long? formId,
        [FromForm(Name = "PlayerIds")] string? formIds)
    {
        var caller = this.RequireCurrentPlayerId();
        if (!await IsCallerAdminOrDevAsync(caller))
            return Forbid();

        var ids = new List<long>();
        if (singleId is long s && s > 0) ids.Add(s);
        if (formId  is long f && f > 0) ids.Add(f);
        if (!string.IsNullOrEmpty(formIds))
        {
            foreach (var part in formIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (long.TryParse(part, out var v) && v > 0) ids.Add(v);
        }
        ids.AddRange(await ReadKickPlayerDtoIdsAsync());
        ids = ids.Distinct().ToList();
        if (ids.Count == 0) return Ok(new { success = false, error = "missing_target" });

        foreach (var target in ids)
        {
            logger.LogInformation("[mod] instant-kick by {Caller}: target={Target}", caller, target);
            // Push the ModerationKick shape the 2020.12 client expects:
            // Msg.Reason must be a ModerationBlockDetail object.
            await notifications.KickPlayerAsync(target, "Instant kick by moderator.");
        }
        return Ok(new { success = true, error = "" });
    }

    /// <summary>Read the 2023 client's raw <c>KickPlayerDTO</c> JSON body.
    /// Returns an empty list for a form post or an unparseable body so the
    /// older query/form paths are unaffected.</summary>
    private async Task<List<long>> ReadKickPlayerDtoIdsAsync()
    {
        var ids = new List<long>();
        if (Request.HasFormContentType) return ids;

        try
        {
            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var doc = await JsonDocument.ParseAsync(Request.Body);
            Request.Body.Position = 0;
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return ids;

            foreach (var name in new[] { "PlayerIds", "playerIds" })
            {
                if (!doc.RootElement.TryGetProperty(name, out var arr) ||
                    arr.ValueKind != JsonValueKind.Array) continue;
                foreach (var el in arr.EnumerateArray())
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var v) && v > 0)
                        ids.Add(v);
                break;
            }

            foreach (var name in new[] { "PlayerId", "playerId" })
            {
                if (doc.RootElement.TryGetProperty(name, out var one) &&
                    one.ValueKind == JsonValueKind.Number &&
                    one.TryGetInt64(out var v) && v > 0)
                {
                    ids.Add(v);
                    break;
                }
            }
        }
        catch (JsonException) { /* not a JSON body */ }
        return ids;
    }

    // ── /v1/deviceId — log mismatched device id ──────────────────────
    [HttpPost("api/PlayerReporting/v1/deviceId")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public IActionResult MismatchedDeviceWarning(
        [FromForm(Name = "oldDeviceId")] string? oldDeviceId)
    {
        logger.LogInformation(
            "[mod] mismatched device id reported: caller={Caller} old={Old}",
            this.CurrentPlayerId(), oldDeviceId);
        return Ok(new { success = true, error = "" });
    }

    // ── /api/PlayersBanned/v2/ban — admin ban ────────────────────────
    [HttpPost("api/PlayersBanned/v2/ban")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> Ban(
        [FromForm(Name = "PlayerId")] long? playerId,
        [FromForm(Name = "Reason")] int reason = 0,
        [FromForm(Name = "BanType")] int banType = 0,
        [FromForm(Name = "DisplayReason")] string? displayReason = null,
        [FromForm(Name = "BannedUntil")] string? bannedUntil = null)
    {
        var caller = this.RequireCurrentPlayerId();
        if (!await IsCallerAdminOrDevAsync(caller))
            return Forbid();

        if (playerId is not long target || target <= 0)
            return Ok(new { success = false, error = "missing_target" });

        // BannedUntil arrives as "MM/dd/yyyy HH:mm" per Moderation.txt:696.
        DateTime? until = null;
        if (!string.IsNullOrEmpty(bannedUntil) &&
            DateTime.TryParseExact(bannedUntil, "MM/dd/yyyy HH:mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            until = parsed;
        }

        var p = await db.Players.FirstOrDefaultAsync(x => x.Id == target);
        if (p is null) return Ok(new { success = false, error = "no_such_player" });
        p.BannedUntil = until ?? DateTime.UtcNow.AddYears(99);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "[mod] ban by {Caller}: target={Target} reason={Reason} until={Until} display={Display}",
            caller, target, reason, p.BannedUntil, displayReason);

        // Push the ban as a ModerationKick (22) with IsBan set, NOT as
        // ModerationRoomBan (24): the 2023-03-21 client's moderation manager
        // registers only 22, "ModerationUnkick" and 23
        // (RecNet.Runtime/FPIBGPIAOBI.txt:310, :323, :335) — there is no handler
        // for 24, so that push landed nowhere and the banned player kept playing
        // until their next login. The 22 handler reads Msg.Reason as a
        // ModerationBlockDetail, which ModerationKickPayload already produces.
        await notifications.KickPlayerAsync(
            target,
            // `reason` is the numeric report category; DisplayReason is the
            // moderator's text and is what the ban dialog shows.
            string.IsNullOrWhiteSpace(displayReason)
                ? "You have been banned from this room."
                : displayReason.Trim(),
            isBan: true);
        return Ok(new { success = true, error = "" });
    }

    private async Task<bool> IsCallerAdminOrDevAsync(long callerId) =>
        await db.Players
            .Where(p => p.Id == callerId)
            .Select(p => p.IsAdmin || p.IsDeveloper)
            .FirstOrDefaultAsync();
}
