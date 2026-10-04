namespace PalaceRoomViewer.Core;

// The order a rehearsal walks a Room. Standard is Position order; the advanced routes walk the
// walls band by band (the 8 wall-slots of one Slice, then the next) and end on floor and ceiling.
public enum RehearsalRoute { Standard, TopFirst, BottomFirst }

public static class RehearsalRoutes
{
    public static readonly IReadOnlyList<RehearsalRoute> Advanced = [RehearsalRoute.TopFirst, RehearsalRoute.BottomFirst];
    // Advanced rehearsal unlocks once any saved rehearsal of the Room earned this medal or better.
    public const Medal UnlockMedal = Medal.Gold;
    private const int WallSlots = 8, Floor = 25, Ceiling = 26;

    public static bool IsAdvanced(this RehearsalRoute route) => route != RehearsalRoute.Standard;

    public static bool Unlocked(IEnumerable<RehearsalRun> roomRuns) => roomRuns.Any(r => r.Medal >= UnlockMedal);

    // The Room's populated Positions in route order. Bands are Slice 3 (where wall meets ceiling),
    // Slice 2 (center) and Slice 1 (where floor meets wall); each band runs wall-slot 1 to 8.
    // Both advanced routes end with the floor, then the ceiling.
    public static IReadOnlyList<int> Order(RehearsalRoute route, IEnumerable<int> positions)
    {
        var present = positions.ToHashSet();
        if (!route.IsAdvanced()) return present.Order().ToArray();
        int[] slices = route == RehearsalRoute.TopFirst ? [3, 2, 1] : [1, 2, 3];
        return slices.SelectMany(slice => Enumerable.Range(0, WallSlots).Select(slot => slot * 3 + slice))
            .Append(Floor).Append(Ceiling).Where(present.Contains).ToArray();
    }

    public static RehearsalRoute Pick(Random random) => Advanced[random.Next(Advanced.Count)];

    public static string Name(this RehearsalRoute route) => route switch
    {
        RehearsalRoute.TopFirst => "top band first",
        RehearsalRoute.BottomFirst => "bottom band first",
        _ => "Position order"
    };
}
