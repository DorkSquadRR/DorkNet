using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;
using DorkNet.Server.Services;
using Serilog;

namespace DorkNet.Server.Startup;

/// <summary>Boot-time database orchestration: schema migration,
/// idempotent data upgrades, and canonical seed application. Replaces
/// the inline block previously in Program.cs.
///
/// <para>Provider-specific paths:
/// <list type="bullet">
///   <item>SQLite — <c>Database.Migrate()</c> after
///   <see cref="BaselineExistingSchemaIfNeeded"/> reconciles any legacy
///   migration-history state.</item>
///   <item>Postgres — <c>EnsureCreated</c> behind a transaction-scoped
///   advisory lock so concurrent replica boots don't race on
///   <c>CREATE TABLE</c>. Connect retries cover the
///   container-orchestrated boot window where Postgres DNS isn't
///   resolvable yet.</item>
/// </list>
/// After the schema step both branches run the same idempotent
/// post-init sequence: system-account seed, avatar-seed self-heal,
/// starter-wallet backfill, dorm backfill, <see cref="LegacyUpgrades"/>
/// data transforms, canonical room seeding + overrides, and per-feature
/// seeders. The <c>/healthz</c> probe flips to 200 only after all of
/// this completes — Coolify's rolling deploy holds traffic at the LB
/// until then.</para></summary>
public static class DatabaseBootstrap
{
    public static async Task RunDatabaseBootstrapAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();

        if (db.Database.IsSqlite())
        {
            BaselineExistingSchemaIfNeeded(db);
            db.Database.Migrate();
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

            await conn.CloseAsync();
        }

        // Signup-code + pending-device tables. These post-date the
        // consolidated Initial migration, and the Postgres path is
        // EnsureCreated-only (no migration replay), so neither provider
        // gets them automatically on an existing DB. Create them with
        // idempotent raw SQL that works on a fresh DB too (CREATE TABLE
        // IF NOT EXISTS no-ops when EnsureCreated already built them).
        await EnsureSignupCodeTablesAsync(db);

        // Coach system account at Player.Id=1. The RR-Original room seeder
        // below assigns CreatorPlayerId=1 to every canonical room
        // (RecCenter, Paintball, Dodgeball, etc.), so the FK target needs
        // to exist before we insert rooms. Idempotent: the call no-ops once
        // the row is there. Must run BEFORE roomService.SeedAsync().
        var playerService = scope.ServiceProvider.GetRequiredService<PlayerService>();
        await playerService.EnsureSystemAccountAsync();
        // Self-heal the AvatarEntity seed on every startup so accounts
        // created with old/broken starter values get the current safe
        // GUIDs without needing a one-shot EF migration.
        await playerService.EnsureAvatarSeedAsync();

        // Grant the starter RecCenterTokens balance to any account that
        // doesn't yet have a CurrencyType=2 row. Pre-existing accounts
        // booted into a 0-token wallet and saw "Not enough tokens" on
        // every store tile — this brings them up to the same starting
        // state new accounts get. Idempotent.
        await playerService.BackfillStarterWalletsAsync();

        // Backfill the per-player dorm room + DormStateEntity for any
        // legacy account that signed in via a path that didn't run
        // EnsurePersonalDormAsync. Without these rows the watch's
        // /api/rooms/v4/details/{dormId} → cdn.{apex}/room/{blobName}
        // chain hits an empty CurrentDataBlobName.
        var roomService = scope.ServiceProvider.GetRequiredService<RoomService>();
        await roomService.EnsureDormsForAllPlayersAsync();

        // Idempotent data-transform pass for things that can't be expressed
        // as an EF migration against the entity model. MUST run before
        // SeedAsync() so the seed sees the post-rename state.
        var legacyLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DorkNet.Server.LegacyUpgrades");
        await LegacyUpgrades.RunAsync(db, app.Configuration, legacyLogger);

        // Seed canonical Rec Room Original rooms so the watch's "Trending"
        // tab has content on first launch. Idempotent — bails if any
        // rooms already exist.
        await roomService.SeedAsync();

        // Canonical room overrides: rename BloodMoon → Crescendo, fold
        // the paintball-map standalone rooms into Paintball as sub-rooms,
        // fold LaserTagHangar into LaserTag, hide MakerRoom + EventRoom
        // from browse, and pull down the user-supplied thumbnails.
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
        {
            var imagesDir = Path.Combine(AppContext.BaseDirectory, "data", "images");
            await roomService.ApplyCanonicalOverridesAsync(http, imagesDir);
        }

        // Seed default curated playlists, club category tags, store catalog.
        // Must run after SeedAsync so they can reference RR-Originals.
        var playlistService = scope.ServiceProvider.GetRequiredService<PlaylistService>();
        await playlistService.SeedCuratedAsync();
        var clubService = scope.ServiceProvider.GetRequiredService<ClubService>();
        await clubService.SeedDefaultsAsync();
        var storeService = scope.ServiceProvider.GetRequiredService<StoreService>();
        await storeService.SeedAsync();

