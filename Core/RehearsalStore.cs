using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PalaceRoomViewer.Core;

// Rehearsal history in palace.db. This is the viewer's only write: one additive table,
// created on the first save, one INSERT per completed rehearsal. Rooms and Loci are never written.
// Tables created before LocusIds existed gain that nullable column on their next save (ADR 0004).
public sealed class RehearsalStore
{
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
            LocusIds                TEXT                -- JSON array of Loci.Id aligned with Positions; NULL in older rows
        );
        CREATE INDEX IF NOT EXISTS ix_rehearsalruns_room ON RehearsalRuns(RoomId);
        """;

    // Read-only. A database without the table simply has no history yet.
    public IReadOnlyList<RehearsalRun> Load(string databasePath)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadOnly);
        try
        {
            using (var exists = connection.CreateCommand())
            {
                exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
                exists.Parameters.AddWithValue("$name", Table);
                if (exists.ExecuteScalar() is null) return [];
            }
            using var command = connection.CreateCommand();
            var hasLocusIds = HasLocusIds(connection, null);
            command.CommandText = "SELECT Id, RoomId, StartedAt, CompletedAt, DurationMs, Positions, MissesByRound, SplitsMs, " +
                "FirstPassMissedLocusIds, BestCombo, Score, " + (hasLocusIds ? "LocusIds" : "NULL") + " FROM RehearsalRuns ORDER BY CompletedAt, Id";
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
            if (!HasLocusIds(connection, transaction))
            {
                using var alter = connection.CreateCommand();
                alter.Transaction = transaction;
                alter.CommandText = "ALTER TABLE RehearsalRuns ADD COLUMN LocusIds TEXT";
                alter.ExecuteNonQuery();
            }
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO RehearsalRuns (RoomId, StartedAt, CompletedAt, DurationMs, LociCount, FirstPassKnown, Rounds,
                  BestCombo, Score, Medal, Positions, MissesByRound, SplitsMs, FirstPassMissedLocusIds, LocusIds)
                VALUES ($room, $started, $completed, $duration, $loci, $known, $rounds, $combo, $score, $medal,
                  $positions, $misses, $splits, $missedIds, $locusIds);
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
            var id = (long)insert.ExecuteScalar()!;
            transaction.Commit();
            return run with { Id = id };
        }
        catch (SqliteException ex)
        {
            throw new ViewerException($"Could not save the rehearsal.\nSQLite: {ex.Message}", ex);
        }
    }

    private static bool HasLocusIds(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM pragma_table_info('RehearsalRuns') WHERE name = 'LocusIds'";
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
