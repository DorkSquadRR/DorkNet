using System.Text.Json.Serialization;

namespace DorkNet.Models.Auth;

// =====================================================================
// JSON keys verified by disassembling each .Deserialize method body
// directly from GameAssembly.dll. Each key here is the literal string
// passed to Util.GetKey / Util.GetKeyOrDefault inside the client. Util.GetKey
// uses Dictionary<string,object>.TryGetValue, which is case-sensitive — so
// the keys MUST match exactly.
//
// Convention summary (it's mixed):
//   RecNetResult       lowercase           "success", "error"
//   Account            camelCase           "accountId", "displayName", "isJunior"…
//   SelfAccount        camelCase           "email", "birthday", "juniorState"…
//   LoginResponse      snake_case (OAuth)  "access_token", "error_description"…
//   RefreshLoginResp.  lowercase           "token"
//   CachedLogin        camelCase           "platformId", "lastLoginTime"…
//   CreateAccountResp  uses "value" as the wrapper key for the Account dict
//
// NOTE: TreatAsJunior on the C# side maps to JSON key "isJunior" — they
// don't match. Account.Deserialize reads dict["isJunior"], not dict["treatAsJunior"].
// =====================================================================

/// <summary>
/// RecNet.RecNetResult — dump.cs:584325, Deserialize at RVA 0x1146340.
/// Reads "success" (required) and "error" (optional).
/// </summary>
public class RecNetResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

/// <summary>
/// RecNet.Account — dump.cs:567684, Deserialize at RVA 0xF9DE50.
/// JSON keys verified in the current 2020 IL2CPP build. Account.Deserialize
/// reads profileImage.
/// Other keys: accountId, username, displayName, isJunior, platforms. The C# Account class also has
/// RawUsername and HasBirthday properties but Deserialize does not read
/// them from JSON (they're set via MakeNameAdhereToPlatformRequirements
/// or other flows). We expose them with reasonable camelCase keys for
/// other endpoints that may consume them.
/// </summary>
public class RecNetAccount
{
    private string profileImage = string.Empty;

    [JsonPropertyName("accountId")]
    public int AccountId { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("profileImage")]
    public string ProfileImage
    {
        get => profileImage;
        set => profileImage = value ?? string.Empty;
    }

    // Account.Deserialize reads "isJunior" — NOT "treatAsJunior".
    // The C# property name and the JSON key name differ.
    [JsonPropertyName("isJunior")]
    public bool TreatAsJunior { get; set; } = false;

    // PlatformManager.PlatformMask: Steam=1, Oculus=2, etc.
    [JsonPropertyName("platforms")]
    public int Platforms { get; set; } = 1;

    // Not in Account.Deserialize body but kept for forward-compat.
    [JsonPropertyName("rawUsername")]
    public string RawUsername { get; set; } = string.Empty;

    // Likewise.
    [JsonPropertyName("hasBirthday")]
    public bool HasBirthday { get; set; } = true;

    // 2020.12 Account.Deserialize reads this key (verified via Cpp2IL ISIL
    // dump of CCEOLAOLEKJ.PPGFHEDFBEA — the obfuscated Account class). The
    // 2020.03 client doesn't read it, but emitting it is harmless there.
    // Missing → LitJson throws and 2020.12 surfaces "Malformed Response".
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// RecNet.SelfAccount : Account — dump.cs:567797, Deserialize at RVA 0xAF4FB0.
/// Calls base Account.Deserialize, then reads:
/// email, phone, birthday, juniorState, parentAccountId.
/// </summary>
public class RecNetSelfAccount : RecNetAccount
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("birthday")]
    public DateTime? Birthday { get; set; } = new DateTime(2000, 1, 1);

    // RecNet.JuniorState enum (int)
    [JsonPropertyName("juniorState")]
    public int JuniorState { get; set; } = 0;

    [JsonPropertyName("parentAccountId")]
    public int? ParentAccountId { get; set; } = null;
}

/// <summary>
/// Accounts.CreateAccountResponse : RecNetResult — dump.cs:568167,
/// Deserialize at RVA 0xFAA200.
/// Calls base RecNetResult.Deserialize (reads "success", "error"),
/// then reads the Account from dict["value"] — NOT dict["account"].
/// Verified by disassembly: the only string literal after the base
/// call is "value", passed to Util.GetObjectKey&lt;object&gt;.
/// </summary>
public class CreateAccountResponse : RecNetResult
{
    [JsonPropertyName("value")]
    public RecNetAccount Account { get; set; } = new();
}
