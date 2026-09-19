using System.Globalization;

namespace PalaceRoomViewer.Core;

public sealed record ViewerOptions(long RoomId, string DatabasePath)
{
    public const string DefaultDatabasePath = @"C:\tools\Data\palace.db";
    public const string Usage = "PalaceRoomViewer.exe -- --room <id> [--db <path>]";

    public static ViewerOptions Parse(IReadOnlyList<string> arguments)
    {
        long? roomId = null;
        string? databasePath = null;
        for (var i = 0; i < arguments.Count; i++)
        {
            var option = arguments[i];
            if (option is not ("--room" or "--db"))
                throw new ViewerException($"Unknown argument: {option}\n{Usage}");
            if (++i >= arguments.Count || arguments[i].StartsWith("--", StringComparison.Ordinal))
                throw new ViewerException($"A value is required after {option}.\n{Usage}");
            var value = arguments[i];
            if (option == "--room")
            {
                if (roomId.HasValue)
                    throw new ViewerException("Supply --room only once.");
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                    throw new ViewerException($"Invalid Room ID '{value}'. Use a positive integer.");
                roomId = id;
            }
            else
            {
                if (databasePath is not null)
                    throw new ViewerException("Supply --db only once.");
                if (string.IsNullOrWhiteSpace(value))
                    throw new ViewerException("The database path cannot be empty.");
                databasePath = value;
            }
        }
        if (!roomId.HasValue)
            throw new ViewerException($"Select a Room with --room <id>.\n{Usage}\nExample: PalaceRoomViewer.exe -- --room 8");
        return new ViewerOptions(roomId.Value, databasePath ?? DefaultDatabasePath);
    }
}

public sealed class ViewerException(string message, Exception? inner = null) : Exception(message, inner);
