using System.Text.Json.Serialization;

namespace DorkNet.Models.Auth;

/// <summary>
/// Login.CachedLogin — dump.cs:578388, Deserialize at RVA 0x1447BD0.
///
/// Verified by disassembly: reads platform, platformId, accountId,
/// lastLoginTime (DateTime), requirePassword (default false). The Account
/// field on the C# class is NOT read from the JSON dict — it is set via
/// SetAccount(Account) after the fact, so the JSON shape doesn't need it.
///
/// Returned by:
///   GET auth.rec.net/cachedlogin/forplatformid/{platform}/{platformId}
///   GET api.rec.net/api/platformlogin/cached
///   GET accounts.rec.net/account/v1/savedlogins
/// as a List&lt;CachedLogin&gt;. Empty list = no remembered accounts.
/// </summary>
public class CachedLogin
{
    // PlatformManager.PlatformType (int)
    [JsonPropertyName("platform")]
    public int Platform { get; set; }

    [JsonPropertyName("platformId")]
    public string PlatformId { get; set; } = string.Empty;

    [JsonPropertyName("accountId")]
    public int AccountId { get; set; }

    [JsonPropertyName("lastLoginTime")]
    public DateTime LastLoginTime { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("requirePassword")]
    public bool RequirePassword { get; set; } = false;
}
