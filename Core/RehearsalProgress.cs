namespace PalaceRoomViewer.Core;

// One completed rehearsal, as stored in the RehearsalRuns table.
public sealed record RehearsalRun(long RoomId, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, long DurationMs,
    IReadOnlyList<int> Positions, IReadOnlyList<IReadOnlyList<int>> MissesByRound, IReadOnlyList<long> SplitsMs,
    IReadOnlyList<long> FirstPassMissedLocusIds, int BestCombo, int Score)
{
    public long Id { get; init; }
    // Loci.Id at each Position, aligned with Positions. Null for runs saved before this was recorded.
    public IReadOnlyList<long>? LocusIds { get; init; }
    public int LociCount => Positions.Count;
    public int FirstPassKnown => LociCount - MissesByRound[0].Count;
    public int Rounds => MissesByRound.Count;
    public bool Perfect => MissesByRound[0].Count == 0;
    public Medal Medal => RehearsalScoring.MedalFor(LociCount, FirstPassKnown, DurationMs);
    public DateOnly Day => DateOnly.FromDateTime(CompletedAt.LocalDateTime);

    public long? SplitFor(int position)
    {
        for (var i = 0; i < Positions.Count && i < SplitsMs.Count; i++)
            if (Positions[i] == position) return SplitsMs[i];
        return null;
    }

    public static RehearsalRun From(RehearsalSession session, RoomSnapshot room, DateTimeOffset startedAt, DateTimeOffset completedAt) =>
        new(room.Id, startedAt, completedAt, session.ElapsedMs, session.Positions,
            session.MissesByRound.Select(r => (IReadOnlyList<int>)r.ToArray()).ToArray(),
            session.FirstPassSplitsMs.ToArray(),
            session.FirstPassMisses.Select(p => room.Loci[p].Id).ToArray(),
            session.BestCombo, session.Score)
        { LocusIds = session.Positions.Select(p => room.Loci[p].Id).ToArray() };
}

// Personal bests for one Room.
public sealed record RoomProgress(long RoomId, IReadOnlyList<RehearsalRun> Runs)
{
    public RehearsalRun? Fastest => Runs.MinBy(r => r.DurationMs);
    public RehearsalRun? FastestPerfect => Runs.Where(r => r.Perfect).MinBy(r => r.DurationMs);
    public int BestScore => Runs.Select(r => r.Score).DefaultIfEmpty().Max();
    public int BestCombo => Runs.Select(r => r.BestCombo).DefaultIfEmpty().Max();
    public Medal BestMedal => Runs.Select(r => r.Medal).DefaultIfEmpty().Max();
    public RehearsalRun? BestFirstPass => Runs.MaxBy(r => (r.FirstPassKnown / (double)r.LociCount, -r.DurationMs));
    public DateTimeOffset? LastRehearsed => Runs.Count == 0 ? null : Runs.Max(r => r.CompletedAt);

    public static IReadOnlyDictionary<long, RoomProgress> ByRoom(IEnumerable<RehearsalRun> runs) =>
        runs.GroupBy(r => r.RoomId).ToDictionary(g => g.Key, g => new RoomProgress(g.Key, g.ToArray()));
}

// How long a Room took to learn: from its first loop drill (FirstLoopDrills) to its first flawless
// rehearsal of all 26 Positions (RehearsalRuns). Only the start is stored; the finish is derived.
public sealed record RoomLearning(long RoomId, DateTimeOffset? StartedAt, RehearsalRun? LearnedBy)
{
    public bool Learned => LearnedBy is not null;
    // Null while still learning, and for Rooms learned before any loop drill was recorded.
    public TimeSpan? Duration => StartedAt is { } start && LearnedBy is { } run && run.CompletedAt >= start ? run.CompletedAt - start : null;

    // A flawless first pass through a full Room. The same run earns Full House.
    public static bool Learns(RehearsalRun run) => run.Perfect && run.LociCount >= RoomLayout.Capacity;

