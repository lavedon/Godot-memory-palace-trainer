using System.Numerics;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PalaceRoomViewer.Core;

var directory = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "PalaceRoomViewer-tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var fixture = Path.Combine(directory, "acceptance.db");
if (File.Exists(fixture)) throw new InvalidOperationException("Use a new fixture directory; existing databases are never overwritten.");
var failures = new List<string>();
var count = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Test(string name, Action test)
{
    count++;
    try { test(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures.Add(name); Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
void Fails(Action test, string contains)
{
    try { test(); } catch (ViewerException ex) { Check(ex.Message.Contains(contains, StringComparison.OrdinalIgnoreCase), ex.Message); return; }
    throw new Exception("Expected a visible ViewerException");
}
void Execute(SqliteConnection connection, string sql)
{
    using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
}
using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = fixture, Pooling = false }.ToString()))
{
    connection.Open();
    Execute(connection, """
        CREATE TABLE Rooms (Id INTEGER PRIMARY KEY, Title TEXT);
        CREATE TABLE Loci (Id INTEGER PRIMARY KEY, RoomId INTEGER, Position, Text);
        INSERT INTO Rooms VALUES (8, 'The complete Room'), (7, 'Overflow Room'), (20, 'Gaps stay put'),
          (21, 'Empty Room'), (22, 'Invalid Positions'), (23, 'Long text'), (24, NULL), (25, 'Invalid text');
        INSERT INTO Loci VALUES (2001,20,1,'First'), (2010,20,10,'Anchor ten'), (2026,20,26,'Ceiling');
        INSERT INTO Loci VALUES (2201,22,1,'Lowest Id wins'), (2202,22,1,'Duplicate'),
          (2203,22,NULL,'No Position'), (2204,22,27,'Above capacity'), (2205,22,0,'Zero'),
          (2206,22,-4,'Negative'), (2207,22,2.5,'Fraction'), (2208,22,'3','String'),
          (2209,22,9223372036854775807,'Very large'), (2210,22,10,'Keep this Position');
        INSERT INTO Loci VALUES (2501,25,1,NULL);
        """);
    for (var room = 7; room <= 8; room++)
        for (var position = 1; position <= (room == 7 ? 29 : 26); position++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Loci(Id,RoomId,Position,Text) VALUES (@id,@room,@position,@text)";
            command.Parameters.AddWithValue("@id", room * 100 + position);
            command.Parameters.AddWithValue("@room", room);
            command.Parameters.AddWithValue("@position", position);
            command.Parameters.AddWithValue("@text", $"Position {position}: {RoomLayout.Description(Math.Min(position,26))}. A memorable idea belongs at this fixed Anchor.");
            command.ExecuteNonQuery();
        }
    using var longCommand = connection.CreateCommand();
    longCommand.CommandText = "INSERT INTO Loci VALUES (2301,23,1,@text),(2325,23,25,@text),(2326,23,26,@text)";
    longCommand.Parameters.AddWithValue("@text", string.Concat(Enumerable.Repeat("A longer wrapping sample preserves every word, including punctuation, Unicode café, 日本語, and a journey across the Room. ", 12)));
    longCommand.ExecuteNonQuery();
}
var repository = new RoomRepository();
RoomSnapshot Load(long id) => repository.Load(new ViewerOptions(id, fixture));
var before = SHA256.HashData(File.ReadAllBytes(fixture));

Test("Required Room ID", () => Fails(() => ViewerOptions.Parse([]), "--room"));
Test("Default database", () => Check(ViewerOptions.Parse(["--room","8"]).DatabasePath == ViewerOptions.DefaultDatabasePath, "default path"));
Test("Database override and order", () => Check(ViewerOptions.Parse(["--db", "a path;with spaces.db", "--room", "8"]) == new ViewerOptions(8, "a path;with spaces.db"), "options"));
foreach (var invalid in new[] { "0", "-2", "x", "8.5", "9223372036854775808", "8 OR 1=1" })
    Test($"Reject Room ID {invalid}", () => Fails(() => ViewerOptions.Parse(["--room", invalid]), "Invalid Room ID"));
