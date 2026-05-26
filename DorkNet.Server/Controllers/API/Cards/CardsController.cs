using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;

namespace DorkNet.Server.Controllers.API.Cards;

/// <summary>
/// api.{rec.net,localhost}/api/cards/v1/all — home-screen card panel
/// (announcements, promos, daily-bonus tiles). Returns global cards
/// PLUS any cards targeted at the caller, sorted by Priority then
/// CreatedAt. Expired cards filtered out server-side.
/// </summary>
[ApiController]
public class CardsController(DorkNetDbContext db) : ControllerBase
{
    [HttpGet("api/cards/v1/all")]
    public async Task<IActionResult> All()
    {
        var pid = this.CurrentPlayerId();
        var now = DateTime.UtcNow;
        var rows = await db.Cards
            .Where(c => (c.PlayerId == null || c.PlayerId == pid) &&
                        (c.ExpiresAt == null || c.ExpiresAt > now))
            .OrderByDescending(c => c.Priority).ThenByDescending(c => c.CreatedAt)
            .Take(50)
            .Select(c => new
            {
                c.Id,
                c.Title,
                c.Body,
                c.ImageName,
                c.ActionUrl,
                c.Category,
                c.CreatedAt,
            })
            .ToListAsync();
        return Ok(rows);
    }
}
