using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

public partial class WalkingCamera : CharacterBody3D
{
    public Camera3D Camera { get; private set; } = null!;
    public bool Enabled { get; set; } = true;
    private float _pitch;
    // Guided glide toward a viewpoint (rehearsal). Any walking or looking cancels it.
    private Vector3 _guideFrom, _guideTo;
    private float _yawFrom, _yawTo, _pitchFrom, _pitchTo, _guideSeconds;
    private float _guideTime = -1;
    public bool Guiding => _guideTime >= 0;
    // Capturing the mouse re-centres the cursor, which arrives as one mouse motion. That motion
    // must not count as looking: it would cancel the glide that usually starts at the same moment.
    private const ulong CaptureSettleMs = 200;
    private ulong _ignoreMotionUntilMs;
    public bool SettlingCapture => Time.GetTicksMsec() < _ignoreMotionUntilMs;

    public override void _Ready()
    {
        Position = RoomGeometry.ToGodot(RoomLayout.Start);
        Rotation = new(0, Mathf.Pi, 0);
        MotionMode = MotionModeEnum.Floating;
        AddChild(new CollisionShape3D
        {
            Position = new(0, .9f, 0), Shape = new CapsuleShape3D { Radius = .3f, Height = 1.8f }
        });
        Camera = new Camera3D { Name = "Eyes", Position = new(0, RoomLayout.EyeHeight, 0), Current = true, Fov = 75, Near = .06f, Far = 70 };
        AddChild(Camera);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Enabled || Input.MouseMode != Input.MouseModeEnum.Captured) return;
        if (@event is InputEventMouseMotion motion) MouseLook(motion.ScreenRelative);
    }

    // Looking with the mouse takes over from a guided glide, except for the re-centring motion just after capture.
    public void MouseLook(Vector2 delta)
    {
        if (SettlingCapture) return;
        CancelGuide();
        Look(delta);
    }

    public void Look(Vector2 delta)
    {
        RotateY(-delta.X * RoomLayout.MouseSensitivity);
        _pitch = Mathf.Clamp(_pitch - delta.Y * RoomLayout.MouseSensitivity, -RoomLayout.PitchLimit, RoomLayout.PitchLimit);
        Camera.Rotation = new(_pitch, 0, 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Enabled || Input.MouseMode != Input.MouseModeEnum.Captured) { Velocity = Vector3.Zero; return; }
        var input = Input.GetVector("walk_left", "walk_right", "walk_forward", "walk_back");
        if (input != Vector2.Zero) CancelGuide();
        if (!Guiding) Walk(input);
    }

    // Moves to a standing spot and turns so lookTarget sits under the crosshair.
    public void Guide(Vector3 position, Vector3 lookTarget, float seconds)
    {
        _guideTo = new(position.X, 0, position.Z);
        var offset = lookTarget - (_guideTo + new Vector3(0, RoomLayout.EyeHeight, 0));
        _yawTo = Mathf.Atan2(-offset.X, -offset.Z);
        _pitchTo = Mathf.Clamp(Mathf.Atan2(offset.Y, new Vector2(offset.X, offset.Z).Length()), -RoomLayout.PitchLimit, RoomLayout.PitchLimit);
        _guideFrom = Position;
        _yawFrom = Rotation.Y;
        _pitchFrom = _pitch;
        _guideSeconds = seconds;
        _guideTime = 0;
        if (seconds <= 0) ApplyGuide(1);
    }

    public void CancelGuide() => _guideTime = -1;

    public void CaptureMouse()
    {
        _ignoreMotionUntilMs = Time.GetTicksMsec() + CaptureSettleMs;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Process(double delta)
    {
        if (!Guiding) return;
        _guideTime += (float)delta;
        ApplyGuide(_guideTime / _guideSeconds);
    }

    private void ApplyGuide(float progress)
    {
        var t = Mathf.SmoothStep(0, 1, Mathf.Clamp(progress, 0, 1));
        Position = _guideFrom.Lerp(_guideTo, t);
        Rotation = new(0, Mathf.LerpAngle(_yawFrom, _yawTo, t), 0);
        _pitch = Mathf.Lerp(_pitchFrom, _pitchTo, t);
        Camera.Rotation = new(_pitch, 0, 0);
        if (progress >= 1) _guideTime = -1;
    }

    public void Walk(Vector2 input)
    {
        var direction = GlobalBasis * new Vector3(input.X, 0, input.Y);
        direction.Y = 0;
        Velocity = direction.LimitLength() * RoomLayout.WalkSpeed;
        MoveAndSlide();
        // Fixed walking plane: neither camera pitch nor collision may introduce flight.
        Position = new(Position.X, 0, Position.Z);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut) Input.MouseMode = Input.MouseModeEnum.Visible;
    }
}
