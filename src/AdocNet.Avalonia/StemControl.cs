using global::Avalonia;
using global::Avalonia.Automation;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Media;

namespace AdocNet.Avalonia;

/// <summary>
/// Draws a typeset formula at the inherited font size and in the inherited text colour. Inside a
/// TextBlock its baseline sits on the text baseline.
/// </summary>
internal sealed class StemControl : Control
{
    private readonly StemGraphic _graphic;
    private readonly Geometry _geometry;

    static StemControl()
    {
        AffectsMeasure<StemControl>(TextElement.FontSizeProperty);
        AffectsRender<StemControl>(TextElement.ForegroundProperty);
    }

    public StemControl(StemGraphic graphic, string source)
    {
        _graphic = graphic;
        _geometry = Geometry.Parse("F1 " + graphic.PathData); // non-zero fill, as glyph outlines need
        AutomationProperties.SetName(this, source);
    }

    private double FontSize => TextElement.GetFontSize(this);

    protected override Size MeasureOverride(Size availableSize)
    {
        double size = FontSize;
        // The text layout aligns an embedded control's baseline at this distance from its top.
        TextBlock.SetBaselineOffset(this, _graphic.Ascent * size);
        return new Size(_graphic.Width * size, _graphic.Height * size);
    }

    public override void Render(DrawingContext context)
    {
        double size = FontSize;
        using (context.PushTransform(Matrix.CreateScale(size, size)))
            context.DrawGeometry(TextElement.GetForeground(this) ?? Brushes.Black, null, _geometry);
    }
}