Test("Missing option value", () => Fails(() => ViewerOptions.Parse(["--room"]), "value"));
Test("Next option is not a value", () => Fails(() => ViewerOptions.Parse(["--room","--db","x"]), "value"));
Test("Unknown option", () => Fails(() => ViewerOptions.Parse(["--rom","8"]), "Unknown"));
Test("Duplicate Room argument", () => Fails(() => ViewerOptions.Parse(["--room","8","--room","7"]), "once"));
Test("Duplicate database argument", () => Fails(() => ViewerOptions.Parse(["--room","8","--db","a","--db","b"]), "once"));
Test("Empty database argument", () => Fails(() => ViewerOptions.Parse(["--room","8","--db"," "]), "empty"));
Test("Wall textures are optional", () => Check(ViewerOptions.Parse(["--room","8"]).WallTextures.Resolve().Paths.Count == 0, "no default texture files"));
Test("All wall switches parse independently", () =>
{
    var options = ViewerOptions.Parse(["--left","left image.png","--room","8","--right","right.jpg","--forward","front.png","--back","back.webp","--room-textures","wall folder"]);
    Check(options.WallTextures == new WallTextureOptions("left image.png","right.jpg","front.png","back.webp","wall folder"), "wall paths");
});
foreach (var option in new[] {"--left","--right","--forward","--back","--room-textures"})
{
    Test($"Missing {option} value", () => Fails(() => ViewerOptions.Parse(["--room","8",option]), "value"));
    Test($"Empty {option} value", () => Fails(() => ViewerOptions.Parse(["--room","8",option," "]), "empty"));
    Test($"Duplicate {option}", () => Fails(() => ViewerOptions.Parse(["--room","8",option,"a",option,"b"]), "once"));
}
var wallFolder = Path.Combine(directory, "texture path fixtures");
Directory.CreateDirectory(wallFolder);
// These are path-resolution fixtures, not images; decoding is exercised in Godot.
foreach (var name in new[] {"left.png","right.png","forward.png","back.png","ignored.png"})
    File.WriteAllText(Path.Combine(wallFolder,name), "path fixture");
