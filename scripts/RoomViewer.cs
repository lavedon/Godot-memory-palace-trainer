using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

public partial class RoomViewer : Node3D
{
    public WalkingCamera Player { get; private set; } = null!;
    public ViewerHud Hud { get; private set; } = null!;
    public List<LocusDisplay> Displays { get; } = [];
    public RoomSnapshot? Room { get; private set; }
    public WallTextureSources WallTextures { get; private set; } = new(new Dictionary<RoomWall, string>(), []);
    public bool TextVisible => Displays.Any(d => d.Billboard.Visible);
    public bool MarkersVisible { get; private set; } = true;
    private bool _loaded;
    private bool _verificationActive;

    public override void _Ready()
    {
        _verificationActive = !string.IsNullOrWhiteSpace(OS.GetEnvironment("PALACE_VIEWER_SMOKE_DIR"));
        if (_verificationActive) GetWindow().Unfocusable = true;
        RegisterInput();
        RoomGeometry.Build(this);
        Player = new WalkingCamera { Name = "Walker" };
        AddChild(Player);
        Hud = new ViewerHud { Name = "Hud" };
        AddChild(Hud);
        try
        {
            var options = ViewerOptions.Parse(OS.GetCmdlineUserArgs());
            Room = new RoomRepository().Load(options);
            WallTextures = RoomGeometry.ApplyTextures(this, options.WallTextures.Resolve(Room));
            Hud.ShowRoom(Room, WallTextures.Warnings);
            foreach (var warning in Room.Warnings) GD.Print($"WARNING: {warning}");
            foreach (var warning in WallTextures.Warnings) GD.Print($"WARNING: {warning}");
            foreach (var (wall, path) in WallTextures.Paths) GD.Print($"{wall} texture: {path}");
            GD.Print($"Loaded Room {Room.Id}: {Room.Loci.Count}/26 Positions, {Room.Warnings.Count} warnings. Database opened read-only.");
            _loaded = true;
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
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.Escape || key.Keycode == Key.Escape)
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
        var focus = FindFocusedDisplay();
        Hud.UpdateState(Displays.Count(d => d.Billboard.Visible), Room?.Loci.Count ?? 0, MarkersVisible, captured, focus);
        Hud.Map.PlayerPosition = Player.GlobalPosition;
        Hud.Map.PlayerForward = -Player.Camera.GlobalBasis.Z;
        Hud.Map.QueueRedraw();
        if (captured) UpdateReading(focus);
    }

    private static void RegisterInput()
    {
        foreach (var (action, key) in new[] { ("walk_forward", Key.W), ("walk_back", Key.S), ("walk_left", Key.A), ("walk_right", Key.D) })
        {
            if (!InputMap.HasAction(action)) InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }

    private async void RunVerification() => await RuntimeVerification.Run(this, OS.GetEnvironment("PALACE_VIEWER_SMOKE_DIR"));
}
