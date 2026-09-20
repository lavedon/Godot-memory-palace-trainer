namespace PalaceRoomViewer.Core;

public sealed record Locus(long Id, int Position, string Text);
public sealed record LoadWarning(long LocusId, string Position, string Reason)
{
    public override string ToString() => $"Locus {LocusId} · Position {Position}: {Reason}";
}

public sealed record RoomSnapshot(long Id, string Title, IReadOnlyDictionary<int, Locus> Loci,
    IReadOnlyList<LoadWarning> Warnings, string DatabasePath)
{
    public IReadOnlyDictionary<RoomWall, string> ImagePaths { get; init; } = new Dictionary<RoomWall, string>();
    public IReadOnlyList<string> ImageWarnings { get; init; } = [];
}
