namespace PalaceRoomViewer.Core;

// Repeats a chosen stretch of Positions — reveal, then grade — lap after lap until stopped.
// Unlike a rehearsal it never finishes, never narrows to misses, and is not recorded.
public sealed class LoopDrill
{
    private int _index;

    public LoopDrill(IEnumerable<int> positions)
    {
        Positions = positions.Distinct().Order().ToArray();
        if (Positions.Count == 0) throw new ArgumentException("A loop drill needs at least one Position.", nameof(positions));
    }

    public IReadOnlyList<int> Positions { get; }
    public int Lap { get; private set; } = 1;
    public int IndexInLap => _index;
    public int Current => Positions[_index];
    public bool Revealed { get; private set; }
    public int LapKnown { get; private set; }
    public int LapMissed { get; private set; }
    // Known count of the lap just finished; null during the first lap.
    public int? PreviousLapKnown { get; private set; }
    public int TotalKnown { get; private set; }
    public int TotalGraded { get; private set; }
    public int Streak { get; private set; }
    public int BestStreak { get; private set; }

    public bool Reveal()
    {
        if (Revealed) return false;
        Revealed = true;
        return true;
    }

    // Grading is ignored until the text has been revealed. Returns true when the answer closed a lap.
    public bool Grade(bool knew)
    {
        if (!Revealed) return false;
        Revealed = false;
        TotalGraded++;
        if (knew) { LapKnown++; TotalKnown++; Streak++; }
        else { LapMissed++; Streak = 0; }
        BestStreak = Math.Max(BestStreak, Streak);
        if (++_index < Positions.Count) return false;
        _index = 0;
        Lap++;
        PreviousLapKnown = LapKnown;
        LapKnown = LapMissed = 0;
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