Test("Folder uses the four agreed filenames", () =>
{
    var sources = new WallTextureOptions(Folder: wallFolder).Resolve();
    Check(sources.Paths.Count == 4 && sources.Warnings.Count == 0 && sources.Paths[RoomWall.Forward] == Path.Combine(wallFolder,"forward.png"), "folder mappings");
});
var overridePath = Path.Combine(directory,"a; quoted 'wall'.png"); File.WriteAllText(overridePath,"path fixture");
Test("Explicit wall wins regardless of argument order", () =>
{
    foreach (var arguments in new[]
    {
        new[] {"--room","8","--left",overridePath,"--room-textures",wallFolder},
        new[] {"--room-textures",wallFolder,"--left",overridePath,"--room","8"}
    })
    {
        var sources = ViewerOptions.Parse(arguments).WallTextures.Resolve();
        Check(sources.Paths.Count == 4 && sources.Paths[RoomWall.Left] == overridePath && sources.Warnings.Count == 0, "override precedence");
    }
});
var partialFolder = Path.Combine(directory,"partial texture paths"); Directory.CreateDirectory(partialFolder);
File.WriteAllText(Path.Combine(partialFolder,"left.png"),"path fixture");
Test("Partial texture folder is allowed", () =>
{
    var sources = new WallTextureOptions(Folder: partialFolder).Resolve();
    Check(sources.Paths.Count == 1 && sources.Paths.ContainsKey(RoomWall.Left) && sources.Warnings.Count == 0, "partial folder");
});
Test("Missing override warns and does not silently use folder image", () =>
{
    var sources = new WallTextureOptions(Left: Path.Combine(directory,"missing.png"), Folder: wallFolder).Resolve();
    Check(sources.Paths.Count == 3 && !sources.Paths.ContainsKey(RoomWall.Left) && sources.Warnings.Single().Contains("Left"), "missing explicit image");
});
Test("Missing folder still allows explicit images", () =>
{
    var sources = new WallTextureOptions(Right: overridePath, Folder: Path.Combine(directory,"absent-folder")).Resolve();
    Check(sources.Paths.Count == 1 && sources.Paths[RoomWall.Right] == overridePath && sources.Warnings.Count == 1, "missing folder");
});
Test("Relative texture paths use the working directory", () =>
{
    var relative = Path.GetRelativePath(Environment.CurrentDirectory, overridePath);
    Check(new WallTextureOptions(Back: relative).Resolve().Paths[RoomWall.Back] == overridePath, "relative path");
});
Test("Invalid texture paths produce warnings", () => Check(new WallTextureOptions(Left: "bad\0path").Resolve().Warnings.Count == 1, "invalid path"));
Test("All 26 Positions load", () => { var room = Load(8); Check(room.Title == "The complete Room" && room.Loci.Count == 26 && room.Warnings.Count == 0, "full Room"); });
Test("Overflow warns by Locus identity", () => { var room = Load(7); Check(room.Loci.Count == 26 && room.Warnings.Select(w => w.LocusId).SequenceEqual(new long[] {727,728,729}), "overflow warning identities"); });
Test("Gaps preserve identity", () => Check(Load(20).Loci.Keys.SequenceEqual(new[] {1,10,26}), "gaps must remain empty"));
Test("Empty differs from missing", () => { Check(Load(21).Loci.Count == 0, "empty Room"); Fails(() => Load(9999), "does not exist"); });
Test("Invalid Positions do not block good Loci", () => { var room = Load(22); Check(room.Loci.Count == 2 && room.Loci[1].Id == 2201 && room.Loci[10].Id == 2210 && room.Warnings.Count == 8, "validation"); });
Test("Duplicate lowest Id wins", () => Check(Load(22).Warnings.Single(w => w.LocusId == 2202).Reason.Contains("2201"), "duplicate owner"));
Test("Full long Unicode text preserved", () => Check(Load(23).Loci[25].Text.Length > 1200 && Load(23).Loci[25].Text.Contains("日本語"), "no truncation"));
Test("Invalid Title visible", () => Fails(() => Load(24), "Title"));
Test("Invalid Text visible", () => Fails(() => Load(25), "Text"));
Test("Missing path creates no database", () => { var absent = Path.Combine(directory,"never-created.db"); Fails(() => repository.Load(new(8,absent)), "does not exist"); Check(!File.Exists(absent), "must not create"); });
Test("Connection string characters are escaped", () => { var path = Path.Combine(directory,"a;b 'quoted' database.db"); File.Copy(fixture,path); Check(repository.Load(new(8,path)).Loci.Count == 26, "safe connection builder"); });
var badSchema = Path.Combine(directory, "bad-schema.db");
using (var connection = new SqliteConnection($"Data Source={badSchema};Pooling=False")) { connection.Open(); Execute(connection,"CREATE TABLE Unrelated(Id INTEGER)"); }
Test("Incompatible schema visible", () => Fails(() => repository.Load(new(8,badSchema)), "schema"));
var corrupt = Path.Combine(directory, "corrupt.db"); File.WriteAllText(corrupt,"This is not SQLite.");
Test("Corrupt database visible", () => Fails(() => repository.Load(new(8,corrupt)), "not a SQLite"));
Test("Exclusive lock reports useful failure", () =>
{
    using var connection = new SqliteConnection($"Data Source={fixture};Mode=ReadWrite;Pooling=False");
    connection.Open(); Execute(connection,"BEGIN EXCLUSIVE");
    try { Fails(() => Load(8), "locked"); } finally { Execute(connection,"ROLLBACK"); }
});
Test("Read-only file loads", () =>
{
    var attributes = File.GetAttributes(fixture); File.SetAttributes(fixture, attributes | FileAttributes.ReadOnly);
    try { Check(Load(8).Loci.Count == 26, "read-only load"); } finally { File.SetAttributes(fixture,attributes); }
});
Test("Database bytes unchanged", () => Check(before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(fixture))), "database modified"));
Test("No pooled connection keeps database locked", () => { using var stream = File.Open(fixture,FileMode.Open,FileAccess.ReadWrite,FileShare.None); Check(stream.Length > 0,"exclusive access"); });

