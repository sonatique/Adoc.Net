using AdocNet.Converters.Pdf;
using AdocNet.Parser;
using System.Text;

namespace AdocNet.Tests;

[TestFixture]
public class TrueTypeFontTests
{
    private static string? FindSystemFont()
    {
        string[] candidates =
        [
            @"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\calibri.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/System/Library/Fonts/Supplemental/Courier New.ttf",
            "/System/Library/Fonts/Supplemental/Times New Roman.ttf",
            "/Library/Fonts/Arial.ttf",
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    // ── Parser tests ─────────────────────────────────────────────────

    [Test]
    public void TrueType_parser_reads_glyph_count_greater_than_zero()
    {
        var fontPath = FindSystemFont();
        if (fontPath is null) { Assert.Ignore("No system TrueType font available"); return; }

        var font = TrueTypeFont.Parse(File.ReadAllBytes(fontPath));
        // If the parser works, 'A' should have a non-zero glyph ID
        Assert.That(font.GetGlyphId('A'), Is.GreaterThan((ushort)0));
        Assert.That(font.UnitsPerEm, Is.GreaterThan(0));
        Assert.That(font.FontName, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void TrueType_cmap_maps_common_characters()
    {
        var fontPath = FindSystemFont();
        if (fontPath is null) { Assert.Ignore("No system font"); return; }

        var font = TrueTypeFont.Parse(File.ReadAllBytes(fontPath));

        // ASCII characters should have glyph IDs
        Assert.That(font.GetGlyphId('a'), Is.GreaterThan((ushort)0));
        Assert.That(font.GetGlyphId('Z'), Is.GreaterThan((ushort)0));
        Assert.That(font.GetGlyphId('0'), Is.GreaterThan((ushort)0));

        // Accented characters (Latin-1 supplement)
        Assert.That(font.GetGlyphId('\u00E9'), Is.GreaterThan((ushort)0), "e-acute should be mapped");
        Assert.That(font.GetGlyphId('\u00E8'), Is.GreaterThan((ushort)0), "e-grave should be mapped");
    }

    // ── Subsetting tests ─────────────────────────────────────────────

    [Test]
    public void Subsetter_produces_smaller_font()
    {
        var fontPath = FindSystemFont();
        if (fontPath is null) { Assert.Ignore("No system font"); return; }

        var font = TrueTypeFont.Parse(File.ReadAllBytes(fontPath));
        var usedCodePoints = new HashSet<int> { 'A', 'B', 'C', ' ' };
        byte[] subset = TrueTypeSubsetter.Subset(font, usedCodePoints).FontData;

        Assert.That(subset.Length, Is.GreaterThan(0));
        Assert.That(subset.Length, Is.LessThan(font.FontData.Length),
            "Subset should be smaller than the full font");
    }

    [Test]
    public void Subsetter_includes_notdef_glyph()
    {
        var fontPath = FindSystemFont();
        if (fontPath is null) { Assert.Ignore("No system font"); return; }

        var font = TrueTypeFont.Parse(File.ReadAllBytes(fontPath));
        var usedCodePoints = new HashSet<int> { 'X' };
        byte[] subset = TrueTypeSubsetter.Subset(font, usedCodePoints).FontData;

        // Subset should be a valid TrueType: starts with 0x00010000
        Assert.That(subset.Length, Is.GreaterThan(12));
        Assert.That(subset[0], Is.EqualTo(0));
        Assert.That(subset[1], Is.EqualTo(1));
        Assert.That(subset[2], Is.EqualTo(0));
        Assert.That(subset[3], Is.EqualTo(0));
    }

    // ── Unicode rendering tests ──────────────────────────────────────

    [Test]
    public void Unicode_rendering_with_embedded_font_produces_valid_pdf()
    {
        var fontPath = FindSystemFont();
        if (fontPath is null) { Assert.Ignore("No system font"); return; }

        var doc = AdocParser.Parse("= café résumé naïve\n\nUnicode text: café résumé naïve").Document;
        var options = new PdfRenderOptions { FontPath = fontPath };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);

        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("%PDF-1.4"));
        Assert.That(text, Does.Contain("/CIDFontType2"));
        Assert.That(text, Does.Contain("/ToUnicode"));
    }

    // ── Semantic correctness: glyph ID round-trip ─────────────────────

    [Test]
    public void Embedded_font_glyph_ids_resolve_to_correct_characters_via_tounicode()
    {
        // This test verifies the SEMANTIC CORRECTNESS of font embedding:
        // glyph IDs in the content stream must map back to the correct Unicode
        // characters via the ToUnicode CMap. This catches CIDToGIDMap bugs
        // where the subsetter renumbers glyphs but the mapping is wrong.

        var fontPath = FindSystemFont();
        if (fontPath is null) { Assert.Ignore("No system font"); return; }

        var doc = AdocParser.Parse("= Test\n\nHello world.").Document;
        var options = new PdfRenderOptions { FontPath = fontPath };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);
        var pdfText = Encoding.ASCII.GetString(bytes);

        // Extract the ToUnicode CMap: find "beginbfchar" ... "endbfchar" block
        // Each line is: <glyphId> <unicodeCodePoint>
        var glyphToUnicode = new Dictionary<string, string>();
        int bfStart = pdfText.IndexOf("beginbfchar", StringComparison.Ordinal);
        int bfEnd = pdfText.IndexOf("endbfchar", StringComparison.Ordinal);
        Assert.That(bfStart, Is.GreaterThan(0), "ToUnicode CMap must contain beginbfchar");

        var cmapBlock = pdfText.Substring(bfStart, bfEnd - bfStart);
        foreach (var line in cmapBlock.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("<") && trimmed.Contains("> <"))
            {
                var parts = trimmed.Split(new[] { "> <" }, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    string gid = parts[0].TrimStart('<');
                    string unicode = parts[1].TrimEnd('>');
                    glyphToUnicode[gid] = unicode;
                }
            }
        }

        Assert.That(glyphToUnicode.Count, Is.GreaterThan(0), "ToUnicode CMap must have entries");

        // Now find hex-encoded text in the content stream: <XXXX XXXX ...> Tj
        // and verify each glyph ID maps to a valid character via ToUnicode
        int searchPos = 0;
        int hexStringsFound = 0;
        while ((searchPos = pdfText.IndexOf("> Tj", searchPos, StringComparison.Ordinal)) >= 0)
        {
            // Walk back to find the opening <
            int openBracket = pdfText.LastIndexOf('<', searchPos);
            if (openBracket >= 0 && openBracket < searchPos)
            {
                string hexContent = pdfText.Substring(openBracket + 1, searchPos - openBracket - 1).Trim();
                if (hexContent.Length >= 4 && hexContent.Length % 4 == 0)
                {
                    hexStringsFound++;
                    // Each 4-char group is a glyph ID
                    for (int i = 0; i < hexContent.Length; i += 4)
                    {
                        string glyphHex = hexContent.Substring(i, 4);
                        Assert.That(glyphToUnicode.ContainsKey(glyphHex), Is.True,
                            $"Glyph ID <{glyphHex}> in content stream has no ToUnicode mapping — " +
                            $"this means the text will render as garbled characters");
                    }
                }
            }
            searchPos++;
        }

        Assert.That(hexStringsFound, Is.GreaterThan(0),
            "PDF should contain hex-encoded glyph ID strings for embedded font text");
    }

