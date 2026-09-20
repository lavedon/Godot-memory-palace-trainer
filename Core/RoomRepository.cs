using System.Globalization;
using Microsoft.Data.Sqlite;

namespace PalaceRoomViewer.Core;

public sealed class RoomRepository
{
    public RoomSnapshot Load(ViewerOptions options)
    {
        string path;
        try { path = Path.GetFullPath(options.DatabasePath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new ViewerException($"Invalid database path: {options.DatabasePath}", ex); }

        if (!File.Exists(path))
            throw new ViewerException($"Database file does not exist or cannot be accessed:\n{path}\nUse --db to select an existing SQLite file.");

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false,
                DefaultTimeout = 3
            }.ToString());
            connection.Open();
            // Schema, Room and Loci are read from one consistent, read-only snapshot.
            using var transaction = connection.BeginTransaction(deferred: true);
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var schema = connection.CreateCommand())
            {
                schema.Transaction = transaction;
                schema.CommandText = "PRAGMA table_info(Rooms)";
                using var reader = schema.ExecuteReader();
                while (reader.Read()) columns.Add(reader.GetString(1));
            }
            var imageColumns = RoomImageColumns.ByWall.Where(p => columns.Contains(p.Value)).ToArray();
            using var roomCommand = connection.CreateCommand();
            roomCommand.Transaction = transaction;
            // Only fixed, known identifiers are interpolated; old databases need no automatic migration.
            roomCommand.CommandText = "SELECT Title" + string.Concat(imageColumns.Select(p => $", \"{p.Value}\"")) + " FROM Rooms WHERE Id = @id";
            roomCommand.Parameters.AddWithValue("@id", options.RoomId);
            string title;
            var imagePaths = new Dictionary<RoomWall, string>();
            var imageWarnings = new List<string>();
            using (var reader = roomCommand.ExecuteReader())
            {
                if (!reader.Read())
                    throw new ViewerException($"Room {options.RoomId} does not exist in:\n{path}");
                if (reader.GetValue(0) is not string roomTitle || string.IsNullOrWhiteSpace(roomTitle))
                    throw new ViewerException($"Room {options.RoomId} has a missing or invalid Title.");
                title = roomTitle;
                for (var i = 0; i < imageColumns.Length; i++)
                {
                    if (reader.IsDBNull(i + 1)) continue;
                    if (reader.GetValue(i + 1) is not string imagePath)
                        imageWarnings.Add($"Room {options.RoomId}: {imageColumns[i].Value} must contain an image file path as text.");
                    else if (!string.IsNullOrWhiteSpace(imagePath)) imagePaths[imageColumns[i].Key] = imagePath;
                }
            }

            var loci = new Dictionary<int, Locus>();
            var warnings = new List<LoadWarning>();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT Id, Position, Text, typeof(Position), typeof(Text) FROM Loci WHERE RoomId = @id ORDER BY Id";
            command.Parameters.AddWithValue("@id", options.RoomId);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var id = reader.GetInt64(0);
                    var raw = reader.IsDBNull(1) ? "NULL" : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? "NULL";
                    if (reader.GetString(3) != "integer")
                    {
                        warnings.Add(new(id, raw, "skipped; Position must be an integer from 1 to 26."));
                        continue;
                    }
                    var number = reader.GetInt64(1);
                    if (number is < 1 or > RoomLayout.Capacity)
                    {
                        warnings.Add(new(id, raw, "skipped; supported Positions are 1–26."));
                        continue;
                    }
                    var position = (int)number;
                    if (loci.TryGetValue(position, out var existing))
                    {
                        warnings.Add(new(id, raw, $"skipped duplicate; Locus {existing.Id} has the lowest Id at this Position."));
                        continue;
                    }
                    if (reader.GetString(4) != "text")
                        throw new ViewerException($"Locus {id} at Position {position} has invalid Text; expected text in the database.");
                    loci.Add(position, new Locus(id, position, reader.GetString(2)));
                }
            }
            transaction.Commit();
            return new RoomSnapshot(options.RoomId, title, loci, warnings, path)
            { ImagePaths = imagePaths, ImageWarnings = imageWarnings };
        }
        catch (SqliteException ex)
        {
            var reason = ex.SqliteErrorCode switch
            {
                1 => "The database schema is incompatible. Expected Rooms(Id, Title) and Loci(Id, RoomId, Position, Text).",
                5 or 6 => "The database is busy or locked. Close the operation holding the lock and try again.",
                11 or 26 => "This file is damaged or is not a SQLite database.",
                14 or 3 => "The database could not be opened for reading. Check the path and file permissions.",
                _ => "Database access failed."
            };
            throw new ViewerException($"{reason}\n{path}\nSQLite: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidCastException or OverflowException)
        { throw new ViewerException($"Could not read Room {options.RoomId}: {ex.Message}\n{path}", ex); }
    }
}
