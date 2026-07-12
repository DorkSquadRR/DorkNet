using DorkNet.Models.Notification;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Services;

/// <summary>
/// The REAL channel for event notifications (cheers, friend events, room
/// co-owner changes, …). A notification is a <see cref="MessageEntity"/>
/// with a <see cref="MessageType"/> discriminator: it persists to the
/// recipient's message/notification feed (so <c>GET api/messages</c>
/// returns it) and is pushed live via <c>MessageReceived</c> with the
/// client's Message DTO shape (RecNet.Runtime <c>KJECOLODAFA</c>:
/// Id/FromPlayerId/SentTime/Type/Data/RoomId/PlayerEventId).
///
/// This replaces the old anti-pattern of pushing <c>{Reason:"…"}</c> blobs
/// on the <c>SubscriptionUpdateProfile</c> ("AccountUpdate") channel — the
/// 2023 client decoded those as its Account DTO, producing a blank account
/// (accountId 0) and "Orphan player account (0) found" log spam, and never
/// rendered the notification. Every field type here matches the client
/// reader (FromPlayerId is Int32; SentTime is an ISO DateTime; Type/RoomId/
/// PlayerEventId are number/nullable-number).
/// </summary>
public sealed class SystemNotificationService(DorkNetDbContext db, NotificationService notifications)
{
    /// <summary>RecNet message-type enum (<c>KJECOLODAFA+MEPCALGGMJC</c>),
    /// verified from the 2023.03.21 client. Only the values we send are
    /// listed; add more as needed.</summary>
    public static class MessageType
    {
        public const int TextMessage = 30;
        public const int FriendRequestAccepted = 40;
        public const int FriendCodeUsed = 41;
        public const int PlayerCheer = 50;
        public const int PlayerCheerAnonymous = 51;
        public const int RoomCoOwnerAdded = 60;
        public const int RoomCoOwnerRemoved = 61;
        public const int RoomCoOwnerInvited = 62;
        public const int CreatorPublishedNewRoom = 70;
        public const int PlayerAttendingEvent = 80;
        public const int PlayerEventInvitation = 81;
    }

    /// <summary>Persist and push a system notification to
    /// <paramref name="recipientPlayerId"/>. <paramref name="fromPlayerId"/>
    /// is the actor the client resolves the name/avatar from (0 for a
    /// system/anonymous notification). <paramref name="data"/> is the
    /// type-specific payload string (empty when the Type + FromPlayerId are
    /// sufficient, e.g. a cheer). Returns the created message id.</summary>
    public async Task<long> SendAsync(
        long recipientPlayerId,
        int type,
        long fromPlayerId = 0,
        string data = "",
        long? roomId = null)
    {
        if (recipientPlayerId <= 0) return 0;

        var msg = new MessageEntity
        {
            SenderPlayerId = fromPlayerId,
            RecipientPlayerId = recipientPlayerId,
            Body = data ?? string.Empty,
            Type = type,
            RoomId = roomId,
            SentAt = DateTime.UtcNow,
        };
        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        await notifications.NotifyAsync(recipientPlayerId, PushNotificationId.MessageReceived, new
        {
            Id = msg.Id,
            // Client Message DTO reads FromPlayerId as Int32.
            FromPlayerId = unchecked((int)fromPlayerId),
            SentTime = msg.SentAt,
            Type = type,
            Data = msg.Body,
            RoomId = roomId,
            PlayerEventId = (long?)null,
        });
        return msg.Id;
    }
}
