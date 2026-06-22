using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DorkNet.Server.Controllers.API.GameSight;

[ApiController]
[AllowAnonymous]
public class GameSightController : ControllerBase
{
    [HttpPost("/api/gamesight/event")]
    [HttpPost("/data/event")]
    [HttpPost("/data/heartbeat")]
    public IActionResult Event() => Ok(new { Success = true });
}
