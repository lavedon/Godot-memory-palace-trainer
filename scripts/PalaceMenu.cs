using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

// Palace → Room picker. Lists every Room with its Loci count and whether its six
// surface images are set in palace.db and present on disk, plus rehearsal bests,
// the daily streak, and trophies. Opened with M.
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
    private Label _stats = null!;
    private Button _trophies = null!;
    private bool _showingTrophies;
    private IReadOnlyList<RehearsalRun> _runs = [];
    private IReadOnlyDictionary<long, RoomProgress> _progress = new Dictionary<long, RoomProgress>();
    private IReadOnlyList<PalaceSummary> _catalog = [];
    private long? _currentRoomId;
    public event Action<long>? RoomChosen;
    public event Action? CloseRequested;
    public bool IsOpen => _root.Visible;
    // False when there is nothing to return to (no Room loaded and no error to show).
    public bool CanClose => !_close.Disabled;
    public IReadOnlyList<PalaceSummary> Catalog => _catalog;
    public IReadOnlyList<RehearsalRun> Runs => _runs;
    public string DetailsText => _details.GetParsedText();
    public string StatsText => _stats.Text;

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
        _stats = ViewerHud.Text("", 14, new Color("f1d39b"));
        column.AddChild(_stats);

        var body = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16);
        column.AddChild(body);

        var palaceColumn = new VBoxContainer { CustomMinimumSize = new(340, 0) };
        body.AddChild(palaceColumn);
        palaceColumn.AddChild(ViewerHud.Text("PALACES", 12, ViewerHud.Muted));
        _palaces = new Tree
        {
            Columns = 3, HideRoot = true, ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row, SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _palaces.AddThemeFontSizeOverride("font_size", 15);
        _palaces.SetColumnTitle(0, "Palace");
        _palaces.SetColumnTitle(1, "Imaged");
        _palaces.SetColumnExpand(1, false);
        _palaces.SetColumnCustomMinimumWidth(1, 80);
        _palaces.SetColumnTitle(2, "Gold+");
        _palaces.SetColumnExpand(2, false);
        _palaces.SetColumnCustomMinimumWidth(2, 70);
        _palaces.ItemSelected += () => ShowPalace(_palaces.GetSelected()?.GetMetadata(0).AsInt32() ?? -1);
        palaceColumn.AddChild(_palaces);

        var roomColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddChild(roomColumn);
        _roomsTitle = ViewerHud.Text("ROOMS", 12, ViewerHud.Muted);
        roomColumn.AddChild(_roomsTitle);
        _rooms = new Tree
        {
            Columns = 4, HideRoot = true, ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row, SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        _rooms.AddThemeFontSizeOverride("font_size", 15);
        _rooms.SetColumnTitle(0, "Room");
        _rooms.SetColumnTitle(1, "Loci");
        // Long titles clip (full title in the tooltip) so every column stays visible.
        _rooms.SetColumnClipContent(0, true);
        _rooms.SetColumnTitle(2, "Best time");
        _rooms.SetColumnTitle(3, "Background images");
        _rooms.SetColumnExpand(1, false);
        _rooms.SetColumnCustomMinimumWidth(1, 60);
        _rooms.SetColumnExpand(2, false);
        _rooms.SetColumnCustomMinimumWidth(2, 170);
        _rooms.SetColumnExpand(3, false);
        _rooms.SetColumnCustomMinimumWidth(3, 190);
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
        _trophies = new Button { Text = "Trophies", CustomMinimumSize = new(120, 38), ToggleMode = true };
        _trophies.Toggled += on =>
        {
            _showingTrophies = on;
            if (on) ShowTrophies(); else ShowSelectedRoom();
        };
        buttons.AddChild(_trophies);
        _close = new Button { Text = "Close", CustomMinimumSize = new(110, 38) };
        _close.Pressed += () => CloseRequested?.Invoke();
        buttons.AddChild(_close);
        _load = new Button { Text = "Load Room", CustomMinimumSize = new(140, 38), Disabled = true };
        _load.Pressed += LoadSelected;
        buttons.AddChild(_load);
    }

    public void Open(string databasePath, long? currentRoomId, bool canClose, string? progressDatabasePath = null)
    {
        _showingTrophies = false;
        _trophies.SetPressedNoSignal(false);
        LoadProgress(progressDatabasePath ?? databasePath);
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
            var golden = palace.Rooms.Count(r => Progress(r.Id)?.BestMedal >= Medal.Gold);
            item.SetText(2, $"{golden}/{palace.Rooms.Count}");
            item.SetTextAlignment(2, HorizontalAlignment.Center);
            item.SetCustomColor(2, golden > 0 ? ViewerHud.MedalColor(Medal.Gold) : ViewerHud.Muted);
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
            var progress = Progress(room.Id);
            item.SetText(2, progress?.Fastest is { } fastest
                ? $"{RehearsalScoring.FormatTime(fastest.DurationMs)}   {progress.BestMedal.ToString().ToUpperInvariant()}" : "not yet");
            item.SetCustomColor(2, ViewerHud.MedalColor(progress?.BestMedal ?? Medal.None));
            item.SetText(3, room.ImageSummary);
            item.SetCustomColor(3, room.Missing > 0 ? Missing : room.HasImages ? Found : ViewerHud.Muted);
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
        if (room is null || _showingTrophies) return;
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
        _details.Text = $"{header}\n{ProgressSummary(room)}[table=3]{string.Concat(cells)}[/table]";
    }

    private RoomProgress? Progress(long roomId) => _progress.GetValueOrDefault(roomId);

    // History is read from palace.db on every open. A locked or unreadable table only hides stats.
    private void LoadProgress(string progressDatabasePath)
    {
        try { _runs = new RehearsalStore().Load(progressDatabasePath); }
        catch (Exception ex)
        {
            _runs = [];
            GD.PrintErr(ex.Message);
        }
        _progress = RoomProgress.ByRoom(_runs);
        var streak = RehearsalStreak.Days(_runs.Select(r => r.Day), DateOnly.FromDateTime(DateTime.Now));
        var trophies = Achievements.Unlocked(_runs).Count;
        var medals = _progress.Values.GroupBy(p => p.BestMedal).Where(g => g.Key >= Medal.Gold).OrderByDescending(g => g.Key)
            .Select(g => $"{g.Count()} {g.Key}");
        _stats.Text = _runs.Count == 0 ? "No rehearsals yet. Load a Room and press R to set your first time."
            : $"Day {streak} streak   ·   {Plural(_runs.Count, "rehearsal")}   ·   {trophies} of {Achievements.All.Count} trophies" +
              string.Concat(medals.Select(m => "   ·   " + m));
    }

    private string ProgressSummary(RoomSummary room)
    {
        if (Progress(room.Id) is not { } progress) return "[color=#93a7ac]Not rehearsed yet. Load it and press R.[/color]\n";
        var medal = progress.BestMedal;
        var perfect = progress.FastestPerfect is { } p ? RehearsalScoring.FormatTime(p.DurationMs) : "none yet";
        var days = (DateTime.Now.Date - progress.LastRehearsed!.Value.LocalDateTime.Date).Days;
        var last = days switch { 0 => "today", 1 => "yesterday", _ => $"{days} days ago" };
        return $"[color=#{ViewerHud.MedalColor(medal).ToHtml(false)}][b]{medal.ToString().ToUpperInvariant()}[/b][/color]   " +
            $"Best {RehearsalScoring.FormatTime(progress.Fastest!.DurationMs)}   ·   flawless best {perfect}   ·   " +
            $"high score {progress.BestScore:N0}   ·   combo ×{progress.BestCombo}   ·   {Plural(progress.Runs.Count, "run")}, last {last}\n";
    }

    private void ShowTrophies()
    {
        var unlocked = Achievements.Unlocked(_runs).ToDictionary(u => u.Achievement.Id, u => u.UnlockedAt);
        var cells = Achievements.All.Select(a => unlocked.TryGetValue(a.Id, out var at)
            ? $"[cell padding=0,2,20,2][color=#f1d39b][b]{a.Name}[/b][/color][/cell][cell padding=0,2,20,2]{a.Description}[/cell][cell][color=#93a7ac]{at.LocalDateTime:MMM d}[/color][/cell]"
            : $"[cell padding=0,2,20,2][color=#5d7076]{a.Name}[/color][/cell][cell padding=0,2,20,2][color=#5d7076]{a.Description}[/color][/cell][cell][color=#5d7076]locked[/color][/cell]");
        _details.Text = $"[b]Trophies[/b]   {unlocked.Count} of {Achievements.All.Count} unlocked\n[table=3]{string.Concat(cells)}[/table]";
    }


    private void LoadSelected()
    {
        if (SelectedRoom() is { } room) RoomChosen?.Invoke(room.Id);
    }

    private static string Plural(int count, string one, string? many = null) => $"{count} {(count == 1 ? one : many ?? one + "s")}";
    private static string Escape(string text) => text.Replace("[", "[lb]");
}
