namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Single-row table holding server-wide runtime toggles that admins can
/// flip from the SPA without a redeploy. Modelled after
/// <see cref="CommunityBoardEntity"/>: <see cref="Id"/> is pinned to 1
/// so EF Core has a key to track changes against, and reads/writes
/// touch one row.
///
/// Add new boolean (or scalar) columns directly to this entity as more
/// toggles appear — keeping them strongly typed beats a generic
/// key/value table for the dozen-or-so global switches we'll ever have.
/// </summary>
public class ServerSettingsEntity
{
    public int Id { get; set; } = 1;

    /// <summary>When true, the watch's account-creation endpoints
    /// (<c>POST /account/create</c> and <c>POST /api/account/v1/create</c>)
    /// short-circuit with <c>Success=false</c> and the watch surfaces
    /// the error to the player. Existing logins are unaffected; only
    /// brand-new account requests are blocked.</summary>
    public bool SignupsDisabled { get; set; }

    /// <summary>When true, the challenge map tells the 2020 watch that
    /// weekly challenge completion is required for the weekly reward flow.
    /// Individual player challenge rows still default to incomplete until
    /// the client reports progress.</summary>
    public bool WeeklyChallengesCompletedRequired { get; set; } = true;

    /// <summary>Admin-managed weekly challenge templates as JSON. Challenge
    /// ids are generated per weekly map so old progress rows stay scoped to
    /// that week's map id.</summary>
    public string WeeklyChallengesJson { get; set; } = string.Empty;

    /// <summary>Admin-managed weekly reward JSON. The progression API uses
    /// this for both the watch's ChallengeGift payload and the once-per-week
    /// server-side XP/token grant.</summary>
    public string WeeklyChallengeRewardJson { get; set; } = string.Empty;

    /// <summary>Admin-managed Play menu room filter chips as JSON. Stores
    /// separate pinned and popular tag lists; wire endpoints serve them to
    /// the watch without leading # characters.</summary>
    public string PlayMenuTagsJson { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