    // Every Room with a recorded first loop drill or a learning run.
    public static IReadOnlyDictionary<long, RoomLearning> ByRoom(IReadOnlyDictionary<long, DateTimeOffset> starts, IEnumerable<RehearsalRun> runs)
    {
        var learnedBy = runs.Where(Learns).GroupBy(r => r.RoomId).ToDictionary(g => g.Key, g => g.MinBy(r => (r.CompletedAt, r.Id))!);
        return starts.Keys.Union(learnedBy.Keys).ToDictionary(id => id,
            id => new RoomLearning(id, starts.TryGetValue(id, out var start) ? start : null, learnedBy.GetValueOrDefault(id)));
    }

    // "2 d 4 h", "3 h 12 min", "14 min".
    public static string Format(TimeSpan span) =>
        span.TotalDays >= 1 ? $"{(int)span.TotalDays} d {span.Hours} h"
        : span.TotalHours >= 1 ? $"{span.Hours} h {span.Minutes} min"
        : span.TotalMinutes >= 1 ? $"{span.Minutes} min"
        : "under a minute";
}

public static class RehearsalStreak
{
    // Consecutive days with at least one rehearsal, ending today (or yesterday, so the streak survives until tonight).
    public static int Days(IEnumerable<DateOnly> days, DateOnly today)
    {
        var set = days.ToHashSet();
        var day = set.Contains(today) ? today : today.AddDays(-1);
        var count = 0;
        while (set.Contains(day)) { count++; day = day.AddDays(-1); }
        return count;
    }
}

public sealed record Achievement(string Id, string Name, string Description, Func<RehearsalRun, IReadOnlyList<RehearsalRun>, bool> Earned);

public sealed record UnlockedAchievement(Achievement Achievement, DateTimeOffset UnlockedAt);

// Achievements are derived from run history, so they need no extra storage.
// Each check sees the run being judged and every run completed before it.
public static class Achievements
{
    private static int StreakAt(RehearsalRun run, IReadOnlyList<RehearsalRun> earlier) =>
        RehearsalStreak.Days(earlier.Select(r => r.Day).Append(run.Day), run.Day);

    private static bool BeatBest(RehearsalRun run, IReadOnlyList<RehearsalRun> earlier)
    {
        var previous = earlier.Where(r => r.RoomId == run.RoomId).ToArray();
        return previous.Length > 0 && run.DurationMs < previous.Min(r => r.DurationMs);
    }

    public static readonly IReadOnlyList<Achievement> All =
    [
        new("first-steps", "First Steps", "Clear any Room.", (_, _) => true),
        new("flawless", "Flawless", "Recall every Locus on the first pass.", (r, _) => r.Perfect),
        new("full-house", "Full House", "Go flawless through a full 26-Locus Room.", (r, _) => RoomLearning.Learns(r)),
        new("silver", "Sharp Memory", "Earn a Silver medal or better.", (r, _) => r.Medal >= Medal.Silver),
        new("gold", "Golden Walk", "Earn a Gold medal.", (r, _) => r.Medal >= Medal.Gold),
        new("platinum", "Platinum Mind", "Earn a Platinum medal.", (r, _) => r.Medal == Medal.Platinum),
        new("combo-10", "On a Roll", "Reach a ×10 combo.", (r, _) => r.BestCombo >= 10),
        new("combo-20", "Unstoppable", "Reach a ×20 combo.", (r, _) => r.BestCombo >= 20),
        new("personal-best", "Faster Than Before", "Beat your best time on a Room.", BeatBest),
        new("comeback", "Comeback", "Clear a Room after missing 5 or more on the first pass.", (r, _) => r.MissesByRound[0].Count >= 5),
        new("high-score", "Five Thousand", "Score 5,000 points in one rehearsal.", (r, _) => r.Score >= 5_000),
        new("explorer", "Explorer", "Rehearse 5 different Rooms.", (r, e) => e.Select(x => x.RoomId).Append(r.RoomId).Distinct().Count() >= 5),
        new("marathon", "Marathon", "Clear 3 different Rooms in one day.", (r, e) => e.Where(x => x.Day == r.Day).Select(x => x.RoomId).Append(r.RoomId).Distinct().Count() >= 3),
        new("dedicated", "Dedicated", "Complete 10 rehearsals.", (_, e) => e.Count + 1 >= 10),
        new("devoted", "Devoted", "Complete 50 rehearsals.", (_, e) => e.Count + 1 >= 50),
        new("streak-3", "Three in a Row", "Rehearse three days in a row.", (r, e) => StreakAt(r, e) >= 3),
        new("streak-7", "Week Walker", "Rehearse seven days in a row.", (r, e) => StreakAt(r, e) >= 7),
        new("night-owl", "Night Owl", "Finish a rehearsal between midnight and 4 a.m.", (r, _) => r.CompletedAt.LocalDateTime.Hour < 4),
        new("early-bird", "Early Bird", "Finish a rehearsal between 4 and 7 a.m.", (r, _) => r.CompletedAt.LocalDateTime.Hour is >= 4 and < 7),
    ];

