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
            Slug: reward.Slug?.Trim() ?? string.Empty,
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
            Name: "Visit any room",
            Config: "{\"Type\":\"VisitRooms\",\"Goal\":1}",
            Description: "Step into any room this week.",
            Tooltip: "Any room visit counts toward this challenge."),
        new(
            Index: 1,
            Name: "Cheer a player",
            Config: "{\"Type\":\"CheerPlayers\",\"Goal\":1}",
            Description: "Send a cheer to anyone you meet this week.",
            Tooltip: "Use the watch to cheer players you meet."),
        new(
            Index: 2,
            Name: "Finish an activity",
            Config: "{\"Type\":\"CompleteActivity\",\"Goal\":1}",
            Description: "Complete any activity this week.",
            Tooltip: "Play any activity to make progress."),
    ];

    public static WeeklyChallengeReward DefaultWeeklyReward() =>
        new(
            GiftDropId: 0,
            Slug: string.Empty,
            Xp: 250,
            Tokens: 250,
            Level: 1,
            GiftContext: 0,
            GiftRarity: 0,
            AvatarItemDesc: string.Empty,
            ConsumableItemDesc: string.Empty,
            EquipmentPrefabName: string.Empty,
            EquipmentModificationGuid: string.Empty);
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

/// <summary>The gift granted when the week's challenges complete.
/// <para><b>Skin grant (the fix vs. the December build):</b> December
/// stored only a masked <c>GiftDropId</c> and could never recover the
/// store item, so avatar/consumable rewards were shown on the watch but
/// never actually landed in the player's inventory. March carries the
/// item's <see cref="Slug"/> so <c>ProgressionController</c> can grant
/// the real item server-side via
/// <see cref="StoreService.GrantItemFreeBySlugAsync"/>. The
/// <c>AvatarItemDesc</c>/<c>ConsumableItemDesc</c>/<c>Equipment*</c>
/// fields still ride the wire for the client's gift-card render.</para></summary>
public sealed record WeeklyChallengeReward(
    int GiftDropId,
    string Slug,
    int Xp,
    int Tokens,
    int Level,
    int GiftContext,
    int GiftRarity,
    string AvatarItemDesc,
    string ConsumableItemDesc,
    string EquipmentPrefabName,
    string EquipmentModificationGuid);
