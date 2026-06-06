using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DorkNet.Server.Services;

/// <summary>
/// Effective-friend resolution that honors the
/// <see cref="ServerSettingsEntity.GlobalFriendsEnabled"/> toggle.
///
/// When the toggle is ON, every account on the server is treated as a
/// friend of every other account WITHOUT writing any relationship rows —
/// the friend graph is synthesized at read time. This keeps a small private
/// server frictionless (no searching + manual friend requests) and reverts
/// instantly when the toggle flips off, since nothing was persisted.
///
/// Blocked relationships still suppress the pairing, and the system/coach
/// account (<see cref="SystemAccountId"/>) is never offered as a friend.
///
/// Live propagation: flipping the toggle broadcasts
/// <c>PushNotificationId.RelationshipsInvalid</c>, which the 2020 watch
/// handles by calling <c>Relationships.RefreshList</c> — a full re-fetch of
/// <c>api/relationships/v2/get</c> — so connected players see the change
/// without relogging (verified against the readable March decompile:
/// <c>RecNet/Relationships.txt → OnRelationshipsInvalid → RefreshList</c>).
/// </summary>
public static class RelationshipQueries
{
    /// <summary>Coach/system account seeded at startup (Player.Id = 1).
    /// Excluded from the global-friends set so it never shows up as a
    /// friend in anyone's list.</summary>
    public const long SystemAccountId = 1;

    /// <summary>True when <paramref name="username"/> is still an
    /// auto-generated placeholder name (<c>Player_</c> followed by digits
    /// only — see <c>PlayerService.GenerateUsername</c>). These are accounts
    /// that never set a real username; the global-friends toggle hides them
    /// from the synthesized "everyone" list so they don't clutter it. A
    /// genuine Friend row still surfaces them — only the synthetic pairing is
    /// suppressed.</summary>
    public static bool IsPlaceholderUsername(string? username)
    {
        const string prefix = "Player_";
        if (string.IsNullOrEmpty(username) ||
            !username.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var suffix = username.AsSpan(prefix.Length);
        if (suffix.IsEmpty) return false;
        foreach (var c in suffix)
            if (!char.IsAsciiDigit(c)) return false;
        return true;
    }

    /// <summary>The effective friend ids for <paramref name="me"/>. With the
    /// global-friends toggle off this is the real Friend graph; with it on
    /// it's every other (non-system, non-blocked) account, excluding
    /// placeholder <c>Player_NNN</c> accounts that aren't already real
    /// friends.</summary>
    public static async Task<List<long>> EffectiveFriendIdsAsync(
        DorkNetDbContext db, ServerSettingsService settings, long me)
    {
        if (!await settings.IsGlobalFriendsEnabledAsync())
        {
            return await db.Relationships
                .Where(r => r.Status == RelationshipStatus.Friend &&
                            (r.RequesterId == me || r.TargetId == me))
                .Select(r => r.RequesterId == me ? r.TargetId : r.RequesterId)
                .ToListAsync();
        }

        // Global friends: everyone except myself, the system account, and
        // anyone in a Blocked relationship with me (either direction).
        var blocked = await db.Relationships
            .Where(r => r.Status == RelationshipStatus.Blocked &&
                        (r.RequesterId == me || r.TargetId == me))
            .Select(r => r.RequesterId == me ? r.TargetId : r.RequesterId)
            .ToListAsync();
        var realFriendSet = (await db.Relationships
            .Where(r => r.Status == RelationshipStatus.Friend &&
                        (r.RequesterId == me || r.TargetId == me))
            .Select(r => r.RequesterId == me ? r.TargetId : r.RequesterId)
            .ToListAsync()).ToHashSet();

        var candidates = await db.Players
            .Where(p => p.Id != me && p.Id != SystemAccountId && !blocked.Contains(p.Id))
            .Select(p => new { p.Id, p.Username })
            .ToListAsync();

        // Suppress placeholder accounts unless they're an actual friend.
        return candidates
            .Where(p => realFriendSet.Contains(p.Id) || !IsPlaceholderUsername(p.Username))
            .Select(p => p.Id)
            .ToList();
    }
}