    public static IReadOnlyList<UnlockedAchievement> Unlocked(IEnumerable<RehearsalRun> runs)
    {
        var ordered = runs.OrderBy(r => r.CompletedAt).ThenBy(r => r.Id).ToArray();
        var unlocked = new List<UnlockedAchievement>();
        foreach (var achievement in All)
            for (var i = 0; i < ordered.Length; i++)
                if (achievement.Earned(ordered[i], ordered[..i]))
                {
                    unlocked.Add(new(achievement, ordered[i].CompletedAt));
                    break;
                }
        return unlocked;
    }

    // Achievements earned by adding newRun to the history in earlier.
    public static IReadOnlyList<Achievement> NewlyUnlocked(IReadOnlyList<RehearsalRun> earlier, RehearsalRun newRun)
    {
        var before = Unlocked(earlier).Select(u => u.Achievement.Id).ToHashSet();
        return Unlocked(earlier.Append(newRun)).Select(u => u.Achievement).Where(a => !before.Contains(a.Id)).ToArray();
    }
}

// What a just-finished rehearsal achieved compared with the history before it.
public sealed record RehearsalOutcome(RehearsalRun Run, RoomProgress Before, IReadOnlyList<Achievement> Unlocked, int StreakDays, string? SaveError)
{
    // Set when this run is the Room's first flawless rehearsal of all 26 Positions.
    public RoomLearning? JustLearned { get; init; }
    // The quickest any earlier Room was learned, to compare with JustLearned.
    public TimeSpan? FastestLearnedBefore { get; init; }
    public bool FirstClear => Before.Runs.Count == 0;
    public long? PreviousBestMs => Before.Fastest?.DurationMs;
    public bool NewBestTime => PreviousBestMs is { } best && Run.DurationMs < best;
    public bool NewHighScore => !FirstClear && Run.Score > Before.BestScore;
    public bool NewBestCombo => !FirstClear && Run.BestCombo > Before.BestCombo;
    public bool NewBestMedal => Run.Medal > Before.BestMedal;
    public bool Celebrate => NewBestTime || NewHighScore || (NewBestMedal && Run.Medal >= Medal.Gold) || Unlocked.Count > 0 || JustLearned is not null;

    // learningStarts holds each Room's first loop drill (RehearsalStore.LoadFirstLoopDrills).
    public static RehearsalOutcome Create(IReadOnlyList<RehearsalRun> history, RehearsalRun run, DateOnly today, string? saveError = null,
        IReadOnlyDictionary<long, DateTimeOffset>? learningStarts = null)
    {
        var before = new RoomProgress(run.RoomId, history.Where(r => r.RoomId == run.RoomId).ToArray());
        var learned = RoomLearning.Learns(run) && !before.Runs.Any(RoomLearning.Learns);
        learningStarts ??= new Dictionary<long, DateTimeOffset>();
        return new(run, before, Achievements.NewlyUnlocked(history, run), RehearsalStreak.Days(history.Append(run).Select(r => r.Day), today), saveError)
        {
            JustLearned = learned ? new RoomLearning(run.RoomId, learningStarts.TryGetValue(run.RoomId, out var start) ? start : null, run) : null,
            FastestLearnedBefore = learned ? RoomLearning.ByRoom(learningStarts, history).Values.Min(l => l.Duration) : null
        };
    }
}
