using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

public partial class RoomViewer : Node3D
{
    public WalkingCamera Player { get; private set; } = null!;
    public ViewerHud Hud { get; private set; } = null!;
    public PalaceMenu Menu { get; private set; } = null!;
    public KeyBindingsMenu KeysMenu { get; private set; } = null!;
    public string DatabasePath { get; private set; } = ViewerOptions.DefaultDatabasePath;
    public List<LocusDisplay> Displays { get; } = [];
    public RoomSnapshot? Room { get; private set; }
    public WallTextureSources WallTextures { get; private set; } = new(new Dictionary<RoomWall, string>(), []);
    public bool TextVisible => Displays.Any(d => d.Billboard.Visible);
    public bool MarkersVisible { get; private set; } = true;
    public RehearsalSession? Rehearsal { get; private set; }
    public RehearsalOutcome? LastOutcome { get; private set; }
    public LoopDrill? Drill { get; private set; }
    // Where rehearsal history is read and saved. Verification points this at a copy.
    public string ProgressDatabasePath { get; set; } = ViewerOptions.DefaultDatabasePath;
    // The day FSRS forecasts are made for; verification moves it forward to see recall decay.
    public DateOnly? ForecastDay { get; set; }
    private DateOnly Today => ForecastDay ?? DateOnly.FromDateTime(DateTime.Now);
    // Seconds the camera takes to glide to each rehearsal Position; 0 snaps.
    public float GuideSeconds { get; set; } = .7f;
    private DateTimeOffset _rehearsalStarted;
    private double _rehearsalSeconds;
    private IReadOnlyList<RehearsalRun> _history = [];
    // Each Room's first loop drill: when its learning clock started.
    private IReadOnlyDictionary<long, DateTimeOffset> _learningStarts = new Dictionary<long, DateTimeOffset>();
    // The Room's fastest run; its splits are the ghost to beat.
    private RehearsalRun? _ghost;
    private string _feedback = "";
    private RehearsalSounds _sounds = null!;
    private long RehearsalMs => (long)(_rehearsalSeconds * 1000);
    private bool _loaded;
    private bool _verificationActive;
    // A Room chosen in the menu survives the scene reload that displays it.
    private static ViewerOptions? s_pendingSelection;
    // The last loop range typed, offered again next time (also after loading another Room).
    private static string s_drillRange = "1-3";

