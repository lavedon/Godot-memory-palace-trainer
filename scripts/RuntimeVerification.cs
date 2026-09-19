using System.Text.Json;
using Godot;
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
            result["loci"] = viewer.Room?.Loci.Values.OrderBy(l => l.Position).ToArray() ?? [];
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                result["nativeModules"] = process.Modules.Cast<System.Diagnostics.ProcessModule>()
                    .Where(m => m.ModuleName is "e_sqlite3.dll" or "coreclr.dll")
                    .Select(m => m.FileName).ToArray();
            }
            Check(viewer.Displays.Count == 26, "All 26 Anchors exist");
            Check(viewer.Displays.All(d => d.Marker.Visible), "Markers start visible");
            Check(viewer.Displays.All(d => !d.Billboard.Visible), "Text starts hidden");
            if (viewer.Room is null)
            {
                Check(viewer.Hud.HasError && viewer.Hud.ErrorText.Length > 0, "Failure is visible");
                Check(!viewer.Player.Enabled, "Walking disabled on failed load");
                await Capture("error");
            }
            else
            {
                Check(!viewer.Hud.HasError, "Successful load has no error");
                Check(viewer.Room.Warnings.All(w => viewer.Hud.WarningText.Contains(w.ToString())), "Every skipped Locus appears in visible warnings");
                Check(viewer.Displays.All(d => d.LogicalAnchor == RoomGeometry.ToGodot(RoomLayout.Anchor(d.PositionNumber))), "Spatial mapping matches stable Position identity");
                await Capture("markers");
                Keypress(Key.J);
                Check(viewer.Displays.Count(d => d.Billboard.Visible) == viewer.Room.Loci.Count, "J shows every populated Billboard");
                Check(viewer.Displays.Where(d => d.Locus is not null).All(d => d.Billboard.Text == d.Locus!.Text), "Billboards retain complete text");
                Keypress(Key.K);
                Check(viewer.Displays.All(d => !d.Marker.Visible), "K hides markers");
                Check(viewer.TextVisible, "K leaves text visible");
                Keypress(Key.J);
                Check(viewer.Displays.All(d => !d.Billboard.Visible && !d.Marker.Visible), "J hides text independently of markers");
                Keypress(Key.K);
                Keypress(Key.J);
                Input.ParseInputEvent(new InputEventKey { Device = InputDevice, PhysicalKeycode = Key.J, Pressed = true, Echo = true });
                Input.FlushBufferedEvents();
                Check(viewer.TextVisible, "Key repeat does not retrigger toggle");
                var player = viewer.Player;
                player.Enabled = false; // Verification drives movement deterministically on physics ticks.
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
