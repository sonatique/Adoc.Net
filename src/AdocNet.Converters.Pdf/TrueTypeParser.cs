using System.Text;

namespace AdocNet.Converters.Pdf;

/// <summary>
/// Minimal TrueType font file parser. Extracts cmap, hmtx, head, OS/2, and name tables
/// for PDF embedding with Unicode support. Pure managed C# — no external dependencies.
/// </summary>
internal sealed class TrueTypeFont
{
    public string FontName { get; }
    public int UnitsPerEm { get; }
    public int Ascender { get; }
    public int Descender { get; }
    /// <summary>Line gap in font units (OS/2 typo line gap when non-zero, else hhea), as Prawn uses it.</summary>
    public int LineGap { get; }
    public byte[] FontData { get; }

    // Maps Unicode code point -> glyph ID
    private readonly Dictionary<int, ushort> _cmapTable;

    // Maps glyph ID -> advance width (in font units)
    private readonly Dictionary<ushort, int> _glyphWidths;

    // Kerning pairs from the 'kern' table (first format-0 subtable, as Prawn/ttfunk read it):
    // (left glyph << 16 | right glyph) -> adjustment in font units.
    private readonly Dictionary<uint, short> _kernPairs = new();

    /// <summary>True when the font has 'kern' table pairs.</summary>
    public bool HasKerning => _kernPairs.Count > 0;

    /// <summary>Kerning adjustment between two glyphs, in font units (0 when none).</summary>
    public short GetKerning(ushort left, ushort right) =>
        _kernPairs.TryGetValue(((uint)left << 16) | right, out var k) ? k : (short)0;

    private TrueTypeFont(string fontName, int unitsPerEm, int ascender, int descender, int lineGap,
        byte[] fontData, Dictionary<int, ushort> cmapTable, Dictionary<ushort, int> glyphWidths)
    {
        LineGap = lineGap;
        FontName = fontName;
        UnitsPerEm = unitsPerEm;
        Ascender = ascender;
        Descender = descender;
        FontData = fontData;
        _cmapTable = cmapTable;
        _glyphWidths = glyphWidths;
    }

    public ushort GetGlyphId(int codePoint) => _cmapTable.GetValueOrDefault(codePoint);

    public int GetGlyphWidth(ushort glyphId) => _glyphWidths.GetValueOrDefault(glyphId, UnitsPerEm / 2);

    /// <summary>
    /// Measures text width in PDF points at the given font size.
    /// </summary>
    public float MeasureText(string text, float fontSize) => MeasureText(text, fontSize, kerning: false);

    /// <summary>
    /// Measures text width in PDF points; with <paramref name="kerning"/>, adds the 'kern'
    /// table adjustments between consecutive glyphs (scaled to 1/1000 em, as Prawn does).
    /// </summary>
    public float MeasureText(string text, float fontSize, bool kerning)
    {
        float kern = 0;
        if (kerning && _kernPairs.Count > 0)
        {
            for (int i = 1; i < text.Length; i++)
                kern += GetKerning(GetGlyphId(text[i - 1]), GetGlyphId(text[i])) * 1000f / UnitsPerEm;
        }
        return MeasureUnkerned(text, fontSize) + kern * fontSize / 1000f;
    }

    private float MeasureUnkerned(string text, float fontSize)
    {
        // Widths in whole 1/1000 em, truncated: exactly what the embedded font's /W array
        // tells the viewer (PdfFontEmbedder) and what Prawn measures with.
        int total = 0;
        foreach (var ch in text)
        {
            var gid = GetGlyphId(ch);
            total += (int)(GetGlyphWidth(gid) * 1000.0 / UnitsPerEm);
        }
        return total * fontSize / 1000f;
    }

