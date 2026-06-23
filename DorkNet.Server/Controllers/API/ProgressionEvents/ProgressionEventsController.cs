using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.ProgressionEvents;

[ApiController]
public class ProgressionEventsController(DorkNetDbContext db, ServerSettingsService serverSettings) : ControllerBase
{
    [HttpGet("api/progressionEvents")]
    [Authorize]
    public async Task<IActionResult> Progress()
    {
        var me = this.RequireCurrentPlayerId();
        var rows = await db.ObjectiveProgress
            .Where(o => o.PlayerId == me && o.Key.StartsWith("progressionEvent:"))
            .OrderByDescending(o => o.ClearedAt)
            .Take(100)
            .Select(o => new
            {
                EventKey = o.Key,
                o.IsCompleted,
                o.ClearedAt,
            })
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet("api/progressionEvents/event/{eventId:long}")]
    [AllowAnonymous]
    public async Task<IActionResult> Event(long eventId)
    {
        var activeId = CurrentProgressionEventId();
        if (eventId != activeId)
            return NotFound();

        var weekly = await serverSettings.GetWeeklyChallengesAsync();
        var (start, end) = CurrentWeekWindow();
        return Ok(new
        {
            ProgressionEventId = activeId,
            Name = "Weekly Progression",
            Rewards = new[]
            {
                new
                {
                    ProgressionEventRewardId = activeId * 1000L,
                    GiftDropId = weekly.Reward.GiftDropId != 0 ? weekly.Reward.GiftDropId : activeId,
                    ImageName = string.Empty,
                    Xp = weekly.Reward.Xp,
                    RewardIndex = 0,
                    IsBonus = false,
                },
            },
            KeepsakeRoomLists = Array.Empty<object>(),
            StartTime = start,
            EndTime = end,
            CollectionEndTime = end,
            UsesBoost = false,
            BoostDailyGameplayMinutesLimit = 0,
            BoostXpMultiplier = 1.0,
            PurchasableXpBoostId = (Guid?)null,
            ActiveExperiment = string.Empty,
            ChallengesIconImageName = string.Empty,
            RewardsPipImageName = string.Empty,
            EventInfoImageName = string.Empty,
        });
    }

    [HttpGet("api/progressionEvents/active")]
    [AllowAnonymous]
    public IActionResult Active() => Ok(CurrentProgressionEventId());

    private static long CurrentProgressionEventId()
    {
        var (start, _) = CurrentWeekWindow();
        return long.Parse(start.ToString("yyyyMMdd"));
    }

    private static (DateTime Start, DateTime End) CurrentWeekWindow()
    {
        var start = DateTime.UtcNow.Date.AddDays(-(int)DateTime.UtcNow.DayOfWeek);
        return (start, start.AddDays(7));
    }
}
