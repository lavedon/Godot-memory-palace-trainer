using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

public partial class RoomViewer : Node3D
{
    public WalkingCamera Player { get; private set; } = null!;
    public ViewerHud Hud { get; private set; } = null!;
    public PalaceMenu Menu { get; private set; } = null!;
    public string DatabasePath { get; private set; } = ViewerOptions.DefaultDatabasePath;
    public List<LocusDisplay> Displays { get; } = [];
    public RoomSnapshot? Room { get; private set; }
    public WallTextureSources WallTextures { get; private set; } = new(new Dictionary<RoomWall, string>(), []);
    public bool TextVisible => Displays.Any(d => d.Billboard.Visible);
    public bool MarkersVisible { get; private set; } = true;
    public RehearsalSession? Rehearsal { get; private set; }
    public string RehearsalLogPath { get; private set; } = "";
    // Seconds the camera takes to glide to each rehearsal Position; 0 snaps.
    public float GuideSeconds { get; set; } = .7f;
    private DateTimeOffset _rehearsalStarted;
    private bool _loaded;
    private bool _verificationActive;
    // A Room chosen in the menu survives the scene reload that displays it.
    private static ViewerOptions? s_pendingSelection;

    public override void _Ready()
    {
        _verificationActive = !string.IsNullOrWhiteSpace(OS.GetEnvironment("PALACE_VIEWER_SMOKE_DIR"));
        if (_verificationActive) GetWindow().Unfocusable = true;
        RehearsalLogPath = _verificationActive
            ? Path.Combine(OS.GetEnvironment("PALACE_VIEWER_SMOKE_DIR"), "rehearsals.jsonl")
            : ProjectSettings.GlobalizePath("user://rehearsals.jsonl");
        RegisterInput();
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
        var pending = s_pendingSelection;
        s_pendingSelection = null;
        ViewerOptions? options = null;
        try
        {
            options = pending ?? ViewerOptions.Parse(OS.GetCmdlineUserArgs(), requireRoom: false);
            DatabasePath = options.DatabasePath;
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
        if (Menu.IsOpen)
        {
            // The menu owns input while open; only its close keys pass through here.
            if (@event is InputEventKey { Pressed: true, Echo: false } menuKey && Menu.CanClose &&
                (menuKey.PhysicalKeycode is Key.M or Key.Escape || menuKey.Keycode is Key.M or Key.Escape))
                CloseMenu();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (Rehearsal is not null && HandleRehearsalInput(@event))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (_loaded && Room!.Loci.Count > 0 && (key.PhysicalKeycode == Key.R || key.Keycode == Key.R))
            {
                StartRehearsal();
                GetViewport().SetInputAsHandled();
            }
            else if (key.PhysicalKeycode == Key.M || key.Keycode == Key.M)
            {
                OpenMenu();
                GetViewport().SetInputAsHandled();
            }
            else if (key.PhysicalKeycode == Key.Escape || key.Keycode == Key.Escape)
            {
                Input.MouseMode = Input.MouseModeEnum.Visible;
                GetViewport().SetInputAsHandled();
            }
            else if (_loaded && (key.PhysicalKeycode == Key.J || key.Keycode == Key.J))
            {
                ToggleText();
                GetViewport().SetInputAsHandled();
            }
            else if (_loaded && (key.PhysicalKeycode == Key.K || key.Keycode == Key.K))
            {
                ToggleMarkers();
                GetViewport().SetInputAsHandled();
            }
            else if (_loaded && (key.PhysicalKeycode == Key.L || key.Keycode == Key.L))
            {
                ToggleFocusedText();
                GetViewport().SetInputAsHandled();
            }
        }
        if (_loaded && @event is InputEventMouseButton { Pressed: true } mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                Input.MouseMode = Input.MouseModeEnum.Captured;
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

    public void OpenMenu()
    {
        Player.Enabled = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Hud.SetErrorVisible(false);
        Menu.Open(DatabasePath, Room?.Id, canClose: _loaded || Hud.HasError);
    }

    public void CloseMenu()
    {
        if (!Menu.CanClose) return;
        Menu.Close();
        Hud.SetErrorVisible(true);
        Player.Enabled = _loaded;
        if (_loaded && DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    // Rebuilds the scene for the chosen Room. CLI image overrides applied only to the starting Room.
    private void LoadRoom(long roomId)
    {
        s_pendingSelection = new ViewerOptions(roomId, DatabasePath);
        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
    }

    // While rehearsing, text toggles are disabled so nothing is revealed early.
    private bool HandleRehearsalInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true } button)
        {
            if (button.ButtonIndex == MouseButton.Left) Input.MouseMode = Input.MouseModeEnum.Captured;
            return button.ButtonIndex is MouseButton.Left or MouseButton.Right;
        }
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return false;
        bool Is(Key k) => key.PhysicalKeycode == k || key.Keycode == k;
        if (Is(Key.R)) StopRehearsal();
        else if (Is(Key.Space)) { if (Rehearsal!.IsComplete) StartRehearsal(); else RevealRehearsal(); }
        else if (Is(Key.Key1) || Is(Key.Kp1)) GradeRehearsal(knew: false);
        else if (Is(Key.Key2) || Is(Key.Kp2)) GradeRehearsal(knew: true);
        else return Is(Key.J) || Is(Key.L);
        return true;
    }

    private LocusDisplay? RehearsalDisplay => Rehearsal?.Current is { } position ? Displays[position - 1] : null;

    public void StartRehearsal()
    {
        Rehearsal = new RehearsalSession(Room!.Loci.Keys);
        _rehearsalStarted = DateTimeOffset.Now;
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
        Hud.ShowRehearsal(Rehearsal);
    }

    private void GradeRehearsal(bool knew)
    {
        var display = RehearsalDisplay;
        if (display is null || !Rehearsal!.Grade(knew)) return;
        display.Billboard.Visible = false;
        display.SetCue(knew ? MarkerCue.Known : MarkerCue.Missed);
        Hud.ShowReading(null);
        if (!Rehearsal.IsComplete)
        {
            ShowRehearsalStep();
            return;
        }
        Hud.ShowRehearsal(Rehearsal);
        try
        {
            RehearsalLog.Append(RehearsalLogPath, RehearsalRecord.From(Rehearsal, Room!, _rehearsalStarted, DateTimeOffset.Now));
            GD.Print($"Rehearsal logged: {RehearsalLogPath}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Could not write the rehearsal log {RehearsalLogPath}: {ex.Message}");
        }
    }

    private void ShowRehearsalStep()
    {
        var display = RehearsalDisplay!;
        display.SetCue(MarkerCue.Target);
        Player.Guide(display.Viewpoint, display.GlobalPosition, GuideSeconds);
        Hud.ShowRehearsal(Rehearsal!);
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
        var focus = Menu.IsOpen ? null : FindFocusedDisplay();
        // The open menu and the rehearsal panel stand in for the "mouse released" hint.
        Hud.UpdateState(Displays.Count(d => d.Billboard.Visible), Room?.Loci.Count ?? 0, MarkersVisible, captured || Menu.IsOpen || Rehearsal is not null, focus);
        Hud.Map.PlayerPosition = Player.GlobalPosition;
        Hud.Map.PlayerForward = -Player.Camera.GlobalBasis.Z;
        Hud.Map.QueueRedraw();
        if (captured && Rehearsal is null) UpdateReading(focus);
    }

    private static void RegisterInput()
    {
        foreach (var (action, key) in new[] { ("walk_forward", Key.W), ("walk_back", Key.S), ("walk_left", Key.A), ("walk_right", Key.D) })
        {
            // Actions persist across scene reloads; register each key once.
            if (InputMap.HasAction(action)) continue;
            InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }

    private async void RunVerification() => await RuntimeVerification.Run(this, OS.GetEnvironment("PALACE_VIEWER_SMOKE_DIR"));
}
