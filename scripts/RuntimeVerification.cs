using System.Text.Json;
using Godot;
using Microsoft.Data.Sqlite;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

// Opt-in acceptance runner used by scripts/verify.ps1, including in the exported binary.
// It does not alter database content. Reports and captures only go to the requested test directory.
public static class RuntimeVerification
{
    public const int InputDevice = 91026;
    public static async Task Run(RoomViewer viewer, string outputDirectory)
    {
        var checks = new List<string>();
        var result = new Dictionary<string, object?>();
        try
        {
            Directory.CreateDirectory(outputDirectory);
            async Task Frame(int count = 2)
            {
                for (var i = 0; i < count; i++) await viewer.ToSignal(viewer.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            void Check(bool condition, string name)
            {
                if (!condition) throw new InvalidOperationException(name);
                checks.Add(name);
            }
            async Task Capture(string name)
            {
                await Frame(5);
                if (DisplayServer.GetName() == "headless") return;
                await viewer.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = viewer.GetViewport().GetTexture().GetImage();
                var error = image.SavePng(Path.Combine(outputDirectory, name + ".png"));
                Check(error == Error.Ok, $"Capture {name}");
            }
            void Keypress(Key key)
            {
                Input.ParseInputEvent(new InputEventKey { Device = InputDevice, PhysicalKeycode = key, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Device = InputDevice, PhysicalKeycode = key, Pressed = false });
                Input.FlushBufferedEvents();
            }
            await Frame(8);
            result["roomId"] = viewer.Room?.Id;
            result["loaded"] = viewer.Room is not null;
            result["error"] = viewer.Hud.ErrorText;
            result["warnings"] = viewer.Room?.Warnings.Select(w => w.ToString()).ToArray() ?? [];
            result["wallTextures"] = viewer.WallTextures.Paths.ToDictionary(p => p.Key.ToString().ToLowerInvariant(), p => p.Value);
            result["textureWarnings"] = viewer.WallTextures.Warnings;
            result["loci"] = viewer.Room?.Loci.Values.OrderBy(l => l.Position).ToArray() ?? [];
            // Load the Rust FSRS scheduler now so the export check can see where it came from.
            Check(Fsrs.Parameters.Count == 21, "FSRS scheduler loads");
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                result["nativeModules"] = process.Modules.Cast<System.Diagnostics.ProcessModule>()
                    .Where(m => m.ModuleName is "e_sqlite3.dll" or "coreclr.dll" or "fsrs_ffi.dll")
                    .Select(m => m.FileName).ToArray();
            }
            Check(viewer.Displays.Count == 26, "All 26 Anchors exist");
            Check(viewer.Displays.All(d => d.Marker.Visible), "Markers start visible");
            Check(viewer.Displays.All(d => !d.Billboard.Visible), "Text starts hidden");
            result["menuOpen"] = viewer.Menu.IsOpen;
            result["palaces"] = viewer.Menu.Catalog.Select(p => new { p.Id, p.Name, Rooms = p.Rooms.Select(r => new { r.Id, r.Title, r.LociCount, r.Found, r.Missing }) }).ToArray();
            if (viewer.Room is null && !viewer.Hud.HasError)
            {
                Check(viewer.Menu.IsOpen, "Starting without --room opens the Palace menu");
                Check(viewer.Menu.Catalog.Sum(p => p.Rooms.Count) > 0, "Palace menu lists Rooms");
                Check(!viewer.Player.Enabled, "Walking disabled while choosing a Room");
                Check(!viewer.Menu.CanClose, "Menu cannot close with no Room loaded");
                await Capture("menu");
            }
            else if (viewer.Room is null)
            {
                Check(viewer.Hud.HasError && viewer.Hud.ErrorText.Length > 0, "Failure is visible");
                Check(!viewer.Player.Enabled, "Walking disabled on failed load");
                await Capture("error");
            }
            else
            {
                Check(!viewer.Hud.HasError, "Successful load has no error");
                Check(viewer.Room.Warnings.All(w => viewer.Hud.WarningText.Contains(w.ToString())), "Every skipped Locus appears in visible warnings");
                Check(viewer.WallTextures.Warnings.All(w => viewer.Hud.WarningText.Contains(w)), "Every wall image failure appears in visible warnings");
                Check(viewer.Displays.All(d => d.LogicalAnchor == RoomGeometry.ToGodot(RoomLayout.Anchor(d.PositionNumber))), "Spatial mapping matches stable Position identity");
                await Capture("markers");
                var player = viewer.Player;
                player.Enabled = false; // Verification drives movement deterministically on physics ticks.
                var startPosition = player.Position;
                var startCameraRotation = player.Camera.Rotation;
                foreach (var wall in viewer.WallTextures.Paths.Keys)
                {
                    var surface = viewer.GetNode<MeshInstance3D>($"Texture{wall}");
                    Check(surface.Mesh is QuadMesh && surface.MaterialOverride is StandardMaterial3D { AlbedoTexture: ImageTexture }, $"{wall} uses a decoded external image texture");
                    var inward = wall switch
                    {
                        RoomWall.Forward => Vector3.Forward, RoomWall.Back => Vector3.Back,
                        RoomWall.Left => Vector3.Right, RoomWall.Right => Vector3.Left,
                        RoomWall.Floor => Vector3.Up, _ => Vector3.Down
                    };
                    Check(surface.GlobalBasis.Z.Dot(inward) > .99f, $"{wall} image faces into the Room");
                    if (wall is RoomWall.Floor or RoomWall.Ceiling)
                    {
                        Check(viewer.GetNode<MeshInstance3D>(wall == RoomWall.Floor ? "FloorJoint" : "CeilingLight").Visible, $"{wall} retains existing details");
                        Check(surface.GlobalBasis.Y.Dot(Vector3.Forward) > .99f, $"{wall} image top points toward BACK");
                    }
                    else
                    {
                        var prefix = wall == RoomWall.Forward ? "Front" : wall.ToString();
                        Check(viewer.GetNode<MeshInstance3D>(prefix + "Band").Visible && viewer.GetNode<MeshInstance3D>(prefix + "Seam").Visible, $"{wall} retains the existing grid");
                    }
                    player.Position = wall switch { RoomWall.Floor => new Vector3(0, 3, 7), RoomWall.Ceiling => new Vector3(0, 0, -5), _ => Vector3.Zero };
                    player.Camera.LookAt(surface.GlobalPosition);
                    await Capture("wall-" + wall.ToString().ToLowerInvariant());
                }
                Check(viewer.GetNode<MeshInstance3D>("FrontInset").Visible == !viewer.WallTextures.Paths.ContainsKey(RoomWall.Forward), "Front image replaces the inset only when loaded");
                player.Position = startPosition;
                player.Camera.Rotation = startCameraRotation;
                void AimAt(LocusDisplay display)
                {
                    player.Position = display.Viewpoint;
                    player.Camera.LookAt(display.GlobalPosition);
                }
                var populated = viewer.Displays.Where(d => d.Locus is not null).ToArray();
                foreach (var display in populated)
                {
                    AimAt(display);
                    Keypress(Key.L);
                    Check(viewer.Displays.All(d => d.Billboard.Visible == (d == display)), $"L reveals only Position {display.PositionNumber} from hidden text");
                    Check(viewer.Hud.ReadingVisible, "L-revealed text is available in the reading panel");
                    Input.ParseInputEvent(new InputEventKey { Device = InputDevice, PhysicalKeycode = Key.L, Pressed = true, Echo = true });
                    Input.FlushBufferedEvents();
                    Check(display.Billboard.Visible, "L ignores key repeat");
                    if (display == populated[0]) await Capture("single-text");
                    Keypress(Key.L);
                    Check(!viewer.TextVisible && !viewer.Hud.ReadingVisible, $"L hides Position {display.PositionNumber} and its reading panel");
                }
                foreach (var display in viewer.Displays.Where(d => d.Locus is null))
                {
                    AimAt(display);
                    Keypress(Key.L);
                    Check(!viewer.TextVisible, $"L on empty Position {display.PositionNumber} does nothing");
                }
                if (populated.Length > 0)
                {
                    var display = populated[0];
                    AimAt(display);
                    display.UpdateScale(player.Camera);
                    var markerCenter = display.GlobalPosition + player.Camera.GlobalBasis.Y * display.Marker.Offset.Y * display.Marker.PixelSize * display.Marker.Scale.Y;
                    player.Camera.LookAt(markerCenter);
                    Keypress(Key.L);
                    Check(display.Billboard.Visible, "L can target the numbered marker");
                    Keypress(Key.K);
                    Keypress(Key.L);
                    Check(!viewer.TextVisible && viewer.Displays.All(d => !d.Marker.Visible), "L works with markers hidden and leaves them hidden");
                    Keypress(Key.K);
                    AimAt(display);
                    Keypress(Key.L);
                    player.Position = new(-5, 0, -8);
                    player.Camera.LookAt(new Vector3(-4.95f, -1, -8));
                    var visibilityBefore = viewer.Displays.Select(d => d.Billboard.Visible).ToArray();
                    await Frame();
                    Check(visibilityBefore.SequenceEqual(viewer.Displays.Select(d => d.Billboard.Visible)), "Looking away preserves individual visibility");
                    Keypress(Key.L);
                    Check(visibilityBefore.SequenceEqual(viewer.Displays.Select(d => d.Billboard.Visible)), "L with no target changes nothing");
                    if (populated.Length > 1)
                    {
                        AimAt(populated[1]);
                        Keypress(Key.L);
                        Check(display.Billboard.Visible && populated[1].Billboard.Visible && viewer.Displays.Count(d => d.Billboard.Visible) == 2, "L reveals another Locus without hiding the first");
                    }
                    Keypress(Key.J);
                    Check(!viewer.TextVisible, "J hides all text after individual reveals");
                    Keypress(Key.J);
                    AimAt(display);
                    Keypress(Key.L);
                    Check(viewer.Displays.All(d => d.Billboard.Visible == (d.Locus is not null && d != display)), "L hides only its target after J shows all text");
                    if (viewer.TextVisible) Keypress(Key.J);
                }
                if (populated.Length > 0)
                {
                    // Rehearsal history goes to a copy: verification never writes the database under test.
                    var progressDatabase = Path.Combine(outputDirectory, "progress.db");
                    using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(viewer.DatabasePath), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
                    using (var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = progressDatabase, Pooling = false }.ToString()))
                    {
                        source.Open();
                        copy.Open();
                        source.BackupDatabase(copy);
                    }
                    viewer.ProgressDatabasePath = progressDatabase;
                    var store = new RehearsalStore();
                    var earlierRuns = store.Load(progressDatabase).Count;
                    var learnedEarlier = store.Load(progressDatabase).Any(r => r.RoomId == viewer.Room.Id && RoomLearning.Learns(r, viewer.Room.Loci.Keys));
                    var clockStartedEarlier = store.LoadFirstLoopDrills(progressDatabase).ContainsKey(viewer.Room.Id);
                    var markerColors = viewer.Displays.Select(d => d.Marker.Modulate).ToArray();
                    viewer.GuideSeconds = 0;
                    Keypress(Key.R);
                    await Frame(3);
                    Check(viewer.Hud.RehearsalText.Contains("TIME") && viewer.Hud.RehearsalText.Contains("SCORE"), "Rehearsal shows a live timer and score");
                    var session = viewer.Rehearsal;
                    Check(session is not null && session.Current == populated[0].PositionNumber, "R starts rehearsal at the first populated Position");
                    Check(!viewer.TextVisible && !viewer.Hud.ReadingVisible && viewer.Hud.RehearsalVisible, "Rehearsal hides text until revealed");
                    var misses = new List<int>();
                    while (session!.Round == 1 && session.Current is { } position)
                    {
                        var target = viewer.Displays[position - 1];
                        target.UpdateScale(player.Camera);
                        Check(target.IsUnderCrosshair(player.Camera, out _), $"Rehearsal guides the view to Position {position}");
                        Keypress(Key.Key2);
                        Keypress(Key.J);
                        Keypress(Key.K);
                        Keypress(Key.L);
                        Check(session.Current == position && !viewer.TextVisible && viewer.MarkersVisible, $"Position {position} stays hidden until Space; J and K do not toggle");
                        Keypress(Key.Space);
                        Check(viewer.Displays.All(d => d.Billboard.Visible == (d == target)) && viewer.Hud.ReadingVisible, $"Space reveals only Position {position}");
                        if (position == populated[0].PositionNumber) await Capture("rehearsal-reveal");
                        var miss = position == populated[0].PositionNumber || position == populated[^1].PositionNumber;
                        if (miss) misses.Add(position);
                        Keypress(miss ? Key.K : Key.J);
                        Check(!target.Billboard.Visible, $"Grading hides Position {position}");
                    }
                    Check(session.Round == 2 && session.RoundPositions.SequenceEqual(misses) && viewer.Hud.RehearsalText.Contains("M I S S E S   O N L Y"), "Round 2 repeats only the misses");
                    while (session.Current is not null)
                    {
                        Keypress(Key.Space);
                        Keypress(Key.Key2);
                    }
                    Check(session.IsComplete && session.FirstPassMisses.SequenceEqual(misses) && viewer.Hud.RehearsalText.Contains("R O O M   C L E A R"), "A clean round clears the Room");
                    var outcome = viewer.LastOutcome;
                    Check(outcome is { SaveError: null } && viewer.Hud.RehearsalText.Contains(outcome.Run.Medal.ToString().ToUpperInvariant()) && viewer.Hud.RehearsalText.Contains("Score"), "Result shows medal, time and score");
                    await Capture("rehearsal-complete");
                    var runs = store.Load(progressDatabase);
                    Check(runs.Count == earlierRuns + 1 && runs[^1].RoomId == viewer.Room.Id && runs[^1].MissesByRound[0].SequenceEqual(misses) &&
                        runs[^1].FirstPassMissedLocusIds.Count == misses.Count && runs[^1].SplitsMs.Count == populated.Length, "Completed rehearsal is saved to RehearsalRuns");
                    if (earlierRuns == 0)
                        Check(outcome!.FirstClear && outcome.Unlocked.Any(a => a.Id == "first-steps") && viewer.Hud.RehearsalText.Contains("UNLOCKED"), "First clear unlocks First Steps");
                    Keypress(Key.Space);
                    Check(viewer.Rehearsal is { Round: 1, IndexInRound: 0 } && viewer.LastOutcome is null, "Space starts another rehearsal");
                    session = viewer.Rehearsal;
                    while (session!.Current is not null)
                    {
                        Keypress(Key.H);
                        Check(session.Revealed, "H reveals during a rehearsal");
                        Keypress(Key.Key2);
                        if (populated.Length > 1 && session.IndexInRound == 1)
                            Check(viewer.Hud.RehearsalText.Contains("vs best"), "Ghost split compares with the best run");
                    }
                    Check(viewer.LastOutcome is { Run.Perfect: true, SaveError: null } && viewer.Hud.RehearsalText.Contains("flawless"), "Flawless second run");
                    if (earlierRuns == 0) Check(viewer.LastOutcome!.Unlocked.Any(a => a.Id == "flawless"), "Flawless unlocks its trophy");
                    if (!learnedEarlier)
                        Check(viewer.LastOutcome!.JustLearned is { Learned: true } && viewer.Hud.RehearsalText.Contains("ROOM LEARNED"), "A flawless walk of every Locus learns the Room");
                    else Check(viewer.LastOutcome!.JustLearned is null && !viewer.Hud.RehearsalText.Contains("ROOM LEARNED"), "Only a Room's first flawless walk of every Locus learns it");
                    Check(store.Load(progressDatabase).Count == earlierRuns + 2, "Every completed rehearsal is saved");
                    Keypress(Key.M);
                    Check(viewer.Menu.IsOpen && viewer.Menu.Runs.Count == earlierRuns + 2 && viewer.Menu.StatsText.Contains("streak"), "Palace menu shows rehearsal stats");
                    Check(viewer.Menu.DetailsText.Contains("high score") && viewer.Menu.DetailsText.Contains("Best "), "Palace menu shows the Room's personal bests");
                    await Capture("menu-progress");
                    Keypress(Key.M);
                    Check(!viewer.Menu.IsOpen && viewer.Rehearsal is not null, "Closing the menu returns to the result");
                    Keypress(Key.R);
                    Check(viewer.Rehearsal is null && !viewer.TextVisible && !viewer.Hud.RehearsalVisible && !player.Guiding, "R ends rehearsal and hides text");
                    Keypress(Key.R);
                    Keypress(Key.Slash);
                    Check(viewer.Rehearsal is not null && !viewer.Hud.DrillPromptVisible, "/ does nothing during a rehearsal");
                    Keypress(Key.Q);
                    Check(viewer.Rehearsal is null && !viewer.Hud.RehearsalVisible, "Q ends rehearsal");
                    Check(viewer.Displays.Select(d => d.Marker.Modulate).SequenceEqual(markerColors), "Ending rehearsal restores marker colors");

                    Keypress(Key.Slash);
                    Check(viewer.Hud.DrillPromptVisible && !player.Enabled, "/ opens the loop range prompt");
                    Keypress(Key.Escape);
                    Check(!viewer.Hud.DrillPromptVisible && viewer.Drill is null && player.Enabled, "Escape cancels the loop prompt");
                    Keypress(Key.T);
                    Check(viewer.Hud.DrillPromptVisible, "T also opens the loop range prompt");
                    viewer.StartDrill("27");
                    Check(viewer.Hud.DrillPromptVisible && viewer.Drill is null && viewer.Hud.DrillPromptError.Contains("outside"), "An invalid range keeps the prompt open");
                    var loop = populated.Take(3).Select(d => d.PositionNumber).ToArray();
                    viewer.StartDrill(LoopDrill.Describe(loop).Replace('–', '-'));
                    var drill = viewer.Drill;
                    Check(drill is not null && !viewer.Hud.DrillPromptVisible && drill.Positions.SequenceEqual(loop) && drill.Current == loop[0], "Loop drill starts at the first Position in range");
                    Check(viewer.Hud.RehearsalText.Contains("L O O P") && !viewer.TextVisible, "Loop drill hides text until revealed");
                    var learningStarts = store.LoadFirstLoopDrills(progressDatabase);
                    Check(learningStarts.ContainsKey(viewer.Room.Id) && viewer.Hud.RehearsalText.Contains("Learning clock started") == !clockStartedEarlier,
                        "The Room's first loop drill starts its learning clock");
                    for (var lap = 1; lap <= 3; lap++)
                        foreach (var position in loop)
                        {
                            var target = viewer.Displays[position - 1];
                            target.UpdateScale(player.Camera);
                            Check(drill!.Lap == lap && drill.Current == position && target.IsUnderCrosshair(player.Camera, out _), $"Loop lap {lap} guides the view to Position {position}");
                            Keypress(lap == 2 ? Key.H : Key.Space);
                            Check(viewer.Displays.All(d => d.Billboard.Visible == (d == target)), $"Space or H reveals only Position {position} in the loop");
                            if (lap == 1 && position == loop[0]) await Capture("loop-reveal");
                            Keypress(position == loop[0] ? Key.K : Key.J);
                        }
                    Check(drill!.Lap == 4 && drill.Current == loop[0] && drill.PreviousLapKnown == loop.Length - 1, "Loop drill keeps lapping, misses included");
                    Keypress(Key.R);
                    Check(viewer.Rehearsal is null && viewer.Drill is not null, "R does nothing during a loop drill");
                    Keypress(Key.Slash);
                    Check(viewer.Drill is null && viewer.Hud.DrillPromptVisible, "/ during a loop drill picks a new range");
                    viewer.StartDrill(LoopDrill.Describe(loop).Replace('–', '-'));
                    Check(viewer.Drill is { Lap: 1, IndexInLap: 0 }, "A new range starts a fresh loop");
                    Check(store.LoadFirstLoopDrills(progressDatabase)[viewer.Room.Id] == learningStarts[viewer.Room.Id] && !viewer.Hud.RehearsalText.Contains("Learning clock"),
                        "Later loop drills keep the first start");
                    Keypress(Key.T);
                    Check(viewer.Drill is null && !viewer.Hud.RehearsalVisible, "T ends the loop drill");
                    viewer.StartDrill(LoopDrill.Describe(loop).Replace('–', '-'));
                    Keypress(Key.Q);
                    Check(viewer.Drill is null && !viewer.TextVisible && !viewer.Hud.RehearsalVisible && !player.Guiding, "Q ends the loop drill");

                    // Build-up: starts with the last Locus; three clean laps in a row add the one before it.
                    void Answer(bool knew) { Keypress(Key.Space); Keypress(knew ? Key.J : Key.K); }
                    var last = populated[^1].PositionNumber;
                    viewer.StartDrill("b");
                    var buildUp = viewer.Drill;
                    Check(buildUp is { IsBuildUp: true } && buildUp.Positions.SequenceEqual([last]) && buildUp.Current == last &&
                        viewer.Hud.RehearsalText.Contains("B U I L D   U P") && viewer.Hud.RehearsalText.Contains("CLEAN LAPS  0/3"), "b starts a build-up at the last Locus");
                    if (populated.Length > 1)
                    {
                        Answer(true); Answer(true); Answer(false);
                        Check(buildUp!.CleanLaps == 0 && buildUp.Positions.Count == 1, "A missed lap resets the clean laps");
                        Answer(true); Answer(true); Answer(true);
                        var grownTo = new[] { populated[^2].PositionNumber, last };
                        Check(buildUp.Positions.SequenceEqual(grownTo) && buildUp.Current == grownTo[0] && viewer.Hud.RehearsalText.Contains("added Position"),
                            "Three clean laps in a row add the Locus before");
                        await Capture("build-up");
                        Keypress(Key.Q);
                        Check(viewer.Drill is null && !viewer.Hud.RehearsalVisible, "Q stops a build-up");
                        Keypress(Key.T);
                        Check(viewer.Hud.DrillPromptRange == $"b {grownTo[0]}-{last}", "The loop prompt offers to resume the build-up");
                        Keypress(Key.Escape);
                    }
                    if (populated.Length > 1)
                    {
                        // A build-up ending before the last Locus keeps looping what it built.
                        var end = populated[^2].PositionNumber;
                        viewer.StartDrill($"b {end}");
                        Check(viewer.Drill is { IsBuildUp: true } partial && partial.Positions.SequenceEqual([end]), "b with a Position builds up to it");
                        viewer.StartDrill($"b {populated[0].PositionNumber}-{end}");
                        for (var i = 0; i < LoopDrill.CleanLapsToGrow * (populated.Length - 1); i++) Answer(true);
                        Check(viewer.Drill is { IsBuildUp: false } built && built.Positions.SequenceEqual(populated[..^1].Select(d => d.PositionNumber)) &&
                            viewer.Rehearsal is null && viewer.Hud.RehearsalText.Contains("Built up"), "A finished partial build-up keeps looping what it built");
                        viewer.StopDrill();
                    }
                    // Resuming with the whole Room in the loop; three clean laps turn it into the rehearsal.
                    viewer.StartDrill($"b {populated[0].PositionNumber}-{last}");
                    Check(viewer.Drill is { IsBuildUp: true } whole && whole.Positions.SequenceEqual(populated.Select(d => d.PositionNumber)), "b with a range resumes the build-up");
                    for (var i = 0; i < LoopDrill.CleanLapsToGrow * populated.Length; i++) Answer(true);
                    Check(viewer.Drill is null && viewer.Rehearsal is { Round: 1, IndexInRound: 0 } && viewer.Hud.RehearsalText.Contains("R E H E A R S E") &&
                        viewer.Hud.RehearsalText.Contains("Built up all"), "A finished build-up turns into the rehearsal");
                    Keypress(Key.Q);
                    Check(viewer.Rehearsal is null && store.Load(progressDatabase).Count == earlierRuns + 2, "Quitting that rehearsal saves nothing");

                    // Regression: Enter in the loop prompt re-captures the mouse, and the re-centring motion
                    // used to cancel the glide to the first station. Uses a real glide, not an instant one.
                    viewer.GuideSeconds = .7f;
                    viewer.StartDrill(LoopDrill.Describe(loop).Replace('–', '-'));
                    player.CaptureMouse();
                    // Headless runs cannot capture a real mouse, so the motion goes straight to the look handler.
                    player.MouseLook(new(60, 0));
                    Check(player.Guiding && player.SettlingCapture, "Capturing the mouse does not cancel the glide to the first loop station");
                    // Wait in real time: a timer counts the whole current frame, which can be long when a window
                    // renders after many synchronous steps, and could fire before the capture has settled.
                    var settled = Time.GetTicksMsec() + 300;
                    while (Time.GetTicksMsec() < settled) await Frame(1);
                    player.MouseLook(new(60, 0));
                    Check(!player.Guiding, "Looking around after the capture settles still cancels the glide");
                    viewer.StopDrill();
                    viewer.GuideSeconds = 0;
                    Input.MouseMode = Input.MouseModeEnum.Visible;
                    Check(viewer.Displays.Select(d => d.Marker.Modulate).SequenceEqual(markerColors), "Ending the loop drill restores marker colors");

                    var keysMenu = viewer.KeysMenu;
                    Keypress(Key.F1);
                    Check(keysMenu.IsOpen && !player.Enabled, "F1 opens the key bindings menu");
                    await Capture("key-bindings");
                    Keypress(Key.R);
                    Check(viewer.Rehearsal is null, "Keys do not leak through the key bindings menu");
                    keysMenu.StartCapture(new KeySlot(KeyAction.Knew, 0));
                    Keypress(Key.U);
                    Check(keysMenu.Capturing is null && Keys.Current.Get(KeyAction.Knew, 0) == (long)Key.U, "A pressed key is captured into the chosen slot");
                    keysMenu.Capture(new KeySlot(KeyAction.Missed, 0), (long)Key.Q);
                    Check(Keys.Current.Get(KeyAction.Quit, 0) == KeyBindings.None && keysMenu.MessageText.Contains("removed from Quit"), "A clashing key moves and the menu says so");
                    keysMenu.Capture(new KeySlot(KeyAction.WalkForward, 1), (long)Key.I);
                    Check(InputMap.ActionGetEvents("walk_forward").OfType<InputEventKey>().Any(e => e.PhysicalKeycode == Key.I), "Walking keys follow their bindings");
                    var saved = Path.Combine(outputDirectory, "keybindings.cfg");
                    Check(File.Exists(saved) && File.ReadAllText(saved).Contains($"Knew={(long)Key.U},"), "Bindings are saved");
                    Keypress(Key.Escape);
                    Check(!keysMenu.IsOpen && player.Enabled, "Escape closes the key bindings menu");
                    Check(viewer.Hud.ControlsText.Contains("F1   Keys") && viewer.Hud.ControlsText.StartsWith("WASD   Walk"), "Controls bar shows the bindings");
                    Keypress(Key.R);
                    Keypress(Key.Space);
                    Keypress(Key.J);
                    Check(viewer.Rehearsal is { IndexInRound: 0, Revealed: true } && viewer.TextVisible, "A key moved off Knew it no longer grades");
                    Check(viewer.Hud.RehearsalText.Contains("U / 2   Knew it") && viewer.Hud.RehearsalText.Contains("Q / 1   Missed"), "Prompts show rebound keys");
                    Keypress(Key.U);
                    Check(viewer.Rehearsal is { IndexInRound: 1 } or { IsComplete: true } && viewer.Rehearsal.FirstPassMisses.Count == 0, "The rebound key grades");
                    Keypress(Key.R);
                    Check(viewer.Rehearsal is null, "Rehearse key still ends the rehearsal");
                    Keypress(Key.M);
                    Keypress(Key.F1);
                    Check(viewer.Menu.IsOpen && keysMenu.IsOpen, "F1 opens key bindings over the Palace menu");
                    keysMenu.ResetDefaults();
                    Check(Keys.Current.Serialize() == KeyBindings.Defaults().Serialize() && !InputMap.ActionGetEvents("walk_forward").OfType<InputEventKey>().Any(e => e.PhysicalKeycode == Key.I), "Reset restores defaults");
                    Keypress(Key.Escape);
                    Check(!keysMenu.IsOpen && viewer.Menu.IsOpen, "Closing key bindings returns to the Palace menu");
                    Keypress(Key.M);
                    Check(!viewer.Menu.IsOpen, "Palace menu closes after key bindings");

                    // FSRS: everything was rehearsed today, so nothing is weak yet.
                    Keypress(Key.Slash);
                    Check(viewer.Hud.DrillPromptVisible && viewer.Hud.DrillWeakText.Contains("none"), "Freshly rehearsed Loci are not weak spots");
                    Keypress(Key.Escape);
                    // Sixty days later recall has decayed below 90% for every Locus.
                    viewer.ForecastDay = DateOnly.FromDateTime(DateTime.Now).AddDays(60);
                    Keypress(Key.Slash);
                    Check(viewer.Hud.DrillWeakText.Contains("Weak spots (below 90% recall)"), "Decayed Loci are offered as weak spots");
                    Keypress(Key.Tab);
                    var populatedPositions = populated.Select(d => d.PositionNumber).ToArray();
                    Check(viewer.Hud.DrillPromptRange == LoopDrill.Describe(populatedPositions), "TAB fills the weak spots into the range");
                    await Capture("weak-spots");
                    viewer.StartDrill(viewer.Hud.DrillPromptRange);
                    Check(viewer.Drill is { } weakDrill && weakDrill.Positions.SequenceEqual(populatedPositions), "Weak spots start a loop drill");
                    viewer.StopDrill();
                    Keypress(Key.M);
                    var forecast = viewer.Menu.Forecasts[viewer.Room.Id];
                    Check(forecast.Due && forecast.Tested.Count == populated.Length && forecast.Tested.All(l => l.Reviews == 1), "Same-day rehearsals count once");
                    Check(viewer.Menu.StatsText.Contains("due") && viewer.Menu.DetailsText.Contains("RECALL"), "Palace menu shows due Rooms and recall");
                    Check(viewer.Menu.DetailsText.Contains("LEARNED") && viewer.Menu.StatsText.Contains("learned"), "Palace menu shows the Room as learned");
                    viewer.Menu.ToggleReviewNext(true);
                    Check(viewer.Menu.DetailsText.Contains("Review next") && viewer.Menu.DetailsText.Contains("likely forgotten"), "Review next ranks due Rooms");
                    await Capture("review-next");
                    viewer.Menu.SelectRoom(viewer.Room.Id);
                    Check(viewer.Menu.DetailsText.Contains("RECALL") && !viewer.Menu.DetailsText.Contains("likely forgotten"), "A Review next link opens the Room");
                    Check(viewer.Menu.DetailsText.Contains("ANKI CARDS"), "Room details show whether Anki cards can be built");
                    if (viewer.Menu.CanCopyAnkiCommand)
                    {
                        viewer.Menu.PressCopyAnkiCommand();
                        Check(viewer.Menu.CopiedCommand is { } copied && copied.Contains("db_to_anki_room_cloze.py") && copied.EndsWith($"--rooms {viewer.Room.Id}") &&
                            viewer.Menu.HintText.Contains("Copied"), "Copy Anki command puts the card command on the clipboard");
                        result["ankiCommand"] = viewer.Menu.CopiedCommand;
                    }
                    else Check(viewer.Menu.DetailsText.Contains("Not ready") && viewer.Menu.CopiedCommand is null, "Rooms the card script would skip offer no command");
                    Keypress(Key.M);
                    viewer.ForecastDay = null;
                }
                Check(viewer.Displays.All(d => d.Marker.Visible), "Individual text toggles preserve marker visibility");
                player.Position = startPosition;
                player.Camera.Rotation = startCameraRotation;
                Keypress(Key.J);
                Check(viewer.Displays.Count(d => d.Billboard.Visible) == viewer.Room.Loci.Count, "J shows every populated Billboard");
                Check(viewer.Displays.Where(d => d.Locus is not null).All(d => d.Billboard.Text == d.Locus!.Text), "Billboards retain complete text");
                Keypress(Key.K);
                Check(viewer.Displays.All(d => !d.Marker.Visible), "K hides markers");
                Check(viewer.Displays.Count(d => d.Billboard.Visible) == viewer.Room.Loci.Count, "K leaves populated text visible");
                Keypress(Key.J);
                Check(viewer.Displays.All(d => !d.Billboard.Visible && !d.Marker.Visible), "J hides text independently of markers");
                Keypress(Key.K);
                Keypress(Key.J);
                Input.ParseInputEvent(new InputEventKey { Device = InputDevice, PhysicalKeycode = Key.J, Pressed = true, Echo = true });
                Input.FlushBufferedEvents();
                Check(viewer.Displays.Count(d => d.Billboard.Visible) == viewer.Room.Loci.Count, "Key repeat does not retrigger toggle");
                await Capture("front-text");
                player.Position = new(0, 0, 1);
                player.Camera.LookAt(new Vector3(0, .75f, 0));
                await Capture("floor-text");
                player.Camera.LookAt(new Vector3(0, 6.15f, 0));
                await Capture("ceiling-text");
                player.Position = new(0, 0, -3);
                player.Camera.LookAt(new Vector3(4.75f, 3.5f, 7.75f));
                await Capture("corner-text");
                if (DisplayServer.GetName() != "headless")
                {
                    // Inspect every Position from a suitable walking viewpoint with all text enabled.
                    foreach (var display in viewer.Displays.Where(d => d.Locus is not null))
                    {
                        var target = display.GlobalPosition;
                        var horizontal = new Vector3(target.X, 0, target.Z);
                        player.Position = horizontal.Length() > 2 ? horizontal - horizontal.Normalized() * 3 : new Vector3(0, 0, 1.7f);
                        player.Camera.LookAt(target);
                        await Capture($"position-{display.PositionNumber:00}");
                    }
                }
                player.Camera.Rotation = Vector3.Zero;
                player.Rotation = Vector3.Zero;
                player.Look(new Vector2(0, -100000));
                Check(Mathf.Abs(player.Camera.Rotation.X) <= RoomLayout.PitchLimit + .001f, "Pitch has an upper limit");
                player.Look(new Vector2(0, 200000));
                Check(Mathf.Abs(player.Camera.Rotation.X) <= RoomLayout.PitchLimit + .001f, "Pitch has a lower limit");
                // Exercise all four physical walls while pitched, at realistic physics steps.
                foreach (var (start, input) in new[]
                {
                    (new Vector3(0, 0, -8.5f), new Vector2(0, -1)),
                    (new Vector3(0, 0, 8.5f), new Vector2(0, 1)),
                    (new Vector3(-5.5f, 0, 0), new Vector2(-1, 0)),
                    (new Vector3(5.5f, 0, 0), new Vector2(1, 0))
                })
                {
                    player.Position = start;
                    for (var i = 0; i < 18; i++)
                    {
                        await viewer.ToSignal(viewer.GetTree(), SceneTree.SignalName.PhysicsFrame);
                        player.Walk(input);
                    }
                    Check(Mathf.Abs(player.Position.X) <= 5.71f && Mathf.Abs(player.Position.Z) <= 8.71f, $"Wall collision at {start}");
                    Check(Mathf.IsEqualApprox(player.Camera.GlobalPosition.Y, RoomLayout.EyeHeight), "Walking and looking keep fixed eye height");
                }
                player.Position = Vector3.Zero;
                player.Rotation = new(0, Mathf.Pi / 2, 0);
                await viewer.ToSignal(viewer.GetTree(), SceneTree.SignalName.PhysicsFrame);
                player.Walk(new Vector2(0, -1));
                Check(player.Position.X < 0 && Mathf.Abs(player.Position.Z) < .001f, "W follows horizontal camera heading");
                Check(viewer.Displays.All(d => d.LogicalAnchor == RoomGeometry.ToGodot(RoomLayout.Anchor(d.PositionNumber))), "Turning and walking never relocate Anchors");
                // A headless display driver cannot capture a physical mouse.
                if (DisplayServer.GetName() != "headless")
                {
                    viewer._UnhandledInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
                    Check(Input.MouseMode == Input.MouseModeEnum.Captured, "Click captures mouse");
                    Keypress(Key.Escape);
                    Check(Input.MouseMode == Input.MouseModeEnum.Visible, "Escape releases mouse");
                }
            }
            result["passed"] = true;
            result["checks"] = checks;
            File.WriteAllText(Path.Combine(outputDirectory, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"ACCEPTANCE PASSED: {checks.Count} checks. {outputDirectory}");
            viewer.GetTree().Quit();
        }
        catch (Exception ex)
        {
            result["passed"] = false;
            result["checks"] = checks;
            result["failure"] = ex.ToString();
            File.WriteAllText(Path.Combine(outputDirectory, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            GD.PrintErr(ex);
            viewer.GetTree().Quit(1);
        }
    }
}
