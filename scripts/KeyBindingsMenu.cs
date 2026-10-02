using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

// Lists every action with its main and alternate key. Click a key, then press the new one:
// Delete or Backspace unbinds it, Escape cancels. Changes save immediately. Opened with F1
// or the Keys button in the Palace menu.
public partial class KeyBindingsMenu : CanvasLayer
{
    private Control _root = null!;
    private GridContainer _grid = null!;
    private Label _message = null!;
    private readonly Dictionary<KeySlot, Button> _buttons = [];
    private KeySlot? _capturing;
    public event Action? Closed;
    public event Action? BindingsChanged;
    public bool IsOpen => _root.Visible;
    public KeySlot? Capturing => _capturing;
    public string MessageText => _message.Text;

    public override void _Ready()
    {
        Layer = 3;
        _root = new Control { Name = "KeyBindingsMenu", Visible = false };
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        var backdrop = new ColorRect { Color = new Color(0, 0, 0, .55f) };
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(backdrop);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", ViewerHud.Surface());
        _root.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        panel.OffsetLeft = -360; panel.OffsetRight = 360;
        panel.OffsetTop = -380; panel.OffsetBottom = 380;
        var column = ViewerHud.Column(panel);
        column.AddThemeConstantOverride("separation", 10);
        column.AddChild(ViewerHud.Text("K E Y   B I N D I N G S", 12, ViewerHud.Accent));
        column.AddChild(ViewerHud.Text("Click a key, then press the new one.   DELETE unbinds it.   ESC cancels.", 13, ViewerHud.Muted));

        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(scroll);
        _grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 12);
        _grid.AddThemeConstantOverride("v_separation", 4);
        scroll.AddChild(_grid);
        foreach (var context in Enum.GetValues<KeyContext>())
        {
            AddHeading(context switch
            {
                KeyContext.Anywhere => "ANYWHERE",
                KeyContext.Exploring => "WHILE EXPLORING",
                _ => "IN A REHEARSAL OR LOOP DRILL"
            });
            foreach (var info in KeyBindings.Actions.Where(a => a.Context == context))
            {
                var name = ViewerHud.Text(info.Name, 15, ViewerHud.Ink);
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                _grid.AddChild(name);
                for (var slot = 0; slot < KeyBindings.Slots; slot++)
                {
                    var key = new KeySlot(info.Action, slot);
                    var button = new Button { CustomMinimumSize = new(140, 32), FocusMode = Control.FocusModeEnum.None };
                    button.Pressed += () => StartCapture(key);
                    _grid.AddChild(button);
                    _buttons[key] = button;
                }
            }
        }
        column.AddChild(ViewerHud.Text("Mouse: left-click = this text, right-click = all text.   ESC always releases the mouse.", 12, ViewerHud.Muted));
        _message = ViewerHud.Text("", 13, new Color("f1d39b"));
        _message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_message);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(buttons);
        var reset = new Button { Text = "Reset to defaults", CustomMinimumSize = new(170, 38), FocusMode = Control.FocusModeEnum.None };
        reset.Pressed += ResetDefaults;
        buttons.AddChild(reset);
        var done = new Button { Text = "Done", CustomMinimumSize = new(110, 38), FocusMode = Control.FocusModeEnum.None };
        done.Pressed += Close;
        buttons.AddChild(done);
    }

    private void AddHeading(string text)
    {
        var heading = ViewerHud.Text(text, 12, ViewerHud.Accent);
        heading.CustomMinimumSize = new(0, 30);
        heading.VerticalAlignment = VerticalAlignment.Bottom;
        _grid.AddChild(heading);
        _grid.AddChild(new Control());
        _grid.AddChild(new Control());
    }

    public void Open()
    {
        _capturing = null;
        _message.Text = Keys.FilePath.Length > 0 ? $"Saved to {Keys.FilePath}" : "";
        Refresh();
        _root.Visible = true;
    }

    public void Close()
    {
        if (!IsOpen) return;
        _capturing = null;
        _root.Visible = false;
        Closed?.Invoke();
    }

    public void StartCapture(KeySlot slot)
    {
        _capturing = slot;
        _message.Text = $"Press a key for {KeyBindings.Info(slot.Action).Name}…";
        Refresh();
    }

    // Keyboard input belongs to this menu while it is open; the mouse still reaches its buttons.
    public override void _Input(InputEvent @event)
    {
        if (!IsOpen || @event is not InputEventKey { Pressed: true, Echo: false } key) return;
        GetViewport().SetInputAsHandled();
        if (_capturing is not { } slot)
        {
            if (key.PhysicalKeycode == Key.Escape || key.Keycode == Key.Escape || Keys.Is(KeyAction.KeyBindings, key)) Close();
            return;
        }
        Capture(slot, Keys.FromEvent(key));
    }

    // Binds the pressed key to the slot being captured. Also used directly by runtime verification.
    public void Capture(KeySlot slot, long code)
    {
        _capturing = null;
        var name = KeyBindings.Info(slot.Action).Name;
        if (code == KeyBindings.Escape) _message.Text = "Cancelled.";
        else if (code is (long)Key.Delete or (long)Key.Backspace)
        {
            Keys.Current.Clear(slot.Action, slot.Slot);
            _message.Text = $"{name}: key removed.";
            Save();
        }
        else
        {
            var cleared = Keys.Current.Assign(slot.Action, slot.Slot, code);
            _message.Text = $"{name}: {Keys.Name(code)}." + (cleared.Count == 0 ? ""
                : $"   {Keys.Name(code)} was removed from " + string.Join(" and ", cleared.Select(c => KeyBindings.Info(c.Action).Name)) + ".");
            Save();
        }
        Refresh();
    }

    public void ResetDefaults()
    {
        _capturing = null;
        Keys.Reset();
        _message.Text = "Defaults restored.";
        Save();
        Refresh();
    }

    private void Save()
    {
        if (Keys.Save() is { } error) _message.Text += $"   Not saved: {error}";
        BindingsChanged?.Invoke();
    }

    private void Refresh()
    {
        foreach (var (slot, button) in _buttons)
        {
            var capturing = _capturing == slot;
            button.Text = capturing ? "press a key…" : Keys.Name(Keys.Current.Get(slot.Action, slot.Slot));
            button.Modulate = capturing ? new Color("f1d39b") : Colors.White;
        }
    }
}
