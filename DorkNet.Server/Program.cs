using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using DorkNet.Server.Auth;
using DorkNet.Server.Data;
using DorkNet.Server.Services;
using Serilog;
using Serilog.Formatting.Compact;
using StackExchange.Redis;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Personal/local-machine override layer. Loaded last so it wins over both
// appsettings.json and appsettings.{Environment}.json. Gitignored — use
// this for Photon AppId, JWT secret, and anything you don't want committed.
builder.Configuration.AddJsonFile("appsettings.Local.json",
    optional: true, reloadOnChange: true);

// ── Logging ──────────────────────────────────────────────────────────────────
// Production: structured JSON (one JSON object per line) so Coolify's log
// pipeline / future Loki ingest can parse fields without regex.
// Development: human-readable text so the local console stays scannable.
// The JSON output is the same shape no matter the environment, just
// rendered differently — log keys like {Tag}, {Status}, {Method} stay
// queryable when we ship to a structured log store later.
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

// ── Database ──────────────────────────────────────────────────────────────────
// Provider switch — `Database:Provider` config key (or DATABASE__PROVIDER env var)
// chooses sqlite (default for local dev) or postgres (production). Each provider
// gets its own migrations assembly path so we can ship both schemas without
// EF Core complaining about provider-specific column types in the snapshot.
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
        // ConnectionStrings:Default is the standard ASP.NET Core key; env var
        // form is ConnectionStrings__Default. Coolify sets this when you link
        // the managed Postgres service.
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

// ── Domain ────────────────────────────────────────────────────────────────────
// Single source of truth for the deployment apex. Read once at startup
// from DORKNET_DOMAIN (env-var) or Domain:Apex (config), defaulting to
// localhost so existing deploys keep working without setting the env var.
// Replaces the per-controller [Host(...)] filters that hard-coded
// rec.net/localhost pairs — the singleton DomainConfig is injected
// anywhere code needs to build outbound URLs (img.{apex}/...,
// cdn.{apex}/..., notify.{apex}/...). The HostFilteringMiddleware
// allowed-hosts list is derived from {apex} + *.{apex} so every
// subdomain under the configured apex reaches the controllers; the
// [Host] attributes previously acted as both routing keys AND host
// filters, but now they're gone and HostFiltering does the latter alone.
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
var domainConfig = new DomainConfig(apex, domainScheme, domainPort, singleOriginBaseUrl);
builder.Services.AddSingleton(domainConfig);
builder.Services.Configure<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>(opt =>
{
    opt.AllowedHosts = new[] { apex, $"*.{apex}", "localhost", "127.0.0.1" };
    opt.AllowEmptyHosts = true;
    opt.IncludeFailureMessage = true;
});
Console.WriteLine($"[domain] apex={apex}, scheme={domainScheme}, port={domainPort}, singleOrigin={singleOriginBaseUrl}, allowedHosts=[{apex}, *.{apex}, localhost]");

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<ConfigService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<RoomService>();
builder.Services.AddScoped<LevelService>();
builder.Services.AddScoped<StoreService>();
// Singleton — the protobuf blob is identical for every room and built
// once at startup; no need to rebuild per request.
builder.Services.AddSingleton<RoomDataBlobService>();
builder.Services.AddSingleton<RoomBlobNormalizerService>();
// IHttpClientFactory + the .htr asset mirror that the admin Import
// Room endpoint kicks off as fire-and-forget after a successful upload.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<HtrAssetMirrorService>();
// Scoped (per-request) — these services hold DbContext references and
// must share the request's scope to participate in the request's
// transaction lifecycle. Pre-PR-3 they were Singleton with in-process
// ConcurrentDictionary state; PR 3 moved their state to Postgres and
// they now resolve from DI per-request like any other DbContext consumer.
builder.Services.AddScoped<GameSessionService>();
builder.Services.AddScoped<PrivateInstanceService>();
builder.Services.AddScoped<CommunityBoardService>();
builder.Services.AddScoped<ServerSettingsService>();
builder.Services.AddScoped<SignupCodeService>();
// Singletons — these own connectionless state (Redis-backed or
// process-local) and don't need a per-request scope.
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<OrphanAccountTracker>();
builder.Services.AddSingleton<PlayerPresenceService>();
builder.Services.AddSingleton<OnlinePresenceService>();
// Per-player rolling request log used by the admin UI's "Player logs"
// tab. Singleton because it's stateless beyond Redis + an in-process
// fallback queue; no DbContext involved.
builder.Services.AddSingleton<PlayerLogService>();
// S3-compatible object storage (Garage in production, MinIO/disk in dev)
// — holds profile images + room-blob bytes. Stateless wrapper around
// the AWS SDK; safe as a singleton.
builder.Services.AddSingleton<IObjectStorage, ObjectStorageService>();
builder.Services.AddSingleton<ImageSignatureService>();

// ── Redis ─────────────────────────────────────────────────────────────────────
// IConnectionMultiplexer is registered ONLY when a connection string is
// present. The Redis-backed services check for it via DI and fall back to
// process-local state when null. Local dev can run without Redis; Coolify
// production sets ConnectionStrings__Redis on the linked service.
//
// Coolify hands you a URI like
//     redis://default:PASSWORD@host:6379/0
// but StackExchange.Redis.Connect(string) doesn't reliably handle the
// user-info segment of a URI (the "default:PASSWORD@" part) — passing
// the URI verbatim ends up with no password applied to the
// ConfigurationOptions and Connect throws a misleading
// "Error connecting right now". So we detect a URI prefix and translate
// to SE.Redis's native config string ("host:port,password=...,user=...,
// abortConnect=false") before handing off.
var redisConn = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConn))
{
    var seRedisConfig = NormalizeRedisConn(redisConn);
    var mux = ConnectionMultiplexer.Connect(seRedisConfig);
    builder.Services.AddSingleton<IConnectionMultiplexer>(mux);
}

static string NormalizeRedisConn(string raw)
{
    if (!raw.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) &&
        !raw.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
        return raw; // already SE.Redis native config string
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
    var opts = new StackExchange.Redis.ConfigurationOptions
    {
        EndPoints = { { host, port } },
        AbortOnConnectFail = false,
        Ssl = raw.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase),
    };
    if (!string.IsNullOrEmpty(user)) opts.User = user;
    if (!string.IsNullOrEmpty(pass)) opts.Password = pass;
    return opts.ToString();
}

