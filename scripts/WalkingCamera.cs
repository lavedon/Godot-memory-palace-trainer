using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

public partial class WalkingCamera : CharacterBody3D
{
    public Camera3D Camera { get; private set; } = null!;
    public bool Enabled { get; set; } = true;
    private float _pitch;

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
        if (@event is InputEventMouseMotion motion) Look(motion.ScreenRelative);
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
        Walk(input);
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
