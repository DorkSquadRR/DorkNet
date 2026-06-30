using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Formatting.Compact;
using StackExchange.Redis;

namespace DorkNet.Server.Startup;

/// <summary>Central DI and host configuration for the DorkNet server.</summary>
public static class ServiceCollectionExtensions
{
    public static void AddDorkNetServices(this WebApplicationBuilder builder)
    {
        ConfigureLogging(builder);
        AddDatabase(builder);
        var domain = AddDomain(builder);
        AddCoreServices(builder.Services);
        var redisConn = AddRedis(builder);
        AddAuthentication(builder, domain);
        AddKestrelAndSignalR(builder, redisConn);
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
    }

    private static void ConfigureLogging(WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((ctx, services, cfg) =>
        {
            cfg.ReadFrom.Configuration(ctx.Configuration)
               .ReadFrom.Services(services)
               .Enrich.FromLogContext();

            if (ctx.HostingEnvironment.IsDevelopment())
                cfg.WriteTo.Console();
            else
                cfg.WriteTo.Console(new RenderedCompactJsonFormatter());
        });
    }

    private static void AddDatabase(WebApplicationBuilder builder)
    {
        var dbProvider = (builder.Configuration["Database:Provider"] ?? "sqlite").ToLowerInvariant();

        builder.Services.AddDbContext<DorkNetDbContext>(opt =>
        {
            opt.ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

            if (dbProvider == "postgres")
            {
                var conn = builder.Configuration.GetConnectionString("Default")
                    ?? throw new InvalidOperationException(
                        "Database:Provider=postgres but ConnectionStrings:Default is not set. " +
                        "Set the ConnectionStrings__Default env var or `ConnectionStrings:Default` " +
                        "in appsettings.Local.json.");
                opt.UseNpgsql(conn, npg => npg.MigrationsAssembly("DorkNet.Server"));
            }
            else
            {
                var dbPath = builder.Configuration["Database:SqlitePath"];
                if (string.IsNullOrWhiteSpace(dbPath))
                    dbPath = Path.Combine(AppContext.BaseDirectory, "data", "dorknet.db");
                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
                opt.UseSqlite($"Data Source={dbPath}", lite => lite.MigrationsAssembly("DorkNet.Server"));
            }
        });
    }

    private static DomainConfig AddDomain(WebApplicationBuilder builder)
    {
        var apex =
            builder.Configuration["Domain:Apex"]
            ?? Environment.GetEnvironmentVariable("DORKNET_DOMAIN")
            ?? "localhost";
        var domainScheme =
            builder.Configuration["Domain:Scheme"]
            ?? Environment.GetEnvironmentVariable("DORKNET_DOMAIN_SCHEME")
            ?? "https";
        var domainPort =
            builder.Configuration["Domain:Port"]
            ?? Environment.GetEnvironmentVariable("DORKNET_DOMAIN_PORT");
        var singleOriginBaseUrl =
            builder.Configuration["Domain:SingleOriginBaseUrl"]
            ?? Environment.GetEnvironmentVariable("DORKNET_SINGLE_ORIGIN_BASE_URL");

        var domain = new DomainConfig(apex, domainScheme, domainPort, singleOriginBaseUrl);
        builder.Services.AddSingleton(domain);
        builder.Services.Configure<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>(opt =>
        {
            opt.AllowedHosts = new[] { apex, $"*.{apex}", "localhost", "127.0.0.1" };
            opt.AllowEmptyHosts = true;
            opt.IncludeFailureMessage = true;
        });
        Console.WriteLine($"[domain] apex={apex}, scheme={domainScheme}, port={domainPort}, singleOrigin={singleOriginBaseUrl}, allowedHosts=[{apex}, *.{apex}, localhost]");
        return domain;
    }

