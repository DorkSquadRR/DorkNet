using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;

namespace DorkNet.Server.Services;

/// <summary>
/// The single account-erasure cascade used by every "remove this player"
/// path in the server: the admin SPA's
/// <c>DELETE api/admin/v1/players/{id}</c> and the 2023 client's
/// <c>DELETE account/me</c> ("delete local account").
///
/// Personal rows are deleted outright; authored durable content (rooms,
/// clubs, inventions, playlists, chat threads …) is reassigned to the
/// system account so other players' worlds don't lose their contents when
/// one author leaves. Kept as a static helper — not a DI service — so both
/// call sites share one implementation without either controller having to
/// grow a constructor dependency.
///
/// Callers are responsible for the surrounding transaction, for removing
/// the <c>Players</c> row itself, and for kicking the live session.
/// </summary>
public static class PlayerAccountPurge
{
    /// <summary>Delete/reassign every row that references
    /// <paramref name="playerId"/>. Returns a one-line audit summary.</summary>
    public static async Task<string> PurgeAsync(
        DorkNetDbContext db, long playerId, long systemPlayerId)
    {
        var now = DateTime.UtcNow;
        var deleted = 0;
        var reassigned = 0;

        deleted += await db.Avatars.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.PlayerSettings.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.Relationships.Where(x => x.RequesterId == playerId || x.TargetId == playerId).ExecuteDeleteAsync();
        deleted += await db.CurrencyBalances.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.PlayerInventory.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.PlayerDevices.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.NotificationPrefs.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.CohortAssignments.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.PushTokens.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.PlatformIgnores.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.Cards.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.ObjectiveProgress.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.GameRewardSelections.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.CouponRedemptions.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.LeaderboardStats.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.PlayerElo.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.RoyalePlayerProgress.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.RoyaleMatchPlayers.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();

        deleted += await db.Messages.Where(x => x.SenderPlayerId == playerId || x.RecipientPlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.ChatMessages.Where(x => x.SenderPlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.ChatThreadMembers.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        reassigned += await db.ChatThreads
            .Where(x => x.CreatorPlayerId == playerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatorPlayerId, systemPlayerId));

        deleted += await db.Cheers.Where(x => x.FromPlayerId == playerId || x.TargetPlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.Reports.Where(x => x.ReporterPlayerId == playerId || x.TargetPlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.BugReports.Where(x => x.ReporterPlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.Subscriptions.Where(x => x.SubscriberPlayerId == playerId || x.TargetPlayerId == playerId).ExecuteDeleteAsync();

        deleted += await db.ClubMemberships.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.ClubSubscriptions.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.ClubAnnouncementReads.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        reassigned += await db.Clubs
            .Where(x => x.CreatorPlayerId == playerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CreatorPlayerId, systemPlayerId)
                .SetProperty(x => x.UpdatedAt, now));
        reassigned += await db.ClubAnnouncements
            .Where(x => x.AuthorPlayerId == playerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.AuthorPlayerId, systemPlayerId));

        deleted += await db.RoomBookmarks.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.RoomVisits.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.RoomRoles.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        deleted += await db.RoomBans.Where(x => x.BannedPlayerId == playerId).ExecuteDeleteAsync();
        reassigned += await db.RoomBans
            .Where(x => x.BannedByPlayerId == playerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.BannedByPlayerId, systemPlayerId));
        reassigned += await db.RoomRoles
            .Where(x => x.GrantedByPlayerId == playerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.GrantedByPlayerId, systemPlayerId));
        reassigned += await db.Rooms
            .Where(x => x.CreatorPlayerId == playerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CreatorPlayerId, systemPlayerId)
                .SetProperty(x => x.UpdatedAt, now));
        reassigned += await db.RoomDataBlobs
            .Where(x => x.UploadedByPlayerId == playerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UploadedByPlayerId, systemPlayerId));
        reassigned += await db.RoomKeys
            .Where(x => x.CreatorPlayerId == playerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatorPlayerId, systemPlayerId));
        deleted += await db.RoomKeyPurchases.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();

        deleted += await db.PlaylistInteractions.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        reassigned += await db.Playlists
            .Where(x => x.CreatorPlayerId == playerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatorPlayerId, systemPlayerId));

        deleted += await db.PlayerEventResponses
            .Where(x => x.PlayerId == playerId
                || db.PlayerEvents.Where(e => e.CreatorPlayerId == playerId).Select(e => e.Id).Contains(x.EventId))
            .ExecuteDeleteAsync();
        deleted += await db.PlayerEvents.Where(x => x.CreatorPlayerId == playerId).ExecuteDeleteAsync();

        deleted += await db.PrivateInstanceInvitees.Where(x => x.PlayerId == playerId).ExecuteDeleteAsync();
        var privateInstanceIds = await db.PrivateInstances
            .Where(x => x.OwnerPlayerId == playerId)
            .Select(x => x.Id)
            .ToListAsync();
        if (privateInstanceIds.Count > 0)
        {
            deleted += await db.PrivateInstanceInvitees
                .Where(x => privateInstanceIds.Contains(x.PrivateInstanceId))
                .ExecuteDeleteAsync();
            deleted += await db.PrivateInstances
                .Where(x => privateInstanceIds.Contains(x.Id))
                .ExecuteDeleteAsync();
        }

        deleted += await db.GiftPackages
            .Where(x => x.RecipientPlayerId == playerId)
            .ExecuteDeleteAsync();
        if (playerId <= int.MaxValue)
        {
            reassigned += await db.GiftPackages
                .Where(x => x.FromPlayerId == (int)playerId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.FromPlayerId, (int?)null));
        }

        reassigned += await db.Inventions
            .Where(x => x.CreatorPlayerId == playerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CreatorPlayerId, systemPlayerId)
                .SetProperty(x => x.UpdatedAt, now));
        reassigned += await db.Photos
            .Where(x => x.UploaderPlayerId == playerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.UploaderPlayerId, systemPlayerId)
                .SetProperty(x => x.DeletedAt, now)
                .SetProperty(x => x.IsPublic, false));

        // Custom avatar items: the purged player's ownership rows go away;
        // items they CREATED are reassigned to the system account like
        // Inventions, because other players may own and be wearing them —
        // deleting the item would break those avatars.
        deleted += await db.CustomAvatarItemOwnership
            .Where(x => x.PlayerId == playerId)
            .ExecuteDeleteAsync();
        reassigned += await db.CustomAvatarItems
            .Where(x => x.CreatorPlayerId == playerId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.CreatorPlayerId, systemPlayerId)
                .SetProperty(x => x.UpdatedAt, now));

        reassigned += await db.SignupCodes
            .Where(x => x.RedeemedByPlayerId == playerId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RedeemedByPlayerId, (long?)null));

        return $"deleted_rows={deleted} reassigned_rows={reassigned}";
    }
}
