using System.IO.Compression;
using System.Text;

namespace AdocNet.Converters.Pdf;

/// <summary>
/// Handles embedding TrueType fonts into PDF objects: subsetting, CIDFont creation,
/// ToUnicode CMap, glyph ID encoding, and code point tracking.
/// </summary>
internal static class PdfFontEmbedder
{
    /// <summary>
    /// Embeds all registered TrueType fonts into PDF objects, filling placeholder entries.
    /// Must be called during ToBytes() after all text has been written.
    /// </summary>
    internal static void EmbedFonts(
        Dictionary<string, TrueTypeFont> embeddedFonts,
        Dictionary<string, HashSet<int>> usedCodePoints,
        Dictionary<string, int> placeholders,
        Func<PdfObject, int> allocObject,
        Action<int, PdfObject> setObject,
        HashSet<string>? monospaceFonts = null)
    {
        foreach (var (fontKey, font) in embeddedFonts)
        {
            bool isMonospace = monospaceFonts?.Contains(fontKey) ?? false;
            var usedCps = usedCodePoints[fontKey];
            int placeholderId = placeholders[fontKey];

            // 1. Subset and compress font data
            var subsetResult = TrueTypeSubsetter.Subset(font, usedCps);
            byte[] compressedFont = Compress(subsetResult.FontData);

            // 2. Create font stream object
            int fontStreamId = allocObject(new PdfObject(
                $"<< /Length {compressedFont.Length} /Length1 {subsetResult.FontData.Length} /Filter /FlateDecode >>",
                compressedFont));

            // 3. Build /W array (glyph widths for used glyphs, keyed by old GID = CID)
            var wEntries = new StringBuilder();
            foreach (var cp in usedCps.OrderBy(c => c))
            {
                var gid = font.GetGlyphId(cp);
                var width = (int)(font.GetGlyphWidth(gid) * 1000.0 / font.UnitsPerEm);
                wEntries.Append($"{gid} [{width}] ");
            }

            // 4. Build CIDToGIDMap: maps old GID (CID in content stream) → new GID in subset
            byte[] cidToGidMap = BuildCidToGidMap(font, usedCps, subsetResult.OldToNewGlyphIds);
            int cidToGidMapId = allocObject(new PdfObject(
                $"<< /Length {cidToGidMap.Length} >>",
                cidToGidMap));

            // 5. Create font descriptor
            // Flag bits: 1=FixedPitch, 2=Serif, 4=Symbolic, 8=Script, 32=Nonsymbolic, 64=Italic.
            // Symbolic and Nonsymbolic are mutually exclusive. Set FixedPitch for monospace
            // fonts so PDF readers apply correct character spacing.
            int flags = isMonospace ? 33 /* FixedPitch + Nonsymbolic */ : 32 /* Nonsymbolic */;
            int descriptorId = allocObject(new PdfObject(
                $"<< /Type /FontDescriptor /FontName /{font.FontName} " +
                $"/Flags {flags} /ItalicAngle 0 " +
                $"/Ascent {font.Ascender * 1000 / font.UnitsPerEm} " +
                $"/Descent {font.Descender * 1000 / font.UnitsPerEm} " +
                $"/FontBBox [0 {font.Descender * 1000 / font.UnitsPerEm} 1000 {font.Ascender * 1000 / font.UnitsPerEm}] " +
                $"/FontFile2 {fontStreamId} 0 R >>"));

            // 6. Create CIDFont object with explicit CIDToGIDMap
            int cidFontId = allocObject(new PdfObject(
                $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{font.FontName} " +
                $"/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                $"/W [{wEntries}] /FontDescriptor {descriptorId} 0 R " +
                $"/CIDToGIDMap {cidToGidMapId} 0 R >>"));

            // 6. Build ToUnicode CMap
            var toUnicode = BuildToUnicodeCMap(font, usedCps);
            byte[] toUnicodeBytes = Encoding.ASCII.GetBytes(toUnicode);
            int toUnicodeId = allocObject(new PdfObject(
                $"<< /Length {toUnicodeBytes.Length} >>",
                toUnicodeBytes));

            // 7. Create Type0 (composite) font object — fill the placeholder
            setObject(placeholderId, new PdfObject(
                $"<< /Type /Font /Subtype /Type0 /BaseFont /{font.FontName} " +
                $"/Encoding /Identity-H " +
                $"/DescendantFonts [{cidFontId} 0 R] " +
                $"/ToUnicode {toUnicodeId} 0 R >>"));
        }
    }

