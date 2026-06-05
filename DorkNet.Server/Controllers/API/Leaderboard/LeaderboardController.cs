using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.Leaderboard;

/// <summary>
/// leaderboard.{rec.net,localhost}/leaderboard/* — per-stat-channel
/// leaderboard reads + writes. URLs verified against
/// <c>Cpp2IL_ISIL/.../RecNet/Leaderboards.txt:1934, 2416, 2700</c>:
///
///   POST leaderboard/SetStat        body: SetStatRequestDTO
///       <c>{StatChannel(int), RoomId(long), StatValue(int)}</c>
///   POST leaderboard/GetPlayerRank  body: GetPlayerRankData
///       <c>{PlayerId(int), StatChannel(int), RoomId(long)}</c>
///
/// **Subdomain matters**: the client uses
/// <c>Core.SendRequest(method, Service.Leaderboard, requestUri)</c>
/// where <c>Service.Leaderboard = 9</c> resolves via the
/// <c>RecRoomConfig.ServiceUrls</c> map (we serve via
/// <c>api/config/v2</c>) to <c>https://leaderboard.{apex}</c>. So
/// the actual outbound URL is <c>leaderboard.localhost/leaderboard/SetStat</c>
/// — not <c>api.localhost/api/leaderboard/...</c>.
///
/// Response shapes:
///   SetStat → RequestResult ack <c>{success:true,error:""}</c>
///   GetPlayerRank → FullLeaderboard.Entry — <b>camelCase</b> per
///   <c>FullLeaderboard_NestedType_Entry.txt:111-121</c>:
///   <c>{playerId(int), score(long), rank(int)}</c>.
///
/// Without explicit subdomain routing the request fell through to
/// <see cref="GlobalCatchAllController"/> which returns <c>[]</c>;
/// the client's <c>Util.Deserialize&lt;Entry&gt;</c> tried to cast
/// list-as-dictionary →
/// <c>InvalidCastException: Unable to cast 'List' to 'Dictionary'</c>
/// → "Failed to retrieve leaderboard: Malformed Response".
///
/// We bind both the leaderboard subdomain (canonical) AND the api
/// host (defensive — some 2020 builds may proxy via api/) so either
/// path lands here.
/// </summary>
[ApiController]
public class LeaderboardController(
    DorkNetDbContext db,
    PlayerPresenceService playerPresence) : ControllerBase
{
    public sealed class SetStatRequest
    {
        public int StatChannel { get; set; }
        public long RoomId { get; set; }
        public int StatValue { get; set; }
    }

    /// <summary>POST <c>/leaderboard/SetStat</c> on the leaderboard
    /// subdomain (canonical client path) AND <c>/api/leaderboard/SetStat</c>
    /// on the api host (legacy / proxy fallback).
    ///
    /// **Wire semantics**: the watch's
    /// <c>Leaderboards.ConditionalSetStat_ReturningNewValue</c>
    /// (Cpp2IL_ISIL/.../RecNet/Leaderboards.txt:645) handles the
    /// per-channel direction (high-score vs low-time-better) LOCALLY
    /// via a SetMode enum and only POSTs to <c>SetStat</c> when the
    /// new value should replace the cached one. The wire request
    /// itself carries no direction hint — it's just
    /// <c>{StatChannel, RoomId, StatValue}</c>. So the server must
    /// trust the client and assign unconditionally; an earlier
    /// <c>Math.Max</c> here silently kept the LARGEST value, which
    /// broke channels like StuntRunner (channel 7, low elapsed time
    /// wins) where the watch posts the new best and we'd discard it
    /// for any prior worse value.</summary>
    [HttpPost("/leaderboard/SetStat")]
    [HttpPost("/api/leaderboard/SetStat")]
    [Authorize]
    public async Task<IActionResult> SetStat([FromBody] SetStatRequest req)
    {
        var pid = this.RequireCurrentPlayerId();
        var roomId = ResolveRoomId(req.RoomId, pid);
        var row = await db.LeaderboardStats.FirstOrDefaultAsync(s =>
            s.RoomId == roomId && s.PlayerId == pid && s.StatChannel == req.StatChannel);
        if (row is null)
        {
            row = new LeaderboardStatEntity
            {
                PlayerId = pid,
                RoomId = roomId,
                StatChannel = req.StatChannel,
                Value = req.StatValue,
            };
            db.LeaderboardStats.Add(row);
        }
        else
        {
            row.Value = req.StatValue;
            row.UpdatedAt = DateTime.UtcNow;
        }

        // Auto-attach the channel to whichever room the score came
        // from so every room implicitly gets its own leaderboards.
        // The watch's <c>SetStatRequestDTO</c> carries
        // <c>RoomId</c> (see Cpp2IL_ISIL .../SetStatRequestDTO);
        // when it's zero (rare — direct stat report outside a room
        // context) fall back to the player's current presence so a
        // stray score still gets bucketed somewhere visible.
        if (roomId > 0)
        {
            var meta = await db.LeaderboardChannelMeta
                .FirstOrDefaultAsync(c => c.RoomId == roomId && c.Channel == req.StatChannel);
            if (meta is null)
            {
                // First time this channel reports. Stamp a meta row so
                // the admin SPA's per-room Leaderboards tab surfaces it
                // immediately, with a placeholder name the admin can
                // rename later. Direction defaults to higher-is-better;
                // admin flips the toggle for time-trial channels.
                db.LeaderboardChannelMeta.Add(new LeaderboardChannelMetaEntity
                {
                    Channel = req.StatChannel,
                    RoomId = roomId,
                    Name = $"Channel {req.StatChannel}",
                    LowerIsBetter = false,
                    ValueFormat = "count",
                });
            }
        }

        await db.SaveChangesAsync();
        return Ok(new { success = true, error = "" });
    }

    public sealed class GetPlayerRankRequest
    {
        public int PlayerId { get; set; }
        public int StatChannel { get; set; }
        public long RoomId { get; set; }
    }

    [HttpPost("/leaderboard/GetPlayerRank")]
    [HttpPost("/api/leaderboard/GetPlayerRank")]
    public async Task<IActionResult> GetPlayerRank([FromBody] GetPlayerRankRequest req)
    {
        var pid = req.PlayerId > 0
            ? (long)req.PlayerId
            : this.CurrentPlayerId() ?? 0;
        // Empty/no-row case: return a zero entry. The client tolerates
        // this — `Entry.Deserialize` reads playerId/score/rank as
        // primitives with defaults.
        if (pid == 0)
            return Ok(new { playerId = 0, score = 0L, rank = 0 });

        var roomId = ResolveRoomId(req.RoomId, pid);
        var row = await db.LeaderboardStats.FirstOrDefaultAsync(s =>
            s.RoomId == roomId && s.PlayerId == pid && s.StatChannel == req.StatChannel);
        if (row is null)
            return Ok(new { playerId = (int)pid, score = 0L, rank = 0 });

        var lowerIsBetter = await IsLowerIsBetterAsync(roomId, req.StatChannel);
        var betterCount = await db.LeaderboardStats
            .CountAsync(s => s.RoomId == roomId
                && s.StatChannel == req.StatChannel
                && (lowerIsBetter ? s.Value < row.Value : s.Value > row.Value));
        return Ok(new
        {
            playerId = (int)pid,
            score = row.Value,
            rank = betterCount + 1,
        });
    }

    public sealed class GetNearbyScoresRequest
    {
        public int PlayerId { get; set; }
        public int StatChannel { get; set; }
        public long RoomId { get; set; }
        public int WindowSize { get; set; }
    }

    /// <summary>POST <c>/leaderboard/GetNearbyScores</c> — returns the
    /// N entries on either side of the caller's rank. Wire shape per
    /// <c>GetNearbyScoresRequestDTO.cs</c>: PlayerId/StatChannel/RoomId
    /// inherited from GetRankRequestDTO + WindowSize.</summary>
    [HttpPost("/leaderboard/GetNearbyScores")]
    [HttpPost("/api/leaderboard/GetNearbyScores")]
    public async Task<IActionResult> GetNearbyScores([FromBody] GetNearbyScoresRequest req)
    {
        var pid = req.PlayerId > 0 ? (long)req.PlayerId : this.CurrentPlayerId() ?? 0;
        var window = Math.Clamp(req.WindowSize, 1, 50);
        var roomId = ResolveRoomId(req.RoomId, pid);
        var lowerIsBetter = await IsLowerIsBetterAsync(roomId, req.StatChannel);

        var query = db.LeaderboardStats
            .Where(s => s.RoomId == roomId && s.StatChannel == req.StatChannel);
        query = lowerIsBetter ? query.OrderBy(s => s.Value) : query.OrderByDescending(s => s.Value);
        var all = await query.Select(s => new { s.PlayerId, s.Value }).ToListAsync();
        if (all.Count == 0)
        {
            // Empty case must still come back as a SingleLeaderboard
            // Dictionary, not a bare array — the 2020 watch's
            // SingleLeaderboard.Deserialize reads dict["rows"]
            // (Cpp2IL_ISIL/.../RecNet/SingleLeaderboard.txt:60).
            // Returning `[]` cast-fails as List→Dictionary inside
            // RecNet.Util.Deserialize, logged repeatedly as
            // "Received malformed RecNet response: '[]'" and
            // eventually starves the StuntRunner UI of a usable
            // leaderboard.
            return Ok(new { rows = Array.Empty<object>() });
        }

        var ranked = all.Select((r, i) => new
        {
            playerId = (int)r.PlayerId,
            score = r.Value,
            rank = i + 1,
        }).ToList();

        var myIdx = ranked.FindIndex(r => r.playerId == (int)pid);
        if (myIdx < 0)
        {
            return Ok(new { rows = ranked.Take(window * 2 + 1).ToArray() });
        }
        var lo = Math.Max(0, myIdx - window);
        var hi = Math.Min(ranked.Count - 1, myIdx + window);
        return Ok(new { rows = ranked.GetRange(lo, hi - lo + 1).ToArray() });
    }

    public sealed class GetRanksRequest
    {
        public int PlayerId { get; set; }
        public int StatChannel { get; set; }
        public long RoomId { get; set; }
        public int RankStart { get; set; }
        public int RankEnd { get; set; }
    }

    /// <summary>POST <c>/leaderboard/GetRanks</c> — returns a slice of
    /// the leaderboard between RankStart and RankEnd (inclusive,
    /// 1-indexed). Wire shape per <c>GetRanksRequestDTO.cs</c>.</summary>
    [HttpPost("/leaderboard/GetRanks")]
    [HttpPost("/api/leaderboard/GetRanks")]
    public async Task<IActionResult> GetRanks([FromBody] GetRanksRequest req)
    {
        var start = Math.Max(1, req.RankStart);
        var end = Math.Max(start, req.RankEnd);
        var skip = start - 1;
        var take = Math.Clamp(end - skip, 1, 200);
        var pid = req.PlayerId > 0 ? (long)req.PlayerId : this.CurrentPlayerId() ?? 0;
        var roomId = ResolveRoomId(req.RoomId, pid);
        var lowerIsBetter = await IsLowerIsBetterAsync(roomId, req.StatChannel);

        var query = db.LeaderboardStats
            .Where(s => s.RoomId == roomId && s.StatChannel == req.StatChannel);
        query = lowerIsBetter ? query.OrderBy(s => s.Value) : query.OrderByDescending(s => s.Value);
        var rows = await query.Skip(skip).Take(take).ToListAsync();
        var entries = rows.Select((s, i) => new
        {
            playerId = (int)s.PlayerId,
            score = s.Value,
            rank = start + i,
        }).ToArray();
        // SingleLeaderboard wrapper — see GetNearbyScores above.
        return Ok(new { rows = entries });
    }

    /// <summary>Top-N for a channel. Used by the admin tooling and
    /// by the watch's full-leaderboard scoreboard view.</summary>
    [HttpGet("/leaderboard/Top")]
    [HttpGet("/api/leaderboard/Top")]
    public async Task<IActionResult> Top([FromQuery] int channel, [FromQuery] long roomId = 0, [FromQuery] int take = 50)
    {
        take = Math.Clamp(take, 1, 200);
        var resolvedRoomId = ResolveRoomId(roomId, this.CurrentPlayerId() ?? 0);
        var lowerIsBetter = await IsLowerIsBetterAsync(resolvedRoomId, channel);
        var query = db.LeaderboardStats
            .Where(s => s.RoomId == resolvedRoomId && s.StatChannel == channel);
        query = lowerIsBetter ? query.OrderBy(s => s.Value) : query.OrderByDescending(s => s.Value);
        var rows = await query.Take(take).ToListAsync();
        // FullLeaderboard wire shape: GlobalOverall / GlobalPeriodic /
        // FriendsOverall / FriendsPeriodic + NextResetUTC. Use the
        // same Entry list for all four buckets — friends-vs-global
        // can come later.
        var entries = rows.Select((s, i) => new
        {
            playerId = (int)s.PlayerId,
            score = s.Value,
            rank = i + 1,
        }).ToArray();
        return Ok(new
        {
            GlobalOverall = entries,
            GlobalPeriodic = entries,
            FriendsOverall = Array.Empty<object>(),
            FriendsPeriodic = Array.Empty<object>(),
            NextResetUTC = DateTime.UtcNow.AddDays(7),
        });
    }

    private long ResolveRoomId(long requestedRoomId, long playerId)
    {
        if (requestedRoomId > 0) return requestedRoomId;
        if (playerId > 0) return playerPresence.GetRoom(playerId)?.RoomId ?? 0;
        return 0;
    }

    private async Task<bool> IsLowerIsBetterAsync(long roomId, int channel)
    {
        var live = await db.LeaderboardChannelMeta
            .Where(c => c.RoomId == roomId && c.Channel == channel)
            .Select(c => (bool?)c.LowerIsBetter)
            .FirstOrDefaultAsync();
        return live ?? false;
    }
}