    /// <summary>
    /// Parses a TrueType font file (.ttf) and extracts metrics for PDF embedding.
    /// </summary>
    public static TrueTypeFont Parse(byte[] data)
    {
        // Read offset table
        // uint32 sfVersion, uint16 numTables, uint16 searchRange, entrySelector, rangeShift
        int numTables = ReadUInt16(data, 4);

        // Read table directory
        var tables = new Dictionary<string, (int Offset, int Length)>();
        for (int i = 0; i < numTables; i++)
        {
            int entryOffset = 12 + i * 16;
            string tag = Encoding.ASCII.GetString(data, entryOffset, 4);
            int tableOffset = (int)ReadUInt32(data, entryOffset + 8);
            int tableLength = (int)ReadUInt32(data, entryOffset + 12);
            tables[tag] = (tableOffset, tableLength);
        }

        // Parse head table: unitsPerEm at offset 18
        int unitsPerEm = 1000;
        if (tables.TryGetValue("head", out var headTable))
        {
            unitsPerEm = ReadUInt16(data, headTable.Offset + 18);
        }

        // Parse hhea table: ascender at 4, descender at 6, numberOfHMetrics at 34
        int ascender = 0;
        int descender = 0;
        int numberOfHMetrics = 0;
        int lineGap = 0;
        if (tables.TryGetValue("hhea", out var hheaTable))
        {
            ascender = ReadInt16(data, hheaTable.Offset + 4);
            descender = ReadInt16(data, hheaTable.Offset + 6);
            lineGap = ReadInt16(data, hheaTable.Offset + 8);
            numberOfHMetrics = ReadUInt16(data, hheaTable.Offset + 34);
        }

        // Parse OS/2 table for better ascender/descender if available
        if (tables.TryGetValue("OS/2", out var os2Table))
        {
            int sTypoAscender = ReadInt16(data, os2Table.Offset + 68);
            int sTypoDescender = ReadInt16(data, os2Table.Offset + 70);
            if (sTypoAscender != 0)
                ascender = sTypoAscender;
            if (sTypoDescender != 0)
                descender = sTypoDescender;
            int sTypoLineGap = ReadInt16(data, os2Table.Offset + 72);
            if (sTypoLineGap != 0)
                lineGap = sTypoLineGap;
        }

        // Parse hmtx table: array of numberOfHMetrics entries (4 bytes each: uint16 advanceWidth + int16 lsb)
        var glyphWidths = new Dictionary<ushort, int>();
        if (tables.TryGetValue("hmtx", out var hmtxTable))
        {
            int lastAdvanceWidth = 0;
            for (int i = 0; i < numberOfHMetrics; i++)
            {
                int advanceWidth = ReadUInt16(data, hmtxTable.Offset + i * 4);
                glyphWidths[(ushort)i] = advanceWidth;
                lastAdvanceWidth = advanceWidth;
            }

            // Remaining glyphs (if any) all use the last advance width
            int maxp = 0;
            if (tables.TryGetValue("maxp", out var maxpTable))
            {
                maxp = ReadUInt16(data, maxpTable.Offset + 4);
            }
            for (int i = numberOfHMetrics; i < maxp; i++)
            {
                glyphWidths[(ushort)i] = lastAdvanceWidth;
            }
        }

        // Parse cmap table
        var cmapTable2 = new Dictionary<int, ushort>();
        if (tables.TryGetValue("cmap", out var cmapEntry))
        {
            ParseCmap(data, cmapEntry.Offset, cmapTable2);
        }

        // Parse name table: nameID=6 (PostScript name) from platform 3, encoding 1
        string fontName = "UnknownFont";
        if (tables.TryGetValue("name", out var nameTable))
        {
            fontName = ParseName(data, nameTable.Offset, nameId: 6) ?? fontName;
        }

        var font = new TrueTypeFont(fontName, unitsPerEm, ascender, descender, lineGap, data, cmapTable2, glyphWidths);
        if (tables.TryGetValue("kern", out var kernTable))
            font.ParseKern(data, kernTable.Offset, kernTable.Length);
        return font;
    }

    /// <summary>
    /// Reads the first subtable of a Microsoft 'kern' table (version 0) when it is format 0
    /// (sorted pair list). Other layouts are ignored, as in ttfunk.
    /// </summary>
    private void ParseKern(byte[] data, int offset, int length)
    {
        int end = Math.Min(data.Length, offset + length);
        if (offset + 4 > end || ReadUInt16(data, offset) != 0 || ReadUInt16(data, offset + 2) == 0) return;
        int sub = offset + 4;
        if (sub + 14 > end) return;
        int coverage = ReadUInt16(data, sub + 4);
        if ((coverage >> 8) != 0 || (coverage & 1) == 0) return; // format 0, horizontal
        int nPairs = ReadUInt16(data, sub + 6);
        int pair = sub + 14;
        for (int i = 0; i < nPairs && pair + 6 <= end; i++, pair += 6)
        {
            uint key = ((uint)ReadUInt16(data, pair) << 16) | ReadUInt16(data, pair + 2);
            _kernPairs[key] = ReadInt16(data, pair + 4);
        }
    }

