using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using DorkNet.Server.Data;
using DorkNet.Server.Services;
using System.Security.Claims;
using System.Text.Json;

namespace DorkNet.Server.Hubs;

/// <summary>
/// notify.rec.net/hub/v1 — SignalR Core hub used by the client's
/// BestHTTP.SignalRCore.HubConnection (RecNet.SignalRHubConnection wraps it).
///
/// Each connection authenticates via the same JWT used for the rest of
/// the API; we extract the player id from the bearer claim and add the
/// connection to a per-player SignalR group so the rest of the app can
/// push to "player N" by group name without tracking connection ids
/// directly.
///
/// **Per-replica vs cross-replica state**: SignalR's group fan-out is
/// driven by its registered backplane — when Redis is configured in
/// <c>Program.cs</c> via <c>AddStackExchangeRedis</c>, a notification
/// published to <c>player:N</c> from any replica reaches every
/// replica's matching connections. Connection-tracking for
/// "is player N online?" queries lives in <see cref="OnlinePresenceService"/>
/// (also Redis-backed) so admin tools see the cluster-wide truth.
/// Pre-PR-2 used a local <c>static ConcurrentDictionary</c> which only
/// reflected this replica's connections.
///
/// Hub methods the 2023-03-21 client sends (fire-and-forget, RecNet.Runtime
/// OMHPBIEHOHN):
///   "SubscribeToPlayers" - {PlayerIds}: players to get presence for
///   "SubscribeTo"        - {Channels}: always empty in this build
///   "UnsubscribeFrom"    - {Channels}: always empty in this build
/// There is no UnsubscribeFromPlayers; SubscribeToPlayers always carries the
/// complete set. All accept JsonElement and tolerate casing differences.
///
/// The client reconnects at most 3 times per login (its attempt counter is
/// never reset), so the server must not drop healthy connections.
/// </summary>
[Authorize]
public class NotifyHub(
    OnlinePresenceService presence,
    PlayerPresenceService playerRooms,
    NotificationService notifications,
    PresenceBroadcastService presenceFanout,
    ILogger<NotifyHub> logger) : Hub
{
    /// <summary>SignalR group name for a given player id. Use this
    /// from <see cref="Services.NotificationService"/> to push.</summary>
    public static string GroupForPlayer(long playerId) => $"player:{playerId}";

    public override async Task OnConnectedAsync()
    {
        var pid = TryGetPlayerId();
        if (pid is long id)
        {
            // Enforce single-session-per-account. When a SECOND device
            // logs in with the same credentials, both the original
            // session and the new one would otherwise share the
            // <c>player:N</c> group. We can't differentiate by LoginLock
            // because the watch derives that token from cached login
            // state (device_id-tied) — two clients restored from the
            // same install hand back the same lock GUID.
            //
            // Instead: at the moment the SECOND session's WebSocket
            // arrives, take a snapshot of the EXISTING connection ids
            // (which still belong to the original session — we haven't
            // joined the group yet) and send those ids a Logout with
            // Reason SessionTakeover. The client shows its own "signed in
            // elsewhere" message and returns to the boot scene. (A
            // ModerationKick only sent the old session to its dorm, still
            // signed in.)
            //
            // Targeting specific connection ids (not the group) means
            // the new connection — about to join the group on the next
            // line — never receives it, even with Redis backplane
            // routing across replicas.
            var existing = presence.GetConnections(id);
            if (existing.Count > 0)
            {
                logger.LogWarning(
                    "[hub] second-session connect for player={PlayerId} (conn={NewConn}); logging out {OldCount} existing connection(s)",
                    id, Context.ConnectionId, existing.Count);
                try
                {
                    await notifications.LogoutConnectionsAsync(existing);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[hub] failed to kick stale connections for player={PlayerId}", id);
                }
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForPlayer(id));
            var cameOnline = await presence.AddConnectionAsync(id, Context.ConnectionId);
            logger.LogInformation(
                "[hub] connected player={PlayerId} conn={ConnectionId} group={Group} cameOnline={CameOnline}",
                id, Context.ConnectionId, GroupForPlayer(id), cameOnline);

            // Tell friends they're back. OnDisconnectedAsync pushes
            // PlayerWentOffline, but nothing used to push the opposite
            // transition, so the pair was asymmetric: a SignalR reconnect
            // (network blip, replica restart) greyed the player out on every
            // friend's list for the rest of the session even though they were
            // still playing.
            //
            // isOnline is sent explicitly now, so this no longer needs a
            // known room: a player who connects before matchmaking, or who is
            // sitting in the offline room, is online with roomInstance null.
            if (cameOnline)
                await presenceFanout.PublishCurrentAsync(id);
        }
        else
        {
            logger.LogWarning(
                "[hub] connected without player id conn={ConnectionId} user={User}",
                Context.ConnectionId, Context.User?.Identity?.Name);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var pid = TryGetPlayerId();
        if (pid is long id)
        {
            var wentOffline = await presence.RemoveConnectionAsync(id, Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupForPlayer(id));
            if (wentOffline)
            {
                // The socket can drop under a live session (the server or a
                // proxy closes it; the client logs "RecNet HubConnection error:
                // ... TCP Stream closed unexpectedly by the remote server" and
                // reconnects by itself). While the player is still
                // heartbeating, wait one activity window before calling them
                // offline; a real exit either logs out (immediate offline push)
                // or stops heartbeating and is caught by the deferred check.
                if (playerRooms.IsRecentlyActive(id))
                    presenceFanout.ScheduleOfflineCheck(id);
                else
                    await presenceFanout.PublishOfflineAsync(id);
            }
            logger.LogInformation(
                "[hub] disconnected player={PlayerId} conn={ConnectionId} wentOffline={WentOffline} error={Error}",
                id, Context.ConnectionId, wentOffline, exception?.Message);
        }
        else
        {
            logger.LogInformation(
                "[hub] disconnected unknown-player conn={ConnectionId} error={Error}",
                Context.ConnectionId, exception?.Message);
        }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>SignalR group a connection joins to receive one player's
    /// presence. Only presence is sent here — a subscriber must never receive
    /// the subject's own pushes (DMs, gifts, …).</summary>
    public static string PresenceGroupForPlayer(long playerId) => $"presence:{playerId}";

    private const string SubscriptionsKey = "presence-subscriptions";

    /// <summary>Most players one connection may subscribe to. The client sends
    /// its relationships plus the players around it (dozens); anything far
    /// past that is not a real client, and each id costs a group membership
    /// on the backplane.</summary>
    public const int MaxPresenceSubscriptions = 5000;

    /// <summary><c>SubscribeToPlayers(PlayerSubscriptionListDTO {PlayerIds:
    /// List&lt;int&gt;})</c> (RecNet.Runtime OMHPBIEHOHN.txt:3430): the full set
    /// of players this client wants presence for — its relationships, the
    /// players around it, and itself. Sent on every (re)connect and whenever
    /// the set changes, always as the complete set, so it REPLACES the
    /// connection's subscriptions. Presence for those players is then pushed
    /// to <see cref="PresenceGroupForPlayer"/> by
    /// <see cref="PresenceBroadcastService"/>, so non-friends the client is
    /// looking at (people in the room, an open profile) stay current too.
    ///
    /// The caller's own id is skipped: a presence frame about the local player
    /// is applied as a correction and moves them. An unreadable payload
    /// leaves the current subscriptions alone rather than clearing them.
    /// Groups live per connection on its own replica, are routed cluster-wide
    /// by the Redis backplane, and are dropped by SignalR on disconnect.</summary>
    public async Task SubscribeToPlayers(JsonElement payload)
    {
        var me = TryGetPlayerId();
        if (!TryReadPlayerIds(payload, out var requested))
        {
            logger.LogWarning(
                "[hub] SubscribeToPlayers unreadable payload player={PlayerId} conn={ConnectionId} payload={Payload}",
                me, Context.ConnectionId, RawPayload(payload));
            return;
        }
        if (me is long self) requested.Remove(self);
        if (requested.Count > MaxPresenceSubscriptions)
        {
            logger.LogWarning(
                "[hub] SubscribeToPlayers over the {Max} limit player={PlayerId} conn={ConnectionId} count={Count}; kept the current set",
                MaxPresenceSubscriptions, me, Context.ConnectionId, requested.Count);
            return;
        }

        var current = Context.Items.TryGetValue(SubscriptionsKey, out var existing) && existing is HashSet<long> set
            ? set
            : new HashSet<long>();
        var added = requested.Except(current).ToList();
        var removed = current.Except(requested).ToList();
        foreach (var id in added)
            await Groups.AddToGroupAsync(Context.ConnectionId, PresenceGroupForPlayer(id));
        foreach (var id in removed)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, PresenceGroupForPlayer(id));
        Context.Items[SubscriptionsKey] = requested;

        logger.LogInformation(
            "[hub] SubscribeToPlayers player={PlayerId} conn={ConnectionId} subscribed={Count} added={Added} removed={Removed}",
            me, Context.ConnectionId, requested.Count, added.Count, removed.Count);
    }

    /// <summary><c>SubscribeTo({Channels: List&lt;string&gt;})</c>. The
    /// 2023-03-21 client composes it on every connect but its channel set is
    /// always empty, so there is nothing to subscribe to; logged only.</summary>
    public Task SubscribeTo(JsonElement payload)
    {
        logger.LogInformation(
            "[hub] SubscribeTo player={PlayerId} conn={ConnectionId} payload={Payload}",
            TryGetPlayerId(), Context.ConnectionId, RawPayload(payload));
        return Task.CompletedTask;
    }

    /// <summary>Counterpart of <see cref="SubscribeTo"/>; logged only.</summary>
    public Task UnsubscribeFrom(JsonElement payload)
    {
        logger.LogInformation(
            "[hub] UnsubscribeFrom player={PlayerId} conn={ConnectionId} payload={Payload}",
            TryGetPlayerId(), Context.ConnectionId, RawPayload(payload));
        return Task.CompletedTask;
    }

    /// <summary>Reads <c>{PlayerIds:[…]}</c> (any key casing), or a bare
    /// array, as player ids; numbers or numeric strings.</summary>
    public static bool TryReadPlayerIds(JsonElement payload, out HashSet<long> ids)
    {
        ids = new HashSet<long>();
        var list = payload;
        if (payload.ValueKind == JsonValueKind.Object)
        {
            var match = payload.EnumerateObject()
                .Where(prop => string.Equals(prop.Name, "PlayerIds", StringComparison.OrdinalIgnoreCase))
                .Select(prop => (JsonElement?)prop.Value)
                .FirstOrDefault();
            if (match is not JsonElement value) return false;
            list = value;
        }
        if (list.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var n) && n > 0) ids.Add(n);
            else if (item.ValueKind == JsonValueKind.String && long.TryParse(item.GetString(), out var p) && p > 0) ids.Add(p);
        }
        return true;
    }

    private long? TryGetPlayerId()
    {
        var raw = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(raw, out var id) ? id : null;
    }

    private static string RawPayload(JsonElement payload)
    {
        try
        {
            return payload.ValueKind == JsonValueKind.Undefined ? "<undefined>" : payload.GetRawText();
        }
        catch
        {
            return "<unreadable>";
        }
    }
}
