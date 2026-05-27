using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DorkNet.Launcher.Backend;

/// <summary>Finds the machine's preferred LAN IPv4 address for
/// "local network" hosting mode. Skip Cloudflare entirely, hand
/// out the LAN IP in the join code, and players on the same network
/// connect direct.
///
/// <para>Selection priority: an "Up" non-loopback network interface
/// with a private (RFC1918) IPv4 address. Falls back to 127.0.0.1
/// if nothing usable is found — works for solo testing on one
/// machine but joiners need either a real LAN address or
/// Cloudflare mode.</para></summary>
public static class LocalNetwork
{
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
