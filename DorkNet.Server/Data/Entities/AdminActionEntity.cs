using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// Append-only audit row written every time an admin performs a
/// privileged action (ban, unban, promote, demote, room delete,
/// MOTD change, etc.). The admin watch UI's "audit" tab reads
/// from this in reverse-chronological order.
///
/// Rows are never deleted from the application — privileged actions
/// stay on the record. Storage cost is negligible at single-server
/// scale.
/// </summary>
public class AdminActionEntity
{
    public long Id { get; set; }

    /// <summary>The admin who performed the action.</summary>
    public long AdminPlayerId { get; set; }

    /// <summary>Free-form action name. Convention: lower-snake-case verb,
    /// e.g. <c>ban_player</c>, <c>unban_player</c>, <c>promote_admin</c>,
    /// <c>delete_room</c>, <c>set_motd</c>.</summary>
    [MaxLength(64)]
    public string Action { get; set; } = string.Empty;

    /// <summary>Targeted entity type — <c>player</c>, <c>room</c>,
    /// <c>system</c>. Free-form so future actions can introduce new
    /// kinds without a migration.</summary>
    [MaxLength(32)]
    public string TargetType { get; set; } = string.Empty;

    /// <summary>Numeric id of the target. <c>0</c> for system-wide
    /// actions like setting the MOTD.</summary>
    public long TargetId { get; set; }

    /// <summary>Optional human reason the admin supplied at action
    /// time. Surfaced to the audit log UI verbatim.</summary>
    [MaxLength(512)]
    public string Reason { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