    public override void _Ready()
    {
        _verificationActive = !string.IsNullOrWhiteSpace(OS.GetEnvironment("PALACE_VIEWER_SMOKE_DIR"));
        if (_verificationActive) GetWindow().Unfocusable = true;
        // Verification uses default keys and never touches the user's saved bindings.
        Keys.Load(_verificationActive ? Path.Combine(OS.GetEnvironment("PALACE_VIEWER_SMOKE_DIR"), "keybindings.cfg")
            : ProjectSettings.GlobalizePath("user://keybindings.cfg"));
        RoomGeometry.Build(this);
        Player = new WalkingCamera { Name = "Walker" };
        AddChild(Player);
        Hud = new ViewerHud { Name = "Hud" };
        AddChild(Hud);
        Menu = new PalaceMenu { Name = "Menu" };
        AddChild(Menu);
        Menu.RoomChosen += LoadRoom;
        Menu.CloseRequested += CloseMenu;
        Hud.ChooseRoomRequested += OpenMenu;
        KeysMenu = new KeyBindingsMenu { Name = "KeyBindings" };
        AddChild(KeysMenu);
        KeysMenu.Closed += CloseKeyBindings;
        KeysMenu.BindingsChanged += Hud.RefreshKeys;
        Menu.KeyBindingsRequested += OpenKeyBindings;
        Hud.DrillRangeSubmitted += StartDrill;
        Hud.DrillPromptCancelled += CloseDrillPrompt;
        _sounds = new RehearsalSounds { Name = "Sounds" };
        AddChild(_sounds);
        var pending = s_pendingSelection;
        s_pendingSelection = null;
        ViewerOptions? options = null;
        try
        {
            options = pending ?? ViewerOptions.Parse(OS.GetCmdlineUserArgs(), requireRoom: false);
            DatabasePath = ProgressDatabasePath = options.DatabasePath;
            if (options.HasRoom)
            {
                Room = new RoomRepository().Load(options);
                WallTextures = RoomGeometry.ApplyTextures(this, options.WallTextures.Resolve(Room));
                Hud.ShowRoom(Room, WallTextures.Warnings);
                foreach (var warning in Room.Warnings) GD.Print($"WARNING: {warning}");
                foreach (var warning in WallTextures.Warnings) GD.Print($"WARNING: {warning}");
                foreach (var (wall, path) in WallTextures.Paths) GD.Print($"{wall} texture: {path}");
                GD.Print($"Loaded Room {Room.Id}: {Room.Loci.Count}/26 Positions, {Room.Warnings.Count} warnings. Database opened read-only.");
                _loaded = true;
            }
            else
            {
                // Started without --room: the Palace menu picks one.
                Hud.ShowNoRoom();
                Player.Enabled = false;
            }
        }
        catch (Exception ex)
        {
            var message = ex is ViewerException ? ex.Message : $"The viewer could not load the Room.\n{ex.Message}";
            Hud.ShowError(message);
            GD.PrintErr(message);
            Player.Enabled = false;
        }

        for (var position = 1; position <= RoomLayout.Capacity; position++)
        {
            var locus = Room?.Loci.GetValueOrDefault(position);
            var display = new LocusDisplay();
            display.Initialize(position, locus);
            AddChild(display);
            Displays.Add(display);
        }
        Input.MouseMode = _loaded && DisplayServer.GetName() != "headless" ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        if (options is { HasRoom: false }) OpenMenu();
        if (_verificationActive)
            CallDeferred(MethodName.RunVerification);
    }