    private static void AddCoreServices(IServiceCollection services)
    {
        services.AddScoped<PlayerService>();
        services.AddScoped<ConfigService>();
        services.AddScoped<AuthService>();
        services.AddScoped<RoomService>();
        services.AddScoped<LevelService>();
        services.AddScoped<StoreService>();
        services.AddSingleton<RoomDataBlobService>();
        services.AddSingleton<RoomBlobNormalizerService>();
        services.AddHttpClient();
        services.AddSingleton<HtrAssetMirrorService>();
        services.AddScoped<GameSessionService>();
        services.AddScoped<PrivateInstanceService>();
        services.AddScoped<CommunityBoardService>();
        services.AddScoped<ServerSettingsService>();
        services.AddScoped<SignupCodeService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<OrphanAccountTracker>();
        services.AddSingleton<PlayerPresenceService>();
        services.AddSingleton<OnlinePresenceService>();
        services.AddSingleton<PlayerLogService>();
        services.AddSingleton<IObjectStorage, ObjectStorageService>();
        services.AddSingleton<ImageSignatureService>();
    }

    private static string? AddRedis(WebApplicationBuilder builder)
    {
        var redisConn = builder.Configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redisConn)) return null;

        var seRedisConfig = NormalizeRedisConn(redisConn);
        var mux = ConnectionMultiplexer.Connect(seRedisConfig);
        builder.Services.AddSingleton<IConnectionMultiplexer>(mux);
        return redisConn;
    }

    internal static string NormalizeRedisConn(string raw)
    {
        if (!raw.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
            return raw;

        var uri = new Uri(raw);
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 6379;
        string? user = null, pass = null;
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            user = Uri.UnescapeDataString(parts[0]);
            if (parts.Length == 2) pass = Uri.UnescapeDataString(parts[1]);
        }
        var opts = new ConfigurationOptions
        {
            EndPoints = { { host, port } },
            AbortOnConnectFail = false,
            Ssl = raw.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase),
        };
        if (!string.IsNullOrEmpty(user)) opts.User = user;
        if (!string.IsNullOrEmpty(pass)) opts.Password = pass;
        return opts.ToString();
    }

    private static void AddAuthentication(WebApplicationBuilder builder, DomainConfig domain)
    {
        var signingKeyProvider = new IdentityServerSigningKeyProvider(builder.Configuration);
        builder.Services.AddSingleton(signingKeyProvider);
        builder.Services.AddRecRoomIdentityServer(builder.Configuration, domain, signingKeyProvider);

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opt =>
            {
                opt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = signingKeyProvider.ValidationKeys,
                    ValidateIssuer = true,
                    ValidIssuers = new[] { domain.AuthIssuer, AuthService.LegacyIssuer },
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero,
                };
                opt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var accessToken = ctx.Request.Query["access_token"];
                        var path = ctx.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub"))
                            ctx.Token = accessToken;
                        if (string.IsNullOrEmpty(ctx.Token) &&
                            ctx.Request.Cookies.TryGetValue(AuthService.AccessCookieName, out var cookieToken) &&
                            !string.IsNullOrWhiteSpace(cookieToken))
                        {
                            ctx.Token = cookieToken;
                        }
                        return Task.CompletedTask;
                    },
                };
            });

        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddJsonOptions(opt =>
            opt.JsonSerializerOptions.PropertyNamingPolicy = null);
    }

    private static void AddKestrelAndSignalR(WebApplicationBuilder builder, string? redisConn)
    {
        builder.WebHost.ConfigureKestrel(kopts =>
        {
            kopts.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
            kopts.Limits.MinRequestBodyDataRate = null;
            kopts.Limits.MinResponseDataRate = null;
        });

        var signalR = builder.Services.AddSignalR(opts =>
        {
            opts.KeepAliveInterval = TimeSpan.FromSeconds(10);
            opts.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
            opts.HandshakeTimeout = TimeSpan.FromSeconds(15);
            opts.MaximumReceiveMessageSize = 1024 * 1024;
            opts.EnableDetailedErrors = true;
        });

        if (!string.IsNullOrWhiteSpace(redisConn))
            signalR.AddStackExchangeRedis(NormalizeRedisConn(redisConn), opts =>
            {
                opts.Configuration.ChannelPrefix = RedisChannel.Literal("dorknet-signalr");
            });
    }
}
