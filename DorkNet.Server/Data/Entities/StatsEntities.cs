using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One leaderboard stat row per (room, player, statChannel). Backs
/// <c>api/Leaderboard/v1/SetStats</c> + <c>api/Leaderboard/v2/getPlayerRank</c>.
///
/// The 2020 watch's <c>RecNet.Leaderboards</c> uses an int channel
/// id (e.g. 1=PaintballWins, 2=DodgeballHits, etc.) and a single
/// long value per channel. A player's "rank" is computed as
/// <c>1 + count(*)</c> of rows in the same channel with a higher
/// value.
/// </summary>
public class LeaderboardStatEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }

    /// <summary>Room the score was reported from. The same stat channel
    /// id is reused by different rooms, so room scope is part of the
    /// leaderboard identity. 0 means legacy/global/unscoped.</summary>
    public long RoomId { get; set; }

    /// <summary>RecNet stat channel id (small int per game mode +
    /// stat type — e.g. paintball-CTF-wins, dodgeball-hits).</summary>
    public int StatChannel { get; set; }

    /// <summary>Aggregate stat value. Set semantics depend on
    /// <see cref="SetMode"/>: 0=Always, 1=OnlyIfHigher, 2=OnlyIfLower,
    /// 3=Increment. Most stat channels are mode 1 (high score).</summary>
    public long Value { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Match-result Elo per (player, game mode). Backs
/// <c>api/PlayerElo/v1/reportPlayerElo</c>. The 2020 watch reports
/// an Elo delta after each ranked match (Royale, Paintball,
/// Soccer); we accumulate it per game mode for matchmaking.
/// </summary>
public class PlayerEloEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }

    /// <summary>RecNet game mode id (small int). We don't enumerate
    /// these; the wire just sends an int and we pass it through.</summary>
    public int GameMode { get; set; }

    /// <summary>Current Elo. Default 1000 on first row.</summary>
    public int Elo { get; set; } = 1000;

    public int Wins { get; set; }
    public int Losses { get; set; }
    public int MatchesPlayed { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
