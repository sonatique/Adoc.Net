using System.Text;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests.Converters.Pdf;

/// <summary>
/// Regression tests for issue #77: a supplementary-plane (non-BMP) codepoint's
/// ToUnicode <c>bfchar</c> destination must be a UTF-16BE surrogate pair, so the
/// glyph is selectable/searchable/extractable as the correct character — not a
/// truncated 16-bit value (U+1F6C7 → U+1F6C).
/// </summary>
[TestFixture]
public class PdfToUnicodeTests
{
    [TestCase(0x41, "0041")]      // 'A' — BMP, four hex digits
    [TestCase(0x26D4, "26D4")]    // ⛔ — BMP symbol
    [TestCase(0x1F6C7, "D83DDEC7")] // 🛇 — supplementary, surrogate pair
    [TestCase(0x1F6AB, "D83DDEAB")] // 🚫 — supplementary, surrogate pair
    public void Utf16BeHex_encodes_codepoints_as_utf16be(int codePoint, string expected)
    {
        Assert.That(PdfFontEmbedder.Utf16BeHex(codePoint), Is.EqualTo(expected));
    }

    [Test]
    public void NonBmp_symbol_tounicode_is_a_surrogate_pair_not_truncated()
    {
        const string src = "= T\n\nM26D4 &#x26D4; M1F6AB &#x1F6AB; M1F6C7 &#x1F6C7; end.\n";
        var pdf = new PdfRenderer().RenderToBytes(AdocParser.Parse(src).Document, PdfRenderOptions.A4);
        var raw = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);

        // Supplementary codepoints map to their UTF-16BE surrogate pairs.
        Assert.That(raw, Does.Contain("<D83DDEC7>"), "U+1F6C7 ToUnicode should be the surrogate pair");
        Assert.That(raw, Does.Contain("<D83DDEAB>"), "U+1F6AB ToUnicode should be the surrogate pair");

        // The malformed 5-hex (raw codepoint) destination must NOT appear.
        Assert.That(raw, Does.Not.Contain("<1F6C7>"), "the raw >U+FFFF value must not be emitted");
        Assert.That(raw, Does.Not.Contain("<1F6AB>"));

        // BMP symbols stay a single 16-bit unit.
        Assert.That(raw, Does.Contain("<26D4>"), "U+26D4 (BMP) ToUnicode is a single 16-bit unit");
    }
}
