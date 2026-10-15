using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace DorkNet.Server.Services;

/// <summary>
/// Single-row settings store. Same Postgres-backed pattern as
/// <see cref="CommunityBoardService"/>: every read is one SELECT,
/// every write is one UPDATE/INSERT, and the row is visible to every
/// replica immediately so admin toggles propagate without a cache
/// invalidation dance.
///
/// Scoped (not singleton) because it holds a DbContext reference. The
/// toggles it backs (signups, etc.) are checked on rare paths —
/// account creation hits this once per signup attempt, which is
/// dwarfed by everything else the request does.
/// </summary>
public class ServerSettingsService(DorkNetDbContext db)
{
    private const int RowId = 1;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<ServerSettingsEntity> GetAsync()
    {
        var row = await db.ServerSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == RowId);
        return row ?? new ServerSettingsEntity { Id = RowId };
    }

    public async Task<bool> AreSignupsDisabledAsync()
    {
        var row = await db.ServerSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == RowId);
        return row?.SignupsDisabled ?? false;
    }

    public async Task<ServerSettingsEntity> SetSignupsDisabledAsync(bool disabled)
    {
        var existing = await GetTrackedRowAsync();
        existing.SignupsDisabled = disabled;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return existing;
    }

    /// <summary>When true, every account is treated as a friend of every
    /// other account (synthesized at read time — no relationship rows are
    /// written). See <see cref="ServerSettingsEntity.GlobalFriendsEnabled"/>
    /// and <see cref="RelationshipQueries"/>.</summary>
    public async Task<bool> IsGlobalFriendsEnabledAsync()
    {
        var row = await db.ServerSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == RowId);
        return row?.GlobalFriendsEnabled ?? false;
    }

    public async Task<ServerSettingsEntity> SetGlobalFriendsEnabledAsync(bool enabled)
    {
        var existing = await GetTrackedRowAsync();
        existing.GlobalFriendsEnabled = enabled;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return existing;
    }

    /// <summary>When true, every account is treated as owning every avatar
    /// item in the master catalog (synthesized at read time — no inventory
    /// rows are written). See <see cref="ServerSettingsEntity.AllAvatarItemsOwned"/>
    /// and <see cref="Controllers.API.Avatar.V4.AvatarItemsController"/>.</summary>
    public async Task<bool> IsAllAvatarItemsOwnedEnabledAsync()
    {
        var row = await db.ServerSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == RowId);
        return row?.AllAvatarItemsOwned ?? false;
    }

    public async Task<ServerSettingsEntity> SetAllAvatarItemsOwnedEnabledAsync(bool enabled)
    {
        var existing = await GetTrackedRowAsync();
        existing.AllAvatarItemsOwned = enabled;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return existing;
    }

    public async Task<WeeklyChallengeSettings> GetWeeklyChallengesAsync()
    {
        var row = await GetAsync();
        return ToWeeklySettings(row);
    }

    public async Task<PlayMenuTagSettings> GetPlayMenuTagsAsync()
    {
        var row = await GetAsync();
        return ToPlayMenuTags(row);
    }

    public async Task<RecCenterDoorSettings> GetRecCenterDoorsAsync()
    {
        var row = await GetAsync();
        return ToRecCenterDoors(row);
    }

    /// <summary>Built-in default base-room picker (used when an admin hasn't
    /// customised the list). RRO originals minus #quest rooms, MakerRoom
    /// first as the canonical blank canvas.</summary>
    public static readonly IReadOnlyList<string> DefaultBaseRoomNames = new[]
    {
        "MakerRoom", "RecCenter", "3DCharades", "Dodgeball",
        "DiscGolfLake", "DiscGolfPropulsion", "Paddleball",
        "Paintball", "Soccer", "LaserTag", "RecRoyaleSquads",
        "RecRoyaleSolos", "Park", "BowlingAlley", "RecRally",
    };

    public async Task<IReadOnlyList<string>> GetBaseRoomNamesAsync()
    {
        var row = await GetAsync();
        return ToBaseRoomNames(row);
    }

    public async Task<IReadOnlyList<string>> SetBaseRoomNamesAsync(IReadOnlyList<string> names)
    {
        var normalized = NormalizeBaseRoomNames(names);
        var existing = await GetTrackedRowAsync();
        existing.BaseRoomNamesJson = JsonSerializer.Serialize(normalized, JsonOptions);
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return normalized;
    }

    private static IReadOnlyList<string> ToBaseRoomNames(ServerSettingsEntity row)
    {
        if (!string.IsNullOrWhiteSpace(row.BaseRoomNamesJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(row.BaseRoomNamesJson, JsonOptions);
                if (parsed is { Count: > 0 })
                    return NormalizeBaseRoomNames(parsed);
            }
            catch
            {
                // Bad settings must not break room creation; admins can
                // overwrite from the SPA.
            }
        }
        return DefaultBaseRoomNames.ToList();
    }

    private static IReadOnlyList<string> NormalizeBaseRoomNames(IReadOnlyList<string> names)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var n in names)
        {
            var t = (n ?? string.Empty).Trim();
            if (t.Length > 0 && seen.Add(t)) result.Add(t);
        }
        return result.Count > 0 ? result : DefaultBaseRoomNames.ToList();
    }

    public async Task<DiscoveredGameConfigSettings> GetDiscoveredGameConfigsAsync()
    {
        var row = await GetAsync();
        return ToDiscoveredGameConfigs(row);
    }

    public async Task<IReadOnlyList<GameConfigurationSetting>> GetGameConfigurationsAsync()
    {
        var row = await GetAsync();
        return ToGameConfigurations(
            ToRecCenterDoors(row),
            ToDiscoveredGameConfigs(row),
            ToCircuitsV2Limits(row));
    }

    public async Task<CircuitsV2LimitSettings> GetCircuitsV2LimitsAsync()
    {
        var row = await GetAsync();
        return ToCircuitsV2Limits(row);
    }

    public async Task<CircuitsV2LimitSettings> SetCircuitsV2LimitsAsync(
        CircuitsV2LimitSettings settings)
    {
        var normalized = NormalizeCircuitsV2Limits(settings);
        var existing = await GetTrackedRowAsync();
        existing.CircuitsV2LimitsJson = JsonSerializer.Serialize(normalized, JsonOptions);
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return normalized with { UpdatedAt = existing.UpdatedAt };
    }

    public async Task<PlayMenuTagSettings> SetPlayMenuTagsAsync(
        IReadOnlyList<string> pinned,
        IReadOnlyList<string> popular,
        IReadOnlyList<string>? trending = null)
    {
        var normalized = NormalizePlayMenuTags(pinned, popular, trending);
        var existing = await GetTrackedRowAsync();
        existing.PlayMenuTagsJson = JsonSerializer.Serialize(normalized, JsonOptions);
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return normalized with { UpdatedAt = existing.UpdatedAt };
    }

    public async Task<RecCenterDoorSettings> SetRecCenterDoorsAsync(
        IReadOnlyList<RecCenterDoorConfig> doors)
    {
        var normalized = NormalizeRecCenterDoors(doors);
        var existing = await GetTrackedRowAsync();
        existing.RecCenterDoorsJson = JsonSerializer.Serialize(normalized, JsonOptions);
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return normalized with { UpdatedAt = existing.UpdatedAt };
    }

    public async Task<DiscoveredGameConfigSettings> SetDiscoveredGameConfigsAsync(
        DiscoveredGameConfigSettings settings)
    {
        var normalized = NormalizeDiscoveredGameConfigs(settings);
        var existing = await GetTrackedRowAsync();
        existing.DiscoveredGameConfigsJson = JsonSerializer.Serialize(normalized, JsonOptions);
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return normalized with { UpdatedAt = existing.UpdatedAt };
    }

    public async Task<WeeklyChallengeSettings> SetWeeklyChallengesAsync(
        bool completedRequired,
        IReadOnlyList<WeeklyChallengeTemplate> challenges,
        WeeklyChallengeReward? reward)
    {
        var normalized = NormalizeWeeklyChallenges(challenges);
        var normalizedReward = NormalizeWeeklyReward(reward);
        var existing = await GetTrackedRowAsync();
        existing.WeeklyChallengesCompletedRequired = completedRequired;
        existing.WeeklyChallengesJson = JsonSerializer.Serialize(normalized, JsonOptions);
        existing.WeeklyChallengeRewardJson = JsonSerializer.Serialize(normalizedReward, JsonOptions);
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ToWeeklySettings(existing);
    }

    /// <summary>Whether the server-side profanity filter behind
    /// <c>api/sanitize/*</c> is bypassed. Off by default (filter active).</summary>
    public async Task<bool> IsProfanityFilterDisabledAsync()
    {
        var row = await db.ServerSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == RowId);
        return row?.ProfanityFilterDisabled ?? false;
    }

    public async Task<ServerSettingsEntity> SetProfanityFilterDisabledAsync(bool disabled)
    {
        var existing = await GetTrackedRowAsync();
        existing.ProfanityFilterDisabled = disabled;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return existing;
    }

    /// <summary>Whether the CDN's PersistedRoomData version clamp for the
    /// March-2023 client is bypassed. Off by default (clamp active) — see
    /// <see cref="ServerSettingsEntity.RoomBlobVersionClampDisabled"/> and
    /// <see cref="RoomDataBlobService.ClampVersionsFor2023"/>.</summary>
    public async Task<bool> IsRoomBlobVersionClampDisabledAsync()
    {
        var row = await db.ServerSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == RowId);
        return row?.RoomBlobVersionClampDisabled ?? false;
    }

    public async Task<ServerSettingsEntity> SetRoomBlobVersionClampDisabledAsync(bool disabled)
    {
        var existing = await GetTrackedRowAsync();
        existing.RoomBlobVersionClampDisabled = disabled;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return existing;
    }

    /// <summary>Live charades slot→list bindings. Empty/unparseable JSON
    /// resolves to <see cref="CharadesSlotBindings.Empty"/> (every slot
    /// falls back to its built-in seed list).</summary>
    public async Task<CharadesSlotBindings> GetCharadesSlotBindingsAsync()
    {
        var row = await GetAsync();
        if (string.IsNullOrWhiteSpace(row.CharadesSlotBindingsJson))
            return CharadesSlotBindings.Empty;
        try
        {
            return JsonSerializer.Deserialize<CharadesSlotBindings>(
                row.CharadesSlotBindingsJson, JsonOptions) ?? CharadesSlotBindings.Empty;
        }
        catch
        {
            return CharadesSlotBindings.Empty;
        }
    }

    public async Task<CharadesSlotBindings> SetCharadesSlotBindingsAsync(CharadesSlotBindings bindings)
    {
        var existing = await GetTrackedRowAsync();
        existing.CharadesSlotBindingsJson = JsonSerializer.Serialize(bindings, JsonOptions);
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return bindings;
    }

    private async Task<ServerSettingsEntity> GetTrackedRowAsync()
    {
        var existing = await db.ServerSettings.FirstOrDefaultAsync(s => s.Id == RowId);
        if (existing is not null) return existing;

        existing = new ServerSettingsEntity { Id = RowId, UpdatedAt = DateTime.UtcNow };
        db.ServerSettings.Add(existing);
        return existing;
    }

    private static WeeklyChallengeSettings ToWeeklySettings(ServerSettingsEntity row)
    {
        var challenges = DefaultWeeklyChallenges();
        var reward = DefaultWeeklyReward();
        if (!string.IsNullOrWhiteSpace(row.WeeklyChallengesJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<WeeklyChallengeTemplate>>(
                    row.WeeklyChallengesJson,
                    JsonOptions);
                if (parsed?.Count > 0)
                    challenges = NormalizeWeeklyChallenges(parsed);
            }
            catch
            {
                challenges = DefaultWeeklyChallenges();
            }
        }

        if (!string.IsNullOrWhiteSpace(row.WeeklyChallengeRewardJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<WeeklyChallengeReward>(
                    row.WeeklyChallengeRewardJson,
                    JsonOptions);
                reward = NormalizeWeeklyReward(parsed);
            }
            catch
            {
                reward = DefaultWeeklyReward();
            }
        }

        return new WeeklyChallengeSettings(
            row.WeeklyChallengesCompletedRequired,
            challenges,
            reward,
            row.UpdatedAt);
    }

    private static PlayMenuTagSettings ToPlayMenuTags(ServerSettingsEntity row)
    {
        if (!string.IsNullOrWhiteSpace(row.PlayMenuTagsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<PlayMenuTagSettings>(
                    row.PlayMenuTagsJson,
                    JsonOptions);
                if (parsed is not null)
                    return NormalizePlayMenuTags(parsed.PinnedTags, parsed.PopularTags, parsed.TrendingTags)
                        with { UpdatedAt = row.UpdatedAt };
            }
            catch
            {
                // Bad settings should not break the Play menu; admins can
                // overwrite them from the SPA.
            }
        }

        return DefaultPlayMenuTags() with { UpdatedAt = row.UpdatedAt };
    }

    private static RecCenterDoorSettings ToRecCenterDoors(ServerSettingsEntity row)
    {
        if (!string.IsNullOrWhiteSpace(row.RecCenterDoorsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<RecCenterDoorSettings>(
                    row.RecCenterDoorsJson,
                    JsonOptions);
                if (parsed is not null)
                    return NormalizeRecCenterDoors(parsed.Doors)
                        with { UpdatedAt = row.UpdatedAt };
            }
            catch
            {
                // Bad settings should not break Rec Center startup; admins
                // can overwrite them from the SPA.
            }
        }

        return DefaultRecCenterDoors() with { UpdatedAt = row.UpdatedAt };
    }

    private static DiscoveredGameConfigSettings ToDiscoveredGameConfigs(ServerSettingsEntity row)
    {
        if (!string.IsNullOrWhiteSpace(row.DiscoveredGameConfigsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<DiscoveredGameConfigSettings>(
                    row.DiscoveredGameConfigsJson,
                    JsonOptions);
                if (parsed is not null)
                    return NormalizeDiscoveredGameConfigs(parsed)
                        with { UpdatedAt = row.UpdatedAt };
            }
            catch
            {
                // Bad settings should not break gameconfig bootstrap.
            }
        }

        return DefaultDiscoveredGameConfigs() with { UpdatedAt = row.UpdatedAt };
    }

    private static List<WeeklyChallengeTemplate> NormalizeWeeklyChallenges(
        IReadOnlyList<WeeklyChallengeTemplate> challenges)
    {
        var normalized = challenges
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Take(10)
            .Select((c, i) => new WeeklyChallengeTemplate(
                i,
                c.Name.Trim(),
                NormalizeWeeklyChallengeConfig(c.Config),
                c.Description?.Trim() ?? string.Empty,
                c.Tooltip?.Trim() ?? string.Empty))
            .ToList();

        return normalized.Count > 0 ? normalized : DefaultWeeklyChallenges();
    }

    private static string NormalizeWeeklyChallengeConfig(string? config)
    {
        if (TryGetLegacyWeeklyGoal(config, out var legacyGoal))
            return CountedAnyChallengeConfig(legacyGoal);

        if (IsClientWeeklyChallengeConfig(config))
            return config!.Trim();

        return CountedAnyChallengeConfig(1);
    }

    private static bool TryGetLegacyWeeklyGoal(string? config, out int goal)
    {
        goal = 1;
        if (string.IsNullOrWhiteSpace(config)) return false;

        try
        {
            using var doc = JsonDocument.Parse(config);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            if (!doc.RootElement.TryGetProperty("Type", out _))
                return false;

            if (doc.RootElement.TryGetProperty("Goal", out var goalElement)
                && goalElement.TryGetInt32(out var parsedGoal))
            {
                goal = Math.Clamp(parsedGoal, 1, 1000);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsClientWeeklyChallengeConfig(string? config)
    {
        if (string.IsNullOrWhiteSpace(config)) return false;

        try
        {
            using var doc = JsonDocument.Parse(config);
            return IsClientWeeklyChallengeConfigElement(doc.RootElement);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsClientWeeklyChallengeConfigElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (!element.TryGetProperty("ct", out var typeElement) || !typeElement.TryGetInt32(out var type))
            return false;

        if (type == 0 || IsKnownClientChallengeType(type))
            return true;

        if (type != 1)
            return false;

        if (!element.TryGetProperty("t", out var targetElement)
            || !targetElement.TryGetInt32(out var target)
            || target <= 0)
        {
            return false;
        }

        if (!element.TryGetProperty("ctc", out var children)
            || children.ValueKind != JsonValueKind.Array
            || children.GetArrayLength() == 0)
        {
            return false;
        }

        return children.EnumerateArray().All(IsClientWeeklyChallengeConfigElement);
    }

    private static bool IsKnownClientChallengeType(int type) => type is
        2  // TimedBufferChallenge
        or 3  // DynamicFloatArithmeticChallenge
        or 4  // DynamicIntArithmeticChallenge
        or 6  // RequiredEventTypeChallenge
        or 7  // RequiredRoomSceneLocationChallenge
        or 8  // RequiredEnemyTypeChallenge
        or 9  // BoolVarEqualsChallenge
        or 11 // DiscGolfFinishUnderParChallenge
        or 12 // RequiredGameModeActivityChallenge
        or 13 // CompleteGameWithoutChallenge
        or 14 // RequiredGestureChallenge
        or 15 // HitstreakChallenge
        or 16; // HitstreakCountChallenge

    private static string CountedAnyChallengeConfig(int target) =>
        "{\"ct\":1,\"ctc\":[{\"ct\":0}],\"t\":" + Math.Clamp(target, 1, 1000) + ",\"cc\":0}";

    private static WeeklyChallengeReward NormalizeWeeklyReward(WeeklyChallengeReward? reward)
    {
        reward ??= DefaultWeeklyReward();
        return new WeeklyChallengeReward(
            GiftDropId: Math.Max(0, reward.GiftDropId),
            Xp: Math.Max(0, reward.Xp),
            Tokens: Math.Max(0, reward.Tokens),
            Level: Math.Max(0, reward.Level),
            GiftContext: Math.Max(0, reward.GiftContext),
            GiftRarity: Math.Max(0, reward.GiftRarity),
            AvatarItemDesc: reward.AvatarItemDesc?.Trim() ?? string.Empty,
            ConsumableItemDesc: reward.ConsumableItemDesc?.Trim() ?? string.Empty,
            EquipmentPrefabName: reward.EquipmentPrefabName?.Trim() ?? string.Empty,
            EquipmentModificationGuid: reward.EquipmentModificationGuid?.Trim() ?? string.Empty);
    }

    public static List<WeeklyChallengeTemplate> DefaultWeeklyChallenges() =>
    [
        new(
            Index: 0,
            Name: "Play 3 Rec Room Originals",
            Config: CountedAnyChallengeConfig(3),
            Description: "Complete three Rec Room Original activities.",
            Tooltip: "Play any RRO activity to make progress."),
        new(
            Index: 1,
            Name: "Cheer 5 Players",
            Config: CountedAnyChallengeConfig(5),
            Description: "Send cheers to five players.",
            Tooltip: "Use the watch to cheer players you meet."),
        new(
            Index: 2,
            Name: "Visit 5 Rooms",
            Config: CountedAnyChallengeConfig(5),
            Description: "Visit five rooms this week.",
            Tooltip: "Any room visit counts toward this challenge."),
    ];

    public static WeeklyChallengeReward DefaultWeeklyReward() =>
        new(
            GiftDropId: 0,
            Xp: 250,
            Tokens: 250,
            Level: 1,
            GiftContext: 0,
            GiftRarity: 0,
            AvatarItemDesc: string.Empty,
            ConsumableItemDesc: string.Empty,
            EquipmentPrefabName: string.Empty,
            EquipmentModificationGuid: string.Empty);

    public static PlayMenuTagSettings DefaultPlayMenuTags() =>
        new(
            PinnedTags:
            [
                "community",
                "recroomoriginal",
                "featured",
                "quest",
                "sport",
                "template",
                "hangout",
                "creative",
            ],
            PopularTags:
            [
                "paintball",
                "dodgeball",
                "soccer",
                "lasertag",
                "recroyale",
                "discgolf",
                "charades",
                "bowling",
                "paddleball",
                "stuntrunner",
                "makerpen",
                "pvp",
                "co-op",
                "chill",
                "music",
                "parkour",
            ],
            TrendingTags:
            [
                "featured",
                "community",
                "makerpen",
                "pvp",
                "quest",
                "music",
                "parkour",
                "hangout",
                "creative",
                "recroomoriginal",
            ],
            UpdatedAt: DateTime.UtcNow);

    public static RecCenterDoorSettings DefaultRecCenterDoors() =>
        new(
            Doors:
            [
                new("Shooters", "Shooters", "#paintball|#lasertag|#recroyale"),
                new("Creative", "Creative", "#creative|#makerpen|#template"),
                new("Quests", "Quests", "#quest"),
                new("Sports", "Sports", "#sport"),
                new("Featured", "Featured", "#featured|#recroomoriginal"),
            ],
            UpdatedAt: DateTime.UtcNow);

    public static DiscoveredGameConfigSettings DefaultDiscoveredGameConfigs() =>
        new(
            FriendsPostGamePromptUnderFriendCount: 3,
            FriendsSuggestFriendCodeOnFriendsScreenCount: 5,
            ScreensForceVerification: false,
            VrForceVerification: false,
            RewardsUseRewardSelection: false,
            RewardsSelectionTimeout: 30,
            RoomDetailsPhotoRollEnabled: false,
            LoadingNetworkTimeout: 30,
            RunningNetworkTimeout: 30,
            SynchronizedFieldRemoveDefaultEntries: false,
            RenderingDisableSrpBatcher: false,
            SplitTestSoftOverrides: "{}",
            SplitTestHardOverrides: "{}",
            SplitTestSegmentProbabilities: "{}",
            UpdatedAt: DateTime.UtcNow);

    /// <summary>
    /// Retail shipped one network entry — 5 RPCs per frame, 1024 bytes per
    /// 60 frames — which is what makes busy CircuitsV2 rooms drop chip
    /// sends. DorkNet ships the budgets effectively unlimited instead, and
    /// raises the room-size limits to values the client survives with
    /// headroom (each is justified in docs/recroom-2023-circuitsv2-limits.md):
    /// <list type="bullet">
    /// <item>Compute 50000 → 500000: the per-frame CPU budget shared by
    /// every circuit in the room. It is the only thing that stops a runaway
    /// loop, so it stays finite.</item>
    /// <item>Chips 3000 → 30000: the room's chip cap ("Too many
    /// chips.").</item>
    /// <item>CloudValues 100 → 1000: the room's cloud-variable cap.</item>
    /// <item>UnculledChips / VisibleChips 500 → 3000: chips past these
    /// counts get culled, so every chip of a retail-sized room stays
    /// visible.</item>
    /// <item>InkPerChip 15 → 0: this is an ink COST, charged against the
    /// client's hard-coded 30000-ink room cap, so lowering it is the
    /// increase. 0 means chips stop eating room ink.</item>
    /// </list>
    /// CloudValuesAutosaveSeconds stays at retail's 300: it is a save
    /// cadence, not a capacity limit. Admins can dial any of it back from
    /// the SPA.
    /// </summary>
    public static CircuitsV2LimitSettings DefaultCircuitsV2Limits() =>
        new(
            Network:
            [
                new(
                    NumPlayersForActivation: 0,
                    Bytes: UnlimitedBudget,
                    BytesTimeWindowFrame60s: 60,
                    Rpcs: UnlimitedBudget,
                    RpcTimeWindowFrame60s: 1),
            ],
            Compute: 500_000,
            Chips: 30_000,
            CloudValues: 1_000,
            CloudValuesAutosaveSeconds: 300f,
            UnculledChips: 3_000,
            VisibleChips: 3_000,
            InkPerChip: 0,
            UpdatedAt: DateTime.UtcNow);

    /// <summary>The budget value that stands in for "unlimited". The client
    /// only ever compares a running counter against it, so any large int
    /// works; this leaves 2x headroom under int.MaxValue.</summary>
    private const int UnlimitedBudget = 1_000_000_000;

    /// <summary>Time windows size a <c>new int[N]</c> ring buffer — one
    /// bucket per frame — inside the client's rate limiter, so a huge
    /// window is an allocation, not a bigger budget. 3600 frames (a minute
    /// at 60fps) is 14KB and far past anything useful.</summary>
    private const int MaxTimeWindowFrames = 3600;

    /// <summary>Play-menu carousel order. <c>PlayMenuCarouselModel.TryGetDisplayPriorityMap</c>
    /// looks this exact key up (literal at Assembly-CSharp/RRUI/Data/PlayMenuCarouselModel.txt:547)
    /// and, when it is absent, logs "Could not find GameConfig for PlayMenuCarouselOrdering_1"
    /// (:758) and leaves the carousels in prefab order. The value is split on ';' then '='
    /// into id → Int32 priority (:603, :632, :655); <c>PlayMenuCarouselController.DisplayOrderPriorityImpl</c>
    /// then reorders the carousel children with SetSiblingIndex.
    ///
    /// The ids are the carousel ids serialized on that component in the 2023-03-21 build's
    /// Addressables bundle (3900770abc33861326d1865e454c0901.bundle, MonoBehaviour
    /// PlayMenuCarouselController/DisplayOrderPriorityImpl): continueplaying, friendsplaying,
    /// myclubhouses, myevents, mysubscriptions, recommended, hotrooms, topcreators, featured,
    /// myfavorites, recroomoriginals, new, limitedtimeevent. Priorities follow the live
    /// service's 2023 "PlayMenuCarouselOrdering_3" segment (higher first), with the
    /// time-limited event carousel put on top.</summary>
    public const string PlayMenuCarouselOrderingKey = "PlayMenuCarouselOrdering_1";

    public const string PlayMenuCarouselOrdering =
        "limitedtimeevent=15;hotrooms=14;new=13;recroomoriginals=12;continueplaying=11;" +
        "myfavorites=10;friendsplaying=9;recommended=8;mysubscriptions=7;featured=5;" +
        "myclubhouses=4;myevents=3;topcreators=2";

    public static IReadOnlyList<GameConfigurationSetting> ToGameConfigurations(
        RecCenterDoorSettings doors,
        DiscoveredGameConfigSettings discovered,
        CircuitsV2LimitSettings circuits)
    {
        var rows = new List<GameConfigurationSetting>();
        foreach (var door in NormalizeRecCenterDoors(doors.Doors).Doors)
        {
            rows.Add(new($"Door.{door.Key}.Title", door.Title));
            rows.Add(new($"Door.{door.Key}.Query", door.Query));
        }

        var gameConfig = NormalizeDiscoveredGameConfigs(discovered);
        rows.AddRange([
            new("Friends.PostGamePromptUnderFriendCount", gameConfig.FriendsPostGamePromptUnderFriendCount.ToString()),
            new("Friends.SuggestFriendCodeOnFriendsScreenCount", gameConfig.FriendsSuggestFriendCodeOnFriendsScreenCount.ToString()),
            new("Screens.ForceVerification", gameConfig.ScreensForceVerification ? "1" : "0"),
            new("VR.ForceVerification", gameConfig.VrForceVerification ? "1" : "0"),
            new("Rewards.UseRewardSelection", gameConfig.RewardsUseRewardSelection.ToString().ToLowerInvariant()),
            new("Rewards.SelectionTimeout", gameConfig.RewardsSelectionTimeout.ToString()),
            new("RoomDetails.PhotoRollEnabled", gameConfig.RoomDetailsPhotoRollEnabled.ToString().ToLowerInvariant()),
            new("loadingNetworkTimeout", gameConfig.LoadingNetworkTimeout.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("runningNetworkTimeout", gameConfig.RunningNetworkTimeout.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("SynchronizedField.RemoveDefaultEntries", gameConfig.SynchronizedFieldRemoveDefaultEntries.ToString().ToLowerInvariant()),
            new("Rendering.DisableSrpBatcher", gameConfig.RenderingDisableSrpBatcher.ToString().ToLowerInvariant()),
            new("splitTestSoftOverrides", gameConfig.SplitTestSoftOverrides),
            new("splitTestHardOverrides", gameConfig.SplitTestHardOverrides),
            new("splitTestSegmentProbabilities", gameConfig.SplitTestSegmentProbabilities),
            new(PlayMenuCarouselOrderingKey, PlayMenuCarouselOrdering),
        ]);

        var limits = NormalizeCircuitsV2Limits(circuits);
        rows.AddRange([
            new("UGC.CircuitsV2.Limit.Network", JsonSerializer.Serialize(limits.Network, JsonOptions)),
            new("UGC.CircuitsV2.Limit.Compute", Int(limits.Compute)),
            new("UGC.CircuitsV2.Limit.Chips", Int(limits.Chips)),
            new("UGC.CircuitsV2.Limit.CloudValues", Int(limits.CloudValues)),
            new("UGC.CircuitsV2.Limit.CloudValuesAutosaveSeconds",
                limits.CloudValuesAutosaveSeconds.ToString("0.###", CultureInfo.InvariantCulture)),
            new("UGC.CircuitsV2.Limit.UnculledChips", Int(limits.UnculledChips)),
            new("UGC.CircuitsV2.Limit.VisibleChips", Int(limits.VisibleChips)),
            new("UGC.CircuitsV2.Limit.InkPerChip", Int(limits.InkPerChip)),
        ]);
        return rows;

        // The client parses every scalar with int/float.TryParse under
        // InvariantCulture, so no thousands separators.
        static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    private static PlayMenuTagSettings NormalizePlayMenuTags(
        IReadOnlyList<string>? pinned,
        IReadOnlyList<string>? popular,
        IReadOnlyList<string>? trending)
    {
        static List<string> Normalize(IReadOnlyList<string>? tags, int max)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return (tags ?? [])
                .Select(t => (t ?? string.Empty).Trim().TrimStart('#').ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Where(t => t.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
                .Where(seen.Add)
                .Take(max)
                .ToList();
        }

        var defaults = DefaultPlayMenuTags();
        var pinnedTags = Normalize(pinned, 16);
        var popularTags = Normalize(popular, 32);
        var trendingTags = Normalize(trending, 32);
        return new PlayMenuTagSettings(
            pinnedTags.Count > 0 ? pinnedTags : defaults.PinnedTags,
            popularTags.Count > 0 ? popularTags : defaults.PopularTags,
            trendingTags.Count > 0 ? trendingTags : defaults.TrendingTags,
            DateTime.UtcNow);
    }

    private static RecCenterDoorSettings NormalizeRecCenterDoors(
        IReadOnlyList<RecCenterDoorConfig> doors)
    {
        static string NormalizeKey(string value)
        {
            var clean = new string((value ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .ToArray());
            return string.IsNullOrWhiteSpace(clean) ? string.Empty : clean;
        }

        static string NormalizeQuery(string value)
        {
            var tags = (value ?? string.Empty)
                .Split(['|', ',', '\n', '\r', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.Trim().TrimStart('#').ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Where(t => t.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
                .Select(t => $"#{t}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
            return string.Join("|", tags);
        }

        var normalized = doors
            .Select(d =>
            {
                var key = NormalizeKey(d.Key);
                var title = string.IsNullOrWhiteSpace(d.Title) ? key : d.Title.Trim();
                var query = NormalizeQuery(d.Query);
                return new RecCenterDoorConfig(key, title, query);
            })
            .Where(d => d.Key.Length > 0 && d.Query.Length > 0)
            .GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(8)
            .ToList();

        return new RecCenterDoorSettings(
            normalized.Count > 0 ? normalized : DefaultRecCenterDoors().Doors,
            DateTime.UtcNow);
    }

    private static DiscoveredGameConfigSettings NormalizeDiscoveredGameConfigs(
        DiscoveredGameConfigSettings settings)
    {
        static string JsonObjectOrDefault(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? "{}" : value.Trim();
            try
            {
                using var doc = JsonDocument.Parse(value);
                return doc.RootElement.ValueKind == JsonValueKind.Object ? value : "{}";
            }
            catch
            {
                return "{}";
            }
        }

        return new DiscoveredGameConfigSettings(
            FriendsPostGamePromptUnderFriendCount: Math.Clamp(settings.FriendsPostGamePromptUnderFriendCount, 0, 100),
            FriendsSuggestFriendCodeOnFriendsScreenCount: Math.Clamp(settings.FriendsSuggestFriendCodeOnFriendsScreenCount, 0, 100),
            ScreensForceVerification: settings.ScreensForceVerification,
            VrForceVerification: settings.VrForceVerification,
            RewardsUseRewardSelection: settings.RewardsUseRewardSelection,
            RewardsSelectionTimeout: Math.Clamp(settings.RewardsSelectionTimeout, 0, 300),
            RoomDetailsPhotoRollEnabled: settings.RoomDetailsPhotoRollEnabled,
            LoadingNetworkTimeout: Math.Clamp(settings.LoadingNetworkTimeout, 1, 300),
            RunningNetworkTimeout: Math.Clamp(settings.RunningNetworkTimeout, 1, 300),
            SynchronizedFieldRemoveDefaultEntries: settings.SynchronizedFieldRemoveDefaultEntries,
            RenderingDisableSrpBatcher: settings.RenderingDisableSrpBatcher,
            SplitTestSoftOverrides: JsonObjectOrDefault(settings.SplitTestSoftOverrides),
            SplitTestHardOverrides: JsonObjectOrDefault(settings.SplitTestHardOverrides),
            SplitTestSegmentProbabilities: JsonObjectOrDefault(settings.SplitTestSegmentProbabilities),
            UpdatedAt: DateTime.UtcNow);
    }

    /// <summary>
    /// Clamps the limits to what the client can actually survive. Four of
    /// these are crash guards rather than taste:
    /// <list type="bullet">
    /// <item>Rpcs of 0 divides by zero in the CircuitsV2 debug subscreen,
    /// which renders <c>RpcRecentMax / Rpcs</c>.</item>
    /// <item>A time window sizes a per-frame ring buffer, so an
    /// unbounded value is an out-of-memory kill at circuit init.</item>
    /// <item>Chips sizes four list preallocations in the chip-culling
    /// manager, and InkPerChip × CloudValues is an unchecked int multiply,
    /// so both are capped.</item>
    /// <item>The client rejects the whole network array — falling back to
    /// the retail 5-RPC budget — unless some entry has
    /// NumPlayersForActivation 0, so the lowest entry is forced to 0.</item>
    /// </list>
    /// </summary>
    private static CircuitsV2LimitSettings NormalizeCircuitsV2Limits(
        CircuitsV2LimitSettings settings)
    {
        var seen = new HashSet<int>();
        var network = (settings.Network ?? [])
            .Where(entry => entry is not null)
            .Select(entry => new CircuitsV2NetworkLimit(
                NumPlayersForActivation: Math.Clamp(entry.NumPlayersForActivation, 0, 1000),
                Bytes: Math.Clamp(entry.Bytes, 1, UnlimitedBudget),
                BytesTimeWindowFrame60s: Math.Clamp(entry.BytesTimeWindowFrame60s, 1, MaxTimeWindowFrames),
                Rpcs: Math.Clamp(entry.Rpcs, 1, UnlimitedBudget),
                RpcTimeWindowFrame60s: Math.Clamp(entry.RpcTimeWindowFrame60s, 1, MaxTimeWindowFrames)))
            .OrderBy(entry => entry.NumPlayersForActivation)
            .Where(entry => seen.Add(entry.NumPlayersForActivation))
            .Take(16)
            .ToList();

        if (network.Count == 0)
            network = DefaultCircuitsV2Limits().Network;
        else if (network[0].NumPlayersForActivation != 0)
            network[0] = network[0] with { NumPlayersForActivation = 0 };

        return new CircuitsV2LimitSettings(
            Network: network,
            // Compute is the per-frame CPU budget: CircuitsV2Manager's frame
            // tick (CircuitsV2Manager.txt OCGDEIJJBAG ISIL 240, :21241)
            // builds a fresh BENFNKBKHEA(Compute) every frame, which sums
            // four int counters and compares them <= the budget
            // (Circuits.V2/BENFNKBKHEA.txt). Nothing is allocated from it,
            // but it is the only runaway-loop guard: a budget near
            // int.MaxValue lets the summed counters wrap negative and never
            // trip. 10M keeps 200x headroom under int.MaxValue.
            Compute: Math.Clamp(settings.Compute, 1, 10_000_000),
            // The chip-culling manager's ctor preallocates four
            // List<Chip>(Chips) (PAFDMBHNMBP.txt .ctor ISIL 053-108), so
            // 0 or a negative is an ArgumentOutOfRange and 1M is 32MB of
            // pointer arrays — the ceiling.
            Chips: Math.Clamp(settings.Chips, 1, 1_000_000),
            // CalculateActiveResourceCost computes
            // inkCost += InkPerChip * Math.Min(numChips, CloudValues) in
            // plain int (CreationManager.txt ISIL 389-415, :6136-6162).
            // With InkPerChip capped at 30000 below, 10000 keeps that
            // product at 3e8, well under int.MaxValue.
            CloudValues: Math.Clamp(settings.CloudValues, 0, 10_000),
            // A non-finite value would serialize as "NaN"/"Infinity", which
            // the client's float.TryParse happily accepts.
            CloudValuesAutosaveSeconds: float.IsFinite(settings.CloudValuesAutosaveSeconds)
                ? Math.Clamp(settings.CloudValuesAutosaveSeconds, 1f, 3600f)
                : 300f,
            UnculledChips: Math.Clamp(settings.UnculledChips, 1, 1_000_000),
            VisibleChips: Math.Clamp(settings.VisibleChips, 1, 1_000_000),
            // InkPerChip is an ink cost against the room's hard-coded
            // 30000-ink cap (CostInfo(0x7530, 64, 101, Chips, CloudValues)
            // in CreationManager.OnCircuitsV2LimitsRefreshed, ISIL 060,
            // :3374). Past 30000 a single chip is unaffordable, and the
            // cap also bounds the int multiply noted on CloudValues. No
            // client code divides by it, so 0 is safe.
            InkPerChip: Math.Clamp(settings.InkPerChip, 0, 30_000),
            UpdatedAt: DateTime.UtcNow);
    }

    private static CircuitsV2LimitSettings ToCircuitsV2Limits(ServerSettingsEntity row)
    {
        if (!string.IsNullOrWhiteSpace(row.CircuitsV2LimitsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<CircuitsV2LimitSettings>(
                    row.CircuitsV2LimitsJson,
                    JsonOptions);
                if (parsed is not null)
                    return NormalizeCircuitsV2Limits(parsed) with { UpdatedAt = row.UpdatedAt };
            }
            catch
            {
                // Bad settings should not break gameconfig bootstrap.
            }
        }

        return DefaultCircuitsV2Limits() with { UpdatedAt = row.UpdatedAt };
    }
}

public sealed record WeeklyChallengeSettings(
    bool CompletedRequired,
    IReadOnlyList<WeeklyChallengeTemplate> Challenges,
    WeeklyChallengeReward Reward,
    DateTime UpdatedAt);

public sealed record WeeklyChallengeTemplate(
    int Index,
    string Name,
    string Config,
    string Description,
    string Tooltip);

public sealed record WeeklyChallengeReward(
    int GiftDropId,
    int Xp,
    int Tokens,
    int Level,
    int GiftContext,
    int GiftRarity,
    string AvatarItemDesc,
    string ConsumableItemDesc,
    string EquipmentPrefabName,
    string EquipmentModificationGuid);

public sealed record PlayMenuTagSettings(
    List<string> PinnedTags,
    List<string> PopularTags,
    List<string> TrendingTags,
    DateTime UpdatedAt);

public sealed record RecCenterDoorSettings(
    List<RecCenterDoorConfig> Doors,
    DateTime UpdatedAt);

public sealed record RecCenterDoorConfig(
    string Key,
    string Title,
    string Query);

public sealed record GameConfigurationSetting(
    string Key,
    string Value,
    DateTime? StartTime = null,
    DateTime? EndTime = null);

/// <summary>
/// One entry of the <c>UGC.CircuitsV2.Limit.Network</c> array. The client
/// sorts entries ascending by <see cref="NumPlayersForActivation"/> and
/// activates the highest one whose threshold is at or below the room's max
/// player capacity, so a single entry with a threshold of 0 applies
/// everywhere. Field names are the literal JSON keys the client's
/// Newtonsoft reader binds to <c>LimitConfig.NetworkConfig</c>.
/// </summary>
public sealed record CircuitsV2NetworkLimit(
    int NumPlayersForActivation,
    int Bytes,
    int BytesTimeWindowFrame60s,
    int Rpcs,
    int RpcTimeWindowFrame60s);

/// <summary>
/// The <c>UGC.CircuitsV2.Limit.*</c> gameconfig family, read by
/// <c>RecRoom.CircuitsV2.Config.LimitConfig</c>. Every field maps to one
/// key; the client falls back to its baked-in defaults for any key the
/// server omits or sends unparseably.
/// </summary>
public sealed record CircuitsV2LimitSettings(
    List<CircuitsV2NetworkLimit> Network,
    int Compute,
    int Chips,
    int CloudValues,
    float CloudValuesAutosaveSeconds,
    int UnculledChips,
    int VisibleChips,
    int InkPerChip,
    DateTime UpdatedAt);

public sealed record DiscoveredGameConfigSettings(
    int FriendsPostGamePromptUnderFriendCount,
    int FriendsSuggestFriendCodeOnFriendsScreenCount,
    bool ScreensForceVerification,
    bool VrForceVerification,
    bool RewardsUseRewardSelection,
    int RewardsSelectionTimeout,
    bool RoomDetailsPhotoRollEnabled,
    float LoadingNetworkTimeout,
    float RunningNetworkTimeout,
    bool SynchronizedFieldRemoveDefaultEntries,
    bool RenderingDisableSrpBatcher,
    string SplitTestSoftOverrides,
    string SplitTestHardOverrides,
    string SplitTestSegmentProbabilities,
    DateTime UpdatedAt);
