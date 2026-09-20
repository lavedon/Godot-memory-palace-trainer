namespace PalaceRoomViewer.Core;

// Forward is the fixed FRONT wall, independent of the camera's current heading.
public enum RoomWall { Left, Right, Forward, Back, Floor, Ceiling }

public sealed record WallTextureSources(IReadOnlyDictionary<RoomWall, string> Paths, IReadOnlyList<string> Warnings);

public sealed record WallTextureOptions(string? Left = null, string? Right = null,
    string? Forward = null, string? Back = null, string? Folder = null)
{
    public WallTextureSources Resolve(RoomSnapshot? room = null)
    {
        var selected = new Dictionary<RoomWall, (string Path, string BaseDirectory)>();
        var warnings = room?.ImageWarnings.ToList() ?? [];
        var workingDirectory = Environment.CurrentDirectory;
        if (room is not null)
            foreach (var (wall, path) in room.ImagePaths)
                selected[wall] = (path, Path.GetDirectoryName(room.DatabasePath)!);
        string? FullPath(string path, string description, string baseDirectory)
        {
            try { return Path.GetFullPath(path, baseDirectory); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                warnings.Add($"{description}: invalid path '{path}'. {ex.Message}");
                return null;
            }
        }

        if (Folder is not null)
        {
            var folder = FullPath(Folder, "Room texture folder", workingDirectory);
            if (folder is not null)
            {
                if (!Directory.Exists(folder))
                    warnings.Add($"Room texture folder does not exist or cannot be accessed: {folder}");
                else
                {
                    foreach (var wall in Enum.GetValues<RoomWall>())
                    {
                        var path = Path.Combine(folder, wall.ToString().ToLowerInvariant() + ".png");
                        // Missing folder files leave the database choice (or default) in place.
                        if (File.Exists(path)) selected[wall] = (path, workingDirectory);
                    }
                }
            }
        }

        foreach (var (wall, suppliedPath) in new[]
        {
            (RoomWall.Left, Left), (RoomWall.Right, Right), (RoomWall.Forward, Forward), (RoomWall.Back, Back)
        })
        {
            if (suppliedPath is null) continue;
            // Explicit options win regardless of argument order, even if the override is invalid.
            selected[wall] = (suppliedPath, workingDirectory);
        }
        var paths = new Dictionary<RoomWall, string>();
        foreach (var (wall, source) in selected)
        {
            var description = $"{wall} image";
            var path = FullPath(source.Path, description, source.BaseDirectory);
            if (path is null) continue;
            if (File.Exists(path)) paths[wall] = path;
            else warnings.Add($"{description} does not exist or cannot be accessed: {path}");
        }
        return new WallTextureSources(paths, warnings);
    }
}