// Independent diagram oracle, ordered by wall-slot; no reimplementation of the production formula.
var diagram = new (int[] positions, float x, float z)[] {
    ([1,2,3],0,9), ([4,5,6],6,9), ([7,8,9],6,0), ([10,11,12],6,-9),
    ([13,14,15],0,-9), ([16,17,18],-6,-9), ([19,20,21],-6,0), ([22,23,24],-6,9)
};
Test("All three diagrams match world convention", () =>
{
    foreach (var (positions,x,z) in diagram)
    {
        Check(RoomLayout.Anchor(positions[0]) == new Vector3(x,0,z), "floor Slice");
        Check(RoomLayout.Anchor(positions[1]) == new Vector3(x,3.5f,z), "middle Slice");
        Check(RoomLayout.Anchor(positions[2]) == new Vector3(x,7,z), "upper Slice");
    }
});
Test("Floor and ceiling are centered", () => Check(RoomLayout.Anchor(25) == Vector3.Zero && RoomLayout.Anchor(26) == new Vector3(0,7,0), "centers"));
Test("Room depth exceeds width", () => Check(RoomLayout.Depth > RoomLayout.Width, "rectangular Room"));
Test("Anchors are unique", () => Check(Enumerable.Range(1,26).Select(RoomLayout.Anchor).Distinct().Count() == 26, "duplicate Anchor"));
Test("Presentations are inside room", () => Check(Enumerable.Range(1,26).Select(RoomLayout.Presentation).All(p => Math.Abs(p.X)<6 && Math.Abs(p.Z)<9 && p.Y>0 && p.Y<7), "text inset"));

Test("Menu mode allows a missing Room ID", () =>
{
    var options = ViewerOptions.Parse(["--db", "menu.db"], requireRoom: false);
    Check(!options.HasRoom && options.DatabasePath == "menu.db", "menu options");
    Check(ViewerOptions.Parse(["--room", "8"], requireRoom: false).HasRoom, "explicit Room still loads");
});
Test("Catalog without Palaces lists every Room in one group", () =>
{
    var catalog = new PalaceCatalog().Load(fixture);
    var group = catalog.Single();
    Check(group.Id == PalaceCatalog.UngroupedPalaceId && group.Rooms.Count == 8, "one ungrouped list");
    Check(group.Rooms.Single(r => r.Id == 7).LociCount == 29 && group.Rooms.Single(r => r.Id == 24).Title.Contains("untitled"), "counts and titles");
    Check(group.Rooms.All(r => !r.HasImages && r.Missing == 0 && r.ImageSummary == "No images"), "no image columns");
});
Test("Catalog groups Rooms by Palace and reports image files", () =>
{
    var palaceDatabase = Path.Combine(directory, "palaces.db");
    var images = Path.Combine(directory, "palace images");
    Directory.CreateDirectory(images);
    File.WriteAllBytes(Path.Combine(images, "left.png"), [1]);
    using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = palaceDatabase, Pooling = false }.ToString()))
    {
        connection.Open();
        Execute(connection, $"""
            CREATE TABLE Palaces (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Description TEXT);
            CREATE TABLE Rooms (Id INTEGER PRIMARY KEY, PalaceId INTEGER, Title TEXT NOT NULL,
              LeftImagePath TEXT, RightImagePath TEXT, ForwardImagePath TEXT, BackImagePath TEXT, FloorImagePath TEXT, CeilingImagePath TEXT);
            CREATE TABLE Loci (Id INTEGER PRIMARY KEY, RoomId INTEGER, Position INTEGER, Text TEXT);
            INSERT INTO Palaces VALUES (1, 'Apartment', 'Okta'), (2, 'Empty palace', NULL);
            INSERT INTO Rooms (Id, PalaceId, Title, LeftImagePath, RightImagePath, FloorImagePath) VALUES
              (5, 1, 'Pictured', 'palace images/left.png', 'palace images/absent.png', '  '),
              (6, 1, 'Plain', NULL, NULL, NULL), (7, 99, 'Orphan', NULL, NULL, NULL);
            INSERT INTO Loci VALUES (1, 5, 1, 'a'), (2, 5, 2, 'b');
            """);
    }
    var catalog = new PalaceCatalog().Load(palaceDatabase);
    Check(catalog.Select(p => p.Name).SequenceEqual(["Apartment", "Empty palace", "Rooms without a Palace"]), "palace groups");
    var pictured = catalog[0].Rooms[0];
    Check(pictured.LociCount == 2 && pictured.Found == 1 && pictured.Missing == 1 && catalog[0].RoomsWithImages == 1, "image counts");
    Check(pictured.Images.Single(i => i.Wall == RoomWall.Left).FullPath == Path.Combine(images, "left.png"), "relative to database directory");
    Check(pictured.Images.Single(i => i.Wall == RoomWall.Floor).State == SurfaceImageState.None, "blank path means no image");
    Check(pictured.ImageSummary == "1/6 images · 1 missing" && catalog[1].Rooms.Count == 0, "summary");
});
Test("Catalog reports a missing database", () => Fails(() => new PalaceCatalog().Load(Path.Combine(directory, "absent.db")), "does not exist"));

