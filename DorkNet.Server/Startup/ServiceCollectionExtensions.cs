using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using DorkNet.Contracts;
using DorkNet.Server.Compat;
using DorkNet.Server.Data;
using DorkNet.Server.Services;
using DorkNet.Server.Versions.Late2020;
using Serilog;
using Serilog.Formatting.Compact;
using StackExchange.Redis;
using System.Text;

namespace DorkNet.Server.Startup;

/// <summary>One-stop DI registration for the DorkNet host. Each
/// section is a self-contained private method so the top-level
/// <see cref="AddDorkNetServices"/> reads as a checklist of what the
/// server actually needs. Keep new registrations grouped by topic
/// rather than appended chronologically.</summary>
public static class ServiceCollectionExtensions
{
    public static void AddDorkNetServices(this WebApplicationBuilder builder)
    {
        ConfigureLogging(builder);
        AddDatabase(builder);
        AddDomain(builder);
        AddCoreServices(builder.Services);
        AddVersionCompatibility(builder);
        var redisConn = AddRedis(builder);
        AddAuthentication(builder);
        AddKestrelAndSignalR(builder, redisConn);
    }

    // ── Logging ───────────────────────────────────────────────────────────────
    // Production: structured JSON (one JSON object per line) so the log pipeline
    // can parse fields without regex. Development: human-readable text so the
    // local console stays scannable. JSON output is the same shape no matter the
    // environment — log keys like {Tag}, {Status}, {Method} stay queryable.
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

