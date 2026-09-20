using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PalaceRoomViewer.Core;
using PalaceRoomViewer.Migrations;

internal static class RoomImageChecks
{
    public static void Run(string directory, Action<string, Action> test)
    {
        var database = Path.Combine(directory, "migration.db");
        using (var connection = Open(database))
            Execute(connection, """
                CREATE TABLE Rooms (Id INTEGER PRIMARY KEY, Title TEXT NOT NULL UNIQUE, RoomImage TEXT);
                CREATE TABLE Loci (Id INTEGER PRIMARY KEY, RoomId INTEGER REFERENCES Rooms(Id), Position INTEGER, Text TEXT);
                CREATE INDEX ix_loci_room ON Loci(RoomId);
                INSERT INTO Rooms VALUES (1, 'Existing room', 'original room image.png');
                INSERT INTO Loci VALUES (1, 1, 1, 'Keep this locus');
                """);
        var original = Hash(database);
        test("Migration preview does not change the database", () =>
        {
            var result = RoomImagesMigration.Run(database, checkOnly: true);
            Check(!result.Applied && result.BackupPath is null && result.Columns.Count == 6 && Hash(database) == original, "read-only preview");
        });
        MigrationResult? migration = null;
        test("Migration adds exactly six nullable TEXT columns and keeps existing data", () =>
        {
            migration = RoomImagesMigration.Run(database);
            using var connection = Open(database);
            Check(migration.Applied && migration.Columns.SequenceEqual(RoomImageColumns.ByWall.Values), "six additions");
            using var schema = connection.CreateCommand();
            schema.CommandText = "PRAGMA table_info(Rooms)";
            using (var reader = schema.ExecuteReader())
            {
                var count = 0;
                while (reader.Read())
                {
                    count++;
                    if (RoomImageColumns.ByWall.Values.Contains(reader.GetString(1)))
                        Check(reader.GetString(2) == "TEXT" && reader.GetInt64(3) == 0 && reader.IsDBNull(4), "optional text columns");
                }
                Check(count == 9, "only six columns added");
            }
            Check(Scalar(connection, "SELECT Title || '|' || RoomImage FROM Rooms") == "Existing room|original room image.png", "original Room fields");
            Check(Scalar(connection, "SELECT Text FROM Loci") == "Keep this locus", "original Loci");
            Check(Scalar(connection, "SELECT count(*) FROM Rooms WHERE LeftImagePath IS NULL AND RightImagePath IS NULL AND ForwardImagePath IS NULL AND BackImagePath IS NULL AND FloorImagePath IS NULL AND CeilingImagePath IS NULL") == "1", "initially NULL");
            Check(Scalar(connection, "SELECT count(*) FROM sqlite_master WHERE name='ix_loci_room'") == "1", "index kept");
            Check(Scalar(connection, "PRAGMA foreign_key_check") == "", "foreign keys kept");
        });
        test("Migration backup contains original schema and records", () =>
        {
            Check(migration?.BackupPath is not null && File.Exists(migration.BackupPath), "backup exists");
            using var backup = Open(migration!.BackupPath!);
            Check(Scalar(backup, "SELECT count(*) FROM pragma_table_info('Rooms')") == "3", "pre-migration schema");
            Check(Scalar(backup, "SELECT RoomImage FROM Rooms") == "original room image.png" && Scalar(backup, "SELECT Text FROM Loci") == "Keep this locus", "backup contents");
            Check(Scalar(backup, "PRAGMA integrity_check") == "ok", "backup integrity");
        });
        test("Migration can be repeated without changes or another backup", () =>
        {
            using (var connection = Open(database)) Execute(connection, "UPDATE Rooms SET LeftImagePath='existing.png'");
            var before = Hash(database);
            var repeat = RoomImagesMigration.Run(database);
            Check(!repeat.Applied && repeat.Columns.Count == 0 && repeat.BackupPath is null && Hash(database) == before, "idempotent migration");
        });
        test("Missing migration target is never created", () =>
        {
            var absent = Path.Combine(directory, "absent-migration.db");
            ExpectFailure(() => RoomImagesMigration.Run(absent));
            Check(!File.Exists(absent), "missing file stayed missing");
        });
        test("Incompatible existing column prevents all migration changes", () =>
        {
            var incompatible = Path.Combine(directory, "migration-incompatible.db");
            using (var connection = Open(incompatible)) Execute(connection, "CREATE TABLE Rooms (Id INTEGER, Title TEXT, FloorImagePath INTEGER)");
            var before = Hash(incompatible);
            ExpectFailure(() => RoomImagesMigration.Run(incompatible));
            Check(Hash(incompatible) == before && Directory.GetFiles(directory, "migration-incompatible.db.*.bak").Length == 0, "no partial schema or backup");
        });
        test("Partially migrated database keeps existing paths", () =>
        {
            var partial = Path.Combine(directory, "migration-partial.db");
            using (var connection = Open(partial))
                Execute(connection, "CREATE TABLE Rooms (Id INTEGER, Title TEXT, leftimagepath TEXT); INSERT INTO Rooms VALUES (1,'Partial','keep.png'); CREATE TABLE Loci(Id INTEGER,RoomId INTEGER,Position INTEGER,Text TEXT)");
            var result = RoomImagesMigration.Run(partial);
            Check(result.Columns.Count == 5, "only missing columns added, case-insensitive");
            Check(new RoomRepository().Load(new(1, partial)).ImagePaths[RoomWall.Left] == "keep.png", "existing path retained");
        });
        test("Migration backup includes uncheckpointed WAL records", () =>
        {
            var wal = Path.Combine(directory, "migration-wal.db");
            using var keeper = Open(wal);
            Execute(keeper, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; CREATE TABLE Rooms(Id INTEGER PRIMARY KEY, Title TEXT); INSERT INTO Rooms VALUES(1,'WAL-only room')");
            Check(File.Exists(wal + "-wal") && new FileInfo(wal + "-wal").Length > 0, "WAL fixture active");
            var result = RoomImagesMigration.Run(wal);
            using var backup = Open(result.BackupPath!);
            Check(Scalar(backup, "SELECT Title FROM Rooms") == "WAL-only room", "WAL content backed up");
            Check(Scalar(backup, "SELECT count(*) FROM pragma_table_info('Rooms')") == "2", "backup predates migration");
        });

        // Real image bytes are supplied later by MakeTextureFixtures.ps1 for Godot tests.
        var folder = Path.Combine(directory, "room images");
        Directory.CreateDirectory(folder);
        foreach (var wall in Enum.GetValues<RoomWall>()) File.WriteAllText(Path.Combine(folder, wall.ToString().ToLowerInvariant() + ".png"), "path fixture");
        var imagesDatabase = Path.Combine(directory, "room-images.db");
        File.Copy(Path.Combine(directory, "acceptance.db"), imagesDatabase);
        RoomImagesMigration.Run(imagesDatabase);
        using (var connection = Open(imagesDatabase))
            Execute(connection, """
                UPDATE Rooms SET LeftImagePath='room images/left.png', RightImagePath='room images/right.png',
                    ForwardImagePath='room images/forward.png', BackImagePath='room images/back.png',
                    FloorImagePath='room images/floor.png', CeilingImagePath='room images/ceiling.png' WHERE Id=8;
                UPDATE Rooms SET LeftImagePath='room images/left.png', ForwardImagePath=' ',
                    FloorImagePath='room images/floor.png', CeilingImagePath='' WHERE Id=20;
                UPDATE Rooms SET LeftImagePath='missing image.png', RightImagePath='corrupt.png' WHERE Id=21;
                UPDATE Rooms SET LeftImagePath=X'000102', FloorImagePath='room images/floor.png' WHERE Id=22;
                """);
        var imagesHash = Hash(imagesDatabase);
        RoomSnapshot Load(long id) => new RoomRepository().Load(new(id, imagesDatabase));
        test("Database image paths load all six surfaces without flags", () =>
        {
            var room = Load(8);
            var textures = new WallTextureOptions().Resolve(room);
            Check(room.ImagePaths.Count == 6 && textures.Paths.Count == 6 && textures.Warnings.Count == 0, "all six database images");
            foreach (var wall in Enum.GetValues<RoomWall>())
                Check(textures.Paths[wall] == Path.Combine(folder, wall.ToString().ToLowerInvariant() + ".png"), "database-relative path");
        });
        test("Database-relative paths do not depend on the working directory", () =>
        {
            var previous = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = Path.GetTempPath();
                Check(new WallTextureOptions().Resolve(Load(8)).Paths.Count == 6, "portable paths");
            }
            finally { Environment.CurrentDirectory = previous; }
        });
        test("Absolute database image paths are supported", () =>
        {
            var room = Load(8) with { ImagePaths = new Dictionary<RoomWall, string> { [RoomWall.Ceiling] = Path.Combine(folder, "ceiling.png") } };
            Check(new WallTextureOptions().Resolve(room).Paths[RoomWall.Ceiling] == Path.Combine(folder, "ceiling.png"), "absolute path");
        });
        test("NULL and blank database images keep defaults", () =>
        {
            Check(Load(20).ImagePaths.Count == 2 && new WallTextureOptions().Resolve(Load(20)).Paths.Count == 2, "two provided surfaces");
            Check(Load(7).ImagePaths.Count == 0 && Load(7).ImageWarnings.Count == 0, "all NULL");
        });
        test("Non-text database image values warn while loading the Room", () =>
        {
            var room = Load(22);
            Check(room.Loci.Count == 2 && room.ImageWarnings.Count == 1 && room.ImagePaths.Count == 1, "invalid BLOB path");
            Check(new WallTextureOptions().Resolve(room).Warnings.Single().Contains("LeftImagePath"), "warning forwarded");
        });
        test("Explicit image overrides database selection", () =>
        {
            var overridePath = Path.Combine(folder, "right.png");
            var selected = new WallTextureOptions(Left: overridePath).Resolve(Load(8));
            Check(selected.Paths[RoomWall.Left] == overridePath && selected.Paths.Count == 6, "explicit wins, other surfaces kept");
        });
        test("Folder overrides only supplied database surfaces", () =>
        {
            var partial = Path.Combine(directory, "database override folder"); Directory.CreateDirectory(partial);
            var overridePath = Path.Combine(partial, "left.png"); File.WriteAllText(overridePath, "path fixture");
            var selected = new WallTextureOptions(Folder: partial).Resolve(Load(8));
            Check(selected.Paths[RoomWall.Left] == overridePath && selected.Paths.Count == 6, "partial folder keeps other database images");
        });
        test("Invalid explicit image does not silently reuse database image", () =>
        {
            var selected = new WallTextureOptions(Left: Path.Combine(directory, "absent-explicit.png")).Resolve(Load(8));
            Check(selected.Paths.Count == 5 && !selected.Paths.ContainsKey(RoomWall.Left) && selected.Warnings.Count == 1, "failed override has warning");
        });
        test("Missing database image warns and other images remain usable", () =>
        {
            var room = Load(8) with { ImagePaths = new Dictionary<RoomWall, string> { [RoomWall.Left] = "missing.png", [RoomWall.Floor] = "room images/floor.png" } };
            var textures = new WallTextureOptions().Resolve(room);
            Check(textures.Paths.Count == 1 && textures.Warnings.Count == 1, "missing image isolated");
        });
        test("Viewer never migrates or writes image data", () =>
        {
            Check(Hash(imagesDatabase) == imagesHash, "migrated database read-only");
            Check(new RoomRepository().Load(new(8, Path.Combine(directory, "acceptance.db"))).ImagePaths.Count == 0, "legacy schema still supported");
        });
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void ExpectFailure(Action action)
    {
        try { action(); } catch (ViewerException) { return; }
        throw new Exception("Expected migration failure");
    }
    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open(); return connection;
    }
    private static void Execute(SqliteConnection connection, string sql)
    { using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    private static string Scalar(SqliteConnection connection, string sql)
    { using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToString(command.ExecuteScalar()) ?? ""; }
}
