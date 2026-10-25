using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;

namespace DorkNet.Server.Services;

/// <summary>
/// External sign-in settings (<c>DorkNet:SignIn:*</c>): an OpenID Connect
/// provider players sign in with on the website and, for admins, the admin
/// panel. Any standard provider works (e.g. a Cloudflare Access SaaS app,
/// Google, Authentik). Unset, the feature is off and only the existing
/// username/password logins exist.
/// </summary>
public sealed class ExternalSignInOptions
{
    /// <summary>The provider's issuer URL; discovery is read from
    /// <c>{Authority}/.well-known/openid-configuration</c>.</summary>
    public string Authority { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The button label: "Sign in with {DisplayName}".</summary>
    public string DisplayName { get; set; } = "single sign-on";

    public string Scopes { get; set; } = "openid email profile";

    /// <summary>Also read the provider's userinfo endpoint after sign-in. Off by
    /// default: the ID token already carries <c>sub</c> and <c>email</c>, and
    /// Cloudflare Access answers userinfo without a Content-Type, which the
    /// OpenID Connect handler rejects.</summary>
    public bool UseUserInfoEndpoint { get; set; }

    public bool IsConfigured =>
        Authority.Length > 0 && ClientId.Length > 0 && ClientSecret.Length > 0;
}

/// <summary>Scheme, path and claim names shared by the sign-in pieces.</summary>
public static class ExternalSignIn
{
    /// <summary>The signed-in session (a linked DorkNet player), per host.</summary>
    public const string WebScheme = "DorkNet.Web";

    /// <summary>A provider identity that isn't linked to an account yet. Lives
    /// only until the player links it (or 15 minutes).</summary>
    public const string ExternalScheme = "DorkNet.External";

    public const string OidcScheme = "DorkNet.Oidc";

    // Everything lives under /api/: the website and admin hosts answer every
    // other GET with their SPA's index.html before authentication runs.
    public const string LoginPath = "/api/auth/external/login";
    public const string CallbackPath = "/api/auth/external/callback";
    public const string CompletePath = "/api/auth/external/complete";

    public const string PlayerIdClaim = "dorknet:player_id";
    public const string IssuerClaim = "dorknet:issuer";
    public const string SubjectClaim = "dorknet:subject";
    public const string EmailClaim = "dorknet:email";

    public static ClaimsPrincipal PlayerPrincipal(PlayerEntity player, ExternalLoginEntity link) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(PlayerIdClaim, player.Id.ToString()),
            new Claim(ClaimTypes.Name, player.Username),
            new Claim(IssuerClaim, link.Issuer),
            new Claim(SubjectClaim, link.Subject),
            new Claim(EmailClaim, link.Email),
        }, WebScheme, ClaimTypes.Name, ClaimTypes.Role));

    public static long? PlayerId(ClaimsPrincipal? principal) =>
        long.TryParse(principal?.FindFirst(PlayerIdClaim)?.Value, out var id) ? id : null;
}

public enum LinkOutcome { Linked, InvalidCredentials, Banned, AlreadyLinkedElsewhere }

/// <summary>Links provider identities to DorkNet accounts and resolves them
/// back. An identity is (issuer, subject); the email is only shown.</summary>
public sealed class ExternalSignInService(DorkNetDbContext db)
{
    public Task<ExternalLoginEntity?> FindAsync(string issuer, string subject) =>
        db.ExternalLogins.FirstOrDefaultAsync(l => l.Issuer == issuer && l.Subject == subject);

    /// <summary>The linked player, unless the link or the player is gone or the
    /// player is banned.</summary>
    public async Task<PlayerEntity?> ActivePlayerAsync(ExternalLoginEntity link)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Id == link.PlayerId);
        return player is null || IsBanned(player) ? null : player;
    }

    public async Task TouchAsync(ExternalLoginEntity link, string email)
    {
        link.LastUsedAt = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(email)) link.Email = email;
        await db.SaveChangesAsync();
    }

    /// <summary>Links (issuer, subject) to the account the credentials prove.
    /// A player has one identity per provider: linking a new one replaces the
    /// old. An identity already linked to a different account is refused.</summary>
    public async Task<(LinkOutcome Outcome, PlayerEntity? Player, ExternalLoginEntity? Link)> LinkAsync(
        string issuer, string subject, string email, string username, string password)
    {
        var player = await db.Players.FirstOrDefaultAsync(p => p.Username == username);
        if (player?.PasswordHash is null || !BCrypt.Net.BCrypt.Verify(password, player.PasswordHash))
            return (LinkOutcome.InvalidCredentials, null, null);
        if (IsBanned(player)) return (LinkOutcome.Banned, null, null);

        var existing = await FindAsync(issuer, subject);
        if (existing is not null && existing.PlayerId != player.Id)
            return (LinkOutcome.AlreadyLinkedElsewhere, null, null);

        if (existing is null)
        {
            db.ExternalLogins.RemoveRange(
                db.ExternalLogins.Where(l => l.PlayerId == player.Id && l.Issuer == issuer));
            existing = new ExternalLoginEntity { PlayerId = player.Id, Issuer = issuer, Subject = subject };
            db.ExternalLogins.Add(existing);
        }
        existing.Email = email;
        existing.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (LinkOutcome.Linked, player, existing);
    }

    public async Task<bool> UnlinkAsync(long playerId, string issuer) =>
        await db.ExternalLogins.Where(l => l.PlayerId == playerId && l.Issuer == issuer).ExecuteDeleteAsync() > 0;

    private static bool IsBanned(PlayerEntity p) => p.BannedUntil is { } until && until > DateTime.UtcNow;
}

/// <summary>Caps failed link attempts (a password guess each) per provider
/// identity and per username: 5 per 15 minutes each. In memory, per process,
/// which is enough to stop guessing through the link form.</summary>
public sealed class LinkAttemptLimiter
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<string, (int Count, DateTime Since)> _failures = new();

    public bool IsBlocked(params string[] keys) => keys.Any(k =>
        _failures.TryGetValue(k, out var f) && DateTime.UtcNow - f.Since < Window && f.Count >= MaxFailures);

    public void RecordFailure(params string[] keys)
    {
        var now = DateTime.UtcNow;
        foreach (var k in keys)
            _failures.AddOrUpdate(k, (1, now), (_, f) => now - f.Since >= Window ? (1, now) : (f.Count + 1, f.Since));
    }

    public void Reset(params string[] keys)
    {
        foreach (var k in keys) _failures.TryRemove(k, out _);
    }
}
