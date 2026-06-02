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

    public async Task<PlayMenuTagSettings> SetPlayMenuTagsAsync(
        IReadOnlyList<string> pinned,
        IReadOnlyList<string> popular)
    {
        var normalized = NormalizePlayMenuTags(pinned, popular);
        var existing = await GetTrackedRowAsync();
        existing.PlayMenuTagsJson = JsonSerializer.Serialize(normalized, JsonOptions);
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
                    return NormalizePlayMenuTags(parsed.PinnedTags, parsed.PopularTags)
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

    private static List<WeeklyChallengeTemplate> NormalizeWeeklyChallenges(
        IReadOnlyList<WeeklyChallengeTemplate> challenges)
    {
        var normalized = challenges
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .Take(10)
            .Select((c, i) => new WeeklyChallengeTemplate(
                i,
                c.Name.Trim(),
                string.IsNullOrWhiteSpace(c.Config) ? "{}" : c.Config.Trim(),
                c.Description?.Trim() ?? string.Empty,
                c.Tooltip?.Trim() ?? string.Empty))
            .ToList();

        return normalized.Count > 0 ? normalized : DefaultWeeklyChallenges();
    }

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
            Config: "{\"Type\":\"CompleteActivity\",\"Goal\":3}",
            Description: "Complete three Rec Room Original activities.",
            Tooltip: "Play any RRO activity to make progress."),
        new(
            Index: 1,
            Name: "Cheer 5 Players",
            Config: "{\"Type\":\"CheerPlayers\",\"Goal\":5}",
            Description: "Send cheers to five players.",
            Tooltip: "Use the watch to cheer players you meet."),
        new(
            Index: 2,
            Name: "Visit 5 Rooms",
            Config: "{\"Type\":\"VisitRooms\",\"Goal\":5}",
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
            UpdatedAt: DateTime.UtcNow);

    private static PlayMenuTagSettings NormalizePlayMenuTags(
        IReadOnlyList<string> pinned,
        IReadOnlyList<string> popular)
    {
        static List<string> Normalize(IReadOnlyList<string> tags, int max)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return tags
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
        return new PlayMenuTagSettings(
            pinnedTags.Count > 0 ? pinnedTags : defaults.PinnedTags,
            popularTags.Count > 0 ? popularTags : defaults.PopularTags,
            DateTime.UtcNow);
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
    DateTime UpdatedAt);
