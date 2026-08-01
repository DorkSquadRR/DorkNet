using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// In-game text-chat message. Sent to the chat.rec.net/thread
/// endpoint family; backed by a flat per-thread row table where the
/// thread id is just the canonical (lower, higher) pair of player ids
/// for DMs, or the room id for room-wide chats.
///
/// We keep a separate table from <see cref="MessageEntity"/> because
/// the wire shape and history-fetch semantics differ — chat is
/// append-and-read-recent, messages are inbox-style with read
/// receipts.
/// </summary>
public class ChatMessageEntity
{
    public long Id { get; set; }

    /// <summary>String thread key. Format: <c>dm:<low>:<high></c> for
    /// direct messages between two players (lower id first), or
    /// <c>room:<roomId></c> for room-wide chats.</summary>
    [MaxLength(64)]
    public string ThreadKey { get; set; } = string.Empty;

    public long SenderPlayerId { get; set; }

    [MaxLength(2000)]
    public string Body { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    /// <summary>Per-message moderation state, set by
    /// <c>PUT thread/message/{id}/moderate</c> (form field
    /// <c>moderationState</c>). 0 = normal; a non-zero value means a moderator
    /// has actioned the message and it should not be re-served verbatim.</summary>
    public int ModerationState { get; set; } = 0;

    /// <summary>Build the canonical DM thread key for two players.
    /// Order-independent so both sides hit the same row.</summary>
    public static string DmKey(long a, long b) =>
        a < b ? $"dm:{a}:{b}" : $"dm:{b}:{a}";

    public static string RoomKey(long roomId) => $"room:{roomId}";
}
