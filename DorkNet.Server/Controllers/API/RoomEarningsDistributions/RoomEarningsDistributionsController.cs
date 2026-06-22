using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;

namespace DorkNet.Server.Controllers.API.RoomEarningsDistributions;

[ApiController]
[Authorize]
public class RoomEarningsDistributionsController(DorkNetDbContext db) : ControllerBase
{
    [HttpGet("api/roomEarningsDistributions")]
    [HttpGet("api/roomEarningsDistributions/v1/earningsDistribution")]
    public async Task<IActionResult> EarningsDistribution([FromQuery] long? roomId = null)
    {
        var me = this.RequireCurrentPlayerId();
        var rooms = db.Rooms.Where(r => r.CreatorPlayerId == me);
        if (roomId is long rid && rid > 0)
            rooms = rooms.Where(r => r.Id == rid);

        var roomRows = await rooms
            .Select(r => new { r.Id, r.Name })
            .ToListAsync();
        var roomIds = roomRows.Select(r => r.Id).ToList();
        var earnings = await db.RoomKeyPurchases
            .Join(db.RoomKeys, p => p.RoomKeyId, k => k.Id, (p, k) => new { p, k })
            .Where(x => roomIds.Contains(x.k.RoomId))
            .GroupBy(x => x.k.RoomId)
            .Select(g => new
            {
                RoomId = g.Key,
                Gross = g.Sum(x => x.p.PaidPrice),
                PurchaseCount = g.Count(),
            })
            .ToListAsync();

        var byRoom = earnings.ToDictionary(e => e.RoomId);
        return Ok(roomRows.Select(room =>
        {
            byRoom.TryGetValue(room.Id, out var value);
            var gross = value?.Gross ?? 0;
            return new
            {
                RoomId = room.Id,
                room.Name,
                GrossEarnings = gross,
                NetEarnings = gross,
                PurchaseCount = value?.PurchaseCount ?? 0,
                CurrencyType = 2,
                UpdatedAt = DateTime.UtcNow,
            };
        }).ToList());
    }
}
