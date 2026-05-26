using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.Royale;

/// <summary>
/// api.rec.net/api/royale/* — Rec Royale battle-royale stats.
///
/// Per <c>RecNet.RecNetRecRoyaleStats</c> (decompiled at
/// <c>Cpp2IL_CS/.../RecNet/RecNetRecRoyaleStats.cs</c>) the client
/// only calls TWO endpoints:
///
/// • <c>GET api/royale/v1/current</c> → <c>RecRoyalePlayerProgress</c>:
///   <c>TotalXP, Level, RankIdx, RankName, CurrentLevelXPThreshold,
///   NextLevelXPThreshold, NextLevelAcornReward</c>.
/// • <c>POST api/royale/v2/matchcomplete</c> with body
///   <c>MatchCompleteStats</c>: <c>Rank, NumEliminations, SecondsAlive,
///   WalkGame, CustomGame, ChestsOpened, ShieldPotionsConsumed,
///   HealthPotionsConsumed, SecondsInAir</c>; returns <c>StatUpdates</c>:
///   <c>XPAwardStrings, TotalXPAwarded, NewProgress</c>.
///
/// We persist match rows to <see cref="RoyaleMatchEntity"/> +
/// <see cref="RoyaleMatchPlayerEntity"/> for server-side audit, and
/// roll the per-player <see cref="RoyalePlayerProgressEntity"/>.
/// </summary>
[ApiController]
public class RoyaleController(DorkNetDbContext db) : ControllerBase
{
    // Rank ladder names (from the watch's RankIdx → RankName mapping).
    // Approximated; the client's ladder isn't exposed in the dump but
    // these are the standard 2020-era Royale tiers.
    private static readonly string[] RankNames =
    {
        "Recruit", "Cadet", "Sergeant", "Lieutenant", "Captain",
        "Major", "Colonel", "Commander", "Champion", "Legend",
    };

    private static long XpForLevel(int level) => 100L * level * level;

    private static (int level, int rankIdx, string rankName, long currentThreshold, long nextThreshold, int reward)
        Compute(long totalXp)
    {
        int lvl = 1;
        while (XpForLevel(lvl + 1) <= totalXp) lvl++;
        var rankIdx = Math.Min(RankNames.Length - 1, lvl / 5);
        var current = XpForLevel(lvl);
        var next = XpForLevel(lvl + 1);
        var reward = 50 + 10 * lvl;
        return (lvl, rankIdx, RankNames[rankIdx], current, next, reward);
    }

    [HttpGet("api/royale/v1/current")]
    public async Task<IActionResult> Current()
    {
        var pid = this.CurrentPlayerId();
        if (pid is not long me) return Ok(BlankProgress());
        var row = await db.RoyalePlayerProgress.FirstOrDefaultAsync(p => p.PlayerId == me);
        if (row is null) return Ok(BlankProgress());
        return Ok(ToWireProgress(row));
    }

    public sealed class MatchCompleteStats
    {
        public int Rank { get; set; }
        public int NumEliminations { get; set; }
        public int SecondsAlive { get; set; }
        public bool WalkGame { get; set; }
        public bool CustomGame { get; set; }
        public int ChestsOpened { get; set; }
        public int ShieldPotionsConsumed { get; set; }
        public int HealthPotionsConsumed { get; set; }
        public int SecondsInAir { get; set; }
    }

    [HttpPost("api/royale/v2/matchcomplete")]
    [Authorize]
    public async Task<IActionResult> MatchComplete([FromBody] MatchCompleteStats body)
    {
        var pid = this.RequireCurrentPlayerId();
        if (body is null) return BadRequest("missing body");

        var match = new RoyaleMatchEntity
        {
            CompletedAt = DateTime.UtcNow,
            Rank = body.Rank,
            NumEliminations = body.NumEliminations,
            SecondsAlive = body.SecondsAlive,
            WalkGame = body.WalkGame,
            CustomGame = body.CustomGame,
            ChestsOpened = body.ChestsOpened,
            ShieldPotionsConsumed = body.ShieldPotionsConsumed,
            HealthPotionsConsumed = body.HealthPotionsConsumed,
            SecondsInAir = body.SecondsInAir,
        };
        db.RoyaleMatches.Add(match);
        await db.SaveChangesAsync();

        db.RoyaleMatchPlayers.Add(new RoyaleMatchPlayerEntity
        {
            MatchId = match.Id,
            PlayerId = pid,
            Rank = body.Rank,
            NumEliminations = body.NumEliminations,
            SecondsAlive = body.SecondsAlive,
        });

        // Compute XP awards. Match the watch's pattern:
        //  • Survival: 1 XP per 10 seconds alive
        //  • Eliminations: 25 XP each
        //  • Placement: bonus inversely proportional to rank
        //  • Custom games don't award XP
        var awards = new List<(string label, int xp)>();
        if (!body.CustomGame)
        {
            var survivalXp = body.SecondsAlive / 10;
            var killXp = body.NumEliminations * 25;
            var rankXp = Math.Max(0, 200 - body.Rank * 5);
            if (survivalXp > 0) awards.Add(($"Time Alive: {body.SecondsAlive}s", survivalXp));
            if (killXp > 0) awards.Add(($"Eliminations: {body.NumEliminations}", killXp));
            if (rankXp > 0) awards.Add(($"Rank #{body.Rank}", rankXp));
            if (body.Rank == 1) awards.Add(("Victory Royale!", 500));
        }
        var totalAward = awards.Sum(a => a.xp);

        // Update progress.
        var row = await db.RoyalePlayerProgress.FirstOrDefaultAsync(p => p.PlayerId == pid);
        if (row is null)
        {
            row = new RoyalePlayerProgressEntity { PlayerId = pid };
            db.RoyalePlayerProgress.Add(row);
        }
        row.TotalXP += totalAward;
        var (lvl, rankIdx, rankName, current, next, reward) = Compute(row.TotalXP);
        row.Level = lvl;
        row.RankIdx = rankIdx;
        row.RankName = rankName;
        row.CurrentLevelXPThreshold = current;
        row.NextLevelXPThreshold = next;
        row.NextLevelAcornReward = reward;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new
        {
            XPAwardStrings = awards.Select(a => $"+{a.xp} XP — {a.label}").ToList(),
            TotalXPAwarded = (long)totalAward,
            NewProgress = new[] { ToWireProgress(row) },
        });
    }

    private static object ToWireProgress(RoyalePlayerProgressEntity p) => new
    {
        p.TotalXP,
        p.Level,
        p.RankIdx,
        p.RankName,
        p.CurrentLevelXPThreshold,
        p.NextLevelXPThreshold,
        p.NextLevelAcornReward,
    };

    private static object BlankProgress() => new
    {
        TotalXP = 0L,
        Level = 1,
        RankIdx = 0,
        RankName = "Recruit",
        CurrentLevelXPThreshold = 0L,
        NextLevelXPThreshold = 100L,
        NextLevelAcornReward = 50,
    };
}
