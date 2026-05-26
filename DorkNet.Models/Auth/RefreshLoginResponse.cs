using System.Text.Json.Serialization;

namespace DorkNet.Models.Auth;

/// <summary>
/// Login.RefreshLoginResponse — dump.cs:578533 (private nested class),
/// Deserialize at RVA 0x145B1E0.
///
/// Verified by disassembly: reads exactly one key — lowercase "token" —
/// via Util.GetKey&lt;string&gt;. Single-field response from
/// POST api/platformlogin/refresh: the new (rotated) bearer access token.
/// </summary>
public class RefreshLoginResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}