Test("Rehearsal walks Positions in order once each", () =>
{
    var session = new RehearsalSession([10, 1, 26, 10]);
    Check(session.Positions.SequenceEqual([1, 10, 26]) && session.Current == 1 && session.Round == 1, "ordered start");
    Check(!session.Grade(true) && session.Current == 1, "grading before reveal is ignored");
    Check(session.Reveal() && !session.Reveal() && session.Revealed, "reveal once");
    Check(session.Grade(true) && !session.Revealed && session.Current == 10, "advances and hides");
});
Test("Rehearsal repeats only misses until a clean round", () =>
{
    var session = new RehearsalSession([1, 2, 3, 4]);
    void Answer(bool knew) { session.Reveal(); session.Grade(knew); }
    foreach (var knew in new[] { true, false, true, false }) Answer(knew);
    Check(session.Round == 2 && session.RoundPositions.SequenceEqual([2, 4]) && session.Current == 2, "round 2 is the misses");
    Answer(true); Answer(false);
    Check(session.Round == 3 && session.RoundPositions.SequenceEqual([4]), "round 3 is the remaining miss");
    Answer(true);
    Check(session.IsComplete && session.Current is null && session.Round == 3, "clean round completes");
    Check(session.FirstPassMisses.SequenceEqual([2, 4]) && session.MissesByRound.Select(r => r.Count).SequenceEqual([2, 1, 0]), "miss history");
    Check(!session.Reveal() && !session.Grade(true), "complete session ignores input");
});
Test("Perfect rehearsal completes in one round", () =>
{
    var session = new RehearsalSession([5]);
    session.Reveal(); session.Grade(true);
    Check(session.IsComplete && session.Round == 1 && session.FirstPassMisses.Count == 0, "one round");
});
Test("Rehearsal needs a populated Room", () =>
{
    try { _ = new RehearsalSession([]); } catch (ArgumentException) { return; }
    throw new Exception("empty rehearsal allowed");
});
Test("Rehearsal log appends records outside the database", () =>
{
    var session = new RehearsalSession(Load(20).Loci.Keys);
    foreach (var knew in new[] { false, true, true, true }) { session.Reveal(); session.Grade(knew); }
    var log = Path.Combine(directory, "logs", "rehearsals.jsonl");
    var started = DateTimeOffset.Parse("2026-09-28T10:00:00-04:00");
    RehearsalLog.Append(log, RehearsalRecord.From(session, Load(20), started, started.AddMinutes(3)));
    RehearsalLog.Append(log, RehearsalRecord.From(session, Load(20), started, started.AddMinutes(4)));
    var records = RehearsalLog.Read(log);
    Check(records.Count == 2 && File.ReadAllLines(log).Length == 2 && File.ReadAllText(log).Contains("\"firstPassMissedLocusIds\":[2001]"), "two camelCase lines");
    var record = records[0];
    Check(record.RoomId == 20 && record.StartedAt == started && record.Positions.SequenceEqual([1, 10, 26]), "room and time");
    Check(record.MissesByRound.Count == 2 && record.MissesByRound[0].SequenceEqual([1]) && record.MissesByRound[1].Count == 0, "misses by round");
    Check(RehearsalLog.Read(Path.Combine(directory, "absent.jsonl")).Count == 0, "missing log reads empty");
});

RoomImageChecks.Run(directory, Test);
Console.WriteLine($"{count - failures.Count}/{count} checks passed. Fixtures: {directory}");
return failures.Count == 0 ? 0 : 1;
