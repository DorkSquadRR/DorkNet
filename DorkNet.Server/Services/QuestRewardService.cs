using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Services;

/// <summary>
/// Per-quest reward chest contents. The end-of-quest gift box
/// (RRO quest / Stunt Runner) is the existing <c>gifts/generate</c> +
/// <c>gifts/consume</c> flow: the client asks the server to generate a
/// gift, the box spawns showing it, and opening it grants the item.
///
/// The generate request carries no quest id (only GiftContext /
/// IsGameGift / Message), so the quest is identified by the room the
/// player is in — <see cref="PlayerPresenceService.GetRoom"/>. This
/// service maps that room's <b>name</b> (the seeded RRO slug, e.g.
/// <c>GoldenTrophy</c> / <c>Crescendo</c> / <c>StuntRunner</c>) to the
/// quest's item pool (<c>data/quest_rewards.json</c>) and resolves one
/// to a real store item to award. Keyed by name rather than the numeric
/// id because seed ids are array-order-derived and brittle. When a room
/// has no configured pool the caller falls back to the generic
/// random-wardrobe gift.
/// </summary>
public sealed class QuestRewardService(DorkNetDbContext db)
{
    private sealed class QuestRewardFile
    {
        public Dictionary<string, List<string>> rooms { get; set; } = new();
    }

    private static readonly Lazy<Dictionary<string, List<string>>> _map = new(Load);

    /// <summary>Room name (case-insensitive) → candidate item slugs.</summary>
    public static IReadOnlyDictionary<string, List<string>> Map => _map.Value;

    /// <summary>Resolve one active store item for the quest chest in the
    /// room with id <paramref name="roomId"/>, chosen deterministically
    /// from <paramref name="seed"/> so repeated generate calls in the
    /// same context are stable. Returns null when the room has no
    /// configured pool or none of its slugs resolve to an active
    /// item.</summary>
    public async Task<StoreItemEntity?> PickForRoomAsync(long roomId, int seed)
    {
        if (Map.Count == 0) return null;
        var name = await db.Rooms.Where(r => r.Id == roomId).Select(r => r.Name).FirstOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(name) || !Map.TryGetValue(name, out var slugs) || slugs.Count == 0)
            return null;

        var items = await db.StoreItems
            .Where(i => i.IsActive && slugs.Contains(i.Slug))
            .ToListAsync();
        if (items.Count == 0) return null;

        // Order deterministically by (id, seed) hash so the same room +
        // seed always yields the same pick without persisting state.
        return items
            .OrderBy(i => HashCode.Combine(i.Id, seed))
            .First();
    }

    private static Dictionary<string, List<string>> Load()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "data", "quest_rewards.json"),
            Path.Combine(AppContext.BaseDirectory, "Data", "quest_rewards.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "data", "quest_rewards.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "DorkNet.Server", "Data", "quest_rewards.json"),
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) return new(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var fs = File.OpenRead(path);
            var data = JsonSerializer.Deserialize<QuestRewardFile>(fs,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new QuestRewardFile();

            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, slugs) in data.rooms)
            {
                if (string.IsNullOrWhiteSpace(key) || key.StartsWith('_')) continue;
                var clean = (slugs ?? new())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (clean.Count > 0) map[key.Trim()] = clean;
            }
            return map;
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
