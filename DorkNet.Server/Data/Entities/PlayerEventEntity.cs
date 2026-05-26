using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// A player-scheduled gathering ("event") in a specific room. Backs
/// the watch's Events tab — players can RSVP via
/// <see cref="PlayerEventResponseEntity"/>.
/// </summary>
public class PlayerEventEntity
{
    public long Id { get; set; }

    public long CreatorPlayerId { get; set; }
    public long RoomId { get; set; }

    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    public int Capacity { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One row per RSVP'd player + event.</summary>
public class PlayerEventResponseEntity
{
    public long Id { get; set; }
    public long EventId { get; set; }
    public long PlayerId { get; set; }
    /// <summary>0=Going, 1=Maybe, 2=NotGoing — matches the watch's
    /// three-state RSVP toggle.</summary>
    public int Response { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
