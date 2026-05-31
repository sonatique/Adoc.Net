using System.Text;
using AdocNet.Converters.Pdf;

namespace AdocNet.Tests;

/// <summary>
/// CI-safe unit coverage for the PDF SVG parser (no external font required).
/// </summary>
[TestFixture]
public class SvgParserTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    [Test]
    public void Parse_extracts_viewbox_dimensions_and_a_path_shape()
    {
        var doc = SvgParser.Parse(Utf8(
            "<svg viewBox=\"0 0 100 50\"><path d=\"M0 0 L10 10 Z\" fill=\"#ff0000\"/></svg>"));

        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Value.ViewBoxWidth, Is.EqualTo(100f).Within(0.01));
        Assert.That(doc.Value.ViewBoxHeight, Is.EqualTo(50f).Within(0.01));
        Assert.That(doc.Value.Shapes, Has.Count.EqualTo(1));
        Assert.That(doc.Value.Shapes[0].PathData, Does.Contain("M0 0"));
    }

    [Test]
    public void Parse_falls_back_to_width_height_when_no_viewbox()
    {
        var doc = SvgParser.Parse(Utf8(
            "<svg width=\"120px\" height=\"80px\"><rect x=\"0\" y=\"0\" width=\"10\" height=\"10\"/></svg>"));

        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Value.Width, Is.EqualTo(120f).Within(0.01));
        Assert.That(doc.Value.Height, Is.EqualTo(80f).Within(0.01));
        // viewBox dimensions fall back to width/height when absent.
        Assert.That(doc.Value.ViewBoxWidth, Is.EqualTo(120f).Within(0.01));
        Assert.That(doc.Value.Shapes, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_returns_null_when_no_dimensions_available()
    {
        var doc = SvgParser.Parse(Utf8("<svg><path d=\"M0 0 L1 1\"/></svg>"));
        Assert.That(doc, Is.Null);
    }

    [Test]
    public void Parse_does_not_throw_on_non_svg_bytes()
    {
        var garbage = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x42, 0x13, 0x37 };
        Assert.DoesNotThrow(() => SvgParser.Parse(garbage));
    }

    [Test]
    public void Parse_handles_empty_input_gracefully()
    {
        Assert.DoesNotThrow(() => SvgParser.Parse(System.Array.Empty<byte>()));
        Assert.That(SvgParser.Parse(System.Array.Empty<byte>()), Is.Null);
    }

    [Test]
    public void ToPdfPathOps_emits_moveto_lineto_and_close()
    {
        var ops = SvgParser.ToPdfPathOps(
            "M0 0 L10 0 L10 10 Z", scaleX: 1, scaleY: 1, offsetX: 0, offsetY: 0, viewBoxHeight: 10);

        Assert.That(ops, Does.Contain(" m\n"), "moveto");
        Assert.That(ops, Does.Contain(" l\n"), "lineto");
        Assert.That(ops, Does.Contain("h\n"), "closepath");
    }
}
