using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DorkNet.Models.Auth;
using DorkNet.Server.Auth;
using DorkNet.Server.Compat2018;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Players.V2;

/// <summary>
/// api/players/* for the June-2018 client. This branch is 2018-only, so every
/// endpoint returns the 2018 <see cref="Player2018"/> shape (keys Id/Developer/
/// JuniorProfile/… — NOT the 2020 PlayerProfile's AccountId/IsDeveloper).
///
/// The 2018 client mixes path versions: profile fetch + batch are v1, while
/// search + displayname are v2. We map both. Param names follow the 2018 client
/// (search ?name=, displayname form Name).
/// </summary>
[ApiController]
[Authorize]
public class PlayersController(PlayerService playerService, NotificationService notificationService) : ControllerBase
{
    private long CurrentPlayerId => this.RequireCurrentPlayerId();

    // GET api/players/v1|v2/me
    [HttpGet("/api/players/v1/me")]
    [HttpGet("/api/players/v2/me")]
    public async Task<ActionResult<Player2018>> GetSelf()
    {
        var player = await playerService.GetByIdAsync(CurrentPlayerId);
        if (player is null) return NotFound();
        return Ok(Player2018Mapper.From(player));
    }

    // GET api/players/v1|v2/{id} — primary profile fetch (2018 uses v1).
    [HttpGet("/api/players/v1/{id:long}")]
    [HttpGet("/api/players/v2/{id:long}")]
    public async Task<ActionResult<Player2018>> GetById(long id)
    {
        var player = await playerService.GetByIdAsync(id);
        if (player is null) return NotFound();
        return Ok(Player2018Mapper.From(player));
    }

    // POST api/players/v1/list — batch profile resolution (JSON array of ids).
    [HttpPost("/api/players/v1/list")]
    [HttpPost("/api/players/v2/list")]
    public async Task<ActionResult<List<Player2018>>> GetMany([FromBody] long[]? ids)
    {
        if (ids is null || ids.Length == 0) return Ok(new List<Player2018>());
        var players = await playerService.GetByIdsAsync(ids);
        return Ok(players.Select(Player2018Mapper.From).ToList());
    }

    // GET api/players/v2/search?name= (2018) — also accept ?query= for safety.
    [HttpGet("/api/players/v1/search")]
    [HttpGet("/api/players/v2/search")]
    public async Task<ActionResult<List<Player2018>>> Search(
        [FromQuery] string? name, [FromQuery] string? query)
    {
        var term = !string.IsNullOrWhiteSpace(name) ? name : query;
        if (string.IsNullOrWhiteSpace(term)) return Ok(new List<Player2018>());
        var results = await playerService.SearchAsync(term);
        return Ok(results.Select(Player2018Mapper.From).ToList());
    }

    // POST api/players/v2/displayname (2018 form key "Name"). Also accept
    // "DisplayName" for safety.
    [HttpPost("/api/players/v1/displayname")]
    [HttpPost("/api/players/v2/displayname")]
    public async Task<ActionResult<Player2018>> UpdateDisplayName(
        [FromForm] string? Name, [FromForm] string? DisplayName)
    {
        var newName = !string.IsNullOrWhiteSpace(Name) ? Name : DisplayName;
        if (string.IsNullOrWhiteSpace(newName)) return BadRequest("Name is required");
        if (!await playerService.UpdateDisplayNameAsync(CurrentPlayerId, newName)) return NotFound();
        var player = await playerService.GetByIdAsync(CurrentPlayerId);
        await notificationService.NotifyAsync(CurrentPlayerId,
            Models.Notification.PushNotificationId.SubscriptionUpdateProfile, Player2018Mapper.From(player!));
        return Ok(Player2018Mapper.From(player!));
    }

    // POST api/players/v1/bio (2018 form key "Bio").
    [HttpPost("/api/players/v1/bio")]
    [HttpPost("/api/players/v2/bio")]
    public async Task<ActionResult<Player2018>> UpdateBio([FromForm] string? Bio)
    {
        await playerService.UpdateBioAsync(CurrentPlayerId, Bio ?? string.Empty);
        var player = await playerService.GetByIdAsync(CurrentPlayerId);
        return Ok(Player2018Mapper.From(player!));
    }

    // POST api/players/v1/avoidJuniors (form "AvoidJuniors"). Best-effort:
    // acknowledged so the toggle in the watch settings doesn't 404. (Server
    // doesn't currently filter matchmaking on this flag.)
    [HttpPost("/api/players/v1/avoidJuniors")]
    [HttpPut("/api/players/v1/avoidJuniors")]
    public async Task<ActionResult<Player2018>> AvoidJuniors()
    {
        var player = await playerService.GetByIdAsync(CurrentPlayerId);
        if (player is null) return NotFound();
        return Ok(Player2018Mapper.From(player));
    }
}
