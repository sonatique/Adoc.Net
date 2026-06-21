using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests.Converters.Pdf;

/// <summary>
/// Regression tests for issue #75: the PDF Unicode fallback is an ordered chain
/// (primary embedded → DejaVu Sans → Symbola), so symbols DejaVu lacks — e.g. the
/// SMP block characters the Avalonia preview shows via system fallback — render via
/// the secondary symbol font instead of a base-font '?'.
/// </summary>
[TestFixture]
public class PdfFallbackChainTests
{
    private static byte[] Render(string adoc) =>
        new PdfRenderer().RenderToBytes(AdocParser.Parse(adoc).Document, PdfRenderOptions.A4);

    private static string Raw(byte[] pdf) => Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

    private static string LiteralTjText(string raw)
    {
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(raw, @"\(((?:[^()\\]|\\.)*)\)\s*Tj"))
            sb.Append(m.Groups[1].Value);
        return sb.ToString();
    }

    private static bool EmbedsFont(string raw, string name) =>
        Regex.IsMatch(raw, @"/BaseFont\s*/" + Regex.Escape(name));

    [Test]
    public void Smp_symbols_render_via_the_symbol_fallback_not_question_mark()
    {
        // U+1F6C7, U+1F6AB, U+26D4, U+29B8 are outside DejaVu's coverage but inside
        // Symbola's; none should fall back to a base-font '?' (#75).
        var raw = Raw(Render("= T\n\nMix ✓ &#x1F6C7; &#x1F6AB; &#x26D4; &#x29B8; here.\n"));

        Assert.That(LiteralTjText(raw), Does.Not.Contain("?"),
            "every listed symbol should route to a fallback font, not a base-font '?'");
        Assert.That(EmbedsFont(raw, "Symbola"), Is.True, "the symbol fallback font must be embedded");
    }

    [Test]
    public void Fallback_chain_consults_dejavu_before_symbola()
    {
        // ✓ is covered by the primary fallback (DejaVu); U+1F6C7 only by the
        // secondary (Symbola). Both fonts embedded ⇒ an ordered chain, not a single
        // fallback — and DejaVu still wins for glyphs it can show.
        var raw = Raw(Render("= T\n\nCheck ✓ and prohibited &#x1F6C7;.\n"));

        Assert.That(EmbedsFont(raw, "DejaVuSans"), Is.True, "DejaVu handles glyphs it covers");
        Assert.That(EmbedsFont(raw, "Symbola"), Is.True, "Symbola handles glyphs DejaVu lacks");
        Assert.That(LiteralTjText(raw), Does.Not.Contain("?"));
    }

    [Test]
    public void Symbol_font_is_absent_when_no_glyph_needs_it()
    {
        // A document whose symbols are all within DejaVu must not embed Symbola
        // (on-demand embedding keeps ordinary documents lean).
        var raw = Raw(Render("= T\n\nArrow => Z and check ✓ only.\n"));

        Assert.That(EmbedsFont(raw, "DejaVuSans"), Is.True);
        Assert.That(EmbedsFont(raw, "Symbola"), Is.False, "Symbola should be embedded only when actually used");
    }
}