// ── JWT Auth ──────────────────────────────────────────────────────────────────
// Resolution order: env-var first (for production / containerised deploys),
// then `Jwt:Secret` from any configuration source (appsettings.Local.json
// is the same-machine convenience path the patcher script writes to).
// Never commit a real secret to appsettings.json — the gitignored
// appsettings.Local.json or the env-var are the only legitimate sources.
// DORKNET_JWT_SECRET is the canonical env var name; RECNET_JWT_SECRET
// kept for backward compat with older Coolify configs (will warn at
// startup, can be removed once everyone has migrated). AuthService
// has the matching fallback so signing + validation use the same key.
var signingKeyProvider = new IdentityServerSigningKeyProvider(builder.Configuration);
builder.Services.AddSingleton(signingKeyProvider);
builder.Services.AddRecRoomIdentityServer(builder.Configuration, domainConfig, signingKeyProvider);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeyProvider.ValidationKeys,
            ValidateIssuer = true,
            ValidIssuers = new[] { domainConfig.AuthIssuer, AuthService.LegacyIssuer },
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero,
        };
        // SignalR over WebSocket: BestHTTP.SignalRCore (the 2020 watch's
        // hub client) negotiates with Authorization: Bearer, but the
        // WebSocket upgrade leg isn't guaranteed to keep the header
        // depending on the proxy in front of us — Cloudflare Tunnel
        // passes through, but Coolify's Caddy / browsers don't. The
        // hub client falls back to ?access_token=<jwt> on the WS URL,
        // which we extract here for any /hub/* path so the connection
        // authenticates the same way negotiate did. Without this, the
        // watch's RecNet.Notifications connect throws "Failed to
        // connect to RecNet Notifications" mid-login.
        opt.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
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
// Use C# property names verbatim — [JsonPropertyName] attributes on each
// model give the per-key casing the client's LitJson Util.GetKey calls
// expect (a mix of lowercase, camelCase and snake_case; verified by
// disassembling each Deserialize body). Don't apply a global naming policy
// or it will override the explicit per-key attributes.
builder.Services.AddControllers().AddJsonOptions(opt =>
    opt.JsonSerializerOptions.PropertyNamingPolicy = null);

// Kestrel-level limits — the defaults trim long-lived sockets earlier
// than our SignalR ping interval allows. Specifically:
//   * KeepAliveTimeout (HTTP keep-alive between requests, default 130s)
//     applies to upgraded WebSocket connections in some hosting paths;
//     bump to 10m so it never beats SignalR's own ping cadence.
//   * RequestHeadersTimeout (default 30s) is for the initial upgrade
//     handshake only — leave it.
//   * MinRequestBodyDataRate / MinResponseDataRate enforce a minimum
//     bytes/sec on streaming bodies; for chunked-upload room imports
//     that's been the cause of mysterious abort-mid-write errors on
//     slow links. Disabling them is safer than risking false-positive
//     "client too slow" closures.
builder.WebHost.ConfigureKestrel(kopts =>
{
    kopts.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(10);
    kopts.Limits.MinRequestBodyDataRate = null;
    kopts.Limits.MinResponseDataRate = null;
});

