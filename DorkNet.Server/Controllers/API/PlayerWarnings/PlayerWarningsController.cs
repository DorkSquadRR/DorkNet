using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;

namespace DorkNet.Server.Controllers.API.PlayerWarnings;

[ApiController]
[Authorize]
public class PlayerWarningsController(DorkNetDbContext db) : ControllerBase
{
    [HttpGet("api/playerwarnings")]
    public async Task<IActionResult> Warnings()
    {
        var me = this.RequireCurrentPlayerId();
        var rows = await db.PlayerSettings
            .Where(s => s.PlayerId == me && s.Key.StartsWith("warning:"))
            .OrderByDescending(s => s.Id)
            .ToListAsync();
        return Ok(rows
            .Select(ToWarningWire)
            .Where(w => !w.Acknowledged)
            .ToList());
    }

    /// <summary>Moderator issues a warning against a player. The 2023-03-21
    /// client POSTs this (verb register <c>rdx=2</c> at
    /// RecNet.Runtime/FPIBGPIAOBI.txt:3455) with the form keys
    /// <c>WarnedPlayerId</c>, <c>ReportCategory</c>, <c>DisplayReason</c> and
    /// <c>ModeratorNote</c> (:3468-3520), matching the issuing method
    /// <c>FGLDKEJLAKB&lt;DJMHAFPGLLN&gt; OEJIMKKDMBI(Int32, BDOGOIGCKMK,
    /// String, String)</c> (:3102).
    ///
    /// Only GET was registered here, so every warning the moderator tools
    /// tried to create came back 405 and silently failed.</summary>
    [HttpPost("api/playerwarnings")]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> CreateWarning(
        [FromForm(Name = "WarnedPlayerId")] long? warnedPlayerId,
        [FromForm(Name = "ReportCategory")] int? reportCategory,
        [FromForm(Name = "DisplayReason")] string? displayReason,
        [FromForm(Name = "ModeratorNote")] string? moderatorNote)
    {
        var me = this.RequireCurrentPlayerId();

        var moderator = await db.Players
            .Where(p => p.Id == me)
            .Select(p => (bool?)(p.IsAdmin || p.IsDeveloper || p.IsCommunityTeam))
            .FirstOrDefaultAsync();
        if (moderator != true)
            return Ok(new { Success = false, Message = "not_authorized" });

        var target = warnedPlayerId
                     ?? (long.TryParse(Request.Query["WarnedPlayerId"].FirstOrDefault(), out var q) ? q : 0);
        if (target <= 0) return Ok(new { Success = false, Message = "missing_player" });
        if (!await db.Players.AnyAsync(p => p.Id == target))
            return Ok(new { Success = false, Message = "player_not_found" });

        // Same "warning:{id}|message|category|createdAt|acknowledged" layout the
        // GET list handler parses in ToWarningWire.
        var reason = (displayReason ?? string.Empty).Trim();
        var note = (moderatorNote ?? string.Empty).Trim();
        var message = string.IsNullOrEmpty(note) ? reason : $"{reason} ({note})";
        var id = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");

        db.PlayerSettings.Add(new DorkNet.Server.Data.Entities.PlayerSettingEntity
        {
            PlayerId = target,
            Key = $"warning:{id}",
            Value = string.Join('|',
                message.Replace('|', '/'),
                (reportCategory ?? 0).ToString(),
                DateTime.UtcNow.ToString("O"),
                "false"),
        });
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Message = string.Empty, WarningId = id });
    }

    /// <summary>The 2023-03-21 client's acknowledge call takes NO arguments —
    /// the issuing method is <c>FGLDKEJLAKB&lt;DJMHAFPGLLN&gt; DOFADJPFBMK()</c>
    /// (FPIBGPIAOBI.txt:3524) and it POSTs an empty body (:3590-3592). It means
    /// "acknowledge my pending warning".
    ///
    /// Demanding a warningId made every call 400, so the modal was never marked
    /// read and re-appeared on each login. With no id supplied we now
    /// acknowledge the newest unacknowledged warning, and acknowledging nothing
    /// is a success rather than an error.</summary>
    [HttpPost("api/playerwarnings/acknowledge")]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> Acknowledge([FromForm] string? warningId)
    {
        var me = this.RequireCurrentPlayerId();
        var id = warningId ?? Request.Query["warningId"].FirstOrDefault() ?? Request.Query["id"].FirstOrDefault();

        DorkNet.Server.Data.Entities.PlayerSettingEntity? row;
        if (string.IsNullOrWhiteSpace(id))
        {
            // Acknowledged state lives inside the packed Value string, so the
            // filter has to run client-side after materialising.
            var rows = await db.PlayerSettings
                .Where(s => s.PlayerId == me && s.Key.StartsWith("warning:"))
                .OrderByDescending(s => s.Id)
                .ToListAsync();
            row = rows.FirstOrDefault(s => !ToWarningWire(s).Acknowledged);
            if (row is null) return Ok(new { Success = true, Message = string.Empty });
        }
        else
        {
            row = await db.PlayerSettings
                .FirstOrDefaultAsync(s => s.PlayerId == me && s.Key == $"warning:{id}");
            if (row is null) return Ok(new { Success = true, Message = string.Empty });
        }

        var parts = row.Value.Split('|');
        row.Value = string.Join('|', parts.Take(3).Append("true"));
        await db.SaveChangesAsync();
        return Ok(new { Success = true, Message = string.Empty, WarningId = row.Key["warning:".Length..] });
    }

    private static WarningWire ToWarningWire(DorkNet.Server.Data.Entities.PlayerSettingEntity row)
    {
        var parts = row.Value.Split('|');
        return new WarningWire
        {
            WarningId = row.Key["warning:".Length..],
            Message = parts.ElementAtOrDefault(0) ?? string.Empty,
            Category = int.TryParse(parts.ElementAtOrDefault(1), out var category) ? category : 0,
            CreatedAt = parts.ElementAtOrDefault(2) ?? string.Empty,
            Acknowledged = bool.TryParse(parts.ElementAtOrDefault(3), out var acknowledged) && acknowledged,
        };
    }

    private sealed class WarningWire
    {
        public string WarningId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int Category { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public bool Acknowledged { get; set; }
    }
}
