namespace PalaceRoomViewer.Core;

public enum KeyAction
{
    WalkForward, WalkBack, WalkLeft, WalkRight, PalaceMenu, Rehearse, Loop, Sound, KeyBindings,
    AllText, ThisText, Markers, Reveal, Knew, Missed, Quit, AdvancedRehearse
}

// Where an action is live. Anywhere actions clash with every other action; Exploring and Quiz
// actions only clash within their own context, so J can toggle text while exploring and grade in a quiz.
public enum KeyContext { Anywhere, Exploring, Quiz }

public sealed record KeyActionInfo(KeyAction Action, string Name, KeyContext Context);

public sealed record KeySlot(KeyAction Action, int Slot);

// Each action has a main and an alternate key. Keys are Godot Key values (physical keys) so
// Core stays engine-free; the viewer saves these in a small text file in its user folder.
public sealed class KeyBindings
{
    public const int Slots = 2;
    public const long None = 0;
    // Godot Key values used by the defaults. Escape is reserved for releasing the mouse and cancelling.
    public const long Escape = 4194305, Space = 32, Slash = 47, Digit1 = 49, Digit2 = 50, F1 = 4194332;
    private static long Letter(char c) => c;

    public static readonly IReadOnlyList<KeyActionInfo> Actions =
    [
        new(KeyAction.WalkForward, "Walk forward", KeyContext.Anywhere),
        new(KeyAction.WalkBack, "Walk back", KeyContext.Anywhere),
        new(KeyAction.WalkLeft, "Walk left", KeyContext.Anywhere),
        new(KeyAction.WalkRight, "Walk right", KeyContext.Anywhere),
        new(KeyAction.PalaceMenu, "Palace menu", KeyContext.Anywhere),
        new(KeyAction.Rehearse, "Rehearse (start / stop)", KeyContext.Anywhere),
        new(KeyAction.AdvancedRehearse, "Advanced rehearse (after Gold)", KeyContext.Anywhere),
        new(KeyAction.Loop, "Loop drill (new range)", KeyContext.Anywhere),
        new(KeyAction.Sound, "Sound on / off", KeyContext.Anywhere),
        new(KeyAction.KeyBindings, "Key bindings", KeyContext.Anywhere),
        new(KeyAction.AllText, "All text", KeyContext.Exploring),
        new(KeyAction.ThisText, "This text", KeyContext.Exploring),
        new(KeyAction.Markers, "Markers", KeyContext.Exploring),
        new(KeyAction.Reveal, "Reveal / go again", KeyContext.Quiz),
        new(KeyAction.Knew, "Knew it", KeyContext.Quiz),
        new(KeyAction.Missed, "Missed", KeyContext.Quiz),
        new(KeyAction.Quit, "Quit rehearsal or loop", KeyContext.Quiz),
    ];

    private static readonly IReadOnlyDictionary<KeyAction, long[]> DefaultKeys = new Dictionary<KeyAction, long[]>
    {
        [KeyAction.WalkForward] = [Letter('W'), None],
        [KeyAction.WalkBack] = [Letter('S'), None],
        [KeyAction.WalkLeft] = [Letter('A'), None],
        [KeyAction.WalkRight] = [Letter('D'), None],
        [KeyAction.PalaceMenu] = [Letter('M'), None],
        [KeyAction.Rehearse] = [Letter('R'), None],
        [KeyAction.AdvancedRehearse] = [Letter('G'), None],
        [KeyAction.Loop] = [Slash, Letter('T')],
        [KeyAction.Sound] = [Letter('V'), None],
        [KeyAction.KeyBindings] = [F1, None],
        [KeyAction.AllText] = [Letter('J'), None],
        [KeyAction.ThisText] = [Letter('L'), None],
        [KeyAction.Markers] = [Letter('K'), None],
        [KeyAction.Reveal] = [Letter('H'), Space],
        [KeyAction.Knew] = [Letter('J'), Digit2],
        [KeyAction.Missed] = [Letter('K'), Digit1],
        [KeyAction.Quit] = [Letter('Q'), None],
    };

    private readonly Dictionary<KeyAction, long[]> _keys;

    private KeyBindings(Dictionary<KeyAction, long[]> keys) => _keys = keys;

    public static KeyBindings Defaults() => new(DefaultKeys.ToDictionary(p => p.Key, p => p.Value.ToArray()));

    public static KeyActionInfo Info(KeyAction action) => Actions.First(a => a.Action == action);

    public long Get(KeyAction action, int slot) => _keys[action][slot];

    public IEnumerable<long> Keys(KeyAction action) => _keys[action].Where(k => k != None);

    public bool Matches(KeyAction action, long key) => key != None && _keys[action].Contains(key);

    public static bool Clash(KeyContext a, KeyContext b) => a == KeyContext.Anywhere || b == KeyContext.Anywhere || a == b;

    // Binds key to one slot. Any slot that would clash with it is cleared and returned so the menu can say so.
    public IReadOnlyList<KeySlot> Assign(KeyAction action, int slot, long key)
    {
        if (key == Escape) throw new ViewerException("Escape is reserved for releasing the mouse and cancelling.");
        if (key == None) throw new ArgumentException("Use Clear to unbind a slot.", nameof(key));
        var context = Info(action).Context;
        var cleared = new List<KeySlot>();
        foreach (var other in Actions)
            for (var s = 0; s < Slots; s++)
                if (_keys[other.Action][s] == key && !(other.Action == action && s == slot) && (other.Action == action || Clash(context, other.Context)))
                {
                    _keys[other.Action][s] = None;
                    if (other.Action != action) cleared.Add(new(other.Action, s));
                }
        _keys[action][slot] = key;
        return cleared;
    }

    public void Clear(KeyAction action, int slot) => _keys[action][slot] = None;

    // One "Action=key,key" line per action.
    public string Serialize() =>
        string.Join("\n", Actions.Select(a => $"{a.Action}={string.Join(",", _keys[a.Action])}")) + "\n";

    // Unknown lines and unreadable values are ignored; actions missing from the text keep their defaults.
    public static KeyBindings Parse(string text)
    {
        var bindings = Defaults();
        var loaded = new HashSet<KeyAction>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2 || !Enum.TryParse<KeyAction>(parts[0], out var action) || !Enum.IsDefined(action)) continue;
            var keys = parts[1].Split(',');
            if (keys.Length != Slots || !keys.All(k => long.TryParse(k, out var v) && v >= 0 && v != Escape)) continue;
            bindings._keys[action] = keys.Select(long.Parse).ToArray();
            loaded.Add(action);
        }
        // An action added after the file was saved keeps its default keys only where they clash
        // with nothing the user chose; a clashing default is left unbound.
        foreach (var added in Actions.Where(a => !loaded.Contains(a.Action)))
            for (var slot = 0; slot < Slots; slot++)
            {
                var key = bindings._keys[added.Action][slot];
                if (key != None && Actions.Any(o => loaded.Contains(o.Action) && Clash(added.Context, o.Context) && bindings._keys[o.Action].Contains(key)))
                    bindings._keys[added.Action][slot] = None;
            }
        return bindings;
    }
}
