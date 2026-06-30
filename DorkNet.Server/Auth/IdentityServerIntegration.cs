using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Duende.IdentityServer.Validation;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;
using DorkNet.Server.Data;
using DorkNet.Server.Data.Entities;
using DorkNet.Server.Services;

namespace DorkNet.Server.Auth;

public sealed class IdentityServerSigningKeyProvider
{
    private readonly string _legacySecret;

    public IdentityServerSigningKeyProvider(IConfiguration config)
    {
        _legacySecret =
            Environment.GetEnvironmentVariable("DORKNET_JWT_SECRET")
            ?? Environment.GetEnvironmentVariable("RECNET_JWT_SECRET")
            ?? config["Jwt:Secret"]
            ?? throw new InvalidOperationException(
                "JWT secret not configured. Set DORKNET_JWT_SECRET env var or Jwt:Secret in appsettings.Local.json.");

        CertificatePath = ResolveCertificatePath(config);
        var password = config["IdentityServer:SigningCertificatePassword"] ?? _legacySecret;
        var certificateBase64 = config["IdentityServer:SigningCertificateBase64"];
        if (!string.IsNullOrWhiteSpace(certificateBase64))
        {
            LoadedFromBase64 = true;
            Certificate = LoadCertificateFromBase64(certificateBase64, password);
        }
        else
        {
            Certificate = LoadOrCreateCertificate(CertificatePath, password);
        }
        SigningKey = new X509SecurityKey(Certificate);
        LegacyJwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_legacySecret));
    }

    public string CertificatePath { get; }
    public bool LoadedFromBase64 { get; }
    public string CertificateSource => LoadedFromBase64
        ? "IdentityServer:SigningCertificateBase64"
        : CertificatePath;
    public X509Certificate2 Certificate { get; }
    public SecurityKey SigningKey { get; }
    public SecurityKey LegacyJwtKey { get; }
    public SecurityKey[] ValidationKeys => new[] { SigningKey, LegacyJwtKey };

    private static string ResolveCertificatePath(IConfiguration config)
    {
        var path = config["IdentityServer:SigningCertificatePath"];
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(AppContext.BaseDirectory, "data", "identityserver-signing.pfx");
        return path;
    }

    private static X509Certificate2 LoadCertificateFromBase64(string certificateBase64, string password)
    {
        var flags = X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable;
        var compact = new string(certificateBase64.Where(c => !char.IsWhiteSpace(c)).ToArray());
        var bytes = Convert.FromBase64String(compact);
        return new X509Certificate2(bytes, password, flags);
    }

    private static X509Certificate2 LoadOrCreateCertificate(string path, string password)
    {
        var flags = X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (File.Exists(path))
            return new X509Certificate2(path, password, flags);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=DorkNet IdentityServer Signing",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));

        using var created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));
        var bytes = created.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(path, bytes);
        return new X509Certificate2(bytes, password, flags);
    }
}

public static class RecRoomIdentityServerRegistration
{
    public static void AddRecRoomIdentityServer(
        this IServiceCollection services,
        IConfiguration config,
        DomainConfig domain,
        IdentityServerSigningKeyProvider signingKeyProvider)
    {
        services
            .AddIdentityServer(options =>
            {
                options.IssuerUri = domain.AuthIssuer.TrimEnd('/');
                options.KeyManagement.Enabled = false;
                var licenseKey =
                    Environment.GetEnvironmentVariable("DUENDE_IDENTITYSERVER_LICENSE_KEY")
                    ?? config["IdentityServer:LicenseKey"];
                if (!string.IsNullOrWhiteSpace(licenseKey))
                    options.LicenseKey = licenseKey;
            })
            .AddInMemoryIdentityResources(RecRoomIdentityServerConfig.IdentityResources)
            .AddInMemoryApiScopes(RecRoomIdentityServerConfig.ApiScopes)
            .AddInMemoryClients(RecRoomIdentityServerConfig.Clients)
            .AddSigningCredential(signingKeyProvider.SigningKey, SecurityAlgorithms.RsaSha256)
            .AddResourceOwnerValidator<RecRoomPasswordValidator>()
            .AddExtensionGrantValidator<CachedLoginGrantValidator>()
            .AddProfileService<RecRoomProfileService>();
    }
}

