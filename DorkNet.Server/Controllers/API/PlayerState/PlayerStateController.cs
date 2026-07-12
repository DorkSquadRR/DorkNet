using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Notification;
using DorkNet.Models.Players;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.PlayerState;

/// <summary>
/// api.rec.net endpoints that return player-state objects whose Deserialize
/// methods do strict required-key reads via Util.GetKey&lt;T&gt;. The previous
/// catch-all returned {} or {"Success":true} for these, which crashed the
/// client with KeyNotFoundException at:
///   RecNet.Progression.Deserialize          → "PlayerId", "Level", "XP"
///   RecNet.Reputation.Deserialize           → "AccountId", "Noteriety" (sic), Cheer*, Sub*
///   RecNet.ModerationBlockDetail.Deserialize → "ReportCategory", "Duration", "GameSessionId", "Message"
///
/// Each endpoint here returns the minimum-viable shape (zeros / empty
/// strings) that satisfies every required key. Real values can be plugged
/// in later when persistence is added for these.
///
/// Specific routes here win over the api/PlayerReporting/{*path} and
/// api.rec.net catch-alls.
/// </summary>
[ApiController]
public class PlayerStateController(
    DorkNetDbContext db,
    SystemNotificationService systemNotifications,
    LevelService level) : ControllerBase
{
    // RecNet.Progressions.GetProgressionById / GetMyProgression — real
    // values from PlayerEntity.Level / .XP. Default 1/0 when the
    // account hasn't been seen yet (synthetic ids passed in queries).
    [HttpGet("api/players/v1/progression/{accountId:long}")]
    [HttpGet("api/players/v2/progression/{accountId:long}")]
    public async Task<ActionResult<Progression>> GetProgression(long accountId)
    {
        var p = await db.Players
            .Where(p => p.Id == accountId)
            .Select(p => new { p.Id, p.Level, p.XP })
            .FirstOrDefaultAsync();
        return Ok(new Progression
        {
            PlayerId = (int)(p?.Id ?? accountId),
            Level = p?.Level ?? 1,
            XP = p?.XP ?? 0,
        });
    }

    [HttpPost("api/players/v1/progression/bulk")]
    [HttpPost("api/players/v2/progression/bulk")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<ActionResult<List<Progression>>> GetProgressionBulk(
        [FromForm(Name = "Ids")] string? ids)
    {
        var idList = ParseIds(ids).Select(i => (long)i).ToList();
        return Ok(await BuildProgressionBulkAsync(idList));
    }

    [HttpGet("api/players/v2/progression/bulk")]
    public async Task<ActionResult<List<Progression>>> GetProgressionBulkV2Get()
    {
        var idList = ParseQueryIds().ToList();
        return Ok(await BuildProgressionBulkAsync(idList));
    }

    private async Task<List<Progression>> BuildProgressionBulkAsync(IReadOnlyCollection<long> idList)
    {
        if (idList.Count == 0) return new List<Progression>();
        var rows = await db.Players
            .Where(p => idList.Contains(p.Id))
            .Select(p => new Progression
            {
                PlayerId = (int)p.Id,
                Level = p.Level,
                XP = p.XP,
            })
            .ToListAsync();
        // Synthesise zeros for any ids the watch asked about that
        // don't exist in our DB — keeps array length stable.
        var found = rows.Select(r => r.PlayerId).ToHashSet();
        rows.AddRange(idList
            .Where(id => !found.Contains((int)id))
            .Select(id => new Progression { PlayerId = (int)id }));
        return rows;
    }

    // RecNet.Reputations.GetReputationById — aggregates CheerEntity
    // by category. Each Cheer* count is the sum of distinct cheers
    // received from other players in that category.
    [HttpGet("api/playerReputation/v1/{accountId:long}")]
    [HttpGet("api/playerReputation/v2/{accountId:long}")]
    public async Task<ActionResult<Reputation>> GetReputation(long accountId) =>
        Ok(await BuildReputationAsync(accountId));

    [HttpPost("api/playerReputation/v1/bulk")]
    [HttpPost("api/playerReputation/v2/bulk")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<ActionResult<List<Reputation>>> GetReputationBulk(
        [FromForm(Name = "Ids")] string? ids)
    {
        var idList = ParseIds(ids).Select(i => (long)i).ToList();
        return Ok(await BuildReputationBulkAsync(idList));
    }

    [HttpGet("api/playerReputation/v2/bulk")]
    public async Task<ActionResult<List<Reputation>>> GetReputationBulkV2Get()
    {
        var idList = ParseQueryIds().ToList();
        return Ok(await BuildReputationBulkAsync(idList));
    }

    private async Task<List<Reputation>> BuildReputationBulkAsync(IReadOnlyCollection<long> idList)
    {
        var result = new List<Reputation>(idList.Count);
        foreach (var id in idList)
            result.Add(await BuildReputationAsync(id));
        return result;
    }

    /// <summary>POST api/playerReputation/v1/cheer/{playerId} — cheer
    /// another player. Idempotent on (caller, target, type) so
    /// repeat-clicking the same category doesn't inflate the count.
    /// Pushes a presence-update notification so the target's watch
    /// refreshes their cheer total.</summary>
    [HttpPost("api/playerReputation/v1/cheer/{playerId:long}")]
    [Authorize]
    public async Task<ActionResult> CheerPlayer(long playerId, [FromQuery] int type = 0)
    {
        var me = this.RequireCurrentPlayerId();
        if (me == playerId) return BadRequest("cannot_cheer_self");

        var existing = await db.Cheers.FirstOrDefaultAsync(c =>
            c.FromPlayerId == me && c.TargetPlayerId == playerId &&
            c.TargetRoomId == 0 && c.Type == type);
        if (existing is not null) return Ok(new { already_cheered = true });

        db.Cheers.Add(new CheerEntity
        {
            FromPlayerId = me,
            TargetPlayerId = playerId,
            Type = type,
        });
        await db.SaveChangesAsync();

        // Reward the cheered player with a small XP bump.
        await level.AwardXpAsync(playerId, LevelService.CheerReceivedXp, $"cheer_from:{me}");

        // Real PlayerCheer notification (persisted + pushed as a Message),
        // replacing the old blank-account SubscriptionUpdateProfile misuse.
        await systemNotifications.SendAsync(playerId,
            SystemNotificationService.MessageType.PlayerCheer, fromPlayerId: me);
        return Ok();
    }

    /// <summary>POST api/rooms/v1/cheer/{roomId} — cheer a room.
    /// Same idempotency rules as player cheers; bumps
    /// <see cref="RoomEntity.CheerCount"/> as a denormalised counter
    /// so the watch's "Hot" rooms feed doesn't have to JOIN-aggregate
    /// on every request.</summary>
    [HttpPost("api/rooms/v1/cheer/{roomId:long}")]
    [Authorize]
    public async Task<ActionResult> CheerRoom(long roomId, [FromQuery] int type = 0)
    {
        var me = this.RequireCurrentPlayerId();

        var existing = await db.Cheers.FirstOrDefaultAsync(c =>
            c.FromPlayerId == me && c.TargetRoomId == roomId &&
            c.TargetPlayerId == 0 && c.Type == type);
        if (existing is not null) return Ok(new { already_cheered = true });

        db.Cheers.Add(new CheerEntity
        {
            FromPlayerId = me,
            TargetRoomId = roomId,
            Type = type,
        });
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is not null)
        {
            room.CheerCount += 1;
            // Cheers feed the Hot-tab ranking — weight 5x a first
            // visit so the "Trending" feed reflects what players
            // actually like, not just what they passed through.
            room.HotScore += 10.0;
        }
        await db.SaveChangesAsync();
        return Ok();
    }

    private async Task<Reputation> BuildReputationAsync(long accountId)
    {
        var rows = await db.Cheers
            .Where(c => c.TargetPlayerId == accountId)
            .GroupBy(c => c.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();
        int Count(int type) => rows.FirstOrDefault(r => r.Type == type)?.Count ?? 0;

        var subscriberCount = await db.Subscriptions
            .CountAsync(s => s.TargetPlayerId == accountId);
        var subscribedCount = await db.Subscriptions
            .CountAsync(s => s.SubscriberPlayerId == accountId);

        // SelectedCheer is the pinned badge category the player chose
        // via PlayerCheerController.SetSelectedCheer. Stored as a
        // PlayerSettingEntity with key "SelectedCheer" and Value = the
        // category int as string. Null when unset — UI just shows no
        // pinned badge.
        var selected = await db.PlayerSettings
            .Where(s => s.PlayerId == accountId && s.Key == "SelectedCheer")
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        int? selectedCheer = int.TryParse(selected, out var sc) ? sc : null;

        return new Reputation
        {
            AccountId = (int)accountId,
            Notoriety = rows.Sum(r => r.Count),
            CheerGeneral = Count(0),
            CheerHelpful = Count(1),
            CheerGreatHost = Count(2),
            CheerSportsman = Count(3),
            CheerCreative = Count(4),
            CheerCredit = Count(5),
            SelectedCheer = selectedCheer,
            SubscriberCount = subscriberCount,
            SubscribedCount = subscribedCount,
        };
    }

    /// <summary>
    /// "1,2,3" → [1, 2, 3]. The client formats Ids as a comma-separated
    /// string when calling AddField on the form. Tolerant of empty input
    /// and whitespace.
    /// </summary>
    private static IEnumerable<int> ParseIds(string? ids) =>
        string.IsNullOrWhiteSpace(ids)
            ? Enumerable.Empty<int>()
            : ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Select(s => int.TryParse(s, out var v) ? v : 0)
                 .Where(v => v != 0);

    private IEnumerable<long> ParseQueryIds()
    {
        foreach (var (key, values) in Request.Query)
        {
            if (long.TryParse(key, out var keyId) && keyId > 0)
                yield return keyId;
            foreach (var value in values)
            {
                if (long.TryParse(value, out var valueId) && valueId > 0)
                    yield return valueId;
                foreach (var part in value.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (long.TryParse(part, out var partId) && partId > 0)
                        yield return partId;
            }
        }
    }

    // RecNet.PlayerReporting.GetModerationBlockDetails
    // IMPORTANT: returns a single object, not an array. Defaults indicate
    // "user is not currently blocked / kicked".
    // ASP.NET Core route matching is case-insensitive, so a single attribute
    // covers both "PlayerReporting" and "playerreporting" callers.
    [HttpGet("api/PlayerReporting/v1/moderationBlockDetails")]
    public ActionResult<ModerationBlockDetail> GetModerationBlockDetails() =>
        Ok(new ModerationBlockDetail());
}