    public override void _Input(InputEvent @event)
    {
        // Automated visual runs must not consume unrelated desktop keystrokes.
        if (_verificationActive && @event.Device != RuntimeVerification.InputDevice)
            GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (KeysMenu.IsOpen)
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (Menu.IsOpen)
        {
            // The menu owns input while open; only its close keys and the key bindings key pass through here.
            if (@event is InputEventKey { Pressed: true, Echo: false } menuKey)
            {
                if (Keys.Is(KeyAction.KeyBindings, menuKey)) OpenKeyBindings();
                else if (Menu.CanClose && (Keys.Is(KeyAction.PalaceMenu, menuKey) || IsEscape(menuKey))) CloseMenu();
            }
            GetViewport().SetInputAsHandled();
            return;
        }
        if (Hud.DrillPromptVisible)
        {
            // The range field has focus; anything it does not use goes nowhere.
            if (@event is InputEventKey { Pressed: true, Echo: false } promptKey)
            {
                if (IsEscape(promptKey)) CloseDrillPrompt();
                else if (promptKey.PhysicalKeycode == Key.Tab || promptKey.Keycode == Key.Tab) Hud.FillWeakSpots();
            }
            if (@event is InputEventKey or InputEventMouseButton) GetViewport().SetInputAsHandled();
            return;
        }
        if (Rehearsal is not null && HandleRehearsalInput(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (Drill is not null && HandleDrillInput(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            var handled = true;
            var quizzable = _loaded && Room!.Loci.Count > 0;
            if (IsEscape(key)) Input.MouseMode = Input.MouseModeEnum.Visible;
            else if (quizzable && Keys.Is(KeyAction.Rehearse, key)) StartRehearsal();
            else if (quizzable && Keys.Is(KeyAction.Loop, key)) OpenDrillPrompt();
            else if (Keys.Is(KeyAction.PalaceMenu, key)) OpenMenu();
            else if (Keys.Is(KeyAction.KeyBindings, key)) OpenKeyBindings();
            else if (Keys.Is(KeyAction.Sound, key))
            {
                RehearsalSounds.Enabled = !RehearsalSounds.Enabled;
                _feedback = $"[color=#93a7ac]Sound {(RehearsalSounds.Enabled ? "on" : "off")}[/color]";
            }
            else if (_loaded && Keys.Is(KeyAction.AllText, key)) ToggleText();
            else if (_loaded && Keys.Is(KeyAction.Markers, key)) ToggleMarkers();
            else if (_loaded && Keys.Is(KeyAction.ThisText, key)) ToggleFocusedText();
            else handled = false;
            if (handled) GetViewport().SetInputAsHandled();
        }
        if (_loaded && @event is InputEventMouseButton { Pressed: true } mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                Player.CaptureMouse();
                ToggleFocusedText();
                GetViewport().SetInputAsHandled();
            }
            else if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                ToggleText();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private static bool IsEscape(InputEventKey key) => key.PhysicalKeycode == Key.Escape || key.Keycode == Key.Escape;

    // Exploring keys are swallowed during a quiz so nothing is revealed early.
    private static bool IsExploring(InputEventKey key) =>
        Keys.Is(KeyAction.AllText, key) || Keys.Is(KeyAction.ThisText, key) || Keys.Is(KeyAction.Markers, key);

    public void OpenKeyBindings()
    {
        Player.Enabled = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        KeysMenu.Open();
    }

    // Returns to the Palace menu if it was open underneath, otherwise to the Room.
    private void CloseKeyBindings()
    {
        Hud.RefreshKeys();
        if (Menu.IsOpen) return;
        Player.Enabled = _loaded;
        if (_loaded && DisplayServer.GetName() != "headless") Player.CaptureMouse();
    }

    public void OpenMenu()
    {
        Player.Enabled = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Hud.SetErrorVisible(false);
        Menu.Open(DatabasePath, Room?.Id, canClose: _loaded || Hud.HasError, ProgressDatabasePath, Today);
    }

    public void CloseMenu()
    {
        if (!Menu.CanClose) return;
        Menu.Close();
        Hud.SetErrorVisible(true);
        Player.Enabled = _loaded;
        if (_loaded && DisplayServer.GetName() != "headless") Player.CaptureMouse();
    }

    // Rebuilds the scene for the chosen Room. CLI image overrides applied only to the starting Room.
    private void LoadRoom(long roomId)
    {
        s_pendingSelection = new ViewerOptions(roomId, DatabasePath);
        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
    }

    // While rehearsing, exploring keys are disabled so nothing is revealed early (J and K grade by default).
    // Reveal also starts another run from the result card; Quit or Rehearse ends.
    private bool HandleRehearsalInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } button)
        {
            if (button.ButtonIndex == MouseButton.Left) Player.CaptureMouse();
            return button.ButtonIndex is MouseButton.Left or MouseButton.Right;
        }
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return false;
        if (Keys.Is(KeyAction.Quit, key) || Keys.Is(KeyAction.Rehearse, key)) StopRehearsal();
        else if (Keys.Is(KeyAction.Reveal, key)) { if (Rehearsal!.IsComplete) StartRehearsal(); else RevealRehearsal(); }
        else if (Keys.Is(KeyAction.Missed, key)) GradeRehearsal(knew: false);
        else if (Keys.Is(KeyAction.Knew, key)) GradeRehearsal(knew: true);
        else return IsExploring(key) || Keys.Is(KeyAction.Loop, key);
        return true;
    }

    private LocusDisplay? RehearsalDisplay => Rehearsal?.Current is { } position ? Displays[position - 1] : null;

    public void StartRehearsal()
    {
        Rehearsal = new RehearsalSession(Room!.Loci.Keys);
        LastOutcome = null;
        _rehearsalStarted = DateTimeOffset.Now;
        _rehearsalSeconds = 0;
        _feedback = "";
        try { _history = new RehearsalStore().Load(ProgressDatabasePath); }
        catch (ViewerException ex)
        {
            _history = [];
            GD.PrintErr(ex.Message);
        }
        try { _learningStarts = new RehearsalStore().LoadFirstLoopDrills(ProgressDatabasePath); }
        catch (ViewerException ex)
        {
            _learningStarts = new Dictionary<long, DateTimeOffset>();
            GD.PrintErr(ex.Message);
        }
        _ghost = new RoomProgress(Room.Id, _history.Where(r => r.RoomId == Room.Id).ToArray()).Fastest;
        foreach (var display in Displays)
        {
            display.Billboard.Visible = false;
            display.SetCue(MarkerCue.Normal);
        }
        ShowRehearsalStep();
    }

    public void StopRehearsal()
    {
        Rehearsal = null;
        Player.CancelGuide();
        foreach (var display in Displays)
        {
            display.Billboard.Visible = false;
            display.SetCue(MarkerCue.Normal);
        }
        Hud.HideRehearsal();
        Hud.ShowReading(null);
    }

    private void RevealRehearsal()
    {
        if (!Rehearsal!.Reveal()) return;
        RehearsalDisplay!.Billboard.Visible = true;
        Hud.ShowReading(RehearsalDisplay);
        _sounds.Play(RehearsalSound.Reveal);
        Hud.ShowRehearsal(Rehearsal, RehearsalMs, _ghost, _feedback);
    }

    private void GradeRehearsal(bool knew)
    {
        var display = RehearsalDisplay;
        var firstPass = Rehearsal!.Round == 1;
        if (display is null || !Rehearsal.Grade(knew, RehearsalMs)) return;
        display.Billboard.Visible = false;
        display.SetCue(knew ? MarkerCue.Known : MarkerCue.Missed);
        Hud.ShowReading(null);
        _sounds.Play(knew ? RehearsalSound.Knew : RehearsalSound.Missed, Rehearsal.Combo);
        _feedback = Rehearsal.LastPoints > 0 ? $"[color=#88d8c4][b]+{Rehearsal.LastPoints}[/b][/color]" : knew ? "" : "[color=#ee8a6b]miss[/color]";
        // Ghost split: how far ahead of or behind the Room's fastest run this answer came.
        if (firstPass && _ghost?.SplitFor(display.PositionNumber) is { } ghostMs)
        {
            var delta = Rehearsal.FirstPassSplitsMs[^1] - ghostMs;
            _feedback += $"   [color=#{(delta <= 0 ? "88d8c4" : "ee8a6b")}]{RehearsalScoring.FormatDelta(delta)}[/color] [color=#93a7ac]vs best[/color]";
        }
        if (Rehearsal.IsComplete) FinishRehearsal();
        else ShowRehearsalStep();
    }

    private void FinishRehearsal()
    {
        var run = RehearsalRun.From(Rehearsal!, Room!, _rehearsalStarted, DateTimeOffset.Now);
        string? error = null;
        try
        {
            run = new RehearsalStore().Save(ProgressDatabasePath, run);
            GD.Print($"Rehearsal saved: Room {run.RoomId}, {RehearsalScoring.FormatTime(run.DurationMs)}, {run.Medal}, score {run.Score}.");
        }
        catch (ViewerException ex)
        {
            error = ex.Message.Replace('\n', ' ');
            GD.PrintErr(ex.Message);
        }
        LastOutcome = RehearsalOutcome.Create(_history, run, DateOnly.FromDateTime(DateTime.Now), error, _learningStarts);
        Hud.ShowRehearsalResult(LastOutcome);
        _sounds.Play(LastOutcome.Celebrate ? RehearsalSound.Record : RehearsalSound.Clear);
        if (LastOutcome.Celebrate) Hud.Celebrate();
    }

    private void ShowRehearsalStep()
    {
        var display = RehearsalDisplay!;
        display.SetCue(MarkerCue.Target);
        Player.Guide(display.Viewpoint, display.GlobalPosition, GuideSeconds);
        Hud.ShowRehearsal(Rehearsal!, RehearsalMs, _ghost, _feedback);
    }

    public void OpenDrillPrompt()
    {
        Player.Enabled = false;
        Player.CancelGuide();
        Input.MouseMode = Input.MouseModeEnum.Visible;
        ShowWeakSpots();
        Hud.ShowDrillPrompt(s_drillRange);
    }

    // FSRS over this Room's saved rehearsals, so the loop prompt can offer its weak spots.
    private void ShowWeakSpots()
    {
        try
        {
            var runs = new RehearsalStore().Load(ProgressDatabasePath);
            var loci = new Dictionary<long, IReadOnlyDictionary<int, long>> { [Room!.Id] = Room.Loci.Values.ToDictionary(l => l.Position, l => l.Id) };
            var forecast = ReviewPlanner.Forecast(runs, loci, Today)[Room.Id];
            Hud.SetWeakSpots(forecast.Rehearsed ? forecast.WeakPositions : null);
        }
        catch (ViewerException ex)
        {
            GD.PrintErr(ex.Message);
            Hud.SetWeakSpots(null, ex.Message.Split('\n')[0]);
        }
    }

    private void CloseDrillPrompt()
    {
        Hud.HideDrillPrompt();
        Player.Enabled = true;
        if (DisplayServer.GetName() != "headless") Player.CaptureMouse();
    }

    // Starts looping the populated Positions in range; an empty or invalid range keeps the prompt open.
    public void StartDrill(string range)
    {
        try
        {
            var positions = LoopDrill.ParseRange(range).Where(Room!.Loci.ContainsKey).ToArray();
            if (positions.Length == 0) throw new ViewerException($"No populated Positions in {range.Trim()}.");
            s_drillRange = range.Trim();
            CloseDrillPrompt();
            Drill = new LoopDrill(positions);
        }
        catch (ViewerException ex)
        {
            Hud.ShowDrillPrompt(range, ex.Message);
            return;
        }
        _feedback = StartLearningClock();
        foreach (var display in Displays)
        {
            display.Billboard.Visible = false;
            display.SetCue(MarkerCue.Normal);
        }
        ShowDrillStep();
    }

    // A Room's first loop drill starts its learning clock, which stops at its first flawless
    // rehearsal of all 26 Positions. Returns feedback for the drill panel.
    private string StartLearningClock()
    {
        try
        {
            return new RehearsalStore().RecordFirstLoopDrill(ProgressDatabasePath, Room!.Id, DateTimeOffset.Now)
                ? "[color=#f1d39b]Learning clock started[/color]   [color=#93a7ac]it stops at your first flawless rehearsal of all 26[/color]" : "";
        }
        catch (ViewerException ex)
        {
            GD.PrintErr(ex.Message);
            return $"[color=#edbf7f]Learning clock not saved: {ex.Message.Replace('\n', ' ').Replace("[", "[lb]")}[/color]";
        }
    }

    public void StopDrill()
    {
        Drill = null;
        Player.CancelGuide();
        foreach (var display in Displays)
        {
            display.Billboard.Visible = false;
            display.SetCue(MarkerCue.Normal);
        }
        Hud.HideRehearsal();
        Hud.ShowReading(null);
    }

    // Same keys as a rehearsal. Quit stops; the Loop key picks a new range.
    private bool HandleDrillInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } button)
        {
            if (button.ButtonIndex == MouseButton.Left) Player.CaptureMouse();
            return button.ButtonIndex is MouseButton.Left or MouseButton.Right;
        }
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return false;
        if (Keys.Is(KeyAction.Quit, key)) StopDrill();
        else if (Keys.Is(KeyAction.Loop, key)) { StopDrill(); OpenDrillPrompt(); }
        else if (Keys.Is(KeyAction.Reveal, key)) RevealDrill();
        else if (Keys.Is(KeyAction.Missed, key)) GradeDrill(knew: false);
        else if (Keys.Is(KeyAction.Knew, key)) GradeDrill(knew: true);
        else return IsExploring(key) || Keys.Is(KeyAction.Rehearse, key);
        return true;
    }

    private void RevealDrill()
    {
        if (!Drill!.Reveal()) return;
        var display = Displays[Drill.Current - 1];
        display.Billboard.Visible = true;
        Hud.ShowReading(display);
        _sounds.Play(RehearsalSound.Reveal);
        Hud.ShowDrill(Drill, _feedback);
    }

    private void GradeDrill(bool knew)
    {
        var display = Displays[Drill!.Current - 1];
        if (!Drill.Revealed) return;
        var lapDone = Drill.Grade(knew);
        display.Billboard.Visible = false;
        display.SetCue(knew ? MarkerCue.Known : MarkerCue.Missed);
        Hud.ShowReading(null);
        _sounds.Play(lapDone && Drill.PreviousLapKnown == Drill.Positions.Count ? RehearsalSound.Clear : knew ? RehearsalSound.Knew : RehearsalSound.Missed, Drill.Streak);
        _feedback = knew ? "[color=#88d8c4]knew it[/color]" : "[color=#ee8a6b]miss[/color]";
        if (lapDone) _feedback += $"   [color=#f1d39b]lap {Drill.Lap - 1} done · {Drill.PreviousLapKnown}/{Drill.Positions.Count}[/color]";
        ShowDrillStep();
    }

    private void ShowDrillStep()
    {
        var display = Displays[Drill!.Current - 1];
        display.SetCue(MarkerCue.Target);
        Player.Guide(display.Viewpoint, display.GlobalPosition, GuideSeconds);
        Hud.ShowDrill(Drill, _feedback);
    }

    public void ToggleText()
    {
        var show = !TextVisible;
        foreach (var display in Displays) display.Billboard.Visible = show && display.Locus is not null;
        UpdateReading(FindFocusedDisplay());
    }

    private void ToggleFocusedText()
    {
        var focus = FindFocusedDisplay();
        if (focus?.Locus is null) return;
        focus.Billboard.Visible = !focus.Billboard.Visible;
        UpdateReading(focus);
    }

    private LocusDisplay? FindFocusedDisplay()
    {
        LocusDisplay? focus = null;
        var nearest = float.PositiveInfinity;
        foreach (var display in Displays)
        {
            display.UpdateScale(Player.Camera);
            if (display.IsUnderCrosshair(Player.Camera, out var depth) && depth < nearest)
            {
                nearest = depth;
                focus = display;
            }
        }
        return focus;
    }

    private void UpdateReading(LocusDisplay? focus) => Hud.ShowReading(focus?.Billboard.Visible == true ? focus : null);

    public void ToggleMarkers()
    {
        MarkersVisible = !MarkersVisible;
        foreach (var display in Displays) display.Marker.Visible = MarkersVisible;
    }

    public override void _Process(double delta)
    {
        var captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        var overlay = Menu.IsOpen || KeysMenu.IsOpen;
        var focus = overlay ? null : FindFocusedDisplay();
        // The open menu and the rehearsal panel stand in for the "mouse released" hint.
        Hud.UpdateState(Displays.Count(d => d.Billboard.Visible), Room?.Loci.Count ?? 0, MarkersVisible, captured || overlay || Rehearsal is not null || Drill is not null || Hud.DrillPromptVisible, focus);
        Hud.Map.PlayerPosition = Player.GlobalPosition;
        Hud.Map.PlayerForward = -Player.Camera.GlobalBasis.Z;
        Hud.Map.QueueRedraw();
        if (captured && Rehearsal is null && Drill is null) UpdateReading(focus);
        // The clock pauses while the Palace or key bindings menu is open.
        if (Rehearsal is { IsComplete: false } && !overlay)
        {
            _rehearsalSeconds += delta;
            Hud.ShowRehearsal(Rehearsal, RehearsalMs, _ghost, _feedback);
        }
    }

    private async void RunVerification() => await RuntimeVerification.Run(this, OS.GetEnvironment("PALACE_VIEWER_SMOKE_DIR"));
}