        // Tell the /healthz probe migrations are done. Coolify's rolling
        // deploy holds traffic at the LB until this flips and /healthz
        // returns 200.
        Controllers.Health.HealthController.MigrationsComplete = true;
    }

    /// <summary>Idempotently create the SignupCodes + PendingDevices
    /// tables on whichever provider is in use. Safe to run every boot:
    /// CREATE TABLE / INDEX IF NOT EXISTS no-op once they exist (whether
    /// created here or by a fresh-DB EnsureCreated).</summary>
    private static async Task EnsureSignupCodeTablesAsync(DorkNetDbContext db)
    {
        var pg = db.Database.IsNpgsql();
        var statements = pg
            ? new[]
            {
                """
                CREATE TABLE IF NOT EXISTS "SignupCodes" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "Code" text NOT NULL,
                    "Descriptor" text NOT NULL DEFAULT '',
                    "CreatedByPlayerId" bigint NOT NULL DEFAULT 0,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "ExpiresAt" timestamp with time zone NULL,
                    "RedeemedByPlayerId" bigint NULL,
                    "RedeemedAt" timestamp with time zone NULL,
                    "Revoked" boolean NOT NULL DEFAULT false
                );
                """,
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SignupCodes_Code"" ON ""SignupCodes"" (""Code"");",
                """
                CREATE TABLE IF NOT EXISTS "PendingDevices" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "DeviceId" text NOT NULL,
                    "Platform" integer NOT NULL DEFAULT 0,
                    "PlatformId" text NOT NULL DEFAULT '',
                    "LastIp" text NULL,
                    "FirstSeenAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "LastSeenAt" timestamp with time zone NOT NULL DEFAULT now()
                );
                """,
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PendingDevices_DeviceId"" ON ""PendingDevices"" (""DeviceId"");",
            }
            : new[]
            {
                """
                CREATE TABLE IF NOT EXISTS "SignupCodes" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_SignupCodes" PRIMARY KEY AUTOINCREMENT,
                    "Code" TEXT NOT NULL,
                    "Descriptor" TEXT NOT NULL DEFAULT '',
                    "CreatedByPlayerId" INTEGER NOT NULL DEFAULT 0,
                    "CreatedAt" TEXT NOT NULL,
                    "ExpiresAt" TEXT NULL,
                    "RedeemedByPlayerId" INTEGER NULL,
                    "RedeemedAt" TEXT NULL,
                    "Revoked" INTEGER NOT NULL DEFAULT 0
                );
                """,
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SignupCodes_Code"" ON ""SignupCodes"" (""Code"");",
                """
                CREATE TABLE IF NOT EXISTS "PendingDevices" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_PendingDevices" PRIMARY KEY AUTOINCREMENT,
                    "DeviceId" TEXT NOT NULL,
                    "Platform" INTEGER NOT NULL DEFAULT 0,
                    "PlatformId" TEXT NOT NULL DEFAULT '',
                    "LastIp" TEXT NULL,
                    "FirstSeenAt" TEXT NOT NULL,
                    "LastSeenAt" TEXT NOT NULL
                );
                """,
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PendingDevices_DeviceId"" ON ""PendingDevices"" (""DeviceId"");",
            };

        foreach (var sql in statements)
            await db.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>SQLite migration-history reconciliation. Handles two
    /// cutover scenarios so legacy DBs boot cleanly without manual SQL:
    /// <list type="number">
    ///   <item><b>Legacy EnsureCreated</b> — schema exists, no
    ///   <c>__EFMigrationsHistory</c>. (Pre-migrations DorkNet builds
    ///   used EnsureCreated for SQLite too.)</item>
    ///   <item><b>Stale history after consolidation</b> — history
    ///   references migration IDs that no longer exist in the assembly.
    ///   Happens once when the per-feature migrations are collapsed into
    ///   a single Initial (see <c>Data/MIGRATIONS.md</c>).</item>
    /// </list>
    /// In both cases the on-disk schema is canonical; we just need history
    /// to reflect the assembly's current migration list so Migrate()
    /// no-ops. Fresh DBs short-circuit (no <c>Players</c> table yet) and
    /// Migrate() does the full CreateTable run normally.</summary>
    private static void BaselineExistingSchemaIfNeeded(DorkNetDbContext db)
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

        var assemblyMigrations = db.Database.GetMigrations().ToList();
        if (assemblyMigrations.Count == 0) return;

        var historyExists = false;
        using (var historyProbe = conn.CreateCommand())
        {
            historyProbe.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory';";
            historyExists = historyProbe.ExecuteScalar() is not null;
        }

        var existingIds = new HashSet<string>(StringComparer.Ordinal);
        if (historyExists)
        {
            using var readCmd = conn.CreateCommand();
            readCmd.CommandText = @"SELECT ""MigrationId"" FROM ""__EFMigrationsHistory"";";
            using var reader = readCmd.ExecuteReader();
            while (reader.Read()) existingIds.Add(reader.GetString(0));

            if (existingIds.SetEquals(assemblyMigrations))
                return;
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

        using (var wipe = conn.CreateCommand())
        {
            wipe.CommandText = @"DELETE FROM ""__EFMigrationsHistory"";";
            wipe.ExecuteNonQuery();
        }

        var productVersion = typeof(DbContext).Assembly.GetName().Version?.ToString() ?? "10.0.0";
        foreach (var id in assemblyMigrations)
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ($id, $ver);
                """;
            var pId = insert.CreateParameter(); pId.ParameterName = "$id"; pId.Value = id;  insert.Parameters.Add(pId);
            var pVr = insert.CreateParameter(); pVr.ParameterName = "$ver"; pVr.Value = productVersion; insert.Parameters.Add(pVr);
            insert.ExecuteNonQuery();
        }

        // A previous failed migrate may have left a stale row in
        // __EFMigrationsLock — drop the table so Migrate re-creates it
        // cleanly. Safe — the lock is per-application-instance and
        // we're single-instance here.
        using (var dropLock = conn.CreateCommand())
        {
            dropLock.CommandText = "DROP TABLE IF EXISTS __EFMigrationsLock;";
            dropLock.ExecuteNonQuery();
        }

        var stale = existingIds.Except(assemblyMigrations).Count();
        var added = assemblyMigrations.Except(existingIds).Count();
        Log.Information(
            "[migrations] Reconciled SQLite history: dropped {Stale} stale ids, baselined {Added} assembly migrations ({First} … {Last}).",
            stale, added, assemblyMigrations.First(), assemblyMigrations.Last());
    }
}
