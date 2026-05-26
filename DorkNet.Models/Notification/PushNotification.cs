using System.Text.Json.Serialization;

namespace DorkNet.Models.Notification;

public class PushNotification
{
    [JsonPropertyName("Id")]
    public PushNotificationId Id { get; set; }

    [JsonPropertyName("Msg")]
    public object? Msg { get; set; }
}

/// <summary>
/// Mirrors the client's <c>RecNet.Notifications+PushNotificationId</c>
/// enum exactly (decompiled at
/// <c>Cpp2IL_CS/.../RecNet/Notifications.cs:52-89</c>). Values are
/// non-contiguous because the client allocates them in groups (1–6
/// core; 11–15 subscriptions; 20–25 moderation; 30–31 gifts;
/// 60–62 storefronts; 70–71 consumables; 80–85 events; 90 chat;
/// 95–96 community board; 100 inventions).
///
/// Server <c>NotifyAsync(playerId, id)</c> pushes raise an event with
/// this int value over the SignalR hub. If the value doesn't match
/// what the client expects for a given concept, the push is silently
/// dropped — match the client byte-for-byte.
/// </summary>
public enum PushNotificationId
{
    RelationshipChanged = 1,
    MessageReceived = 2,
    MessageDeleted = 3,
    PresenceHeartbeatResponse = 4,
    RefreshLogin = 5,
    Logout = 6,
    SubscriptionUpdateProfile = 11,
    SubscriptionUpdatePresence = 12,
    SubscriptionUpdateGameSession = 13,
    SubscriptionUpdateRoom = 15,
    ModerationQuitGame = 20,
    ModerationUpdateRequired = 21,
    ModerationKick = 22,
    ModerationKickAttemptFailed = 23,
    ModerationRoomBan = 24,
    ServerMaintenance = 25,
    GiftPackageReceived = 30,
    GiftPackageReceivedImmediate = 31,
    ProfileJuniorStatusUpdate = 40,
    RelationshipsInvalid = 50,
    StorefrontBalanceAdd = 60,
    StorefrontBalanceUpdate = 61,
    StorefrontBalancePurchase = 62,
    ConsumableMappingAdded = 70,
    ConsumableMappingRemoved = 71,
    PlayerEventCreated = 80,
    PlayerEventUpdated = 81,
    PlayerEventDeleted = 82,
    PlayerEventResponseChanged = 83,
    PlayerEventResponseDeleted = 84,
    PlayerEventStateChanged = 85,
    ChatMessageReceived = 90,
    CommunityBoardUpdate = 95,
    CommunityBoardAnnouncementUpdate = 96,
    InventionModerationStateChanged = 100,
}
