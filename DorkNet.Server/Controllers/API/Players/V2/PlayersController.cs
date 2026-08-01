using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DorkNet.Models.Players;
using DorkNet.Server.Auth;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Players.V2;

[ApiController]
[Route("api/[controller]/v2")]
[Authorize]
public class PlayersController(PlayerService playerService, NotificationService notificationService) : ControllerBase
{
    private long CurrentPlayerId => this.RequireCurrentPlayerId();

    [HttpGet("me")]
    public async Task<ActionResult<PlayerProfile>> GetSelf()
    {
        var player = await playerService.GetByIdAsync(CurrentPlayerId);
        if (player is null) return NotFound();
        return Ok(PlayerService.ToProfile(player));
    }

    // The 2023 client PUTs this right after setting an email; GET-only meant a
    // 405 on every contact-preferences write.
    [HttpPut("/api/players/v4/current/contact")]
    [HttpGet("/api/players/v4/current/contact")]
    public async Task<IActionResult> CurrentContact()
    {
        var player = await playerService.GetByIdAsync(CurrentPlayerId);
        if (player is null) return NotFound();
        return Ok(new
        {
            AccountId = (int)player.Id,
            Email = player.Email ?? string.Empty,
            Phone = player.Phone ?? string.Empty,
            CanReceiveInvites = player.CanReceiveInvites,
        });
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<PlayerProfile>> GetById(long id)
    {
        var player = await playerService.GetByIdAsync(id);
        if (player is null) return NotFound();
        return Ok(PlayerService.ToProfile(player));
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<PlayerProfile>>> Search([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Ok(new List<PlayerProfile>());
        var results = await playerService.SearchAsync(query);
        return Ok(results.Select(PlayerService.ToProfile).ToList());
    }

    [HttpPost("displayName")]
    public async Task<ActionResult<PlayerProfile>> UpdateDisplayName([FromForm] string? DisplayName)
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
            return BadRequest("DisplayName is required");

        var updated = await playerService.UpdateDisplayNameAsync(CurrentPlayerId, DisplayName);
        if (!updated) return NotFound();

        var player = await playerService.GetByIdAsync(CurrentPlayerId);
        await notificationService.AccountChanged(player!);
        return Ok(PlayerService.ToProfile(player!));
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<PlayerProfile>> UpdateProfile(long id, [FromBody] UpdateProfileRequest req)
    {
        if (id != CurrentPlayerId) return Forbid();

        if (req.DisplayName is not null)
            await playerService.UpdateDisplayNameAsync(id, req.DisplayName);

        if (req.Bio is not null)
            await playerService.UpdateBioAsync(id, req.Bio);

        var player = await playerService.GetByIdAsync(id);
        await notificationService.AccountChanged(player!);
        return Ok(PlayerService.ToProfile(player!));
    }
}
