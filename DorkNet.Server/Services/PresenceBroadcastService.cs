using DorkNet.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DorkNet.Server.Services;

/// <summary>
/// Online-status visibility enum (2023 client, dump.cs IPLLHDINPBL). Picked in
/// the profile's "Set Online Status Visibility" dialog (Assembly-CSharp
/// RRUI/Data/BaseAccountModel.txt, ShowChangeOnlineStatusVisibilityDialog) and
/// sent as PUT player/statusvisibility form statusVisibility (RecNet.Runtime
/// RecNet/Matchmaking.txt:17147-17161).
/// </summary>
public static class PresenceVisibility
{
    public const int Public = 0;
    public const int FriendsOnly = 1;
    public const int FavoriteFriendsOnly = 2;
    public const int Offline = 3;

    public static bool IsValid(int value) => value is >= Public and <= Offline;
}

/// <summary>
/// Tells a player's friends when that player's presence changes, from one
/// place, so every transition sends the same frame built from the same state.
///
/// Before this existed only the immediate <c>/goto</c> commit pushed anything.
/// Deferred commits (matchmake/dorm, private-instance hops) committed in
/// reportjoinresult without a push, matchmake/none and logout changed presence
/// silently, and SignalR connect/disconnect pushed with <c>isOnline</c> derived
/// from "has a room" — so a friend's list showed people in rooms they had left,
/// offline while they sat in their dorm, and never noticed a status-visibility
/// change.
///
/// The client's PresenceUpdate handler (Matchmaking.EGPABEDPEJO, registered at
/// RecNet.Runtime/RecNet/Matchmaking.txt:1436) feeds the payload straight into
/// FLAOOBNJAAD (:6917), the same routine the heartbeat uses to apply an out-of-sync
/// correction (:18467). A frame about the LOCAL player therefore moves that player, so
/// these frames never go to the subject. They go to the subject's friends and to every
/// other connection that subscribed to the subject with SubscribeToPlayers (see
/// NotifyHub.SubscribeToPlayers); non-friends only see the real presence when it is
/// Public.
/// </summary>
public sealed class PresenceBroadcastService(
    IServiceScopeFactory scopes,
    PlayerPresenceService presence,
    OnlinePresenceService connections,
    NotificationService notifications,
    ILogger<PresenceBroadcastService> logger)
{
    /// <summary>PlayerSettings key for the persisted
    /// <c>/player/statusvisibility</c> choice.</summary>
    public const string StatusVisibilityPrefKey = "pref:statusVisibility";

    /// <summary>A player counts as online while they heartbeat or hold a
    /// SignalR connection. Being in a room is NOT required: the client's
    /// presence DTO carries <c>isOnline</c> separately (LCEHINCCHOB +0x50) and
    /// DNKHCBFPLJG reads "online, in no room / the offline room" as a valid
    /// state, which is exactly what a player in the offline dorm is.</summary>
    public bool IsOnline(long playerId) =>
        presence.IsRecentlyActive(playerId) || connections.GetConnections(playerId).Count > 0;

    /// <summary>Push the player's current presence (online state + committed
    /// room) to every friend and subscriber.</summary>
    public Task PublishCurrentAsync(long playerId) => PublishAsync(playerId, forceOffline: false);

    /// <summary>Push an offline frame regardless of any lingering markers —
    /// for an explicit logout.</summary>
    public Task PublishOfflineAsync(long playerId) => PublishAsync(playerId, forceOffline: true);

    /// <summary>
    /// The last SignalR connection dropped but the player may still be
    /// playing: the socket can be closed under a live session ("RecNet
    /// HubConnection error: ... TCP Stream closed unexpectedly by the remote
    /// server") and the client reconnects on its own. Pushing offline right
    /// away greys the player out for every friend until that reconnect. Wait
    /// one activity window and only then decide: still silent means the game
    /// is really gone.
    /// </summary>
    public void ScheduleOfflineCheck(long playerId)
    {
        var delay = PlayerPresenceService.ActivityTtl + TimeSpan.FromSeconds(5);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay);
                if (IsOnline(playerId)) return;
                await PublishAsync(playerId, forceOffline: true);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[presence-fanout] deferred offline check failed player={PlayerId}", playerId);
            }
        });
    }

    private async Task PublishAsync(long playerId, bool forceOffline)
    {
        if (playerId <= 0) return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
            var settings = scope.ServiceProvider.GetRequiredService<ServerSettingsService>();

            var friendIds = (await RelationshipQueries.EffectiveFriendIdsAsync(db, settings, playerId))
                .Where(id => id != playerId)
                .Distinct()
                .ToList();

            var online = !forceOffline && IsOnline(playerId);
            var room = online ? presence.GetRoom(playerId) : null;
            var visibility = await GetStatusVisibilityAsync(db, playerId);

            // Honour the player's visibility choice the same way GET /player
            // does (PlayerPresenceController.ResolveHiddenAsync): recipients
            // who may not see them get the frame a genuinely offline player
            // produces. Every recipient here is already a friend, so
            // FriendsOnly needs no split; FavoriteFriendsOnly reaches only the
            // friends this player favorited (Favorited lives on the
            // favoriter's own row, RequesterId = favoriter).
            var favorited = visibility == PresenceVisibility.FavoriteFriendsOnly
                ? (await db.Relationships.AsNoTracking()
                    .Where(r => r.RequesterId == playerId && r.Favorited && friendIds.Contains(r.TargetId))
                    .Select(r => r.TargetId)
                    .ToListAsync()).ToHashSet()
                : new HashSet<long>();
            var (visibleTo, hiddenFrom) = SplitRecipients(friendIds, visibility, favorited);

            logger.LogInformation(
                "[presence-fanout] player={PlayerId} online={Online} room={RoomId} instance={InstanceId} visibility={Visibility} -> {FriendCount} friend(s), {HiddenCount} hidden",
                playerId, online, room?.RoomId, room?.RoomInstanceId, visibility, visibleTo.Count, hiddenFrom.Count);

            if (visibleTo.Count > 0)
                await notifications.PlayerPresenceChanged(visibleTo, playerId, room, online, visibility);
            if (hiddenFrom.Count > 0)
                await notifications.PlayerPresenceChanged(hiddenFrom, playerId, null, false, visibility);

            // Everyone else who subscribed to this player (SubscribeToPlayers:
            // people in the same room, an open profile). They are not friends,
            // so they see the real presence only when it is Public, and the
            // offline frame otherwise. Friends were served above and the
            // subject must never get a frame about themselves, so their
            // connections are excluded.
            // Only friends who are online have connections; one call for the
            // online set, then one pipelined lookup for all of them.
            var onlineIds = connections.OnlinePlayerIds().ToHashSet();
            var exclude = (await connections.GetConnectionsAsync(
                    friendIds.Where(onlineIds.Contains).Append(playerId)))
                .Distinct()
                .ToList();
            var publicToAll = visibility == PresenceVisibility.Public && online;
            await notifications.PresenceToSubscribersAsync(
                playerId, publicToAll ? room : null, publicToAll, visibility, exclude);
        }
        catch (Exception ex)
        {
            // Best-effort: a fan-out failure must never fail the matchmake,
            // logout or hub event that triggered it.
            logger.LogWarning(ex, "[presence-fanout] push failed player={PlayerId}", playerId);
        }
    }

    /// <summary>Split a player's friends into those who may see their real
    /// presence and those who must see them offline, per their status
    /// visibility. <paramref name="favorited"/> holds the friends the player
    /// favorited; it only matters for FavoriteFriendsOnly.</summary>
    public static (List<long> VisibleTo, List<long> HiddenFrom) SplitRecipients(
        IReadOnlyList<long> friendIds, int visibility, IReadOnlySet<long> favorited) =>
        visibility switch
        {
            PresenceVisibility.Offline => (new List<long>(), friendIds.ToList()),
            PresenceVisibility.FavoriteFriendsOnly => (
                friendIds.Where(favorited.Contains).ToList(),
                friendIds.Where(id => !favorited.Contains(id)).ToList()),
            _ => (friendIds.ToList(), new List<long>()),
        };

    /// <summary>Stored status visibility for one player (0 = default).</summary>
    public static async Task<int> GetStatusVisibilityAsync(DorkNetDbContext db, long playerId)
    {
        var raw = await db.PlayerSettings.AsNoTracking()
            .Where(s => s.PlayerId == playerId && s.Key == StatusVisibilityPrefKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        return int.TryParse(raw, out var v) && PresenceVisibility.IsValid(v) ? v : PresenceVisibility.Public;
    }

    /// <summary>Stored status visibility for many players in one query.
    /// Players with no stored choice are absent from the result.</summary>
    public static async Task<Dictionary<long, int>> GetStatusVisibilitiesAsync(
        DorkNetDbContext db, IReadOnlyCollection<long> playerIds)
    {
        if (playerIds.Count == 0) return new Dictionary<long, int>();
        var rows = await db.PlayerSettings.AsNoTracking()
            .Where(s => s.Key == StatusVisibilityPrefKey && playerIds.Contains(s.PlayerId))
            .Select(s => new { s.PlayerId, s.Value })
            .ToListAsync();
        var result = new Dictionary<long, int>(rows.Count);
        foreach (var row in rows)
        {
            if (int.TryParse(row.Value, out var v) && PresenceVisibility.IsValid(v)) result[row.PlayerId] = v;
        }
        return result;
    }
}
