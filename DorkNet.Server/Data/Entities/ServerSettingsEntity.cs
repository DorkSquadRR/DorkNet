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

    /// <summary>When true, the watch's weekly-challenge map reports
    /// <c>CompletedRequired</c> — the player must finish every listed
    /// challenge before the gift unlocks. Mirrors
    /// <c>ChallengeMap.CompletedRequired</c> on the 2020.03 client.</summary>
    public bool WeeklyChallengesCompletedRequired { get; set; } = true;

    /// <summary>JSON-serialised <c>List&lt;WeeklyChallengeTemplate&gt;</c>
    /// (see <c>ServerSettingsService</c>). Empty string falls back to
    /// <c>ServerSettingsService.DefaultWeeklyChallenges()</c>. Admin-
    /// editable from the SPA Settings page so the weekly slate can change
    /// without a redeploy.</summary>
    public string WeeklyChallengesJson { get; set; } = string.Empty;

    /// <summary>JSON-serialised <c>WeeklyChallengeReward</c> — the gift
    /// granted when the week's challenges complete (XP + tokens, plus an
    /// optional store skin identified by <c>Slug</c>). Empty string falls
    /// back to <c>ServerSettingsService.DefaultWeeklyReward()</c>.</summary>
    public string WeeklyChallengeRewardJson { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
