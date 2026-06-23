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

    /// <summary>
    /// The current player's progress record for a given progression event.
    /// The 2023 client fetches this during InitialRoomLoad
    /// (<c>api/progressionEvents/record/{id}</c>, where <c>{id}</c> is the
    /// <c>yyyyMMdd</c> event id). A 404 here faults the room-load coroutine
    /// with a NullReferenceException and traps the client in a dorm
    /// matchmaking loop, so we always return a non-null record.
    /// </summary>
    [HttpGet("api/progressionEvents/record/{progressionEventId:long}")]
    [Authorize]
    public async Task<IActionResult> Record(long progressionEventId)
    {
        var me = this.RequireCurrentPlayerId();

        // Count this player's completed progression-event objectives so the
        // record reflects real progress rather than a flat zero.
        var completed = await db.ObjectiveProgress
            .CountAsync(o => o.PlayerId == me
                && o.IsCompleted
                && o.Key.StartsWith("progressionEvent:"));

        return Ok(new
        {
            ProgressionEventId = progressionEventId,
            Xp = 0,
            ClaimedRewardIndex = completed > 0 ? completed - 1 : -1,
            PurchasedXpBoostCount = 0,
            DailyBoostGameplayMinutes = 0,
            XpBoostExpiresAt = (DateTime?)null,
        });
    }

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
