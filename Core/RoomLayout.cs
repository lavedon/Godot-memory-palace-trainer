using System.Numerics;

namespace PalaceRoomViewer.Core;

// World orientation is recorded in docs/IMPLEMENTATION.md. Never derive Anchors from the camera.
public static class RoomLayout
{
    public const int Capacity = 26;
    public const float Width = 12f;
    public const float Depth = 18f;
    public const float Height = 7f;
    public const float EyeHeight = 1.7f;
    public const float WalkSpeed = 3.5f;
    public const float MouseSensitivity = 0.002f;
    public const float PitchLimit = 85f * MathF.PI / 180f;
    public static Vector3 Start => new(0, 0, -4);

    private static readonly Vector2[] Perimeter =
    [new(0, 9), new(6, 9), new(6, 0), new(6, -9), new(0, -9), new(-6, -9), new(-6, 0), new(-6, 9)];

    public static Vector3 Anchor(int position)
    {
        if (position is < 1 or > Capacity) throw new ArgumentOutOfRangeException(nameof(position));
        if (position == 25) return Vector3.Zero;
        if (position == 26) return new(0, Height, 0);
        var wall = Perimeter[(position - 1) / 3];
        return new(wall.X, ((position - 1) % 3) * Height / 2, wall.Y);
    }

    public static Vector3 Presentation(int position)
    {
        var anchor = Anchor(position);
        if (position == 25) return anchor + new Vector3(0, 0.75f, 0);
        if (position == 26) return anchor - new Vector3(0, 0.85f, 0);
        return new(anchor.X == 0 ? 0 : anchor.X - MathF.Sign(anchor.X) * 1.75f,
            Math.Clamp(anchor.Y, 0.85f, Height - 0.85f),
            anchor.Z == 0 ? 0 : anchor.Z - MathF.Sign(anchor.Z) * 1.75f);
    }

    public static string Description(int position)
    {
        if (position == 25) return "Floor · Center";
        if (position == 26) return "Ceiling · Center";
        string[] names = ["Front", "Front-right", "Right", "Back-right", "Back", "Back-left", "Left", "Front-left"];
        return $"{names[(position - 1) / 3]} · Slice {(position - 1) % 3 + 1}";
    }
}