    private static void ParseCmap(byte[] data, int cmapOffset, Dictionary<int, ushort> result)
    {
        int numSubtables = ReadUInt16(data, cmapOffset + 2);

        // Prefer format 12 (full Unicode) over format 4 (BMP only).
        // First pass: look for platform 3 encoding 10 (Windows full Unicode) with format 12
        for (int i = 0; i < numSubtables; i++)
        {
            int recordOffset = cmapOffset + 4 + i * 8;
            int platformId = ReadUInt16(data, recordOffset);
            int encodingId = ReadUInt16(data, recordOffset + 2);
            int subtableOffset = (int)ReadUInt32(data, recordOffset + 4);

            if (platformId == 3 && encodingId == 10)
            {
                int absOffset = cmapOffset + subtableOffset;
                int format = ReadUInt16(data, absOffset);
                if (format == 12)
                {
                    ParseCmapFormat12(data, absOffset, result);
                    return;
                }
            }
        }

        // Second pass: platform 3 encoding 1 (Windows BMP) with format 4
        for (int i = 0; i < numSubtables; i++)
        {
            int recordOffset = cmapOffset + 4 + i * 8;
            int platformId = ReadUInt16(data, recordOffset);
            int encodingId = ReadUInt16(data, recordOffset + 2);
            int subtableOffset = (int)ReadUInt32(data, recordOffset + 4);

            if (platformId == 3 && encodingId == 1)
            {
                int absOffset = cmapOffset + subtableOffset;
                int format = ReadUInt16(data, absOffset);
                if (format == 4)
                {
                    ParseCmapFormat4(data, absOffset, result);
                    return;
                }
            }
        }

        // Fallback: platform 0 (Unicode), any encoding
        for (int i = 0; i < numSubtables; i++)
        {
            int recordOffset = cmapOffset + 4 + i * 8;
            int platformId = ReadUInt16(data, recordOffset);
            int subtableOffset = (int)ReadUInt32(data, recordOffset + 4);

            if (platformId == 0)
            {
                int absOffset = cmapOffset + subtableOffset;
                int format = ReadUInt16(data, absOffset);
                if (format == 12)
                {
                    ParseCmapFormat12(data, absOffset, result);
                    return;
                }
                if (format == 4)
                {
                    ParseCmapFormat4(data, absOffset, result);
                    return;
                }
            }
        }
    }

    private static void ParseCmapFormat4(byte[] data, int offset, Dictionary<int, ushort> result)
    {
        int segCountX2 = ReadUInt16(data, offset + 6);
        int segCount = segCountX2 / 2;

        int endCodeBase = offset + 14;
        int startCodeBase = endCodeBase + segCountX2 + 2; // +2 for reservedPad
        int idDeltaBase = startCodeBase + segCountX2;
        int idRangeOffsetBase = idDeltaBase + segCountX2;

        for (int seg = 0; seg < segCount; seg++)
        {
            int endCode = ReadUInt16(data, endCodeBase + seg * 2);
            int startCode = ReadUInt16(data, startCodeBase + seg * 2);
            int idDelta = ReadInt16(data, idDeltaBase + seg * 2);
            int idRangeOffset = ReadUInt16(data, idRangeOffsetBase + seg * 2);

            if (startCode == 0xFFFF)
                break;

            for (int cp = startCode; cp <= endCode; cp++)
            {
                ushort glyphId;
                if (idRangeOffset == 0)
                {
                    glyphId = (ushort)((cp + idDelta) & 0xFFFF);
                }
                else
                {
                    // idRangeOffset is relative to its own position in the array
                    int glyphIdOffset = idRangeOffsetBase + seg * 2 + idRangeOffset + (cp - startCode) * 2;
                    if (glyphIdOffset + 1 < data.Length)
                    {
                        glyphId = ReadUInt16(data, glyphIdOffset);
                        if (glyphId != 0)
                            glyphId = (ushort)((glyphId + idDelta) & 0xFFFF);
                    }
                    else
                    {
                        glyphId = 0;
                    }
                }

                if (glyphId != 0)
                    result[cp] = glyphId;
            }
        }
    }

