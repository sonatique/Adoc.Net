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
    public byte[] FontData { get; }

    // Maps Unicode code point -> glyph ID
    private readonly Dictionary<int, ushort> _cmapTable;

    // Maps glyph ID -> advance width (in font units)
    private readonly Dictionary<ushort, int> _glyphWidths;

    private TrueTypeFont(string fontName, int unitsPerEm, int ascender, int descender,
        byte[] fontData, Dictionary<int, ushort> cmapTable, Dictionary<ushort, int> glyphWidths)
    {
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
    public float MeasureText(string text, float fontSize)
    {
        float totalWidth = 0;
        foreach (var ch in text)
        {
            var gid = GetGlyphId(ch);
            totalWidth += GetGlyphWidth(gid) * fontSize / UnitsPerEm;
        }
        return totalWidth;
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
        if (tables.TryGetValue("hhea", out var hheaTable))
        {
            ascender = ReadInt16(data, hheaTable.Offset + 4);
            descender = ReadInt16(data, hheaTable.Offset + 6);
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
            fontName = ParsePostScriptName(data, nameTable.Offset) ?? fontName;
        }

        return new TrueTypeFont(fontName, unitsPerEm, ascender, descender, data, cmapTable2, glyphWidths);
    }

    private static void ParseCmap(byte[] data, int cmapOffset, Dictionary<int, ushort> result)
    {
        int numSubtables = ReadUInt16(data, cmapOffset + 2);

        for (int i = 0; i < numSubtables; i++)
        {
            int recordOffset = cmapOffset + 4 + i * 8;
            int platformId = ReadUInt16(data, recordOffset);
            int encodingId = ReadUInt16(data, recordOffset + 2);
            int subtableOffset = (int)ReadUInt32(data, recordOffset + 4);

            // Look for platform 3 (Windows), encoding 1 (Unicode BMP)
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

        // Fallback: try platform 0 (Unicode), any encoding with format 4
        for (int i = 0; i < numSubtables; i++)
        {
            int recordOffset = cmapOffset + 4 + i * 8;
            int platformId = ReadUInt16(data, recordOffset);
            int subtableOffset = (int)ReadUInt32(data, recordOffset + 4);

            if (platformId == 0)
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

    private static string? ParsePostScriptName(byte[] data, int nameOffset)
    {
        int count = ReadUInt16(data, nameOffset + 2);
        int stringOffset = ReadUInt16(data, nameOffset + 4);

        for (int i = 0; i < count; i++)
        {
            int recordOffset = nameOffset + 6 + i * 12;
            int platformId = ReadUInt16(data, recordOffset);
            int encodingId = ReadUInt16(data, recordOffset + 2);
            int nameId = ReadUInt16(data, recordOffset + 6);
            int length = ReadUInt16(data, recordOffset + 8);
            int offset = ReadUInt16(data, recordOffset + 10);

            // nameID 6 = PostScript name, platform 3 encoding 1 = Windows Unicode BMP
            if (nameId == 6 && platformId == 3 && encodingId == 1)
            {
                int strStart = nameOffset + stringOffset + offset;
                if (strStart + length <= data.Length)
                {
                    return Encoding.BigEndianUnicode.GetString(data, strStart, length);
                }
            }
        }

        // Fallback: try platform 1 (Macintosh)
        for (int i = 0; i < count; i++)
        {
            int recordOffset = nameOffset + 6 + i * 12;
            int platformId = ReadUInt16(data, recordOffset);
            int nameId = ReadUInt16(data, recordOffset + 6);
            int length = ReadUInt16(data, recordOffset + 8);
            int offset = ReadUInt16(data, recordOffset + 10);

            if (nameId == 6 && platformId == 1)
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
