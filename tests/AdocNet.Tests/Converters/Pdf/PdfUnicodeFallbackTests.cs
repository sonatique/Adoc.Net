using System.Text;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests.Converters.Pdf;

/// <summary>
/// Regression tests for portable special-character rendering in PDF (issue #52):
/// glyphs outside WinAnsi (✓, arrows) are drawn with an embedded Unicode fallback
/// font and a ToUnicode CMap, so they render off the authoring machine and remain
/// extractable — instead of rendering as '?'.
/// </summary>
[TestFixture]
public class PdfUnicodeFallbackTests
{
    private static string RenderRaw(string adoc)
    {
        var pdf = new PdfRenderer().RenderToBytes(AdocParser.Parse(adoc).Document, PdfRenderOptions.A4);
        return Encoding.Latin1.GetString(pdf);
    }

    [Test]
    public void Check_mark_and_arrows_use_embedded_unicode_font_with_tounicode()
    {
        // ✓ (U+2713), → (U+2192, from "->") and ⇒ (U+21D2, from "=>") all come
        // from the embedded DejaVu Sans fallback as a Type0/CIDFontType2 font.
        var raw = RenderRaw("= Doc\n\nA check ✓, an arrow -> and a double => here.\n");

        Assert.That(raw, Does.Contain("DejaVuSans"), "special chars should embed DejaVu Sans");
        Assert.That(raw, Does.Contain("/Subtype /Type0"), "fallback font is a composite Type0 font");
        Assert.That(raw, Does.Contain("/ToUnicode"), "a ToUnicode CMap must be present");

        // The ToUnicode CMap must map the glyphs back to their real code points so
        // the characters are selectable / searchable / extractable.
        Assert.That(raw, Does.Contain("<2713>"), "✓ must be recoverable via ToUnicode (U+2713)");
        Assert.That(raw, Does.Contain("<2192>"), "→ must be recoverable via ToUnicode (U+2192)");
        Assert.That(raw, Does.Contain("<21D2>"), "⇒ must be recoverable via ToUnicode (U+21D2)");
    }

    [Test]
    public void Check_mark_in_table_cell_uses_embedded_font()
    {
        var raw = RenderRaw("|===\n| A | B\n| ✓ | x\n|===\n");
        Assert.That(raw, Does.Contain("DejaVuSans"));
        Assert.That(raw, Does.Contain("<2713>"));
    }

    [Test]
    public void Ascii_only_document_embeds_no_fallback_font()
    {
        // The fallback font is embedded only on demand — a document with no
        // special characters must not pay for it.
        var raw = RenderRaw("= Doc\n\nJust plain ASCII text, nothing special here.\n");
        Assert.That(raw, Does.Not.Contain("DejaVuSans"),
            "no special characters → no fallback font embedded");
    }
}
