using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DorkNet.Server.Data;

namespace DorkNet.Server.Services;

public class AuthService(IConfiguration config, DorkNetDbContext db)
{
    public const string AccessCookieName = "dorknet_access_token";

    // Resolution order MUST match Program.cs's JwtBearer validator setup —
    // env var first, config key second — otherwise tokens are signed with
    // one secret and validated against another and every authenticated
    // request 401s. In production we set DORKNET_JWT_SECRET in Coolify
    // (RECNET_JWT_SECRET kept as fallback for backward compat with older
    // configs) and leave Jwt:Secret as the appsettings.json placeholder;
    // the mismatched-secret bug shows up only there.
    private string SecretKey =>
        Environment.GetEnvironmentVariable("DORKNET_JWT_SECRET")
        ?? Environment.GetEnvironmentVariable("RECNET_JWT_SECRET")
        ?? config["Jwt:Secret"]
        ?? throw new InvalidOperationException(
            "JWT secret not configured. Set DORKNET_JWT_SECRET env var or " +
            "Jwt:Secret in appsettings.Local.json.");

    public string GenerateToken(long playerId) => CreateJwt(playerId, TimeSpan.FromHours(12), "access");

    /// <summary>
    /// Issues an (access, refresh) pair for the OAuth-style password-grant
    /// response shape (Login.LoginResponse). Access tokens are short-lived;
    /// the refresh token is what api/platformlogin/refresh accepts later.
    /// </summary>
    public (string AccessToken, string RefreshToken) GenerateTokenPair(long playerId) =>
        (CreateJwt(playerId, TimeSpan.FromHours(12), "access"),
         CreateJwt(playerId, TimeSpan.FromDays(30), "refresh"));

    private string CreateJwt(long playerId, TimeSpan lifetime, string tokenType)
    {
        var handler = new JwtSecurityTokenHandler();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));

        // Look up dev/admin flags so the JWT carries a "developer" role
        // claim for accounts that should see the in-game debug button.
        // The watch's Login.SetAccessToken parses the access_token's
        // <c>role</c> claim — either a single string or a list — and
        // calls <c>HasRole("developer", token)</c>; that result drives
        // <c>Accounts.SetLocalAccountIsDev</c> and ultimately
        // <c>SessionManager.IsDeveloper</c>, which the watch UI checks
        // when deciding whether to show the dev console toggle.
        var isDev = db.Players
            .Where(p => p.Id == playerId)
            .Select(p => (bool?)(p.IsDeveloper || p.IsAdmin))
            .FirstOrDefault() ?? false;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, playerId.ToString()),
            new(JwtRegisteredClaimNames.Sub, playerId.ToString()),
            new("accountId", playerId.ToString()),
            new("account_id", playerId.ToString()),
            new("accountid", playerId.ToString()),
            new(ClaimTypes.Role, "gameClient"),
            new("roles", "gameClient"),
            new("token_type", tokenType),
        };
        // Multiple Role claims become a JSON array on the wire — the
        // watch's HasRole helper handles both string and List<string>
        // forms.
        if (isDev)
        {
            claims.Add(new Claim(ClaimTypes.Role, "developer"));
            claims.Add(new Claim("roles", "developer"));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.Add(lifetime),
            Issuer = "https://api.rec.net/",
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        return handler.WriteToken(handler.CreateJwtSecurityToken(descriptor));
    }

    public long? ValidateToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey));

            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = "https://api.rec.net/",
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero,
            }, out _);

            var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return idClaim is not null ? long.Parse(idClaim) : null;
        }
        catch
        {
            return null;
        }
    }
}
