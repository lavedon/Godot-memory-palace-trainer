using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PalaceRoomViewer.Core;

// Rehearsal history in palace.db. These are the viewer's only writes: two additive tables, each
// created on its first write. RehearsalRuns gets one INSERT per completed rehearsal; FirstLoopDrills
// gets one row per Room, when its first loop drill starts its learning clock (ADR 0005).
// Rooms and Loci are never written.
// Tables created before LocusIds (ADR 0004) or Route (ADR 0007) existed gain those nullable columns on their next save.
public sealed class RehearsalStore
{
    public const string LoopDrillTable = "FirstLoopDrills";
    public const string CreateLoopDrillSql = """
        CREATE TABLE IF NOT EXISTS FirstLoopDrills (
            RoomId    INTEGER PRIMARY KEY REFERENCES Rooms(Id) ON DELETE CASCADE,
            StartedAt TEXT    NOT NULL    -- ISO 8601 with UTC offset; the Room's first loop drill, never updated
        );
        """;

    public const string Table = "RehearsalRuns";
    public const string CreateSql = """
        CREATE TABLE IF NOT EXISTS RehearsalRuns (
            Id                      INTEGER PRIMARY KEY,
            RoomId                  INTEGER NOT NULL REFERENCES Rooms(Id) ON DELETE CASCADE,
            StartedAt               TEXT    NOT NULL,   -- ISO 8601 with UTC offset
            CompletedAt             TEXT    NOT NULL,   -- ISO 8601 with UTC offset
            DurationMs              INTEGER NOT NULL,   -- start to Room clear
            LociCount               INTEGER NOT NULL,
            FirstPassKnown          INTEGER NOT NULL,
            Rounds                  INTEGER NOT NULL,
            BestCombo               INTEGER NOT NULL,
            Score                   INTEGER NOT NULL,
            Medal                   TEXT    NOT NULL,   -- None | Bronze | Silver | Gold | Platinum, as awarded
            Positions               TEXT    NOT NULL,   -- JSON array, rehearsal order
            MissesByRound           TEXT    NOT NULL,   -- JSON array of arrays of Positions
            SplitsMs                TEXT    NOT NULL,   -- JSON array, elapsed ms at each first-pass answer
            FirstPassMissedLocusIds TEXT    NOT NULL,   -- JSON array of Loci.Id
            LocusIds                TEXT,               -- JSON array of Loci.Id aligned with Positions; NULL in older rows
            Route                   TEXT                -- TopFirst | BottomFirst for advanced rehearsals; NULL = Position order
        );
        CREATE INDEX IF NOT EXISTS ix_rehearsalruns_room ON RehearsalRuns(RoomId);
        """;

