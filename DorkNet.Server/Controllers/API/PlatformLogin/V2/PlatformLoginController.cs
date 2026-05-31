using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Auth;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.PlatformLogin.V2;

/// <summary>
/// api.rec.net/api/platformlogin/* — OAuth-style password-grant login.
/// Login.LOGIN_API in dump.cs (line 578249) is "api/platformlogin/" — the
/// version suffix (v2 / v5) is appended by callers. The 2020 build uses v5.
///
/// Request body is application/x-www-form-urlencoded (HTTPUrlEncodedForm in
/// Login.LoginHelper, dump.cs:578319), NOT JSON. Form fields are bound
/// case-insensitively by ASP.NET Core, so the parameter names don't have to
/// match the on-the-wire casing exactly.
///
/// Response is a Login.LoginResponse: { Error, ErrorDescription,
/// AccessToken, RefreshToken } — JSON keys must be PascalCase (the
/// Util.GetKey calls inside Login.LoginResponse.Deserialize pass PascalCase
/// string literals; verified via global-metadata.dat scan).
/// </summary>
[ApiController]
[Route("api/platformlogin")]
public class PlatformLoginController(
    PlayerService playerService,
    AuthService authService,
    ServerSettingsService settings,
    SignupCodeService signupCodes,
    DorkNetDbContext db) : ControllerBase
{
    // Login.LoginToAccount → POST api/platformlogin/v2 / v5
    // Form fields the 2020 client sends are not yet fully confirmed; common
    // OAuth-2 password-grant + Rec Room platform fields:
    //   grant_type, username, password, platform, platformId, deviceId,
    //   buildTimestamp, version. Bind permissively and ignore extras.
    [HttpPost("v2")]
    [HttpPost("v5")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromForm(Name = "username")] string? Username,
        [FromForm(Name = "Name")] string? Name,
        [FromForm(Name = "platform")] int Platform = 0,
        [FromForm(Name = "platformId")] string? PlatformId = null,
        [FromForm(Name = "deviceId")] string? DeviceId = null,
        [FromForm(Name = "buildTimestamp")] long BuildTimestamp = 0,
        [FromForm(Name = "version")] string? Version = null)
    {
        var displayName = Username ?? Name ?? PlatformId ?? "Player";

        // Admin "signups disabled" gate. Mirrors the same guard on
        // /connect/token's device-id fallback in AuthController — if
        // the device has no existing account, GetOrCreate would mint a
        // new one here, bypassing the toggle. Refuse pre-creation.
        if (await settings.AreSignupsDisabledAsync()
            && await playerService.GetByDeviceAsync(DeviceId) is null)
        {
            // Stash the refused device so the player can claim it on the
            // /join page (matched by IP) with a signup code.
            await signupCodes.RecordPendingDeviceAsync(
                DeviceId, Platform, PlatformId, SignupCodeService.ClientIp(HttpContext));
            return Unauthorized(new LoginResponse
            {
                Error = "signups_disabled",
                ErrorDescription = "Account creation is currently disabled by the server admin.",
            });
        }

        // Account identity is keyed off deviceId. The service handles the
        // null/empty case by synthesising a stable id from platformId or a
        // GUID — either way, no two unrelated callers ever share an account.
        var player = await playerService.GetOrCreateByDeviceAsync(
            deviceId: DeviceId,
            platform: Platform,
            platformId: PlatformId,
            displayName: displayName);
        var (access, refresh) = authService.GenerateTokenPair(player.Id);

        return Ok(new LoginResponse
        {
            AccessToken = access,
            RefreshToken = refresh,
        });
    }

    // Login.DownloadCachedLogins → GET api/platformlogin/cached
    // Returns List<Login.CachedLogin>. Empty array means no remembered accounts.
    //
    // The 2020 client doesn't include a platformId in the path here, but
    // BestHTTP appends current-session credentials as query params on these
    // bootstrap calls — so we accept platform/platformId via query or form
    // and reuse the same lookup as auth.rec.net/cachedlogin/forplatformid.
    [HttpGet("cached")]
    [HttpGet("cachedlogins")]
    [HttpGet("savedlogins")]
    [HttpGet("v1/cached")]
    [HttpGet("v2/cached")]
    public async Task<IActionResult> GetCachedLogins(
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

    // Login.RemoveCachedLogin → DELETE api/platformlogin/cached
    [HttpDelete("cached")]
    [HttpDelete("cachedlogins")]
    public IActionResult RemoveCachedLogin() => Ok(new { });

    // Login.RefreshLogin → POST api/platformlogin/refresh
    // Response is Login.RefreshLoginResponse { Token } — single field, the
    // new access token. (Note: dump.cs Login.RefreshLoginResponse has only
    // "Token", not "AccessToken".)
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        // Refresh requires a valid bearer (access OR refresh JWT); both are
        // signed with the same secret so ValidateToken accepts either.
        // Refusing to "GetOrCreateAsync(null)" here means a stale install
        // can't quietly mint a brand-new account when its refresh token is
        // gone — it has to go through the cached-login flow instead.
        var auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();
        var token = auth["Bearer ".Length..].Trim();
        var resolved = authService.ValidateToken(token);
        if (resolved is not long playerId)
            return Unauthorized();
        var player = await playerService.GetByIdAsync(playerId);
        if (player is null) return Unauthorized();
        var (access, _) = authService.GenerateTokenPair(player.Id);
        return Ok(new RefreshLoginResponse { Token = access });
    }

    // Login.LoginToCachedAccount(int accountId) → POST api/platformlogin/cached/{accountId}
    // Returns the same Login.LoginResponse shape as the regular login.
    [HttpPost("cached/{accountId:long}")]
    public async Task<IActionResult> LoginToCachedAccount(
        long accountId,
        [FromForm] int platform = 0,
        [FromForm(Name = "platformId")] string? platformId = null,
        [FromForm(Name = "platform_id")] string? platformIdSnake = null)
    {
        // No GetOrCreateAsync(null) fallback — if the cached id doesn't
        // resolve, the cache is stale (another DB instance, deleted account,
        // etc.) and we'd rather force the client back to the create-account
        // flow than silently mint a new player.
        var player = await playerService.GetByIdAsync(accountId);
        if (player is null) return Unauthorized();
        await playerService.TagPlatformAsync(player.Id, platform, platformId ?? platformIdSnake);
        var (access, refresh) = authService.GenerateTokenPair(player.Id);
        return Ok(new LoginResponse
        {
            AccessToken = access,
            RefreshToken = refresh,
        });
    }

    // Login.CheckHasPassword → GET api/platformlogin/haspassword
    // Client uses ExpectPrimitiveResponse<bool> → Convert.ChangeType
    // on the raw response, so we MUST emit a bare boolean literal.
    // Real password state lives on PlayerEntity.PasswordHash;
    // AuthAccountController on auth.* does the same lookup against the
    // authenticated player. We mirror it here so the api.* host also
    // returns the truthful value instead of an always-true stub.
    [HttpGet("haspassword")]
    public async Task<IActionResult> HasPassword()
    {
        var pid = this.CurrentPlayerId() ?? 0;
        if (pid == 0) return Content("false", "application/json");
        var player = await playerService.GetByIdAsync(pid);
        var has = player?.PasswordHash is { Length: > 0 };
        return Content(has ? "true" : "false", "application/json");
    }

    // Login.CreatePassword / ChangePassword / ResetPassword on auth.*
    // already do the real bcrypt write — they're wired in
    // AuthAccountController. The api.* path duplicates the route
    // without persisting; rather than mirror the full logic here,
    // delegate to AuthService.ChangePassword for consistency.
    [HttpPost("password")]
    [HttpPut("password")]
    public async Task<IActionResult> SetPassword([FromForm(Name = "newPassword")] string? newPassword,
                                                 [FromForm(Name = "password")] string? legacy)
    {
        var pid = this.CurrentPlayerId() ?? 0;
        if (pid == 0) return Unauthorized();
        var pw = newPassword ?? legacy ?? string.Empty;
        if (pw.Length < 6) return Ok(new RecNetResult { Success = false, Error = "password_too_short" });
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == pid);
        if (player is null) return NotFound();
        player.PasswordHash = BCrypt.Net.BCrypt.HashPassword(pw, workFactor: 11);
        await db.SaveChangesAsync();
        return Ok(new RecNetResult { Success = true, Error = string.Empty });
    }

    [HttpPost("resetpassword")]
    public IActionResult ResetPassword()
    {
        // Real password reset would email a token; on a private
        // server with no SMTP this is an explicit no-op (the admin
        // SPA "change password" tool is the supported path).
        return Ok(new RecNetResult { Success = false, Error = "reset_not_supported" });
    }
}
