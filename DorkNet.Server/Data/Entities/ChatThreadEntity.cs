using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Per-thread metadata for chat: name (for group chats), creator, and
/// timestamps. Rows here are sparse — a two-person DM doesn't need
/// one unless the players want a custom thread name. Group chats
/// always have a row so the member set + name are persisted.
/// </summary>
public class ChatThreadEntity
{
    public long Id { get; set; }

    /// <summary>Canonical thread key (matches <c>ChatMessageEntity.ThreadKey</c>).
    /// Format: <c>dm:&lt;low&gt;:&lt;high&gt;</c> for DMs,
    /// <c>group:&lt;guid&gt;</c> for group chats, <c>room:&lt;id&gt;</c> for room-wide.</summary>
    [MaxLength(96)]
    public string ThreadKey { get; set; } = string.Empty;

    /// <summary>Optional custom name. DMs default to empty (watch renders
    /// "Chat with &lt;username&gt;" client-side); group chats persist a
    /// name set via <c>PUT thread/{id}/rename</c>.</summary>
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    public long CreatorPlayerId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