    // Read-only. A database without the table simply has no history yet.
    public IReadOnlyList<RehearsalRun> Load(string databasePath)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadOnly);
        try
        {
            if (!TableExists(connection, Table)) return [];
            using var command = connection.CreateCommand();
            var hasLocusIds = HasColumn(connection, null, "LocusIds");
            var hasRoute = HasColumn(connection, null, "Route");
            command.CommandText = "SELECT Id, RoomId, StartedAt, CompletedAt, DurationMs, Positions, MissesByRound, SplitsMs, " +
                "FirstPassMissedLocusIds, BestCombo, Score, " + (hasLocusIds ? "LocusIds" : "NULL") + ", " + (hasRoute ? "Route" : "NULL") +
                " FROM RehearsalRuns ORDER BY CompletedAt, Id";
            using var reader = command.ExecuteReader();
            var runs = new List<RehearsalRun>();
            while (reader.Read())
            {
                // Rows edited by other tools are skipped rather than blocking the whole history.
                try
                {
                    var run = new RehearsalRun(reader.GetInt64(1), Time(reader.GetString(2)), Time(reader.GetString(3)), reader.GetInt64(4),
                        Json<int[]>(reader.GetString(5)), Json<int[][]>(reader.GetString(6)), Json<long[]>(reader.GetString(7)),
                        Json<long[]>(reader.GetString(8)), reader.GetInt32(9), reader.GetInt32(10)) { Id = reader.GetInt64(0) };
                    // A LocusIds value that does not line up with Positions is ignored, not trusted.
                    if (!reader.IsDBNull(11) && reader.GetValue(11) is string ids && Json<long[]>(ids) is { } parsed && parsed.Length == run.Positions.Count)
                        run = run with { LocusIds = parsed };
                    // A route this version does not know is skipped: its times would not compare with anything.
                    if (!reader.IsDBNull(12))
                    {
                        if (reader.GetValue(12) is not string route || !Enum.TryParse<RehearsalRoute>(route, out var parsedRoute) ||
                            !Enum.IsDefined(parsedRoute) || !parsedRoute.IsAdvanced()) continue;
                        run = run with { Route = parsedRoute };
                    }
                    if (run.Positions.Count > 0 && run.MissesByRound.Count > 0) runs.Add(run);
                }
                catch (Exception ex) when (ex is FormatException or JsonException or InvalidCastException or InvalidOperationException) { }
            }
            return runs;
        }
        catch (SqliteException ex)
        {
            throw new ViewerException($"Could not read rehearsal history.\nSQLite: {ex.Message}", ex);
        }
    }

    public RehearsalRun Save(string databasePath, RehearsalRun run)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadWrite);
        try
        {
            using var transaction = connection.BeginTransaction(deferred: false);
            using (var create = connection.CreateCommand())
            {
                create.Transaction = transaction;
                create.CommandText = CreateSql;
                create.ExecuteNonQuery();
            }
            foreach (var column in new[] { "LocusIds", "Route" })
            {
                if (HasColumn(connection, transaction, column)) continue;
                using var alter = connection.CreateCommand();
                alter.Transaction = transaction;
                // Only these two fixed names are interpolated.
                alter.CommandText = $"ALTER TABLE RehearsalRuns ADD COLUMN {column} TEXT";
                alter.ExecuteNonQuery();
            }
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO RehearsalRuns (RoomId, StartedAt, CompletedAt, DurationMs, LociCount, FirstPassKnown, Rounds,
                  BestCombo, Score, Medal, Positions, MissesByRound, SplitsMs, FirstPassMissedLocusIds, LocusIds, Route)
                VALUES ($room, $started, $completed, $duration, $loci, $known, $rounds, $combo, $score, $medal,
                  $positions, $misses, $splits, $missedIds, $locusIds, $route);
                SELECT last_insert_rowid();
                """;
            insert.Parameters.AddWithValue("$room", run.RoomId);
            insert.Parameters.AddWithValue("$started", run.StartedAt.ToString("o", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$completed", run.CompletedAt.ToString("o", CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$duration", run.DurationMs);
            insert.Parameters.AddWithValue("$loci", run.LociCount);
            insert.Parameters.AddWithValue("$known", run.FirstPassKnown);
            insert.Parameters.AddWithValue("$rounds", run.Rounds);
            insert.Parameters.AddWithValue("$combo", run.BestCombo);
            insert.Parameters.AddWithValue("$score", run.Score);
            insert.Parameters.AddWithValue("$medal", run.Medal.ToString());
            insert.Parameters.AddWithValue("$positions", JsonSerializer.Serialize(run.Positions));
            insert.Parameters.AddWithValue("$misses", JsonSerializer.Serialize(run.MissesByRound));
            insert.Parameters.AddWithValue("$splits", JsonSerializer.Serialize(run.SplitsMs));
            insert.Parameters.AddWithValue("$missedIds", JsonSerializer.Serialize(run.FirstPassMissedLocusIds));
            insert.Parameters.AddWithValue("$locusIds", run.LocusIds is { } locusIds ? JsonSerializer.Serialize(locusIds) : DBNull.Value);
            insert.Parameters.AddWithValue("$route", run.Route.IsAdvanced() ? run.Route.ToString() : DBNull.Value);
            var id = (long)insert.ExecuteScalar()!;
            transaction.Commit();
            return run with { Id = id };
        }
        catch (SqliteException ex)
        {
            throw new ViewerException($"Could not save the rehearsal.\nSQLite: {ex.Message}", ex);
        }
    }

    // Read-only. Each Room's first loop drill, for Rooms that have had one.
    public IReadOnlyDictionary<long, DateTimeOffset> LoadFirstLoopDrills(string databasePath)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadOnly);
        try
        {
            var starts = new Dictionary<long, DateTimeOffset>();
            if (!TableExists(connection, LoopDrillTable)) return starts;
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT RoomId, StartedAt FROM FirstLoopDrills";
            using var reader = command.ExecuteReader();
            // Rows edited by other tools into something unreadable are skipped.
            while (reader.Read())
                if (reader.GetValue(0) is long roomId && reader.GetValue(1) is string text &&
                    DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startedAt))
                    starts[roomId] = startedAt;
            return starts;
        }
        catch (SqliteException ex)
        {
            throw new ViewerException($"Could not read loop drill history.\nSQLite: {ex.Message}", ex);
        }
    }

    // Records a Room's first loop drill. Later drills change nothing, and do not even take a write lock.
    // Returns true when this drill was the Room's first.
    public bool RecordFirstLoopDrill(string databasePath, long roomId, DateTimeOffset startedAt)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadWrite);
        try
        {
            if (TableExists(connection, LoopDrillTable))
            {
                using var exists = connection.CreateCommand();
                exists.CommandText = "SELECT 1 FROM FirstLoopDrills WHERE RoomId = $room";
                exists.Parameters.AddWithValue("$room", roomId);
                if (exists.ExecuteScalar() is not null) return false;
            }
            using var transaction = connection.BeginTransaction(deferred: false);
            using (var create = connection.CreateCommand())
            {
                create.Transaction = transaction;
                create.CommandText = CreateLoopDrillSql;
                create.ExecuteNonQuery();
            }
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT OR IGNORE INTO FirstLoopDrills (RoomId, StartedAt) VALUES ($room, $started)";
            insert.Parameters.AddWithValue("$room", roomId);
            insert.Parameters.AddWithValue("$started", startedAt.ToString("o", CultureInfo.InvariantCulture));
            var first = insert.ExecuteNonQuery() == 1;
            transaction.Commit();
            return first;
        }
        catch (SqliteException ex)
        {
            throw new ViewerException($"Could not record the loop drill.\nSQLite: {ex.Message}", ex);
        }
    }

    private static bool TableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", table);
        return command.ExecuteScalar() is not null;
    }

    private static bool HasColumn(SqliteConnection connection, SqliteTransaction? transaction, string column)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM pragma_table_info('RehearsalRuns') WHERE name = $column";
        command.Parameters.AddWithValue("$column", column);
        return command.ExecuteScalar() is not null;
    }

    private static SqliteConnection Open(string databasePath, SqliteOpenMode mode)
    {
        string path;
        try { path = Path.GetFullPath(databasePath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new ViewerException($"Invalid database path: {databasePath}", ex); }
        // ReadWrite (never ReadWriteCreate): a wrong path must not create an empty database.
        if (!File.Exists(path)) throw new ViewerException($"Database file does not exist or cannot be accessed:\n{path}");
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = mode, Pooling = false, DefaultTimeout = 3 }.ToString());
        try { connection.Open(); return connection; }
        catch (SqliteException ex)
        {
            connection.Dispose();
            throw new ViewerException($"Could not open {path}.\nSQLite: {ex.Message}", ex);
        }
    }

    private static DateTimeOffset Time(string text) => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static T Json<T>(string text) => JsonSerializer.Deserialize<T>(text) ?? throw new JsonException("null");
}
