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
    /// </list>
    ///
    /// <para><b>The default is numeric, and that matters.</b> Both enum
    /// overloads of the client's <c>RegisterHandler</c>
    /// (<c>OMHPBIEHOHN.PNDNMHHDOOJ</c>) call <c>Int32.ToString()</c> on the id
    /// and register ONLY that numeric string —
    /// <c>RecNet.Runtime/OMHPBIEHOHN.txt:662</c> and <c>:733</c>. There is no
    /// enum-name companion key. The dispatcher does a plain case-sensitive
    /// <c>Dictionary.TryGetValue</c> on the envelope's <c>Id</c> and returns
    /// silently on a miss (<c>OMHPBIEHOHN.txt:4052-4237</c>), so a wrong key is
    /// not an error anywhere — the push simply never arrives.</para>
    ///
    /// <para>This used to default to <c>id.ToString()</c> (the enum NAME), which
    /// silently dropped every enum-registered push that wasn't explicitly listed:
    /// friend-request add/accept/remove and the RelationshipsInvalid refresh,
    /// forced logout, maintenance broadcasts, live token-balance updates after a
    /// level-up or purchase, consumable inventory changes, and invention
    /// moderation state. Only the six ids below are genuinely string-keyed;
    /// everything else must go out as a number. Verify against the binary before
    /// adding an entry here — guessing wrong fails silently in both directions.</para>
    /// </summary>
    private static string WireName(PushNotificationId id) => id switch
    {
        // The ONLY ids registered under a bespoke string. Everything else is
        // numeric — see the note below for why the default flipped.
        PushNotificationId.SubscriptionUpdateProfile     => "AccountUpdate",
        PushNotificationId.SubscriptionUpdatePresence    => "PresenceUpdate",
        PushNotificationId.SubscriptionUpdateGameSession => "RoomInstanceUpdate",
        PushNotificationId.SubscriptionUpdateRoom        => "RoomUpdate",
        PushNotificationId.ChatMessageReceived           => "ChatMessageReceived",
        PushNotificationId.CommunityBoardUpdate          => "CommunityBoardUpdate",

        // Numeric, because the client registered these through the enum
        // overload. This is the default for a reason — see below.
        _                                                => ((int)id).ToString(),
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

    /// <summary>Push fresh account payloads after a profile mutation.
    /// The owner gets <c>SelfAccountUpdate</c> (private self DTO) and every
    /// connected client gets <c>AccountUpdate</c> (public DTO). The 2023
    /// client subscribes to both strings (RecNet.Runtime
    /// OPMAPIOEIFG.txt:267 "AccountUpdate" → Action&lt;Account&gt;, :279
    /// "SelfAccountUpdate" → Action&lt;SelfAccount&gt;) and refreshes its
    /// cached account from the payload — the profile-edit PUTs return no
    /// account, so this push is the ONLY way the new emoji / banner /
    /// pronouns / identity flags reach the editor's own UI and anyone else
    /// already looking at the profile. Sending AccountUpdate to the owner
    /// alone (the previous behaviour) left every other player rendering the
    /// stale cached account until relog.</summary>
    public Task AccountChanged(PlayerEntity player)
    {
        var account = AccountDtos.Public(player);
        var selfAccount = AccountDtos.Self(player);

        return Task.WhenAll(
            BroadcastAsync(PushNotificationId.SubscriptionUpdateProfile, account),
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

    /// <summary>Push one relationship row to <paramref name="recipientPlayerId"/>.
    /// <paramref name="relationship"/> must be the recipient's OWN view of the
    /// pair — the same <c>{PlayerID, RelationshipType, Muted, Ignored,
    /// Favorited}</c> object <c>api/relationships/v2/get</c> returns. The 2023
    /// handler (<c>EEGNOHOELBG.FOCBJAIDCBB</c>, Relationship
    /// <c>ECGNEHMCGCN</c>) stores it under its <c>PlayerID</c> and raises the
    /// changed event, which refreshes the friends list and the client's
    /// SubscribeToPlayers set. Any other shape parses as PlayerID 0 and the
    /// real row never updates.</summary>
    public Task RelationshipChanged(long recipientPlayerId, object relationship) =>
        NotifyAsync(recipientPlayerId, PushNotificationId.RelationshipChanged, relationship);

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
    public async Task PlayerPresenceChanged(
        IEnumerable<long> friendIds,
        long playerId,
        RoomInstanceDto? room,
        bool? isOnline = null,
        int statusVisibility = 0)
    {
        var groups = friendIds.Select(NotifyHub.GroupForPlayer).ToArray();
        if (groups.Length == 0) return;

        var payload = BuildPresenceUpdatePayload(playerId, room, isOnline, statusVisibility);

        logger.LogDebug(
            "[notify] presence push player={PlayerId} → {FriendCount} friend(s), payload={Payload}",
            playerId, groups.Length, payload);

        await hub.Clients.Groups(groups).SendAsync(HubMethod, payload);
    }

    /// <summary>Push one player's presence to every connection that subscribed
    /// to them with <c>SubscribeToPlayers</c>, except
    /// <paramref name="excludeConnectionIds"/> — the subject's own connections
    /// (a frame about the local player moves them) and anyone already sent the
    /// frame through their friend group.</summary>
    public async Task PresenceToSubscribersAsync(
        long playerId,
        RoomInstanceDto? room,
        bool isOnline,
        int statusVisibility,
        IReadOnlyList<string> excludeConnectionIds)
    {
        var payload = BuildPresenceUpdatePayload(playerId, room, isOnline, statusVisibility);
        await hub.Clients.GroupExcept(NotifyHub.PresenceGroupForPlayer(playerId), excludeConnectionIds)
            .SendAsync(HubMethod, payload);
    }

    /// <summary>The PresenceUpdate envelope for one player, as a JSON string.
    ///
    /// <paramref name="isOnline"/> is independent of the room: the 2023
    /// presence DTO stores it in its own field (LCEHINCCHOB +0x50,
    /// RecNet.Runtime/LCEHINCCHOB.txt:635-653) and DNKHCBFPLJG treats
    /// "online with no room" as a real state — a player in the offline dorm
    /// after matchmake/none. Deriving it from the room, as this used to, sent
    /// such players to their friends as OFFLINE. When omitted it still falls
    /// back to "has a room" for callers that only know about room moves.
    /// An offline frame never carries a room.</summary>
    public static string BuildPresenceUpdatePayload(
        long playerId,
        RoomInstanceDto? room,
        bool? isOnline = null,
        int statusVisibility = 0)
    {
        var online = isOnline ?? room is not null;
        var redacted = online ? RoomInstanceDto.Redact(room) : null;
        var msg = new System.Text.Json.Nodes.JsonObject
        {
            ["playerId"]         = (int)playerId,
            // The player's stored /player/statusvisibility choice (default
            // 0 = Everyone), the same value their own heartbeat reports.
            ["statusVisibility"] = statusVisibility,
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
            ["isOnline"]         = online,
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
        return envelope.ToJsonString();
    }

    public Task PlayerCameOnline(IEnumerable<long> friendIds, long playerId, RoomInstanceDto? room) =>
        PlayerPresenceChanged(friendIds, playerId, room);

    public Task PlayerWentOffline(IEnumerable<long> friendIds, long playerId) =>
        PlayerPresenceChanged(friendIds, playerId, null);

    public Task PlayerEnteredRoom(IEnumerable<long> friendIds, long playerId, RoomInstanceDto room) =>
        PlayerPresenceChanged(friendIds, playerId, room);

    /// <summary>Client logout reasons (<c>Logout</c> "6" payload <c>Reason</c>,
    /// enum <c>MPJOEALLGHE</c>, read by formatter <c>PBEDEFHAHID</c>). The
    /// value must go out as a NUMBER: release builds obfuscate enum names, so a
    /// string like "AdminPasswordReset" fails the parse and the whole push is
    /// dropped. <c>SessionManager.JNNAOHMNGEM</c> shows its own "signed in
    /// elsewhere" text for <see cref="SessionTakeover"/>, then returns the
    /// client to the boot scene for every reason.</summary>
    public enum LogoutReason
    {
        Unknown = 0,
        UserInitiated = 1,
        SessionTakeover = 2,
        ForciblyLoggedOut = 3,
        Banned = 4,
    }

    /// <summary>Log every session of <paramref name="playerId"/> out to the
    /// boot scene. Use this, not a kick, when the player must not stay signed
    /// in: a ModerationKick only sends them to their dorm.</summary>
    public Task LogoutAsync(long playerId, LogoutReason reason) =>
        NotifyAsync(playerId, PushNotificationId.Logout, new { Reason = (int)reason });

    /// <summary>Single-session-per-account: when a second login replaces the
    /// LoginLock, log the older session out. The new session hasn't called
    /// <c>negotiate</c> yet (it happens AFTER /player/login on the boot
    /// sequence), so only the old session's connections receive it.</summary>
    public Task LogoutStaleSession(long playerId) =>
        LogoutAsync(playerId, LogoutReason.SessionTakeover);

    /// <summary>The same takeover logout, sent to specific SignalR connection
    /// ids — used by <see cref="DorkNet.Server.Hubs.NotifyHub.OnConnectedAsync"/>
    /// to evict the previous session when a new device connects on the same
    /// account. Targets ids (not the player group) so the brand-new
    /// connection, which joins the group right after, never receives it. Works
    /// across replicas because the Redis backplane routes
    /// <c>Clients.Clients(ids)</c> to whichever node owns each id.</summary>
    public Task LogoutConnectionsAsync(IReadOnlyCollection<string> connectionIds)
    {
        if (connectionIds.Count == 0) return Task.CompletedTask;
        var payload = Serialize(PushNotificationId.Logout, new { Reason = (int)LogoutReason.SessionTakeover });
        return hub.Clients.Clients(connectionIds).SendAsync(HubMethod, payload);
    }

    /// <summary>Kick <paramref name="playerId"/> back to their dorm with
    /// <paramref name="reason"/> shown in the kick dialog. The player stays
    /// signed in; use <see cref="LogoutAsync"/> for bans and deletions.
    /// Pass the room instance the player is in as
    /// <paramref name="gameSessionId"/> to have the client announce the kick
    /// to that room.</summary>
    public Task KickPlayerAsync(long playerId, string reason, long gameSessionId = 0) =>
        NotifyAsync(playerId, PushNotificationId.ModerationKick, ModerationKickPayload(reason, gameSessionId));

    /// <summary>ModerationKick "22" payload: the FLAT moderation block
    /// <c>PIBLFGPOCIB</c> (formatter <c>LAIJIAIHGCO</c>). The kick handler
    /// (<c>SessionManager</c> HandleModerationKicked) starts from its own
    /// "Kicked to Dormroom" text and only adds a reason as
    /// a "Reason: " line from <c>VoteKickReason</c> when <c>ReportCategory</c> is 10
    /// (VoteKick) and <c>IsHostKick</c> is false; <c>Message</c> is never
    /// shown. When <c>GameSessionId</c> is the kicked player's current room
    /// instance it also broadcasts the kick to that room, and
    /// <c>Duration</c> &gt; 0 starts a timeout, so it stays 0. The old nested
    /// <c>{Reason:{…}}</c> shape (from the 2020 client) parsed to all defaults,
    /// which booted the player but dropped every reason.</summary>
    private static object ModerationKickPayload(string reason, long gameSessionId)
    {
        var message = string.IsNullOrWhiteSpace(reason) ? "Kicked to Dormroom" : reason.Trim();
        return new
        {
            ReportCategory = 10, // VoteKick: the only category that shows a reason
            Duration = 0,
            GameSessionId = gameSessionId,
            IsHostKick = false,
            Message = message,
            PlayerIdReporter = (int?)null,
            IsBan = false,
            IsVoiceModAutoban = false,
            IsWarning = false,
            VoteKickReason = message,
            TimeoutStartedAt = (DateTime?)null,
        };
    }
}
