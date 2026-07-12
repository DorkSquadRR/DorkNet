using DorkNet.Models.Notification;
using DorkNet.Server.Controllers.Match;
using DorkNet.Models.Auth;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

namespace DorkNet.Server.Services;

/// <summary>
/// Pushes <see cref="PushNotification"/>s to specific players over the
/// SignalR <see cref="NotifyHub"/>. Each connected player belongs to a
/// per-player group named <c>player:&lt;id&gt;</c>; sending to that group
/// reaches every active connection for that player without us tracking
/// individual connection ids.
///
/// **Wire contract** (verified against
/// <c>Cpp2IL_ISIL/.../RecNet/Notifications.txt:1979-1994</c>):
/// - Hub method name: <c>"Notification"</c> (singular). The client
///   calls <c>connection.On("Notification", Action&lt;string&gt;)</c>.
/// - Payload: a SINGLE <c>string</c> argument containing JSON of
///   the form <c>{"Id":int, "Msg":object}</c>. The client's handler
///   then parses that string into a <c>Dictionary&lt;string, object&gt;</c>
///   and dispatches by Id to per-handler callbacks registered via
///   <c>RegisterHandler(PushNotificationId, NotificationHandler)</c>.
///
/// Earlier versions sent <c>SendAsync("ReceiveNotification", obj)</c>
/// — wrong method name AND wrong arg type (object instead of string).
/// All push notifications were silently dropped client-side; admin
/// kicks / broadcasts / friend-request alerts never reached the
/// in-game client. Keep the method name + string serialisation
/// in sync with the decompiled client when extending.
///
/// Higher-level helpers (FriendRequestReceived, FriendRequestAccepted,
/// PlayerEnteredRoom, etc.) wrap NotifyAsync so callers don't have to
/// pick the right <see cref="PushNotificationId"/> by hand.
/// </summary>
public class NotificationService(
    IHubContext<NotifyHub> hub,
    OnlinePresenceService onlinePresence,
    ILogger<NotificationService> logger)
{
    private const string HubMethod = "Notification";

    private static readonly JsonSerializerOptions Json = new()
    {
        // Client uses LitJson which reads PascalCase keys verbatim;
        // no camelCase / snake_case conversion in the wire payload.
        PropertyNamingPolicy = null,
    };

    /// <summary>The watch's <c>Notifications.OnNotificationReceived</c>
    /// reads <c>Id</c> as a string and dispatches against
    /// <c>handlers : Dictionary&lt;string, NotificationHandler&gt;</c>
    /// (verified at <c>Cpp2IL_ISIL/.../RecNet/Notifications.txt:2097-2222</c>).
    /// Wire name per id is whatever the registering class actually
    /// stored in the handlers dict — verified live by tracing every
    /// <c>Notifications.RegisterHandler</c> call at boot via
    /// <c>tools/SaveDebugMod</c>'s <c>[notify-register]</c> log lines.
    /// The handlers fall into three buckets:
    /// <list type="bullet">
    ///   <item>Registered ONLY under the bare integer string (no
    ///         enum-name companion): <c>MessageReceived (2)</c>,
    ///         <c>MessageDeleted (3)</c>, <c>ModerationKick (22)</c>,
    ///         <c>ModerationKickAttemptFailed (23)</c>,
    ///         <c>ModerationRoomBan (24)</c>, and the entire
    ///         <c>PlayerEvent*</c> family (80-85). Sending the enum
    ///         name (e.g. <c>"MessageReceived"</c>) makes lookup miss
    ///         and the push drops silently — this is exactly what
    ///         broke DMs / game-invite delivery: the server logged
    ///         <c>[notify] sending id=MessageReceived ...
    ///         connections=1</c> AND the watch's topmost
    ///         <c>OnNotificationReceived</c> hook saw the payload, but
    ///         <c>Messages.OnMessageReceived</c> never fired because
    ///         <c>handlers["MessageReceived"]</c> didn't exist —
    ///         <c>handlers["2"]</c> did.</item>
    ///   <item>Registered under a CUSTOM short name (no integer
    ///         companion): <c>SubscriptionUpdate*</c> →
    ///         <c>"PresenceUpdate"</c> / <c>"RoomInstanceUpdate"</c> /
    ///         <c>"AccountUpdate"</c>, and <c>ChatMessageReceived
    ///         (90)</c> which is registered only as
    ///         <c>"ChatMessageReceived"</c>, NOT <c>"90"</c>.</item>
    ///   <item>Registered through the enum overload, which the IL2CPP
    ///         client lowers to the numeric string value. Gifts are a
    ///         concrete example: <c>GiftPackageReceived</c> registers
    ///         <c>"30"</c> and <c>GiftPackageReceivedImmediate</c>
    ///         registers <c>"31"</c>; sending the enum name misses.</item>
    /// </list></summary>
    private static string WireName(PushNotificationId id) => id switch
    {
        // Custom short names — registered under a bespoke string,
        // no integer companion in the dispatch dict.
        PushNotificationId.SubscriptionUpdatePresence    => "PresenceUpdate",
        PushNotificationId.SubscriptionUpdateGameSession => "RoomInstanceUpdate",
        PushNotificationId.SubscriptionUpdateProfile     => "AccountUpdate",
        // Int-only registrations — sending the enum name misses.
        PushNotificationId.MessageReceived               => "2",
        PushNotificationId.MessageDeleted                => "3",
        PushNotificationId.ModerationKick                => "22",
        PushNotificationId.ModerationKickAttemptFailed   => "23",
        PushNotificationId.ModerationRoomBan             => "24",
        PushNotificationId.GiftPackageReceived           => "30",
        PushNotificationId.GiftPackageReceivedImmediate  => "31",
        PushNotificationId.PlayerEventCreated            => "80",
        PushNotificationId.PlayerEventUpdated            => "81",
        PushNotificationId.PlayerEventDeleted            => "82",
        PushNotificationId.PlayerEventResponseChanged    => "83",
        PushNotificationId.PlayerEventResponseDeleted    => "84",
        PushNotificationId.PlayerEventStateChanged       => "85",
        // Everything else: enum.ToString() matches because the watch's
        // enum-overload RegisterHandler registers both forms.
        _                                                => id.ToString(),
    };

    private static string Serialize(PushNotificationId id, object? msg) =>
        JsonSerializer.Serialize(new { Id = WireName(id), Msg = msg }, Json);

    /// <summary>Push a single notification to one player. No-op if the
    /// player has no active connections.</summary>
    public async Task NotifyAsync(long playerId, PushNotificationId id, object? msg = null)
    {
        var payload = Serialize(id, msg);
        // Include the connection count so we can tell at a glance
        // whether the push had a recipient. Zero connections = the
        // watch isn't on SignalR right now (transport churn, just-
        // logged-out session, etc.) and the message is dropped.
        // The save flow specifically REQUIRES the watch to have a
        // live SignalR connection at send time — without it,
        // SubscriptionUpdateRoom evaporates into the hub and
        // MasterUploadRoomDataBlobCoroutine waits forever on
        // roomDetailsUpdated.
        var connections = onlinePresence.GetConnections(playerId);
        logger.LogInformation(
            "[notify] sending id={Id} wireName={WireName} to player={PlayerId} bytes={Bytes} connections={ConnectionCount}",
            id, WireName(id), playerId, payload.Length, connections.Count);
        if (id == PushNotificationId.SubscriptionUpdateRoom)
        {
            logger.LogInformation(
                "[notify] room-update payload player={PlayerId} json={Payload}",
                playerId, payload.Length <= 6000 ? payload : payload[..6000] + "...<truncated>");
        }
        await hub.Clients.Group(NotifyHub.GroupForPlayer(playerId))
            .SendAsync(HubMethod, payload);
    }

    public async Task NotifyTypedAsync(long playerId, string notificationType, object? msg = null)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { Id = notificationType, Msg = msg }, Json);
            await hub.Clients.Group(NotifyHub.GroupForPlayer(playerId))
                .SendAsync(HubMethod, payload);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[notify] failed to send typed notification {Type} to player {PlayerId}",
                notificationType, playerId);
        }
    }

    /// <summary>Push fresh account payloads to the affected player's
    /// watch. The 2020 client caches Account/SelfAccount objects in memory;
    /// without this, username and display-name edits persist server-side
    /// but the current session keeps rendering the stale cached names until
    /// a full relog rebuilds the account cache.</summary>
    public Task AccountChanged(PlayerEntity player)
    {
        var account = new RecNetAccount
        {
            AccountId = (int)player.Id,
            RawUsername = player.Username,
            Username = player.Username,
            DisplayName = string.IsNullOrWhiteSpace(player.DisplayName) ? player.Username : player.DisplayName,
            ProfileImage = player.ProfileImageName ?? string.Empty,
            TreatAsJunior = player.IsJunior,
            HasBirthday = true,
            Platforms = 1,
            CreatedAt = player.CreatedAt,
        };
        var selfAccount = new RecNetSelfAccount
        {
            AccountId = account.AccountId,
            RawUsername = account.RawUsername,
            Username = account.Username,
            DisplayName = account.DisplayName,
            ProfileImage = account.ProfileImage,
            TreatAsJunior = account.TreatAsJunior,
            HasBirthday = account.HasBirthday,
            Platforms = account.Platforms,
            CreatedAt = account.CreatedAt,
            Email = player.Email ?? string.Empty,
            Phone = player.Phone ?? string.Empty,
            Birthday = player.Birthday ?? new DateTime(2000, 1, 1),
            JuniorState = player.IsJunior ? 1 : 0,
            ParentAccountId = null,
        };

        return Task.WhenAll(
            NotifyAsync(player.Id, PushNotificationId.SubscriptionUpdateProfile, account),
            NotifyTypedAsync(player.Id, "SelfAccountUpdate", selfAccount));
    }

    /// <summary>Push a notification to every currently-connected
    /// player. Used for global announcements and admin broadcasts.</summary>
    public async Task BroadcastAsync(PushNotificationId id, object? msg = null)
    {
        await hub.Clients.All.SendAsync(HubMethod, Serialize(id, msg));
    }

    /// <summary>Push the same notification to every player in
    /// <paramref name="playerIds"/>. Used for "your friends are online"
    /// fan-out.</summary>
    public async Task NotifyManyAsync(
        IEnumerable<long> playerIds, PushNotificationId id, object? msg = null)
    {
        var groups = playerIds.Select(NotifyHub.GroupForPlayer).ToArray();
        if (groups.Length == 0) return;
        await hub.Clients.Groups(groups).SendAsync(HubMethod, Serialize(id, msg));
    }

    // ── Higher-level event helpers ─────────────────────────────────────

    public Task FriendRequestReceived(long targetPlayerId, long fromPlayerId) =>
        NotifyAsync(targetPlayerId, PushNotificationId.RelationshipChanged,
            new { Reason = "FriendRequestReceived", From = fromPlayerId });

    public Task FriendRequestAccepted(long requesterPlayerId, long byPlayerId) =>
        NotifyAsync(requesterPlayerId, PushNotificationId.RelationshipChanged,
            new { Reason = "FriendRequestAccepted", By = byPlayerId });

    public Task FriendRequestDeclined(long requesterPlayerId, long byPlayerId) =>
        NotifyAsync(requesterPlayerId, PushNotificationId.RelationshipChanged,
            new { Reason = "FriendRequestDeclined", By = byPlayerId });

    public Task FriendRemoved(long otherPlayerId, long byPlayerId) =>
        NotifyAsync(otherPlayerId, PushNotificationId.RelationshipChanged,
            new { Reason = "FriendRemoved", By = byPlayerId });

    public Task FriendBlocked(long blockedPlayerId, long byPlayerId) =>
        NotifyAsync(blockedPlayerId, PushNotificationId.RelationshipChanged,
            new { Reason = "FriendBlocked", By = byPlayerId });

    /// <summary>Push a presence delta so subscribed friends re-render
    /// their friend-list row without waiting for the watch's 15-45 s
    /// bulk-presence poll. The <c>Msg</c> body MUST match
    /// <c>RecNet.PlayerPresence</c>'s wire shape — the watch's
    /// <c>OnSubscriptionUpdatePresence</c> handler (dump.cs:579282)
    /// passes the raw dict straight into <c>PlayerPresence.Deserialize</c>
    /// then <c>UpdateCachedPresence</c>. Anything other than that exact
    /// shape (e.g. an ad-hoc <c>{ PlayerId, RoomId }</c>) is silently
    /// dropped client-side and the friend-list room stays stuck on
    /// whatever was last bulk-fetched.
    ///
    /// Builds the payload directly as <c>JsonObject</c> (instead of
    /// boxing a <see cref="PlayerPresenceDto"/> through
    /// <see cref="PushNotification.Msg"/> as <c>object</c>) so the inner
    /// camelCase keys don't depend on System.Text.Json's
    /// polymorphic-property heuristics. With <c>object?</c>-typed Msg,
    /// the runtime-type path can pick the wrong contract under certain
    /// option combos and emit an empty <c>{}</c> body — which silently
    /// drops the presence update at the watch.</summary>
    public async Task PlayerPresenceChanged(IEnumerable<long> friendIds, long playerId, RoomInstanceDto? room)
    {
        var groups = friendIds.Select(NotifyHub.GroupForPlayer).ToArray();
        if (groups.Length == 0) return;

        var redacted = RoomInstanceDto.Redact(room);
        var msg = new System.Text.Json.Nodes.JsonObject
        {
            ["playerId"]         = (int)playerId,
            // Everyone=0 (was 1=FriendsOnly, which hid you from non-friends).
            ["statusVisibility"] = 0,
            ["deviceClass"]      = 0,
            ["vrMovementMode"]   = 0,
            ["roomInstance"]     = redacted is null
                ? null
                : new System.Text.Json.Nodes.JsonObject
                {
                    ["roomInstanceId"]   = redacted.RoomInstanceId,
                    ["roomId"]           = redacted.RoomId,
                    ["subRoomId"]        = redacted.SubRoomId,
                    ["roomInstanceType"] = redacted.RoomInstanceType,
                    ["location"]         = redacted.Location,
                    ["dataBlob"]         = redacted.DataBlob,
                    ["eventId"]          = redacted.EventId,
                    ["clubId"]           = redacted.ClubId,
                    ["roomCode"]         = redacted.RoomCode,
                    ["photonRegionId"]   = redacted.PhotonRegionId,
                    ["photonRoomId"]     = redacted.PhotonRoomId,
                    ["name"]             = redacted.NameWire,
                    ["maxCapacity"]      = redacted.MaxCapacity,
                    ["isFull"]           = redacted.IsFull,
                    ["isPrivate"]        = redacted.IsPrivate,
                    ["isInProgress"]     = redacted.IsInProgress,
                    ["EncryptVoiceChat"] = redacted.EncryptVoiceChat,
                },
            // 2020.12 PlayerPresence.Deserialize requires these.
            ["isOnline"]         = redacted is not null,
            // 2023.03.21 client build version; must match the joining
            // client's own version or it reports a version mismatch. MUST be
            // a STRING ("20230317"): the client's presence DTO reads
            // appVersion with a string reader, so an int crashes the whole
            // SignalR message with "expected:'String Begin Token',
            // actual:'20230317'" — which aborts the presence/room update
            // (e.g. surfaced as "failed to create a new room").
            ["appVersion"]       = "20230317",
        };

        var envelope = new System.Text.Json.Nodes.JsonObject
        {
            // String dispatch key (NOT the int enum value) —
            // see WireName for rationale.
            ["Id"]  = WireName(PushNotificationId.SubscriptionUpdatePresence),
            ["Msg"] = msg,
        };
        var payload = envelope.ToJsonString();

        logger.LogDebug(
            "[notify] presence push player={PlayerId} → {FriendCount} friend(s), payload={Payload}",
            playerId, groups.Length, payload);

        await hub.Clients.Groups(groups).SendAsync(HubMethod, payload);
    }

    public Task PlayerCameOnline(IEnumerable<long> friendIds, long playerId, RoomInstanceDto? room) =>
        PlayerPresenceChanged(friendIds, playerId, room);

    public Task PlayerWentOffline(IEnumerable<long> friendIds, long playerId) =>
        PlayerPresenceChanged(friendIds, playerId, null);

    public Task PlayerEnteredRoom(IEnumerable<long> friendIds, long playerId, RoomInstanceDto room) =>
        PlayerPresenceChanged(friendIds, playerId, room);

    /// <summary>Force every currently-connected SignalR session for
    /// <paramref name="playerId"/> to disconnect via the watch's
    /// ModerationKick flow. Wire path verified against decompilation:
    ///   <c>PushNotificationId.ModerationKick = 22</c> is registered
    ///   by <c>LCCEEFHOBEN.OFFLOPLJBBG</c> via the enum overload.
    ///   <c>LCCEEFHOBEN.KEIAFMLIMFA(Dictionary)</c> reads
    ///   <c>Msg.Reason</c> as a ModerationBlockDetail object, then
    ///   calls <c>PODFHPNOHGB</c> / <c>BootLocalPlayerToDormRoom</c>.
    ///
    /// Used by <see cref="Controllers.Match.MatchPlayerController.PlayerLogin"/>
    /// to enforce single-session-per-account: when a second login on
    /// the same account replaces the LoginLock, the older SignalR
    /// connections are kicked. The new session hasn't called
    /// <c>negotiate</c> yet (it happens AFTER /player/login on the
    /// boot sequence), so the kick only hits old sessions.
    /// </summary>
    public Task KickStaleSession(long playerId, string reason) =>
        NotifyAsync(playerId, PushNotificationId.ModerationKick,
            ModerationKickPayload(reason, isBan: false, source: "session_replaced"));

    public Task KickPlayerAsync(long playerId, string reason, bool isBan = false) =>
        NotifyAsync(playerId, PushNotificationId.ModerationKick,
            ModerationKickPayload(reason, isBan, source: "admin_kick"));

    /// <summary>Push a ModerationKick to a specific set of SignalR
    /// connection ids — used by <see cref="DorkNet.Server.Hubs.NotifyHub.OnConnectedAsync"/>
    /// to evict the previous session when a new device connects on the
    /// same account. Targets specific ids (not the player group) so
    /// the brand-new connection joining the group right after this
    /// returns can't accidentally receive its own eviction notice.
    /// Works across replicas because SignalR's Redis backplane routes
    /// <c>Clients.Clients(ids)</c> to whichever node owns each id.</summary>
    public Task KickConnectionsAsync(IReadOnlyCollection<string> connectionIds, string reason)
    {
        if (connectionIds.Count == 0) return Task.CompletedTask;
        var payload = Serialize(PushNotificationId.ModerationKick,
            ModerationKickPayload(reason, isBan: false, source: "session_replaced"));
        return hub.Clients.Clients(connectionIds).SendAsync(HubMethod, payload);
    }

    private static object ModerationKickPayload(string reason, bool isBan, string source)
    {
        var message = string.IsNullOrWhiteSpace(reason)
            ? "Kicked to Dormroom"
            : reason.Trim();

        var blockDetail = new
        {
            ReportCategory = 10, // PlayerReporting.ReportCategory.VoteKick
            Duration = 0,
            GameSessionId = 0L,
            Message = message,
            IsHostKick = true,
            PlayerIdReporter = 1,
            IsBan = isBan,
            VoteKickReason = message,
        };

        return new
        {
            Reason = blockDetail,
            DisplayReason = message,
            BannedByPlayerId = 1,
            Source = source,
        };
    }

    /// <summary>Notify a private-instance owner that someone tried to
    /// follow them in via "Go to Player" but was rejected for not
    /// being on the invite list. Reuses the MessageReceived channel
    /// since the watch already renders that as an inbox toast — no
    /// new client-side handler needed. The body wording surfaces it
    /// as "Player X tried to join your private match" so the owner
    /// knows to send an explicit invite if they want to let them in.</summary>
    public Task PrivateInstanceJoinRejected(long ownerPlayerId, long attemptedByPlayerId) =>
        NotifyAsync(ownerPlayerId, PushNotificationId.MessageReceived,
            new
            {
                Reason = "PrivateInstanceJoinRejected",
                AttemptedBy = attemptedByPlayerId,
                Preview = $"Player {attemptedByPlayerId} tried to join your private match",
            });
}
