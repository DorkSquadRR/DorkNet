using Microsoft.AspNetCore.Authorization;
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
    PlayerPresenceService presence,
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
    /// DELETE <c>/account/me</c> — the 2023 client's "delete local account"
    /// (RecNet.Runtime <c>OPMAPIOEIFG.ADFMLPGEBOF()</c>). Binary evidence:
    /// the request object is built at OPMAPIOEIFG.txt:8767-8777 with the
    /// verb ordinal in <c>rdx</c> = 4 (BestHTTP.HTTPMethods.Delete) and the
    /// path literal <c>"account/me"</c> at :8771; the failure branch at
    /// :8790 carries the string <c>"Failed to delete local account"</c>.
    /// The issuing method returns <c>LDGADANDBIO</c>, the fire-and-forget
    /// promise handle — the response BODY is never parsed, only the HTTP
    /// status decides success — so the RecNetResult we emit is purely for
    /// human/curl consumption. Registering only <c>[HttpGet]</c> on this
    /// path (the previous state) produced a 405 and the in-game
    /// "delete my account" button silently failed.
    ///
    /// Erasure runs through the same <see cref="PlayerAccountPurge"/>
    /// cascade the admin SPA uses, inside one transaction: personal rows
    /// are deleted, authored durable content is reassigned to the system
    /// account, then the Players row goes and the live session is kicked
    /// so the bearer token stops being useful immediately.
    /// </summary>
    [HttpDelete("/account/me")]
    [HttpDelete("/account/v1/me")]
    [HttpDelete("/account/v2/me")]
    public async Task<IActionResult> DeleteMe()
    {
        var player = await GetCurrentPlayerAsync();
        if (player is null) return Unauthorized();

        // The system account (id 1) owns every reassigned artefact — losing
        // it would orphan the whole DB — and admin accounts must be demoted
        // through the SPA first so a compromised session can't nuke the
        // server's only operator. Both refusals are surfaced as a non-2xx so
        // the client's promise rejects and the player sees the failure
        // rather than a phantom success.
        if (player.Id == 1)
        {
            logger.LogWarning("[accounts] DELETE account/me refused — system account");
            return BadRequest(new RecNetResult
            {
                Success = false,
                Error = "The system account cannot be deleted.",
            });
        }
        if (player.IsAdmin)
        {
            logger.LogWarning(
                "[accounts] DELETE account/me refused — {Id} ({Username}) is an admin",
                player.Id, player.Username);
            return BadRequest(new RecNetResult
            {
                Success = false,
                Error = "Admin accounts cannot self-delete. Demote the account first.",
            });
        }

        var playerId = player.Id;
        var username = player.Username;

        // Authored content is reassigned rather than destroyed. Preference
        // order: the system account (id 1), else the lowest-id surviving
        // account, else 0 (single-account server — nothing left to own it).
        var systemPlayerId = await db.Players.AnyAsync(p => p.Id == 1)
            ? 1L
            : await db.Players.Where(p => p.Id != playerId)
                .OrderBy(p => p.Id)
                .Select(p => p.Id)
                .FirstOrDefaultAsync();

        // GetCurrentPlayerAsync goes through PlayerService.GetByIdAsync,
        // which Include()s the Avatar and leaves it tracked on this same
        // scoped DbContext. PurgeAsync deletes that avatar row with
        // ExecuteDelete, so leaving it tracked would make the Players.Remove
        // cascade emit a second DELETE for an already-gone row and throw
        // DbUpdateConcurrencyException. Drop the graph, then re-read the
        // Players row on its own.
        db.ChangeTracker.Clear();
        var row = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId);
        if (row is null) return Ok(new RecNetResult { Success = true, Error = string.Empty });

        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var summary = await PlayerAccountPurge.PurgeAsync(db, playerId, systemPlayerId);
            db.Players.Remove(row);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            logger.LogInformation(
                "[accounts] DELETE account/me — removed {Id} ({Username}); {Summary}",
                playerId, username, summary);
        }

        await notifications.KickPlayerAsync(playerId, "This account has been deleted.");
        presence.Clear(playerId);

        return Ok(new RecNetResult { Success = true, Error = string.Empty });
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
        var token = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? auth["Bearer ".Length..].Trim()
            : Request.Cookies[AuthService.AccessCookieName];
        if (string.IsNullOrWhiteSpace(token))
            return null;
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
    [HttpGet("/account/search")]
    [HttpGet("/account/v1/search")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    // The 2023-03-21 client names the search term "name"; binding only "query"
    // meant every player search came back empty (the handler short-circuited on
    // a null term before it ever reached the service).
    public async Task<IActionResult> Search(
        [FromQuery] string? query,
        [FromQuery] string? name,
        [FromQuery] int take = 20)
    {
        var term = !string.IsNullOrWhiteSpace(name) ? name : query;
        if (string.IsNullOrWhiteSpace(term)) return Ok(Array.Empty<object>());
        var clamped = Math.Clamp(take, 1, 50);
        var matches = await playerService.SearchAsync(term.Trim(), clamped);
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

    // Accounts.GetNameGenerationOptions → NameGenDTO {Nouns, Adjectives}
    // (two List<string>). The 2023 client also calls the newer root path
    // `namegen/options` (RecNet.Runtime EAHBKJEMHEM, two List<string>);
    // route it here too. Keys are duplicated in lower-case as a safety net
    // because the 2023 DTO's obfuscated members give no literal key names —
    // LitJson ignores keys it doesn't recognise, so extras are harmless.
    [HttpGet("/account/v1/namegen")]
    [HttpGet("/account/v1/namegeneration")]
    [HttpGet("/namegen/options")]
    public IActionResult NameGen()
    {
        var nouns = new[]
        {
            "Player", "Hero", "Star", "Fox", "Wolf", "Comet", "Tiger", "Falcon",
            "Panda", "Dragon", "Ninja", "Robot", "Ghost", "Rocket", "Shark", "Raven",
        };
        var adjectives = new[]
        {
            "Brave", "Swift", "Clever", "Bold", "Bright", "Cosmic", "Silent", "Mighty",
            "Turbo", "Lucky", "Sneaky", "Epic", "Fuzzy", "Wild", "Golden", "Neon",
        };
        // Anonymous objects serialize with the server's camelCase policy, so
        // these become {"nouns":[…],"adjectives":[…]} on the wire.
        return Ok(new { Nouns = nouns, Adjectives = adjectives });
    }

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
    // The 2023 client PUTs birthday (verb 3 at OPMAPIOEIFG.txt:7950-7954);
    // POST-only returned 405 and the birthday step of onboarding always failed.
    [HttpPut("/account/v1/birthday")]
    [HttpPut("/account/me/birthday")]
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

    private async Task SetPlayerSettingAsync(long playerId, string key, string value)
    {
        var row = await db.PlayerSettings.FirstOrDefaultAsync(s => s.PlayerId == playerId && s.Key == key);
        if (row is null)
        {
            db.PlayerSettings.Add(new PlayerSettingEntity
            {
                PlayerId = playerId,
                Key = key,
                Value = value,
            });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync();
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

    [HttpGet("/account/isactivecreator/me")]
    public async Task<IActionResult> IsActiveCreator()
    {
        var pid = this.CurrentPlayerId();
        if (pid is not long me)
            return Content("false", "application/json");

        var active = await db.Rooms.AnyAsync(r => r.CreatorPlayerId == me)
            || await db.Inventions.AnyAsync(i => i.CreatorPlayerId == me && !i.IsDeleted)
            || await db.CustomAvatarItems.AnyAsync(i => i.CreatorPlayerId == me);
        return Content(active ? "true" : "false", "application/json");
    }

    [HttpPost("/account/me/createlogintoken")]
    [HttpGet("/account/me/createlogintoken")]
    public async Task<IActionResult> CreateLoginToken()
    {
        var player = await GetCurrentPlayerAsync();
        if (player is null) return Unauthorized();

        var token = Guid.NewGuid().ToString("N");
        var expiresAt = DateTime.UtcNow.AddMinutes(10);
        await SetPlayerSettingAsync(player.Id, "remote_login_token", $"{token}|{expiresAt:O}");
        return Ok(new
        {
            Success = true,
            Error = string.Empty,
            Token = token,
            LoginToken = token,
            ExpiresAt = expiresAt,
        });
    }

    [HttpPost("/account/me/remoteauth")]
    [HttpGet("/account/me/remoteauth")]
    public async Task<IActionResult> RemoteAuth()
    {
        var token = Request.Query["token"].FirstOrDefault()
                    ?? Request.Query["loginToken"].FirstOrDefault()
                    ?? Request.Query["code"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token) && Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            token = form["token"].FirstOrDefault()
                    ?? form["loginToken"].FirstOrDefault()
                    ?? form["code"].FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            var player = await GetCurrentPlayerAsync();
            if (player is null) return Unauthorized();
            var pair = authService.GenerateTokenPair(player.Id);
            return Ok(new LoginResponse
            {
                AccessToken = pair.AccessToken,
                RefreshToken = pair.RefreshToken,
            });
        }

        var prefix = token.Trim() + "|";
        var row = await db.PlayerSettings
            .Where(s => s.Key == "remote_login_token" && s.Value.StartsWith(prefix))
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync();
        if (row is null) return Unauthorized();

        var rawExpiry = row.Value[prefix.Length..];
        if (!DateTime.TryParse(rawExpiry, out var expiresAt) || expiresAt < DateTime.UtcNow)
            return Unauthorized();

        db.PlayerSettings.Remove(row);
        await db.SaveChangesAsync();
        var (access, refresh) = authService.GenerateTokenPair(row.PlayerId);
        return Ok(new LoginResponse
        {
            AccessToken = access,
            RefreshToken = refresh,
        });
    }

    [HttpPost("/account/recoverpassword")]
    [HttpGet("/account/recoverpassword")]
    [Consumes("application/x-www-form-urlencoded", "multipart/form-data", "application/json")]
    public async Task<IActionResult> RecoverPassword(
        [FromForm] string? username,
        [FromForm] string? email)
    {
        var nameOrEmail = username
                          ?? email
                          ?? Request.Query["username"].FirstOrDefault()
                          ?? Request.Query["email"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(nameOrEmail))
            return Ok(new RecNetResult { Success = false, Error = "missing_account" });

        var needle = nameOrEmail.Trim().ToLowerInvariant();
        var player = await db.Players
            .FirstOrDefaultAsync(p => p.Username.ToLower() == needle
                                      || (p.Email != null && p.Email.ToLower() == needle));
        if (player is null)
            return Ok(new RecNetResult { Success = false, Error = "unknown_account" });

        var code = Random.Shared.Next(100000, 999999).ToString();
        var expiresAt = DateTime.UtcNow.AddMinutes(30);
        await SetPlayerSettingAsync(player.Id, "password_recovery_code", $"{code}|{expiresAt:O}");
        return Ok(new
        {
            Success = true,
            Error = string.Empty,
            Code = code,
            ExpiresAt = expiresAt,
        });
    }

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

    /// <summary>
    /// GET <c>/platformid/{accountId}</c> — the 2023 client's
    /// <c>OPMAPIOEIFG.DPLOCBLKCEB(Int32 accountId)</c>. Binary evidence:
    /// path literal <c>"platformid/{0}"</c> at OPMAPIOEIFG.txt:11056 with
    /// the verb ordinal <c>rdx</c> = 0 (GET) at :11066, and the failure
    /// string <c>"Failed to GetPlatformId for accountId {0}"</c> at :11102.
    ///
    /// RESPONSE SHAPE: the issuing method's return type is
    /// <c>FGLDKEJLAKB&lt;System.String&gt;</c> — a BARE JSON string, not an
    /// object. Emitting <c>{"PlatformId":0}</c> makes the strict reader hit
    /// <c>'{'</c> where it wants <c>'"'</c> and throw. The legacy
    /// <c>/account/v1/{id}/platformid</c> spelling is kept as an alias and
    /// now returns the real stored value instead of a hardcoded 0.
    ///
    /// The client short-circuits and never issues the request when
    /// accountId &lt;= 0 (:11052-11053), so we only ever see real ids.
    /// Unknown accounts get an empty string — "no platform identity" is the
    /// honest answer and keeps the promise resolved rather than rejected.
    /// </summary>
    // LastPlatformId is a third-party identifier (Steam/Oculus id); without
    // an auth gate this was an anonymous enumeration oracle over every
    // account. The 2023 client only ever calls it with a token.
    [HttpGet("/platformid/{accountId:long}")]
    [HttpGet("/account/v1/{accountId:long}/platformid")]
    [Authorize]
    public async Task<IActionResult> GetPlatformId(long accountId)
    {
        var platformId = await db.Players
            .Where(p => p.Id == accountId)
            .Select(p => p.LastPlatformId)
            .FirstOrDefaultAsync();
        return Content(
            System.Text.Json.JsonSerializer.Serialize(platformId ?? string.Empty),
            "application/json");
    }

    /// <summary>
    /// GET <c>/accounts/{accountId}/receives/{category}</c> — the 2023
    /// client's platform-notification opt-in probe
    /// (<c>LELAJKMOMIA.GCIPPNCOKAJ(Int32 accountId)</c>). Binary evidence:
    /// path literal at LELAJKMOMIA.txt:695, verb ordinal <c>rdx</c> = 0
    /// (GET) at :705, service-host ordinal 15 (platformnotifications) at
    /// :709. The <c>{1}</c> segment is a <c>CCOKJMJOIJF</c> enum value
    /// boxed and <c>String.Format</c>ed at :690-698, i.e. the enum MEMBER
    /// NAME — the constant at this call site is value 8 (:693) but the
    /// member names appear nowhere as literals in the binary, so the
    /// segment MUST stay an unconstrained opaque string.
    ///
    /// RESPONSE SHAPE: the continuation is <c>System.Action&lt;Boolean&gt;</c>
    /// over <c>FGLDKEJLAKB&lt;Boolean&gt;</c> (:721/:733) — a BARE JSON
    /// boolean, no envelope. A 404 here rejects the promise inside AGUI's
    /// AccountSpecificUIBehaviour and blocks the notification-gated action.
    ///
    /// Answered from the real <see cref="NotificationPrefsEntity"/> row that
    /// backs <c>PUT /preferences</c>: <c>AllowAll</c> gates everything, then
    /// the per-category flag. Categories are resolved by the same ids the
    /// platformnotifications <c>config/categories</c> catalogue publishes
    /// (1 Messages, 2 Friend Requests, 3 Event Invites, 4 Announcements) and
    /// by normalised name, so both the numeric and member-name spellings
    /// land on the right column. A category we don't model (such as the
    /// value-8 one this call site uses) falls back to <c>AllowAll</c> — the
    /// player has no mute for it, which is the truthful answer, not a stub.
    /// </summary>
    [HttpGet("/accounts/{accountId:long}/receives/{category}")]
    [HttpGet("/platformnotifications/accounts/{accountId:long}/receives/{category}")]
    public async Task<IActionResult> Receives(long accountId, string category)
    {
        var exists = await db.Players.AnyAsync(p => p.Id == accountId);
        if (!exists) return Content("false", "application/json");

        var prefs = await db.NotificationPrefs
            .FirstOrDefaultAsync(p => p.PlayerId == accountId);
        // No row at all = never opted out of anything.
        if (prefs is null) return Content("true", "application/json");

        var receives = prefs.AllowAll && CategoryAllowed(prefs, category);
        return Content(receives ? "true" : "false", "application/json");
    }

    /// <summary>Maps the opaque <c>{category}</c> segment onto the columns
    /// of <see cref="NotificationPrefsEntity"/>. Accepts the numeric
    /// CategoryId published by <c>config/categories</c> as well as the enum
    /// member name in any casing/punctuation; anything we don't model is
    /// only gated by <c>AllowAll</c>.</summary>
    private static bool CategoryAllowed(NotificationPrefsEntity prefs, string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return true;
        var key = new string(category.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return key switch
        {
            "1" or "message" or "messages" or "directmessage" or "directmessages"
                => prefs.AllowMessage,
            "2" or "friendrequest" or "friendrequests"
                => prefs.AllowFriendRequest,
            "3" or "eventinvite" or "eventinvites" or "invite" or "invites"
                => prefs.AllowEventInvite,
            "4" or "announcement" or "announcements" or "news"
                => prefs.AllowAnnouncements,
            _ => true,
        };
    }

    [HttpGet("/accountprivacysettings/{accountId:long}")]
    [HttpGet("/account/v1/privacysettings/{accountId:long}")]
    [HttpGet("/account/{accountId:long}/privacysettings")]
    public async Task<IActionResult> GetAccountPrivacySettings(long accountId)
    {
        // Recent-history visibility is the one privacy bit the watch can
        // actually toggle (setter below); read it back so the client's
        // GetRecentHistoryVisibility reader sees the persisted value.
        var recentHistoryVisible = await GetRecentHistoryVisibleAsync(accountId);
        return Ok(new
        {
            AccountId = (int)accountId,
            IsProfileVisible = true,
            IsOnlineStatusVisible = true,
            IsActivityVisible = true,
            ShowOnlineStatus = true,
            ShowActivity = true,
            ShowCurrentRoom = true,
            ReceiveFriendRequests = true,
            ReceiveInvites = true,
            ReceiveMessages = true,
            IsRecentHistoryVisible = recentHistoryVisible,
            WhoCanSeeOnlineStatus = 0,
            WhoCanSeeActivity = 0,
            WhoCanSeeCurrentRoom = 0,
            WhoCanSendFriendRequests = 0,
            WhoCanInviteMe = 0,
            WhoCanMessageMe = 0,
            VoicePrivacy = 0,
            TextChatPrivacy = 0,
        });
    }

    private const string RecentHistoryVisibleKey = "privacy:recenthistoryvisible";

    private async Task<bool> GetRecentHistoryVisibleAsync(long accountId)
    {
        var row = await db.PlayerSettings
            .FirstOrDefaultAsync(s => s.PlayerId == accountId && s.Key == RecentHistoryVisibleKey);
        // Default: visible (true) when never toggled.
        return row is null || !string.Equals(row.Value, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>POST/PUT <c>/accountprivacysettings/recenthistoryvisibility</c>
    /// — the watch's <c>ChangeRecentHistoryVisibility(bool)</c>. Body carries
    /// <c>isRecentHistoryVisible</c> (accepts the PascalCase variant and a
    /// raw bool body too). The client's return type is the fire-and-forget
    /// response handle (body ignored), but we echo the persisted object so a
    /// GET-style consumer is also satisfied. The existing
    /// <c>/accountprivacysettings/{accountId:long}</c> route can never match
    /// this literal segment (the <c>:long</c> constraint rejects it), so
    /// without this handler the toggle 404'd ("Failed to modify …").</summary>
    [HttpPost("/accountprivacysettings/recenthistoryvisibility")]
    [HttpPut("/accountprivacysettings/recenthistoryvisibility")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    [Consumes("application/json", "application/x-www-form-urlencoded", "multipart/form-data")]
    public async Task<IActionResult> SetRecentHistoryVisibility()
    {
        var me = this.RequireCurrentPlayerId();
        var visible = await ReadRecentHistoryVisibleBodyAsync();
        await SetPlayerSettingAsync(me, RecentHistoryVisibleKey, visible ? "true" : "false");
        return Ok(new
        {
            AccountId = (int)me,
            IsRecentHistoryVisible = visible,
        });
    }

    private async Task<bool> ReadRecentHistoryVisibleBodyAsync()
    {
        // Form body (isRecentHistoryVisible / IsRecentHistoryVisible).
        if (Request.HasFormContentType)
        {
            foreach (var k in new[] { "isRecentHistoryVisible", "IsRecentHistoryVisible" })
                if (bool.TryParse(Request.Form[k], out var fv)) return fv;
        }
        // JSON body: { "isRecentHistoryVisible": true } or a bare `true`.
        try
        {
            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(Request.Body);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.True) return true;
            if (root.ValueKind == System.Text.Json.JsonValueKind.False) return false;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var k in new[] { "isRecentHistoryVisible", "IsRecentHistoryVisible" })
                    if (root.TryGetProperty(k, out var v) &&
                        (v.ValueKind == System.Text.Json.JsonValueKind.True ||
                         v.ValueKind == System.Text.Json.JsonValueKind.False))
                        return v.GetBoolean();
            }
        }
        catch { /* non-JSON / empty body */ }
        // Query fallback.
        if (bool.TryParse(Request.Query["isRecentHistoryVisible"], out var qv)) return qv;
        return true;
    }

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