    /// <summary>
    /// Builds a binary CIDToGIDMap stream that maps CIDs (= old glyph IDs used in the content stream)
    /// to the new glyph IDs in the subset font.
    /// </summary>
    private static byte[] BuildCidToGidMap(TrueTypeFont font, HashSet<int> usedCodePoints,
        Dictionary<ushort, ushort> oldToNew)
    {
        // Find the max CID (old GID) we need to map
        ushort maxCid = 0;
        foreach (var cp in usedCodePoints)
        {
            var gid = font.GetGlyphId(cp);
            if (gid > maxCid) maxCid = gid;
        }

        // CIDToGIDMap is a stream of (maxCid + 1) * 2 bytes, big-endian uint16 per entry
        byte[] map = new byte[(maxCid + 1) * 2];
        foreach (var (oldGid, newGid) in oldToNew)
        {
            if (oldGid <= maxCid)
            {
                map[oldGid * 2] = (byte)(newGid >> 8);
                map[oldGid * 2 + 1] = (byte)newGid;
            }
        }
        return map;
    }

    internal static string BuildToUnicodeCMap(TrueTypeFont font, HashSet<int> usedCodePoints)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/CIDInit /ProcSet findresource begin");
        sb.AppendLine("12 dict begin");
        sb.AppendLine("begincmap");
        sb.AppendLine("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def");
        sb.AppendLine("/CMapName /Adobe-Identity-UCS def");
        sb.AppendLine("/CMapType 2 def");
        sb.AppendLine("1 begincodespacerange");
        sb.AppendLine("<0000> <FFFF>");
        sb.AppendLine("endcodespacerange");

        var entries = usedCodePoints.OrderBy(cp => cp).ToList();
        for (int i = 0; i < entries.Count; i += 100)
        {
            int count = Math.Min(100, entries.Count - i);
            sb.AppendLine($"{count} beginbfchar");
            for (int j = i; j < i + count; j++)
            {
                var cp = entries[j];
                var gid = font.GetGlyphId(cp);
                // The bfchar destination is a UTF-16BE string: one 16-bit unit for a
                // BMP codepoint, a surrogate pair for a supplementary one. Writing a
                // raw >U+FFFF value (e.g. "1F6C7") is malformed and truncates on
                // extraction (U+1F6C7 → U+1F6C); emit the surrogate pair instead (#77).
                sb.AppendLine($"<{gid:X4}> <{Utf16BeHex(cp)}>");
            }
            sb.AppendLine("endbfchar");
        }

        sb.AppendLine("endcmap");
        sb.AppendLine("CMapName currentdict /CMap defineresource pop");
        sb.AppendLine("end");
        sb.AppendLine("end");
        return sb.ToString();
    }

    /// <summary>
    /// Encodes a Unicode codepoint as upper-case UTF-16BE hex: four digits for a
    /// BMP codepoint, eight (a high+low surrogate pair) for a supplementary one.
    /// Used for ToUnicode <c>bfchar</c> destinations, which must be UTF-16BE (#77).
    /// </summary>
    internal static string Utf16BeHex(int codePoint)
    {
        var sb = new StringBuilder(8);
        foreach (var unit in char.ConvertFromUtf32(codePoint))
            sb.Append(((int)unit).ToString("X4"));
        return sb.ToString();
    }

    /// <summary>
    /// Enumerates the Unicode codepoints of <paramref name="text"/>, combining a
    /// surrogate pair into the single codepoint it encodes. Iterating codepoints
    /// (rather than UTF-16 code units) keeps non-BMP characters whole, so they map
    /// to one glyph rather than two missing-glyph halves (issue #72).
    /// </summary>
    internal static IEnumerable<int> EnumerateCodePoints(string text)
    {
        for (int i = 0; i < text.Length;)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return char.ConvertToUtf32(c, text[i + 1]);
                i += 2;
            }
            else
            {
                yield return c;
                i++;
            }
        }
    }

    internal static string EncodeTextAsGlyphIds(string text, TrueTypeFont font)
    {
        var sb = new StringBuilder(text.Length * 4);
        foreach (var cp in EnumerateCodePoints(text))
        {
            var gid = font.GetGlyphId(cp);
            sb.Append($"{gid:X4}");
        }
        return sb.ToString();
    }

    internal static void TrackCodePoints(Dictionary<string, HashSet<int>> usedCodePoints, string fontKey, string text)
    {
        if (usedCodePoints.TryGetValue(fontKey, out var codePoints))
        {
            foreach (var cp in EnumerateCodePoints(text))
                codePoints.Add(cp);
        }
    }

    internal static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }
        uint adler = ComputeAdler32(data);
        ms.WriteByte((byte)(adler >> 24));
        ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >> 8));
        ms.WriteByte((byte)adler);
        return ms.ToArray();
    }

    private static uint ComputeAdler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (byte d in data)
        {
            a = (a + d) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }
}
