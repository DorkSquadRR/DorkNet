using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;
using System.Net;
using System.Net.Sockets;

namespace DorkNet.Server.Auth;

/// <summary>
/// Short-circuits any incoming request whose remote IP falls under an
/// active <see cref="DorkNet.Server.Data.Entities.IpBanEntity"/> row
/// with 403. Sits BEFORE authentication so banned IPs can't spend a
/// round-trip negotiating a JWT — the connection terminates as early
/// as possible.
///
/// Lookup is one query against IpBans per request. The table is small
/// (a few rows in practice) and the simple "Cidr matches?" filter is
/// done in C# rather than SQL because CIDR maths in SQLite is awkward.
/// </summary>
public class IpBanCheckMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, DorkNetDbContext db)
    {
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is null)
        {
            await next(ctx);
            return;
        }

        // Materialise the active-ban list once per request. Could be
        // cached for ~30s with a hosted service if it ever becomes a
        // hot path, but right now this is unmeasurable.
        var bans = await db.IpBans
            .Where(b => b.Until == null || b.Until > DateTime.UtcNow)
            .Select(b => b.Cidr)
            .ToListAsync();

        foreach (var cidr in bans)
        {
            if (CidrMatches(cidr, remote))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("ip_banned");
                return;
            }
        }

        await next(ctx);
    }

    /// <summary>Match a single IP against a CIDR-or-bare-IP string.
    /// Bare IPs match exactly; CIDRs match if the prefix bits agree.
    /// Falls back to false on any parse error.</summary>
    private static bool CidrMatches(string cidr, IPAddress remote)
    {
        try
        {
            var slash = cidr.IndexOf('/');
            if (slash < 0)
            {
                return IPAddress.TryParse(cidr, out var single) &&
                       single.Equals(remote);
            }

            var addrPart = cidr[..slash];
            var prefixPart = cidr[(slash + 1)..];
            if (!IPAddress.TryParse(addrPart, out var net) ||
                !int.TryParse(prefixPart, out var prefix))
                return false;

            // Only IPv4 networks for now — IPv6 CIDR matching is
            // straightforward but we'd need to mask 128-bit values
            // and the user's setup is single-host IPv4 anyway.
            if (net.AddressFamily != AddressFamily.InterNetwork ||
                remote.AddressFamily != AddressFamily.InterNetwork)
                return false;

            var netBytes = net.GetAddressBytes();
            var remoteBytes = remote.GetAddressBytes();
            int fullBytes = prefix / 8;
            int extraBits = prefix % 8;

            for (int i = 0; i < fullBytes; i++)
                if (netBytes[i] != remoteBytes[i]) return false;
            if (extraBits == 0) return true;

            int mask = 0xFF << (8 - extraBits) & 0xFF;
            return (netBytes[fullBytes] & mask) == (remoteBytes[fullBytes] & mask);
        }
        catch { return false; }
    }
}
