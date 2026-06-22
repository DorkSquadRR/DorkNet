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

    [HttpPost("api/playerwarnings/acknowledge")]
    public async Task<IActionResult> Acknowledge([FromForm] string? warningId)
    {
        var me = this.RequireCurrentPlayerId();
        var id = warningId ?? Request.Query["warningId"].FirstOrDefault() ?? Request.Query["id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id)) return BadRequest("missing_warning");
        var row = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == me && s.Key == $"warning:{id}");
        if (row is null) return NotFound();
        var parts = row.Value.Split('|');
        row.Value = string.Join('|', parts.Take(3).Append("true"));
        await db.SaveChangesAsync();
        return Ok(new { Success = true, WarningId = id });
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
