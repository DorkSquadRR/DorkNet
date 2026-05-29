using Microsoft.AspNetCore.Mvc;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Auth;

/// <summary>
/// auth.localhost/photon/customauth — endpoint Photon Cloud calls during a
/// client connect when "Custom Authentication" is configured on the
/// Photon AppId.
///
/// Photon Cloud sends a GET (or POST) with the parameters the client
/// attached via <c>AuthenticationValues.AddAuthParameter</c>. The
/// DorkNet client mod (DorkNet.ClientMod) injects two:
/// <list type="bullet">
///   <item><c>userid</c> — the player's DorkNet account id (long).</item>
///   <item><c>LoginLock</c> — the per-session GUID stored in
///     <see cref="PlayerPresenceService"/> at /player/login time.</item>
/// </list>
///
/// Response shape (per
/// https://doc.photonengine.com/en-us/realtime/current/reference/custom-authentication):
/// <list type="bullet">
///   <item><c>ResultCode = 1</c> → success, allow client</item>
///   <item><c>ResultCode = 2</c> → parameter invalid, reject</item>
///   <item><c>ResultCode = 3</c> → auth failed, reject</item>
/// </list>
///
/// MODE: strict (2026-05-11). Rejects connects whose userid+LoginLock
/// don't match the most-recent /player/login. The "Reject if Auth
/// Failed" toggle in the Photon dashboard SHOULD also be on so a
/// rejected ResultCode actually closes the Photon socket — without
/// the toggle the rejection is informational only.
///
/// Fail-open on Redis errors: <see cref="PlayerPresenceService.ValidateLock"/>
/// returns true if Redis throws, so a transient infra blip doesn't
/// mass-kick every player off Photon.
/// </summary>
[ApiController]
public class PhotonCustomAuthController(
    PlayerPresenceService presence,
    IConfiguration config,
    ILogger<PhotonCustomAuthController> log) : ControllerBase
{
    [HttpGet("/photon/customauth")]
    [HttpPost("/photon/customauth")]
    public async Task<IActionResult> Authenticate()
    {
        // Photon forwards AuthGetParameters as querystring and
        // AuthPostData as form body. Read both so it works whichever
        // mode the dashboard picks.
        //
        // Multi-value handling: Photon's dashboard URL template can
        // contain literal placeholders like <c>?userid={userid}</c>,
        // and the watch ALSO ships AuthValues.AuthGetParameters
        // (which Photon appends to the URL). The two collide in the
        // querystring as <c>userid={userid}&userid=230343</c> — both
        // values arrive on the same key. ASP.NET Core surfaces this
        // as a multi-value <c>StringValues</c>; we keep the array so
        // <see cref="PickClean"/> below can pick the first value
        // that's actually a usable string (skipping unsubstituted
        // <c>{…}</c> placeholders).
        var qs = Request.Query
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string[]> form = new(StringComparer.OrdinalIgnoreCase);
        if (Request.HasFormContentType)
        {
            try
            {
                var f = await Request.ReadFormAsync();
                form = f.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.ToArray()!,
                    StringComparer.OrdinalIgnoreCase);
            }
            catch { /* best effort */ }
        }

        // Pick the first non-empty, non-placeholder value across
        // every alias for a parameter. A "placeholder" looks like
        // <c>{userid}</c> — Photon's dashboard URL template syntax
        // that Photon Cloud forgets to substitute when the same key
        // is also sent as an AuthGetParameter. Falling back to the
        // SECOND query value (the AuthGetParameter) gives us the
        // real id.
        static bool IsUsable(string? v)
            => !string.IsNullOrWhiteSpace(v)
               && !(v!.StartsWith('{') && v.EndsWith('}'));

        string? PickClean(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (qs.TryGetValue(k, out var arr))
                    foreach (var v in arr) if (IsUsable(v)) return v;
                if (form.TryGetValue(k, out var farr))
                    foreach (var v in farr) if (IsUsable(v)) return v;
            }
            return null;
        }

        var userIdRaw = PickClean("userid", "userId", "UserId");
        var loginLock = PickClean("LoginLock", "loginlock", "loginLock");

        if (!config.GetValue("Photon:RequireCustomAuth", false))
        {
            log.LogInformation(
                "[photon-auth] fail-open allow RequireCustomAuth=false userid={UserId} LoginLockPresent={Lock}",
                userIdRaw ?? "<anonymous>",
                !string.IsNullOrWhiteSpace(loginLock));
            return Ok(new
            {
                ResultCode = 1,
                Message = "ok (custom auth disabled)",
                UserId = userIdRaw ?? string.Empty,
            });
        }

        // Anonymous connect: pre-login Photon region pings + the watch's
        // initial NameServer probe both fire CustomAuth WITHOUT
        // AuthValues attached (the client mod only injects AuthValues
        // once /player/login has run and Core.LocalAccountId is set).
        // Returning ResultCode 1 here lets those bootstrap connects
        // succeed; the actual gameplay session has populated AuthValues
        // and goes through the strict path below.
        if (string.IsNullOrWhiteSpace(userIdRaw) && string.IsNullOrWhiteSpace(loginLock))
        {
            log.LogDebug("[photon-auth] anonymous bootstrap connect — allow");
            return Ok(new { ResultCode = 1, Message = "ok (anonymous)" });
        }

        // Half-set is suspicious — either someone's spoofing AuthValues
        // by hand, or the plugin's injector partially fired. Reject so
        // we don't grant gameplay credentials to an unverifiable session.
        if (string.IsNullOrWhiteSpace(userIdRaw) || string.IsNullOrWhiteSpace(loginLock))
        {
            log.LogWarning(
                "[photon-auth] partial auth — userid={UserId} LoginLockPresent={Lock} " +
                "query={Query} form={Form}",
                userIdRaw ?? "<missing>",
                !string.IsNullOrWhiteSpace(loginLock),
                System.Text.Json.JsonSerializer.Serialize(qs),
                System.Text.Json.JsonSerializer.Serialize(form));
            return Ok(new
            {
                ResultCode = 2,
                Message    = "userid and LoginLock must both be sent (or both absent for anonymous bootstrap)",
            });
        }

        if (!long.TryParse(userIdRaw, out var playerId))
        {
            log.LogWarning("[photon-auth] non-numeric userid={UserId}", userIdRaw);
            return Ok(new { ResultCode = 2, Message = $"userid '{userIdRaw}' is not a valid account id" });
        }

        if (!presence.ValidateLock(playerId, loginLock))
        {
            log.LogWarning(
                "[photon-auth] lock mismatch for player={PlayerId} — stale session, " +
                "different machine, or never logged in",
                playerId);
            return Ok(new
            {
                ResultCode = 3,
                Message    = "LoginLock mismatch — sign in via /player/login first",
            });
        }

        log.LogDebug("[photon-auth] OK player={PlayerId}", playerId);
        return Ok(new
        {
            ResultCode = 1,
            Message    = "ok",
            UserId     = playerId.ToString(),
        });
    }
}
