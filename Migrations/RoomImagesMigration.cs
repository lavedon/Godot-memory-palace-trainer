using Microsoft.Data.Sqlite;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer.Migrations;

public sealed record MigrationResult(string DatabasePath, string? BackupPath, IReadOnlyList<string> Columns, bool Applied);

// Explicit maintenance tool. The viewer never calls this code or opens a writable connection.
public static class RoomImagesMigration
{
    public static MigrationResult Run(string databasePath, bool checkOnly = false)
    {
        var path = Path.GetFullPath(databasePath);
        if (!File.Exists(path)) throw new ViewerException($"Database does not exist: {path}");
        using var connection = Open(path, checkOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite);
        // Reserve the writer before inspection and backup so another writer cannot race the migration.
        using var transaction = connection.BeginTransaction(deferred: checkOnly);
        var columns = new Dictionary<string, (string Type, long Required, long Hidden)>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "PRAGMA table_xinfo(Rooms)";
            using var reader = command.ExecuteReader();
            while (reader.Read()) columns[reader.GetString(1)] = (reader.GetString(2), reader.GetInt64(3), reader.GetInt64(6));
        }
        if (!columns.ContainsKey("Id") || !columns.ContainsKey("Title"))
            throw new ViewerException("Expected an existing Rooms table with Id and Title columns.");
        foreach (var name in RoomImageColumns.ByWall.Values)
            if (columns.TryGetValue(name, out var column) &&
                (!column.Type.Equals("TEXT", StringComparison.OrdinalIgnoreCase) || column.Required != 0 || column.Hidden != 0))
                throw new ViewerException($"Existing Rooms.{name} is incompatible; expected a nullable TEXT column. No changes made.");
        var missing = RoomImageColumns.ByWall.Values.Where(name => !columns.ContainsKey(name)).ToArray();
        if (checkOnly || missing.Length == 0)
        {
            transaction.Commit();
            return new(path, null, missing, false);
        }
        CheckIntegrity(connection, transaction);
        var backupPath = path + $".before-room-images-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak";
        // Backup API includes committed WAL content. A separate reader works while our writer
        // holds its RESERVED lock; using the writing connection as backup source would be locked.
        using (File.Open(backupPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        using (var source = Open(path, SqliteOpenMode.ReadOnly))
        using (var backup = Open(backupPath, SqliteOpenMode.ReadWrite))
        {
            source.BackupDatabase(backup);
            CheckIntegrity(backup, null);
        }
        foreach (var name in missing)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"ALTER TABLE Rooms ADD COLUMN \"{name}\" TEXT NULL";
            command.ExecuteNonQuery();
        }
        CheckIntegrity(connection, transaction);
        transaction.Commit();
        return new(path, backupPath, missing, true);
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = mode, Pooling = false, DefaultTimeout = 3 }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static void CheckIntegrity(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA integrity_check";
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
            throw new ViewerException("SQLite integrity check failed. Migration was not committed.");
    }
}
