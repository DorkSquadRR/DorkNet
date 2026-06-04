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

    /// <summary>The effective friend ids for <paramref name="me"/>. With the
    /// global-friends toggle off this is the real Friend graph; with it on
    /// it's every other (non-system, non-blocked) account.</summary>
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

        return await db.Players
            .Where(p => p.Id != me && p.Id != SystemAccountId && !blocked.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync();
    }
}
