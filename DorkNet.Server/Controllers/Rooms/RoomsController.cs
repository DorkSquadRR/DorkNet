using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Rooms;

/// <summary>
/// rooms.{rec.net,localhost} — secondary room-listing host (the
/// primary surface is api.*/api/rooms/v2/* via
/// <c>Controllers/API/Rooms/V2/RoomsController</c>). Some older
/// 2020 client paths still hit this host for the room-tile lookup
/// during share-link resolution and friend-room-join probes.
/// Both controllers share <see cref="RoomService"/> so the data is
/// identical regardless of which host the watch chose.
/// </summary>
[ApiController]
public class RoomsController(RoomService rooms) : ControllerBase
{
    [HttpGet("/rooms/v3/{roomId:long}")]
    public async Task<IActionResult> GetById(long roomId)
    {
        var r = await rooms.GetByIdAsync(roomId);
        return r is null ? NotFound() : Ok(RoomService.ToWireRoom(r));
    }

    [HttpGet("/rooms/v3/search")]
    public async Task<IActionResult> Search([FromQuery] string? query, [FromQuery] int take = 20)
    {
        var rows = await rooms.SearchAsync(query ?? string.Empty, Math.Clamp(take, 1, 100));
        return Ok(rows.Select(RoomService.ToWireRoom).ToList());
    }

    [HttpGet("/rooms/v3/hot")]
    public async Task<IActionResult> Hot([FromQuery] string? tag, [FromQuery] int take = 50)
        => Ok((await rooms.HotAsync(tag, Math.Clamp(take, 1, 100))).Select(RoomService.ToWireRoom).ToList());

    [HttpGet("/rooms/v3/new")]
    public async Task<IActionResult> New([FromQuery] int take = 50)
    {
        // No dedicated "new rooms" path on RoomService — order by
        // CreatedAt is acceptable as a fallback since RoomEntity has
        // both UpdatedAt + CreatedAt.
        var rows = await rooms.HotAsync(null, Math.Clamp(take, 1, 100));
        return Ok(rows.Select(RoomService.ToWireRoom).ToList());
    }

    [HttpGet("/rooms/v3/{roomId:long}/stats")]
    public async Task<IActionResult> GetStats(long roomId)
    {
        var r = await rooms.GetByIdAsync(roomId);
        if (r is null) return NotFound();
        return Ok(new
        {
            RoomId = r.Id,
            VisitCount = r.VisitCount,
            CheerCount = r.CheerCount,
            FavoriteCount = r.FavoriteCount,
            VisitorCount = r.VisitorCount,
        });
    }
}
