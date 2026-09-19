using Godot;
using PalaceRoomViewer.Core;

namespace PalaceRoomViewer;

public partial class LocusDisplay : Node3D
{
    public int PositionNumber { get; private set; }
    public Locus? Locus { get; private set; }
    public Label3D Marker { get; private set; } = null!;
    public Label3D Billboard { get; private set; } = null!;
    public Vector3 LogicalAnchor { get; private set; }
    private float _textHeight;

    public void Initialize(int position, Locus? locus)
    {
        PositionNumber = position;
        Locus = locus;
        Name = $"Position{position:00}";
        LogicalAnchor = RoomGeometry.ToGodot(RoomLayout.Anchor(position));
        Position = RoomGeometry.ToGodot(RoomLayout.Presentation(position));
        Marker = new Label3D
        {
            Name = "Marker", Text = position.ToString("00"), FontSize = 64, PixelSize = .003f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = locus is null ? new Color("819396") : new Color("d0b47d"),
            OutlineModulate = new Color("102028"), OutlineSize = 9,
            Offset = new(0, 150), NoDepthTest = false
        };
        Billboard = new Label3D
        {
            Name = "Billboard", Text = locus?.Text ?? "", FontSize = 48, PixelSize = .0055f,
            Width = Mathf.Max(600, Mathf.Sqrt((locus?.Text.Length ?? 0) * 26f * 58f * 2.1f)),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = new Color("f2eee4"), OutlineModulate = new Color("0b1720"),
            OutlineSize = 8, NoDepthTest = false, Visible = false
        };
        AddChild(Marker);
        AddChild(Billboard);
        // Label3D's billboard AABB is a conservative volume, not the text's height.
        // Measure wrapped glyph layout once to avoid unnecessarily shrinking short text.
        using var paragraph = new TextParagraph
        {
            Width = Billboard.Width,
            BreakFlags = TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Adaptive
        };
        paragraph.AddString(Billboard.Text, ThemeDB.FallbackFont, Billboard.FontSize);
        _textHeight = paragraph.GetSize().Y * Billboard.PixelSize;
    }

    public void UpdateScale(Camera3D camera)
    {
        var distance = camera.GlobalPosition.DistanceTo(GlobalPosition);
        var scale = Mathf.Clamp(distance / 5f, .4f, 1f);
        // Keep arbitrarily long text inside its Slice. Full text is retained, and the HUD
        // provides a scrollable reading view without shrinking the user's reading font.
        if (_textHeight > 0) scale = Mathf.Min(scale, 1.35f / _textHeight);
        scale = Mathf.Min(scale, 3.3f / (Billboard.Width * Billboard.PixelSize));
        Billboard.Scale = Vector3.One * scale;
        var markerScale = Mathf.Clamp(distance / 9f, .65f, 1.15f);
        Marker.Scale = Vector3.One * markerScale;
        Marker.Offset = new(0, (Mathf.Max(.24f, _textHeight * scale / 2) + .15f) / (Marker.PixelSize * markerScale));
    }
}
