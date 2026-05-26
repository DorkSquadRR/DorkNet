namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Per-(thread, player) row: membership, per-player snooze, and the
/// last-read message id used for unread counts and the inbox's "X
/// unread" badge. One row per active participant. A "leave" deletes
/// the row so the player no longer receives new messages on that
/// thread.
///
/// For two-person DMs without a custom name, rows are created lazily
/// on first MarkRead / Snooze / Leave — the thread itself still works
/// fine without rows (the message table is the source of truth for
/// who participated).
/// </summary>
public class ChatThreadMemberEntity
{
    public long Id { get; set; }

    /// <summary>The thread this membership applies to. Matches
    /// <c>ChatMessageEntity.ThreadKey</c> / <c>ChatThreadEntity.ThreadKey</c>.</summary>
    public string ThreadKey { get; set; } = string.Empty;

    public long PlayerId { get; set; }

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When set, the watch's inbox treats this thread as
    /// muted until the UTC timestamp. Cleared by re-snoozing with a
    /// null payload.</summary>
    public DateTime? SnoozeUntil { get; set; }

    /// <summary>Id of the last <c>ChatMessageEntity</c> the player
    /// has acknowledged via <c>PUT thread/{id}/read</c>. Drives the
    /// per-thread unread counter on the watch's inbox tab.</summary>
    public long? LastReadMessageId { get; set; }
}
