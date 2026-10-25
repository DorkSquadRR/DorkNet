using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using DorkNet.Server.Services;

namespace DorkNet.Server.Controllers.Site;

/// <summary>
/// Sign-in with the external OpenID Connect provider (<see cref="ExternalSignInOptions"/>)
/// for the website and the admin panel. The game client's login is separate.
///
/// <para>Flow: <c>login</c> challenges the provider; its callback
/// (<see cref="ExternalSignIn.CallbackPath"/>, handled by the OpenID Connect
/// middleware) stores the provider identity in the short-lived
/// <c>DorkNet.External</c> cookie and lands on <c>complete</c>. A linked identity
/// becomes a <c>DorkNet.Web</c> session at once; an unlinked one goes to the
/// website's <c>/link</c> page, where the player proves the account with its
/// password once (<c>account/link</c>).</para>
///
/// <para>Each host signs in on its own (website and admin panel), because the
/// hyphen host style puts them under different cookie domains.</para>
/// </summary>
[ApiController]
[AllowAnonymous]
public class ExternalSignInController(
    IOptions<ExternalSignInOptions> options,
    ExternalSignInService signIn,
    LinkAttemptLimiter limiter,
    DomainConfig domain,
    ILogger<ExternalSignInController> log) : ControllerBase
{
    private ExternalSignInOptions Options => options.Value;

    /// <summary>GET /api/auth/external/login?returnUrl=/path — start sign-in.</summary>
    [HttpGet(ExternalSignIn.LoginPath)]
    public IActionResult Login([FromQuery] string? returnUrl)
    {
        if (!Options.IsConfigured) return NotFound(new { error = "sign_in_not_configured" });
        var complete = $"{ExternalSignIn.CompletePath}?returnUrl={Uri.EscapeDataString(LocalUrl(returnUrl))}";
        return Challenge(new AuthenticationProperties { RedirectUri = complete }, ExternalSignIn.OidcScheme);
    }

    /// <summary>GET /api/auth/external/complete — after the provider's callback.</summary>
    [HttpGet(ExternalSignIn.CompletePath)]
    public async Task<IActionResult> Complete([FromQuery] string? returnUrl)
    {
        returnUrl = LocalUrl(returnUrl);
        var external = await HttpContext.AuthenticateAsync(ExternalSignIn.ExternalScheme);
        if (!external.Succeeded || ExternalIdentity(external.Principal) is not { } identity)
            return Redirect(WithQuery(returnUrl, "signin_error", "no_identity"));
        var (issuer, subject, email) = identity;

        var link = await signIn.FindAsync(issuer, subject);
        var player = link is null ? null : await signIn.ActivePlayerAsync(link);
        if (link is not null && player is not null)
        {
            await signIn.TouchAsync(link, email);
            await HttpContext.SignInAsync(ExternalSignIn.WebScheme, ExternalSignIn.PlayerPrincipal(player, link));
            await HttpContext.SignOutAsync(ExternalSignIn.ExternalScheme);
            log.LogInformation("[signin] player={Player} via {Issuer}", player.Id, issuer);
            return Redirect(returnUrl);
        }
        if (link is not null)
            return Redirect(WithQuery(returnUrl, "signin_error", "account_unavailable"));

        // Not linked yet. Linking happens on the website; the admin panel
        // tells its user to link there first.
        if (IsAdminHost())
            return Redirect(WithQuery(returnUrl, "signin_error", "not_linked"));
        return Redirect($"/link?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    /// <summary>GET /api/site/v1/account/me — who is signed in, or which
    /// provider identity is waiting to be linked.</summary>
    [HttpGet("api/site/v1/account/me")]
    public async Task<IActionResult> Me()
    {
        var web = await HttpContext.AuthenticateAsync(ExternalSignIn.WebScheme);
        var external = await HttpContext.AuthenticateAsync(ExternalSignIn.ExternalScheme);
        var pending = external.Succeeded ? ExternalIdentity(external.Principal) : null;
        return Ok(new
        {
            signInAvailable = Options.IsConfigured,
            provider = Options.DisplayName,
            signedIn = web.Succeeded,
            player = web.Succeeded
                ? new
                {
                    id = ExternalSignIn.PlayerId(web.Principal)?.ToString(),
                    username = web.Principal!.Identity?.Name,
                    email = web.Principal.FindFirst(ExternalSignIn.EmailClaim)?.Value,
                }
                : null,
            pendingLink = pending is { } p ? new { email = p.Email } : null,
        });
    }

    public sealed record LinkRequest(string? Username, string? Password);

    /// <summary>POST /api/site/v1/account/link {username, password} — link the
    /// pending provider identity to the account these credentials prove.</summary>
    [HttpPost("api/site/v1/account/link")]
    public async Task<IActionResult> Link([FromBody] LinkRequest body)
    {
        var external = await HttpContext.AuthenticateAsync(ExternalSignIn.ExternalScheme);
        if (!external.Succeeded || ExternalIdentity(external.Principal) is not { } identity)
            return Unauthorized(new { error = "no_pending_identity" });
        var (issuer, subject, email) = identity;
        var username = body.Username?.Trim() ?? string.Empty;
        if (username.Length == 0 || string.IsNullOrEmpty(body.Password))
            return BadRequest(new { error = "missing_credentials" });

        var keys = new[] { $"sub:{issuer}|{subject}", $"user:{username.ToLowerInvariant()}" };
        if (limiter.IsBlocked(keys))
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "too_many_attempts" });

        var (outcome, player, link) = await signIn.LinkAsync(issuer, subject, email, username, body.Password);
        switch (outcome)
        {
            case LinkOutcome.Linked:
                limiter.Reset(keys);
                await HttpContext.SignInAsync(ExternalSignIn.WebScheme, ExternalSignIn.PlayerPrincipal(player!, link!));
                await HttpContext.SignOutAsync(ExternalSignIn.ExternalScheme);
                log.LogInformation("[signin] linked player={Player} to {Issuer}", player!.Id, issuer);
                return Ok(new { linked = true, username = player.Username });
            case LinkOutcome.InvalidCredentials:
                limiter.RecordFailure(keys);
                return Unauthorized(new { error = "invalid_credentials" });
            case LinkOutcome.Banned:
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "banned" });
            default:
                return Conflict(new { error = "identity_linked_to_another_account" });
        }
    }

    /// <summary>POST /api/site/v1/account/unlink — remove this provider identity
    /// from the account and sign out.</summary>
    [HttpPost("api/site/v1/account/unlink")]
    public async Task<IActionResult> Unlink()
    {
        var web = await HttpContext.AuthenticateAsync(ExternalSignIn.WebScheme);
        if (!web.Succeeded || ExternalSignIn.PlayerId(web.Principal) is not { } playerId)
            return Unauthorized(new { error = "not_signed_in" });
        var issuer = web.Principal!.FindFirst(ExternalSignIn.IssuerClaim)?.Value ?? string.Empty;
        await signIn.UnlinkAsync(playerId, issuer);
        await HttpContext.SignOutAsync(ExternalSignIn.WebScheme);
        return Ok(new { unlinked = true });
    }

    /// <summary>POST /api/site/v1/account/logout — end the session here.</summary>
    [HttpPost("api/site/v1/account/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(ExternalSignIn.WebScheme);
        await HttpContext.SignOutAsync(ExternalSignIn.ExternalScheme);
        return Ok(new { signedOut = true });
    }

    /// <summary>(issuer, subject, email) from the provider's claims. The
    /// subject is the account key; the email is display only.</summary>
    private (string Issuer, string Subject, string Email)? ExternalIdentity(System.Security.Claims.ClaimsPrincipal? p)
    {
        var subject = p?.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(subject)) return null;
        var issuer = p!.FindFirst("iss")?.Value ?? Options.Authority;
        return (issuer.TrimEnd('/'), subject, p.FindFirst("email")?.Value ?? string.Empty);
    }

    private string LocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url) ? url : "/";

    private static string WithQuery(string url, string key, string value) =>
        url + (url.Contains('?') ? "&" : "?") + key + "=" + Uri.EscapeDataString(value);

    private bool IsAdminHost() =>
        string.Equals(Request.Host.Host, domain.Sub("admin"), StringComparison.OrdinalIgnoreCase);
}
