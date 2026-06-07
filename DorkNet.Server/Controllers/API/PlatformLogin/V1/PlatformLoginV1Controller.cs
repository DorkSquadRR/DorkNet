using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DorkNet.Models.Auth;
using DorkNet.Server.Auth;
using DorkNet.Server.Compat2018;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.API.PlatformLogin.V1;

/// <summary>
/// api.rec.net/api/platformlogin/v1/* — the JUNE-2018 login family.
///
/// The 2018 client (build 20180621_EA) logs in via a JSON body and parses the
/// PascalCase "login envelope" {Error, Player, Token, FirstLoginOfTheDay,
/// AnalyticsSessionId} (RecNet.cs:40014-40021). This is wholly different from
/// the 2019/2020 OAuth password-grant flow handled by the V2 controller, so it
/// lives in its own versioned controller. This branch targets the 2018 client;
/// the V2 controller still serves 2019/2020 builds.
///
/// Bodies are read leniently from JSON OR form OR query so JsonUtility,
/// LitJson, and form-encoded callers all bind. The single login Token IS the
/// JWT access token — the 2018 client re-attaches it as "Bearer {Token}" on
/// every authenticated call and refreshes it via GET v1/refreshlogin.
/// </summary>
[ApiController]
[Route("api/platformlogin/v1")]
public class PlatformLoginV1Controller(
    PlayerService playerService,
    AuthService authService,
    ServerSettingsService settings,
    SignupCodeService signupCodes,
    DorkNetDbContext db,
    ILogger<PlatformLoginV1Controller> logger) : ControllerBase
{
    // POST api/platformlogin/v1/loginaccount — primary sign-in.
    [HttpPost("loginaccount")]
    public async Task<IActionResult> LoginAccount()
    {
        var p = await ReadParamsAsync();
        var username = p.Get("Username");
        var password = p.Get("Password");
        var deviceId = p.Get("DeviceId");
        var platformId = p.Get("PlatformId");
        var platform = p.GetInt("Platform");

        PlayerEntity player;

        // Username + password: real credential check against PasswordHash.
        if (!string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(username))
        {
            var byName = await playerService.GetByUsernameAsync(username);
            if (byName?.PasswordHash is null ||
                !BCrypt.Net.BCrypt.Verify(password, byName.PasswordHash))
                return Ok(Fail("Incorrect username or password."));
            player = byName;
            await playerService.TagPlatformAsync(player.Id, platform, platformId);
        }
        else
        {
            // Device-id fallback (no password yet). Honour the signups-disabled
            // toggle: refuse before minting a brand-new account for an unknown
            // device, and stash it for /join code redemption.
            if (await settings.AreSignupsDisabledAsync()
                && await playerService.GetByDeviceAsync(deviceId) is null)
            {
                await signupCodes.RecordPendingDeviceAsync(
                    deviceId, platform, platformId, SignupCodeService.ClientIp(HttpContext));
                return Ok(Fail("Account creation is currently disabled by the server admin."));
            }
            player = await playerService.GetOrCreateByDeviceAsync(
                deviceId: deviceId, platform: platform, platformId: platformId,
                displayName: username);
        }

        return Ok(BuildEnvelope(player));
    }

    // POST api/platformlogin/v1/logincached — sign in to a remembered account
    // (the account-selection screen). Body carries PlayerId.
    [HttpPost("logincached")]
    public async Task<IActionResult> LoginCached()
    {
        var p = await ReadParamsAsync();
        var id = p.GetLong("PlayerId");
        if (id == 0) return Ok(Fail("Missing PlayerId."));
        var player = await playerService.GetByIdAsync(id);
        if (player is null) return Ok(Fail("That account no longer exists."));
        await playerService.TagPlatformAsync(player.Id, p.GetInt("Platform"), p.Get("PlatformId"));
        return Ok(BuildEnvelope(player));
    }

    // POST api/platformlogin/v1/createaccount — create AND sign in in one call.
    [HttpPost("createaccount")]
    public async Task<IActionResult> CreateAccount()
    {
        var p = await ReadParamsAsync();
        var deviceId = p.Get("DeviceId");
        var platformId = p.Get("PlatformId");
        var platform = p.GetInt("Platform");

        if (await settings.AreSignupsDisabledAsync()
            && await playerService.GetByDeviceAsync(deviceId) is null)
        {
            await signupCodes.RecordPendingDeviceAsync(
                deviceId, platform, platformId, SignupCodeService.ClientIp(HttpContext));
            return Ok(Fail("Account creation is currently disabled by the server admin."));
        }

        var player = await playerService.CreateNewAccountAsync(
            deviceId: deviceId, platform: platform, platformId: platformId,
            displayName: p.Get("Username"));

        // Attach birthday / email if the signup form supplied them.
        var birthdayTicks = p.GetLong("Birthday");
        var email = p.Get("Email");
        if (birthdayTicks != 0 || !string.IsNullOrEmpty(email))
        {
            var entity = await db.Players.FirstOrDefaultAsync(x => x.Id == player.Id);
            if (entity is not null)
            {
                if (birthdayTicks != 0)
                    entity.Birthday = new DateTime(birthdayTicks, DateTimeKind.Utc);
                if (!string.IsNullOrEmpty(email)) entity.Email = email;
                await db.SaveChangesAsync();
                player = entity;
            }
        }
        return Ok(BuildEnvelope(player, firstLogin: true));
    }

    // POST api/platformlogin/v1/registeraccount?Email=... — attach an email to
    // the authenticated account. Reads {Success, Message} (NOT "error").
    [HttpPost("registeraccount")]
    public async Task<IActionResult> RegisterAccount([FromQuery(Name = "Email")] string? email)
    {
        var pid = this.CurrentPlayerId() ?? 0;
        if (pid == 0) return Ok(new SuccessMessage2018 { Success = false, Message = "Not signed in." });
        var entity = await db.Players.FirstOrDefaultAsync(x => x.Id == pid);
        if (entity is null) return Ok(new SuccessMessage2018 { Success = false, Message = "Account not found." });
        if (!string.IsNullOrWhiteSpace(email)) { entity.Email = email; await db.SaveChangesAsync(); }
        return Ok(new SuccessMessage2018 { Success = true, Message = string.Empty });
    }

    // GET api/platformlogin/v1/refreshlogin — mint a fresh access token from the
    // current bearer. Response is {Token} (PascalCase). The 2018 client calls
    // Application.Quit() on an empty Token, so a 401 (no body) is the correct
    // "session ended" signal.
    [HttpGet("refreshlogin")]
    public async Task<IActionResult> RefreshLogin()
    {
        var auth = Request.Headers.Authorization.ToString();
        if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();
        var token = auth["Bearer ".Length..].Trim();
        if (authService.ValidateToken(token) is not long pid) return Unauthorized();
        var player = await playerService.GetByIdAsync(pid);
        if (player is null) return Unauthorized();
        var (access, _) = authService.GenerateTokenPair(player.Id);
        return Ok(new { Token = access });
    }

    // POST api/platformlogin/v1/getcachedlogins?Platform=&PlatformId= — the
    // account-selection list. Returns a BARE JSON ARRAY of Player2018 objects.
    [HttpPost("getcachedlogins")]
    [HttpGet("getcachedlogins")]
    public async Task<IActionResult> GetCachedLogins()
    {
        // The 2018 client sends Platform/PlatformId in the FORM BODY
        // (application/x-www-form-urlencoded), not the query string — so read
        // from form/query/json. Reading [FromQuery] only returned [] (empty
        // account grid + broke +profile auto-login). Verified from the wire:
        // req=Platform=0&PlatformId=76561198969189675.
        var p = await ReadParamsAsync();
        var players = await playerService.GetCachedLoginsAsync(p.GetInt("Platform"), p.Get("PlatformId"));
        return Ok(players.Select(ToPlayer2018).ToList());
    }

    // POST api/platformlogin/v1/removecachedlogin?Platform=&PlatformId= —
    // "forget this account". No persistent cache to clear; ack with 200.
    [HttpPost("removecachedlogin")]
    public IActionResult RemoveCachedLogin() => Ok(new SuccessMessage2018 { Success = true });

    // POST api/platformlogin/v1/logout?LoginLockToken=... — fire-and-forget.
    [HttpPost("logout")]
    public IActionResult Logout() => Ok(new SuccessMessage2018 { Success = true });

    // ── helpers ──────────────────────────────────────────────────────────────

    private Login2018Response BuildEnvelope(PlayerEntity player, bool firstLogin = false)
    {
        var (access, _) = authService.GenerateTokenPair(player.Id);
        // First-login-of-the-day is cosmetic (drives a daily XP bonus); derive
        // it from LastSeenAt where we still have the pre-update value.
        var first = firstLogin || player.LastSeenAt.Date < DateTime.UtcNow.Date;
        return new Login2018Response
        {
            Error = string.Empty,
            Player = ToPlayer2018(player),
            Token = access,
            FirstLoginOfTheDay = first,
            AnalyticsSessionId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
    }

    private static Login2018Response Fail(string message) => new() { Error = message };

    private static Player2018 ToPlayer2018(PlayerEntity p) => Player2018Mapper.From(p);

    /// <summary>Read request params from JSON body, form, and query — merged,
    /// case-insensitive — so JsonUtility/LitJson/form/query callers all bind.</summary>
    private async Task<ParamBag> ReadParamsAsync()
    {
        var bag = new ParamBag();
        foreach (var kv in Request.Query) bag.Set(kv.Key, kv.Value.ToString());
        if (Request.HasFormContentType)
        {
            try { foreach (var kv in Request.Form) bag.Set(kv.Key, kv.Value.ToString()); }
            catch { /* not a form */ }
        }
        else if (Request.ContentLength is > 0)
        {
            try
            {
                Request.EnableBuffering();
                Request.Body.Position = 0;
                using var doc = await JsonDocument.ParseAsync(Request.Body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        bag.Set(prop.Name, prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString(),
                            JsonValueKind.Number => prop.Value.GetRawText(),
                            JsonValueKind.True => "true",
                            JsonValueKind.False => "false",
                            _ => null,
                        });
                Request.Body.Position = 0;
            }
            catch { /* not JSON */ }
        }
        return bag;
    }

    private sealed class ParamBag
    {
        private readonly Dictionary<string, string?> _d = new(StringComparer.OrdinalIgnoreCase);
        public void Set(string k, string? v) { if (!string.IsNullOrEmpty(v)) _d[k] = v; }
        public string? Get(string k) => _d.TryGetValue(k, out var v) ? v : null;
        public int GetInt(string k) => int.TryParse(Get(k), out var v) ? v : 0;
        public long GetLong(string k) => long.TryParse(Get(k), out var v) ? v : 0;
    }
}
