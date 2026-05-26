using System.ComponentModel.DataAnnotations;

namespace DorkNet.Server.Data.Entities;

/// <summary>
/// IP-level ban for blocking misbehaving traffic before the JWT-based
/// player ban can apply. Checked by <c>IpBanCheckMiddleware</c> ahead
/// of authentication; matching requests get a 403.
///
/// CIDR notation lets a single row block a /24 or /32. The expiry is
/// optional — null means "permanent until manually removed".
/// </summary>
public class IpBanEntity
{
    public long Id { get; set; }

    /// <summary>CIDR or single-IP string (<c>1.2.3.4</c> or <c>1.2.3.0/24</c>).
    /// Compared against <c>HttpContext.Connection.RemoteIpAddress</c>.</summary>
    [MaxLength(64)]
    public string Cidr { get; set; } = string.Empty;

    [MaxLength(512)]
    public string Reason { get; set; } = string.Empty;

    public long BannedByAdminId { get; set; }
    public DateTime BannedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Null = permanent.</summary>
    public DateTime? Until { get; set; }
}
