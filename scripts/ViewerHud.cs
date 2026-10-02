using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

public partial class ViewerHud : CanvasLayer
{
    internal static readonly Color Ink = new("e9edea");
    internal static readonly Color Muted = new("93a7ac");
    internal static readonly Color Accent = new("88d8c4");
    private Control _root = null!;
    private Label _title = null!;
    private Label _subtitle = null!;
    private Label _state = null!;
    private Label _focusHint = null!;
    private Label _crosshair = null!;
    private Label _pauseHint = null!;
    private Label _controls = null!;
    private Label _controlsMore = null!;
    private PanelContainer _readingPanel = null!;
    private Label _readingTitle = null!;
    private RichTextLabel _readingText = null!;
    private PanelContainer _errorPanel = null!;
    private Label _errorTitle = null!;
    private RichTextLabel _errorText = null!;
    private PanelContainer _warningsPanel = null!;
    private Label _warningsTitle = null!;
    private RichTextLabel _warningsText = null!;
    private PanelContainer _rehearsalPanel = null!;
    private Label _rehearsalTitle = null!;
    private Label _rehearsalTarget = null!;
    private RichTextLabel _rehearsalLive = null!;
    private Label _rehearsalPrompt = null!;
    private PanelContainer _drillPrompt = null!;
    private LineEdit _drillRange = null!;
    private Label _drillError = null!;
    private Label _drillWeak = null!;
    private string? _weakRange;
    private CpuParticles2D _confetti = null!;
    private long? _readingId;
    private bool _hasError;
    private bool _roomLoaded;
    public RoomMap Map { get; private set; } = null!;
    public event Action? ChooseRoomRequested;
    public event Action<string>? DrillRangeSubmitted;
    public event Action? DrillPromptCancelled;
    public bool DrillPromptVisible => _drillPrompt.Visible;
    public string DrillPromptError => _drillError.Text;
    public string DrillPromptRange => _drillRange.Text;
    public string DrillWeakText => _drillWeak.Text;
    public bool HasError => _hasError;
    public string ErrorText => _errorText.Text;
    public string WarningText => _warningsText.Text;
    public bool ReadingVisible => _readingPanel.Visible;
    public bool RehearsalVisible => _rehearsalPanel.Visible;
    public string ControlsText => _controls.Text + "\n" + _controlsMore.Text;
    public string RehearsalText => string.Join("\n", _rehearsalTitle.Text, _rehearsalTarget.Text, _rehearsalLive.GetParsedText(), _rehearsalPrompt.Text);

