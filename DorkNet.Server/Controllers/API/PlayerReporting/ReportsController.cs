using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.PlayerReporting;

/// <summary>
/// api.rec.net/api/PlayerReporting/v1/* — real player report flow.
/// The 2020 client's "report player" UI in the watch posts here.
/// Form fields the watch sends (per the in-game send_report dialog):
///   <c>TargetPlayerId</c> — int account id of the offender
///   <c>Type</c>           — RoomieReportCategory enum (1..5)
///   <c>Message</c>        — free-form description (capped at 1000)
///   <c>GameSessionId</c>  — optional, current session for context
///   <c>RoomId</c>          — optional, room the offence happened in
///
/// Anyone authenticated can file a report. Admins read the queue
/// from <c>GET api/admin/v1/reports</c>.
/// </summary>
[ApiController]
[Authorize]
public class ReportsController(DorkNetDbContext db, ILogger<ReportsController> logger) : ControllerBase
{
    [HttpGet("api/PlayerReporting/v1/voteToKickReasons")]
    [AllowAnonymous]
    public IActionResult VoteToKickReasons() => Ok(new[]
    {
        new { ReportCategory = 1, Reason = "Harassment" },
        new { ReportCategory = 2, Reason = "Inappropriate behavior" },
        new { ReportCategory = 3, Reason = "Cheating" },
        new { ReportCategory = 4, Reason = "Spam" },
        new { ReportCategory = 5, Reason = "Other" },
    });

    [HttpPost("api/PlayerReporting/v1/report")]
    [HttpPost("api/PlayerReporting/v1/playerreport")]
    [HttpPost("api/PlayerReporting/v2/report")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> SubmitReport(
        [FromForm(Name = "TargetPlayerId")] long? targetPlayerId,
        [FromForm(Name = "Type")] int type = 0,
        [FromForm(Name = "Message")] string? message = null,
        [FromForm(Name = "GameSessionId")] long gameSessionId = 0,
        [FromForm(Name = "RoomId")] long roomId = 0)
    {
        if (targetPlayerId is not long target || target <= 0)
            return BadRequest(new { success = false, error = "missing_target" });

        var reporter = this.RequireCurrentPlayerId();
        if (reporter == target)
            return BadRequest(new { success = false, error = "cannot_report_self" });

        // Cap message length to match the watch UI's enforcement +
        // the column max — anything longer is almost certainly junk.
        var trimmed = (message ?? string.Empty).Trim();
        if (trimmed.Length > 1000) trimmed = trimmed[..1000];

        db.Reports.Add(new ReportEntity
        {
            ReporterPlayerId = reporter,
            TargetPlayerId = target,
            Category = type,
            Message = trimmed,
            GameSessionId = gameSessionId,
            RoomId = roomId,
        });
        await db.SaveChangesAsync();

        return Ok(new { success = true, error = "" });
    }

    /// <summary>GET <c>api/PlayerReporting/v2/detail?playerId={id}</c>
    /// — moderator detail panel. Lists recent reports against the
    /// target player. Public — admin-only mutations are elsewhere.</summary>
    [HttpGet("api/PlayerReporting/v2/detail")]
    public async Task<IActionResult> ReportDetail(
        [FromQuery] long? playerId, [FromQuery] long? accountId)
    {
        var pid = playerId ?? accountId ?? 0;
        if (pid <= 0) return Ok(new { PlayerId = 0L, OpenReports = 0, RecentReports = Array.Empty<object>() });
        var reports = await db.Reports
            .Where(r => r.TargetPlayerId == pid)
            .OrderByDescending(r => r.CreatedAt)
            .Take(20)
            .ToListAsync();
        return Ok(new
        {
            PlayerId = pid,
            OpenReports = reports.Count(r => r.ResolvedAt == null),
            RecentReports = reports.Select(r => new
            {
                r.Id, r.Category, r.Message,
                r.ReporterPlayerId, r.RoomId, r.CreatedAt, r.ResolvedAt,
            }),
        });
    }

    public sealed class V2BanRequest
    {
        public long PlayerId { get; set; }
        public int? Category { get; set; }
        public string? Reason { get; set; }
        public DateTime? Until { get; set; }
    }

    /// <summary>POST <c>api/PlayerReporting/v2/ban</c> — admin-only.
    /// Sets <see cref="PlayerEntity.BannedUntil"/> + appends a
    /// resolved <see cref="ReportEntity"/> for the audit trail.</summary>
    [HttpPost("api/PlayerReporting/v2/ban")]
    [Authorize]
    public async Task<IActionResult> V2Ban([FromBody] V2BanRequest req)
    {
        var me = this.RequireCurrentPlayerId();
        if (req.PlayerId <= 0) return BadRequest(new { error = "missing playerId" });

        var isAdmin = await db.Players
            .Where(p => p.Id == me)
            .Select(p => p.IsAdmin)
            .FirstOrDefaultAsync();
        if (!isAdmin) return Forbid();

        var target = await db.Players.FirstOrDefaultAsync(p => p.Id == req.PlayerId);
        if (target is null) return NotFound();
        target.BannedUntil = req.Until ?? DateTime.UtcNow.AddYears(100);
        var reason = req.Reason ?? string.Empty;
        if (reason.Length > 1000) reason = reason[..1000];
        db.Reports.Add(new ReportEntity
        {
            ReporterPlayerId = me,
            TargetPlayerId = req.PlayerId,
            Category = req.Category ?? 5,
            Message = reason,
            ResolvedAt = DateTime.UtcNow,
            ResolverAdminId = me,
            ResolutionNote = "v2/ban",
        });
        await db.SaveChangesAsync();
        return Ok(new { Banned = true, target.Id, target.BannedUntil });
    }

    public sealed class HileRequest
    {
        public string? Detail { get; set; }
        public string? Body { get; set; }
        public string? Message { get; set; }
        public int? Type { get; set; }
        public long? GameSessionId { get; set; }
    }

    /// <summary>POST <c>api/PlayerReporting/v1/hile</c> — anti-cheat
    /// heartbeat. Stored as a <see cref="BugReportEntity"/> tagged
    /// <c>hile</c> so admins can audit the stream.</summary>
    [HttpPost("api/PlayerReporting/v1/hile")]
    public async Task<IActionResult> Hile()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            return await SaveHileAsync(
                form["Message"].FirstOrDefault() ??
                form["Detail"].FirstOrDefault() ??
                form["Body"].FirstOrDefault(),
                int.TryParse(form["Type"].FirstOrDefault(), out var t) ? t : (int?)null,
                long.TryParse(form["GameSessionId"].FirstOrDefault(), out var g) ? g : (long?)null);
        }