internal static class RecRoomIdentityServerConfig
{
    public const string ClientId = "recroom";
    public const string ClientSecret = "VxZ53kgbbEaRoZAeMe00MagtgD12GLL2";
    public const string GameClientScope = "gameClient";
    public const string CachedLoginGrantType = "cached_login";

    public static readonly IdentityResource[] IdentityResources =
    {
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
    };

    public static readonly ApiScope[] ApiScopes =
    {
        new(GameClientScope, "Game client")
        {
            UserClaims = RecRoomIdentityClaims.UserClaimTypes,
        },
        new("api", "DorkNet API")
        {
            UserClaims = RecRoomIdentityClaims.UserClaimTypes,
        },
    };

    public static readonly Client[] Clients =
    {
        new()
        {
            ClientId = ClientId,
            ClientName = "Rec Room game client",
            RequireClientSecret = true,
            ClientSecrets = { new Secret(ClientSecret.Sha256()) },
            AllowedGrantTypes = { GrantType.ResourceOwnerPassword, GrantType.RefreshToken, CachedLoginGrantType },
            AllowedScopes = RecRoomScopes(),
            AllowOfflineAccess = true,
            AccessTokenLifetime = 60 * 60 * 12,
            AbsoluteRefreshTokenLifetime = 60 * 60 * 24 * 30,
            SlidingRefreshTokenLifetime = 60 * 60 * 24 * 30,
            RefreshTokenUsage = TokenUsage.ReUse,
            RefreshTokenExpiration = TokenExpiration.Absolute,
            UpdateAccessTokenClaimsOnRefresh = true,
            AlwaysIncludeUserClaimsInIdToken = true,
        },
    };

    private static ICollection<string> RecRoomScopes() =>
    [
        IdentityServerConstants.StandardScopes.OpenId,
        IdentityServerConstants.StandardScopes.Profile,
        IdentityServerConstants.StandardScopes.OfflineAccess,
        GameClientScope,
        "api",
    ];
}

public sealed class IdentityServerGameTokenRequestMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsTokenRequest(context))
        {
            await next(context);
            return;
        }

        var form = await context.Request.ReadFormAsync();
        var values = form.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value,
            StringComparer.OrdinalIgnoreCase);
        var changed = false;

        if (IsDevicePasswordGrant(values))
        {
            if (!values.ContainsKey("username") || string.IsNullOrWhiteSpace(values["username"].ToString()))
            {
                values["username"] = First(
                    values.TryGetValue("deviceId", out var deviceId) ? deviceId.ToString() : null,
                    values.TryGetValue("device_id", out var deviceIdSnake) ? deviceIdSnake.ToString() : null,
                    values.TryGetValue("platformId", out var platformId) ? platformId.ToString() : null,
                    values.TryGetValue("platform_id", out var platformIdSnake) ? platformIdSnake.ToString() : null,
                    "anonymous");
                changed = true;
            }

            if (!values.ContainsKey("password") || string.IsNullOrWhiteSpace(values["password"].ToString()))
            {
                values["password"] = "__dorknet_device_login__";
                values["dorknet_device_login"] = "true";
                changed = true;
            }
        }

        var scopes = values.TryGetValue("scope", out var rawScope)
            ? rawScope.ToString()
            : string.Empty;
        var mergedScopes = MergeScopes(scopes);
        if (!string.Equals(scopes, mergedScopes, StringComparison.Ordinal))
        {
            values["scope"] = mergedScopes;
            changed = true;
        }

        if (changed)
            context.Features.Set<IFormFeature>(new FormFeature(new FormCollection(values)));

        await next(context);
    }

    private static bool IsTokenRequest(HttpContext context) =>
        HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Equals("/connect/token", StringComparison.OrdinalIgnoreCase)
        && context.Request.HasFormContentType;

    private static bool IsDevicePasswordGrant(Dictionary<string, StringValues> values)
    {
        if (!values.TryGetValue("grant_type", out var grantType) ||
            !string.Equals(grantType.ToString(), GrantType.ResourceOwnerPassword, StringComparison.OrdinalIgnoreCase))
            return false;
        return !values.TryGetValue("password", out var password) ||
               string.IsNullOrWhiteSpace(password.ToString());
    }

    private static string First(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "anonymous";

    private static string MergeScopes(string existing)
    {
        var scopes = existing
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        scopes.Add(IdentityServerConstants.StandardScopes.OpenId);
        scopes.Add(IdentityServerConstants.StandardScopes.Profile);
        scopes.Add(IdentityServerConstants.StandardScopes.OfflineAccess);
        scopes.Add(RecRoomIdentityServerConfig.GameClientScope);
        return string.Join(' ', scopes);
    }
}