    public override void _Ready()
    {
        _root = new Control { Name = "Interface", MouseFilter = Control.MouseFilterEnum.Ignore };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        var heading = Panel(24, 24, 740, 108);
        var headingBox = Column(heading);
        var eyebrow = Text("P A L A C E   /   R O O M   V I E W E R", 12, Accent);
        headingBox.AddChild(eyebrow);
        _title = Text("Your memory, placed in space.", 25, Ink);
        _title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        headingBox.AddChild(_title);
        _subtitle = Text("26 Positions · One Room", 13, Muted);
        headingBox.AddChild(_subtitle);

        var statePanel = Panel(-338, 24, -24, 108, true);
        var stateBox = Column(statePanel);
        stateBox.AddChild(Text("DISPLAY", 12, Muted));
        _state = Text("J   Text hidden     K   Markers visible", 15, Accent);
        stateBox.AddChild(_state);
        _focusHint = Text("L   Aim at a Position", 13, Muted);
        stateBox.AddChild(_focusHint);

        _pauseHint = Text("", 15, Ink);
        _root.AddChild(_pauseHint);
        _pauseHint.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _pauseHint.OffsetLeft = -250; _pauseHint.OffsetRight = 250;
        _pauseHint.OffsetTop = 154; _pauseHint.OffsetBottom = 186;
        _pauseHint.HorizontalAlignment = HorizontalAlignment.Center;

        _crosshair = Text("+", 21, new Color(1, 1, 1, .55f));
        _root.AddChild(_crosshair);
        _crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _crosshair.OffsetLeft = -12; _crosshair.OffsetRight = 12;
        _crosshair.OffsetTop = -15; _crosshair.OffsetBottom = 15;
        _crosshair.HorizontalAlignment = HorizontalAlignment.Center;

        var controls = Panel(24, -85, 790, -24, false, true);
        var controlsBox = Column(controls);
        _controls = Text("", 14, Ink);
        controlsBox.AddChild(_controls);
        _controlsMore = Text("", 12, Muted);
        controlsBox.AddChild(_controlsMore);
        RefreshKeys();

        _readingPanel = Panel(24, -306, 630, -103, false, true);
        _readingPanel.Visible = false;
        var readerBox = Column(_readingPanel);
        _readingTitle = Text("", 13, Accent);
        readerBox.AddChild(_readingTitle);
        _readingText = RichText(19);
        _readingText.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _readingText.CustomMinimumSize = new(0, 135);
        readerBox.AddChild(_readingText);

        _rehearsalPanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _rehearsalPanel.AddThemeStyleboxOverride("panel", Surface());
        _root.AddChild(_rehearsalPanel);
        _rehearsalPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        // Grows downward to fit the result summary.
        _rehearsalPanel.OffsetLeft = -330; _rehearsalPanel.OffsetRight = 330;
        _rehearsalPanel.OffsetTop = 126; _rehearsalPanel.OffsetBottom = 126;
        var rehearsalBox = Column(_rehearsalPanel);
        _rehearsalTitle = Text("", 12, Accent);
        rehearsalBox.AddChild(_rehearsalTitle);
        _rehearsalTarget = Text("", 24, Ink);
        rehearsalBox.AddChild(_rehearsalTarget);
        _rehearsalLive = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _rehearsalLive.AddThemeFontSizeOverride("normal_font_size", 14);
        _rehearsalLive.AddThemeFontSizeOverride("bold_font_size", 14);
        _rehearsalLive.AddThemeColorOverride("default_color", Ink);
        rehearsalBox.AddChild(_rehearsalLive);
        _rehearsalPrompt = Text("", 16, new Color("f1d39b"));
        rehearsalBox.AddChild(_rehearsalPrompt);

        _drillPrompt = new PanelContainer { Visible = false };
        _drillPrompt.AddThemeStyleboxOverride("panel", Surface());
        _root.AddChild(_drillPrompt);
        _drillPrompt.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
        _drillPrompt.OffsetLeft = -330; _drillPrompt.OffsetRight = 330;
        _drillPrompt.OffsetTop = 126; _drillPrompt.OffsetBottom = 126;
        var drillBox = Column(_drillPrompt);
        drillBox.AddChild(Text("L O O P   /   C H O O S E   P O S I T I O N S", 12, Accent));
        _drillRange = new LineEdit { PlaceholderText = "1-3", CustomMinimumSize = new(0, 40), KeepEditingOnTextSubmit = true };
        _drillRange.AddThemeFontSizeOverride("font_size", 20);
        _drillRange.TextSubmitted += text => DrillRangeSubmitted?.Invoke(text);
        _drillRange.GuiInput += @event =>
        {
            if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
            if (key.Keycode == Key.Tab || key.PhysicalKeycode == Key.Tab)
            {
                _drillRange.AcceptEvent();
                FillWeakSpots();
            }
            else if (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape)
            {
                _drillRange.AcceptEvent();
                DrillPromptCancelled?.Invoke();
            }
        };
        drillBox.AddChild(_drillRange);
        drillBox.AddChild(Text("e.g.  1-3   or   1-3, 7, 10-12        ENTER   Start loop        ESC   Cancel", 13, Muted));
        _drillWeak = Text("", 13, new Color("f1d39b"));
        drillBox.AddChild(_drillWeak);
        _drillError = Text("", 13, new Color("edbf7f"));
        drillBox.AddChild(_drillError);

        _confetti = new CpuParticles2D
        {
            Emitting = false, OneShot = true, Amount = 180, Lifetime = 2.4, Explosiveness = .95f,
            Direction = new(0, -1), Spread = 80, InitialVelocityMin = 380, InitialVelocityMax = 820,
            Gravity = new(0, 900), ScaleAmountMin = 5, ScaleAmountMax = 10,
            ColorInitialRamp = new Gradient
            {
                InterpolationMode = Gradient.InterpolationModeEnum.Constant,
                Offsets = [0, .2f, .4f, .6f, .8f],
                Colors = [new("f1c55b"), new("88d8c4"), new("ee8a6b"), new("bfe6ff"), new("f2eee4")]
            }
        };
        AddChild(_confetti);

        _warningsPanel = Panel(-394, 126, -24, 362, true);
        _warningsPanel.Visible = false;
        var warningBox = Column(_warningsPanel);
        _warningsTitle = Text("", 15, new Color("edbf7f"));
        _warningsTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        warningBox.AddChild(_warningsTitle);
        _warningsText = RichText(14);
        _warningsText.AddThemeColorOverride("default_color", new Color("dec6a6"));
        _warningsText.CustomMinimumSize = new(0, 158);
        _warningsText.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        warningBox.AddChild(_warningsText);
        warningBox.AddChild(Text("Escape releases the mouse for scrolling.", 11, Muted));

        var mapPanel = Panel(-264, -296, -24, -24, true, true);
        var mapBox = Column(mapPanel);
        mapBox.AddChild(Text("ROOM PLAN   /   TOP VIEW", 11, Muted));
        Map = new RoomMap { CustomMinimumSize = new(202, 215), MouseFilter = Control.MouseFilterEnum.Ignore };
        mapBox.AddChild(Map);

        _errorPanel = new PanelContainer { Visible = false };
        _errorPanel.AddThemeStyleboxOverride("panel", Surface());
        _root.AddChild(_errorPanel);
        _errorPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _errorPanel.OffsetLeft = -380;
        _errorPanel.OffsetRight = 380;
        _errorPanel.OffsetTop = -180;
        _errorPanel.OffsetBottom = 180;
        var errorBox = Column(_errorPanel);
        _errorTitle = Text("Room could not be loaded", 26, new Color("edbf7f"));
        errorBox.AddChild(_errorTitle);
        _errorText = RichText(18);
        _errorText.CustomMinimumSize = new(0, 190);
        _errorText.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        errorBox.AddChild(_errorText);
        var errorButtons = new HBoxContainer();
        errorButtons.AddThemeConstantOverride("separation", 10);
        errorBox.AddChild(errorButtons);
        var choose = new Button { Text = "Choose a Room", CustomMinimumSize = new(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        choose.Pressed += () => ChooseRoomRequested?.Invoke();
        errorButtons.AddChild(choose);
        var quit = new Button { Text = "Close viewer", CustomMinimumSize = new(0, 40), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        quit.Pressed += () => GetTree().Quit();
        errorButtons.AddChild(quit);
    }

    // Rewrites the key hints after the bindings change.
    public void RefreshKeys()
    {
        var walk = new[] { KeyAction.WalkForward, KeyAction.WalkLeft, KeyAction.WalkBack, KeyAction.WalkRight }
            .Select(a => Keys.Current.Keys(a).Select(Keys.Name).FirstOrDefault() ?? "—").ToArray();
        _controls.Text = $"{(walk.All(k => k.Length == 1) ? string.Concat(walk) : string.Join(" ", walk))}   Walk    MOUSE   Look    " +
            $"{K(KeyAction.AllText)}   All text    {K(KeyAction.ThisText)}   This text    {K(KeyAction.Markers)}   Markers    " +
            $"{K(KeyAction.Rehearse)}   Rehearse    {K(KeyAction.Loop)}   Loop";
        _controlsMore.Text = "LEFT CLICK   This text    RIGHT CLICK   All text    ESC   Release mouse    " +
            $"{K(KeyAction.PalaceMenu)}   Palaces    {K(KeyAction.Sound)}   Sound    {K(KeyAction.KeyBindings)}   Keys";
    }

    private static string K(KeyAction action) => Keys.Label(action);

    public void ShowRoom(RoomSnapshot room, IReadOnlyList<string>? textureWarnings = null)
    {
        _roomLoaded = true;
        _title.Text = room.Title;
        _subtitle.Text = $"ROOM {room.Id:00}   /   {room.Loci.Count} of 26 Positions populated" + (room.Loci.Count == 0 ? "   /   Empty Room" : "");
        textureWarnings ??= [];
        _warningsPanel.Visible = room.Warnings.Count > 0 || textureWarnings.Count > 0;
        var summaries = new List<string>();
        if (room.Warnings.Count > 0) summaries.Add($"{room.Warnings.Count} {(room.Warnings.Count == 1 ? "Locus" : "Loci")} skipped");
        if (textureWarnings.Count > 0) summaries.Add($"{textureWarnings.Count} image {(textureWarnings.Count == 1 ? "warning" : "warnings")}");
        _warningsTitle.Text = string.Join(" · ", summaries);
        _warningsText.Text = string.Join("\n\n", room.Warnings.Select(w => w.ToString()).Concat(textureWarnings));
    }

    public void ShowNoRoom()
    {
        _title.Text = "Select a Room to begin";
        _subtitle.Text = "Press M to choose a Palace and Room.";
    }

    // The Palace menu hides a load error while it is open; the error returns if the menu closes.
    public void SetErrorVisible(bool visible) => _errorPanel.Visible = visible && _hasError;

    public void ShowError(string message)
    {
        _errorText.Text = message;
        _hasError = true;
        _errorPanel.Visible = true;
        _title.Text = "Select a Room to begin";
        _subtitle.Text = "Choose a Room, or pass a Room ID when starting the viewer.";
        _readingPanel.Visible = false;
    }

    public void UpdateState(int visibleCount, int totalCount, bool markersVisible, bool captured, LocusDisplay? focus)
    {
        var textState = visibleCount == 0 ? "hidden" : visibleCount == totalCount ? "visible" : $"{visibleCount}/{totalCount}";
        _state.Text = $"{K(KeyAction.AllText)}   Text {textState}     {K(KeyAction.Markers)}   Markers {(markersVisible ? "visible" : "hidden")}";
        _focusHint.Text = focus?.Locus is not null
            ? $"{K(KeyAction.ThisText)}   {(focus.Billboard.Visible ? "Hide" : "Show")} text · Position {focus.PositionNumber:00}"
            : focus is not null ? $"Position {focus.PositionNumber:00} · Empty" : $"{K(KeyAction.ThisText)}   Aim at a Position";
        _crosshair.Modulate = focus?.Locus is not null ? Accent : Colors.White;
        _pauseHint.Text = captured || HasError ? "" : _roomLoaded ? "Mouse released · Click the Room to continue" : "Press M to choose a Room";
    }

    // Called every frame while rehearsing; labels only change when their text does.
    // feedback is BBCode for the last answer, e.g. "+180   −1.3 s".
    public void ShowRehearsal(RehearsalSession session, long elapsedMs, RehearsalRun? best, string feedback)
    {
        if (session.Current is not { } position) return;
        _rehearsalPanel.Visible = true;
        Set(_rehearsalTitle, session.Round == 1 ? "R E H E A R S E   /   R O U N D   1   ·   W H O L E   R O O M"
            : $"R E H E A R S E   /   R O U N D   {session.Round}   ·   M I S S E S   O N L Y");
        Set(_rehearsalTarget, $"Position {position:00}   ·   {RoomLayout.Description(position)}");
        _rehearsalTarget.AddThemeColorOverride("font_color", Ink);
        var bestText = best is null ? "[color=#93a7ac]first clear sets the target[/color]" : $"[color=#93a7ac]BEST[/color]  {RehearsalScoring.FormatTime(best.DurationMs)}";
        var combo = session.Round == 1 && session.Combo >= 2 ? $"      [color=#f1d39b]COMBO ×{session.Combo}[/color]" : "";
        SetRich(_rehearsalLive,
            $"[color=#93a7ac]TIME[/color]  [b]{RehearsalScoring.FormatTime(elapsedMs)}[/b]      {bestText}      " +
            $"[color=#93a7ac]SCORE[/color]  {session.Score:N0}{combo}\n" +
            $"[color=#93a7ac]{session.IndexInRound + 1} of {session.RoundPositions.Count}   ·   {session.RoundMisses.Count} missed this round[/color]      {feedback}");
        Set(_rehearsalPrompt, session.Revealed ? GradePrompt($"{K(KeyAction.Sound)}   Sound") : RevealPrompt($"{K(KeyAction.Quit)}   Quit"));
    }

    public void ShowRehearsalResult(RehearsalOutcome outcome)
    {
        var run = outcome.Run;
        _rehearsalPanel.Visible = true;
        Set(_rehearsalTitle, "R E H E A R S E   /   R O O M   C L E A R");
        Set(_rehearsalTarget, $"{run.Medal.ToString().ToUpperInvariant()}   ·   {RehearsalScoring.FormatTime(run.DurationMs)}");
        _rehearsalTarget.AddThemeColorOverride("font_color", MedalColor(run.Medal));
        var lines = new List<string>();
        if (outcome.FirstClear) lines.Add("[color=#f1d39b]FIRST CLEAR[/color]   This is your time to beat.");
        else if (outcome.NewBestTime)
            lines.Add($"[color=#f1d39b][b]NEW PERSONAL BEST[/b][/color]   [color=#88d8c4]{RehearsalScoring.FormatDelta(run.DurationMs - outcome.PreviousBestMs!.Value)}[/color]   (was {RehearsalScoring.FormatTime(outcome.PreviousBestMs.Value)})");
        else
            lines.Add($"Best {RehearsalScoring.FormatTime(outcome.PreviousBestMs!.Value)}   [color=#ee8a6b]{RehearsalScoring.FormatDelta(run.DurationMs - outcome.PreviousBestMs.Value)}[/color]   So close. Go again?");
        lines.Add($"{run.FirstPassKnown} of {run.LociCount} on the first pass   ·   " +
            (run.Rounds == 1 ? "flawless" : $"{run.Rounds} rounds") + $"   ·   best combo ×{run.BestCombo}" +
            (outcome.NewBestCombo ? "  [color=#f1d39b]NEW[/color]" : ""));
        if (!run.Perfect) lines.Add("[color=#93a7ac]First-pass misses: " + string.Join(", ", run.MissesByRound[0].Select(p => p.ToString("00"))) + "[/color]");
        lines.Add($"Score  [b]{run.Score:N0}[/b]" + (outcome.NewHighScore ? "   [color=#f1d39b]NEW HIGH SCORE[/color]" : "") +
            $"      [color=#f1d39b]Day {outcome.StreakDays} streak[/color]");
        if (outcome.Unlocked.Count > 0)
            lines.Add("[color=#f1d39b]UNLOCKED[/color]   " + string.Join("   ·   ", outcome.Unlocked.Select(a => $"[b]{a.Name}[/b]")));
        if (outcome.SaveError is { } error) lines.Add($"[color=#edbf7f]Not saved: {error.Replace("[", "[lb]")}[/color]");
        SetRich(_rehearsalLive, string.Join("\n", lines));
        Set(_rehearsalPrompt, $"{K(KeyAction.Reveal)}   Rehearse again        {K(KeyAction.Quit)}   Done        {K(KeyAction.PalaceMenu)}   Palaces");
    }

    private static string GradePrompt(string extra) => $"{K(KeyAction.Knew)}   Knew it        {K(KeyAction.Missed)}   Missed        {extra}";
    private static string RevealPrompt(string extra) => $"Recall it, then   {K(KeyAction.Reveal)}   Reveal        {extra}";

    public void HideRehearsal() => _rehearsalPanel.Visible = false;

    public void ShowDrillPrompt(string range, string error = "")
    {
        _rehearsalPanel.Visible = false;
        _drillPrompt.Visible = true;
        _drillError.Text = error;
        _drillError.Visible = error.Length > 0;
        if (_drillRange.Text != range) _drillRange.Text = range;
        _drillRange.GrabFocus();
        _drillRange.Edit();
        _drillRange.SelectAll();
        _drillRange.CaretColumn = range.Length;
    }

    // FSRS weak spots for the loaded Room: null when it has never been rehearsed.
    public void SetWeakSpots(IReadOnlyList<int>? weak, string? error = null)
    {
        _weakRange = weak is { Count: > 0 } ? LoopDrill.Describe(weak) : null;
        _drillWeak.Text = error is not null ? $"Weak spots unavailable: {error}"
            : weak is null ? "Weak spots: rehearse this Room first and FSRS will find them."
            : weak.Count == 0 ? "Weak spots: none. Every Locus is at 90% recall or better."
            : $"Weak spots (below 90% recall): {_weakRange}        TAB   Use them";
    }

    // Puts the weak spots into the range field; Enter then starts the loop.
    public void FillWeakSpots()
    {
        if (_weakRange is null) return;
        _drillRange.Text = _weakRange;
        _drillRange.CaretColumn = _weakRange.Length;
    }

    public void HideDrillPrompt()
    {
        _drillPrompt.Visible = false;
        _drillRange.ReleaseFocus();
    }

    // Loop drill shares the rehearsal panel. feedback is BBCode for the last answer.
    public void ShowDrill(LoopDrill drill, string feedback)
    {
        _rehearsalPanel.Visible = true;
        Set(_rehearsalTitle, $"L O O P   /   P O S I T I O N S   {LoopDrill.Describe(drill.Positions)}   ·   L A P   {drill.Lap}");
        Set(_rehearsalTarget, $"Position {drill.Current:00}   ·   {RoomLayout.Description(drill.Current)}");
        _rehearsalTarget.AddThemeColorOverride("font_color", Ink);
        var previous = drill.PreviousLapKnown is { } known ? $"      [color=#93a7ac]LAST LAP[/color]  {known}/{drill.Positions.Count}" : "";
        var streak = drill.Streak >= 2 ? $"      [color=#f1d39b]STREAK ×{drill.Streak}[/color]" : "";
        SetRich(_rehearsalLive,
            $"[color=#93a7ac]THIS LAP[/color]  {drill.LapKnown} known · {drill.LapMissed} missed{previous}{streak}\n" +
            $"[color=#93a7ac]{drill.IndexInLap + 1} of {drill.Positions.Count}   ·   {drill.TotalKnown} of {drill.TotalGraded} known overall[/color]      {feedback}");
        Set(_rehearsalPrompt, drill.Revealed ? GradePrompt($"{K(KeyAction.Quit)}   Stop")
            : RevealPrompt($"{K(KeyAction.Loop)}   New range        {K(KeyAction.Quit)}   Stop"));
    }

    // Confetti burst and a warm flash of the rehearsal panel.
    public void Celebrate()
    {
        _confetti.Position = new(GetViewport().GetVisibleRect().Size.X / 2, 190);
        _confetti.Restart();
        _confetti.Emitting = true;
        CreateTween().TweenProperty(_rehearsalPanel, "modulate", Colors.White, .9).From(new Color(1.7f, 1.45f, .85f));
    }

    public static Color MedalColor(Medal medal) => medal switch
    {
        Medal.Platinum => new Color("bfe6ff"),
        Medal.Gold => new Color("f1c55b"),
        Medal.Silver => new Color("c9d3d6"),
        Medal.Bronze => new Color("d39a6a"),
        _ => Muted
    };

    private static void Set(Label label, string text) { if (label.Text != text) label.Text = text; }
    private static void SetRich(RichTextLabel label, string text) { if (label.Text != text) label.Text = text; }

    public void ShowReading(LocusDisplay? display)
    {
        _readingPanel.Visible = display?.Locus is not null && !HasError;
        if (display?.Locus is not { } locus || _readingId == locus.Id) return;
        _readingId = locus.Id;
        _readingTitle.Text = $"POSITION {locus.Position:00}   /   {RoomLayout.Description(locus.Position).ToUpperInvariant()}";
        _readingText.Text = locus.Text;
        _readingText.ScrollToLine(0);
    }

    private PanelContainer Panel(float left, float top, float right, float bottom, bool anchorRight = false, bool anchorBottom = false)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", Surface());
        _root.AddChild(panel);
        panel.AnchorLeft = panel.AnchorRight = anchorRight ? 1 : 0;
        panel.AnchorTop = panel.AnchorBottom = anchorBottom ? 1 : 0;
        panel.OffsetLeft = left; panel.OffsetRight = right; panel.OffsetTop = top; panel.OffsetBottom = bottom;
        return panel;
    }

    internal static StyleBoxFlat Surface() => new()
    {
        BgColor = new Color(.035f, .065f, .085f, .94f),
        BorderColor = new Color("30434c"), BorderWidthBottom = 1, BorderWidthTop = 1,
        BorderWidthLeft = 1, BorderWidthRight = 1,
        CornerRadiusBottomLeft = 10, CornerRadiusBottomRight = 10,
        CornerRadiusTopLeft = 10, CornerRadiusTopRight = 10,
        ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 13, ContentMarginBottom = 13
    };

    internal static VBoxContainer Column(PanelContainer panel)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        panel.AddChild(column);
        return column;
    }

    internal static Label Text(string text, int size, Color color)
    {
        var label = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    internal static RichTextLabel RichText(int size)
    {
        var text = new RichTextLabel { BbcodeEnabled = false, SelectionEnabled = true, ScrollActive = true };
        text.AddThemeFontSizeOverride("normal_font_size", size);
        text.AddThemeColorOverride("default_color", Ink);
        return text;
    }
}

public partial class RoomMap : Control
{
    public Vector3 PlayerPosition { get; set; }
    public Vector3 PlayerForward { get; set; } = Vector3.Back;
    public override void _Draw()
    {
        var color = new Color("637b83");
        var accent = new Color("88d8c4");
        var origin = new Vector2(101, 102);
        Vector2 Project(float x, float z) => origin + new Vector2(x * 9.1f, z * 7.4f);
        DrawRect(new Rect2(Project(-6, -9), new(109.2f, 133.2f)), color, false, 1);
        DrawLine(Project(-6, 9), Project(6, 9), accent, 3);
        for (var position = 1; position <= 24; position += 3)
        {
            var anchor = RoomLayout.Anchor(position);
            var point = Project(anchor.X, anchor.Z);
            DrawCircle(point, 3, accent);
            var offset = new Vector2(anchor.X < 0 ? -42 : anchor.X > 0 ? 6 : -18, anchor.Z > 0 ? 19 : -9);
            DrawString(ThemeDB.FallbackFont, point + offset, $"{position}–{position + 2}", HorizontalAlignment.Left, -1, 11, color);
        }
        var player = Project(PlayerPosition.X, PlayerPosition.Z);
        var direction = new Vector2(PlayerForward.X, PlayerForward.Z).Normalized();
        DrawCircle(player, 4, new Color("f1d39b"));
        DrawLine(player, player + direction * 14, new Color("f1d39b"), 2);
        DrawString(ThemeDB.FallbackFont, new(80, 207), "FRONT", HorizontalAlignment.Left, -1, 11, accent);
        DrawString(ThemeDB.FallbackFont, new(51, 13), "25 floor · 26 ceiling", HorizontalAlignment.Left, -1, 11, color);
    }
}
