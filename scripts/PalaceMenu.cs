using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

// Palace → Room picker. Lists every Room with its Loci count and whether its six
// surface images are set in palace.db and present on disk. Opened with M.
public partial class PalaceMenu : CanvasLayer
{
    private static readonly Color Found = new("88d8c4");
    private static readonly Color Missing = new("edbf7f");
    private Control _root = null!;
    private Label _database = null!;
    private Tree _palaces = null!;
    private Tree _rooms = null!;
    private Label _roomsTitle = null!;
    private RichTextLabel _details = null!;
    private Button _load = null!;
    private Button _close = null!;
    private Label _hint = null!;
    private IReadOnlyList<PalaceSummary> _catalog = [];
    private long? _currentRoomId;
    public event Action<long>? RoomChosen;
    public event Action? CloseRequested;
    public bool IsOpen => _root.Visible;
    // False when there is nothing to return to (no Room loaded and no error to show).
    public bool CanClose => !_close.Disabled;
    public IReadOnlyList<PalaceSummary> Catalog => _catalog;

    public override void _Ready()
    {
        Layer = 2;
        _root = new Control { Name = "PalaceMenu", Visible = false };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, .55f) };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", ViewerHud.Surface());
        _root.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.OffsetLeft = -580; panel.OffsetRight = 580;
        panel.OffsetTop = -330; panel.OffsetBottom = 330;
        var column = ViewerHud.Column(panel);
        column.AddThemeConstantOverride("separation", 10);
        column.AddChild(ViewerHud.Text("P A L A C E S   /   C H O O S E   A   R O O M", 12, ViewerHud.Accent));
        _database = ViewerHud.Text("", 12, ViewerHud.Muted);
        _database.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        column.AddChild(_database);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16);
        column.AddChild(body);

        var palaceColumn = new VBoxContainer { CustomMinimumSize = new(340, 0) };
        body.AddChild(palaceColumn);
        palaceColumn.AddChild(ViewerHud.Text("PALACES", 12, ViewerHud.Muted));
        _palaces = new Tree
        {
            Columns = 2, HideRoot = true, ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row, SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _palaces.AddThemeFontSizeOverride("font_size", 15);
        _palaces.SetColumnTitle(0, "Palace");
        _palaces.SetColumnTitle(1, "Imaged");
        _palaces.SetColumnExpand(1, false);
        _palaces.SetColumnCustomMinimumWidth(1, 80);
        _palaces.ItemSelected += () => ShowPalace(_palaces.GetSelected()?.GetMetadata(0).AsInt32() ?? -1);
        palaceColumn.AddChild(_palaces);

        var roomColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddChild(roomColumn);
        _roomsTitle = ViewerHud.Text("ROOMS", 12, ViewerHud.Muted);
        roomColumn.AddChild(_roomsTitle);
        _rooms = new Tree
        {
            Columns = 3, HideRoot = true, ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row, SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _rooms.AddThemeFontSizeOverride("font_size", 15);
        _rooms.SetColumnTitle(0, "Room");
        _rooms.SetColumnTitle(1, "Loci");
        _rooms.SetColumnTitle(2, "Background images");
        _rooms.SetColumnExpand(1, false);
        _rooms.SetColumnCustomMinimumWidth(1, 60);
        _rooms.SetColumnExpand(2, false);
        _rooms.SetColumnCustomMinimumWidth(2, 190);
        _rooms.ItemSelected += ShowSelectedRoom;
        _rooms.ItemActivated += LoadSelected;
        roomColumn.AddChild(_rooms);

        _details = ViewerHud.RichText(14);
        _details.BbcodeEnabled = true;
        _details.CustomMinimumSize = new(0, 215);
        roomColumn.AddChild(_details);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(buttons);
        _hint = ViewerHud.Text("", 12, ViewerHud.Muted);
        _hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        buttons.AddChild(_hint);
        _close = new Button { Text = "Close", CustomMinimumSize = new(110, 38) };
        _close.Pressed += () => CloseRequested?.Invoke();
        buttons.AddChild(_close);
        _load = new Button { Text = "Load Room", CustomMinimumSize = new(140, 38), Disabled = true };
        _load.Pressed += LoadSelected;
        buttons.AddChild(_load);
    }

    public void Open(string databasePath, long? currentRoomId, bool canClose)
    {
        _currentRoomId = currentRoomId;
        _close.Disabled = !canClose;
        _hint.Text = "Double-click or Enter loads a Room" + (canClose ? "   ·   M or Esc closes" : "");
        _database.Text = databasePath;
        _database.TooltipText = databasePath;
        _palaces.Clear();
        _rooms.Clear();
        _load.Disabled = true;
        _root.Visible = true;
        try
        {
            // Reloaded on every open so newly added Rooms or images appear without restarting.
            _catalog = new PalaceCatalog().Load(databasePath);
        }
        catch (Exception ex)
        {
            _catalog = [];
            _details.Text = $"[color=#{Missing.ToHtml(false)}]{Escape(ex.Message)}[/color]";
            return;
        }
        if (_catalog.Count == 0)
        {
            _details.Text = "This database has no Rooms.";
            return;
        }
        var palaceRoot = _palaces.CreateItem();
        var start = currentRoomId is { } id ? _catalog.ToList().FindIndex(p => p.Rooms.Any(r => r.Id == id)) : -1;
        start = Math.Max(start, 0);
        TreeItem? startItem = null;
        for (var i = 0; i < _catalog.Count; i++)
        {
            var palace = _catalog[i];
            var item = _palaces.CreateItem(palaceRoot);
            item.SetMetadata(0, i);
            item.SetText(0, palace.Name);
            item.SetTooltipText(0, $"{palace.Name}\n{Plural(palace.Rooms.Count, "Room")} · {palace.RoomsWithImages} with images" +
                (palace.Description is { Length: > 0 } d ? $"\n{d}" : ""));
            // "Imaged" = Rooms with at least one background image found on disk.
            item.SetText(1, $"{palace.RoomsWithImages}/{palace.Rooms.Count}");
            item.SetTextAlignment(1, HorizontalAlignment.Center);
            item.SetCustomColor(1, palace.RoomsWithImages > 0 ? Found : ViewerHud.Muted);
            if (i == start) startItem = item;
        }
        startItem?.Select(0);
        _rooms.GrabFocus();
    }

    public void Close() => _root.Visible = false;

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!IsOpen || @event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode is Key.Enter or Key.KpEnter && !_load.Disabled)
        {
            LoadSelected();
            GetViewport().SetInputAsHandled();
        }
    }

    private void ShowPalace(int index)
    {
        _rooms.Clear();
        _load.Disabled = true;
        _details.Text = "Select a Room to see its images.";
        if (index < 0 || index >= _catalog.Count) return;
        var palace = _catalog[index];
        _roomsTitle.Text = $"ROOMS IN {palace.Name.ToUpperInvariant()}   ·   {Plural(palace.Rooms.Count, "Room").ToUpperInvariant()}, {palace.RoomsWithImages} WITH IMAGES";
        var root = _rooms.CreateItem();
        TreeItem? current = null;
        foreach (var room in palace.Rooms)
        {
            var item = _rooms.CreateItem(root);
            item.SetMetadata(0, room.Id);
            item.SetText(0, (room.Id == _currentRoomId ? "» " : "") + $"{room.Id:00}   {room.Title}");
            item.SetTooltipText(0, room.Title);
            item.SetText(1, room.LociCount.ToString());
            item.SetTextAlignment(1, HorizontalAlignment.Center);
            item.SetText(2, room.ImageSummary);
            item.SetCustomColor(2, room.Missing > 0 ? Missing : room.HasImages ? Found : ViewerHud.Muted);
            if (room.Id == _currentRoomId) current = item;
        }
        if (palace.Rooms.Count == 0) _details.Text = "This Palace has no Rooms yet.";
        var select = current ?? root.GetFirstChild();
        if (select is null) return;
        select.Select(0);
        _rooms.ScrollToItem(select);
    }

    private RoomSummary? SelectedRoom()
    {
        var id = _rooms.GetSelected()?.GetMetadata(0).AsInt64();
        return id is null ? null : _catalog.SelectMany(p => p.Rooms).FirstOrDefault(r => r.Id == id);
    }

    private void ShowSelectedRoom()
    {
        var room = SelectedRoom();
        _load.Disabled = room is null;
        if (room is null) return;
        var header = $"[b]Room {room.Id}[/b] · {Plural(room.LociCount, "Locus", "Loci")} · {room.ImageSummary}" +
            (room.Id == _currentRoomId ? "   [color=#93a7ac](currently loaded)[/color]" : "");
        var cells = new List<string>();
        foreach (var image in room.Images)
        {
            var (state, color) = image.State switch
            {
                SurfaceImageState.Found => ("found", Found),
                SurfaceImageState.Missing => ("MISSING", Missing),
                _ => ("none", ViewerHud.Muted)
            };
            var name = image.Wall == RoomWall.Forward ? "FRONT" : image.Wall.ToString().ToUpperInvariant();
            var path = image.State == SurfaceImageState.None ? "default surface" : Escape(image.FullPath ?? image.StoredPath ?? "");
            cells.Add($"[cell padding=0,2,24,2]{name}[/cell][cell padding=0,2,24,2][color=#{color.ToHtml(false)}]{state}[/color][/cell][cell][color=#93a7ac]{path}[/color][/cell]");
        }
        _details.Text = $"{header}\n[table=3]{string.Concat(cells)}[/table]";
    }

    private void LoadSelected()
    {
        if (SelectedRoom() is { } room) RoomChosen?.Invoke(room.Id);
    }

    private static string Plural(int count, string one, string? many = null) => $"{count} {(count == 1 ? one : many ?? one + "s")}";
    private static string Escape(string text) => text.Replace("[", "[lb]");
}
