using Microsoft.Data.Sqlite;

namespace PalaceRoomViewer.Core;

public enum SurfaceImageState { None, Found, Missing }

public sealed record SurfaceImage(RoomWall Wall, string? StoredPath, string? FullPath, SurfaceImageState State);

public sealed record RoomSummary(long Id, string Title, int LociCount, IReadOnlyList<SurfaceImage> Images)
{
    // Loci.Id at each displayable Position (1–26), chosen like the viewer: lowest Id wins a shared Position.
    public IReadOnlyDictionary<int, long> LocusIds { get; init; } = new Dictionary<int, long>();

    public int Found => Images.Count(i => i.State == SurfaceImageState.Found);
    public int Missing => Images.Count(i => i.State == SurfaceImageState.Missing);
    public bool HasImages => Found > 0;

    public string ImageSummary => Found == 0 && Missing == 0 ? "No images"
        : $"{Found}/{Images.Count} images" + (Missing > 0 ? $" · {Missing} missing" : "");
}

public sealed record PalaceSummary(long Id, string Name, string? Description, IReadOnlyList<RoomSummary> Rooms)
{
    public int RoomsWithImages => Rooms.Count(r => r.HasImages);
}

// Read-only listing of every Palace and its Rooms for the in-viewer picker.
// Databases without a Palaces table (older fixtures) list all Rooms under one group.
public sealed class PalaceCatalog
{
    public const long UngroupedPalaceId = 0;

    public IReadOnlyList<PalaceSummary> Load(string databasePath)
    {
        string path;
        try { path = Path.GetFullPath(databasePath); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new ViewerException($"Invalid database path: {databasePath}", ex); }
        if (!File.Exists(path))
            throw new ViewerException($"Database file does not exist or cannot be accessed:\n{path}");
        var baseDirectory = Path.GetDirectoryName(path)!;

        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 3
            }.ToString());
            connection.Open();
            using var transaction = connection.BeginTransaction(deferred: true);
            SqliteCommand Command(string sql)
            {
                var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                return command;
            }

            var roomColumns = Columns(Command("PRAGMA table_info(Rooms)"));
            var grouped = roomColumns.Contains("PalaceId") && Columns(Command("PRAGMA table_info(Palaces)")).Count > 0;
            var imageColumns = RoomImageColumns.ByWall.Where(p => roomColumns.Contains(p.Value)).ToArray();

            var palaces = new List<(long Id, string Name, string? Description)>();
            if (grouped)
            {
                using var palaceCommand = Command("SELECT Id, Name, Description FROM Palaces ORDER BY Id");
                using var reader = palaceCommand.ExecuteReader();
                while (reader.Read())
                    palaces.Add((reader.GetInt64(0), Convert.ToString(reader.GetValue(1)) ?? $"Palace {reader.GetInt64(0)}",
                        reader.IsDBNull(2) ? null : Convert.ToString(reader.GetValue(2))));
            }

            // Only fixed, known identifiers are interpolated.
            var sql = "SELECT r.Id, r.Title, " + (grouped ? "r.PalaceId" : "NULL") +
                ", (SELECT COUNT(*) FROM Loci l WHERE l.RoomId = r.Id)" +
                string.Concat(imageColumns.Select(p => $", r.\"{p.Value}\"")) + " FROM Rooms r ORDER BY r.Id";
            var rooms = new List<(long? PalaceId, RoomSummary Room)>();
            using (var roomCommand = Command(sql))
            using (var reader = roomCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    var id = reader.GetInt64(0);
                    var title = reader.GetValue(1) is string t && !string.IsNullOrWhiteSpace(t) ? t : $"Room {id} (untitled)";
                    long? palaceId = reader.IsDBNull(2) ? null : reader.GetInt64(2);
                    var images = new List<SurfaceImage>();
                    foreach (var wall in Enum.GetValues<RoomWall>())
                    {
                        var index = Array.FindIndex(imageColumns, p => p.Key == wall);
                        var stored = index < 0 || reader.IsDBNull(4 + index) ? null : reader.GetValue(4 + index) as string;
                        images.Add(Resolve(wall, stored, baseDirectory));
                    }
                    rooms.Add((palaceId, new RoomSummary(id, title, reader.GetInt32(3), images)));
                }
            }
            var locusIds = new Dictionary<long, Dictionary<int, long>>();
            using (var lociCommand = Command("SELECT RoomId, Id, Position FROM Loci WHERE typeof(Position) = 'integer' AND Position BETWEEN 1 AND " +
                RoomLayout.Capacity + " ORDER BY Id"))
            using (var reader = lociCommand.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader.IsDBNull(0)) continue;
                    var byPosition = locusIds.TryGetValue(reader.GetInt64(0), out var found) ? found : locusIds[reader.GetInt64(0)] = [];
                    byPosition.TryAdd(reader.GetInt32(2), reader.GetInt64(1));
                }
            }
            transaction.Commit();
            for (var i = 0; i < rooms.Count; i++)
                if (locusIds.TryGetValue(rooms[i].Room.Id, out var byPosition))
                    rooms[i] = rooms[i] with { Room = rooms[i].Room with { LocusIds = byPosition } };

            var result = palaces
                .Select(p => new PalaceSummary(p.Id, p.Name, p.Description,
                    rooms.Where(r => r.PalaceId == p.Id).Select(r => r.Room).ToList()))
                .ToList();
            var known = palaces.Select(p => p.Id).ToHashSet();
            var orphans = rooms.Where(r => r.PalaceId is not { } id || !known.Contains(id)).Select(r => r.Room).ToList();
            if (orphans.Count > 0)
                result.Add(new PalaceSummary(UngroupedPalaceId, grouped ? "Rooms without a Palace" : "All Rooms", null, orphans));
            return result;
        }
        catch (SqliteException ex)
        {
            throw new ViewerException($"Could not list Palaces and Rooms.\n{path}\nSQLite: {ex.Message}", ex);
        }
    }

    // Stored paths resolve like the viewer's Room images: absolute, or relative to the database directory.
    private static SurfaceImage Resolve(RoomWall wall, string? stored, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(stored)) return new(wall, null, null, SurfaceImageState.None);
        try
        {
            var full = Path.GetFullPath(stored, baseDirectory);
            return new(wall, stored, full, File.Exists(full) ? SurfaceImageState.Found : SurfaceImageState.Missing);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(wall, stored, null, SurfaceImageState.Missing);
        }
    }

    private static HashSet<string> Columns(SqliteCommand command)
    {
        using (command)
        using (var reader = command.ExecuteReader())
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (reader.Read()) columns.Add(reader.GetString(1));
            return columns;
        }
    }
}
