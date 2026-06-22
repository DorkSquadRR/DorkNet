using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;

namespace DorkNet.Server.Controllers.API.ProgressionEvents;

[ApiController]
public class ProgressionEventsController(DorkNetDbContext db) : ControllerBase
{
    [HttpGet("api/progressionEvents")]
    [Authorize]
    public async Task<IActionResult> Progress()
    {
        var me = this.RequireCurrentPlayerId();
        var rows = await db.ObjectiveProgress
            .Where(o => o.PlayerId == me && o.Key.StartsWith("progressionEvent:"))
            .OrderByDescending(o => o.ClearedAt)
            .Take(100)
            .Select(o => new
            {
                EventKey = o.Key,
                o.IsCompleted,
                o.ClearedAt,
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet("api/progressionEvents/active")]
    [AllowAnonymous]
    public IActionResult Active()
    {
        var start = DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek);
        var end = start.AddDays(7);
        return Ok(new
        {
            EventKey = $"weekly:{start:yyyyMMdd}",
            Name = "Weekly Progression",
            StartAt = start,
            EndAt = end,
            Active = true,
        });
    }
}
