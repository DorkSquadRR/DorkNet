using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using DorkNet.Server.Data;

namespace DorkNet.Server.Tests;

/// <summary>
/// Guards the two ways a column reaches a database that already exists, because
/// getting either one wrong is invisible until a live server starts returning
/// 500s.
///
/// Every other test in this suite runs against a FRESH database, where
/// <c>EnsureCreated</c> builds the whole schema straight from the entity model —
/// so every column always exists and none of this is exercised. Only an
/// *existing* database can be missing a column, and that is exactly what took
/// down club home, matchmake/dorm, the presence heartbeat and the chat inbox at
/// once: three columns were added to the entity model with an EF migration, but
/// the Postgres path never replays migrations, so a deployed database never got
/// them.
///
/// The two mechanisms, and the invariant each test asserts:
///
/// <list type="number">
/// <item><b>SQLite</b> applies committed EF migrations via <c>Database.Migrate()</c>.
/// So the migration history alone must be able to build the current model.</item>
/// <item><b>Postgres</b> is <c>EnsureCreated</c>-only and NEVER replays
/// migrations. A database created before a column existed only gets it from an
/// idempotent <c>Ensure*</c> ALTER in <see cref="DorkNet.Server.Startup"/>'s
/// DatabaseBootstrap. So anything added after the Initial migration must appear
/// there too.</item>
/// </list>
/// </summary>
public sealed class SchemaBootstrapParityTests
{
    /// <summary>Every table and column in the entity model must be reachable on
    /// a database that already exists — via a committed migration, an
    /// idempotent <c>Ensure*</c> patch, or both.
    ///
    /// That union is exactly what boot does: SQLite runs <c>Database.Migrate()</c>
    /// and then the <c>Ensure*</c> helpers, and Postgres runs the helpers alone.
    /// A property with NEITHER reaches only brand-new databases, and every
    /// query touching it throws at runtime on a deployed server.
    ///
    /// The union is asserted rather than migrations alone because a large amount
    /// of the schema legitimately predates this rule and lives only in the
    /// helpers. Tightening that up is a separate cleanup; what must never happen
    /// is a column covered by nothing.</summary>
    [Fact]
    public async Task Every_model_column_reaches_an_existing_database()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<DorkNetDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new DorkNetDbContext(options);
        await db.Database.MigrateAsync();

        var actual = await ReadSqliteSchemaAsync(connection);
        var (patchedTables, patchedColumns) = ReadBootstrapPatches(postgresOnly: false);
        var missing = new List<string>();

        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (string.IsNullOrEmpty(table)) continue;

