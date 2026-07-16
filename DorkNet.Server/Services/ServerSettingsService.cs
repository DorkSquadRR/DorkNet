using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
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
            ToDiscoveredGameConfigs(row));
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

    public static IReadOnlyList<GameConfigurationSetting> ToGameConfigurations(
        RecCenterDoorSettings doors,
        DiscoveredGameConfigSettings discovered)
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
        ]);
        return rows;
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
