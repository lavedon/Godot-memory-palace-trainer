using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

// Palace → Room picker. Lists every Room with its Loci count and whether its six
// surface images are set in palace.db and present on disk, plus rehearsal bests,
// the daily streak, trophies, and FSRS review forecasts ("Review next"). Opened with M.
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
    private Button _copyAnki = null!;
    private string _databasePath = "";
    private Button _close = null!;
    private Label _hint = null!;
    private Label _stats = null!;
    private Button _trophies = null!;
    private bool _showingTrophies;
    private Button _reviewNext = null!;
    private bool _showingReviewNext;
    private IReadOnlyDictionary<long, RoomForecast> _forecasts = new Dictionary<long, RoomForecast>();
    private string? _forecastError;
    private DateOnly _today;
    private IReadOnlyList<RehearsalRun> _runs = [];
    private IReadOnlyDictionary<long, RoomProgress> _progress = new Dictionary<long, RoomProgress>();
    private IReadOnlyDictionary<long, RoomLearning> _learning = new Dictionary<long, RoomLearning>();
    private IReadOnlyDictionary<long, DateTimeOffset> _learningStarts = new Dictionary<long, DateTimeOffset>();
    private IReadOnlyList<PalaceSummary> _catalog = [];
    private long? _currentRoomId;
    public event Action<long>? RoomChosen;
    public event Action? CloseRequested;
    public event Action? KeyBindingsRequested;
    public bool IsOpen => _root.Visible;
    // False when there is nothing to return to (no Room loaded and no error to show).
    public bool CanClose => !_close.Disabled;
    public IReadOnlyList<PalaceSummary> Catalog => _catalog;
    public IReadOnlyList<RehearsalRun> Runs => _runs;
    public string DetailsText => _details.GetParsedText();
    public string StatsText => _stats.Text;
    public string HintText => _hint.Text;
    // The last command put on the clipboard; verification reads this because headless runs have no clipboard.
    public string? CopiedCommand { get; private set; }
    public IReadOnlyDictionary<long, RoomForecast> Forecasts => _forecasts;

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
            Columns = 4, HideRoot = true, ColumnTitlesVisible = true,
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
        _palaces.SetColumnTitle(3, "Due");
        _palaces.SetColumnExpand(3, false);
        _palaces.SetColumnCustomMinimumWidth(3, 60);
        _palaces.ItemSelected += () => ShowPalace(_palaces.GetSelected()?.GetMetadata(0).AsInt32() ?? -1);
        palaceColumn.AddChild(_palaces);

        var roomColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddChild(roomColumn);
        _roomsTitle = ViewerHud.Text("ROOMS", 12, ViewerHud.Muted);
        roomColumn.AddChild(_roomsTitle);
        _rooms = new Tree
        {
            Columns = 5, HideRoot = true, ColumnTitlesVisible = true,
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
        _rooms.SetColumnTitle(4, "Recall");
        _rooms.SetColumnExpand(4, false);
        _rooms.SetColumnCustomMinimumWidth(4, 140);
        _rooms.ItemSelected += ShowSelectedRoom;
        _rooms.ItemActivated += LoadSelected;
        roomColumn.AddChild(_rooms);

        _details = ViewerHud.RichText(14);
        _details.BbcodeEnabled = true;
        _details.CustomMinimumSize = new(0, 215);
        _details.MetaClicked += meta => { if (long.TryParse(meta.AsString(), out var id)) SelectRoom(id); };
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
            if (on) { _showingReviewNext = false; _reviewNext.SetPressedNoSignal(false); ShowTrophies(); }
            else ShowSelectedRoom();
        };
        _reviewNext = new Button { Text = "Review next", CustomMinimumSize = new(130, 38), ToggleMode = true };
        _reviewNext.Toggled += on =>
        {
            _showingReviewNext = on;
            if (on) { _showingTrophies = false; _trophies.SetPressedNoSignal(false); ShowReviewNext(); }
            else ShowSelectedRoom();
        };
        buttons.AddChild(_reviewNext);
        buttons.AddChild(_trophies);
        _copyAnki = new Button { Text = "Copy Anki command", CustomMinimumSize = new(170, 38), Disabled = true,
            TooltipText = "Copy the terminal command that builds this Room's grouped Anki cards" };
        _copyAnki.Pressed += CopyAnkiCommand;
        buttons.AddChild(_copyAnki);
        var keys = new Button { Text = "Keys", CustomMinimumSize = new(100, 38) };
        keys.Pressed += () => KeyBindingsRequested?.Invoke();
        buttons.AddChild(keys);
        _close = new Button { Text = "Close", CustomMinimumSize = new(110, 38) };
        _close.Pressed += () => CloseRequested?.Invoke();
        buttons.AddChild(_close);
        _load = new Button { Text = "Load Room", CustomMinimumSize = new(140, 38), Disabled = true };
        _load.Pressed += LoadSelected;
        buttons.AddChild(_load);
    }

    // today is when FSRS forecasts are made; verification moves it forward to see recall decay.
    public void Open(string databasePath, long? currentRoomId, bool canClose, string? progressDatabasePath = null, DateOnly? today = null)
    {
        _today = today ?? DateOnly.FromDateTime(DateTime.Now);
        _showingTrophies = _showingReviewNext = false;
        _trophies.SetPressedNoSignal(false);
        _reviewNext.SetPressedNoSignal(false);
        LoadProgress(progressDatabasePath ?? databasePath);
        _currentRoomId = currentRoomId;
        _close.Disabled = !canClose;
        _hint.Text = "Double-click or Enter loads a Room" + (canClose ? $"   ·   {Keys.Label(KeyAction.PalaceMenu)} or Esc closes" : "") +
            $"   ·   {Keys.Label(KeyAction.KeyBindings)} Keys";
        _database.Text = databasePath;
        _databasePath = databasePath;
        _copyAnki.Disabled = true;
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
        LoadLearning();
        LoadForecasts();
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
            var due = palace.Rooms.Count(r => Forecast(r.Id)?.Due == true);
            item.SetText(3, due > 0 ? due.ToString() : "—");
            item.SetTextAlignment(3, HorizontalAlignment.Center);
            item.SetCustomColor(3, due > 0 ? Missing : ViewerHud.Muted);
            if (i == start) startItem = item;
        }
        startItem?.Select(0);
        _rooms.GrabFocus();
    }

    public void Close() => _root.Visible = false;

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        // The key bindings menu sits on top and owns the keyboard while open.
        if (!IsOpen || GetParent<RoomViewer>().KeysMenu.IsOpen || @event is not InputEventKey { Pressed: true, Echo: false } key) return;
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
            var forecast = Forecast(room.Id);
            item.SetText(4, forecast switch
            {
                null => _forecastError is null ? "" : "unavailable",
                { Rehearsed: false } => "new",
                { Due: true } => $"{forecast.WeakPositions.Count} weak · {Percent(forecast.AverageRecall)}",
                _ => $"ok · {Percent(forecast.AverageRecall)}"
            });
            item.SetCustomColor(4, forecast is { Due: true } ? Missing : forecast is { Rehearsed: true } ? Found : ViewerHud.Muted);
            if (room.Id == _currentRoomId) current = item;
        }
        if (palace.Rooms.Count == 0) _details.Text = "This Palace has no Rooms yet.";
        var select = current ?? root.GetFirstChild();
        if (select is null) return;
        select.Select(0);
        _rooms.ScrollToItem(select);
    }

    private AnkiCardCommand AnkiCommand(RoomSummary room) =>
        AnkiCards.For(_catalog.First(p => p.Rooms.Contains(room)), room, _databasePath);

    private string AnkiSummary(RoomSummary room)
    {
        var anki = AnkiCommand(room);
        if (!anki.Ready) return $"[color=#93a7ac]ANKI CARDS[/color]  [color=#{Missing.ToHtml(false)}]Not ready: {Escape(anki.Status)}[/color]\n";
        return $"[color=#93a7ac]ANKI CARDS[/color]  [color=#{Found.ToHtml(false)}]Ready[/color], room image found. " +
            "Run with Anki open; rerunning is safe (it skips cards it already made).\n" +
            $"[color=#f1d39b]{Escape(anki.Command!)}[/color]\n" +
            (anki.ScriptFound ? "" : $"[color=#{Missing.ToHtml(false)}]The card script was not found at {Escape(AnkiCards.DefaultScript)}[/color]\n");
    }

    // The command is already shown in the details; this puts it on the clipboard.
    private void CopyAnkiCommand()
    {
        if (SelectedRoom() is not { } room || AnkiCommand(room) is not { Ready: true, Command: { } command }) return;
        DisplayServer.ClipboardSet(command);
        CopiedCommand = command;
        _hint.Text = $"Copied the Anki card command for Room {room.Id}. Paste it into a terminal with Anki open.";
    }

    public void PressCopyAnkiCommand() => _copyAnki.EmitSignal(BaseButton.SignalName.Pressed);
    public bool CanCopyAnkiCommand => !_copyAnki.Disabled;

    private RoomSummary? SelectedRoom()
    {
        var id = _rooms.GetSelected()?.GetMetadata(0).AsInt64();
        return id is null ? null : _catalog.SelectMany(p => p.Rooms).FirstOrDefault(r => r.Id == id);
    }

    private void ShowSelectedRoom()
    {
        var room = SelectedRoom();
        _load.Disabled = room is null;
        _copyAnki.Disabled = room is null || !AnkiCommand(room).Ready;
        if (room is null || _showingTrophies || _showingReviewNext) return;
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
        _details.Text = $"{header}\n{ProgressSummary(room)}{LearningSummary(room)}{ForecastSummary(room)}{AnkiSummary(room)}[table=3]{string.Concat(cells)}[/table]";
    }

    // Position → Loci.Id for every Room in the catalog.
    private Dictionary<long, IReadOnlyDictionary<int, long>> RoomLoci() =>
        _catalog.SelectMany(p => p.Rooms).GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.First().LocusIds);

    private RoomProgress? Progress(long roomId) => _progress.GetValueOrDefault(roomId);
    private RoomForecast? Forecast(long roomId) => _forecasts.GetValueOrDefault(roomId);
    private static string Percent(double value) => (value * 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";

    // FSRS needs the Rust scheduler; if it cannot load, the menu still works without forecasts.
    private void LoadForecasts()
    {
        try
        {
            _forecasts = ReviewPlanner.Forecast(_runs, RoomLoci(), _today);
            _forecastError = null;
        }
        catch (ViewerException ex)
        {
            _forecasts = new Dictionary<long, RoomForecast>();
            _forecastError = ex.Message;
            GD.PrintErr(ex.Message);
        }
        var due = ReviewPlanner.Ranked(_forecasts.Values).Count;
        if (_forecastError is not null) _stats.Text += "   ·   review forecasts unavailable";
        else if (_runs.Count > 0) _stats.Text += due == 0 ? "   ·   nothing due" : $"   ·   {Plural(due, "Room")} due";
    }

    private string ForecastSummary(RoomSummary room)
    {
        if (Forecast(room.Id) is not { Rehearsed: true } forecast) return "";
        var weak = forecast.WeakPositions;
        return $"[color=#93a7ac]RECALL[/color]  {Percent(forecast.AverageRecall)} average today   ·   " +
            (weak.Count == 0 ? "every Locus at 90% or better\n"
                : $"[color=#{Missing.ToHtml(false)}]{weak.Count} of {forecast.LociCount} below 90%[/color]: {LoopDrill.Describe(weak)}   " +
                  $"[color=#93a7ac](in the Room press {Keys.Label(KeyAction.Loop)}, then TAB, to drill them)[/color]\n");
    }

    // Rooms ranked by FSRS: most likely-forgotten Loci first. Titles link to the Room.
    private void ShowReviewNext()
    {
        if (_forecastError is { } error)
        {
            _details.Text = $"[b]Review next[/b]\n[color=#{Missing.ToHtml(false)}]{Escape(error)}[/color]";
            return;
        }
        var ranked = ReviewPlanner.Ranked(_forecasts.Values);
        var fresh = _forecasts.Values.Count(f => !f.Rehearsed && f.LociCount > 0);
        var header = $"[b]Review next[/b]   [color=#93a7ac]FSRS forecast for {_today:MMM d}, aiming for 90% recall · " +
            $"{Plural(ranked.Count, "Room")} due · {fresh} never rehearsed[/color]\n";
        if (ranked.Count == 0)
        {
            _details.Text = header + (_runs.Count == 0 ? "Rehearse a Room (R) to start forecasting." : "Nothing is due. Every rehearsed Locus is at 90% recall or better.");
            return;
        }
        var rooms = _catalog.SelectMany(p => p.Rooms.Select(r => (Palace: p, Room: r))).GroupBy(x => x.Room.Id).ToDictionary(g => g.Key, g => g.First());
        var cells = ranked.Take(12).Select((f, i) =>
        {
            var (palace, room) = rooms[f.RoomId];
            var days = _today.DayNumber - f.LastRehearsed!.Value.DayNumber;
            return $"[cell padding=0,2,14,2][color=#93a7ac]{i + 1}.[/color][/cell]" +
                $"[cell padding=0,2,20,2][url={f.RoomId}]{Escape(palace.Name)} › {f.RoomId:00} {Escape(room.Title)}[/url][/cell]" +
                $"[cell padding=0,2,20,2][color=#{Missing.ToHtml(false)}]≈{f.ExpectedForgotten.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} likely forgotten[/color][/cell]" +
                $"[cell][color=#93a7ac]{f.WeakPositions.Count} weak · last {(days == 0 ? "today" : days == 1 ? "yesterday" : $"{days} days ago")}[/color][/cell]";
        });
        _details.Text = header + $"[table=4]{string.Concat(cells)}[/table]";
    }

    // Selects a Room from a Review next link and shows its details.
    public void SelectRoom(long roomId)
    {
        var palaceIndex = _catalog.ToList().FindIndex(p => p.Rooms.Any(r => r.Id == roomId));
        if (palaceIndex < 0) return;
        _showingReviewNext = _showingTrophies = false;
        _reviewNext.SetPressedNoSignal(false);
        _trophies.SetPressedNoSignal(false);
        for (var item = _palaces.GetRoot()?.GetFirstChild(); item is not null; item = item.GetNext())
            if (item.GetMetadata(0).AsInt32() == palaceIndex) { item.Select(0); break; }
        for (var item = _rooms.GetRoot()?.GetFirstChild(); item is not null; item = item.GetNext())
            if (item.GetMetadata(0).AsInt64() == roomId) { item.Select(0); _rooms.ScrollToItem(item); break; }
        // Selecting an already-selected row emits no signal, so refresh the details directly.
        ShowSelectedRoom();
    }

    public void ToggleReviewNext(bool on) => _reviewNext.ButtonPressed = on;

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
        try { _learningStarts = new RehearsalStore().LoadFirstLoopDrills(progressDatabasePath); }
        catch (Exception ex)
        {
            _learningStarts = new Dictionary<long, DateTimeOffset>();
            GD.PrintErr(ex.Message);
        }
        _learning = new Dictionary<long, RoomLearning>();
        var streak = RehearsalStreak.Days(_runs.Select(r => r.Day), DateOnly.FromDateTime(DateTime.Now));
        var trophies = Achievements.Unlocked(_runs).Count;
        var medals = _progress.Values.GroupBy(p => p.BestMedal).Where(g => g.Key >= Medal.Gold).OrderByDescending(g => g.Key)
            .Select(g => $"{g.Count()} {g.Key}");
        _stats.Text = _runs.Count == 0 ? "No rehearsals yet. Load a Room and press R to set your first time."
            : $"Day {streak} streak   ·   {Plural(_runs.Count, "rehearsal")}   ·   {trophies} of {Achievements.All.Count} trophies" +
              string.Concat(medals.Select(m => "   ·   " + m));
    }

    // Learning needs each Room's current Loci, so it waits for the catalog.
    private void LoadLearning()
    {
        _learning = RoomLearning.ByRoom(_learningStarts, _runs, RoomLoci());
        var learned = _learning.Values.Count(l => l.Learned);
        if (learned > 0) _stats.Text += $"   ·   {Plural(learned, "Room")} learned";
    }

    // From the Room's first loop drill to its first flawless rehearsal of all its Loci.
    private string LearningSummary(RoomSummary room)
    {
        if (_learning.GetValueOrDefault(room.Id) is not { } learning) return "";
        if (learning.LearnedBy is { } run)
            return learning.Duration is { } took
                ? $"[color=#93a7ac]LEARNED[/color]  in [b]{RoomLearning.Format(took)}[/b]   ·   first loop drill {learning.StartedAt!.Value.LocalDateTime:MMM d}, " +
                  $"flawless through every Locus on {run.CompletedAt.LocalDateTime:MMM d}\n"
                : $"[color=#93a7ac]LEARNED[/color]  flawless through every Locus on {run.CompletedAt.LocalDateTime:MMM d}   ·   " +
                  "[color=#93a7ac]untimed: no loop drill was recorded before it[/color]\n";
        var started = learning.StartedAt!.Value;
        return $"[color=#93a7ac]LEARNING[/color]  {RoomLearning.Format(DateTimeOffset.Now - started)} so far, since the first loop drill on {started.LocalDateTime:MMM d}   ·   " +
            "[color=#93a7ac]learned at the first flawless rehearsal of all its Loci[/color]\n";
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