    // ── Database ──────────────────────────────────────────────────────────────
    // Provider switch — `Database:Provider` config key (or DATABASE__PROVIDER env
    // var) chooses sqlite (default for local dev) or postgres (production).
    private static void AddDatabase(WebApplicationBuilder builder)
    {
        var dbProvider = (builder.Configuration["Database:Provider"] ?? "sqlite").ToLowerInvariant();

        builder.Services.AddDbContext<DorkNetDbContext>(opt =>
        {
            // EF Core 9 throws on every connection if the model drifts from
            // the migrations snapshot. We track migrations + the snapshot
            // by hand for data-only fixes, so this warning is noise — Coolify
            // and local-dev databases still apply real migrations from disk.
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
                // Honor an explicit path the host can set via env var
                // (Database__SqlitePath) or appsettings (Database:SqlitePath).
                // The Windows launcher sets this to %APPDATA%\DorkNet\dorknet.db
                // so server installs don't accumulate state under <bin>\data\.
                // Existing Docker / standalone deploys that mounted <bin>\data\
                // keep working because that's still the fallback.
                var dbPath = builder.Configuration["Database:SqlitePath"];
                if (string.IsNullOrWhiteSpace(dbPath))
                    dbPath = Path.Combine(AppContext.BaseDirectory, "data", "dorknet.db");
                Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
                opt.UseSqlite($"Data Source={dbPath}", lite => lite.MigrationsAssembly("DorkNet.Server"));
            }
        });
    }

    // ── Domain ────────────────────────────────────────────────────────────────
    // Single source of truth for the deployment apex. Read once at startup
    // from DORKNET_DOMAIN (env-var) or Domain:Apex (config), defaulting to
    // localhost so existing deploys keep working without setting the env var.
    // Replaces per-controller [Host(...)] filters that hard-coded
    // rec.net/localhost pairs — the singleton DomainConfig is injected
    // anywhere code needs to build outbound URLs.
    private static void AddDomain(WebApplicationBuilder builder)
    {
        var apex =
            builder.Configuration["Domain:Apex"]
            ?? Environment.GetEnvironmentVariable("DORKNET_DOMAIN")
            ?? "localhost";
        var scheme = builder.Configuration["Domain:Scheme"] ?? "https";
        builder.Services.AddSingleton(new DomainConfig(apex, scheme));
        var allowedHosts = new[] { apex, "localhost", "127.0.0.1" }
            .Concat(DorkNetRouteOwnership.PublicSubdomains.Select(subdomain => $"{subdomain}.{apex}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        builder.Services.Configure<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>(opt =>
        {
            opt.AllowedHosts = allowedHosts;
            opt.AllowEmptyHosts = true;
            opt.IncludeFailureMessage = true;
        });
        Console.WriteLine($"[domain] apex={apex}, allowedHosts=[{string.Join(", ", allowedHosts)}]");
    }

    // ── Services ──────────────────────────────────────────────────────────────
    private static void AddCoreServices(IServiceCollection services)
    {
        services.AddScoped<PlayerService>();
        services.AddScoped<ConfigService>();
        services.AddScoped<AuthService>();
        services.AddScoped<RoomService>();
        services.AddScoped<PlaylistService>();
        services.AddScoped<ClubService>();
        services.AddScoped<LevelService>();
        services.AddScoped<StoreService>();
        services.AddScoped<SignupCodeService>();
        // Singleton — the protobuf blob is identical for every room and built
        // once at startup; no need to rebuild per request.
        services.AddSingleton<RoomDataBlobService>();
        services.AddSingleton<RoomBlobNormalizerService>();
        services.AddHttpClient();
        services.AddSingleton<HtrAssetMirrorService>();
        // Scoped (per-request) — these services hold DbContext references and
        // must share the request's scope to participate in the request's
        // transaction lifecycle. Pre-PR-3 they were Singleton with in-process
        // ConcurrentDictionary state; PR 3 moved their state to Postgres and
        // they now resolve from DI per-request like any other DbContext consumer.
        services.AddScoped<GameSessionService>();
        services.AddScoped<PrivateInstanceService>();
        services.AddScoped<CommunityBoardService>();
        services.AddScoped<ServerSettingsService>();
        // Singletons — these own connectionless state (Redis-backed or
        // process-local) and don't need a per-request scope.
        services.AddSingleton<NotificationService>();
        services.AddSingleton<OrphanAccountTracker>();
        services.AddSingleton<PlayerPresenceService>();
        services.AddSingleton<JoinTimeoutService>();
        services.AddSingleton<OnlinePresenceService>();
        services.AddSingleton<PlayerLogService>();
        // S3-compatible object storage (Garage in production, MinIO/disk in dev)
        // — holds profile images + room-blob bytes. Stateless wrapper around
        // the AWS SDK; safe as a singleton.
        services.AddSingleton<IObjectStorage, ObjectStorageService>();
        services.AddSingleton<ImageSignatureService>();
    }

    // ── Client-version compatibility ─────────────────────────────────────────
    // Each Rec Room build the server supports is one IVersionPlugin. Plugins
    // declare which version keys they handle (a single plugin can cover N keys
    // when several builds share the same wire format) and register any
    // generation-specific strategy services into DI.
    //
    // Add a new version family: drop a folder under Versions/, implement
    // IVersionPlugin, and register the singleton here. Add a new version to
    // an existing family (same wire shapes): just list the key in the
    // existing plugin's VersionKeys — no DI change needed.
    private static void AddVersionCompatibility(WebApplicationBuilder builder)
    {
        var pluginInstances = new IVersionPlugin[]
        {
            new Late2020VersionPlugin(),
            // Add March2020 / future generations here as their plugins land.
        };
        foreach (var p in pluginInstances) p.RegisterStrategies(builder.Services);
        foreach (var p in pluginInstances)
            builder.Services.AddSingleton<IVersionPlugin>(p);

        var supported = (builder.Configuration.GetSection("DorkNet:SupportedVersions").Get<string[]>()
                         ?? new[] { "december_2020_12_18" })
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultKey = builder.Configuration["DorkNet:DefaultClientVersion"]
                         ?? "december_2020_12_18";
        builder.Services.AddSingleton(sp => new VersionRegistry(
            sp.GetServices<IVersionPlugin>(),
            supported,
            string.IsNullOrWhiteSpace(defaultKey) ? null : defaultKey));
    }

    // ── Redis ─────────────────────────────────────────────────────────────────
    // IConnectionMultiplexer is registered ONLY when a connection string is
    // present. The Redis-backed services check for it via DI and fall back to
    // process-local state when null. Local dev can run without Redis; Coolify
    // production sets ConnectionStrings__Redis on the linked service.
    private static string? AddRedis(WebApplicationBuilder builder)
    {
        var redisConn = builder.Configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redisConn)) return null;
        var seRedisConfig = NormalizeRedisConn(redisConn);
        var mux = ConnectionMultiplexer.Connect(seRedisConfig);
        builder.Services.AddSingleton<IConnectionMultiplexer>(mux);
        return redisConn;
    }

    // Coolify hands you a URI like
    //     redis://default:PASSWORD@host:6379/0
    // but StackExchange.Redis.Connect(string) doesn't reliably handle the
    // user-info segment of a URI (the "default:PASSWORD@" part) — passing
    // the URI verbatim ends up with no password applied to the
    // ConfigurationOptions and Connect throws a misleading
    // "Error connecting right now". So we detect a URI prefix and translate
    // to SE.Redis's native config string before handing off.
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

    // ── JWT Auth ──────────────────────────────────────────────────────────────
    // Resolution order: env-var first (for production / containerised deploys),
    // then `Jwt:Secret` from any configuration source (appsettings.Local.json
    // is the same-machine convenience path the patcher script writes to).
    // Never commit a real secret to appsettings.json. DORKNET_JWT_SECRET is the
    // canonical env var name; RECNET_JWT_SECRET kept for backward compat with
    // older Coolify configs.
    private static void AddAuthentication(WebApplicationBuilder builder)
    {
        var jwtSecret = Environment.GetEnvironmentVariable("DORKNET_JWT_SECRET")
            ?? Environment.GetEnvironmentVariable("RECNET_JWT_SECRET")
            ?? builder.Configuration["Jwt:Secret"]
            ?? throw new InvalidOperationException(
                "JWT secret not configured. Set the DORKNET_JWT_SECRET env var or " +
                "add `Jwt:Secret` to appsettings.Local.json (the install-plugin.ps1 " +
                "script generates one automatically on same-machine setup).");

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opt =>
            {
                opt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
                    ValidateIssuer = true,
                    ValidIssuer = "https://api.rec.net/",
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero,
                };
                // SignalR over WebSocket: BestHTTP.SignalRCore (the 2020 watch's
                // hub client) negotiates with Authorization: Bearer, but the
                // WebSocket upgrade leg isn't guaranteed to keep the header
                // depending on the proxy in front of us. The hub client falls
                // back to ?access_token=<jwt> on the WS URL, which we extract
                // here for any /hub/* path. Without this, the watch's
                // RecNet.Notifications connect throws "Failed to connect to
                // RecNet Notifications" mid-login.
                opt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var accessToken = ctx.Request.Query["access_token"];
                        var path = ctx.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub"))
                            ctx.Token = accessToken;
                        return Task.CompletedTask;
                    },
                };
            });

        builder.Services.AddAuthorization();
        // Use C# property names verbatim — [JsonPropertyName] attributes on each
        // model give the per-key casing the client's LitJson Util.GetKey calls
        // expect (a mix of lowercase, camelCase and snake_case; verified by
        // disassembling each Deserialize body). Don't apply a global naming policy
        // or it will override the explicit per-key attributes.
        builder.Services.AddControllers().AddJsonOptions(opt =>
            opt.JsonSerializerOptions.PropertyNamingPolicy = null);
    }

    // ── Kestrel + SignalR ────────────────────────────────────────────────────
    // Kestrel-level limits: defaults trim long-lived sockets earlier than our
    // SignalR ping interval allows.
    //   * KeepAliveTimeout (default 130s) applies to upgraded WebSocket
    //     connections in some hosting paths; bump to 10m so it never beats
    //     SignalR's own ping cadence.
    //   * MinRequestBodyDataRate / MinResponseDataRate enforce a minimum
    //     bytes/sec on streaming bodies; for chunked-upload room imports
    //     that's been the cause of mysterious abort-mid-write errors on
    //     slow links. Disabling them is safer than risking false-positive
    //     "client too slow" closures.
    //
    // SignalR — drives notify.{apex}/hub/v1, consumed client-side by
    // BestHTTP.SignalRCore.HubConnection. The framework handles the
    // /negotiate POST automatically. When Redis is configured, attach
    // the StackExchange.Redis backplane so groups (player:N) fan out
    // across replicas.
    private static void AddKestrelAndSignalR(WebApplicationBuilder builder, string? redisConn)
    {
        builder.WebHost.ConfigureKestrel(kopts =>
        {
            kopts.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
            kopts.Limits.MinRequestBodyDataRate = null;
            kopts.Limits.MinResponseDataRate = null;
        });

        // Keepalive cadence: server pings every 10s, gives the client 60s
        // to ack before declaring the connection dead. The 10s ping forces
        // frame traffic well below every proxy timeout we've seen in the
        // path (Cloudflare edge ~100s, cloudflared 600s, Coolify/Traefik
        // 180s, Kestrel 130s).
        var signalR = builder.Services.AddSignalR(opts =>
        {
            opts.KeepAliveInterval = TimeSpan.FromSeconds(10);
            opts.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
            opts.HandshakeTimeout = TimeSpan.FromSeconds(15);
            // Allow large gift / community-board payloads (default 32 KB
            // truncates anything bigger and disconnects the client).
            opts.MaximumReceiveMessageSize = 1024 * 1024;
            opts.EnableDetailedErrors = true;
        });
        if (!string.IsNullOrWhiteSpace(redisConn))
            signalR.AddStackExchangeRedis(NormalizeRedisConn(redisConn), opts =>
            {
                opts.Configuration.ChannelPrefix = RedisChannel.Literal("dorknet-signalr");
            });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
    }
}
