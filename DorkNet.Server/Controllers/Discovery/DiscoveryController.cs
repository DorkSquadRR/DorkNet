using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Discovery;

/// <summary>
/// discovery.{rec.net,localhost} — the watch's room-discovery surface
/// (rooms tab "For You" feed). Backed by the same RoomService used
/// elsewhere so what's hot here matches the main browser.
/// </summary>
[ApiController]
public class DiscoveryController(RoomService rooms) : ControllerBase
{
    [HttpGet("/discover/v2/rooms")]
    public async Task<IActionResult> GetRooms([FromQuery] string? tag, [FromQuery] int take = 50)
        => Ok((await rooms.HotAsync(tag, Math.Clamp(take, 1, 100)))
            .Select(RoomService.ToWireRoom).ToList());

    [HttpGet("/discover/v2/feed")]
    public async Task<IActionResult> GetFeed([FromQuery] int take = 50)
        => Ok((await rooms.HotAsync(null, Math.Clamp(take, 1, 100)))
            .Select(RoomService.ToWireRoom).ToList());

    [HttpGet("/discover/v2/featured")]
    public async Task<IActionResult> GetFeatured([FromQuery] int take = 50)
        // Featured = tagged "featured" — same set as the watch's
        // primary "Featured" filter chip.
        => Ok((await rooms.HotAsync("featured", Math.Clamp(take, 1, 100)))
            .Select(RoomService.ToWireRoom).ToList());
}
