using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;

namespace DorkNet.Server.Controllers.API.SubscriptionSeasons;

[ApiController]
public class SubscriptionSeasonsController(DorkNetDbContext db) : ControllerBase
{
    [HttpGet("api/subscriptionseasons/v1/seasons/current")]
    public async Task<IActionResult> Current()
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1);
        var activeSubscribers = await db.Subscriptions.CountAsync();
        var rewards = await db.StoreItems
            .Where(i => i.IsActive)
            .OrderBy(i => i.Id)
            .Take(10)
            .Select(i => new
            {
                RewardId = i.Id,
                i.Slug,
                Name = i.DisplayName,
                i.Category,
                i.ImageName,
            })
            .ToListAsync();
        return Ok(new
        {
            SeasonId = $"{start:yyyyMM}",
            Name = $"Season {start:yyyy.MM}",
            StartAt = start,
            EndAt = end,
            ActiveSubscriberCount = activeSubscribers,
            Rewards = rewards,
        });
    }
}
