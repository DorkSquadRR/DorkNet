using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One Rec Royale match. Inserted by the server when
/// <c>POST api/royale/v2/matchcomplete</c> arrives so we keep an
/// audit trail (the client doesn't read this table — it only reads
/// the aggregated <see cref="RoyalePlayerProgressEntity"/>).
/// </summary>
public class RoyaleMatchEntity
{
    public long Id { get; set; }

    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    /// <summary>From <c>MatchCompleteStats.Rank</c> — the player's
    /// finish position (1 = Victory Royale).</summary>
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

/// <summary>One per (match, player) — server-side audit only; not
/// exposed to client. Lets us aggregate per-match contributions
/// retroactively.</summary>
public class RoyaleMatchPlayerEntity
{
    public long Id { get; set; }
    public long MatchId { get; set; }
    public long PlayerId { get; set; }

    public int Rank { get; set; }
    public int NumEliminations { get; set; }
    public int SecondsAlive { get; set; }
}

/// <summary>One per player — mirrors wire type
/// <c>RecRoyalePlayerProgress</c> (TotalXP/Level/RankIdx/RankName +
/// per-level XP thresholds and acorn reward). The watch reads this
/// from <c>GET api/royale/v1/current</c>.</summary>
public class RoyalePlayerProgressEntity
{
    public long Id { get; set; }
    public long PlayerId { get; set; }

    public long TotalXP { get; set; }
    public int Level { get; set; }
    public int RankIdx { get; set; }

    [MaxLength(64)]
    public string RankName { get; set; } = "Recruit";

    public long CurrentLevelXPThreshold { get; set; }
    public long NextLevelXPThreshold { get; set; } = 100;
    public int NextLevelAcornReward { get; set; } = 50;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
