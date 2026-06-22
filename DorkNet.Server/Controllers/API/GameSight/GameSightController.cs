using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.GameSight;

[ApiController]
[Authorize]
public class GameSightController(DorkNetDbContext db) : ControllerBase
{
    [HttpPost("api/gamesight/event")]
    [AllowAnonymous]
    public async Task<IActionResult> Event([FromBody] object? body)
    {
        var playerId = this.CurrentPlayerId();
        if (playerId is null)
            return Ok(new { Success = true });

        db.ObjectiveProgress.Add(new ObjectiveProgressEntity
        {
            PlayerId = playerId.Value,
            Key = $"gamesight:{Guid.NewGuid():N}",
            IsCompleted = true,
            ClearedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return Ok(new { Success = true });
    }
}
