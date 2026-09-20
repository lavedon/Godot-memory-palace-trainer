using System.Collections.ObjectModel;

namespace PalaceRoomViewer.Core;

public static class RoomImageColumns
{
    public static IReadOnlyDictionary<RoomWall, string> ByWall { get; } =
        new ReadOnlyDictionary<RoomWall, string>(new Dictionary<RoomWall, string>
        {
            [RoomWall.Left] = "LeftImagePath",
            [RoomWall.Right] = "RightImagePath",
            [RoomWall.Forward] = "ForwardImagePath",
            [RoomWall.Back] = "BackImagePath",
            [RoomWall.Floor] = "FloorImagePath",
            [RoomWall.Ceiling] = "CeilingImagePath"
        });
}
