using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Notification;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.PlayerSubscriptions;

/// <summary>
/// api.rec.net/api/playersubscriptions/v1/* — asymmetric follow
/// graph. The <c>my</c> endpoint returns who the caller follows;
/// subscribe/unsubscribe mutate the row directly. Reputation
/// aggregates pull SubscriberCount / SubscribedCount counts from
/// here.
/// </summary>
[ApiController]
[Route("api/[controller]/v1")]
[Authorize]
public class PlayerSubscriptionsController(
    DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    [HttpGet("my")]
    public async Task<ActionResult> My()
    {
        var subs = await db.Subscriptions
            .Where(s => s.SubscriberPlayerId == Me)
            .Select(s => new { s.TargetPlayerId, s.CreatedAt })
            .ToListAsync();
        return Ok(subs);
    }

    [HttpGet("{playerId:long}/subscribers")]
    public async Task<ActionResult> Subscribers(long playerId)
    {
        var subs = await db.Subscriptions
            .Where(s => s.TargetPlayerId == playerId)
            .Select(s => new { s.SubscriberPlayerId, s.CreatedAt })
            .ToListAsync();
        return Ok(subs);
    }

    /// <summary>GET <c>/subscription/top/creators/today</c> — the
    /// subscriptions tab's "top creators" carousel. Client contract
    /// (RecNet.Runtime <c>JDBPOMCAIIG()</c>) is a bare
    /// <c>List&lt;creator&gt;</c> of <c>{ accountId:int, subscriberCount:long,
    /// … }</c>. Ranks creators by total subscriber count. The leading "/"
    /// makes this route absolute, bypassing the class's
    /// <c>api/[controller]/v1</c> prefix.</summary>
    [HttpGet("/subscription/top/creators/today")]
    [AllowAnonymous]
    public async Task<ActionResult> TopCreatorsToday([FromQuery] int? take)
    {
        var limit = Math.Clamp(take ?? 20, 1, 100);
        var top = await db.Subscriptions
            .GroupBy(s => s.TargetPlayerId)
            .Select(g => new { AccountId = g.Key, SubscriberCount = (long)g.Count() })
            .OrderByDescending(x => x.SubscriberCount)
            .Take(limit)
            .ToListAsync();

        // A day-window boundary for "today"'s new-subscriber tallies.
        var since = DateTime.UtcNow.Date;
        var todayByTarget = await db.Subscriptions
            .Where(s => s.CreatedAt >= since)
            .GroupBy(s => s.TargetPlayerId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        // Dictionaries preserve literal key casing (anonymous objects would be
        // forced to camelCase by the server policy); the creator DTO's keys
        // are obfuscated in the dump, so emit both cases to be safe.
        return Ok(top.Select(c =>
        {
            var newToday = todayByTarget.GetValueOrDefault(c.AccountId);
            return new Dictionary<string, object?>
            {
                ["AccountId"] = (int)c.AccountId,
                ["accountId"] = (int)c.AccountId,
                ["SubscriberCount"] = c.SubscriberCount,
                ["subscriberCount"] = c.SubscriberCount,
                ["NewSubscribersToday"] = newToday,
                ["newSubscribersToday"] = newToday,
            };
        }).ToList());
    }

    [HttpPost("subscribe/{targetId:long}")]
    public async Task<ActionResult> Subscribe(long targetId)
    {
        if (targetId == Me) return BadRequest("cannot_subscribe_to_self");

        var existing = await db.Subscriptions.FirstOrDefaultAsync(s =>
            s.SubscriberPlayerId == Me && s.TargetPlayerId == targetId);
        if (existing is not null) return Ok(new { already_subscribed = true });

        db.Subscriptions.Add(new SubscriptionEntity
        {
            SubscriberPlayerId = Me,
            TargetPlayerId = targetId,
        });
        await db.SaveChangesAsync();

        // No real-time push: no client message type for new subscribers, and
        // the old SubscriptionUpdateProfile {Reason:"NewSubscriber"} blob
        // decoded to a blank account ("Orphan player account (0)") without
        // refreshing anything. The subscriber count refreshes on next fetch.
        return Ok();
    }

    [HttpDelete("subscribe/{targetId:long}")]
    [HttpPost("unsubscribe/{targetId:long}")]
    public async Task<ActionResult> Unsubscribe(long targetId)
    {
        var row = await db.Subscriptions.FirstOrDefaultAsync(s =>
            s.SubscriberPlayerId == Me && s.TargetPlayerId == targetId);
        if (row is null) return NotFound();
        db.Subscriptions.Remove(row);
        await db.SaveChangesAsync();
        return Ok();
    }
}
