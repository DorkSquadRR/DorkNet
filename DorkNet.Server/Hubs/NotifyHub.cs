using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
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
/// Hub methods invoked by the client (verified by string-literal scan
/// of metadata at addresses 0x39b2470, 0x39b24c8, 0x39b4ba8):
///   "Subscribe"          - generic topic subscribe
///   "Unsubscribe"        - generic topic unsubscribe
///   "SubscribeToPlayers" - subscribe to per-player events (used by
///                          RecNet.NotificationManager / SubscribeToPlayers)
/// All accept JsonElement so we don't lock ourselves into a parameter
/// schema we haven't verified yet, and return Task so the client's
/// awaited Invoke completes cleanly.
/// </summary>
[Authorize]
public class NotifyHub(
    OnlinePresenceService presence,
    NotificationService notifications,
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
            // joined the group yet) and push a targeted ModerationKick
            // to those ids. The watch's OnModerationKick handler at
            // PlayerReporting.txt:1857 shows the standard moderation
            // dialog and routes through LocalModerationKickSelf, the
            // same graceful disconnect path the original Rec Room used
            // for real moderator kicks.
            //
            // Targeting specific connection ids (not the group) means
            // the new connection — about to join the group on the next
            // line — never receives the kick, even with Redis backplane
            // routing across replicas.
            var existing = presence.GetConnections(id);
            if (existing.Count > 0)
            {
                logger.LogWarning(
                    "[hub] second-session connect for player={PlayerId} (conn={NewConn}); kicking {OldCount} existing connection(s)",
                    id, Context.ConnectionId, existing.Count);
                try
                {
                    await notifications.KickConnectionsAsync(
                        existing,
                        "Your account was signed in on another device.");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[hub] failed to kick stale connections for player={PlayerId}", id);
                }
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, GroupForPlayer(id));
            await presence.AddConnectionAsync(id, Context.ConnectionId);
            logger.LogInformation(
                "[hub] connected player={PlayerId} conn={ConnectionId} group={Group}",
                id, Context.ConnectionId, GroupForPlayer(id));
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
            await presence.RemoveConnectionAsync(id, Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupForPlayer(id));
            logger.LogInformation(
                "[hub] disconnected player={PlayerId} conn={ConnectionId} error={Error}",
                id, Context.ConnectionId, exception?.Message);
        }
        else
        {
            logger.LogInformation(
                "[hub] disconnected unknown-player conn={ConnectionId} error={Error}",
                Context.ConnectionId, exception?.Message);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public Task SubscribeToPlayers(JsonElement payload)
    {
        logger.LogInformation(
            "[hub] SubscribeToPlayers player={PlayerId} conn={ConnectionId} payload={Payload}",
            TryGetPlayerId(), Context.ConnectionId, RawPayload(payload));
        return Task.CompletedTask;
    }

    public Task UnsubscribeFromPlayers(JsonElement payload)
    {
        logger.LogInformation(
            "[hub] UnsubscribeFromPlayers player={PlayerId} conn={ConnectionId} payload={Payload}",
            TryGetPlayerId(), Context.ConnectionId, RawPayload(payload));
        return Task.CompletedTask;
    }

    public Task Subscribe(JsonElement payload)
    {
        logger.LogInformation(
            "[hub] Subscribe player={PlayerId} conn={ConnectionId} payload={Payload}",
            TryGetPlayerId(), Context.ConnectionId, RawPayload(payload));
        return Task.CompletedTask;
    }

    public Task Unsubscribe(JsonElement payload)
    {
        logger.LogInformation(
            "[hub] Unsubscribe player={PlayerId} conn={ConnectionId} payload={Payload}",
            TryGetPlayerId(), Context.ConnectionId, RawPayload(payload));
        return Task.CompletedTask;
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
