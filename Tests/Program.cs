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
Test("Anki card command follows the card script's rules", () =>
{
    var database = Path.Combine(directory, "anki palaces.db");
    var image = Path.Combine(directory, "room image.png");
    File.WriteAllBytes(image, [1]);
    using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
    {
        connection.Open();
        Execute(connection, $"""
            CREATE TABLE Palaces (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Description TEXT);
            CREATE TABLE Rooms (Id INTEGER PRIMARY KEY, PalaceId INTEGER, Title TEXT NOT NULL, PreviousId INTEGER, RoomImage TEXT);
            CREATE TABLE Loci (Id INTEGER PRIMARY KEY, RoomId INTEGER, Position INTEGER, Text TEXT);
            INSERT INTO Palaces VALUES (3, 'Okta', NULL);
            INSERT INTO Rooms VALUES (10, 3, 'Ready', NULL, '{image}'), (11, 3, 'Missing file', 10, '{Path.Combine(directory, "gone.png")}'),
              (12, 3, 'Relative', 11, 'room image.png'), (13, 3, 'Unlinked', 999, '{image}'), (14, 3, 'Empty', 12, '{image}'),
              (15, NULL, 'No Palace', NULL, '{image}'), (16, 3, 'No image', 10, '  ');
            INSERT INTO Loci VALUES (1, 10, 1, 'a'), (2, 11, 1, 'b'), (3, 12, 1, 'c'), (4, 13, 1, 'd'), (5, 15, 1, 'e'), (6, 16, 1, 'f');
            """);
    }
    var catalog = new PalaceCatalog().Load(database);
    AnkiCardCommand For(long id)
    {
        var palace = catalog.First(p => p.Rooms.Any(r => r.Id == id));
        return AnkiCards.For(palace, palace.Rooms.Single(r => r.Id == id), database, script: Path.Combine(directory, "absent.py"));
    }
    var ready = For(10);
    Check(ready.Ready && !ready.ScriptFound && ready.Command == $"python \"{Path.Combine(directory, "absent.py")}\" --db \"{database}\" --palace 3 --rooms 10", "ready command");
    Check(!For(11).Ready && For(11).Status.Contains("missing") && For(11).Command is null, "missing image file");
    Check(For(12).Status.Contains("relative") && For(13).Status.Contains("PreviousId") && For(14).Status.Contains("no Loci"), "relative, unlinked, empty");
    Check(For(15).Status.Contains("not in a Palace") && For(16).Status.Contains("no room image"), "no palace, no image");
    var legacy = new PalaceCatalog().Load(fixture).Single();
    Check(!AnkiCards.For(legacy, legacy.Rooms[0], fixture).Ready, "databases without Palaces are never ready");
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
Test("Loop drill repeats the same Positions lap after lap", () =>
{
    var drill = new LoopDrill([3, 1, 2, 2]);
    Check(drill.Positions.SequenceEqual([1, 2, 3]) && drill.Current == 1 && drill.Lap == 1, "ordered start");
    Check(!drill.Grade(true) && drill.Current == 1 && drill.TotalGraded == 0, "grading before reveal is ignored");
    Check(drill.Reveal() && !drill.Reveal(), "reveal once");
    Check(!drill.Grade(true) && drill.Current == 2 && !drill.Revealed, "advances and hides");
    drill.Reveal(); drill.Grade(false);
    drill.Reveal();
    Check(drill.Grade(true) && drill.Lap == 2 && drill.Current == 1 && drill.PreviousLapKnown == 2, "wraps to the first Position");
    Check(drill.LapKnown == 0 && drill.LapMissed == 0 && drill.TotalKnown == 2 && drill.TotalGraded == 3, "lap counts reset, totals keep");
    Check(drill.Streak == 1 && drill.BestStreak == 1, "a miss breaks the streak");
    for (var i = 0; i < 6; i++) { drill.Reveal(); drill.Grade(true); }
    Check(drill.Lap == 4 && drill.Current == 1 && drill.PreviousLapKnown == 3 && drill.BestStreak == 7, "misses are not singled out");
});
Test("Loop drill ranges parse and describe", () =>
{
    Check(LoopDrill.ParseRange("1-3").SequenceEqual([1, 2, 3]), "simple range");
    Check(LoopDrill.ParseRange(" 10 – 12 , 7,3-1 ").SequenceEqual([1, 2, 3, 7, 10, 11, 12]), "lists, en dash, reversed");
    Fails(() => LoopDrill.ParseRange("0-3"), "outside");
    Fails(() => LoopDrill.ParseRange("25-27"), "outside");
    Fails(() => LoopDrill.ParseRange("a-b"), "not a Position");
    Fails(() => LoopDrill.ParseRange(" , "), "such as");
    Check(LoopDrill.Describe([12, 1, 2, 3, 7, 10, 11]) == "1–3, 7, 10–12" && LoopDrill.Describe([5]) == "5", "describe");
    try { _ = new LoopDrill([]); } catch (ArgumentException) { return; }
    throw new Exception("empty loop drill allowed");
});
Test("Key binding defaults share keys only across contexts", () =>
{
    var keys = KeyBindings.Defaults();
    Check(keys.Matches(KeyAction.Knew, 'J') && keys.Matches(KeyAction.AllText, 'J') && keys.Matches(KeyAction.Knew, KeyBindings.Digit2), "J grades and toggles");
    Check(keys.Matches(KeyAction.Loop, KeyBindings.Slash) && keys.Matches(KeyAction.Loop, 'T') && !keys.Matches(KeyAction.Loop, KeyBindings.None), "loop keys");
    foreach (var a in KeyBindings.Actions)
        foreach (var b in KeyBindings.Actions)
            if (a != b && KeyBindings.Clash(a.Context, b.Context))
                Check(!keys.Keys(a.Action).Intersect(keys.Keys(b.Action)).Any(), $"{a.Name} clashes with {b.Name}");
});
Test("Assigning a key clears clashing bindings only", () =>
{
    var keys = KeyBindings.Defaults();
    var cleared = keys.Assign(KeyAction.Knew, 0, 'Q');
    Check(cleared.SequenceEqual([new KeySlot(KeyAction.Quit, 0)]) && keys.Get(KeyAction.Quit, 0) == KeyBindings.None && keys.Matches(KeyAction.Knew, 'Q'), "quiz clash cleared");
    Check(keys.Assign(KeyAction.Knew, 1, 'L').Count == 0 && keys.Matches(KeyAction.ThisText, 'L'), "exploring key reused in a quiz");
    cleared = keys.Assign(KeyAction.Sound, 0, 'L');
    Check(cleared.Count == 2 && !keys.Matches(KeyAction.ThisText, 'L') && !keys.Matches(KeyAction.Knew, 'L'), "anywhere key clears both contexts");
    Check(keys.Assign(KeyAction.Sound, 1, 'L').Count == 0 && keys.Get(KeyAction.Sound, 0) == KeyBindings.None && keys.Get(KeyAction.Sound, 1) == 'L', "moves between an action's own slots");
    Fails(() => keys.Assign(KeyAction.Quit, 0, KeyBindings.Escape), "reserved");
    keys.Clear(KeyAction.Reveal, 1);
    Check(!keys.Matches(KeyAction.Reveal, KeyBindings.Space), "clear");
});
Test("Key bindings round-trip and tolerate bad lines", () =>
{
    var keys = KeyBindings.Defaults();
    keys.Assign(KeyAction.WalkForward, 1, 'I');
    keys.Clear(KeyAction.Quit, 0);
    var text = keys.Serialize();
    var loaded = KeyBindings.Parse(text);
    Check(loaded.Serialize() == text && loaded.Matches(KeyAction.WalkForward, 'I') && loaded.Get(KeyAction.Quit, 0) == KeyBindings.None, "round trip");
    var partial = KeyBindings.Parse("Knew=85,0\nBogus=1,2\nMissed=abc\nQuit=4194305,0\nSound=1\n");
    Check(partial.Get(KeyAction.Knew, 0) == 'U' && partial.Matches(KeyAction.Missed, 'K') && partial.Matches(KeyAction.Quit, 'Q') && partial.Matches(KeyAction.Sound, 'V'), "bad lines keep defaults");
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
Test("Rehearsal tracks splits, combo and score", () =>
{
    var session = new RehearsalSession([1, 2, 3]);
    void Answer(bool knew, long ms) { session.Reveal(); session.Grade(knew, ms); }
    Answer(true, 2_000);
    Check(session.Combo == 1 && session.LastPoints == 100 + 0 + 80, "first recall: base plus speed bonus");
    Answer(true, 3_000);
    Check(session.Combo == 2 && session.LastPoints == 100 + 10 + 90, "combo bonus grows");
    Answer(false, 15_000);
    Check(session.Combo == 0 && session.BestCombo == 2 && session.LastPoints == 0, "miss breaks the combo");
    Check(session.FirstPassSplitsMs.SequenceEqual([2_000L, 3_000L, 15_000L]), "first-pass splits");
    Answer(true, 17_000);
    Check(session.IsComplete && session.LastPoints == RehearsalScoring.RetryPoints && session.ElapsedMs == 17_000, "retry points and time");
    Check(session.Score == 180 + 200 + 25 && session.PerfectBonus == 0, "total without perfect bonus");
    var perfect = new RehearsalSession([4]);
    perfect.Reveal(); perfect.Grade(true, 20_000);
    Check(perfect.Score == 100 + RehearsalScoring.PerfectBonusPerLocus && perfect.PerfectBonus == 50, "slow answers still earn base and perfect bonus");
});
Test("Medals reward accuracy and pace", () =>
{
    Check(RehearsalScoring.MedalFor(26, 26, 26 * 4_000) == Medal.Platinum, "platinum");
    Check(RehearsalScoring.MedalFor(26, 26, 26 * 6_000) == Medal.Gold, "gold");
    Check(RehearsalScoring.MedalFor(26, 26, 26 * 9_000) == Medal.Silver, "slow perfect is silver");
    Check(RehearsalScoring.MedalFor(10, 8, 1_000) == Medal.Silver && RehearsalScoring.MedalFor(10, 7, 1_000) == Medal.Bronze, "silver needs 80%");
    Check(RehearsalScoring.FormatTime(83_456) == "01:23.4" && RehearsalScoring.FormatDelta(-1_250) == "−1.3 s" && RehearsalScoring.FormatDelta(800) == "+0.8 s", "formatting");
});
DateTimeOffset At(int day, int hour = 12) => new(new DateTime(2026, 9, day, hour, 0, 0, DateTimeKind.Local));
RehearsalRun Run(long room, int day, long ms, int[] misses, int hour = 12, int loci = 3, int combo = 0, int score = 0) =>
    new(room, At(day, hour).AddMilliseconds(-ms), At(day, hour), ms, Enumerable.Range(1, loci).ToArray(),
        misses.Length == 0 ? [Array.Empty<int>()] : [misses, Array.Empty<int>()],
        Enumerable.Range(1, loci).Select(i => (long)i * 1000).ToArray(), misses.Select(p => (long)p).ToArray(), combo, score);
Test("Room progress reports personal bests", () =>
{
    var progress = RoomProgress.ByRoom([Run(8, 1, 30_000, [2]), Run(8, 2, 20_000, [1, 3]), Run(8, 3, 25_000, []), Run(9, 3, 5_000, [])]);
    var room = progress[8];
    Check(room.Fastest!.DurationMs == 20_000 && room.FastestPerfect!.DurationMs == 25_000, "fastest and fastest perfect");
    Check(room.BestFirstPass!.DurationMs == 25_000 && room.LastRehearsed == At(3), "best accuracy and last rehearsed");
    Check(room.Fastest.SplitFor(2) == 2_000 && room.Fastest.SplitFor(9) is null, "splits by Position");
    Check(progress[9].BestMedal == Medal.Platinum && !progress.ContainsKey(7), "per-room grouping");
});
Test("Daily streak counts consecutive days", () =>
{
    var today = new DateOnly(2026, 9, 28);
    Check(RehearsalStreak.Days([today, today.AddDays(-1), today.AddDays(-2), today.AddDays(-4)], today) == 3, "three days");
    Check(RehearsalStreak.Days([today.AddDays(-1), today.AddDays(-2)], today) == 2, "yesterday keeps the streak");
    Check(RehearsalStreak.Days([today.AddDays(-2)], today) == 0, "broken streak");
});
Test("Achievements unlock from run history", () =>
{
    var first = Run(8, 1, 60_000, [1, 2, 3], hour: 2);
    Check(Achievements.NewlyUnlocked([], first).Select(a => a.Id).SequenceEqual(["first-steps", "night-owl"]), "first run");
    var faster = Run(8, 2, 9_000, [], combo: 12, score: 5_200);
    var unlocked = Achievements.NewlyUnlocked([first], faster).Select(a => a.Id).ToArray();
    Check(unlocked.SequenceEqual(["flawless", "silver", "gold", "platinum", "combo-10", "personal-best", "high-score"]), string.Join(",", unlocked));
    var third = Run(9, 3, 50_000, [1, 2, 3, 4, 5], loci: 10);
    unlocked = Achievements.NewlyUnlocked([first, faster], third).Select(a => a.Id).ToArray();
    Check(unlocked.SequenceEqual(["comeback", "streak-3"]), string.Join(",", unlocked));
    Check(Achievements.Unlocked([first, faster, third]).Single(u => u.Achievement.Id == "flawless").UnlockedAt == At(2), "unlock time is the earning run");
});
Test("Outcome compares a run with the Room's history", () =>
{
    var first = RehearsalOutcome.Create([], Run(8, 1, 60_000, [1], score: 300), new DateOnly(2026, 9, 1));
    Check(first.FirstClear && !first.NewBestTime && !first.NewHighScore && first.NewBestMedal && first.StreakDays == 1 && first.Celebrate, "first clear");
    var history = new[] { Run(8, 1, 60_000, [1], score: 300), Run(9, 2, 1_000, [], score: 900) };
    var faster = RehearsalOutcome.Create(history, Run(8, 2, 50_000, [2], score: 250), new DateOnly(2026, 9, 2));
    Check(faster.NewBestTime && faster.PreviousBestMs == 60_000 && !faster.NewHighScore && faster.StreakDays == 2, "beats only this Room's time");
    var slower = RehearsalOutcome.Create(history, Run(8, 5, 70_000, [1], score: 100), new DateOnly(2026, 9, 5));
    Check(!slower.NewBestTime && !slower.NewBestMedal && !slower.Celebrate, "no celebration for a slower run");
});
Test("Learning time runs from the first loop drill to a flawless rehearsal of every Locus", () =>
{
    var runs = new[]
    {
        Run(8, 1, 9_000, []), Run(8, 2, 90_000, [4], loci: 26), Run(8, 3, 80_000, [], loci: 26), Run(8, 4, 70_000, [], loci: 26),
        Run(9, 1, 50_000, [], loci: 26), Run(11, 2, 50_000, [], loci: 26), Run(12, 2, 20_000, []),
    };
    var starts = new Dictionary<long, DateTimeOffset> { [8] = At(1, 9), [10] = At(2), [11] = At(3), [12] = At(1, 12) };
    IReadOnlyDictionary<int, long> Positions(int count) => Enumerable.Range(1, count).ToDictionary(p => p, p => (long)p);
    var loci = new Dictionary<long, IReadOnlyDictionary<int, long>> { [8] = Positions(26), [9] = Positions(26), [11] = Positions(26), [12] = Positions(3) };
    var learning = RoomLearning.ByRoom(starts, runs, loci);
    Check(learning[8].LearnedBy == runs[2] && learning[8].Duration == TimeSpan.FromHours(51), "a run before Loci were added, then a miss, do not count; the first flawless walk of every Locus does");
    Check(learning[12] is { Learned: true, Duration: { TotalHours: 24 } }, "a Room with fewer than 26 Loci is learned by a flawless walk of all of them");
    Check(learning[9] is { Learned: true, StartedAt: null, Duration: null }, "learned without a loop drill is untimed");
    Check(learning[10] is { Learned: false, Duration: null }, "still learning");
    Check(learning[11] is { Learned: true, Duration: null }, "learned before the first loop drill is untimed");
    Check(learning.Count == 5 && !learning.ContainsKey(7), "only Rooms with a start or a learning run");
    Check(RoomLearning.ByRoom(starts, runs)[8].LearnedBy == runs[0], "without current Loci, any flawless run counts");
    Check(RoomLearning.Format(TimeSpan.FromHours(51)) == "2 d 3 h" && RoomLearning.Format(new TimeSpan(3, 12, 30)) == "3 h 12 min" &&
        RoomLearning.Format(TimeSpan.FromMinutes(14.5)) == "14 min" && RoomLearning.Format(TimeSpan.FromSeconds(20)) == "under a minute", "formatting");
});
Test("Outcome marks the rehearsal that learns a Room", () =>
{
    var starts = new Dictionary<long, DateTimeOffset> { [8] = At(4, 9), [9] = At(1, 10) };
    var history = new[] { Run(9, 2, 50_000, [], loci: 26), Run(8, 4, 60_000, [3], loci: 26) };
    var learned = RehearsalOutcome.Create(history, Run(8, 4, 70_000, [], hour: 15, loci: 26), new DateOnly(2026, 9, 4), learningStarts: starts);
    Check(learned.JustLearned is { Duration: { } took } && took == TimeSpan.FromHours(6) && learned.FastestLearnedBefore == TimeSpan.FromHours(26) && learned.Celebrate,
        "learned in 6 hours, faster than the 26 hours Room 9 took");
    var again = RehearsalOutcome.Create([.. history, learned.Run], Run(8, 5, 65_000, [], loci: 26), new DateOnly(2026, 9, 5), learningStarts: starts);
    Check(again.JustLearned is null && again.FastestLearnedBefore is null, "only the first flawless full walk learns a Room");
    var untimed = RehearsalOutcome.Create([], Run(12, 4, 70_000, [], loci: 26), new DateOnly(2026, 9, 4));
    Check(untimed.JustLearned is { Learned: true, Duration: null } && untimed.FastestLearnedBefore is null, "no loop drill, no learning time");
    Check(RehearsalOutcome.Create([], Run(8, 4, 9_000, []), new DateOnly(2026, 9, 4), learningStarts: starts).JustLearned is { Duration: { TotalHours: 3 } },
        "a Room with 3 Loci is learned by a flawless walk of all 3");
    var grown = RehearsalOutcome.Create([Run(8, 4, 9_000, [])], Run(8, 5, 70_000, [], loci: 26), new DateOnly(2026, 9, 5), learningStarts: starts);
    Check(grown.JustLearned is { Learned: true }, "a Room learned before it gained Loci is learned again by a flawless walk of them all");
});
Test("Build-up loop grows backward after three clean laps in a row", () =>
{
    var drill = LoopDrill.BuildUpFrom([26, 1, 10]);
    void Lap(params bool[] answers) { foreach (var knew in answers) { drill.Reveal(); drill.Grade(knew); } }
    Check(drill is { IsBuildUp: true, Positions: [26], CleanLaps: 0, Current: 26 }, "starts with the last Position alone");
    Lap(true); Lap(true); Lap(false);
    Check(drill is { CleanLaps: 0, Positions.Count: 1, Lap: 4 }, "a missed lap resets the clean laps");
    Lap(true); Lap(true);
    Check(drill is { CleanLaps: 2, Added: null }, "two clean laps are not enough");
    Lap(true);
    Check(drill is { Positions: [10, 26], Added: 10, CleanLaps: 0, Lap: 1, PreviousLapKnown: null, Current: 10 }, "the third adds the Position before");
    Lap(true, false); Lap(true, true); Lap(true, true);
    Check(drill is { Positions.Count: 2, CleanLaps: 2 } && drill.Added is null, "one miss in a lap restarts the count");
    Lap(true, true);
    Check(drill is { Positions: [1, 10, 26], Complete: false }, "grows to the first Position");
    Lap(true, true, true); Lap(true, true, true);
    Check(!drill.Complete, "not complete before three clean laps of every Position");
    Lap(true, true, true);
    Check(drill is { Complete: true, Added: null } && !drill.Reveal(), "three clean laps of every Position complete it");
    Check(LoopDrill.BuildUpFrom([1, 10, 26], 5).Positions.SequenceEqual([10, 26]), "b 5 starts at the first populated Position from 5 on");
    Fails(() => LoopDrill.BuildUpFrom([1, 10], 11), "No populated Positions");
    var plain = new LoopDrill([1]);
    for (var i = 0; i < 5; i++) { plain.Reveal(); plain.Grade(true); }
    Check(plain is { IsBuildUp: false, Positions.Count: 1, Complete: false, CleanLaps: 5 }, "a plain loop never grows");
});
Test("Loop prompt reads b as a build-up", () =>
{
    Check(LoopDrill.TryParseBuildUp(" b ", out var from) && from is null, "b alone");
    Check(LoopDrill.TryParseBuildUp("B 18", out from) && from == 18 && LoopDrill.TryParseBuildUp("b18", out from) && from == 18, "b with a start");
    Check(LoopDrill.TryParseBuildUp("build 7", out from) && from == 7, "build");
    Check(!LoopDrill.TryParseBuildUp("1-3", out _) && !LoopDrill.TryParseBuildUp("", out _), "ranges are not build-ups");
    Fails(() => LoopDrill.TryParseBuildUp("b 27", out _), "1–26");
    Fails(() => LoopDrill.TryParseBuildUp("b 1-3", out _), "like b 18");
});
Test("Rehearsal store creates its table on first save only", () =>
{
    var database = Path.Combine(directory, "rehearsal.db");
    File.Copy(fixture, database);
    var store = new RehearsalStore();
    var before = SHA256.HashData(File.ReadAllBytes(database));
    Check(store.Load(database).Count == 0 && before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(database))), "load without table changes nothing");
    var session = new RehearsalSession(Load(20).Loci.Keys);
    foreach (var (knew, ms) in new[] { (false, 1_000L), (true, 2_500L), (true, 4_000L), (true, 6_000L) }) { session.Reveal(); session.Grade(knew, ms); }
    var run = RehearsalRun.From(session, Load(20), At(28, 9), At(28, 9).AddSeconds(6));
    var saved = store.Save(database, run);
    var second = store.Save(database, run with { CompletedAt = At(28, 10), DurationMs = 5_000 });
    Check(saved.Id > 0 && second.Id > saved.Id, "ids assigned");
    var runs = store.Load(database);
    Check(runs.Count == 2 && runs[0].Id == saved.Id && runs[0].StartedAt == At(28, 9) && runs[0].DurationMs == 6_000, "round trip");
    Check(runs[0].Positions.SequenceEqual([1, 10, 26]) && runs[0].MissesByRound[0].SequenceEqual([1]) && runs[0].SplitsMs.SequenceEqual([1_000L, 2_500L, 4_000L]), "json columns");
    Check(runs[0].FirstPassMissedLocusIds.SequenceEqual([2001L]) && runs[0].Score == session.Score && runs[0].BestCombo == 2, "ids, score, combo");
    Check(repository.Load(new(8, database)).Loci.Count == 26, "Rooms and Loci still load");
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
    connection.Open();
    using (var command = connection.CreateCommand())
    {
        command.CommandText = "SELECT Medal, FirstPassKnown, Rounds FROM RehearsalRuns WHERE Id = " + saved.Id;
        using var reader = command.ExecuteReader();
        Check(reader.Read() && reader.GetString(0) == run.Medal.ToString() && reader.GetInt32(1) == 2 && reader.GetInt32(2) == 2, "queryable summary columns");
    }
    Execute(connection, "INSERT INTO RehearsalRuns (RoomId, StartedAt, CompletedAt, DurationMs, LociCount, FirstPassKnown, Rounds, BestCombo, Score, Medal, Positions, MissesByRound, SplitsMs, FirstPassMissedLocusIds) VALUES (8, 'garbage', 'garbage', 1, 1, 1, 1, 0, 0, 'None', '[]', '[]', '[]', '[]')");
    Check(store.Load(database).Count == 2, "malformed rows are skipped");
});
Test("Rehearsal store adds LocusIds to an older table", () =>
{
    var database = Path.Combine(directory, "legacy-rehearsal.db");
    File.Copy(fixture, database);
    using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString()))
    {
        connection.Open();
        // The schema as first released, before LocusIds.
        Execute(connection, "CREATE TABLE RehearsalRuns (Id INTEGER PRIMARY KEY, RoomId INTEGER NOT NULL, StartedAt TEXT NOT NULL, CompletedAt TEXT NOT NULL, " +
            "DurationMs INTEGER NOT NULL, LociCount INTEGER NOT NULL, FirstPassKnown INTEGER NOT NULL, Rounds INTEGER NOT NULL, BestCombo INTEGER NOT NULL, " +
            "Score INTEGER NOT NULL, Medal TEXT NOT NULL, Positions TEXT NOT NULL, MissesByRound TEXT NOT NULL, SplitsMs TEXT NOT NULL, FirstPassMissedLocusIds TEXT NOT NULL)");
        using (var columns = connection.CreateCommand())
        {
            columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RehearsalRuns') WHERE name = 'LocusIds'";
            Check((long)columns.ExecuteScalar()! == 0, "legacy table has no LocusIds column");
        }
        Execute(connection, "INSERT INTO RehearsalRuns (RoomId, StartedAt, CompletedAt, DurationMs, LociCount, FirstPassKnown, Rounds, BestCombo, Score, Medal, Positions, MissesByRound, SplitsMs, FirstPassMissedLocusIds) VALUES (20, '2026-09-01T12:00:00+00:00', '2026-09-01T12:00:05+00:00', 5000, 3, 3, 1, 3, 300, 'Gold', '[1,10,26]', '[[]]', '[1,2,3]', '[]')");
    }
    var store = new RehearsalStore();
    Check(store.Load(database) is [{ LocusIds: null }], "legacy row loads without LocusIds");
    var session = new RehearsalSession(Load(20).Loci.Keys);
    while (session.Current is not null) { session.Reveal(); session.Grade(true, 1_000); }
    store.Save(database, RehearsalRun.From(session, Load(20), At(2), At(2).AddSeconds(3)));
    var runs = store.Load(database);
    Check(runs.Count == 2 && runs[0].LocusIds is null && runs[1].LocusIds!.SequenceEqual([2001L, 2010L, 2026L]), "column added and filled for new runs");
});
Test("Loop drill store keeps each Room's first start only", () =>
{
    var database = Path.Combine(directory, "loop-drills.db");
    File.Copy(fixture, database);
    var store = new RehearsalStore();
    byte[] Hash() => SHA256.HashData(File.ReadAllBytes(database));
    var untouched = Hash();
    Check(store.LoadFirstLoopDrills(database).Count == 0 && untouched.SequenceEqual(Hash()), "load without table changes nothing");
    Check(store.RecordFirstLoopDrill(database, 8, At(1, 9)), "the first drill starts the clock");
    var afterFirst = Hash();
    Check(!store.RecordFirstLoopDrill(database, 8, At(2, 9)) && afterFirst.SequenceEqual(Hash()), "later drills change nothing");
    Check(store.RecordFirstLoopDrill(database, 20, At(3)), "each Room has its own clock");
    var starts = store.LoadFirstLoopDrills(database);
    Check(starts.Count == 2 && starts[8] == At(1, 9) && starts[20] == At(3), "round trip");
    Check(store.Load(database).Count == 0 && repository.Load(new(8, database)).Loci.Count == 26, "rehearsal history, Rooms and Loci unaffected");
    using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = database, Pooling = false }.ToString());
    connection.Open();
    Execute(connection, "INSERT INTO FirstLoopDrills VALUES (21, 'garbage')");
    Check(store.LoadFirstLoopDrills(database).Count == 2, "malformed rows are skipped");
});
Test("FSRS scheduler loads from the Rust library", () =>
{
    Check(Fsrs.Parameters.Count == 21 && Fsrs.Decay is > 0 and < 1, "default FSRS-6 parameters");
    var memories = Fsrs.MemoryStates([
        [new(FsrsReview.Good, 0)],
        [new(FsrsReview.Good, 0), new(FsrsReview.Good, 3)],
        [new(FsrsReview.Good, 0), new(FsrsReview.Good, 3), new(FsrsReview.Again, 10)]]);
    Check(memories[1].Stability > memories[0].Stability && memories[2].Stability < memories[1].Stability, "success grows stability, a lapse lowers it");
    var s = memories[1];
    Check(Math.Abs(Fsrs.Retrievability(s, 0) - 1) < 1e-6 && Math.Abs(Fsrs.Retrievability(s, s.Stability) - .9) < 1e-3, "recall is 90% at stability");
    Check(Fsrs.Retrievability(s, 30) < Fsrs.Retrievability(s, 10), "recall decays");
    try { Fsrs.MemoryStates([[new(5, 0)]]); } catch (ViewerException) { return; }
    throw new Exception("invalid rating accepted");
});
Test("Review planner forecasts Loci from first passes", () =>
{
    IReadOnlyDictionary<long, IReadOnlyDictionary<int, long>> loci = new Dictionary<long, IReadOnlyDictionary<int, long>>
    {
        [8] = new Dictionary<int, long> { [1] = 801, [2] = 802, [3] = 803, [4] = 804 },
        [9] = new Dictionary<int, long> { [1] = 901, [2] = 902, [3] = 903 },
        [10] = new Dictionary<int, long> { [1] = 1001 },
    };
    var runs = new[]
    {
        Run(8, 1, 1_000, [2]), Run(8, 1, 1_000, [], hour: 15), Run(8, 3, 1_000, [2]),
        Run(9, 3, 1_000, []) with { LocusIds = [999, 902, 903] },
        Run(42, 3, 1_000, []), Run(9, 9, 1_000, [1, 2, 3]),
    };
    var forecast = ReviewPlanner.Forecast(runs, loci, new DateOnly(2026, 9, 4));
    var room8 = forecast[8];
    Check(room8.LastRehearsed == new DateOnly(2026, 9, 3) && room8.Tested.Count == 3 && room8.UntestedPositions.SequenceEqual([4]), "Locus 4 never tested");
    Check(room8.Tested.Single(l => l.Position == 2).Reviews == 2 && room8.Tested.Single(l => l.Position == 1).Reviews == 2, "one rehearsal per Room per day");
    Check(room8.Tested.Single(l => l.Position == 2).Recall < room8.Tested.Single(l => l.Position == 1).Recall, "misses lower recall");
    Check(room8.WeakPositions.SequenceEqual([2, 4]) && room8.Due, "weak spots");
    Check(forecast[9].UntestedPositions.SequenceEqual([1]) && forecast[9].Tested.Count == 2, "recorded Locus Ids beat today's Positions; future runs are ignored");
    Check(!forecast[10].Rehearsed && !forecast[10].Due && !forecast.ContainsKey(42), "new Rooms and unknown Rooms");
    var ranked = ReviewPlanner.Ranked(forecast.Values);
    Check(ranked.Select(f => f.RoomId).SequenceEqual([8, 9]) && ranked[0].ExpectedForgotten > ranked[1].ExpectedForgotten, "ranked by likely-forgotten Loci");
    Check(ReviewPlanner.Ranked(ReviewPlanner.Forecast([], loci, new DateOnly(2026, 9, 4)).Values).Count == 0, "nothing rehearsed, nothing due");
});
Test("Rehearsal store never creates a missing database", () =>
{
    var absent = Path.Combine(directory, "no-rehearsals.db");
    Fails(() => new RehearsalStore().Save(absent, Run(8, 1, 1_000, [])), "does not exist");
    Fails(() => new RehearsalStore().RecordFirstLoopDrill(absent, 8, At(1)), "does not exist");
    Check(!File.Exists(absent), "must not create");
});

RoomImageChecks.Run(directory, Test);
Console.WriteLine($"{count - failures.Count}/{count} checks passed. Fixtures: {directory}");
return failures.Count == 0 ? 0 : 1;
