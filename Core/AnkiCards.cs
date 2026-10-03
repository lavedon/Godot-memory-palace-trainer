namespace PalaceRoomViewer.Core;

// Whether the memory-palace-cli card script can build a Room's grouped Anki cards, and the
// command to run it. Command is null unless Ready. The viewer never runs the script.
public sealed record AnkiCardCommand(bool Ready, string Status, string? Command, bool ScriptFound);

// The viewer only shows (and copies) the command; the script itself talks to Anki.
public static class AnkiCards
{
    public const string DefaultScript = @"C:\my-coding-projects\memory-palace-cli\scripts\db_to_anki_room_cloze.py";

    // Mirrors the script's own rules: it walks a Palace's Rooms through PreviousId from the
    // Room with none, skips Rooms without Loci, and needs Rooms.RoomImage to exist on disk.
    public static AnkiCardCommand For(PalaceSummary palace, RoomSummary room, string databasePath, string script = DefaultScript)
    {
        var scriptFound = File.Exists(script);
        AnkiCardCommand NotReady(string why) => new(false, why, null, scriptFound);
        if (palace.Id == PalaceCatalog.UngroupedPalaceId || room.PalaceId is null) return NotReady("this Room is not in a Palace");
        if (room.LociCount == 0) return NotReady("this Room has no Loci");
        if (room.InPalaceOrder is null) return NotReady("the database has no Rooms.PreviousId column for the Palace's Room order");
        if (room.InPalaceOrder == false) return NotReady("this Room is not linked into its Palace's Room order (Rooms.PreviousId)");
        if (string.IsNullOrWhiteSpace(room.RoomImage)) return NotReady("no room image is set (Rooms.RoomImage)");
        if (!Path.IsPathFullyQualified(room.RoomImage)) return NotReady($"the room image path is relative; the card script needs a full path: {room.RoomImage}");
        if (!room.RoomImageExists) return NotReady($"the room image file is missing: {room.RoomImage}");
        var command = $"python {Quote(script)} --db {Quote(Path.GetFullPath(databasePath))} --palace {palace.Id} --rooms {room.Id}";
        return new(true, "room image found", command, scriptFound);
    }

    // Double quotes work in PowerShell and cmd; paths here never contain a double quote.
    private static string Quote(string path) => $"\"{path}\"";
}
