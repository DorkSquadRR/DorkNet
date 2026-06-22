using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;

namespace DorkNet.Server.Controllers.API.Events.V3;

[ApiController]
[Route("api/[controller]/v3")]
public class EventsController(DorkNetDbContext db) : ControllerBase
{
    [HttpGet("list")]
    public async Task<ActionResult<List<object>>> GetActiveEvents([FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 100);
        var rows = await db.PlayerEvents
            .Where(e => e.EndsAt > DateTime.UtcNow)
            .OrderBy(e => e.StartsAt)
            .Take(take)
            .Select(e => new
            {
                EventId = e.Id,
                PlayerEventId = e.Id,
                Name = e.Title,
                e.Description,
                StartTime = e.StartsAt,
                EndTime = e.EndsAt,
                CreatorPlayerId = (int)e.CreatorPlayerId,
                e.RoomId,
                e.Capacity,
            })
            .ToListAsync();
        return Ok(rows.Cast<object>().ToList());
    }
}
