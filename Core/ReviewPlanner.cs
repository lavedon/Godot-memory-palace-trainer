namespace PalaceRoomViewer.Core;

// A Locus's predicted recall today. Reviews counts the rehearsals it has been tested in.
public sealed record LocusRecall(long LocusId, int Position, double Recall, int Reviews, DateOnly LastReviewed);

// FSRS's view of one Room today. Untested Loci (never in a counted rehearsal) count as forgotten.
public sealed record RoomForecast(long RoomId, IReadOnlyList<LocusRecall> Tested, IReadOnlyList<int> UntestedPositions, DateOnly? LastRehearsed)
{
    public bool Rehearsed => LastRehearsed is not null;
    public int LociCount => Tested.Count + UntestedPositions.Count;
    public double ExpectedForgotten => Tested.Sum(l => 1 - l.Recall) + UntestedPositions.Count;
    public double AverageRecall => LociCount == 0 ? 1 : Tested.Sum(l => l.Recall) / LociCount;
    // Loci below the desired retention, plus untested ones, in Position order.
    public IReadOnlyList<int> WeakPositions =>
        Tested.Where(l => l.Recall < Fsrs.DesiredRetention).Select(l => l.Position).Concat(UntestedPositions).Order().ToArray();
    public bool Due => Rehearsed && WeakPositions.Count > 0;
}

// Runs FSRS over rehearsal history. Only completed rehearsals count (loop drills are never saved),
// only their first pass (later rounds re-ask misses the same session), and only a Room's first
// rehearsal on each day, since FSRS schedules in whole days.
public static class ReviewPlanner
{
    // locusIds: each Room's current Loci by Position. Rooms absent from it are ignored.
    public static IReadOnlyDictionary<long, RoomForecast> Forecast(IEnumerable<RehearsalRun> runs,
        IReadOnlyDictionary<long, IReadOnlyDictionary<int, long>> locusIds, DateOnly today)
    {
        var histories = new Dictionary<long, List<(DateOnly Day, bool Knew)>>();
        var lastRehearsed = new Dictionary<long, DateOnly>();
        var counted = new HashSet<(long RoomId, DateOnly Day)>();
        foreach (var run in runs.OrderBy(r => r.CompletedAt).ThenBy(r => r.Id))
        {
            if (!locusIds.TryGetValue(run.RoomId, out var current) || run.Day > today || !counted.Add((run.RoomId, run.Day))) continue;
            lastRehearsed[run.RoomId] = run.Day;
            var missed = run.MissesByRound[0].ToHashSet();
            for (var i = 0; i < run.Positions.Count; i++)
            {
                var position = run.Positions[i];
                // Older runs did not record every Locus Id; their Positions map to today's Loci.
                long? id = run.LocusIds is { } ids ? ids[i] : current.TryGetValue(position, out var now) ? now : null;
                if (id is not { } locus) continue;
                if (!histories.TryGetValue(locus, out var history)) histories[locus] = history = [];
                history.Add((run.Day, !missed.Contains(position)));
            }
        }

        // One batched call into the scheduler for every tested Locus that still exists.
        var tested = locusIds.SelectMany(room => room.Value.Where(l => histories.ContainsKey(l.Value))
            .Select(l => (RoomId: room.Key, Position: l.Key, LocusId: l.Value))).ToArray();
        var memories = Fsrs.MemoryStates(tested.Select(t => (IReadOnlyList<FsrsReview>)Reviews(histories[t.LocusId])).ToArray());
        var recalls = tested.Select((t, i) =>
        {
            var history = histories[t.LocusId];
            var last = history[^1].Day;
            return (t.RoomId, Recall: new LocusRecall(t.LocusId, t.Position, Fsrs.Retrievability(memories[i], today.DayNumber - last.DayNumber), history.Count, last));
        }).ToLookup(r => r.RoomId, r => r.Recall);

        return locusIds.ToDictionary(room => room.Key, room =>
        {
            var roomTested = recalls[room.Key].OrderBy(l => l.Position).ToArray();
            var untested = room.Value.Where(l => !histories.ContainsKey(l.Value)).Select(l => l.Key).Order().ToArray();
            return new RoomForecast(room.Key, roomTested, untested, lastRehearsed.TryGetValue(room.Key, out var day) ? day : null);
        });
    }

    // Due Rooms first, most likely-forgotten Loci first; ties go to the Room rehearsed longest ago.
    public static IReadOnlyList<RoomForecast> Ranked(IEnumerable<RoomForecast> forecasts) =>
        forecasts.Where(f => f.Due).OrderByDescending(f => f.ExpectedForgotten).ThenBy(f => f.LastRehearsed).ThenBy(f => f.RoomId).ToArray();

    private static FsrsReview[] Reviews(List<(DateOnly Day, bool Knew)> history) =>
        history.Select((h, i) => new FsrsReview(h.Knew ? FsrsReview.Good : FsrsReview.Again,
            i == 0 ? 0 : h.Day.DayNumber - history[i - 1].Day.DayNumber)).ToArray();
}
