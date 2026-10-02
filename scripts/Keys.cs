using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

// The viewer's live key bindings. Static so they survive the scene reload that loads another Room.
public static class Keys
{
    public static KeyBindings Current { get; private set; } = KeyBindings.Defaults();
    public static string FilePath { get; private set; } = "";
    private static readonly (KeyAction Action, string Name)[] Walking =
        [(KeyAction.WalkForward, "walk_forward"), (KeyAction.WalkBack, "walk_back"), (KeyAction.WalkLeft, "walk_left"), (KeyAction.WalkRight, "walk_right")];

    // Reads the bindings file once per run; a missing or unreadable file means defaults.
    public static void Load(string path, bool reload = false)
    {
        if (FilePath == path && !reload) return;
        FilePath = path;
        Current = KeyBindings.Defaults();
        try { if (File.Exists(path)) Current = KeyBindings.Parse(File.ReadAllText(path)); }
        catch (Exception ex) { GD.PrintErr($"Key bindings not loaded from {path}: {ex.Message}"); }
        ApplyWalking();
    }

    // Returns an error message when the file could not be written; the bindings still apply this run.
    public static string? Save()
    {
        ApplyWalking();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, Current.Serialize());
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static void Reset()
    {
        Current = KeyBindings.Defaults();
        ApplyWalking();
    }

    // Walking polls Godot input actions, so they are rebuilt from the bindings.
    public static void ApplyWalking()
    {
        foreach (var (action, name) in Walking)
        {
            if (!InputMap.HasAction(name)) InputMap.AddAction(name);
            InputMap.ActionEraseEvents(name);
            foreach (var key in Current.Keys(action))
                InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = (Key)key });
        }
    }

    public static bool Is(KeyAction action, InputEventKey key) =>
        Current.Matches(action, Normalize(key.PhysicalKeycode)) || Current.Matches(action, Normalize(key.Keycode));

    // The key to store for a press: the physical key where known. Keypad digits and / count as the main keys.
    public static long FromEvent(InputEventKey key) => Normalize(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode);

    private static long Normalize(Key key) => key switch
    {
        >= Key.Kp0 and <= Key.Kp9 => (long)Key.Key0 + (key - Key.Kp0),
        Key.KpDivide => (long)Key.Slash,
        _ => (long)key
    };

    public static string Name(long key) => key switch
    {
        KeyBindings.None => "—",
        KeyBindings.Space => "SPACE",
        > 32 and < 127 => ((char)key).ToString(),
        _ => OS.GetKeycodeString((Key)key).ToUpperInvariant()
    };

    // "H / SPACE", or "unbound" when the action has no keys.
    public static string Label(KeyAction action)
    {
        var keys = Current.Keys(action).Select(Name).ToArray();
        return keys.Length == 0 ? "unbound" : string.Join(" / ", keys);
    }
}
