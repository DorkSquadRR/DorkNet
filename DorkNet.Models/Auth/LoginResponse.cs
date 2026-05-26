using System.Text.Json.Serialization;

namespace DorkNet.Models.Auth;

/// <summary>
/// Login.LoginResponse — dump.cs:578473 (private nested class),
/// Deserialize at RVA 0x1447D10.
///
/// Verified by disassembly: this is a standard OAuth 2.0 RFC 6749
/// password-grant token response. The Deserialize body has two branches:
///
///   error = dict["error"];
///   if (string.IsNullOrEmpty(error)) {
///       this.AccessToken  = dict["access_token"];
///       this.RefreshToken = dict["refresh_token"];
///   } else {
///       this.ErrorDescription = dict["error_description"];
///   }
///
/// Returned by api/platformlogin/v5 (the 2020 build's login endpoint)
/// and api/platformlogin/refresh.
///
/// All four keys are snake_case, NOT PascalCase. This is the standard
/// OAuth response format — Rec Room conforms to RFC 6749 here.
/// </summary>
public class LoginResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }

    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;
}
