namespace PalaceRoomViewer.Core;

// Walks a Room's Positions in order: reveal, then grade. Each later round re-asks only the
// previous round's misses, still in Position order, until a round has no misses.
// Elapsed time is supplied by the caller so the session stays deterministic.
public sealed class RehearsalSession
{
    private readonly List<List<int>> _missesByRound = [[]];
    private readonly List<long> _splits = [];
    private List<int> _round;
    private int _index;

    public RehearsalSession(IEnumerable<int> positions)
    {
        Positions = positions.Distinct().Order().ToArray();
        if (Positions.Count == 0) throw new ArgumentException("A rehearsal needs at least one Position.", nameof(positions));
        _round = [.. Positions];
    }

    public IReadOnlyList<int> Positions { get; }
    public int Round => _missesByRound.Count;
    public IReadOnlyList<int> RoundPositions => _round;
    public int IndexInRound => _index;
    public bool IsComplete => _index >= _round.Count;
    public int? Current => IsComplete ? null : _round[_index];
    public bool Revealed { get; private set; }
    public IReadOnlyList<IReadOnlyList<int>> MissesByRound => _missesByRound;
    public IReadOnlyList<int> RoundMisses => _missesByRound[^1];
    public IReadOnlyList<int> FirstPassMisses => _missesByRound[0];
    // Elapsed milliseconds at each first-pass answer, aligned with Positions.
    public IReadOnlyList<long> FirstPassSplitsMs => _splits;
    public long ElapsedMs { get; private set; }
    public int Combo { get; private set; }
    public int BestCombo { get; private set; }
    public int Score { get; private set; }
    public int LastPoints { get; private set; }
    public int PerfectBonus { get; private set; }

    public bool Reveal()
    {
        if (IsComplete || Revealed) return false;
        Revealed = true;
        return true;
    }

    // Grading is ignored until the text has been revealed.
    public bool Grade(bool knew, long elapsedMs = 0)
    {
        if (IsComplete || !Revealed) return false;
        Revealed = false;
        ElapsedMs = Math.Max(ElapsedMs, elapsedMs);
        LastPoints = 0;
        if (Round == 1)
        {
            var locusMs = ElapsedMs - (_splits.Count > 0 ? _splits[^1] : 0);
            _splits.Add(ElapsedMs);
            Combo = knew ? Combo + 1 : 0;
            BestCombo = Math.Max(BestCombo, Combo);
            if (knew) LastPoints = RehearsalScoring.FirstPassPoints(Combo, locusMs);
        }
        else if (knew) LastPoints = RehearsalScoring.RetryPoints;
        Score += LastPoints;
        if (!knew) _missesByRound[^1].Add(_round[_index]);
        if (++_index < _round.Count) return true;
        if (_missesByRound[^1].Count == 0)
        {
            if (FirstPassMisses.Count == 0)
            {
                PerfectBonus = RehearsalScoring.PerfectBonusPerLocus * Positions.Count;
                Score += PerfectBonus;
            }
            return true;
        }
        _round = [.. _missesByRound[^1]];
        _index = 0;
        _missesByRound.Add([]);
        return true;
    }
}

public enum Medal { None, Bronze, Silver, Gold, Platinum }

public static class RehearsalScoring
{
    public const int RetryPoints = 25;
    public const int PerfectBonusPerLocus = 50;
    public const long PlatinumPaceMs = 4_000;
    public const long GoldPaceMs = 6_000;
    public const double SilverFirstPass = .8;

    // 100 per first-pass recall, +10 per combo step (up to +100), and up to +100 for answering within 10 s.
    public static int FirstPassPoints(int combo, long locusMs) =>
        100 + Math.Min(100, 10 * (combo - 1)) + (int)Math.Max(0, (10_000 - locusMs) / 100);

    // Platinum and Gold need a flawless first pass at pace; Silver needs 80% on the first pass.
    public static Medal MedalFor(int loci, int firstPassKnown, long durationMs)
    {
        if (loci <= 0) return Medal.None;
        var pace = durationMs / (double)loci;
        if (firstPassKnown == loci && pace <= PlatinumPaceMs) return Medal.Platinum;
        if (firstPassKnown == loci && pace <= GoldPaceMs) return Medal.Gold;
        return firstPassKnown >= loci * SilverFirstPass ? Medal.Silver : Medal.Bronze;
    }

    public static string FormatTime(long ms) => $"{ms / 60_000:00}:{ms / 1000 % 60:00}.{ms / 100 % 10}";

    public static string FormatDelta(long ms) => (ms < 0 ? "−" : "+") + $"{Math.Abs(ms) / 1000.0:0.0} s";
}
