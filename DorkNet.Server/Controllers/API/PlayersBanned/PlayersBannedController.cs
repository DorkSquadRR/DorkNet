using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;

namespace DorkNet.Server.Controllers.API.PlayersBanned;

/// <summary>
/// api.{rec.net,localhost}/api/PlayersBanned/v1/all — current global
/// ban list. The watch's mod panel queries this on open.
/// </summary>
[ApiController]
public class PlayersBannedController(DorkNetDbContext db) : ControllerBase
{
    [HttpGet("api/PlayersBanned/v1/all")]
    public async Task<IActionResult> All()
    {
        var now = DateTime.UtcNow;
        var rows = await db.Players
            .Where(p => p.BannedUntil != null && p.BannedUntil > now)
            .Select(p => new
            {
                PlayerId = (int)p.Id,
                p.Username,
                BannedUntil = p.BannedUntil,
            })
            .ToListAsync();
        return Ok(rows);
    }
}
