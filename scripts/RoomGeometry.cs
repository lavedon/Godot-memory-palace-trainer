using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

public static class RoomGeometry
{
    public static void Build(Node3D parent)
    {
        var stone = Material("40505b");
        var side = Material("34444f");
        var ceiling = Material("293943");
        var floor = Material("23323b");
        var teal = Material("236b6a");
        var trim = Material("90baa9", true);
        var brass = Material("9d8156");
        Box(parent, "Floor", new(12.5f, .25f, 18.5f), new(0, -.125f, 0), floor, true);
        Box(parent, "Ceiling", new(12.5f, .25f, 18.5f), new(0, 7.125f, 0), ceiling, true);
        Box(parent, "FrontWall", new(12.5f, 7, .25f), new(0, 3.5f, 9.125f), stone, true);
        Box(parent, "BackWall", new(12.5f, 7, .25f), new(0, 3.5f, -9.125f), stone, true);
        Box(parent, "LeftWall", new(.25f, 7, 18), new(-6.125f, 3.5f, 0), side, true);
        Box(parent, "RightWall", new(.25f, 7, 18), new(6.125f, 3.5f, 0), side, true);

        // Shallow, non-colliding architectural detail leaves the entire floor walkable.
        Box(parent, "FrontInset", new(3.8f, 6.4f, .06f), new(0, 3.5f, 8.97f), teal);
        foreach (var y in new[] { .14f, 2.15f, 4.9f, 6.86f })
        {
            var band = y is .14f or 6.86f ? trim : brass;
            Box(parent, "FrontBand", new(12, .035f, .035f), new(0, y, 8.94f), band);
            Box(parent, "BackBand", new(12, .035f, .035f), new(0, y, -8.94f), band);
            Box(parent, "LeftBand", new(.035f, .035f, 18), new(-5.94f, y, 0), band);
            Box(parent, "RightBand", new(.035f, .035f, 18), new(5.94f, y, 0), band);
        }
        foreach (var x in new[] { -5.8f, -2f, 2f, 5.8f })
        {
            Box(parent, "FrontSeam", new(.025f, 7, .025f), new(x, 3.5f, 8.93f), brass);
            Box(parent, "BackSeam", new(.025f, 7, .025f), new(x, 3.5f, -8.93f), brass);
        }
        for (var z = -6; z <= 6; z += 3)
        {
            Box(parent, "LeftSeam", new(.025f, 7, .025f), new(-5.93f, 3.5f, z), brass);
            Box(parent, "RightSeam", new(.025f, 7, .025f), new(5.93f, 3.5f, z), brass);
        }
        var grout = Material("36464d");
        for (var x = -4; x <= 4; x += 2)
            Box(parent, "FloorJoint", new(.014f, .005f, 18), new(x, .003f, 0), grout);
        for (var z = -7; z <= 7; z += 2)
            Box(parent, "FloorJoint", new(12, .005f, .014f), new(0, .003f, z), grout);
        foreach (var x in new[] { -3.6f, 3.6f })
            Box(parent, "CeilingLight", new(.09f, .025f, 14), new(x, 6.97f, 0), trim);

        WallText(parent, "FRONT", new(0, 5.05f, 8.87f), Mathf.Pi, new Color("b8e8da"));
        WallText(parent, "BACK", new(0, 5.05f, -8.87f), 0, new Color("a9b8be"));
        WallText(parent, "LEFT", new(-5.87f, 5.05f, 0), Mathf.Pi / 2, new Color("a9b8be"));
        WallText(parent, "RIGHT", new(5.87f, 5.05f, 0), -Mathf.Pi / 2, new Color("a9b8be"));

        parent.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("14212b"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("c9dce6"), AmbientLightEnergy = .7f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        });
        foreach (var z in new[] { -6f, 0f, 6f })
            parent.AddChild(new OmniLight3D
            {
                Position = new(0, 5.8f, z), OmniRange = 12, LightEnergy = 1.4f,
                LightColor = new Color("ecdfc6"), ShadowEnabled = false
            });
    }

    private static StandardMaterial3D Material(string hex, bool glow = false) => new()
    {
        AlbedoColor = new Color(hex), Roughness = .9f,
        EmissionEnabled = glow, Emission = glow ? new Color(hex) : Colors.Black,
        EmissionEnergyMultiplier = glow ? .7f : 0
    };

    private static void Box(Node3D parent, string name, Vector3 size, Vector3 position, Material material, bool collide = false)
    {
        var mesh = new MeshInstance3D { Name = name, Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = position };
        parent.AddChild(mesh);
        if (!collide) return;
        var body = new StaticBody3D { Name = name + "Collision", Position = position };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
    }

    private static void WallText(Node3D parent, string text, Vector3 position, float yaw, Color color)
    {
        parent.AddChild(new Label3D
        {
            Name = text + "Cue", Text = text, FontSize = 64, PixelSize = .004f,
            Position = position, Rotation = new(0, yaw, 0), Modulate = color,
            OutlineSize = 0, Shaded = false, DoubleSided = false
        });
    }

    public static Vector3 ToGodot(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);
}
