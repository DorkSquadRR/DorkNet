using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Controllers.API.PlayerElo;

/// <summary>
/// api.rec.net/api/PlayerElo/* — match-result Elo deltas. The 2020
/// client posts after each ranked match (Royale, Paintball, Soccer)
/// with a delta the server applies to the per-player Elo row.
///
/// Wire request matches <c>RecNet.Elo.ReportPlayerElo</c>:
///   <c>{GameMode(int), EloDelta(int), Won(bool)}</c>
/// (we tolerate a few synonym fields). Response: the new Elo as a
/// JSON primitive int.
/// </summary>
[ApiController]
public class PlayerEloController(DorkNetDbContext db) : ControllerBase
{
    private long Me => this.RequireCurrentPlayerId();

    public sealed class ReportEloRequest
    {
        public int GameMode { get; set; }
        public int EloDelta { get; set; }
        public bool Won { get; set; }
    }

    [HttpPost("api/PlayerElo/v1/reportPlayerElo")]
    [Authorize]
    public async Task<IActionResult> ReportPlayerElo([FromBody] ReportEloRequest req)
    {
        var pid = Me;
        var row = await db.PlayerElo.FirstOrDefaultAsync(p =>
            p.PlayerId == pid && p.GameMode == req.GameMode);
        if (row is null)
        {
            row = new PlayerEloEntity
            {
                PlayerId = pid,
                GameMode = req.GameMode,
            };
            db.PlayerElo.Add(row);
        }
        // Clamp Elo to a reasonable range to avoid overflow exploits.
        row.Elo = Math.Clamp(row.Elo + req.EloDelta, 0, 5000);
        row.MatchesPlayed += 1;
        if (req.Won) row.Wins += 1; else row.Losses += 1;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(row.Elo);
    }

    [HttpGet("api/PlayerElo/v1/get")]
    public async Task<IActionResult> Get(
        [FromQuery] long? playerId, [FromQuery] int gameMode = 0)
    {
        var pid = playerId ?? this.CurrentPlayerId() ?? 0;
        if (pid == 0) return Ok(new { Elo = 1000, Wins = 0, Losses = 0 });
        var row = await db.PlayerElo.FirstOrDefaultAsync(p =>
            p.PlayerId == pid && p.GameMode == gameMode);
        return Ok(row is null
            ? new { Elo = 1000, Wins = 0, Losses = 0 }
            : new { row.Elo, row.Wins, row.Losses });
    }
}
