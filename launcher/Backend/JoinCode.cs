using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DorkNet.Launcher.Backend;

/// <summary>Compact URL-safe encoding of the bits a joining player
/// needs: the host's public address (typically a Cloudflare-Tunnel
/// hostname), the Photon AppIds + region they used, the host's
/// server name, and the version key so the joiner's launcher fetches
/// the matching client patcher.
///
/// <para>Format: base64-url of a small JSON object. Roughly 200-300
/// chars including padding — short enough to paste in chat / Discord
/// DM without line-wrapping.</para></summary>
public static class JoinCode
{
    public static string Encode(JoinPayload payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        return Convert.ToBase64String(json)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static JoinPayload? Decode(string code)
    {
        try
        {
            var padded = code.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            var json = Convert.FromBase64String(padded);
            return JsonSerializer.Deserialize<JoinPayload>(json);
        }
        catch { return null; }
    }
}

public sealed class JoinPayload
{
    /// <summary>Public hostname the joining patcher rewrites
    /// <c>*.rec.net</c> URIs to. Cloudflare Tunnel typically gives
    /// <c>random-words.trycloudflare.com</c>.</summary>
    [JsonPropertyName("host")] public string Host { get; set; } = "";

    /// <summary>Version key from versions.json. Tells the joiner which
    /// per-version branch's client patcher to fetch + install.</summary>
    [JsonPropertyName("v")] public string VersionKey { get; set; } = "";

    /// <summary>Photon Realtime AppId. The joining client patcher will
    /// rewrite the in-binary AppId to this so the joiner reaches the
    /// host's Photon-Cloud match instances.</summary>
    [JsonPropertyName("pa")] public string PhotonAppId { get; set; } = "";

    [JsonPropertyName("pv")] public string PhotonVoiceAppId { get; set; } = "";

    [JsonPropertyName("pr")] public string PhotonRegion { get; set; } = "us";

    /// <summary>Server name shown in the joiner's "you're about to
    /// connect to ..." preview.</summary>
    [JsonPropertyName("n")] public string Name { get; set; } = "";
}
