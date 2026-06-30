using Microsoft.AspNetCore.Mvc;
using DorkNet.Models.Auth;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Auth;

/// <summary>
/// auth.rec.net game-specific auth helpers. OAuth/OIDC endpoints are owned by Duende IdentityServer.
/// </summary>
[ApiController]
public class AuthController(
    PlayerService playerService,
    AuthService authService) : ControllerBase
{
    // 2020.12 added an EAC challenge step in the login flow. The client GETs
    // a challenge string and passes it to EACManager.GenerateChallengeResponse,
    // which calls System.Convert.FromBase64CharPtr on the string. So the
    // returned challenge MUST be valid base64 — sending arbitrary text throws
    // FormatException client-side and kicks the login.
    [HttpGet("/eac/challenge")]
    [HttpPost("/eac/challenge")]
    public IActionResult EacChallenge()
        => Content("\"AAAAAAAAAAAAAAAAAAAAAAAA\"", "application/json");

    // Cached-logins lookup — boot-sequence call from ScreenAccountSelectionManager.
    // URL: GET auth.rec.net/cachedlogin/forplatformid/{platform}/{platformId}
    // Returns List<Login.CachedLogin>. Empty array = no remembered accounts.
    [HttpGet("/cachedlogin/forplatformid/{platform:int}/{platformId}")]
    public async Task<IActionResult> CachedLoginsForPlatform(int platform, string platformId)
    {
        var players = await playerService.GetCachedLoginsAsync(platform, platformId);
        return Ok(players.Select(p => ToCachedLogin(p, platform, platformId)).ToList());
    }

    /// <summary>POST <c>cachedlogin/forplatformids</c> — bulk variant
    /// the watch uses when hydrating multiple platform-id mappings
    /// at once. Body is form-urlencoded <c>platform=&amp;platformIds=a,b,c</c>
    /// or JSON with the same shape.</summary>
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
        var token = GetBearerToken();
        if (token is null) return Unauthorized();
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
        var token = GetBearerToken();
        if (token is null) return Unauthorized();
        var id = authService.ValidateToken(token);
        if (id is not long playerId) return Unauthorized();
        await playerService.TagPlatformAsync(playerId, platform, platformId);
        var player = await playerService.GetByIdAsync(playerId);
        return player is null
            ? Unauthorized()
            : Ok(ToCachedLogin(player, platform, platformId ?? string.Empty));
    }

    private string? GetBearerToken()
    {
        var auth = Request.Headers.Authorization.ToString();
        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();
        return Request.Cookies.TryGetValue(AuthService.AccessCookieName, out var cookieToken)
            ? cookieToken
            : null;
    }

    private static CachedLogin ToCachedLogin(PlayerEntity p, int platform, string? platformId) => new()
    {
        Platform = platform,
        PlatformId = platformId ?? string.Empty,
        AccountId = (int)p.Id,
        LastLoginTime = p.LastSeenAt,
        RequirePassword = false,
    };
}
