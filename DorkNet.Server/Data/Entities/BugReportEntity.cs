using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// One bug report submitted via the in-game "Report a Bug" UI
/// (<c>POST /api/bugreporting/v2/reportbug</c>). Surfaces in the
/// admin moderation queue at <c>GET /api/admin/v1/bugreports</c>.
/// </summary>
public class BugReportEntity
{
    public long Id { get; set; }
    public long ReporterPlayerId { get; set; }

    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string Body { get; set; } = string.Empty;

    public long GameSessionId { get; set; }

    [MaxLength(64)]
    public string ClientVersion { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Platform { get; set; } = string.Empty;

    /// <summary>Optional category for routing — also reused by Phase
    /// 11's <c>POST /api/hile/v1/log</c> (huddle telemetry) which
    /// piggybacks on this table with Category=`hile`.</summary>
    [MaxLength(32)]
    public string Category { get; set; } = "bug";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReadAt { get; set; }
}
