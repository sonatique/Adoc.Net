using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Converters.Pdf;

namespace AdocNet.Tests;

/// <summary>
/// Tests for the TrueType font subsetter. These regressions catch bugs in hmtx
/// subsetting and FontDescriptor flags that affect how PDF readers render embedded text.
/// </summary>
[TestFixture]
public class PdfFontSubsetTests
{
    private const string CourierFontPath = @"C:\Windows\Fonts\cour.ttf";

    [Test]
    public void Monospace_subset_preserves_advance_widths_past_numberOfHMetrics()
    {
        // Regression: Windows Courier New has numberOfHMetrics=3 (only the first 3 glyphs
        // store full hmtx records; the rest store only lsb and reuse the last advance).
        // The subsetter must read the original advance correctly for ALL glyph IDs.
        // Symptom of the bug: monospace text rendered in Adobe Acrobat / Edge / Chrome
        // showed extra space between every character pair.
        if (!File.Exists(CourierFontPath))
            Assert.Ignore($"Test requires {CourierFontPath}");

        var doc = AdocNet.Parser.AdocParser.Parse("= T\n\n----\nadoc2pdf.bat hello world test\n----").Document;
        var opts = new PdfRenderOptions { MonoFontPath = CourierFontPath };
        var pdfBytes = new PdfRenderer().RenderToBytes(doc, opts);

        var advances = ExtractMonoFontAdvances(pdfBytes);
        Assert.That(advances, Is.Not.Empty, "No mono font advances extracted from PDF");

        // For Courier monospace, all advances must be equal (1229 in 2048-em).
        // Bug manifests as wildly varying advances (0, 228, 461, etc.) for glyphs > GID 2.
        var distinctAdvances = advances.Distinct().ToList();
        Assert.That(distinctAdvances.Count, Is.EqualTo(1),
            $"Monospace font should have all glyphs at same advance width. " +
            $"Got distinct values: [{string.Join(", ", distinctAdvances)}]. " +
            $"Bug indicator: subsetter read past numberOfHMetrics without honoring packed format.");
    }

    [Test]
    public void Monospace_font_descriptor_has_FixedPitch_flag()
    {
        // Asciidoctor PDF sets the FixedPitch flag (bit 0 = value 1) on monospace
        // font descriptors. Without it, some PDF readers may not apply correct
        // monospace rendering rules. Expected /Flags = 33 (1 FixedPitch + 32 Nonsymbolic).
        if (!File.Exists(CourierFontPath))
            Assert.Ignore($"Test requires {CourierFontPath}");

        var doc = AdocNet.Parser.AdocParser.Parse("= T\n\n----\ncode\n----").Document;
        var opts = new PdfRenderOptions { MonoFontPath = CourierFontPath };
        var pdfBytes = new PdfRenderer().RenderToBytes(doc, opts);
        string pdfText = Encoding.ASCII.GetString(pdfBytes);

        // Find all FontDescriptor blocks and check the one for the embedded Courier
        var descriptorRegex = new Regex(
            @"/Type\s*/FontDescriptor[^>]*?/FontName\s*/([^\s/>]+)[^>]*?/Flags\s+(\d+)",
            RegexOptions.Singleline);
        bool foundCourier = false;
        foreach (Match m in descriptorRegex.Matches(pdfText))
        {
            string fontName = m.Groups[1].Value;
            int flags = int.Parse(m.Groups[2].Value);
            if (fontName.Contains("Courier"))
            {
                foundCourier = true;
                Assert.That((flags & 1) != 0, Is.True,
                    $"Monospace font {fontName} missing FixedPitch flag (got Flags={flags})");
            }
        }
        Assert.That(foundCourier, Is.True, "Did not find embedded Courier font descriptor");
    }

    private static List<int> ExtractMonoFontAdvances(byte[] pdfBytes)
    {
        string pdfText = Encoding.ASCII.GetString(pdfBytes);
        // Find FontDescriptor for an embedded Courier (one that has FontFile2)
        var descriptorRegex = new Regex(
            @"/Type\s*/FontDescriptor[^>]*?/FontName\s*/(?<name>[^\s/>]+)[^>]*?/FontFile2\s+(?<ff>\d+)\s+0\s+R",
            RegexOptions.Singleline);
        Match? courierMatch = null;
        foreach (Match m in descriptorRegex.Matches(pdfText))
        {
            if (m.Groups["name"].Value.Contains("Courier"))
            {
                courierMatch = m;
                break;
            }
        }
        if (courierMatch is null) return [];

        int objNum = int.Parse(courierMatch.Groups["ff"].Value);
        // Find the object body in the PDF
        var objMarker = $"\n{objNum} 0 obj";
        int objIdx = pdfText.IndexOf(objMarker, StringComparison.Ordinal);
        if (objIdx < 0) return [];
        int streamMarker = pdfText.IndexOf("stream\n", objIdx, StringComparison.Ordinal);
        if (streamMarker < 0) return [];
        int streamStart = streamMarker + "stream\n".Length;
        int endStream = pdfText.IndexOf("\nendstream", streamStart, StringComparison.Ordinal);
        if (endStream < 0) return [];

        var compressed = new byte[endStream - streamStart];
        Array.Copy(pdfBytes, streamStart, compressed, 0, compressed.Length);

        // Decompress (skip 2-byte zlib header)
        byte[] fontData;
        try
        {
            using var ms = new MemoryStream(compressed, 2, compressed.Length - 2);
            using var inflate = new DeflateStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            inflate.CopyTo(outMs);
            fontData = outMs.ToArray();
        }
        catch { return []; }

        // Parse TTF table directory
        int numTables = (fontData[4] << 8) | fontData[5];
        int hmtxOff = -1, numHMetrics = -1;
        for (int i = 0; i < numTables; i++)
        {
            int e = 12 + i * 16;
            string tag = Encoding.ASCII.GetString(fontData, e, 4);
            int off = (fontData[e + 8] << 24) | (fontData[e + 9] << 16) | (fontData[e + 10] << 8) | fontData[e + 11];
            if (tag == "hmtx") hmtxOff = off;
            else if (tag == "hhea") numHMetrics = (fontData[off + 34] << 8) | fontData[off + 35];
        }
        if (hmtxOff < 0 || numHMetrics <= 0) return [];

        var advances = new List<int>(numHMetrics);
        for (int g = 0; g < numHMetrics; g++)
            advances.Add((fontData[hmtxOff + g * 4] << 8) | fontData[hmtxOff + g * 4 + 1]);
        return advances;
    }
}
