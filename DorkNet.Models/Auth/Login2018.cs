using System.Text.Json.Serialization;

namespace DorkNet.Models.Auth;

// ── June-2018 (build 20180621_EA) login wire shapes ──────────────────────────
//
// The 2018 client speaks a DIFFERENT login protocol than the 2019/2020 OAuth
// flow the rest of DorkNet implements:
//   • POST api/platformlogin/v1/{loginaccount,logincached,createaccount}
//   • request body is a JSON object (Unity JsonUtility / LitJson), NOT form
//   • response is the "login envelope" below, read by RecNet.cs:40014-40021
//     via case-sensitive LitJson keys.
//
// Keys verified from the assembly's REAL (name-preserved) Deserialize methods
// via dnlib — the obfuscated-name sibling methods are Beebyte decoys with
// scrambled literals (see project_2018_mono_obfuscation memory).

/// <summary>
/// Login response envelope. RecNet login-response Deserialize (RecNet.cs:40016-40020)
/// reads: Error (req string), Player (optional object), Token (req string),
/// FirstLoginOfTheDay (req bool), AnalyticsSessionId (req long). Empty Error
/// means success; a non-empty Error is shown to the user and aborts login.
/// </summary>
public class Login2018Response
{
    [JsonPropertyName("Error")]
    public string Error { get; set; } = string.Empty;

    [JsonPropertyName("Player")]
    public Player2018? Player { get; set; }

    [JsonPropertyName("Token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("FirstLoginOfTheDay")]
    public bool FirstLoginOfTheDay { get; set; }

    [JsonPropertyName("AnalyticsSessionId")]
    public long AnalyticsSessionId { get; set; }
}

/// <summary>
/// 2018 player object embedded in the login envelope and returned bare from
/// getcachedlogins. Keys + required/optional verified from RecNet.cs:64632-64667
/// (RecNet.EEBPLECPEGD.Deserialize):
///   REQUIRED (Util.GetKey throws if missing): Id, Username, DisplayName, XP,
///     Level, RegistrationStatus, Developer, CanReceiveInvites, ProfileImageName,
///     JuniorProfile, ForceJuniorImages, PendingJunior, HasBirthday.
///   OPTIONAL: Bio (TryGetKey), AvoidJuniors (ContainsKey-guarded),
///     PlayerReputation/PlatformId (GetObjectKey -> null ok; omitted here),
///     GroupMemberships (ContainsKey-guarded; omitted).
/// NOTE the 2018 client uses "Id" (not "AccountId") and "Developer" (not
/// "IsDeveloper") — distinct from the 2020 PlayerProfile DTO.
/// </summary>
public class Player2018
{
    [JsonPropertyName("Id")]
    public long Id { get; set; }

    [JsonPropertyName("Username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("DisplayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("Bio")]
    public string Bio { get; set; } = string.Empty;

    [JsonPropertyName("XP")]
    public int XP { get; set; }

    [JsonPropertyName("Level")]
    public int Level { get; set; } = 1;

    // Registration step enum (ProfileSelectionManager.NFCBDCFLBOC):
    // invalid=-1, video=0, birthday=1, email=2, COMPLETE=10. Anything < 10
    // makes the client run the signup step flow (it got "stuck on birthday").
    // 10 = fully registered → client goes straight in-game.
    [JsonPropertyName("RegistrationStatus")]
    public int RegistrationStatus { get; set; } = 10;

    [JsonPropertyName("Developer")]
    public bool Developer { get; set; }

    [JsonPropertyName("CanReceiveInvites")]
    public bool CanReceiveInvites { get; set; } = true;

    [JsonPropertyName("ProfileImageName")]
    public string ProfileImageName { get; set; } = string.Empty;

    [JsonPropertyName("JuniorProfile")]
    public bool JuniorProfile { get; set; }

    [JsonPropertyName("ForceJuniorImages")]
    public bool ForceJuniorImages { get; set; }

    [JsonPropertyName("PendingJunior")]
    public bool PendingJunior { get; set; }

    [JsonPropertyName("HasBirthday")]
    public bool HasBirthday { get; set; }

    [JsonPropertyName("AvoidJuniors")]
    public bool AvoidJuniors { get; set; }
}

/// <summary>
/// Generic 2018 success envelope {Success, Message} (RecNet EFDOILDKBFK) used by
/// registeraccount (email attach) and other simple POSTs. Note the key is
/// "Message" (not "error").
/// </summary>
public class SuccessMessage2018
{
    [JsonPropertyName("Success")]
    public bool Success { get; set; }

    [JsonPropertyName("Message")]
    public string Message { get; set; } = string.Empty;
}
