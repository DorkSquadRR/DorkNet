using Microsoft.EntityFrameworkCore;
using System.Data;
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
            await ConfigureSqliteForLocalConcurrencyAsync(db);
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
        await EnsureServerSettingsColumnsAsync(db);
        await EnsureCharadesWordListTableAsync(db);
        await EnsureInventorySchemaAsync(db);
        await EnsureLeaderboardSchemaAsync(db);
        await EnsureRoomColumnsAsync(db);
        await EnsureRoomSceneColumnsAsync(db);
        await EnsureInventionColumnsAsync(db);
        await EnsureGameRewardColumnsAsync(db);
        await EnsureGiftPackageColumnsAsync(db);
        await EnsureAvatarColumnsAsync(db);
        await EnsureMarch2023TablesAsync(db);

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

        // Seed the three built-in 3D Charades word lists (Default / April
        // Fools / Icebreakers) and bind each client card-source slot to
        // them. Idempotent — no-ops once any list exists so admin edits
        // survive restarts.
        var charadesService = scope.ServiceProvider.GetRequiredService<CharadesWordListService>();
        await charadesService.SeedAsync();

        // Tell the /healthz probe migrations are done. Coolify's rolling
        // deploy holds traffic at the LB until this flips and /healthz
        // returns 200.
        Controllers.Health.HealthController.MigrationsComplete = true;
    }

    private static async Task ConfigureSqliteForLocalConcurrencyAsync(DorkNetDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        var closeAfter = conn.State != ConnectionState.Open;
        if (closeAfter) await conn.OpenAsync();

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            await cmd.ExecuteScalarAsync();

            cmd.CommandText = "PRAGMA busy_timeout=30000;";
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            if (closeAfter) await conn.CloseAsync();
        }
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

    /// <summary>Add post-Initial columns to the Rooms table. Like the other
    /// Ensure*Columns helpers, this exists because the Postgres path is
    /// EnsureCreated-only (no migration replay), so a new entity property
    /// won't appear on an existing DB without an idempotent ALTER.</summary>
    private static async Task EnsureRoomColumnsAsync(DorkNetDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN IF NOT EXISTS ""MaxCapacity"" integer NOT NULL DEFAULT 8;");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN IF NOT EXISTS ""IsStudioRoom"" boolean NOT NULL DEFAULT false;");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN IF NOT EXISTS ""IsRoomLinkedToRecRoomStudio"" boolean NOT NULL DEFAULT false;");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN IF NOT EXISTS ""StudioSessionId"" character varying(128) NOT NULL DEFAULT '';");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN IF NOT EXISTS ""AllowNewUsers"" boolean NOT NULL DEFAULT true;");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN IF NOT EXISTS ""MinLevel"" integer NOT NULL DEFAULT 0;");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN IF NOT EXISTS ""MaxPlayerCalculationMode"" integer NOT NULL DEFAULT 0;");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN IF NOT EXISTS ""LoadScreensJson"" character varying(4096) NOT NULL DEFAULT '';");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN IF NOT EXISTS ""PromoImagesJson"" character varying(4096) NOT NULL DEFAULT '';");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN IF NOT EXISTS ""PromoExternalContentJson"" character varying(4096) NOT NULL DEFAULT '';");
            return;
        }

        try
        {
            // SQLite has no ADD COLUMN IF NOT EXISTS; ignore the duplicate-
            // column error when it already exists.
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN ""MaxCapacity"" INTEGER NOT NULL DEFAULT 8;");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN ""IsStudioRoom"" INTEGER NOT NULL DEFAULT 0;");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN ""IsRoomLinkedToRecRoomStudio"" INTEGER NOT NULL DEFAULT 0;");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN ""StudioSessionId"" TEXT NOT NULL DEFAULT '';");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN ""AllowNewUsers"" INTEGER NOT NULL DEFAULT 1;");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN ""MinLevel"" INTEGER NOT NULL DEFAULT 0;");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN ""MaxPlayerCalculationMode"" INTEGER NOT NULL DEFAULT 0;");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN ""LoadScreensJson"" TEXT NOT NULL DEFAULT '';");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN ""PromoImagesJson"" TEXT NOT NULL DEFAULT '';");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Rooms"" ADD COLUMN ""PromoExternalContentJson"" TEXT NOT NULL DEFAULT '';");
        }
        catch { }
    }

    /// <summary>Add the 2023-client avatar columns (OutfitSelectionsV2 +
    /// CustomAvatarItemsJson on Avatars) — post-Initial, so existing DBs
    /// need an idempotent ALTER on both providers.</summary>
    private static async Task EnsureAvatarColumnsAsync(DorkNetDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Avatars"" ADD COLUMN IF NOT EXISTS ""OutfitSelectionsV2"" character varying(8192) NOT NULL DEFAULT '';");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Avatars"" ADD COLUMN IF NOT EXISTS ""CustomAvatarItemsJson"" text NOT NULL DEFAULT '[]';");
            return;
        }

        try
        {
            // SQLite has no ADD COLUMN IF NOT EXISTS; ignore the duplicate-
            // column error when it already exists.
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Avatars"" ADD COLUMN ""OutfitSelectionsV2"" TEXT NOT NULL DEFAULT '';");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Avatars"" ADD COLUMN ""CustomAvatarItemsJson"" TEXT NOT NULL DEFAULT '[]';");
        }
        catch { }
    }

    private static async Task EnsureInventionColumnsAsync(DorkNetDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Inventions"" ADD COLUMN IF NOT EXISTS ""Price"" integer NOT NULL DEFAULT 0;");
            return;
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""Inventions"" ADD COLUMN ""Price"" INTEGER NOT NULL DEFAULT 0;");
        }
        catch { }
    }

    /// <summary>Post-Initial columns on GameRewardSelections for the real
    /// item-reward flow (offered store-item ids + granted marker).</summary>
    private static async Task EnsureGameRewardColumnsAsync(DorkNetDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var col in new[] { "Offer1ItemId", "Offer2ItemId", "Offer3ItemId", "GrantedItemId" })
                await db.Database.ExecuteSqlRawAsync(
                    $@"ALTER TABLE ""GameRewardSelections"" ADD COLUMN IF NOT EXISTS ""{col}"" bigint NOT NULL DEFAULT 0;");
            return;
        }

        foreach (var col in new[] { "Offer1ItemId", "Offer2ItemId", "Offer3ItemId", "GrantedItemId" })
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    $@"ALTER TABLE ""GameRewardSelections"" ADD COLUMN ""{col}"" INTEGER NOT NULL DEFAULT 0;");
            }
            catch { }
        }
    }

    /// <summary>Post-Initial column on GiftPackages for quest-reward
    /// chests (the exact store item the gift grants on consume).</summary>
    private static async Task EnsureGiftPackageColumnsAsync(DorkNetDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""GiftPackages"" ADD COLUMN IF NOT EXISTS ""SourceStoreItemId"" bigint NOT NULL DEFAULT 0;");
            return;
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""GiftPackages"" ADD COLUMN ""SourceStoreItemId"" INTEGER NOT NULL DEFAULT 0;");
        }
        catch { }
    }

    private static async Task EnsureRoomSceneColumnsAsync(DorkNetDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""RoomScenes"" ADD COLUMN IF NOT EXISTS ""StudioSubRoomDataSaveId"" bigint NULL;");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""RoomScenes"" ADD COLUMN IF NOT EXISTS ""StudioUnityAssetId"" character varying(64) NOT NULL DEFAULT '';");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""RoomScenes"" ADD COLUMN IF NOT EXISTS ""StudioAssetBundleNamesCsv"" character varying(4096) NOT NULL DEFAULT '';");
            return;
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""RoomScenes"" ADD COLUMN ""StudioSubRoomDataSaveId"" INTEGER NULL;");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""RoomScenes"" ADD COLUMN ""StudioUnityAssetId"" TEXT NOT NULL DEFAULT '';");
        }
        catch { }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""RoomScenes"" ADD COLUMN ""StudioAssetBundleNamesCsv"" TEXT NOT NULL DEFAULT '';");
        }
        catch { }
    }

    private static async Task EnsureServerSettingsColumnsAsync(DorkNetDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN IF NOT EXISTS ""PlayMenuTagsJson"" text NOT NULL DEFAULT '';");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN IF NOT EXISTS ""RecCenterDoorsJson"" text NOT NULL DEFAULT '';");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN IF NOT EXISTS ""DiscoveredGameConfigsJson"" text NOT NULL DEFAULT '';");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN IF NOT EXISTS ""GlobalFriendsEnabled"" boolean NOT NULL DEFAULT false;");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN IF NOT EXISTS ""ProfanityFilterDisabled"" boolean NOT NULL DEFAULT false;");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN IF NOT EXISTS ""CharadesSlotBindingsJson"" text NOT NULL DEFAULT '';");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN IF NOT EXISTS ""AllAvatarItemsOwned"" boolean NOT NULL DEFAULT false;");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN IF NOT EXISTS ""BaseRoomNamesJson"" text NOT NULL DEFAULT '';");
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN IF NOT EXISTS ""RoomBlobVersionClampDisabled"" boolean NOT NULL DEFAULT false;");
            return;
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""PlayMenuTagsJson"" TEXT NOT NULL DEFAULT '';");
        }
        catch
        {
            // SQLite has no ADD COLUMN IF NOT EXISTS. If it already exists,
            // ignore the duplicate-column error; any other schema problem
            // will surface when EF reads ServerSettings.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""RecCenterDoorsJson"" TEXT NOT NULL DEFAULT '';");
        }
        catch
        {
            // SQLite has no ADD COLUMN IF NOT EXISTS. If it already exists,
            // ignore the duplicate-column error; any other schema problem
            // will surface when EF reads ServerSettings.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""DiscoveredGameConfigsJson"" TEXT NOT NULL DEFAULT '';");
        }
        catch
        {
            // SQLite has no ADD COLUMN IF NOT EXISTS. If it already exists,
            // ignore the duplicate-column error; any other schema problem
            // will surface when EF reads ServerSettings.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""GlobalFriendsEnabled"" INTEGER NOT NULL DEFAULT 0;");
        }
        catch
        {
            // SQLite has no ADD COLUMN IF NOT EXISTS. If it already exists,
            // ignore the duplicate-column error; any other schema problem
            // will surface when EF reads ServerSettings.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""ProfanityFilterDisabled"" INTEGER NOT NULL DEFAULT 0;");
        }
        catch
        {
            // SQLite has no ADD COLUMN IF NOT EXISTS. If it already exists,
            // ignore the duplicate-column error; any other schema problem
            // will surface when EF reads ServerSettings.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""CharadesSlotBindingsJson"" TEXT NOT NULL DEFAULT '';");
        }
        catch
        {
            // SQLite has no ADD COLUMN IF NOT EXISTS. If it already exists,
            // ignore the duplicate-column error; any other schema problem
            // will surface when EF reads ServerSettings.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""AllAvatarItemsOwned"" INTEGER NOT NULL DEFAULT 0;");
        }
        catch
        {
            // SQLite has no ADD COLUMN IF NOT EXISTS. If it already exists,
            // ignore the duplicate-column error; any other schema problem
            // will surface when EF reads ServerSettings.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""BaseRoomNamesJson"" TEXT NOT NULL DEFAULT '';");
        }
        catch
        {
            // SQLite has no ADD COLUMN IF NOT EXISTS. If it already exists,
            // ignore the duplicate-column error; any other schema problem
            // will surface when EF reads ServerSettings.
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""ServerSettings"" ADD COLUMN ""RoomBlobVersionClampDisabled"" INTEGER NOT NULL DEFAULT 0;");
        }
        catch
        {
            // SQLite has no ADD COLUMN IF NOT EXISTS. If it already exists,
            // ignore the duplicate-column error; any other schema problem
            // will surface when EF reads ServerSettings.
        }
    }

    private static async Task EnsureInventorySchemaAsync(DorkNetDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""PlayerInventory"" ALTER COLUMN ""ItemSlug"" TYPE character varying(128);");
        }
    }

    private static async Task EnsureLeaderboardSchemaAsync(DorkNetDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""LeaderboardStats"" ADD COLUMN IF NOT EXISTS ""RoomId"" bigint NOT NULL DEFAULT 0;");
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE "LeaderboardStats"
                SET "RoomId" = COALESCE((
                    SELECT "RoomId"
                    FROM "LeaderboardChannelMeta"
                    WHERE "LeaderboardChannelMeta"."Channel" = "LeaderboardStats"."StatChannel"
                    LIMIT 1
                ), 0)
                WHERE "RoomId" = 0;
                """);
            await db.Database.ExecuteSqlRawAsync(
                @"DROP INDEX IF EXISTS ""IX_LeaderboardStats_PlayerId_StatChannel"";");
            await db.Database.ExecuteSqlRawAsync(
                @"DROP INDEX IF EXISTS ""IX_LeaderboardStats_StatChannel_Value"";");
            await db.Database.ExecuteSqlRawAsync(
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LeaderboardStats_RoomId_PlayerId_StatChannel""
                  ON ""LeaderboardStats"" (""RoomId"", ""PlayerId"", ""StatChannel"");");
            await db.Database.ExecuteSqlRawAsync(
                @"CREATE INDEX IF NOT EXISTS ""IX_LeaderboardStats_RoomId_StatChannel_Value""
                  ON ""LeaderboardStats"" (""RoomId"", ""StatChannel"", ""Value"");");

            await db.Database.ExecuteSqlRawAsync(
                """
                DO $$
                DECLARE
                    pk_cols text[];
                    pk record;
                BEGIN
                    FOR pk IN
                        SELECT c.conname, array_agg(a.attname::text ORDER BY x.ordinality) AS cols
                        FROM pg_constraint c
                        JOIN unnest(c.conkey) WITH ORDINALITY AS x(attnum, ordinality) ON true
                        JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = x.attnum
                        WHERE c.conrelid = '"LeaderboardChannelMeta"'::regclass
                          AND c.contype = 'p'
                        GROUP BY c.conname
                    LOOP
                        IF pk.cols IS DISTINCT FROM ARRAY['RoomId', 'Channel'] THEN
                            EXECUTE format('ALTER TABLE %I DROP CONSTRAINT %I', 'LeaderboardChannelMeta', pk.conname);
                        END IF;
                    END LOOP;

                    SELECT array_agg(a.attname::text ORDER BY x.ordinality)
                    INTO pk_cols
                    FROM pg_constraint c
                    JOIN unnest(c.conkey) WITH ORDINALITY AS x(attnum, ordinality) ON true
                    JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = x.attnum
                    WHERE c.conrelid = '"LeaderboardChannelMeta"'::regclass
                      AND c.contype = 'p';

                    IF pk_cols IS DISTINCT FROM ARRAY['RoomId', 'Channel'] THEN
                        ALTER TABLE "LeaderboardChannelMeta"
                        ADD CONSTRAINT "PK_LeaderboardChannelMeta" PRIMARY KEY ("RoomId", "Channel");
                    END IF;
                END $$;
                """);
            return;
        }

        try
        {
            await db.Database.ExecuteSqlRawAsync(
                @"ALTER TABLE ""LeaderboardStats"" ADD COLUMN ""RoomId"" INTEGER NOT NULL DEFAULT 0;");
        }
        catch
        {
            // SQLite has no ADD COLUMN IF NOT EXISTS. Ignore duplicate
            // column errors; later queries verify the resulting shape.
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE "LeaderboardStats"
            SET "RoomId" = COALESCE((
                SELECT "RoomId"
                FROM "LeaderboardChannelMeta"
                WHERE "LeaderboardChannelMeta"."Channel" = "LeaderboardStats"."StatChannel"
                LIMIT 1
            ), 0)
            WHERE "RoomId" = 0;
            """);
        await db.Database.ExecuteSqlRawAsync(
            @"DROP INDEX IF EXISTS ""IX_LeaderboardStats_PlayerId_StatChannel"";");
        await db.Database.ExecuteSqlRawAsync(
            @"DROP INDEX IF EXISTS ""IX_LeaderboardStats_StatChannel_Value"";");
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "__LeaderboardChannelMeta_new" (
                "RoomId" INTEGER NOT NULL,
                "Channel" INTEGER NOT NULL,
                "Name" TEXT NOT NULL,
                "LowerIsBetter" INTEGER NOT NULL,
                "ValueFormat" TEXT NOT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "PK_LeaderboardChannelMeta" PRIMARY KEY ("RoomId", "Channel")
            );
            """);
        await db.Database.ExecuteSqlRawAsync(@"DELETE FROM ""__LeaderboardChannelMeta_new"";");
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT OR REPLACE INTO "__LeaderboardChannelMeta_new"
                ("RoomId", "Channel", "Name", "LowerIsBetter", "ValueFormat", "CreatedAt", "UpdatedAt")
            SELECT "RoomId", "Channel", "Name", "LowerIsBetter", "ValueFormat", "CreatedAt", "UpdatedAt"
            FROM "LeaderboardChannelMeta";
            """);
        await db.Database.ExecuteSqlRawAsync(@"DROP TABLE ""LeaderboardChannelMeta"";");
        await db.Database.ExecuteSqlRawAsync(
            @"ALTER TABLE ""__LeaderboardChannelMeta_new"" RENAME TO ""LeaderboardChannelMeta"";");
        await db.Database.ExecuteSqlRawAsync(
            @"CREATE INDEX IF NOT EXISTS ""IX_LeaderboardChannelMeta_RoomId"" ON ""LeaderboardChannelMeta"" (""RoomId"");");
        await db.Database.ExecuteSqlRawAsync(
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_LeaderboardStats_RoomId_PlayerId_StatChannel"" ON ""LeaderboardStats"" (""RoomId"", ""PlayerId"", ""StatChannel"");");
        await db.Database.ExecuteSqlRawAsync(
            @"CREATE INDEX IF NOT EXISTS ""IX_LeaderboardStats_RoomId_StatChannel_Value"" ON ""LeaderboardStats"" (""RoomId"", ""StatChannel"", ""Value"");");
    }

    /// <summary>Idempotently create the CharadesWordLists table on both
    /// providers. Post-dates the consolidated Initial migration (same
    /// rationale as <see cref="EnsureSignupCodeTablesAsync"/>): Postgres
    /// is EnsureCreated-only and the SQLite Initial migration doesn't
    /// include it, so we create it here with CREATE TABLE IF NOT EXISTS
    /// (a no-op on a fresh DB where EnsureCreated/convention already
    /// built it).</summary>
    private static async Task EnsureCharadesWordListTableAsync(DorkNetDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        var pg = provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
        var sql = pg
            ? """
              CREATE TABLE IF NOT EXISTS "CharadesWordLists" (
                  "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                  "Name" text NOT NULL DEFAULT '',
                  "WordsJson" text NOT NULL DEFAULT '[]',
                  "IsBuiltIn" boolean NOT NULL DEFAULT false,
                  "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                  "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
              );
              """
            : """
              CREATE TABLE IF NOT EXISTS "CharadesWordLists" (
                  "Id" INTEGER NOT NULL CONSTRAINT "PK_CharadesWordLists" PRIMARY KEY AUTOINCREMENT,
                  "Name" TEXT NOT NULL DEFAULT '',
                  "WordsJson" TEXT NOT NULL DEFAULT '[]',
                  "IsBuiltIn" INTEGER NOT NULL DEFAULT 0,
                  "CreatedAt" TEXT NOT NULL,
                  "UpdatedAt" TEXT NOT NULL
              );
              """;
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task EnsureMarch2023TablesAsync(DorkNetDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            var statements = new[]
            {
                """
                CREATE TABLE IF NOT EXISTS "CustomAvatarItems" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "PublicId" uuid NOT NULL,
                    "CreatorPlayerId" bigint NOT NULL,
                    "Name" character varying(128) NOT NULL,
                    "Description" character varying(1024) NOT NULL,
                    "Price" integer NOT NULL DEFAULT 100,
                    "ItemType" integer NOT NULL DEFAULT 0,
                    "BaseAvatarItemId" integer NOT NULL DEFAULT 0,
                    "Color" character varying(64) NOT NULL DEFAULT '',
                    "ImageName" character varying(256) NOT NULL DEFAULT '',
                    "AssetName" character varying(256) NOT NULL DEFAULT '',
                    "IsPublic" boolean NOT NULL DEFAULT false,
                    "IsFeatured" boolean NOT NULL DEFAULT false,
                    "CheerCount" bigint NOT NULL DEFAULT 0,
                    "PurchaseCount" bigint NOT NULL DEFAULT 0,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
                );
                """,
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CustomAvatarItems_PublicId"" ON ""CustomAvatarItems"" (""PublicId"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_CustomAvatarItems_CreatorPlayerId"" ON ""CustomAvatarItems"" (""CreatorPlayerId"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_CustomAvatarItems_IsPublic"" ON ""CustomAvatarItems"" (""IsPublic"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_CustomAvatarItems_IsFeatured"" ON ""CustomAvatarItems"" (""IsFeatured"");",
                """
                CREATE TABLE IF NOT EXISTS "CustomAvatarItemOwnership" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "PlayerId" bigint NOT NULL,
                    "CustomAvatarItemId" bigint NOT NULL,
                    "AcquiredAt" timestamp with time zone NOT NULL DEFAULT now()
                );
                """,
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CustomAvatarItemOwnership_PlayerId_CustomAvatarItemId"" ON ""CustomAvatarItemOwnership"" (""PlayerId"", ""CustomAvatarItemId"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_CustomAvatarItemOwnership_CustomAvatarItemId"" ON ""CustomAvatarItemOwnership"" (""CustomAvatarItemId"");",
                """
                CREATE TABLE IF NOT EXISTS "ItemWishlists" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "PlayerId" bigint NOT NULL,
                    "ItemKey" character varying(128) NOT NULL,
                    "ItemType" integer NOT NULL DEFAULT 0,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now()
                );
                """,
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ItemWishlists_PlayerId_ItemKey_ItemType"" ON ""ItemWishlists"" (""PlayerId"", ""ItemKey"", ""ItemType"");",
                """
                CREATE TABLE IF NOT EXISTS "Keepsakes" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "PlayerId" bigint NOT NULL,
                    "Category" character varying(64) NOT NULL DEFAULT 'general',
                    "EventKey" character varying(128) NOT NULL DEFAULT '',
                    "Title" character varying(128) NOT NULL DEFAULT '',
                    "Description" character varying(1024) NOT NULL DEFAULT '',
                    "ImageName" character varying(256) NOT NULL DEFAULT '',
                    "EarnedAt" timestamp with time zone NOT NULL DEFAULT now()
                );
                """,
                @"CREATE INDEX IF NOT EXISTS ""IX_Keepsakes_PlayerId"" ON ""Keepsakes"" (""PlayerId"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_Keepsakes_PlayerId_EventKey"" ON ""Keepsakes"" (""PlayerId"", ""EventKey"");",
                """
                CREATE TABLE IF NOT EXISTS "RoomCurrencies" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "PublicId" uuid NOT NULL,
                    "RoomId" bigint NOT NULL,
                    "CreatorPlayerId" bigint NOT NULL,
                    "Name" character varying(64) NOT NULL DEFAULT '',
                    "Description" character varying(512) NOT NULL DEFAULT '',
                    "ImageName" character varying(256) NOT NULL DEFAULT '',
                    "DailyLimit" integer NOT NULL DEFAULT 0,
                    "IsDeleted" boolean NOT NULL DEFAULT false,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
                );
                """,
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RoomCurrencies_PublicId"" ON ""RoomCurrencies"" (""PublicId"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_RoomCurrencies_RoomId"" ON ""RoomCurrencies"" (""RoomId"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_RoomCurrencies_CreatorPlayerId"" ON ""RoomCurrencies"" (""CreatorPlayerId"");",
                """
                CREATE TABLE IF NOT EXISTS "RoomCurrencyBalances" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "PlayerId" bigint NOT NULL,
                    "RoomCurrencyId" bigint NOT NULL,
                    "Balance" bigint NOT NULL DEFAULT 0,
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
                );
                """,
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RoomCurrencyBalances_PlayerId_RoomCurrencyId"" ON ""RoomCurrencyBalances"" (""PlayerId"", ""RoomCurrencyId"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_RoomCurrencyBalances_RoomCurrencyId"" ON ""RoomCurrencyBalances"" (""RoomCurrencyId"");",
                """
                CREATE TABLE IF NOT EXISTS "RoomCurrencyPurchaseOffers" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "PublicId" uuid NOT NULL,
                    "RoomCurrencyId" bigint NOT NULL,
                    "Name" character varying(64) NOT NULL DEFAULT '',
                    "Amount" integer NOT NULL DEFAULT 0,
                    "Price" integer NOT NULL DEFAULT 0,
                    "CurrencyType" integer NOT NULL DEFAULT 2,
                    "IsDeleted" boolean NOT NULL DEFAULT false,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
                );
                """,
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RoomCurrencyPurchaseOffers_PublicId"" ON ""RoomCurrencyPurchaseOffers"" (""PublicId"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_RoomCurrencyPurchaseOffers_RoomCurrencyId"" ON ""RoomCurrencyPurchaseOffers"" (""RoomCurrencyId"");",
                """
                CREATE TABLE IF NOT EXISTS "UgcPurchasables" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "PublicId" uuid NOT NULL,
                    "RoomId" bigint NOT NULL,
                    "CreatorPlayerId" bigint NOT NULL,
                    "Name" character varying(128) NOT NULL DEFAULT '',
                    "Description" character varying(1024) NOT NULL DEFAULT '',
                    "ImageName" character varying(256) NOT NULL DEFAULT '',
                    "Price" integer NOT NULL DEFAULT 0,
                    "CurrencyType" integer NOT NULL DEFAULT 2,
                    "ItemType" integer NOT NULL DEFAULT 0,
                    "IsFeatured" boolean NOT NULL DEFAULT false,
                    "SortOrder" integer NOT NULL DEFAULT 0,
                    "IsDeleted" boolean NOT NULL DEFAULT false,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
                );
                """,
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_UgcPurchasables_PublicId"" ON ""UgcPurchasables"" (""PublicId"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_UgcPurchasables_RoomId"" ON ""UgcPurchasables"" (""RoomId"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_UgcPurchasables_CreatorPlayerId"" ON ""UgcPurchasables"" (""CreatorPlayerId"");",
                """
                CREATE TABLE IF NOT EXISTS "RoomConsumables" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "PublicId" uuid NOT NULL,
                    "RoomId" bigint NOT NULL,
                    "CreatorPlayerId" bigint NOT NULL,
                    "Name" character varying(128) NOT NULL DEFAULT '',
                    "Description" character varying(1024) NOT NULL DEFAULT '',
                    "ImageName" character varying(256) NOT NULL DEFAULT '',
                    "Price" bigint NOT NULL DEFAULT 0,
                    "CurrencyId" uuid NULL,
                    "IsDeleted" boolean NOT NULL DEFAULT false,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now()
                );
                """,
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RoomConsumables_PublicId"" ON ""RoomConsumables"" (""PublicId"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_RoomConsumables_RoomId"" ON ""RoomConsumables"" (""RoomId"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_RoomConsumables_CreatorPlayerId"" ON ""RoomConsumables"" (""CreatorPlayerId"");",
                """
                CREATE TABLE IF NOT EXISTS "RoomConsumableOwnership" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
                    "PlayerId" bigint NOT NULL,
                    "RoomConsumableId" bigint NOT NULL,
                    "Count" integer NOT NULL DEFAULT 0,
                    "ConcurrencyCode" uuid NOT NULL,
                    "ModifiedAt" timestamp with time zone NOT NULL DEFAULT now()
                );
                """,
                @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RoomConsumableOwnership_PlayerId_RoomConsumableId"" ON ""RoomConsumableOwnership"" (""PlayerId"", ""RoomConsumableId"");",
                @"CREATE INDEX IF NOT EXISTS ""IX_RoomConsumableOwnership_RoomConsumableId"" ON ""RoomConsumableOwnership"" (""RoomConsumableId"");",
            };

            foreach (var sql in statements)
                await db.Database.ExecuteSqlRawAsync(sql);
            return;
        }

        var sqliteStatements = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS "CustomAvatarItems" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CustomAvatarItems" PRIMARY KEY AUTOINCREMENT,
                "PublicId" TEXT NOT NULL,
                "CreatorPlayerId" INTEGER NOT NULL,
                "Name" TEXT NOT NULL,
                "Description" TEXT NOT NULL,
                "Price" INTEGER NOT NULL DEFAULT 100,
                "ItemType" INTEGER NOT NULL DEFAULT 0,
                "BaseAvatarItemId" INTEGER NOT NULL DEFAULT 0,
                "Color" TEXT NOT NULL DEFAULT '',
                "ImageName" TEXT NOT NULL DEFAULT '',
                "AssetName" TEXT NOT NULL DEFAULT '',
                "IsPublic" INTEGER NOT NULL DEFAULT 0,
                "IsFeatured" INTEGER NOT NULL DEFAULT 0,
                "CheerCount" INTEGER NOT NULL DEFAULT 0,
                "PurchaseCount" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """,
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CustomAvatarItems_PublicId"" ON ""CustomAvatarItems"" (""PublicId"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_CustomAvatarItems_CreatorPlayerId"" ON ""CustomAvatarItems"" (""CreatorPlayerId"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_CustomAvatarItems_IsPublic"" ON ""CustomAvatarItems"" (""IsPublic"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_CustomAvatarItems_IsFeatured"" ON ""CustomAvatarItems"" (""IsFeatured"");",
            """
            CREATE TABLE IF NOT EXISTS "CustomAvatarItemOwnership" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CustomAvatarItemOwnership" PRIMARY KEY AUTOINCREMENT,
                "PlayerId" INTEGER NOT NULL,
                "CustomAvatarItemId" INTEGER NOT NULL,
                "AcquiredAt" TEXT NOT NULL
            );
            """,
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CustomAvatarItemOwnership_PlayerId_CustomAvatarItemId"" ON ""CustomAvatarItemOwnership"" (""PlayerId"", ""CustomAvatarItemId"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_CustomAvatarItemOwnership_CustomAvatarItemId"" ON ""CustomAvatarItemOwnership"" (""CustomAvatarItemId"");",
            """
            CREATE TABLE IF NOT EXISTS "ItemWishlists" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_ItemWishlists" PRIMARY KEY AUTOINCREMENT,
                "PlayerId" INTEGER NOT NULL,
                "ItemKey" TEXT NOT NULL,
                "ItemType" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL
            );
            """,
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ItemWishlists_PlayerId_ItemKey_ItemType"" ON ""ItemWishlists"" (""PlayerId"", ""ItemKey"", ""ItemType"");",
            """
            CREATE TABLE IF NOT EXISTS "Keepsakes" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Keepsakes" PRIMARY KEY AUTOINCREMENT,
                "PlayerId" INTEGER NOT NULL,
                "Category" TEXT NOT NULL DEFAULT 'general',
                "EventKey" TEXT NOT NULL DEFAULT '',
                "Title" TEXT NOT NULL DEFAULT '',
                "Description" TEXT NOT NULL DEFAULT '',
                "ImageName" TEXT NOT NULL DEFAULT '',
                "EarnedAt" TEXT NOT NULL
            );
            """,
            @"CREATE INDEX IF NOT EXISTS ""IX_Keepsakes_PlayerId"" ON ""Keepsakes"" (""PlayerId"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_Keepsakes_PlayerId_EventKey"" ON ""Keepsakes"" (""PlayerId"", ""EventKey"");",
            """
            CREATE TABLE IF NOT EXISTS "RoomCurrencies" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_RoomCurrencies" PRIMARY KEY AUTOINCREMENT,
                "PublicId" TEXT NOT NULL,
                "RoomId" INTEGER NOT NULL,
                "CreatorPlayerId" INTEGER NOT NULL,
                "Name" TEXT NOT NULL DEFAULT '',
                "Description" TEXT NOT NULL DEFAULT '',
                "ImageName" TEXT NOT NULL DEFAULT '',
                "DailyLimit" INTEGER NOT NULL DEFAULT 0,
                "IsDeleted" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """,
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RoomCurrencies_PublicId"" ON ""RoomCurrencies"" (""PublicId"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_RoomCurrencies_RoomId"" ON ""RoomCurrencies"" (""RoomId"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_RoomCurrencies_CreatorPlayerId"" ON ""RoomCurrencies"" (""CreatorPlayerId"");",
            """
            CREATE TABLE IF NOT EXISTS "RoomCurrencyBalances" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_RoomCurrencyBalances" PRIMARY KEY AUTOINCREMENT,
                "PlayerId" INTEGER NOT NULL,
                "RoomCurrencyId" INTEGER NOT NULL,
                "Balance" INTEGER NOT NULL DEFAULT 0,
                "UpdatedAt" TEXT NOT NULL
            );
            """,
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RoomCurrencyBalances_PlayerId_RoomCurrencyId"" ON ""RoomCurrencyBalances"" (""PlayerId"", ""RoomCurrencyId"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_RoomCurrencyBalances_RoomCurrencyId"" ON ""RoomCurrencyBalances"" (""RoomCurrencyId"");",
            """
            CREATE TABLE IF NOT EXISTS "RoomCurrencyPurchaseOffers" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_RoomCurrencyPurchaseOffers" PRIMARY KEY AUTOINCREMENT,
                "PublicId" TEXT NOT NULL,
                "RoomCurrencyId" INTEGER NOT NULL,
                "Name" TEXT NOT NULL DEFAULT '',
                "Amount" INTEGER NOT NULL DEFAULT 0,
                "Price" INTEGER NOT NULL DEFAULT 0,
                "CurrencyType" INTEGER NOT NULL DEFAULT 2,
                "IsDeleted" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """,
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RoomCurrencyPurchaseOffers_PublicId"" ON ""RoomCurrencyPurchaseOffers"" (""PublicId"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_RoomCurrencyPurchaseOffers_RoomCurrencyId"" ON ""RoomCurrencyPurchaseOffers"" (""RoomCurrencyId"");",
            """
            CREATE TABLE IF NOT EXISTS "UgcPurchasables" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_UgcPurchasables" PRIMARY KEY AUTOINCREMENT,
                "PublicId" TEXT NOT NULL,
                "RoomId" INTEGER NOT NULL,
                "CreatorPlayerId" INTEGER NOT NULL,
                "Name" TEXT NOT NULL DEFAULT '',
                "Description" TEXT NOT NULL DEFAULT '',
                "ImageName" TEXT NOT NULL DEFAULT '',
                "Price" INTEGER NOT NULL DEFAULT 0,
                "CurrencyType" INTEGER NOT NULL DEFAULT 2,
                "ItemType" INTEGER NOT NULL DEFAULT 0,
                "IsFeatured" INTEGER NOT NULL DEFAULT 0,
                "SortOrder" INTEGER NOT NULL DEFAULT 0,
                "IsDeleted" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """,
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_UgcPurchasables_PublicId"" ON ""UgcPurchasables"" (""PublicId"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_UgcPurchasables_RoomId"" ON ""UgcPurchasables"" (""RoomId"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_UgcPurchasables_CreatorPlayerId"" ON ""UgcPurchasables"" (""CreatorPlayerId"");",
            """
            CREATE TABLE IF NOT EXISTS "RoomConsumables" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_RoomConsumables" PRIMARY KEY AUTOINCREMENT,
                "PublicId" TEXT NOT NULL,
                "RoomId" INTEGER NOT NULL,
                "CreatorPlayerId" INTEGER NOT NULL,
                "Name" TEXT NOT NULL DEFAULT '',
                "Description" TEXT NOT NULL DEFAULT '',
                "ImageName" TEXT NOT NULL DEFAULT '',
                "Price" INTEGER NOT NULL DEFAULT 0,
                "CurrencyId" TEXT NULL,
                "IsDeleted" INTEGER NOT NULL DEFAULT 0,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL
            );
            """,
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RoomConsumables_PublicId"" ON ""RoomConsumables"" (""PublicId"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_RoomConsumables_RoomId"" ON ""RoomConsumables"" (""RoomId"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_RoomConsumables_CreatorPlayerId"" ON ""RoomConsumables"" (""CreatorPlayerId"");",
            """
            CREATE TABLE IF NOT EXISTS "RoomConsumableOwnership" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_RoomConsumableOwnership" PRIMARY KEY AUTOINCREMENT,
                "PlayerId" INTEGER NOT NULL,
                "RoomConsumableId" INTEGER NOT NULL,
                "Count" INTEGER NOT NULL DEFAULT 0,
                "ConcurrencyCode" TEXT NOT NULL,
                "ModifiedAt" TEXT NOT NULL
            );
            """,
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RoomConsumableOwnership_PlayerId_RoomConsumableId"" ON ""RoomConsumableOwnership"" (""PlayerId"", ""RoomConsumableId"");",
            @"CREATE INDEX IF NOT EXISTS ""IX_RoomConsumableOwnership_RoomConsumableId"" ON ""RoomConsumableOwnership"" (""RoomConsumableId"");",
        };

        foreach (var sql in sqliteStatements)
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