public sealed class RecRoomPasswordValidator(
    PlayerService playerService,
    LevelService level,
    ServerSettingsService settings,
    DorkNetDbContext db,
    ILogger<RecRoomPasswordValidator> logger) : IResourceOwnerPasswordValidator
{
    public async Task ValidateAsync(ResourceOwnerPasswordValidationContext context)
    {
        var raw = context.Request.Raw;
        var deviceId = First(raw.Get("deviceId"), raw.Get("device_id"));
        var platformId = First(raw.Get("platformId"), raw.Get("platform_id"));
        var platform = int.TryParse(raw.Get("platform"), out var p) ? p : 0;
        var deviceLogin = string.Equals(raw.Get("dorknet_device_login"), "true", StringComparison.OrdinalIgnoreCase);

        if (!deviceLogin && !string.IsNullOrEmpty(context.Password) && !string.IsNullOrEmpty(context.UserName))
        {
            var byName = await playerService.GetByUsernameAsync(context.UserName);
            if (byName is null || byName.PasswordHash is null)
            {
                context.Result = new GrantValidationResult(
                    TokenRequestErrors.InvalidGrant,
                    "unknown_user_or_no_password");
                return;
            }

            if (!BCrypt.Net.BCrypt.Verify(context.Password, byName.PasswordHash))
            {
                context.Result = new GrantValidationResult(
                    TokenRequestErrors.InvalidGrant,
                    "password_mismatch");
                return;
            }

            await playerService.TagPlatformAsync(byName.Id, platform, platformId);
            await TryAwardLoginXpAsync(byName.Id);
            context.Result = await CreateSuccessAsync(byName, "password");
            return;
        }

        var effectiveDeviceId = !string.IsNullOrWhiteSpace(deviceId)
            ? deviceId
            : $"legacy-{(context.UserName ?? "anonymous").ToLowerInvariant()}";

        if (await settings.AreSignupsDisabledAsync())
        {
            var existing = await playerService.GetByDeviceAsync(effectiveDeviceId);
            if (existing is null)
            {
                logger.LogInformation(
                    "[auth] IdentityServer password grant device fallback refused - signups disabled (device={Device})",
                    effectiveDeviceId);
                context.Result = new GrantValidationResult(
                    TokenRequestErrors.InvalidGrant,
                    "signups_disabled");
                return;
            }
        }

        var player = await playerService.GetOrCreateByDeviceAsync(
            deviceId: effectiveDeviceId,
            platform: platform,
            platformId: platformId,
            displayName: context.UserName);
        await TryAwardLoginXpAsync(player.Id);
        context.Result = await CreateSuccessAsync(player, "device_password");
    }

    private async Task<GrantValidationResult> CreateSuccessAsync(PlayerEntity player, string authMethod) =>
        new(
            subject: player.Id.ToString(),
            authenticationMethod: authMethod,
            claims: await RecRoomIdentityClaims.CreateAsync(db, player.Id, player.Username));

    private async Task TryAwardLoginXpAsync(long playerId)
    {
        var lastSeen = await db.Players
            .Where(p => p.Id == playerId)
            .Select(p => p.LastSeenAt)
            .FirstOrDefaultAsync();
        if (lastSeen.Date < DateTime.UtcNow.Date)
            await level.AwardXpAsync(playerId, LevelService.FirstLoginXp, "first_login_today");
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public sealed class CachedLoginGrantValidator(
    PlayerService playerService,
    LevelService level,
    OrphanAccountTracker orphans,
    DorkNetDbContext db,
    ILogger<CachedLoginGrantValidator> logger) : IExtensionGrantValidator
{
    public string GrantType => RecRoomIdentityServerConfig.CachedLoginGrantType;

    public async Task ValidateAsync(ExtensionGrantValidationContext context)
    {
        var raw = context.Request.Raw;
        if (!long.TryParse(raw.Get("account_id"), out var pickedId))
        {
            context.Result = new GrantValidationResult(
                TokenRequestErrors.InvalidGrant,
                "missing account_id");
            return;
        }

        var picked = await playerService.GetByIdAsync(pickedId);
        if (picked is null)
        {
            context.Result = new GrantValidationResult(
                TokenRequestErrors.InvalidGrant,
                "unknown account_id");
            return;
        }

        var deviceId = First(raw.Get("deviceId"), raw.Get("device_id"));
        var platformId = First(raw.Get("platformId"), raw.Get("platform_id"));
        var platform = int.TryParse(raw.Get("platform"), out var p) ? p : 0;

        var pendingId = orphans.PeekPending(deviceId, platformId);
        if (pendingId is long orphanId && orphanId != pickedId)
        {
            await playerService.DeleteOrphanAsync(orphanId);
            logger.LogInformation(
                "[orphan-cleanup] cached_login picked {PickedId}; deleted just-created {OrphanId}",
                pickedId,
                orphanId);
        }
        orphans.Clear(deviceId, platformId);

        await TryAwardLoginXpAsync(picked.Id);
        await playerService.TagPlatformAsync(picked.Id, platform, platformId);
        context.Result = new GrantValidationResult(
            subject: picked.Id.ToString(),
            authenticationMethod: GrantType,
            claims: await RecRoomIdentityClaims.CreateAsync(db, picked.Id, picked.Username));
    }

    private async Task TryAwardLoginXpAsync(long playerId)
    {
        var lastSeen = await db.Players
            .Where(p => p.Id == playerId)
            .Select(p => p.LastSeenAt)
            .FirstOrDefaultAsync();
        if (lastSeen.Date < DateTime.UtcNow.Date)
            await level.AwardXpAsync(playerId, LevelService.FirstLoginXp, "first_login_today");
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}

public sealed class RecRoomProfileService(PlayerService playerService, DorkNetDbContext db) : IProfileService
{
    public async Task GetProfileDataAsync(ProfileDataRequestContext context)
    {
        if (!TryGetSubjectId(context.Subject, out var playerId))
            return;

        var player = await playerService.GetByIdAsync(playerId);
        if (player is null)
            return;

        context.IssuedClaims.AddRange(
            await RecRoomIdentityClaims.CreateAsync(db, player.Id, player.Username));
    }

    public async Task IsActiveAsync(IsActiveContext context)
    {
        context.IsActive =
            TryGetSubjectId(context.Subject, out var playerId)
            && await playerService.GetByIdAsync(playerId) is not null;
    }

    private static bool TryGetSubjectId(ClaimsPrincipal subject, out long playerId)
    {
        var value = subject.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? subject.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(value, out playerId);
    }
}

public static class RecRoomIdentityClaims
{
    public static readonly List<string> UserClaimTypes =
    [
        JwtRegisteredClaimNames.Sub,
        ClaimTypes.NameIdentifier,
        "name",
        "role",
        ClaimTypes.Role,
        "roles",
        "accountId",
        "account_id",
        "accountid",
    ];

    public static async Task<List<Claim>> CreateAsync(DorkNetDbContext db, long playerId, string? username = null)
    {
        var playerInfo = await db.Players
            .Where(p => p.Id == playerId)
            .Select(p => new { p.Username, IsDev = p.IsDeveloper || p.IsAdmin })
            .FirstOrDefaultAsync();
        var name = username ?? playerInfo?.Username ?? playerId.ToString();
        var isDev = playerInfo?.IsDev ?? false;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, playerId.ToString()),
            new(JwtRegisteredClaimNames.Sub, playerId.ToString()),
            new("name", name),
            new("accountId", playerId.ToString()),
            new("account_id", playerId.ToString()),
            new("accountid", playerId.ToString()),
            new(ClaimTypes.Role, "gameClient"),
            new("role", "gameClient"),
            new("roles", "gameClient"),
        };

        if (isDev)
        {
            claims.Add(new Claim(ClaimTypes.Role, "developer"));
            claims.Add(new Claim("role", "developer"));
            claims.Add(new Claim("roles", "developer"));
        }

        return claims;
    }
}