        HileRequest? body = null;
        try
        {
            body = await JsonSerializer.DeserializeAsync<HileRequest>(
                Request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException) { }

        return await SaveHileAsync(
            body?.Message ?? body?.Detail ?? body?.Body,
            body?.Type,
            body?.GameSessionId);
    }

    /// <summary>POST <c>api/PlayerReporting/v1/log</c> — bare client
    /// telemetry log. Persisted as a low-priority BugReport for admin
    /// visibility but never acted on automatically.</summary>
    [HttpPost("api/PlayerReporting/v1/log")]
    public async Task<IActionResult> ReportLog()
    {
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            await SaveHileAsync(
                form["Message"].FirstOrDefault() ?? form["Body"].FirstOrDefault(),
                null, null);
        }
        return Ok(new { success = true, error = "" });
    }

    private async Task<IActionResult> SaveHileAsync(string? rawDetail, int? type, long? gameSessionId)
    {
        var pid = this.CurrentPlayerId();
        if (pid is not long me) return HileResult();
        var detail = (rawDetail ?? string.Empty).Trim();
        if (type is int hileType)
            detail = detail.Length == 0 ? $"Type={hileType}" : $"Type={hileType}; {detail}";
        if (detail.Length > 4000) detail = detail[..4000];
        db.BugReports.Add(new BugReportEntity
        {
            ReporterPlayerId = me,
            Title = "hile heartbeat",
            Body = detail,
            GameSessionId = gameSessionId ?? 0,
            Category = "hile",
        });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (SqliteErrors.IsBusy(ex))
        {
            db.ChangeTracker.Clear();
            logger.LogWarning(ex, "[hile] dropping low-priority hile heartbeat because sqlite is busy");
        }
        return HileResult();
    }

    private static IActionResult HileResult() => new ContentResult
    {
        Content = "true",
        ContentType = "application/json",
        StatusCode = StatusCodes.Status200OK,
    };
}