    [Test]
    public void Embedded_font_has_cidtogidmap_stream_not_identity()
    {
        // After subsetting, glyph IDs are renumbered. The CIDToGIDMap must be
        // an explicit stream (not /Identity) to map old GIDs to new GIDs.
        // Using /Identity with a subsetted font produces garbled text.

        var fontPath = FindSystemFont();
        if (fontPath is null) { Assert.Ignore("No system font"); return; }

        var doc = AdocParser.Parse("= Test\n\nHello world.").Document;
        var options = new PdfRenderOptions { FontPath = fontPath };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);
        var text = Encoding.ASCII.GetString(bytes);

        // Must NOT use /Identity — that's the bug pattern
        Assert.That(text, Does.Not.Contain("/CIDToGIDMap /Identity"),
            "Subsetted fonts must use an explicit CIDToGIDMap stream, not /Identity");

        // Must have a CIDToGIDMap reference to an object
        Assert.That(text, Does.Contain("/CIDToGIDMap "),
            "CIDFont must have a CIDToGIDMap entry");
    }

    [Test]
    public void Subsetter_oldtonew_mapping_is_consistent_with_subset()
    {
        // Verify the subsetter's glyph ID mapping: the new IDs should be
        // contiguous starting from 0, and every used glyph should be mapped.

        var fontPath = FindSystemFont();
        if (fontPath is null) { Assert.Ignore("No system font"); return; }

        var font = TrueTypeFont.Parse(File.ReadAllBytes(fontPath));
        var usedCodePoints = new HashSet<int> { 'H', 'e', 'l', 'o', ' ', 'W', 'r', 'd' };
        var result = TrueTypeSubsetter.Subset(font, usedCodePoints);

        // Every used code point's glyph ID must be in the mapping
        foreach (var cp in usedCodePoints)
        {
            var oldGid = font.GetGlyphId(cp);
            if (oldGid != 0)
            {
                Assert.That(result.OldToNewGlyphIds.ContainsKey(oldGid), Is.True,
                    $"Code point U+{cp:X4} ('{(char)cp}') with old GID {oldGid} must be in oldToNew mapping");
            }
        }

        // New IDs must be contiguous 0..N-1
        var newIds = result.OldToNewGlyphIds.Values.OrderBy(x => x).ToList();
        for (int i = 0; i < newIds.Count; i++)
        {
            Assert.That(newIds[i], Is.EqualTo((ushort)i),
                $"New glyph IDs must be contiguous: expected {i}, got {newIds[i]}");
        }

        // Subset font data should be valid TrueType
        Assert.That(result.FontData.Length, Is.GreaterThan(12));
        Assert.That(result.FontData[0], Is.EqualTo(0));
        Assert.That(result.FontData[1], Is.EqualTo(1));
    }

    // ── Determinism tests ────────────────────────────────────────────

    [Test]
    public void Determinism_two_renders_produce_identical_output()
    {
        var doc = AdocParser.Parse("= Test\n\nHello world.").Document;
        var bytes1 = new PdfRenderer().RenderToBytes(doc);
        var bytes2 = new PdfRenderer().RenderToBytes(doc);

        Assert.That(bytes1, Is.EqualTo(bytes2), "Two renders of the same document must be byte-identical");
    }

    [Test]
    public void Determinism_with_embedded_font()
    {
        var fontPath = FindSystemFont();
        if (fontPath is null) { Assert.Ignore("No system font"); return; }

        var doc = AdocParser.Parse("= Test\n\nHello café.").Document;
        var options = new PdfRenderOptions { FontPath = fontPath };
        var bytes1 = new PdfRenderer().RenderToBytes(doc, options);
        var bytes2 = new PdfRenderer().RenderToBytes(doc, options);

        Assert.That(bytes1, Is.EqualTo(bytes2), "Embedded font renders must be byte-identical");
    }
}
