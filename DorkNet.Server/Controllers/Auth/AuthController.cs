using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Auth;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Auth;

/// <summary>
/// auth.rec.net — OAuth-style token endpoint used by 2019+ builds alongside PlatformLogin.
/// </summary>
[ApiController]
public class AuthController(
    PlayerService playerService,
    AuthService authService,
    LevelService level,
    OrphanAccountTracker orphans,
    ServerSettingsService settings,
    DorkNet.Server.Data.DorkNetDbContext db,
    DomainConfig domain,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Award the per-day first-login XP if the calling
    /// player hasn't been seen yet today (UTC).</summary>
    private async Task TryAwardLoginXpAsync(long playerId)
    {
        var lastSeen = await db.Players
            .Where(p => p.Id == playerId)
            .Select(p => p.LastSeenAt)
            .FirstOrDefaultAsync();
        if (lastSeen.Date < DateTime.UtcNow.Date)
            await level.AwardXpAsync(playerId, LevelService.FirstLoginXp, "first_login_today");
    }

    private static Dictionary<string, object> BuildTokenResponse(string accessToken, string refreshToken)
    {
        var signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        return new()
        {
            ["access_token"] = accessToken,
            ["Access_token"] = accessToken,
            ["accessToken"] = accessToken,
            ["AccessToken"] = accessToken,
            ["token"] = accessToken,
            ["Token"] = accessToken,
            ["token_type"] = "Bearer",
            ["tokenType"] = "Bearer",
            ["TokenType"] = "Bearer",
            ["expires_in"] = 43200,
            ["expiresIn"] = 43200,
            ["ExpiresIn"] = 43200,
            ["refresh_token"] = refreshToken,
            ["Refresh_token"] = refreshToken,
            ["refreshToken"] = refreshToken,
            ["RefreshToken"] = refreshToken,
            ["key"] = signingKey,
            ["Key"] = signingKey,
            ["scope"] = "openid profile",
        };
    }

    private IActionResult TokenOk(string accessToken, string refreshToken)
    {
        var cookie = new CookieOptions
        {
            HttpOnly = true,
            Secure = string.Equals(domain.Scheme, "https", StringComparison.OrdinalIgnoreCase),
            SameSite = SameSiteMode.None,
            Expires = DateTimeOffset.UtcNow.AddHours(12),
            IsEssential = true,
        };
        if (domain.Apex.Contains('.'))
            cookie.Domain = "." + domain.Apex;
        Response.Cookies.Append(AuthService.AccessCookieName, accessToken, cookie);
        return Ok(BuildTokenResponse(accessToken, refreshToken));
    }

    // OAuth token endpoint. The 2020 client uses three grant types here:
    // - grant_type=cached_login → user clicked a remembered account on the
    //   account-selection screen. Body includes `account_id` (and platform
    //   creds). MUST issue a token for THAT specific account; otherwise the
    //   client receives a JWT for a different account and prompts the user
    //   to create a new one (because the JWT identity doesn't match what
    //   they picked).
    // - grant_type=refresh_token → re-issue access token from the existing
    //   refresh JWT.
    // - grant_type=password → first-time sign-in, resolve via deviceId
    //   (preferred) or fall back to a stable synthetic id derived from
    //   username so legacy callers don't share accounts.
    [HttpPost("/connect/token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token(
        [FromForm] string? grant_type,
        [FromForm] string? username,
        [FromForm] string? password,
        [FromForm] string? refresh_token,
        [FromForm] string? deviceId,
        [FromForm(Name = "device_id")] string? device_id_snake,
        [FromForm] int platform = 0,
        [FromForm] string? platformId = null,
        [FromForm(Name = "platform_id")] string? platform_id_snake = null,
        [FromForm] long? account_id = null)
    {
        // The client mixes camelCase and snake_case parameter naming
        // depending on grant type — accept both and merge.
        var effDeviceId = !string.IsNullOrWhiteSpace(deviceId) ? deviceId : device_id_snake;
        var effPlatformId = !string.IsNullOrWhiteSpace(platformId) ? platformId : platform_id_snake;

        if (string.Equals(grant_type, "cached_login", StringComparison.OrdinalIgnoreCase))
        {
            if (account_id is not long pickedId)
                return BadRequest(new { error = "invalid_request", error_description = "missing account_id" });
            var picked = await playerService.GetByIdAsync(pickedId);
            if (picked is null)
                return Unauthorized(new { error = "invalid_grant", error_description = "unknown account_id" });

            // Orphan cleanup: if the watch just created a brand-new
            // account on account/create as part of its boot probe, but
            // the user actually picked a different cached account here,
            // the just-created row is unused. Delete it so the DB
            // doesn't accumulate one orphan per game launch. When the
            // user is genuinely creating a new account, the picked id
            // matches the just-created id and we leave it.
            var pendingId = orphans.PeekPending(effDeviceId, effPlatformId);
            if (pendingId is long orphanId && orphanId != pickedId)
            {
                await playerService.DeleteOrphanAsync(orphanId);
                logger.LogInformation(
                    "[orphan-cleanup] cached_login picked {PickedId}; deleted just-created {OrphanId}",
                    pickedId, orphanId);
            }
            orphans.Clear(effDeviceId, effPlatformId);

            await TryAwardLoginXpAsync(picked.Id);
            await playerService.TagPlatformAsync(picked.Id, platform, effPlatformId);
            var (acc1, refr1) = authService.GenerateTokenPair(picked.Id);
            return TokenOk(acc1, refr1);
        }

        if (string.Equals(grant_type, "refresh_token", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(refresh_token))
                return BadRequest(new { error = "invalid_request" });
            var resolved = authService.ValidateToken(refresh_token);
            if (resolved is not long refreshedId)
                return Unauthorized(new { error = "invalid_grant" });
            var existing = await playerService.GetByIdAsync(refreshedId);
            if (existing is null)
                return Unauthorized(new { error = "invalid_grant" });
            var (acc, refr) = authService.GenerateTokenPair(existing.Id);
            return TokenOk(acc, refr);
        }

        // password grant: two paths.
        //   • Username + password supplied: real password verify against
        //     PlayerEntity.PasswordHash. Wrong password → 401.
        //   • No password: legacy device-id synth path for the 2020
        //     client's first-launch flow where the player hasn't set a
        //     password yet (HasPassword returns false in the watch UI
        //     and prompts them to create one). Resolves the existing
        //     account (or creates a fresh one) by deviceId.
        if (!string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(username))
        {
            var byName = await playerService.GetByUsernameAsync(username);
            if (byName is null || byName.PasswordHash is null)
                return Unauthorized(new
                {
                    error = "invalid_grant",
                    error_description = "unknown_user_or_no_password",
                });
            if (!BCrypt.Net.BCrypt.Verify(password, byName.PasswordHash))
                return Unauthorized(new
                {
                    error = "invalid_grant",
                    error_description = "password_mismatch",
                });
            // Tag the platform so the next boot's cached-login lookup
            // finds this account — without this the watch's "remembered
            // accounts" prompt would stay empty after a manual sign-in.
            await playerService.TagPlatformAsync(byName.Id, platform, effPlatformId);
            await TryAwardLoginXpAsync(byName.Id);
            var pair = authService.GenerateTokenPair(byName.Id);
            return TokenOk(pair.AccessToken, pair.RefreshToken);
        }

        // Device-id fallback path. Prefer the real deviceId; fall back
        // to a stable legacy synthesis from username so older callers
        // don't accidentally share an account.
        var effectiveDeviceId = !string.IsNullOrWhiteSpace(effDeviceId)
            ? effDeviceId
            : $"legacy-{(username ?? "anonymous").ToLowerInvariant()}";

        // Honour the admin "signups disabled" toggle. If the device
        // hasn't logged in before, GetOrCreateByDeviceAsync would mint
        // a fresh account here — same outcome as hitting
        // /account/create directly, which the toggle blocks. Refuse
        // BEFORE creation so the flag isn't bypassable.
        if (await settings.AreSignupsDisabledAsync())
        {
            var existing = await playerService.GetByDeviceAsync(effectiveDeviceId);
            if (existing is null)
            {
                logger.LogInformation(
                    "[auth] /connect/token device fallback refused — signups disabled (device={Device})",
                    effectiveDeviceId);
                return Unauthorized(new
                {
                    error = "signups_disabled",
                    error_description = "Account creation is currently disabled by the server admin.",
                });
            }
        }

        var player = await playerService.GetOrCreateByDeviceAsync(
            deviceId: effectiveDeviceId,
            platform: platform,
            platformId: effPlatformId,
            displayName: username);
        await TryAwardLoginXpAsync(player.Id);
        var (access, refresh) = authService.GenerateTokenPair(player.Id);

        return TokenOk(access, refresh);
    }

    [HttpGet("/connect/userinfo")]
    public async Task<IActionResult> UserInfo()
    {
        var auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();
        var token = auth["Bearer ".Length..].Trim();
        var id = authService.ValidateToken(token);
        if (id is not long playerId) return Unauthorized();
        var player = await playerService.GetByIdAsync(playerId);
        if (player is null) return Unauthorized();
        return Ok(new { sub = player.Id.ToString(), name = player.Username });
    }

    // Discovery document — issuer + endpoints derived from
    // DomainConfig so a single DORKNET_DOMAIN env var keeps the
    // OpenID document pointed at the live deployment.
    [HttpGet("/.well-known/openid-configuration")]
    public IActionResult OpenIdConfig()
    {
        var base_ = $"https://{domain.Sub("auth")}";
        return Ok(new
        {
            issuer = base_,
            token_endpoint = $"{base_}/connect/token",
            userinfo_endpoint = $"{base_}/connect/userinfo",
        });
    }

    // 2020.12 added an EAC challenge step in the login flow. The client GETs
    // a challenge string and passes it to EACManager.GenerateChallengeResponse,
    // which calls System.Convert.FromBase64CharPtr on the string. So the
    // returned challenge MUST be valid base64 — sending arbitrary text like
    // "dorknet-eac-disabled" throws FormatException client-side and kicks
    // the login. 24 chars of base64 'A's decodes to 18 zero bytes; matches
    // the rough size of a real EAC handshake nonce.
    [HttpGet("/eac/challenge")]
    [HttpPost("/eac/challenge")]
    public IActionResult EacChallenge()
        => Content("\"AAAAAAAAAAAAAAAAAAAAAAAA\"", "application/json");

    // Cached-logins lookup — boot-sequence call from ScreenAccountSelectionManager.
    // URL: GET auth.rec.net/cachedlogin/forplatformid/{platform}/{platformId}
    // Returns List<Login.CachedLogin>. Empty array = no remembered accounts.
    //
    // Real lookup: any account whose last platformlogin matched
    // (platform, platformId) shows up here, sorted most-recent-first. On a
    // single-Steam-emu setup that's one row; on a multi-account dev box
    // it's the full list the user has cycled through.
    [HttpGet("/cachedlogin/forplatformid/{platform:int}/{platformId}")]
    public async Task<IActionResult> CachedLoginsForPlatform(int platform, string platformId)
    {
        var players = await playerService.GetCachedLoginsAsync(platform, platformId);
        return Ok(players.Select(p => ToCachedLogin(p, platform, platformId)).ToList());
    }

    /// <summary>POST <c>cachedlogin/forplatformids</c> — bulk variant
    /// the watch uses when hydrating multiple platform-id mappings
    /// at once (e.g. populating a friend list from Steam ids). Body
    /// is form-urlencoded <c>platform=&amp;platformIds=a,b,c</c> or
    /// JSON with the same shape. Returns the flattened
    /// <c>List&lt;CachedLogin&gt;</c> across every matched id.</summary>
    [HttpPost("/cachedlogin/forplatformids")]
    [HttpGet("/cachedlogin/forplatformids")]
    public async Task<IActionResult> CachedLoginsForPlatformIds()
    {
        int platform = 0;
        string? rawIds = null;
        try
        {
            if (Request.HasFormContentType)
            {
                int.TryParse(Request.Form["platform"].ToString(), out platform);
                rawIds = Request.Form["platformIds"].ToString();
                if (string.IsNullOrWhiteSpace(rawIds))
                    rawIds = Request.Form["PlatformIds"].ToString();
            }
        }
        catch { /* not form */ }
        if (string.IsNullOrWhiteSpace(rawIds))
        {
            int.TryParse(Request.Query["platform"], out platform);
            rawIds = Request.Query["platformIds"].ToString();
            if (string.IsNullOrWhiteSpace(rawIds))
                rawIds = Request.Query["PlatformIds"].ToString();
        }
        if (string.IsNullOrWhiteSpace(rawIds)) return Ok(Array.Empty<object>());

        var ids = rawIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var results = new List<CachedLogin>();
        foreach (var pid in ids)
        {
            var players = await playerService.GetCachedLoginsAsync(platform, pid);
            foreach (var p in players)
                results.Add(ToCachedLogin(p, platform, pid));
        }
        return Ok(results);
    }

    [HttpGet("/cachedlogin/current")]
    public async Task<IActionResult> CurrentCachedLogin()
    {
        var auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();
        var token = auth["Bearer ".Length..].Trim();
        var id = authService.ValidateToken(token);
        if (id is not long playerId) return Unauthorized();
        var player = await playerService.GetByIdAsync(playerId);
        if (player is null) return Unauthorized();
        return Ok(ToCachedLogin(player, player.LastPlatform, player.LastPlatformId));
    }

    [HttpPost("/cachedlogin/migrate")]
    public async Task<IActionResult> MigrateCachedLogin(
        [FromForm] int platform = 0,
        [FromForm] string? platformId = null)
    {
        var auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();
        var token = auth["Bearer ".Length..].Trim();
        var id = authService.ValidateToken(token);
        if (id is not long playerId) return Unauthorized();
        await playerService.TagPlatformAsync(playerId, platform, platformId);
        var player = await playerService.GetByIdAsync(playerId);
        return player is null
            ? Unauthorized()
            : Ok(ToCachedLogin(player, platform, platformId ?? string.Empty));
    }

    [HttpPost("/connect/deviceauthorization")]
    [HttpGet("/connect/deviceauthorization")]
    public async Task<IActionResult> DeviceAuthorization()
    {
        var deviceCode = Guid.NewGuid().ToString("N");
        var userCode = deviceCode[..8].ToUpperInvariant();
        db.PlayerSettings.Add(new PlayerSettingEntity
        {
            PlayerId = RelationshipQueries.SystemAccountId,
            Key = $"deviceauthorization:{deviceCode}",
            Value = $"{userCode}|{DateTime.UtcNow.AddMinutes(15):O}",
        });
        await db.SaveChangesAsync();
        return Ok(new
        {
            device_code = deviceCode,
            user_code = userCode,
            verification_uri = $"https://{domain.Sub("auth")}/device",
            verification_uri_complete = $"https://{domain.Sub("auth")}/device?user_code={userCode}",
            expires_in = 900,
            interval = 5,
        });
    }

    private static CachedLogin ToCachedLogin(PlayerEntity p, int platform, string platformId) => new()
    {
        Platform = platform,
        PlatformId = platformId,
        AccountId = (int)p.Id,
        LastLoginTime = p.LastSeenAt,
        RequirePassword = false,
    };

}