            var migrated = actual.TryGetValue(table, out var columns);
            if (!migrated && !patchedTables.Contains(table))
            {
                missing.Add($"{table} (whole table)");
                continue;
            }
            // A table the helpers create carries its own columns in the same
            // CREATE, so only migrated tables need a per-column check.
            if (!migrated) continue;

            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName();
                if (string.IsNullOrEmpty(column)) continue;
                if (columns!.Contains(column)) continue;
                if (patchedColumns.Contains($"{table}.{column}")) continue;
                missing.Add($"{table}.{column}");
            }
        }

        Assert.True(missing.Count == 0,
            $"""
             {missing.Count} model table(s)/column(s) are covered by NOTHING.

             They exist on a freshly-created database (EnsureCreated builds the
             whole model) but never appear on one that already exists, so a
             deployed server throws "no such column" / "column does not exist"
             on every request that touches them.

             Add a migration AND an idempotent ALTER in an Ensure*Async helper in
             DorkNet.Server/Startup/DatabaseBootstrap.cs for:

             {string.Join(Environment.NewLine, missing.Order(StringComparer.Ordinal))}
             """);
    }

    /// <summary>Every column added AFTER the Initial migration must also be
    /// added by an idempotent Postgres <c>ALTER</c> in DatabaseBootstrap.
    ///
    /// Postgres never replays migrations, so a migration alone reaches only
    /// brand-new databases. A production database created before the column
    /// existed gets it from the Ensure* helpers or not at all — the exact hole
    /// that broke matchmake/dorm (PrivateInstances.RoomCode),
    /// club/home/me (Clubs.ClubChatEnabled) and the chat inbox
    /// (ChatMessages.ModerationState).</summary>
    [Fact]
    public void Post_initial_columns_are_patched_for_existing_postgres_databases()
    {
        var migrations = FindRepoDirectory("DorkNet.Server", "Migrations");
        var (patchedTables, patchedColumns) = ReadBootstrapPatches(postgresOnly: true);

        // Anything AddColumn'd by a migration other than Initial post-dates the
        // baseline, so an existing Postgres database will not have it.
        var gaps = new List<string>();
        foreach (var file in Directory.GetFiles(migrations, "*.cs"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.Contains("Designer", StringComparison.Ordinal)) continue;
            if (name.Contains("Initial", StringComparison.Ordinal)) continue;
            if (name.Contains("ModelSnapshot", StringComparison.Ordinal)) continue;

            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text,
                         @"AddColumn<[^>]+>\(\s*name:\s*""(?<column>\w+)""\s*,\s*table:\s*""(?<table>\w+)""",
                         RegexOptions.Singleline))
            {
                var table = m.Groups["table"].Value;
                var column = m.Groups["column"].Value;
                if (patchedTables.Contains(table)) continue;      // whole table is created by a patch
                if (patchedColumns.Contains($"{table}.{column}")) continue;
                gaps.Add($"{table}.{column}  (added by {name})");
            }

            foreach (Match m in Regex.Matches(text,
                         @"CreateTable\(\s*name:\s*""(?<table>\w+)""",
                         RegexOptions.Singleline))
            {
                var table = m.Groups["table"].Value;
                if (!patchedTables.Contains(table))
                    gaps.Add($"{table} (whole table, created by {name})");
            }
        }

        Assert.True(gaps.Count == 0,
            $"""
             {gaps.Count} schema change(s) reach new databases only.

             The Postgres path is EnsureCreated-only and never replays
             migrations, so a migration alone does NOT add these to a database
             that already exists — a deployed server will throw
             "column does not exist" on every request that touches them.

             Add an idempotent ALTER/CREATE to an Ensure*Async helper in
             DorkNet.Server/Startup/DatabaseBootstrap.cs for:

             {string.Join(Environment.NewLine, gaps.Order(StringComparer.Ordinal))}
             """);
    }

    /// <summary>Tables and columns the <c>Ensure*</c> helpers add by raw SQL.
    ///
    /// The helpers are private, so this reads their statements out of the source
    /// rather than executing them. In that source the SQL sits inside verbatim
    /// strings, so a quoted identifier appears as two literal quote characters
    /// (<c>""Playlists""</c>).
    ///
    /// <paramref name="postgresOnly"/> keys off <c>IF NOT EXISTS</c>: the
    /// Postgres branch uses it, while the SQLite branch omits it and swallows
    /// the duplicate-column error instead. So requiring it isolates the branch
    /// that an existing Postgres database actually depends on.</summary>
    private static (HashSet<string> Tables, HashSet<string> Columns) ReadBootstrapPatches(
        bool postgresOnly)
    {
        var bootstrap = Path.Combine(
            FindRepoDirectory("DorkNet.Server", "Startup"), "DatabaseBootstrap.cs");
        Assert.True(File.Exists(bootstrap), $"missing {bootstrap}");
        var sql = File.ReadAllText(bootstrap);

        // The source mixes both C# string styles, so a quoted identifier shows
        // up as either two quote characters (verbatim, @"... ""X"" ...") or one
        // (raw, """... "X" ..."""). Accept either.
        const string Q = "\"{1,2}";
        var ifNotExists = postgresOnly ? @"IF\s+NOT\s+EXISTS\s+" : @"(?:IF\s+NOT\s+EXISTS\s+)?";

        var columns = Regex
            .Matches(sql,
                $@"ALTER\s+TABLE\s+{Q}(?<table>\w+){Q}\s+ADD\s+COLUMN\s+{ifNotExists}{Q}(?<column>\w+){Q}",
                RegexOptions.IgnoreCase)
            .Select(m => $"{m.Groups["table"].Value}.{m.Groups["column"].Value}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tables = Regex
            .Matches(sql,
                $@"CREATE\s+TABLE\s+IF\s+NOT\s+EXISTS\s+{Q}(?<table>\w+){Q}",
                RegexOptions.IgnoreCase)
            .Select(m => m.Groups["table"].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Some helpers loop a column-name array through one interpolated ALTER
        // (EnsureGameRewardColumnsAsync does). The statement alone only shows
        // "{col}", so pair each such ALTER with the nearest preceding
        // `new[] { "A", "B" }` and expand it.
        foreach (Match alter in Regex.Matches(sql,
                     $@"ALTER\s+TABLE\s+{Q}(?<table>\w+){Q}\s+ADD\s+COLUMN\s+{ifNotExists}{Q}\{{(?<var>\w+)\}}{Q}",
                     RegexOptions.IgnoreCase))
        {
            var loop = Regex.Matches(
                    sql[..alter.Index],
                    $@"foreach\s*\(\s*var\s+{Regex.Escape(alter.Groups["var"].Value)}\s+in\s+new\[\]\s*\{{(?<items>[^}}]*)\}}")
                .Cast<Match>()
                .LastOrDefault();
            if (loop is null) continue;

            foreach (Match item in Regex.Matches(loop.Groups["items"].Value, "\"(?<name>\\w+)\""))
                columns.Add($"{alter.Groups["table"].Value}.{item.Groups["name"].Value}");
        }

        return (tables, columns);
    }

    private static async Task<Dictionary<string, HashSet<string>>> ReadSqliteSchemaAsync(
        SqliteConnection connection)
    {
        var schema = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        var tables = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText =
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }

        foreach (var table in tables)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            schema[table] = columns;
        }

        return schema;
    }

    /// <summary>Walk up from the test binaries to the repo root, then into the
    /// requested source directory.</summary>
    private static string FindRepoDirectory(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DorkNet.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        var path = Path.Combine(new[] { dir!.FullName }.Concat(segments).ToArray());
        Assert.True(Directory.Exists(path), $"missing source directory {path}");
        return path;
    }
}
