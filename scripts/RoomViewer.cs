using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

public partial class RoomViewer : Node3D
{
    public WalkingCamera Player { get; private set; } = null!;
    public ViewerHud Hud { get; private set; } = null!;
    public List<LocusDisplay> Displays { get; } = [];
    public RoomSnapshot? Room { get; private set; }
    public bool TextVisible { get; private set; }
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
            Hud.ShowRoom(Room);
            foreach (var warning in Room.Warnings) GD.Print($"WARNING: {warning}");
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
        }
        if (_loaded && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public void ToggleText()
    {
        TextVisible = !TextVisible;
        foreach (var display in Displays) display.Billboard.Visible = TextVisible && display.Locus is not null;
        if (!TextVisible) Hud.ShowReading(null);
    }

    public void ToggleMarkers()
    {
        MarkersVisible = !MarkersVisible;
        foreach (var display in Displays) display.Marker.Visible = MarkersVisible;
    }

    public override void _Process(double delta)
    {
        var captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        Hud.UpdateState(TextVisible, MarkersVisible, captured);
        Hud.Map.PlayerPosition = Player.GlobalPosition;
        Hud.Map.PlayerForward = -Player.Camera.GlobalBasis.Z;
        Hud.Map.QueueRedraw();
        foreach (var display in Displays) display.UpdateScale(Player.Camera);
        if (!TextVisible || !captured) return;
        LocusDisplay? focus = null;
        var best = .965f;
        foreach (var display in Displays.Where(d => d.Locus is not null))
        {
            var direction = (display.GlobalPosition - Player.Camera.GlobalPosition).Normalized();
            var dot = (-Player.Camera.GlobalBasis.Z).Dot(direction);
            if (dot > best) { best = dot; focus = display; }
        }
        Hud.ShowReading(focus);
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
