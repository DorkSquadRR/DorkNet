using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Auth;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Accounts;

/// <summary>
/// accounts.rec.net — account management.
/// Shapes match dump.cs:
///   RecNet.Account                 { AccountId, RawUsername, Username, DisplayName,
///                                    ProfileImage, TreatAsJunior, HasBirthday, Platforms }
///   RecNet.SelfAccount             Account + { Email, Phone, Birthday, JuniorState,
///                                              ParentAccountId }
///   RecNet.RecNetResult            { Success, Error }
///   Accounts.CreateAccountResponse RecNetResult + { Account }
/// </summary>
[ApiController]
public class AccountsController(
    PlayerService playerService,
    AuthService authService,
    OrphanAccountTracker orphans,
    DorkNetDbContext db,
    NotificationService notifications,
    ServerSettingsService settings,
    ILogger<AccountsController> logger) : ControllerBase
{
    [HttpGet("/account/v1/savedlogins")]
    [HttpGet("/account/v2/savedlogins")]
    [HttpGet("/account/v1/cachedlogins")]
    public async Task<IActionResult> GetSavedLogins(
        [FromQuery] int platform = 0,
        [FromQuery] string? platformId = null)
    {
        var players = await playerService.GetCachedLoginsAsync(platform, platformId);
        return Ok(players.Select(p => new CachedLogin
        {
            Platform = platform,
            PlatformId = platformId ?? string.Empty,
            AccountId = (int)p.Id,
            LastLoginTime = p.LastSeenAt,
            RequirePassword = false,
        }).ToList());
    }

    // Accounts.GetLocalAccount → SelfAccount shape.
    // The 2020 client calls the unversioned /account/me path (observed in
    // server logs); v1/v2 forms are kept for older builds and api.rec.net
    // bridging. Returns SelfAccount, NOT base Account (this is the *self*
    // endpoint — RecNet.Login.<>c.<LoginHelper>b__35_4 expects a SelfAccount).
    [HttpGet("/account/me")]
    [HttpGet("/account/v1/me")]
    [HttpGet("/account/v2/me")]
    [HttpGet("/account/v1/account")]
    public async Task<IActionResult> GetMe()
    {
        var player = await GetCurrentPlayerAsync();
        if (player is null) return Unauthorized();
        return Ok(BuildSelfAccount(player.Id, player.Username, player.DisplayName, player.ProfileImageName));
    }

    /// <summary>
    /// Resolves the player tied to the bearer token on the request. Returns
    /// null when no/invalid bearer is supplied — the caller decides whether
    /// that is a 401 (every "self" endpoint should be) or a noisier
    /// fallback. Anonymous-account creation was previously the fallback
    /// path; that's gone now because every caller has gone through
    /// `/account/create` (deviceId-bound) before reaching us.
    /// </summary>
    private async Task<Data.Entities.PlayerEntity?> GetCurrentPlayerAsync()
    {
        var auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        var token = auth["Bearer ".Length..].Trim();
        var id = authService.ValidateToken(token);
        if (id is not long playerId) return null;
        return await playerService.GetByIdAsync(playerId);
    }

    // Accounts.GetAccountById → Account shape. The 2020 watch posts to
    // the BARE <c>/account/{id}</c> path (no <c>v1</c> prefix) when
    // resolving a room's creator from RoomDetails — verified in the
    // output_log "Failed to GetAccountById: HTTP Error 404" trace where
    // PlayerDetailsWatchUIFlow.SetAccount NREs because the GetAccountById
    // promise rejected with 404. Accept both shapes.
    [HttpGet("/account/{accountId:long}")]
    [HttpGet("/account/v1/{accountId:long}")]
    [HttpGet("/account/v1/account/{accountId:long}")]
    public async Task<IActionResult> GetAccountById(long accountId)
    {
        var player = await playerService.GetByIdAsync(accountId);
        if (player is null) return NotFound();
        return Ok(BuildAccount(player.Id, player.Username, player.DisplayName, player.ProfileImageName));
    }

    // Accounts.GetAccountByUsername
    [HttpGet("/account/v1/username/{username}")]
    public async Task<IActionResult> GetAccountByUsername(string username)
    {
        var player = await playerService.GetByUsernameAsync(username);
        if (player is null) return NotFound();
        return Ok(BuildAccount(player.Id, player.Username, player.DisplayName, player.ProfileImageName));
    }

    // Accounts.CreateAccount — actual URL observed in server logs:
    //   POST accounts.rec.net/account/create
    //   body: platform=0&platformId=<steamId>&deviceId=<hash>
    // Game expects CreateAccountResponse {Success, Error, Account}. Lambda
    // <CreateAccount>b__29_0 NPEs if response.Account is null after deserializing.
    // Clean PascalCase here — multi-case keys appear to confuse LitJson's typed
    // path so its TypeMap.Add throws "Key: none" or leaves Account unpopulated.
    [HttpPost("/account/create")]
    [HttpPost("/account/v1/create")]
    [HttpPost("/account/v1")]
    [HttpPost("/account/v2/create")]
    public async Task<IActionResult> CreateAccount(
        [FromForm] int platform,
        [FromForm] string? platformId,
        [FromForm] string? deviceId)
    {
        // Admin kill-switch. Watch's <CreateAccount>b__29_0 lambda
        // (Accounts_NestedType___c.txt:311) branches on Success: false
        // → ErroredPromise<Account> with the Error string surfaced to
        // the player; true → reads response.Account. Account stays a
        // non-null stub even on refusal so LitJson's typed Deserialize
        // path doesn't fault before the Success check runs.
        if (await settings.AreSignupsDisabledAsync())
        {
            logger.LogInformation(
                "[accounts] account/create refused — signups disabled (device={Device} platform={Platform} platformId={PlatformId})",
                deviceId, platform, platformId);
            return Ok(new CreateAccountResponse
            {
                Success = false,
                Error = "Account creation is currently disabled by the server admin.",
            });
        }

        // The 2020 watch's "Create new account" UI and its boot-time
        // "ensure my account exists" probe BOTH hit this endpoint with
        // identical request bodies. To support multi-account-per-device
        // we always create a brand-new row; the boot-flow case (where
        // the user actually wants their cached account) is handled by
        // the OrphanAccountTracker — when the next cached_login picks
        // a DIFFERENT account_id, that handler deletes this just-
        // created row.
        var player = await playerService.CreateNewAccountAsync(
            deviceId: deviceId,
            platform: platform,
            platformId: platformId);

        orphans.TrackCreation(deviceId, platformId, player.Id);

        logger.LogInformation(
            "[accounts] account/create created {Id} ({Username}) for device {Device} platform={Platform} platformId={PlatformId}",
            player.Id, player.Username, deviceId, platform, platformId);

        return Ok(new CreateAccountResponse
        {
            Success = true,
            Error = string.Empty,
            Account = BuildAccount(player.Id, player.Username, player.DisplayName, player.ProfileImageName),
        });
    }

    // Accounts.SearchForAccounts — used by the watch's typeahead AND the
    // localhost public site's account search. Anonymous-safe (no JWT
    // required) so the front-facing site can call it directly.
    [HttpGet("/account/v1/search")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> Search([FromQuery] string? query, [FromQuery] int take = 20)
    {
        if (string.IsNullOrWhiteSpace(query)) return Ok(Array.Empty<object>());
        var clamped = Math.Clamp(take, 1, 50);
        var matches = await playerService.SearchAsync(query.Trim(), clamped);
        return Ok(matches
            .Select(p => BuildAccount(p.Id, p.Username, p.DisplayName, p.ProfileImageName))
            .ToList());
    }

    // Accounts.GetAccountsBulk / GetAccountsFromPlatformIds / FromEmails / FromPhones
    // Form body: Ids=1,2,3 (BestHTTP HTTPFormBase.AddField). Accept both
    // GET and POST and any content type so route matching succeeds even
    // when the client sends multipart vs urlencoded.
    [HttpPost("/account/v1/bulk")]
    [HttpGet("/account/v1/bulk")]
    [HttpPost("/account/bulk")]
    [HttpGet("/account/bulk")]
    [HttpPost("/account/v1/platformids")]
    [HttpPost("/account/v1/emails")]
    [HttpPost("/account/v1/phones")]
    [HttpPost("/account/v1/byplatformid")]
    public async Task<IActionResult> Bulk()
    {
        // The watch calls this route family with payloads keyed by
        // either Ids (account ids), Emails (email addresses), or
        // Phones (E.164 strings) depending on which Accounts.GetXxx
        // helper fired it. Read all three plus the lowercase / singular
        // variants the cached-account hydration uses.
        string? rawIds = null;
        string? rawEmails = null;
        string? rawPhones = null;
        try
        {
            if (Request.HasFormContentType)
            {
                rawIds = NonEmptyOrNull(Request.Form["Ids"].ToString())
                      ?? NonEmptyOrNull(Request.Form["ids"].ToString())
                      ?? NonEmptyOrNull(Request.Form["Id"].ToString())
                      ?? NonEmptyOrNull(Request.Form["id"].ToString());
                rawEmails = NonEmptyOrNull(Request.Form["Emails"].ToString())
                         ?? NonEmptyOrNull(Request.Form["emails"].ToString())
                         ?? NonEmptyOrNull(Request.Form["Email"].ToString())
                         ?? NonEmptyOrNull(Request.Form["email"].ToString());
                rawPhones = NonEmptyOrNull(Request.Form["Phones"].ToString())
                         ?? NonEmptyOrNull(Request.Form["phones"].ToString())
                         ?? NonEmptyOrNull(Request.Form["Phone"].ToString())
                         ?? NonEmptyOrNull(Request.Form["phone"].ToString());
            }
        }
        catch { /* form parse may fail on non-form bodies */ }
        rawIds ??= NonEmptyOrNull(Request.Query["Ids"].ToString())
                ?? NonEmptyOrNull(Request.Query["ids"].ToString())
                ?? NonEmptyOrNull(Request.Query["Id"].ToString())
                ?? NonEmptyOrNull(Request.Query["id"].ToString());

        static string? NonEmptyOrNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        // Build the set of player ids to hydrate from whatever the
        // caller sent. Each source contributes additively — the watch
        // never mixes (Ids + Emails) in one call, but the union is
        // harmless when it happens.
        var ids = new HashSet<long>();
        if (rawIds is not null)
        {
            foreach (var n in rawIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (long.TryParse(n, out var v) && v > 0) ids.Add(v);
        }
        if (rawEmails is not null)
        {
            var emails = rawEmails.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.ToLowerInvariant())
                .Where(e => e.Length > 0)
                .ToList();
            if (emails.Count > 0)
            {
                var matched = await db.Players
                    .Where(p => p.Email != null && emails.Contains(p.Email.ToLower()))
                    .Select(p => p.Id)
                    .ToListAsync();
                foreach (var id in matched) ids.Add(id);
            }
        }
        if (rawPhones is not null)
        {
            var phones = rawPhones.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => p.Length > 0)
                .ToList();
            if (phones.Count > 0)
            {
                var matched = await db.Players
                    .Where(p => p.Phone != null && phones.Contains(p.Phone))
                    .Select(p => p.Id)
                    .ToListAsync();
                foreach (var id in matched) ids.Add(id);
            }
        }
        if (ids.Count == 0) return Ok(Array.Empty<object>());

        // Strip ids the caller has blocked, and ids that have blocked the
        // caller. This prevents `bulk` from being used as a cheap oracle
        // ("who blocked me?"). When the caller is anonymous (no JWT) we
        // skip the filter entirely — `bulk` is also used during boot for
        // public profile hydration before the player has authenticated.
        var me = this.CurrentPlayerId();
        if (me is long callerId)
        {
            var blocked = await db.Relationships
                .Where(r => r.Status == RelationshipStatus.Blocked &&
                            ((r.RequesterId == callerId && ids.Contains(r.TargetId)) ||
                             (r.TargetId == callerId && ids.Contains(r.RequesterId))))
                .Select(r => r.RequesterId == callerId ? r.TargetId : r.RequesterId)
                .ToListAsync();
            foreach (var b in blocked) ids.Remove(b);
        }

        var accounts = new List<object>();
        foreach (var id in ids)
        {
            var p = await playerService.GetByIdAsync(id);
            if (p is not null)
                accounts.Add(BuildAccount(p.Id, p.Username, p.DisplayName, p.ProfileImageName));
        }
        return Ok(accounts);
    }

    // Accounts.GetNameGenerationOptions  → NameGenDTO {Nouns, Adjectives}
    [HttpGet("/account/v1/namegen")]
    [HttpGet("/account/v1/namegeneration")]
    public IActionResult NameGen() => Ok(new
    {
        Nouns = new[] { "Player", "Hero", "Star", "Fox", "Wolf" },
        Adjectives = new[] { "Brave", "Swift", "Clever", "Bold", "Bright" },
    });

    // Phone-confirm is the one mutation we don't persist — there's no
    // SMS provider in the loop. Acknowledge silently.
    [HttpPost("/account/v1/confirmphone")]
    public IActionResult ConfirmPhone() =>
        Ok(new RecNetResult { Success = true, Error = string.Empty });

    /// <summary>POST /account/v1/birthday — sets the player's
    /// birthday and updates IsJunior accordingly. Rec Room's junior
    /// cutoff is &lt;13 years old; we recompute it on every birthday
    /// write so a freshly turned 13 player loses the junior flag the
    /// next time they update.</summary>
    [HttpPost("/account/v1/birthday")]
    [HttpPost("/account/me/birthday")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> SetBirthday([FromForm(Name = "birthday")] string? birthday)
    {
        if (string.IsNullOrWhiteSpace(birthday) ||
            !DateTime.TryParse(birthday, out var bday))
            return Ok(new RecNetResult { Success = false, Error = "invalid_date" });

        var player = await GetCurrentPlayerAsync();
        if (player is null) return Unauthorized();
        player.Birthday = bday.Date;
        player.IsJunior = AgeYears(bday) < 13;
        await db.SaveChangesAsync();
        return Ok(new RecNetResult { Success = true, Error = string.Empty });
    }

    /// <summary>POST /account/v1/email — set or clear contact email.
    /// Persisted verbatim; format isn't validated server-side, the
    /// in-game email keyboard does it.</summary>
    [HttpPost("/account/v1/email")]
    [HttpPost("/account/me/email")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> SetEmail([FromForm(Name = "email")] string? email)
    {
        var player = await GetCurrentPlayerAsync();
        if (player is null) return Unauthorized();
        player.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        await db.SaveChangesAsync();
        return Ok(new RecNetResult { Success = true, Error = string.Empty });
    }

    /// <summary>POST /account/v1/phone — set or clear contact phone.</summary>
    [HttpPost("/account/v1/phone")]
    [HttpPost("/account/me/phone")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> SetPhone([FromForm(Name = "phone")] string? phone)
    {
        var player = await GetCurrentPlayerAsync();
        if (player is null) return Unauthorized();
        player.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        await db.SaveChangesAsync();
        return Ok(new RecNetResult { Success = true, Error = string.Empty });
    }

    /// <summary>POST /account/v1/profileimage — body: imageName=...
    /// or fileName=... The client first POSTs the bytes to
    /// <c>storage.rec.net/upload</c> with FileType=Image, gets back a
    /// Filename, then sends that filename here. We persist it on
    /// PlayerEntity.ProfileImageName; reads from /account/me +
    /// /account/v1/{id} surface it.</summary>
    [HttpPost("/account/v1/profileimage")]
    [HttpPost("/account/me/profileimage")]
    [HttpPost("/profileimage")]
    [HttpPut("/account/v1/profileimage")]
    [HttpPut("/account/me/profileimage")]
    [HttpPut("/profileimage")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> SetProfileImage(
        [FromForm(Name = "imageName")] string? imageName,
        [FromForm(Name = "fileName")] string? fileName,
        [FromForm(Name = "image")] string? imageAlt)
    {
        // Fall back to query string for the rare client paths that
        // send these fields as URL params instead of form fields.
        var name = imageName ?? fileName ?? imageAlt
                   ?? Request.Query["imageName"].ToString()
                   ?? Request.Query["fileName"].ToString();
        var player = await GetCurrentPlayerAsync();
        if (player is null) return Unauthorized();
        player.ProfileImageName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        await db.SaveChangesAsync();
        logger.LogInformation(
            "[accounts] profile image set: player={PlayerId} → {Name}",
            player.Id, player.ProfileImageName ?? "<cleared>");
        await notifications.AccountChanged(player);
        return Ok(new RecNetResult { Success = true, Error = string.Empty });
    }

    private static int AgeYears(DateTime birthday)
    {
        var today = DateTime.UtcNow.Date;
        var age = today.Year - birthday.Year;
        if (birthday.Date > today.AddYears(-age)) age--;
        return age;
    }

    // Everyone is a dev for now — exposes the in-game debug console + dev
    // tools. Per user request: easier debugging while iterating on the
    // private server. Flip back to false later if you want shipping behavior.
    [HttpGet("/account/v1/{accountId:long}/isdeveloper")]
    [HttpGet("/account/me/isdeveloper")]
    public IActionResult IsDeveloper(long accountId = 0) => Ok(new { IsDeveloper = true });

    /// <summary>
    /// PUT /account/me/username — body: username=NewName
    /// Real handler that persists the change to the DB. Was previously
    /// going to GlobalCatchAllController and returning the shotgun, which
    /// meant username changes didn't actually save.
    /// </summary>
    [HttpPut("/account/me/username")]
    [HttpPost("/account/me/username")]
    [HttpPost("/account/v1/username")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> ChangeUsername([FromForm(Name = "username")] string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return Ok(new { success = false, error = "Username required" });

        var player = await GetCurrentPlayerAsync();
        if (player is null) return Unauthorized();
        await playerService.UpdateUsernameAsync(player.Id, username.Trim());
        var updated = await playerService.GetByIdAsync(player.Id);
        if (updated is not null) await notifications.AccountChanged(updated);
        return Ok(new
        {
            success = true,
            error = "",
            account = new
            {
                accountId = (int)player.Id,
                username = updated?.Username ?? username,
                displayName = updated?.DisplayName ?? username,
            },
        });
    }

    /// <summary>
    /// PUT /account/me/displayname — same flow as username but only updates
    /// the DisplayName field (Username stays as-is).
    /// </summary>
    [HttpPut("/account/me/displayname")]
    [HttpPost("/account/me/displayname")]
    [HttpPost("/account/v1/displayname")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> ChangeDisplayName([FromForm(Name = "displayName")] string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return Ok(new { success = false, error = "DisplayName required" });
        var player = await GetCurrentPlayerAsync();
        if (player is null) return Unauthorized();
        await playerService.UpdateDisplayNameAsync(player.Id, displayName.Trim());
        var updated = await playerService.GetByIdAsync(player.Id);
        if (updated is not null) await notifications.AccountChanged(updated);
        return Ok(new { success = true, error = "" });
    }

    /// <summary>
    /// PUT /account/me/bio — body: bio=...
    /// </summary>
    [HttpPut("/account/me/bio")]
    [HttpPost("/account/me/bio")]
    [HttpPost("/account/v1/bio")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> ChangeBio([FromForm(Name = "bio")] string? bio)
    {
        var player = await GetCurrentPlayerAsync();
        if (player is null) return Unauthorized();
        await playerService.UpdateBioAsync(player.Id, bio ?? string.Empty);
        return Ok(new { success = true, error = "" });
    }

    // ParentalControl.Deserialize at RVA 0x14543A0 — required keys:
    //   accountId               (int, GetKey)
    //   disallowInAppPurchases  (bool, GetKey)
    // Returning {} crashes the client with
    //   "Failed to GetLocalAccountParentalControl: Malformed Response".
    [HttpGet("/parentalcontrol/me")]
    [HttpGet("/parentalcontrol/v1/me")]
    public async Task<IActionResult> ParentalControlMe()
    {
        var player = await GetCurrentPlayerAsync();
        if (player is null) return Unauthorized();
        return Ok(new
        {
            accountId = (int)player.Id,
            disallowInAppPurchases = false,
        });
    }

    [HttpGet("/parentalcontrol/{accountId:long}")]
    [HttpGet("/parentalcontrol/v1/{accountId:long}")]
    public IActionResult ParentalControlById(long accountId) => Ok(new
    {
        accountId = (int)accountId,
        disallowInAppPurchases = false,
    });

    [HttpGet("/account/v1/me/disallowiap")]
    public IActionResult DisallowIap() => Ok(new { Disallow = false });

    /// <summary>GET /account/{id}/bio — public bio lookup. Watch URL
    /// from <c>Cpp2IL_ISIL/.../RecNet/Accounts.txt</c> line 5686:
    /// <c>"account/{0}/bio"</c> (no v1 prefix). Wire shape from
    /// BioDTO.txt: required keys <c>"accountId"</c> (int, Util.GetKey)
    /// and <c>"bio"</c> (string, optional via the GetKeyOrDefault-
    /// flavoured 0x180EE6770 helper). Returning the previous
    /// <c>{ Bio = ... }</c> PascalCase shape made the watch's
    /// <c>BioDTO.Deserialize</c> KeyNotFoundException on
    /// <c>accountId</c>, which surfaced as "Failed to GetBio for
    /// accountId N: Malformed Response" in the client log.</summary>
    [HttpGet("/account/{accountId:long}/bio")]
    [HttpGet("/account/v1/{accountId:long}/bio")]
    public async Task<IActionResult> GetBio(long accountId)
    {
        var p = await playerService.GetByIdAsync(accountId);
        return Ok(new { accountId = (int)accountId, bio = p?.Bio ?? string.Empty });
    }

    [HttpGet("/account/v1/{accountId:long}/platformid")]
    public IActionResult GetPlatformId(long accountId) => Ok(new { PlatformId = 0L });

    // Previously had a /{*path} catch-all that returned [] / {}. Removed —
    // unknown accounts URLs now 404 so we notice them in logs and wire
    // them properly rather than silently masking missing endpoints.

    private static RecNetAccount BuildAccount(long id, string username, string? displayName, string? profileImage = null)
        => new()
        {
            AccountId = (int)id,
            RawUsername = username,
            Username = username,
            DisplayName = displayName ?? username,
            ProfileImage = profileImage ?? string.Empty,
            TreatAsJunior = false,
            HasBirthday = true,
            Platforms = 1,
        };

    private static RecNetSelfAccount BuildSelfAccount(long id, string username, string? displayName, string? profileImage = null)
        => new()
        {
            AccountId = (int)id,
            RawUsername = username,
            Username = username,
            DisplayName = displayName ?? username,
            ProfileImage = profileImage ?? string.Empty,
            TreatAsJunior = false,
            HasBirthday = true,
            Platforms = 1,
            Email = string.Empty,
            Phone = string.Empty,
            Birthday = new DateTime(2000, 1, 1),
            JuniorState = 0,
            ParentAccountId = null,
        };
}
