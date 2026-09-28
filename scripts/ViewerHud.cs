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
    private PanelContainer _readingPanel = null!;
    private Label _readingTitle = null!;
    private RichTextLabel _readingText = null!;
    private PanelContainer _errorPanel = null!;
    private Label _errorTitle = null!;
    private RichTextLabel _errorText = null!;
    private PanelContainer _warningsPanel = null!;
    private Label _warningsTitle = null!;
    private RichTextLabel _warningsText = null!;
    private long? _readingId;
    private bool _hasError;
    private bool _roomLoaded;
    public RoomMap Map { get; private set; } = null!;
    public event Action? ChooseRoomRequested;
    public bool HasError => _hasError;
    public string ErrorText => _errorText.Text;
    public string WarningText => _warningsText.Text;
    public bool ReadingVisible => _readingPanel.Visible;

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

        var controls = Panel(24, -85, 700, -24, false, true);
        var controlsBox = Column(controls);
        controlsBox.AddChild(Text("WASD   Walk    MOUSE   Look    J   All text    L   This text    K   Markers", 14, Ink));
        controlsBox.AddChild(Text("LEFT CLICK   This text    RIGHT CLICK   All text    ESC   Release mouse    M   Palaces", 12, Muted));

        _readingPanel = Panel(24, -306, 630, -103, false, true);
        _readingPanel.Visible = false;
        var readerBox = Column(_readingPanel);
        _readingTitle = Text("", 13, Accent);
        readerBox.AddChild(_readingTitle);
        _readingText = RichText(19);
        _readingText.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _readingText.CustomMinimumSize = new(0, 135);
        readerBox.AddChild(_readingText);

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
        _state.Text = $"J   Text {textState}     K   Markers {(markersVisible ? "visible" : "hidden")}";
        _focusHint.Text = focus?.Locus is not null
            ? $"L   {(focus.Billboard.Visible ? "Hide" : "Show")} text · Position {focus.PositionNumber:00}"
            : focus is not null ? $"Position {focus.PositionNumber:00} · Empty" : "L   Aim at a Position";
        _crosshair.Modulate = focus?.Locus is not null ? Accent : Colors.White;
        _pauseHint.Text = captured || HasError ? "" : _roomLoaded ? "Mouse released · Click the Room to continue" : "Press M to choose a Room";
    }

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