// SignalR — drives notify.rec.net/hub/v1, consumed client-side by
// BestHTTP.SignalRCore.HubConnection. The framework handles the
// /negotiate POST automatically. When Redis is configured, attach
// the StackExchange.Redis backplane so groups (player:N) fan out
// across replicas — without it, player A connected to replica 1
// would never receive a notification published from replica 2.
//
// Keepalive cadence: server pings every 10s, gives the client 60s
// to ack before declaring the connection dead. The 10s ping forces
// frame traffic well below every proxy timeout we've seen in the
// path (Cloudflare edge ~100s, cloudflared 600s, Coolify/Traefik
// 180s, Kestrel 130s). 60s client timeout is double the ping
// interval so a single dropped ping doesn't kill the connection.
var signalR = builder.Services.AddSignalR(opts =>
{
    opts.KeepAliveInterval = TimeSpan.FromSeconds(10);
    opts.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    opts.HandshakeTimeout = TimeSpan.FromSeconds(15);
    // Allow large gift / community-board payloads (default 32 KB
    // truncates anything bigger and disconnects the client).
    opts.MaximumReceiveMessageSize = 1024 * 1024;
    // Surface transport errors so we can tell whether a disconnect
    // was clean (Traefik idleTimeout) vs framing-level (cloudflared
    // chunked-encoding mismatch).
    opts.EnableDetailedErrors = true;
});
if (!string.IsNullOrWhiteSpace(redisConn))
    signalR.AddStackExchangeRedis(NormalizeRedisConn(redisConn), opts =>
    {
        // Channel prefix keeps the SignalR backplane's pub/sub channels
        // namespaced from any other Redis tenants in the same instance.
        opts.Configuration.ChannelPrefix = RedisChannel.Literal("dorknet-signalr");
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// DomainConfig is the single source of truth for the deployment apex
// (DORKNET_DOMAIN). Resolve it once at startup; every downstream
// host-mount / redirect / hub binding below uses this instead of
// hardcoded rec.net/localhost literals.
var domainCfg = app.Services.GetRequiredService<DomainConfig>();

// Easy Launcher single-origin mode. localtunnel gives us one public
// hostname, so the launcher name-server emits URLs like
// https://abc.loca.lt/__dn/api/...; this middleware maps those path
// prefixes back into the same internal host layout used by advanced
// wildcard deployments. If Domain:SingleOriginBaseUrl is unset this is
// inert, preserving the normal subdomain behavior.
app.Use(async (ctx, next) =>
{
    if (domainCfg.SingleOriginEnabled &&
        TryMapSingleOriginPath(ctx.Request.Path, out var service, out var rest))
    {
        ctx.Request.Host = new HostString(service == "www" ? domainCfg.Apex : domainCfg.Sub(service));
        ctx.Request.Path = string.IsNullOrEmpty(rest) ? "/" : rest;
    }

    await next();
});

// Keep endpoint selection after the single-origin path rewrite. Without an
// explicit routing point here, WebApplication can auto-insert routing before
// the rewrite middleware, causing /__dn/api/... requests to stay route misses
// even though the trace logs show the rewritten /api/... path.
app.UseRouting();

// Request/response tracing — emits one structured log per non-health request,
// with the response body included for any 4xx/5xx so we can see exactly what
// the game is asking for. This runs early enough to catch route misses,
// auth failures, storage/CDN host traffic, and controller responses.
var reqLogger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("DorkNet.Server.HttpTrace");
var playerLog = app.Services.GetRequiredService<DorkNet.Server.Services.PlayerLogService>();
app.Use(async (ctx, next) =>
{
    if (IsHealthProbe(ctx.Request))
    {
        await next();
        return;
    }

    // WebSocket upgrades take over the underlying connection — wrapping the
    // response body in a MemoryStream confuses the upgrade handshake and
    // prevents SignalR's WebSocket transport from working. Skip tracing for
    // upgrade requests and let them flow through unmodified.
    if (ctx.WebSockets.IsWebSocketRequest)
    {
        reqLogger.LogInformation(
            "[ws] {Method} {Host}{Path}{Query}",
            ctx.Request.Method, ctx.Request.Host, ctx.Request.Path,
            ctx.Request.QueryString.Value ?? "");
        await next();
        return;
    }

    var host = ctx.Request.Host.Host;
    var path = ctx.Request.Path.Value ?? "";
    var isStorageLike = host.StartsWith("storage.", StringComparison.OrdinalIgnoreCase)
        || host.StartsWith("cdn.", StringComparison.OrdinalIgnoreCase)
        || path.Contains("upload", StringComparison.OrdinalIgnoreCase);

    if (isStorageLike)
    {
        reqLogger.LogInformation(
            "[req:start] {Method} {Host}{Path}{Query} contentType={ContentType} contentLength={ContentLength}",
            ctx.Request.Method, ctx.Request.Host, ctx.Request.Path,
            ctx.Request.QueryString.Value ?? "", ctx.Request.ContentType ?? "",
            ctx.Request.ContentLength);
    }

    var sw = System.Diagnostics.Stopwatch.StartNew();
    var originalBody = ctx.Response.Body;
    var captureResponse = ShouldCaptureResponse(ctx.Request);
    MemoryStream? capture = null;
    if (captureResponse)
    {
        capture = new MemoryStream();
        ctx.Response.Body = capture;
    }

    string? requestBody = null;
    if (ctx.Request.ContentLength is > 0 and < 4096 && IsTextLikeRequest(ctx.Request.ContentType))
    {
        ctx.Request.EnableBuffering();
        using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
        requestBody = await reader.ReadToEndAsync();
        ctx.Request.Body.Position = 0;
    }

    Exception? thrown = null;
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        thrown = ex;
        throw;
    }
    finally
    {
        sw.Stop();
        var status = thrown is null ? ctx.Response.StatusCode : 500;
        var responseBody = "";

        if (capture is not null)
        {
            try
            {
                capture.Seek(0, SeekOrigin.Begin);
                if (ShouldReadResponseBody(ctx.Response.ContentType, status))
                    responseBody = await new StreamReader(capture, leaveOpen: true).ReadToEndAsync();
                capture.Seek(0, SeekOrigin.Begin);
                await capture.CopyToAsync(originalBody);
            }
            finally
            {
                ctx.Response.Body = originalBody;
                await capture.DisposeAsync();
            }
        }

        var trimmedRequest = requestBody is null ? null
            : (requestBody.Length > 200 ? requestBody[..200] + "..." : requestBody);
        var trimmedResponse = responseBody.Length > 300 ? responseBody[..300] + "..." : responseBody;
        var level = thrown is not null || status >= 500 ? LogLevel.Error
                  : status >= 400 ? LogLevel.Warning
                  : LogLevel.Information;

        reqLogger.Log(level, thrown,
            "[req] {Status} {Method} {Host}{Path}{Query} {ElapsedMs}ms contentType={ContentType} contentLength={ContentLength} req={ReqBody} resp={RespBody}",
            status, ctx.Request.Method, ctx.Request.Host, ctx.Request.Path,
            ctx.Request.QueryString.Value ?? "", sw.ElapsedMilliseconds,
            ctx.Request.ContentType ?? "", ctx.Request.ContentLength,
            trimmedRequest ?? "", trimmedResponse);

        // Per-player log — only record when we know who made the call.
        var pid = DorkNet.Server.Auth.ControllerBaseExtensions.CurrentPlayerId(ctx.User);
        if (pid is long playerId)
        {
            playerLog.Record(new DorkNet.Server.Services.PlayerLogEntry
            {
                Timestamp = DateTime.UtcNow,
                PlayerId = playerId,
                Method = ctx.Request.Method,
                Host = ctx.Request.Host.Host,
                Path = ctx.Request.Path.Value ?? string.Empty,
                Query = ctx.Request.QueryString.Value ?? string.Empty,
                Status = status,
                ElapsedMs = sw.ElapsedMilliseconds,
                ReqBody = trimmedRequest,
                RespBody = trimmedResponse,
            });
        }
    }
});

// ── Create/repair DB ──────────────────────────────────────────────────────────
// Local launcher installs use SQLite as an appliance database. For that path,
// create the current EF model directly instead of replaying the migration chain;
// then run small idempotent repair patches for DBs made by older launcher builds.
// Hosted Postgres keeps its existing EnsureCreated + explicit patch path below.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();
    if (db.Database.IsSqlite())
    {
        db.Database.EnsureCreated();
        ApplySqliteCompatibilityPatches(db);
        // New tables that post-date this DB's EnsureCreated snapshot —
        // EnsureCreated never revisits an existing file, so create them
        // idempotently (matches the Postgres patch block below).
        await db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""SignupCodes"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SignupCodes"" PRIMARY KEY AUTOINCREMENT,
                ""Code"" TEXT NOT NULL,
                ""Descriptor"" TEXT NOT NULL DEFAULT '',
                ""CreatedByPlayerId"" INTEGER NOT NULL DEFAULT 0,
                ""CreatedAt"" TEXT NOT NULL,
                ""ExpiresAt"" TEXT NULL,
                ""RedeemedByPlayerId"" INTEGER NULL,
                ""RedeemedAt"" TEXT NULL,
                ""Revoked"" INTEGER NOT NULL DEFAULT 0
            );");
        await db.Database.ExecuteSqlRawAsync(
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SignupCodes_Code"" ON ""SignupCodes"" (""Code"");");
        await db.Database.ExecuteSqlRawAsync(
            @"CREATE TABLE IF NOT EXISTS ""PendingDevices"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PendingDevices"" PRIMARY KEY AUTOINCREMENT,
                ""DeviceId"" TEXT NOT NULL,
                ""Platform"" INTEGER NOT NULL DEFAULT 0,
                ""PlatformId"" TEXT NOT NULL DEFAULT '',
                ""LastIp"" TEXT NULL,
                ""FirstSeenAt"" TEXT NOT NULL,
                ""LastSeenAt"" TEXT NOT NULL
            );");
        await db.Database.ExecuteSqlRawAsync(
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PendingDevices_DeviceId"" ON ""PendingDevices"" (""DeviceId"");");
    }
    else
    {
        // Postgres path: schema generated from the EF Core model via
        // EnsureCreated. Wrapped in a transaction-scoped advisory lock
        // so concurrent replicas booting against the same database
        // don't race on CREATE TABLE — only the first replica to take
        // the lock runs EnsureCreated, the rest see "schema already
        // exists" and proceed.
        //
        // Lock key 0x444F524B (ascii "DORK") is hard-coded; it just
        // needs to be the same int64 across replicas and not collide
        // with anything else in the database. pg_advisory_xact_lock
        // releases automatically on COMMIT, so a crashed replica can't
        // wedge the next boot.
        //
        // Future schema changes will swap EnsureCreated for
        // db.Database.Migrate() once we cut a proper Postgres
        // migrations set; the advisory-lock wrapping stays the same.
        var conn = db.Database.GetDbConnection();

        // Retry initial Postgres connect — on container-orchestrated boots
        // (Coolify, docker-compose) the Postgres service's DNS often isn't
        // resolvable yet when this app starts, surfacing as SocketException
        // errno=11 EAGAIN from Npgsql's Dns.GetHostEntryOrAddressesCore.
        // Postgres itself can also be up-but-not-accepting-connections for
        // a few seconds while it replays WAL. 15 attempts × ~2s backoff
        // covers the typical 5-30s warmup window without looping forever
        // on a permanently broken config.
        var connectLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DorkNet.Server.Bootstrap");
        for (var attempt = 1; ; attempt++)
        {
            try { await conn.OpenAsync(); break; }
            catch (Exception ex) when (attempt < 15 &&
                (ex is System.Net.Sockets.SocketException
                 || ex is Npgsql.NpgsqlException
                 || ex is System.Data.Common.DbException))
            {
                var delay = TimeSpan.FromSeconds(Math.Min(attempt, 5));
                connectLogger.LogWarning(
                    "[bootstrap] Postgres connect attempt {Attempt}/15 failed ({Type}: {Message}); retrying in {Delay}s",
                    attempt, ex.GetType().Name, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }
        await using var tx = await conn.BeginTransactionAsync();
        await using (var lockCmd = conn.CreateCommand())
        {
            lockCmd.Transaction = tx;
            lockCmd.CommandText = "SELECT pg_advisory_xact_lock(1146246987);";
            await lockCmd.ExecuteNonQueryAsync();
        }
        db.Database.EnsureCreated();
        await tx.CommitAsync();

        // ── Post-EnsureCreated schema patches (Postgres only) ─────────
        // EnsureCreated only fires once per database — it lays down the
        // initial schema and never revisits it on later deploys. Any
        // model change after the first boot (column added, NOT NULL
        // dropped, etc.) leaves prod stuck on the bootstrap schema.
        // EF migrations in this codebase are SQLite-only; Postgres
        // schema drift has to be patched explicitly here.
        //
        // Each patch is idempotent — Postgres treats redundant
        // ALTER COLUMN statements as no-ops, so safe on every boot.
        // Add new patches at the bottom of this block; never remove
        // older ones (older databases still need them when they catch
        // up).
        var bootstrapLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DorkNet.Server.Bootstrap");

        async Task RunPatchAsync(string label, string sql)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            try { await cmd.ExecuteNonQueryAsync(); }
            catch (Exception ex)
            {
                bootstrapLogger.LogWarning(ex, "[schema-patch] {Label} failed", label);
            }
        }

        // 2026-05-16 — RoomDataBlobs.Bytes nullable. The storage
        // backfill (admin → Storage panel) needs to UPDATE this
        // column to NULL after streaming the bytes to S3.
        await RunPatchAsync("RoomDataBlobs.Bytes nullable",
            @"ALTER TABLE ""RoomDataBlobs"" ALTER COLUMN ""Bytes"" DROP NOT NULL;");

        // 2026-05-17 — ChatThreads / ChatThreadMembers added for
        // group chats + per-thread snooze/last-read pointers. Both
        // tables are EnsureCreated-skipped on existing DBs; recreate
        // them idempotently here. Column order + types must match
        // the entity classes (see Data/Entities/ChatThread*.cs).
        await RunPatchAsync("ChatThreads table",
            @"CREATE TABLE IF NOT EXISTS ""ChatThreads"" (
                ""Id"" bigserial PRIMARY KEY,
                ""ThreadKey"" varchar(96) NOT NULL,
                ""Name"" varchar(128) NOT NULL DEFAULT '',
                ""CreatorPlayerId"" bigint NOT NULL,
                ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
                ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now()
            );");
        await RunPatchAsync("ChatThreads ThreadKey unique index",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ChatThreads_ThreadKey""
                ON ""ChatThreads"" (""ThreadKey"");");
        await RunPatchAsync("ChatThreadMembers table",
            @"CREATE TABLE IF NOT EXISTS ""ChatThreadMembers"" (
                ""Id"" bigserial PRIMARY KEY,
                ""ThreadKey"" text NOT NULL,
                ""PlayerId"" bigint NOT NULL,
                ""JoinedAt"" timestamp with time zone NOT NULL DEFAULT now(),
                ""SnoozeUntil"" timestamp with time zone NULL,
                ""LastReadMessageId"" bigint NULL
            );");
        await RunPatchAsync("ChatThreadMembers (ThreadKey, PlayerId) unique index",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ChatThreadMembers_ThreadKey_PlayerId""
                ON ""ChatThreadMembers"" (""ThreadKey"", ""PlayerId"");");
        await RunPatchAsync("ChatThreadMembers PlayerId index",
            @"CREATE INDEX IF NOT EXISTS ""IX_ChatThreadMembers_PlayerId""
                ON ""ChatThreadMembers"" (""PlayerId"");");

        // 2026-05-17 — Relationships favorite/mute/ignore columns
        // for v1/favorite, v1/mute, v1/ignore persistence. Default
        // false on every row; existing relationships keep no
        // preference set.
        await RunPatchAsync("Relationships.Favorited column",
            @"ALTER TABLE ""Relationships""
                ADD COLUMN IF NOT EXISTS ""Favorited"" boolean NOT NULL DEFAULT false;");
        await RunPatchAsync("Relationships.Muted column",
            @"ALTER TABLE ""Relationships""
                ADD COLUMN IF NOT EXISTS ""Muted"" boolean NOT NULL DEFAULT false;");
        await RunPatchAsync("Relationships.Ignored column",
            @"ALTER TABLE ""Relationships""
                ADD COLUMN IF NOT EXISTS ""Ignored"" boolean NOT NULL DEFAULT false;");

        // 2026-05-17 — Widen StoreItems.Slug to 128 chars to fit
        // wardrobe-colored-{guid}-{color} SKUs. Postgres ALTER COLUMN
        // is fast (metadata-only); idempotent if already 128+.
        await RunPatchAsync("StoreItems.Slug widen to 128",
            @"ALTER TABLE ""StoreItems"" ALTER COLUMN ""Slug"" TYPE varchar(128);");

        // 2026-05-17 — Messages.Type column for RecNet.MessageType
        // round-tripping (RequestGameInvite = 10, GameInvite = 0,
        // TextMessage = 30, etc.). Before this column existed every
        // message came back to the watch as Type=30, breaking the
        // join-request flow.
        await RunPatchAsync("Messages.Type column",
            @"ALTER TABLE ""Messages""
                ADD COLUMN IF NOT EXISTS ""Type"" integer NOT NULL DEFAULT 30;");
        await RunPatchAsync("Messages.RoomId column",
            @"ALTER TABLE ""Messages""
                ADD COLUMN IF NOT EXISTS ""RoomId"" bigint NULL;");

        // 2026-05-20 — PrivateInstanceInvitees.LatestInviteMessageId.
        // Lets /goto/invite/{messageId} resolve the roomInstanceId via
        // the invitee row when the watch's accept flow has already
        // raced through DELETE /api/messages/v3/delete and the Message
        // row is gone. Pre-patch the server returned ErrorCode 40
        // ("invite expired") for accepts where DELETE landed first.
        await RunPatchAsync("PrivateInstanceInvitees.LatestInviteMessageId column",
            @"ALTER TABLE ""PrivateInstanceInvitees""
                ADD COLUMN IF NOT EXISTS ""LatestInviteMessageId"" bigint NULL;");

        // 2026-05-20 — coerce every existing PrivateInstance row to
        // match the current Photon:CloudRegion. Older rows still carry
        // whatever region was in config when their dorm was first
        // registered; without this rewrite, an invitee whose
        // /goto/invite resolves to one of those rows would be sent to
        // the stale region while the inviter's most recent
        // /goto/room/DormRoom puts them on the current region — two
        // parallel Photon rooms, players can't see each other. The
        // EnsureForDormAsync update path now refreshes PhotonRegion on
        // each owner /goto, so going forward this drift is impossible;
        // this one-shot UPDATE just cleans up the pre-fix backlog.
        // Idempotent — the WHERE skips rows already on the right
        // region.
        var currentRegion = (app.Configuration["Photon:CloudRegion"] ?? "us").ToLowerInvariant();
        await RunPatchAsync($"PrivateInstances.PhotonRegion → {currentRegion}",
            $@"UPDATE ""PrivateInstances""
               SET ""PhotonRegion"" = '{currentRegion}'
               WHERE ""PhotonRegion"" IS DISTINCT FROM '{currentRegion}';");

        // 2026-05-18 — CommunityBoardRows table for the dorm community
        // board (FeaturedPlayer/Announcement/InstagramImages/Videos).
        // EnsureCreated skips this on databases created before the
        // entity was added; without this patch the
        // /api/communityboard/v1/current endpoint silently returns
        // DefaultState because the row table doesn't exist (or the
        // row was never persisted) and admins see an empty board.
        await RunPatchAsync("CommunityBoardRows table",
            @"CREATE TABLE IF NOT EXISTS ""CommunityBoardRows"" (
                ""Id"" integer PRIMARY KEY,
                ""Json"" text NOT NULL DEFAULT '{}',
                ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now()
            );");

        // 2026-05-19 — ServerSettings single-row table backing the
        // admin SPA's runtime toggles (currently just SignupsDisabled).
        // Same EnsureCreated-skip story as CommunityBoardRows above:
        // existing prod DBs won't get the table without this idempotent
        // patch.
        await RunPatchAsync("ServerSettings table",
            @"CREATE TABLE IF NOT EXISTS ""ServerSettings"" (
                ""Id"" integer PRIMARY KEY,
                ""SignupsDisabled"" boolean NOT NULL DEFAULT false,
                ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now()
            );");

        // 2026-05-31 — Weekly-challenge config columns on ServerSettings.
        // Back the admin SPA's editable weekly slate + gift (XP/tokens +
        // an optional store skin granted on completion). EnsureCreated-
        // skipped on existing DBs, so add idempotently.
        await RunPatchAsync("ServerSettings.WeeklyChallengesCompletedRequired column",
            @"ALTER TABLE ""ServerSettings""
                ADD COLUMN IF NOT EXISTS ""WeeklyChallengesCompletedRequired"" boolean NOT NULL DEFAULT true;");
        await RunPatchAsync("ServerSettings.WeeklyChallengesJson column",
            @"ALTER TABLE ""ServerSettings""
                ADD COLUMN IF NOT EXISTS ""WeeklyChallengesJson"" text NOT NULL DEFAULT '';");
        await RunPatchAsync("ServerSettings.WeeklyChallengeRewardJson column",
            @"ALTER TABLE ""ServerSettings""
                ADD COLUMN IF NOT EXISTS ""WeeklyChallengeRewardJson"" text NOT NULL DEFAULT '';");
        // 2026-06-04 — "everyone is friends" admin toggle (synthesized at
        // read time; see ServerSettingsEntity.GlobalFriendsEnabled).
        await RunPatchAsync("ServerSettings.GlobalFriendsEnabled column",
            @"ALTER TABLE ""ServerSettings""
                ADD COLUMN IF NOT EXISTS ""GlobalFriendsEnabled"" boolean NOT NULL DEFAULT false;");

        // 2026-05-19 — Players.IsCommunityTeam column. Backs the
        // overhead-badge admin toggle alongside IsDeveloper; the
        // RoleController ORs the two so either one unlocks the watch's
        // in-settings developer-display slider (which renders
        // "Community Team" / "Developer" above the player's head per
        // Cpp2IL_ISIL/.../PlayerUI.txt:9085-9099).
        await RunPatchAsync("Players.IsCommunityTeam column",
            @"ALTER TABLE ""Players""
                ADD COLUMN IF NOT EXISTS ""IsCommunityTeam"" boolean NOT NULL DEFAULT false;");

        // 2026-05-20 — HiddenFromBrowse keeps admin-utility rooms
        // (MakerRoom, EventRoom) and rooms-folded-into-others
        // (paintball sub-maps, LaserTag Hangar) out of room browse,
        // search, and the originals feed while leaving /goto + clone
        // + admin access intact.
        await RunPatchAsync("Rooms.HiddenFromBrowse column",
            @"ALTER TABLE ""Rooms""
                ADD COLUMN IF NOT EXISTS ""HiddenFromBrowse"" boolean NOT NULL DEFAULT false;");

        // 2026-05-20 — RoomRoles table for per-room co-owner / moderator
        // / host grants. Drives RoomDetails.CoOwners / Moderators / Hosts
        // arrays (and InvitedCoOwners / InvitedModerators / InvitedHosts
        // when Accepted=false). Unique on (RoomId, PlayerId, Role) so
        // granting the same role twice is a no-op.
        await RunPatchAsync("RoomRoles table",
            @"CREATE TABLE IF NOT EXISTS ""RoomRoles"" (
                ""Id"" bigserial PRIMARY KEY,
                ""RoomId"" bigint NOT NULL,
                ""PlayerId"" bigint NOT NULL,
                ""Role"" integer NOT NULL,
                ""Accepted"" boolean NOT NULL DEFAULT true,
                ""GrantedByPlayerId"" bigint NULL,
                ""GrantedAt"" timestamp with time zone NOT NULL DEFAULT now()
            );");
        await RunPatchAsync("RoomRoles (RoomId, PlayerId, Role) unique index",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RoomRoles_RoomId_PlayerId_Role""
                ON ""RoomRoles"" (""RoomId"", ""PlayerId"", ""Role"");");
        await RunPatchAsync("RoomRoles RoomId index",
            @"CREATE INDEX IF NOT EXISTS ""IX_RoomRoles_RoomId"" ON ""RoomRoles"" (""RoomId"");");

        // 2026-05-20 — LeaderboardChannelMeta lets admins map any
        // StatChannel int the watch reports to (room, name, sort
        // direction). Drives the per-room Leaderboards tab in the
        // admin SPA. KnownStatChannels in AdminController stays as
        // hardcoded fallback for channels with no row here.
        await RunPatchAsync("LeaderboardChannelMeta table",
            @"CREATE TABLE IF NOT EXISTS ""LeaderboardChannelMeta"" (
                ""Channel"" integer PRIMARY KEY,
                ""RoomId"" bigint NOT NULL DEFAULT 0,
                ""Name"" varchar(128) NOT NULL DEFAULT '',
                ""LowerIsBetter"" boolean NOT NULL DEFAULT false,
                ""ValueFormat"" varchar(32) NOT NULL DEFAULT 'count',
                ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
                ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now()
            );");
        await RunPatchAsync("LeaderboardChannelMeta RoomId index",
            @"CREATE INDEX IF NOT EXISTS ""IX_LeaderboardChannelMeta_RoomId""
                ON ""LeaderboardChannelMeta"" (""RoomId"");");

        // 2026-05-17 — LoadingScreenTips table for admin-editable
        // dorm-load splash tips with optional uploaded images.
        await RunPatchAsync("LoadingScreenTips table",
            @"CREATE TABLE IF NOT EXISTS ""LoadingScreenTips"" (
                ""Id"" bigserial PRIMARY KEY,
                ""Title"" varchar(128) NOT NULL DEFAULT '',
                ""Message"" varchar(512) NOT NULL DEFAULT '',
                ""ImageName"" varchar(128) NOT NULL DEFAULT '',
                ""Context"" int NOT NULL DEFAULT 0,
                ""PlatformMask"" int NOT NULL DEFAULT -1,
                ""RoomNamesCsv"" varchar(512) NOT NULL DEFAULT '',
                ""SortOrder"" int NOT NULL DEFAULT 0,
                ""IsActive"" boolean NOT NULL DEFAULT true,
                ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
                ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now()
            );");

        // 2026-05-31 — Signup codes + pending-device capture for the
        // admin-issued invite flow (site /join). New tables, so
        // EnsureCreated-skipped on existing DBs.
        await RunPatchAsync("SignupCodes table",
            @"CREATE TABLE IF NOT EXISTS ""SignupCodes"" (
                ""Id"" bigserial PRIMARY KEY,
                ""Code"" text NOT NULL,
                ""Descriptor"" text NOT NULL DEFAULT '',
                ""CreatedByPlayerId"" bigint NOT NULL DEFAULT 0,
                ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
                ""ExpiresAt"" timestamp with time zone NULL,
                ""RedeemedByPlayerId"" bigint NULL,
                ""RedeemedAt"" timestamp with time zone NULL,
                ""Revoked"" boolean NOT NULL DEFAULT false
            );");
        await RunPatchAsync("SignupCodes Code unique index",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SignupCodes_Code"" ON ""SignupCodes"" (""Code"");");
        await RunPatchAsync("PendingDevices table",
            @"CREATE TABLE IF NOT EXISTS ""PendingDevices"" (
                ""Id"" bigserial PRIMARY KEY,
                ""DeviceId"" text NOT NULL,
                ""Platform"" integer NOT NULL DEFAULT 0,
                ""PlatformId"" text NOT NULL DEFAULT '',
                ""LastIp"" text NULL,
                ""FirstSeenAt"" timestamp with time zone NOT NULL DEFAULT now(),
                ""LastSeenAt"" timestamp with time zone NOT NULL DEFAULT now()
            );");
        await RunPatchAsync("PendingDevices DeviceId unique index",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PendingDevices_DeviceId"" ON ""PendingDevices"" (""DeviceId"");");

        await conn.CloseAsync();
    }

    // Coach system account at Player.Id=1. The RR-Original room seeder
    // below assigns CreatorPlayerId=1 to every canonical room
    // (RecCenter, Paintball, Dodgeball, etc.), so the FK target needs
    // to exist before we insert rooms — otherwise admin tools' "rooms
    // by player N" queries return rows whose CreatorPlayerId points to
    // a non-existent Player. Idempotent: the call no-ops once the row
    // is there. Must run BEFORE roomService.SeedAsync().
    var playerService = scope.ServiceProvider.GetRequiredService<PlayerService>();
    await playerService.EnsureSystemAccountAsync();
    // Self-heal the AvatarEntity seed on every startup so accounts
    // created with old/broken starter values get the current safe
    // GUIDs without needing a one-shot EF migration. Idempotent —
    // only updates rows that need it (new accounts, accounts with
    // the old mask/swatch starter, and any orphan empty rows).
    await playerService.EnsureAvatarSeedAsync();

    // Grant the starter RecCenterTokens balance to any account that
    // doesn't yet have a CurrencyType=2 row. Pre-existing accounts
    // (created before this seed shipped) booted into a 0-token wallet
    // and saw "Not enough tokens" on every store tile — this brings
    // them up to the same starting state new accounts get. Idempotent:
    // only inserts where the row is absent, never overwrites a row
    // that's already there (so spent-down accounts aren't refilled).
    await playerService.BackfillStarterWalletsAsync();

    // 2018 client: complete any account missing birthday/email/password so the
    // (screen-mode-broken) signup step flow never appears. See
    // PlayerService.ApplyRegistrationDefaults.
    await playerService.BackfillAccountCompletionAsync();

    // Backfill the per-player dorm room + DormStateEntity for any
    // legacy account that signed in via a path that didn't run
    // EnsurePersonalDormAsync (early-bird signups, manual SQL
    // imports). Without these rows the watch's
    // /api/rooms/v4/details/{dormId} → cdn.localhost/room/{blobName}
    // chain hits an empty CurrentDataBlobName which the CDN serves
    // as the default-dorm bytes — but only if the dorm room itself
    // exists. This call guarantees both rows for every existing
    // account before traffic starts. Runs after EnsureSystemAccount
    // so Coach is excluded.
    var roomService = scope.ServiceProvider.GetRequiredService<RoomService>();
    await roomService.EnsureDormsForAllPlayersAsync();

    // Seed canonical Rec Room Original rooms so the watch's "Trending" tab
    // has content on first launch. Idempotent — RoomService.SeedAsync
    // bails if any rooms already exist.
    await roomService.SeedAsync();

    // Canonical room overrides: rename BloodMoon → Crescendo, fold the
    // paintball-map standalone rooms (River/Homestead/Quarry/Clearcut/
    // Spillway/Drive-In) into Paintball as sub-rooms, fold LaserTagHangar
    // into LaserTag, hide MakerRoom + EventRoom from browse, and pull
    // down the user-supplied thumbnails for Crescendo + Paintball.
    // Idempotent — re-running on an already-overridden DB no-ops.
    using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
    {
        var imagesDir = Path.Combine(AppContext.BaseDirectory, "data", "images");
        await roomService.ApplyCanonicalOverridesAsync(http, imagesDir);
    }

    // Seed the default store catalog so the Shop tab is populated on
    // first boot. Idempotent — only inserts items whose slug isn't
    // already present.
    var storeService = scope.ServiceProvider.GetRequiredService<StoreService>();
    await storeService.SeedAsync();

    // Tell the /healthz probe migrations are done. Coolify's rolling
    // deploy will hold traffic at the LB until this flips and the
    // healthz response goes 503 → 200.
    DorkNet.Server.Controllers.Health.HealthController.MigrationsComplete = true;
}

// ── Middleware ────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// HTTP → HTTPS redirect — only kicks in when an HTTPS endpoint is configured
// in appsettings (Kestrel-direct mode). When fronted by nginx, both endpoints
// are effectively HTTP from Kestrel's perspective and this is a no-op.
app.UseHttpsRedirection();

// 2-minute WebSocket pings are too lazy for cloudflared — the tunnel
// kills the underlying TCP stream after ~90-100s of silence even
// though SignalR's app-layer pings would arrive on schedule. Send a
// ping frame every 30s so the tunnel always sees fresh traffic; the
// ping payload is tiny, and SignalR's own KeepAliveInterval (15s)
// stays under this floor.
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

// Static admin UI hosted on admin.rec.net (its own subdomain). The
// PhysicalFileProvider is rooted at wwwroot/admin so requests like
// admin.rec.net/style.css map to wwwroot/admin/style.css — the URL
// has no /admin prefix.
//
// Mounted BEFORE the tracing wrapper because StaticFileMiddleware uses
// IHttpResponseBodyFeature.SendFileAsync, which writes directly to the
// underlying connection and ignores any Response.Body replacement done
// downstream — putting it after tracing would result in the actual
// file being sent to the socket, then the tracing middleware copying
// its (empty) MemoryStream back over the same body and clobbering it.
//
// UseWhen (NOT MapWhen) so that requests for paths the admin UI
// doesn't serve (e.g. admin.rec.net/api/admin/v1/players) fall through
// to the controller pipeline. MapWhen would fork into a parallel
// pipeline that terminates without invoking matched endpoints.
// Helper: bind a per-host static-files branch. Each subdomain gets its
// own PhysicalFileProvider rooted at wwwroot/<subdir>, so requests like
// admin.rec.net/style.css map to wwwroot/admin/style.css with no path
// prefix munging required.
static void MountStaticHost(WebApplication app, string host, string subdir, bool spaFallback = false)
{
    var staticHostLogger = app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DorkNet.Server.StaticHost");
    var root = Path.Combine(app.Environment.ContentRootPath, "wwwroot", subdir);
    staticHostLogger.LogInformation(
        "[static-host] {Host} → {Root} (exists: {Exists}, spaFallback: {Spa})",
        host, root, Directory.Exists(root), spaFallback);
    if (!Directory.Exists(root)) return;
    var files = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(root);

    // Use UseDefaultFiles + UseStaticFiles directly rather than
    // UseFileServer because the latter's nested options initialization
    // doesn't always propagate the FileProvider correctly when wrapped
    // inside UseWhen — leading to the static files middleware looking
    // at the default WebRoot (wwwroot/) instead of our scoped folder
    // (wwwroot/admin/), which makes /index.html requests fall through
    // to the API catch-all.
    var defaultFilesOpts = new DefaultFilesOptions { FileProvider = files };
    var staticFilesOpts = new StaticFileOptions
    {
        FileProvider = files,
        // Don't let the browser cache admin assets — we iterate on the
        // HTML/JS/CSS often and a stale cached page silently swallows
        // every change. Cache-Control: no-cache forces a conditional
        // GET (If-Modified-Since) on every load; the file is small and
        // the round-trip is fine for an admin UI used by 1-2 people.
        OnPrepareResponse = ctx =>
        {
            var headers = ctx.Context.Response.Headers;
            headers.CacheControl = "no-cache, no-store, must-revalidate";
            headers.Pragma = "no-cache";
            headers.Expires = "0";
        },
    };

    app.UseWhen(ctx => ctx.Request.Host.Host.Equals(host, StringComparison.OrdinalIgnoreCase),
        branch =>
        {
            // Diagnostic: confirm the branch is being entered AND the
            // FileProvider can resolve the requested file. At Information
            // level temporarily to debug the admin asset 404s.
            //
            // Endpoint-clearing rule: the auto-added UseRouting (which
            // runs before our middleware in minimal hosting) may have
            // matched a non-admin endpoint for paths the static-host
            // should answer — typically NsController.Root for "/" on
            // admin.localhost and the apex, which would serve the
            // service-URL-map JSON instead of the SPA's index.html.
            // StaticFileMiddleware's ValidateNoEndpoint guard skips
            // serving whenever an endpoint is already matched, so we
            // clear it for non-API paths to let UseStaticFiles run.
            //
            // BUT: /api/* paths MUST keep their matched endpoint —
            // those are admin/site/feed API calls that the matching
            // controller (AdminController, SiteController, etc.)
            // needs to handle. Clearing the endpoint for /api/*
            // would 404 every admin API call.
            branch.Use(async (ctx, next) =>
            {
                var p = ctx.Request.Path.Value ?? "/";
                var probe = files.GetFileInfo(p == "/" ? "/index.html" : p);
                staticHostLogger.LogInformation(
                    "[static-host:{Host}] path={Path} probe={Probe} exists={Exists}",
                    host, p, probe.PhysicalPath ?? "<null>", probe.Exists);
                if (!p.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                    ctx.SetEndpoint(null);
                await next();
            });
            branch.UseDefaultFiles(defaultFilesOpts);
            branch.UseStaticFiles(staticFilesOpts);

            // Terminal handler — runs AFTER UseStaticFiles. By this point
            // the request either:
            //   (a) was a static file UseStaticFiles served — Response
            //       already started, we just return.
            //   (b) is an API call we want to fall through to controllers
            //       (e.g. admin.localhost/api/admin/v1/players,
            //       localhost/api/site/v1/feed) — call next().
            //   (c) is a SPA deep link (no extension) we want to answer
            //       with index.html so React Router can re-resolve.
            //   (d) is a missing static asset (has extension, file
            //       gone — e.g. /assets/index-<stalehash>.js after a
            //       redeploy). Return 404 with no body — DO NOT fall
            //       through to controllers. The catch-all used to
            //       answer these with JSON {} which a browser <script>
            //       loader rejects with "disallowed MIME type" and the
            //       page stays broken until the user hard-refreshes.
            //       404 makes the browser drop its stale index.html
            //       cache and refetch on the next load.
            branch.Use(async (ctx, next) =>
            {
                if (ctx.Response.HasStarted) return;

                var path = ctx.Request.Path.Value ?? "/";

                // /api/* paths are reserved for controllers (admin API,
                // site API, photos feed API, etc.). Always pass through.
                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                {
                    await next();
                    return;
                }

                // Non-GET methods aren't ours either — let controllers
                // handle POST/PUT/DELETE on this host.
                if (ctx.Request.Method != "GET" && ctx.Request.Method != "HEAD")
                {
                    await next();
                    return;
                }

                // GET with file extension — UseStaticFiles already tried
                // and didn't serve, so the file is missing. 404 directly;
                // don't fall through to controllers (a stale
                // /assets/<hash>.js can't possibly resolve to anything
                // useful, and serving HTML for a .js request breaks the
                // browser's module loader).
                if (Path.HasExtension(path))
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                // GET, no extension. SPA fallback if enabled (admin SPA,
                // public site). Without it (feed.* legacy), 404 so
                // missing deep links don't silently land on the homepage.
                if (!spaFallback)
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                var index = files.GetFileInfo("/index.html");
                if (!index.Exists)
                {
                    ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                ctx.Response.ContentType = "text/html; charset=utf-8";
                var hdrs = ctx.Response.Headers;
                hdrs.CacheControl = "no-cache, no-store, must-revalidate";
                hdrs.Pragma = "no-cache";
                hdrs.Expires = "0";
                await using var stream = index.CreateReadStream();
                await stream.CopyToAsync(ctx.Response.Body);
            });
        });
}

// Per-host static branches — admin SPA, public site, legacy feed.* —
// all keyed off the configured apex so a single DORKNET_DOMAIN env
// var moves every static host together (admin.{apex}, {apex},
// www.{apex}, feed.{apex}).
MountStaticHost(app, domainCfg.Sub("admin"),  "admin", spaFallback: true);
// Public-facing site at the apex (e.g. localhost + www variant).
// React-router routes are client-side so spaFallback must be true —
// without it /players?q=foo, /photo/123, etc. would 404 on first
// load. PublicSiteController owns /api/site/v1/*; everything else
// extensionless falls back to index.html.
MountStaticHost(app, domainCfg.Apex,          "site",  spaFallback: true);
MountStaticHost(app, $"www.{domainCfg.Apex}", "site",  spaFallback: true);
// Old feed.* subdomain kept as legacy; new visitors land on the apex.
MountStaticHost(app, domainCfg.Sub("feed"),   "feed");

// Friendly redirect from the old api.{apex}/admin path → admin.{apex}.
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Host.Host.Equals(domainCfg.Sub("api"), StringComparison.OrdinalIgnoreCase) &&
        (ctx.Request.Path == "/admin" || ctx.Request.Path.StartsWithSegments("/admin")))
    {
        ctx.Response.Redirect($"https://{domainCfg.Sub("admin")}/", permanent: false);
        return;
    }
    await next();
});

// IP-level bans run BEFORE authentication so banned-IP traffic
// gets cut off at the earliest point possible — no JWT round-trip,
// no DB hit beyond the (small) IpBans lookup.
app.UseMiddleware<DorkNet.Server.Auth.IpBanCheckMiddleware>();

app.UseMiddleware<IdentityServerTokenResponseMiddleware>();
app.UseMiddleware<IdentityServerLegacyTokenRequestMiddleware>();
app.UseIdentityServer();

app.UseAuthentication();
app.UseAuthorization();
// Sits between auth and the controllers — by this point ctx.User has the
// validated principal (or is empty for anonymous calls). Bans are enforced
// uniformly across every authenticated endpoint without each controller
// having to remember to check.
app.UseMiddleware<DorkNet.Server.Auth.BanCheckMiddleware>();
app.MapControllers();

// SignalR hub on the notify.* subdomain — RequireHost prevents the
// same path from being matched on api.* or other subdomains. The host
// is derived from DomainConfig so a single DORKNET_DOMAIN env var
// drives where the hub answers; setting DORKNET_DOMAIN=localhost keeps
// notify.localhost, rec.net keeps notify.rec.net, etc.
// Pin transport to WebSockets-only. Letting SignalR fall back to
// ServerSentEvents or LongPolling makes "is the proxy idle?" a
// per-poll question instead of a stream-level one — Coolify's
// Traefik or Cloudflare's edge can close a poll mid-flight and the
// watch's BestHTTP framing layer surfaces it as "TCP Stream closed
// unexpectedly" → "check your internet". WebSockets keep one socket
// hot with our 10s pings; the proxies all see continuous frame
// traffic and never mark it idle.
app.MapHub<DorkNet.Server.Hubs.NotifyHub>("/hub/v1", opts =>
{
    opts.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
    // SignalR's per-connection idle disconnect — must be > the longest
    // keepalive interval in the stack (we ping every 10s; 90s lets two
    // pings be missed before the server gives up).
    opts.TransportSendTimeout = TimeSpan.FromSeconds(30);
}).RequireHost(domainCfg.Sub("notify"));

app.Run();

static bool TryMapSingleOriginPath(PathString path, out string service, out PathString rest)
{
    service = "";
    rest = PathString.Empty;
    if (!path.StartsWithSegments("/__dn", out var tail)) return false;

    var value = tail.Value ?? "";
    if (value.Length <= 1) return false;
    var slash = value.IndexOf('/', 1);
    service = slash < 0 ? value[1..] : value[1..slash];
    if (string.IsNullOrWhiteSpace(service)) return false;

    rest = slash < 0 ? "/" : value[slash..];
    return true;
}

static bool IsHealthProbe(HttpRequest request)
{
    return request.Path.Equals("/healthz", StringComparison.OrdinalIgnoreCase);
}

static bool IsTextLikeRequest(string? contentType)
{
    if (string.IsNullOrWhiteSpace(contentType)) return false;
    return contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
        || contentType.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
        || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
}

static bool ShouldCaptureResponse(HttpRequest request)
{
    var host = request.Host.Host;
    if (host.StartsWith("cdn.", StringComparison.OrdinalIgnoreCase))
        return false;

    var path = request.Path.Value ?? "";
    var ext = Path.GetExtension(path);
    if (string.IsNullOrEmpty(ext))
        return true;

    return ext.Equals(".json", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".txt", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".log", StringComparison.OrdinalIgnoreCase);
}

static bool ShouldReadResponseBody(string? contentType, int status)
{
    if (status < 400) return false;
    if (string.IsNullOrWhiteSpace(contentType)) return true;
    return contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
        || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase);
}

// One-shot upgrade helper: when the SQLite db already contains tables but
// has no __EFMigrationsHistory entries (i.e. it was created by the legacy
// `EnsureCreated()` path before we shipped migrations), insert a history
// row for the Initial migration so Migrate() treats Initial as
// already-applied and runs only the subsequent column-add migrations.
//
// Detection: `Players` table exists AND __EFMigrationsHistory has zero
// rows. After this runs once, the inserted row keeps it from firing again.
//
// Safe to leave in indefinitely — on a fresh DB the `Players` table won't
// exist yet, so the helper short-circuits and Migrate() does the full
// CreateTable run as normal.
static void BaselineExistingSchemaIfNeeded(DorkNetDbContext db)
{
    using var conn = db.Database.GetDbConnection();
    conn.Open();

    using (var probe = conn.CreateCommand())
    {
        probe.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name='Players';";
        var hasPlayers = probe.ExecuteScalar() is not null;
        if (!hasPlayers) return; // Fresh DB — let Migrate() do its full thing.
    }

    using (var historyProbe = conn.CreateCommand())
    {
        historyProbe.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
        var historyExists = historyProbe.ExecuteScalar() is not null;
        if (historyExists)
        {
            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM __EFMigrationsHistory;";
            var existingRows = Convert.ToInt64(countCmd.ExecuteScalar());
            if (existingRows > 0) return; // Already on migrations — nothing to do.
        }
        else
        {
            using var createHistory = conn.CreateCommand();
            createHistory.CommandText = """
                CREATE TABLE "__EFMigrationsHistory" (
                    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                    "ProductVersion" TEXT NOT NULL
                );
                """;
            createHistory.ExecuteNonQuery();
        }
    }

    using (var insert = conn.CreateCommand())
    {
        insert.CommandText = """
            INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES ('20260508092637_Initial', '9.0.4');
            """;
        insert.ExecuteNonQuery();
    }

    // A previous failed migrate may have left a stale row in
    // __EFMigrationsLock (EF Core 9 acquires this lock around the migrate
    // run; a crash before release leaves the row, blocking the next
    // Migrate call indefinitely). Drop the table so Migrate re-creates
    // it cleanly. Safe — the lock is per-application-instance and we're
    // single-instance here.
    using (var dropLock = conn.CreateCommand())
    {
        dropLock.CommandText = "DROP TABLE IF EXISTS __EFMigrationsLock;";
        dropLock.ExecuteNonQuery();
    }

    Log.Information(
        "[migrations] Detected legacy EnsureCreated DB; baselined as 20260508092637_Initial. Subsequent migrations will apply normally.");
}

static void ApplySqliteCompatibilityPatches(DorkNetDbContext db)
{
    if (!db.Database.IsSqlite()) return;
    AddSqliteColumnIfMissing(db, "Rooms", "HiddenFromBrowse",
        @"""HiddenFromBrowse"" INTEGER NOT NULL DEFAULT 0");
    // 2026-05-31 — weekly-challenge config columns (see ServerSettingsEntity).
    AddSqliteColumnIfMissing(db, "ServerSettings", "WeeklyChallengesCompletedRequired",
        @"""WeeklyChallengesCompletedRequired"" INTEGER NOT NULL DEFAULT 1");
    AddSqliteColumnIfMissing(db, "ServerSettings", "WeeklyChallengesJson",
        @"""WeeklyChallengesJson"" TEXT NOT NULL DEFAULT ''");
    AddSqliteColumnIfMissing(db, "ServerSettings", "WeeklyChallengeRewardJson",
        @"""WeeklyChallengeRewardJson"" TEXT NOT NULL DEFAULT ''");
    // 2026-06-04 — "everyone is friends" admin toggle.
    AddSqliteColumnIfMissing(db, "ServerSettings", "GlobalFriendsEnabled",
        @"""GlobalFriendsEnabled"" INTEGER NOT NULL DEFAULT 0");
}

static void AddSqliteColumnIfMissing(DorkNetDbContext db, string table, string column, string definition)
{
    var conn = db.Database.GetDbConnection();
    var shouldClose = conn.State == System.Data.ConnectionState.Closed;
    if (shouldClose) conn.Open();
    try
    {
        var exists = false;
        using var check = conn.CreateCommand();
        check.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\");";
        {
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (!string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) continue;
                exists = true;
                break;
            }
        }

        if (exists)
        {
            Log.Information("[sqlite-compat] {Table}.{Column} already exists", table, column);
            return;
        }

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE \"{table.Replace("\"", "\"\"")}\" ADD COLUMN {definition};";
        alter.ExecuteNonQuery();
        Log.Information("[sqlite-compat] Added {Table}.{Column}", table, column);
    }
    finally
    {
        if (shouldClose) conn.Close();
    }
}
