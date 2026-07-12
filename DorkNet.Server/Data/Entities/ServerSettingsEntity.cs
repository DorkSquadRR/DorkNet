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

    /// <summary>When true, every account on the server is treated as a
    /// friend of every other account — the relationships list, friend
    /// online-status HUD, and room-move fan-out all behave as if everyone
    /// is mutually friended. Built for small private servers where
    /// searching + manually friend-requesting each other is friction; no
    /// rows are written, so flipping it off instantly reverts to the real
    /// relationship graph. Blocked relationships still suppress the pairing,
    /// and the system/coach account (Id=1) is excluded.</summary>
    public bool GlobalFriendsEnabled { get; set; }

    /// <summary>When true, every account is reported as owning every
    /// avatar item in the master catalog (plus all permanent hair dyes) —
    /// the wardrobe/store "unlocked items" endpoints return the full
    /// catalog for every player regardless of their real inventory. Like
    /// <see cref="GlobalFriendsEnabled"/>, nothing is written to the
    /// inventory tables: it's synthesized at read time, so flipping it off
    /// instantly reverts to each player's actually-owned items. Color
    /// variants and player-made custom (UGC) items are out of scope.</summary>
    public bool AllAvatarItemsOwned { get; set; }

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

    /// <summary>Admin-managed Rec Center category-door config. The
    /// 2020.12 client reads this through api/gameconfigs/v1/all as
    /// Door.{Category}.Title and Door.{Category}.Query entries.</summary>
    public string RecCenterDoorsJson { get; set; } = string.Empty;

    /// <summary>Admin-managed values for GameConfig keys confirmed in
    /// the 2020.12 decomp. Kept typed in the admin API; stored as JSON
    /// here so newly discovered built-in keys do not require a table
    /// migration each time.</summary>
    public string DiscoveredGameConfigsJson { get; set; } = string.Empty;

    /// <summary>When true, the server-side profanity filter behind
    /// <c>api/sanitize/*</c> is bypassed — every string is treated as
    /// clean and returned unmodified. Built for private friend servers
    /// that don't want room/invention names or chat censored. Off by
    /// default (filter active). See <see cref="Services.ProfanityFilter"/>
    /// and <c>SanitizeController</c>.</summary>
    public bool ProfanityFilterDisabled { get; set; }

    /// <summary>Which charades word list (row id in
    /// <see cref="CharadesWordListEntity"/>) is live for each of the
    /// client's three baked card-source slots. JSON object keyed by the
    /// client enum name — <c>{"Charades":id,"CharadesAprilFoolsDay":id,
    /// "Icebreakers":id}</c>. Empty/0 or a dangling id falls back to the
    /// list the seeder created for that slot. Lets admins keep an
    /// unlimited library and switch which list a slot serves without a
    /// redeploy.</summary>
    public string CharadesSlotBindingsJson { get; set; } = string.Empty;

    /// <summary>Ordered list of room NAMES shown in the room-creation "base
    /// room" picker (<c>rooms/base</c> / <c>api/rooms/v*/baserooms</c>), as a
    /// JSON string array. Empty falls back to the built-in default set. Lets
    /// admins curate the base-room list from the SPA without a redeploy.</summary>
    public string BaseRoomNamesJson { get; set; } = string.Empty;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
