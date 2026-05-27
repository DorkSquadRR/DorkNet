using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DorkNet.Launcher.Backend;

/// <summary>Compact URL-safe encoding of the bits a joining player
/// needs: the host address (a Localtunnel <c>*.loca.lt</c> URL for the
/// default mode, or an <c>sslip.io</c> name for LAN), the Photon
/// AppIds + region they used, the host's
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
    /// <summary>Hostname the joining patcher rewrites <c>*.rec.net</c>
    /// URIs to. Default Localtunnel mode hands out
    /// <c>&lt;random&gt;.loca.lt</c>; LAN mode uses an <c>sslip.io</c>
    /// name that resolves to the host's private IP.</summary>
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

    /// <summary>True when all RecNet services are exposed under one
    /// public origin using /__dn/{service}/ path prefixes.</summary>
    [JsonPropertyName("so")] public bool SingleOrigin { get; set; }
}
