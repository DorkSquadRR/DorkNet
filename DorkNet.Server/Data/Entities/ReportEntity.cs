using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One in-game player report. Created by any authenticated player via
/// the watch's "report player" UI; surfaces in the admin moderation
/// queue at <c>GET api/admin/v1/reports</c> until an admin resolves it.
/// </summary>
public class ReportEntity
{
    public long Id { get; set; }

    /// <summary>Account id of the player filing the report.</summary>
    public long ReporterPlayerId { get; set; }

    /// <summary>Account id of the player being reported.</summary>
    public long TargetPlayerId { get; set; }

    /// <summary>Free-form category the watch sends — convention is
    /// numeric matching the client's <c>RoomieReportCategory</c> enum
    /// (Harassment=1, Inappropriate=2, Cheating=3, Spam=4, Other=5).</summary>
    public int Category { get; set; }

    /// <summary>The reporter's free-text description. Capped at 1000
    /// chars by the watch UI.</summary>
    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    /// <summary>Optional game session id for context — admins can
    /// correlate to room/server logs around the time of the report.</summary>
    public long GameSessionId { get; set; }

    /// <summary>Room the offending behaviour happened in (0 if N/A).</summary>
    public long RoomId { get; set; }

    /// <summary>If the report targets an invention, the invention id
    /// (0 otherwise).</summary>
    public long TargetInventionId { get; set; }

    /// <summary>If the report targets a room (rather than the player
    /// in it), the room id (0 otherwise).</summary>
    public long TargetRoomId { get; set; }

    /// <summary>If the report targets a player event, the event id
    /// (0 otherwise).</summary>
    public long TargetEventId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null while the report sits in the admin queue; set
    /// when an admin resolves it via
    /// <c>POST api/admin/v1/reports/{id}/resolve</c>. Together with
    /// <see cref="ResolverAdminId"/> + <see cref="ResolutionNote"/>
    /// forms the full audit trail.</summary>
    public DateTime? ResolvedAt { get; set; }

    public long? ResolverAdminId { get; set; }

    [MaxLength(512)]
    public string ResolutionNote { get; set; } = string.Empty;
}
