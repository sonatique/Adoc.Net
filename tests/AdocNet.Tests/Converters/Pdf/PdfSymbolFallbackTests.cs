using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests.Converters.Pdf;

/// <summary>
/// Regression tests for issue #72: the PDF symbol font-fallback must (A) treat a
/// non-BMP character as one codepoint — emitting a single missing-glyph indicator
/// rather than two <c>?</c> — and (B) keep routing symbols to the fallback font in
/// a block that also contains a footnote (the table-cell path regressed in #69).
/// </summary>
[TestFixture]
public class PdfSymbolFallbackTests
{
    private static byte[] Render(string adoc) =>
        new PdfRenderer().RenderToBytes(AdocParser.Parse(adoc).Document, PdfRenderOptions.A4);

    private static string Raw(byte[] pdf) => Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

    // The concatenated literal (parenthesized) Tj strings — the WinAnsi/base-font
    // runs, where a glyph the font can't show appears as a literal '?'. Symbols
    // routed to the embedded fallback font are emitted as hex <…> Tj instead, so
    // they do not appear here.
    private static string LiteralTjText(string raw)
    {
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(raw, @"\(((?:[^()\\]|\\.)*)\)\s*Tj"))
            sb.Append(m.Groups[1].Value);
        return sb.ToString();
    }

    private static int HexTjRuns(string raw) => Regex.Matches(raw, @"<[0-9A-Fa-f]+>\s*Tj").Count;

    [Test]
    public void Non_bmp_char_emits_a_single_missing_glyph_not_double()
    {
        // U+1F6C7 (PROHIBITED SIGN) is a non-BMP codepoint not covered by the
        // fallback font — it must collapse to one '?', never '??' (two surrogate
        // halves). The BMP arrow ⇒ on the same line still routes to the fallback.
        var raw = Raw(Render("= T\n\nProhibited &#x1F6C7; sign and arrow => Z.\n"));
        var literal = LiteralTjText(raw);

        Assert.That(literal, Does.Not.Contain("??"), "a non-BMP char must not become two question marks");
        Assert.That(literal.Count(c => c == '?'), Is.EqualTo(1), "exactly one missing-glyph indicator");
        Assert.That(HexTjRuns(raw), Is.GreaterThanOrEqualTo(1), "the ⇒ arrow should route to the embedded fallback font");
    }

    [Test]
    public void Footnote_in_table_cell_does_not_disable_symbol_fallback()
    {
        // The ⇒ in a cell that also has a footnote must still render via the
        // fallback font (embedded), not regress to '?' in the base font (#72/#69).
        var doc =
            "= T\n\n" +
            "|===\n" +
            "| Plain => K alone\n" +
            "| => K footnote:[a note] mixed\n" +
            "|===\n";
        var raw = Raw(Render(doc));

        Assert.That(LiteralTjText(raw), Does.Not.Contain("?"),
            "no symbol should fall back to a base-font '?' — the footnote cell's ⇒ must use the fallback font");
        Assert.That(HexTjRuns(raw), Is.GreaterThanOrEqualTo(1), "the arrows should be emitted via the embedded fallback font");
    }

    [Test]
    public void Check_mark_in_footnote_cell_routes_to_fallback()
    {
        var doc =
            "= T\n\n" +
            "|===\n" +
            "| ✓ done footnote:[a note]\n" +
            "|===\n";
        var raw = Raw(Render(doc));

        Assert.That(LiteralTjText(raw), Does.Not.Contain("?"),
            "the check mark in a footnote cell must route to the fallback font, not '?'");
    }

    [Test]
    public void Symbol_in_a_plain_paragraph_still_routes_to_fallback()
    {
        // Regression guard for the #52 behaviour that must remain intact.
        var raw = Raw(Render("= T\n\nArrow => Z and check ✓ here.\n"));
        Assert.That(LiteralTjText(raw), Does.Not.Contain("?"));
        Assert.That(HexTjRuns(raw), Is.GreaterThanOrEqualTo(1));
    }
}
