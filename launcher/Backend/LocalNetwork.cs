using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DorkNet.Launcher.Backend;

/// <summary>Finds the machine's preferred LAN IPv4 address for
/// "local network" hosting mode. Skip public tunnels entirely, hand
/// out an sslip.io hostname derived from the LAN IP in the join code,
/// and players on the same network connect direct.
///
/// <para>Selection priority: an "Up" non-loopback network interface
/// with a private (RFC1918) IPv4 address. Falls back to 127.0.0.1
/// if nothing usable is found — works for solo testing on one
/// machine but joiners need either a real LAN address or one of the
/// tunnel modes (Localtunnel / Tunnelto).</para></summary>
public static class LocalNetwork
{
    private const string SslipDomain = "sslip.io";

    public static string GetLanIp()
    {
        try
        {
            // Preferred path: walk active NICs, pick the first IPv4 in
            // RFC1918 ranges on an interface that's up + not virtual.
            // This handles multi-NIC machines (Ethernet + WiFi + VPN +
            // Hyper-V vSwitches) by preferring physical adapters.
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address)
                .ToList();

            // Prefer private-range addresses (the joiners' subnet) over
            // public-routable ones (which suggest a server with a
            // direct WAN IP — unusual for grandma's PC).
            var privateAddr = candidates.FirstOrDefault(IsPrivate);
            if (privateAddr is not null) return privateAddr.ToString();

            // Fall back to the first non-loopback address of any kind.
            var anyAddr = candidates.FirstOrDefault(a => !IPAddress.IsLoopback(a));
            if (anyAddr is not null) return anyAddr.ToString();
        }
        catch { /* fall through to loopback */ }

        return "127.0.0.1";
    }

    /// <summary>Turns a LAN IPv4 address into an sslip.io apex host.
    /// For example, <c>192.168.1.25</c> becomes
    /// <c>192-168-1-25.sslip.io</c>. Keeping the IP in one DNS label
    /// makes derived hosts like <c>api.192-168-1-25.sslip.io</c> and
    /// <c>cdn.192-168-1-25.sslip.io</c> resolve cleanly too.</summary>
    public static string ToSslipHost(string lanIp)
    {
        if (IPAddress.TryParse(lanIp, out var parsed) &&
            parsed.AddressFamily == AddressFamily.InterNetwork)
        {
            return $"{parsed.ToString().Replace('.', '-')}.{SslipDomain}";
        }

        return lanIp;
    }

    public static LocalNetworkAddress GetLanAddress()
    {
        var ip = GetLanIp();
        return new LocalNetworkAddress(ip, ToSslipHost(ip));
    }

    private static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] switch
        {
            10 => true,                              // 10.0.0.0/8
            172 when b[1] >= 16 && b[1] <= 31 => true, // 172.16.0.0/12
            192 when b[1] == 168 => true,             // 192.168.0.0/16
            _ => false,
        };
    }
}

public sealed record LocalNetworkAddress(string Ip, string Host);
