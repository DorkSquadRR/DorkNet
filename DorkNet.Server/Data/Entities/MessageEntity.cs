using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Direct message between two players. Backs the watch's
/// <c>api/messages/v2</c> endpoints (inbox + send + mark-read).
///
/// One row per message. Threading happens at read time — the inbox
/// query groups by (Sender, Recipient) pair to surface the most
/// recent message per conversation.
/// </summary>
public class MessageEntity
{
    public long Id { get; set; }

    public long SenderPlayerId { get; set; }
    public long RecipientPlayerId { get; set; }

    [MaxLength(2000)]
    public string Body { get; set; } = string.Empty;

    /// <summary>RecNet <c>Message.MessageType</c> wire value (see
    /// <c>Cpp2IL_CS/.../RecNet/Message.cs</c>). Default 30 =
    /// TextMessage. Other values used by the 2020 client: 0/6 =
    /// GameInvite[V2], 3/7 = PartyActivitySwitch[V2], 10 =
    /// RequestGameInvite (the "ask to join" flow), 1 =
    /// GameInviteDeclined, 11 = RequestGameInviteDeclined, 20 =
    /// FriendStatusOnline. Negative invite types are tombstones kept
    /// only so the parallel accept/delete flow can still resolve
    /// <c>/goto/invite/{id}</c>.</summary>
    public int Type { get; set; } = 30;

    /// <summary>Optional RecNet room id carried by invite and party
    /// activity messages. The client uses this to render room-aware
    /// cards and passes it back when accepting V2 invites.</summary>
    public long? RoomId { get; set; }

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null until the recipient hits the message in the
    /// watch UI; set to the read timestamp at that point.</summary>
    public DateTime? ReadAt { get; set; }
}