    private static void ParseCmapFormat12(byte[] data, int offset, Dictionary<int, ushort> result)
    {
        // Format 12: Segmented coverage — supports full Unicode (beyond BMP)
        // Header: uint16 format, uint16 reserved, uint32 length, uint32 language, uint32 numGroups
        uint numGroups = ReadUInt32(data, offset + 12);

        for (uint g = 0; g < numGroups; g++)
        {
            int groupOffset = offset + 16 + (int)(g * 12);
            uint startCharCode = ReadUInt32(data, groupOffset);
            uint endCharCode = ReadUInt32(data, groupOffset + 4);
            uint startGlyphId = ReadUInt32(data, groupOffset + 8);

            for (uint cp = startCharCode; cp <= endCharCode && cp <= 0x10FFFF; cp++)
            {
                ushort glyphId = (ushort)(startGlyphId + (cp - startCharCode));
                if (glyphId != 0)
                    result[(int)cp] = glyphId;
            }
        }
    }

    /// <summary>
    /// Reads the legacy family name (name ID 1, e.g. "Calibri Light") of the first
    /// font in a TrueType/OpenType file or collection. Null when absent.
    /// </summary>
    internal static string? ReadFamilyName(byte[] data)
    {
        // A collection ('ttcf') lists font offsets after its 12-byte header; table
        // offsets inside each font are file-absolute, so only the directory moves.
        int fontOffset = Encoding.ASCII.GetString(data, 0, 4) == "ttcf" ? (int)ReadUInt32(data, 12) : 0;
        int numTables = ReadUInt16(data, fontOffset + 4);
        for (int i = 0; i < numTables; i++)
        {
            int entryOffset = fontOffset + 12 + i * 16;
            if (Encoding.ASCII.GetString(data, entryOffset, 4) == "name")
                return ParseName(data, (int)ReadUInt32(data, entryOffset + 8), nameId: 1);
        }
        return null;
    }

    private static string? ParseName(byte[] data, int nameOffset, int nameId)
    {
        int count = ReadUInt16(data, nameOffset + 2);
        int stringOffset = ReadUInt16(data, nameOffset + 4);
        string? firstWindowsMatch = null;

        for (int i = 0; i < count; i++)
        {
            int recordOffset = nameOffset + 6 + i * 12;
            int platformId = ReadUInt16(data, recordOffset);
            int encodingId = ReadUInt16(data, recordOffset + 2);
            int languageId = ReadUInt16(data, recordOffset + 4);
            int recordNameId = ReadUInt16(data, recordOffset + 6);
            int length = ReadUInt16(data, recordOffset + 8);
            int offset = ReadUInt16(data, recordOffset + 10);

            // Platform 3 encoding 1 = Windows Unicode BMP. Prefer en-US (0x0409):
            // records are sorted by language, so a localized name can come first.
            if (recordNameId == nameId && platformId == 3 && encodingId == 1)
            {
                int strStart = nameOffset + stringOffset + offset;
                if (strStart + length <= data.Length)
                {
                    var name = Encoding.BigEndianUnicode.GetString(data, strStart, length);
                    if (languageId == 0x0409) return name;
                    firstWindowsMatch ??= name;
                }
            }
        }
        if (firstWindowsMatch is not null) return firstWindowsMatch;

        // Fallback: try platform 1 (Macintosh)
        for (int i = 0; i < count; i++)
        {
            int recordOffset = nameOffset + 6 + i * 12;
            int platformId = ReadUInt16(data, recordOffset);
            int recordNameId = ReadUInt16(data, recordOffset + 6);
            int length = ReadUInt16(data, recordOffset + 8);
            int offset = ReadUInt16(data, recordOffset + 10);

            if (recordNameId == nameId && platformId == 1)
            {
                int strStart = nameOffset + stringOffset + offset;
                if (strStart + length <= data.Length)
                {
                    return Encoding.ASCII.GetString(data, strStart, length);
                }
            }
        }

        return null;
    }

    // ── Binary reading helpers ──────────────────────────────────────────

    private static ushort ReadUInt16(byte[] data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    private static short ReadInt16(byte[] data, int offset) =>
        (short)((data[offset] << 8) | data[offset + 1]);

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
}
