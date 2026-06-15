using System.Data;
using System.Data.Common;
using DorkNet.Server.Controllers.Health;
using DorkNet.Server.Data;
using DorkNet.Server.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace DorkNet.Server.Startup;

/// <summary>Boot-time schema repair, seed data, and health readiness.</summary>
public static class DatabaseBootstrap
{
    public static async Task RunDatabaseBootstrapAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DorkNetDbContext>();

        if (db.Database.IsSqlite())
        {
            db.Database.EnsureCreated();
            ApplySqliteCompatibilityPatches(db);
            await EnsureSqliteInviteTablesAsync(db);
        }
        else
        {
            await BootstrapPostgresAsync(app, db);
        }

        var playerService = scope.ServiceProvider.GetRequiredService<PlayerService>();
        await playerService.EnsureSystemAccountAsync();
        await playerService.EnsureAvatarSeedAsync();
        await playerService.BackfillStarterWalletsAsync();

        var roomService = scope.ServiceProvider.GetRequiredService<RoomService>();
        await roomService.EnsureDormsForAllPlayersAsync();
        await roomService.SeedAsync();

        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
        {
            var imagesDir = Path.Combine(AppContext.BaseDirectory, "data", "images");
            await roomService.ApplyCanonicalOverridesAsync(http, imagesDir);
        }

        var storeService = scope.ServiceProvider.GetRequiredService<StoreService>();
        await storeService.SeedAsync();

