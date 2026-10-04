using System.Text.RegularExpressions;

namespace PalaceRoomViewer.Core;

// Repeats a chosen stretch of Positions — reveal, then grade — lap after lap until stopped.
// Unlike a rehearsal it never narrows to misses and is not recorded; only a Room's first
// loop drill is noted, to start its learning clock (RoomLearning).
// A build-up drill starts with its end Position (by default the Room's last). Each time it has
// CleanLapsToGrow laps in a row with no miss, it adds the Position before its first. Once it holds
// every Position up to its end for that many clean laps it is Complete.
public sealed class LoopDrill
{
    public const int CleanLapsToGrow = 3;
    private int _index;

    public LoopDrill(IEnumerable<int> positions) : this(positions.Distinct().Order().ToArray(), null) { }

    private LoopDrill(IReadOnlyList<int> positions, IReadOnlyList<int>? buildUp)
    {
        if (positions.Count == 0) throw new ArgumentException("A loop drill needs at least one Position.", nameof(positions));
        Positions = positions;
        BuildUp = buildUp;
    }

    // A build-up over the given Positions up to `to` (by default the last), growing toward the first.
    // It starts with the Positions from `from` to `to` (by default just `to`), so "b 15-18"
    // resumes a build-up ending at 18 that had grown back to 15.
    public static LoopDrill BuildUpTo(IEnumerable<int> positions, int? to = null, int? from = null)
    {
        var all = positions.Distinct().Order().Where(p => to is null || p <= to).ToArray();
        if (all.Length == 0) throw new ViewerException($"No populated Positions up to {to}.");
        var start = all.Where(p => p >= (from ?? all[^1])).ToArray();
        if (start.Length == 0) throw new ViewerException($"No populated Positions in {from}–{to}.");
        return new LoopDrill(start, all);
    }

    // The Positions in the loop now; a build-up adds to the front.
    public IReadOnlyList<int> Positions { get; private set; }
    // Every Position a build-up grows to hold; null for a plain loop.
    public IReadOnlyList<int>? BuildUp { get; }
    public bool IsBuildUp => BuildUp is not null;
    public int Lap { get; private set; } = 1;
    public int IndexInLap => _index;
    public int Current => Positions[_index];
    public bool Revealed { get; private set; }
    public int LapKnown { get; private set; }
    public int LapMissed { get; private set; }
    // Known count of the lap just finished; null during the first lap.
    public int? PreviousLapKnown { get; private set; }
    // Laps in a row with no miss.
    public int CleanLaps { get; private set; }
    // The Position a build-up added when the last answer closed a lap; null otherwise.
    public int? Added { get; private set; }
    public bool Complete { get; private set; }
    public int TotalKnown { get; private set; }
    public int TotalGraded { get; private set; }
    public int Streak { get; private set; }
    public int BestStreak { get; private set; }

    public bool Reveal()
    {
        if (Revealed || Complete) return false;
        Revealed = true;
        return true;
    }

    // Grading is ignored until the text has been revealed. Returns true when the answer closed a lap.
    public bool Grade(bool knew)
    {
        if (!Revealed) return false;
        Revealed = false;
        Added = null;
        TotalGraded++;
        if (knew) { LapKnown++; TotalKnown++; Streak++; }
        else { LapMissed++; Streak = 0; }
        BestStreak = Math.Max(BestStreak, Streak);
        if (++_index < Positions.Count) return false;
        _index = 0;
        Lap++;
        PreviousLapKnown = LapKnown;
        CleanLaps = LapMissed == 0 ? CleanLaps + 1 : 0;
        LapKnown = LapMissed = 0;
        if (BuildUp is { } all && CleanLaps >= CleanLapsToGrow)
        {
            if (Positions.Count == all.Count) Complete = true;
            else
            {
                // The loop is always a tail of BuildUp, so the next Position is the one before it.
                Added = all[^(Positions.Count + 1)];
                Positions = [Added.Value, .. Positions];
                Lap = 1;
                PreviousLapKnown = null;
                CleanLaps = 0;
            }
        }
        return true;
    }

    // "b" builds up the whole Room from its last Position; "b 18" (or "build 18") builds up to 18,
    // starting with 18 alone; "b 15-18" resumes that build-up with 15–18 already in the loop.
    // Returns false when the text is not a build-up, so it can be read as a range instead.
    public static bool TryParseBuildUp(string text, out int? from, out int? to)
    {
        from = to = null;
        var match = Regex.Match(text.Trim(), @"^b(?:uild)?(?:\s*(\S.*))?$", RegexOptions.IgnoreCase);
        if (!match.Success) return false;
        if (!match.Groups[1].Success) return true;
        var bounds = match.Groups[1].Value.Split(['-', '–'], StringSplitOptions.TrimEntries);
        var numbers = bounds.Select(b => int.TryParse(b, out var n) ? n : 0).ToArray();
        if (bounds.Length > 2 || numbers.Any(n => n < 1 || n > RoomLayout.Capacity))
            throw new ViewerException($"Build up to a Position 1–{RoomLayout.Capacity}, like b 18, or resume one, like b 15-18.");
        to = numbers.Max();
        if (numbers.Length == 2) from = numbers.Min();
        return true;
    }

    // Parses "1-3", "5" or "1-3, 7, 10-12" into Positions 1–26, in order.
    public static IReadOnlyList<int> ParseRange(string text)
    {
        var positions = new SortedSet<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = part.Split(['-', '–'], StringSplitOptions.TrimEntries);
            if (bounds.Length > 2 || !bounds.All(b => int.TryParse(b, out _)))
                throw new ViewerException($"'{part}' is not a Position or a range like 1-3.");
            var numbers = bounds.Select(int.Parse).ToArray();
            var (from, to) = (numbers.Min(), numbers.Max());
            if (from < 1 || to > RoomLayout.Capacity)
                throw new ViewerException($"'{part}' is outside Positions 1–{RoomLayout.Capacity}.");
            for (var position = from; position <= to; position++) positions.Add(position);
        }
        if (positions.Count == 0) throw new ViewerException("Enter Positions such as 1-3.");
        return [.. positions];
    }

    // "1–3, 7, 10–12": consecutive Positions collapse into ranges.
    public static string Describe(IEnumerable<int> positions)
    {
        var parts = new List<string>();
        int? start = null, end = null;
        foreach (var position in positions.Distinct().Order().Append(int.MinValue))
        {
            if (start is not null && position == end + 1) { end = position; continue; }
            if (start is not null) parts.Add(start == end ? $"{start}" : $"{start}–{end}");
            (start, end) = (position, position);
        }
        return string.Join(", ", parts);
    }
}
