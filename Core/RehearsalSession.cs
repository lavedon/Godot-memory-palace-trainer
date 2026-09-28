using System.Text.Json;

namespace PalaceRoomViewer.Core;

// Walks a Room's Positions in order: reveal, then grade. Each later round re-asks only the
// previous round's misses, still in Position order, until a round has no misses.
public sealed class RehearsalSession
{
    private readonly List<List<int>> _missesByRound = [[]];
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

    public bool Reveal()
    {
        if (IsComplete || Revealed) return false;
        Revealed = true;
        return true;
    }

    // Grading is ignored until the text has been revealed.
    public bool Grade(bool knew)
    {
        if (IsComplete || !Revealed) return false;
        Revealed = false;
        if (!knew) _missesByRound[^1].Add(_round[_index]);
        if (++_index < _round.Count || _missesByRound[^1].Count == 0) return true;
        _round = [.. _missesByRound[^1]];
        _index = 0;
        _missesByRound.Add([]);
        return true;
    }
}

public sealed record RehearsalRecord(long RoomId, string RoomTitle, DateTimeOffset StartedAt, DateTimeOffset CompletedAt,
    IReadOnlyList<int> Positions, IReadOnlyList<IReadOnlyList<int>> MissesByRound, IReadOnlyList<long> FirstPassMissedLocusIds)
{
    public static RehearsalRecord From(RehearsalSession session, RoomSnapshot room, DateTimeOffset startedAt, DateTimeOffset completedAt) =>
        new(room.Id, room.Title, startedAt, completedAt, session.Positions,
            session.MissesByRound.Select(r => (IReadOnlyList<int>)r.ToArray()).ToArray(),
            session.FirstPassMisses.Select(p => room.Loci[p].Id).ToArray());
}

// One JSON object per line, appended per completed rehearsal. Never stored in palace.db.
public static class RehearsalLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void Append(string path, RehearsalRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.AppendAllText(path, JsonSerializer.Serialize(record, Json) + "\n");
    }

    public static IReadOnlyList<RehearsalRecord> Read(string path) => !File.Exists(path) ? []
        : File.ReadLines(path).Where(l => l.Trim().Length > 0).Select(l => JsonSerializer.Deserialize<RehearsalRecord>(l, Json)!).ToList();
}