        HealthController.MigrationsComplete = true;
    }

    private static async Task EnsureSqliteInviteTablesAsync(DorkNetDbContext db)
    {
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

    private static async Task BootstrapPostgresAsync(WebApplication app, DorkNetDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        var connectLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DorkNet.Server.Bootstrap");

        for (var attempt = 1; ; attempt++)
        {
            try { await conn.OpenAsync(); break; }
            catch (Exception ex) when (attempt < 15 &&
                (ex is System.Net.Sockets.SocketException
                 || ex is Npgsql.NpgsqlException
                 || ex is DbException))
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

        var bootstrapLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DorkNet.Server.Bootstrap");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "RoomDataBlobs.Bytes nullable",
            @"ALTER TABLE ""RoomDataBlobs"" ALTER COLUMN ""Bytes"" DROP NOT NULL;");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "ChatThreads table",
            @"CREATE TABLE IF NOT EXISTS ""ChatThreads"" (
                ""Id"" bigserial PRIMARY KEY,
                ""ThreadKey"" varchar(96) NOT NULL,
                ""Name"" varchar(128) NOT NULL DEFAULT '',
                ""CreatorPlayerId"" bigint NOT NULL,
                ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
                ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now()
            );");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "ChatThreads ThreadKey unique index",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ChatThreads_ThreadKey""
                ON ""ChatThreads"" (""ThreadKey"");");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "ChatThreadMembers table",
            @"CREATE TABLE IF NOT EXISTS ""ChatThreadMembers"" (
                ""Id"" bigserial PRIMARY KEY,
                ""ThreadKey"" text NOT NULL,
                ""PlayerId"" bigint NOT NULL,
                ""JoinedAt"" timestamp with time zone NOT NULL DEFAULT now(),
                ""SnoozeUntil"" timestamp with time zone NULL,
                ""LastReadMessageId"" bigint NULL
            );");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "ChatThreadMembers (ThreadKey, PlayerId) unique index",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_ChatThreadMembers_ThreadKey_PlayerId""
                ON ""ChatThreadMembers"" (""ThreadKey"", ""PlayerId"");");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "ChatThreadMembers PlayerId index",
            @"CREATE INDEX IF NOT EXISTS ""IX_ChatThreadMembers_PlayerId""
                ON ""ChatThreadMembers"" (""PlayerId"");");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "Relationships.Favorited column",
            @"ALTER TABLE ""Relationships""
                ADD COLUMN IF NOT EXISTS ""Favorited"" boolean NOT NULL DEFAULT false;");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "Relationships.Muted column",
            @"ALTER TABLE ""Relationships""
                ADD COLUMN IF NOT EXISTS ""Muted"" boolean NOT NULL DEFAULT false;");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "Relationships.Ignored column",
            @"ALTER TABLE ""Relationships""
                ADD COLUMN IF NOT EXISTS ""Ignored"" boolean NOT NULL DEFAULT false;");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "StoreItems.Slug widen to 128",
            @"ALTER TABLE ""StoreItems"" ALTER COLUMN ""Slug"" TYPE varchar(128);");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "Messages.Type column",
            @"ALTER TABLE ""Messages""
                ADD COLUMN IF NOT EXISTS ""Type"" integer NOT NULL DEFAULT 30;");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "Messages.RoomId column",
            @"ALTER TABLE ""Messages""
                ADD COLUMN IF NOT EXISTS ""RoomId"" bigint NULL;");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "PrivateInstanceInvitees.LatestInviteMessageId column",
            @"ALTER TABLE ""PrivateInstanceInvitees""
                ADD COLUMN IF NOT EXISTS ""LatestInviteMessageId"" bigint NULL;");

        var currentRegion = (app.Configuration["Photon:CloudRegion"] ?? "us").ToLowerInvariant().Replace("'", "''");
        await RunPostgresPatchAsync(conn, bootstrapLogger, $"PrivateInstances.PhotonRegion -> {currentRegion}",
            $@"UPDATE ""PrivateInstances""
               SET ""PhotonRegion"" = '{currentRegion}'
               WHERE ""PhotonRegion"" IS DISTINCT FROM '{currentRegion}';");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "CommunityBoardRows table",
            @"CREATE TABLE IF NOT EXISTS ""CommunityBoardRows"" (
                ""Id"" integer PRIMARY KEY,
                ""Json"" text NOT NULL DEFAULT '{}',
                ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now()
            );");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "ServerSettings table",
            @"CREATE TABLE IF NOT EXISTS ""ServerSettings"" (
                ""Id"" integer PRIMARY KEY,
                ""SignupsDisabled"" boolean NOT NULL DEFAULT false,
                ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now()
            );");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "ServerSettings.WeeklyChallengesCompletedRequired column",
            @"ALTER TABLE ""ServerSettings""
                ADD COLUMN IF NOT EXISTS ""WeeklyChallengesCompletedRequired"" boolean NOT NULL DEFAULT true;");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "ServerSettings.WeeklyChallengesJson column",
            @"ALTER TABLE ""ServerSettings""
                ADD COLUMN IF NOT EXISTS ""WeeklyChallengesJson"" text NOT NULL DEFAULT '';");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "ServerSettings.WeeklyChallengeRewardJson column",
            @"ALTER TABLE ""ServerSettings""
                ADD COLUMN IF NOT EXISTS ""WeeklyChallengeRewardJson"" text NOT NULL DEFAULT '';");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "ServerSettings.GlobalFriendsEnabled column",
            @"ALTER TABLE ""ServerSettings""
                ADD COLUMN IF NOT EXISTS ""GlobalFriendsEnabled"" boolean NOT NULL DEFAULT false;");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "Players.IsCommunityTeam column",
            @"ALTER TABLE ""Players""
                ADD COLUMN IF NOT EXISTS ""IsCommunityTeam"" boolean NOT NULL DEFAULT false;");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "Rooms.HiddenFromBrowse column",
            @"ALTER TABLE ""Rooms""
                ADD COLUMN IF NOT EXISTS ""HiddenFromBrowse"" boolean NOT NULL DEFAULT false;");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "RoomRoles table",
            @"CREATE TABLE IF NOT EXISTS ""RoomRoles"" (
                ""Id"" bigserial PRIMARY KEY,
                ""RoomId"" bigint NOT NULL,
                ""PlayerId"" bigint NOT NULL,
                ""Role"" integer NOT NULL,
                ""Accepted"" boolean NOT NULL DEFAULT true,
                ""GrantedByPlayerId"" bigint NULL,
                ""GrantedAt"" timestamp with time zone NOT NULL DEFAULT now()
            );");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "RoomRoles (RoomId, PlayerId, Role) unique index",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RoomRoles_RoomId_PlayerId_Role""
                ON ""RoomRoles"" (""RoomId"", ""PlayerId"", ""Role"");");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "RoomRoles RoomId index",
            @"CREATE INDEX IF NOT EXISTS ""IX_RoomRoles_RoomId"" ON ""RoomRoles"" (""RoomId"");");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "LeaderboardChannelMeta table",
            @"CREATE TABLE IF NOT EXISTS ""LeaderboardChannelMeta"" (
                ""Channel"" integer PRIMARY KEY,
                ""RoomId"" bigint NOT NULL DEFAULT 0,
                ""Name"" varchar(128) NOT NULL DEFAULT '',
                ""LowerIsBetter"" boolean NOT NULL DEFAULT false,
                ""ValueFormat"" varchar(32) NOT NULL DEFAULT 'count',
                ""CreatedAt"" timestamp with time zone NOT NULL DEFAULT now(),
                ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT now()
            );");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "LeaderboardChannelMeta RoomId index",
            @"CREATE INDEX IF NOT EXISTS ""IX_LeaderboardChannelMeta_RoomId""
                ON ""LeaderboardChannelMeta"" (""RoomId"");");

        await RunPostgresPatchAsync(conn, bootstrapLogger, "LoadingScreenTips table",
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

        await RunPostgresPatchAsync(conn, bootstrapLogger, "SignupCodes table",
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
        await RunPostgresPatchAsync(conn, bootstrapLogger, "SignupCodes Code unique index",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SignupCodes_Code"" ON ""SignupCodes"" (""Code"");");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "PendingDevices table",
            @"CREATE TABLE IF NOT EXISTS ""PendingDevices"" (
                ""Id"" bigserial PRIMARY KEY,
                ""DeviceId"" text NOT NULL,
                ""Platform"" integer NOT NULL DEFAULT 0,
                ""PlatformId"" text NOT NULL DEFAULT '',
                ""LastIp"" text NULL,
                ""FirstSeenAt"" timestamp with time zone NOT NULL DEFAULT now(),
                ""LastSeenAt"" timestamp with time zone NOT NULL DEFAULT now()
            );");
        await RunPostgresPatchAsync(conn, bootstrapLogger, "PendingDevices DeviceId unique index",
            @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PendingDevices_DeviceId"" ON ""PendingDevices"" (""DeviceId"");");

        await conn.CloseAsync();
    }

    private static async Task RunPostgresPatchAsync(DbConnection conn, Microsoft.Extensions.Logging.ILogger logger, string label, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        try { await cmd.ExecuteNonQueryAsync(); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[schema-patch] {Label} failed", label);
        }
    }

    private static void ApplySqliteCompatibilityPatches(DorkNetDbContext db)
    {
        if (!db.Database.IsSqlite()) return;
        AddSqliteColumnIfMissing(db, "Rooms", "HiddenFromBrowse",
            @"""HiddenFromBrowse"" INTEGER NOT NULL DEFAULT 0");
        AddSqliteColumnIfMissing(db, "ServerSettings", "WeeklyChallengesCompletedRequired",
            @"""WeeklyChallengesCompletedRequired"" INTEGER NOT NULL DEFAULT 1");
        AddSqliteColumnIfMissing(db, "ServerSettings", "WeeklyChallengesJson",
            @"""WeeklyChallengesJson"" TEXT NOT NULL DEFAULT ''");
        AddSqliteColumnIfMissing(db, "ServerSettings", "WeeklyChallengeRewardJson",
            @"""WeeklyChallengeRewardJson"" TEXT NOT NULL DEFAULT ''");
        AddSqliteColumnIfMissing(db, "ServerSettings", "GlobalFriendsEnabled",
            @"""GlobalFriendsEnabled"" INTEGER NOT NULL DEFAULT 0");
    }

    private static void AddSqliteColumnIfMissing(DorkNetDbContext db, string table, string column, string definition)
    {
        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State == ConnectionState.Closed;
        if (shouldClose) conn.Open();
        try
        {
            var exists = false;
            using var check = conn.CreateCommand();
            check.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\");";
            using (var reader = check.ExecuteReader())
            {
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
}
