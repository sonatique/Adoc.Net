using System.Text;

namespace AdocNet.Converters.Pdf;

/// <summary>
/// Creates a subset TrueType font containing only the glyphs used in a document.
/// Reduces embedded font size from ~50-500KB to ~5-50KB.
/// </summary>
internal static class TrueTypeSubsetter
{
    /// <summary>
    /// Result of subsetting: the font data and the old→new glyph ID mapping.
    /// </summary>
    internal readonly record struct SubsetResult(byte[] FontData, Dictionary<ushort, ushort> OldToNewGlyphIds);

    /// <summary>
    /// Creates a subset font containing only the glyphs mapped to the given code points.
    /// Glyph 0 (.notdef) is always included. Returns both the font data and the glyph ID mapping.
    /// </summary>
    internal static SubsetResult Subset(TrueTypeFont font, HashSet<int> usedCodePoints)
    {
        var data = font.FontData;

        // Collect used glyph IDs (always include glyph 0 = .notdef)
        var usedGlyphIds = new SortedSet<ushort> { 0 };
        foreach (var cp in usedCodePoints)
        {
            var gid = font.GetGlyphId(cp);
            if (gid != 0)
                usedGlyphIds.Add(gid);
        }

        // Parse table directory
        var tables = ParseTableDirectory(data);

        // Read loca format from head table (0 = short, 1 = long)
        int locaFormat = 0;
        if (tables.TryGetValue("head", out var headEntry))
            locaFormat = ReadInt16(data, headEntry.Offset + 50);

        // Read glyph offsets from loca table
        int numGlyphs = 0;
        if (tables.TryGetValue("maxp", out var maxpEntry))
            numGlyphs = ReadUInt16(data, maxpEntry.Offset + 4);

        var glyphOffsets = ReadLocaTable(data, tables, locaFormat, numGlyphs);

        // Resolve composite glyph dependencies
        if (tables.TryGetValue("glyf", out var glyfEntry))
            ResolveCompositeGlyphs(data, glyfEntry.Offset, glyphOffsets, usedGlyphIds);

        // Build glyph ID mapping: old ID → new ID (contiguous)
        var oldToNew = new Dictionary<ushort, ushort>();
        ushort newId = 0;
        foreach (var gid in usedGlyphIds)
        {
            oldToNew[gid] = newId++;
        }

        // Build the subset font
        byte[] fontData = BuildSubsetFont(data, tables, glyphOffsets, usedGlyphIds, oldToNew, locaFormat, font);
        return new SubsetResult(fontData, oldToNew);
    }

    private static Dictionary<string, (int Offset, int Length)> ParseTableDirectory(byte[] data)
    {
        int numTables = ReadUInt16(data, 4);
        var tables = new Dictionary<string, (int Offset, int Length)>();
        for (int i = 0; i < numTables; i++)
        {
            int entryOffset = 12 + i * 16;
            string tag = Encoding.ASCII.GetString(data, entryOffset, 4);
            int tableOffset = (int)ReadUInt32(data, entryOffset + 8);
            int tableLength = (int)ReadUInt32(data, entryOffset + 12);
            tables[tag] = (tableOffset, tableLength);
        }
        return tables;
    }

    private static int[] ReadLocaTable(byte[] data, Dictionary<string, (int Offset, int Length)> tables,
        int locaFormat, int numGlyphs)
    {
        var offsets = new int[numGlyphs + 1];
        if (!tables.TryGetValue("loca", out var locaEntry))
            return offsets;

        for (int i = 0; i <= numGlyphs; i++)
        {
            offsets[i] = locaFormat == 0
                ? ReadUInt16(data, locaEntry.Offset + i * 2) * 2  // short format: offset / 2
                : (int)ReadUInt32(data, locaEntry.Offset + i * 4); // long format
        }
        return offsets;
    }

    private static void ResolveCompositeGlyphs(byte[] data, int glyfOffset, int[] glyphOffsets,
        SortedSet<ushort> usedGlyphIds)
    {
        // Iterate until no new glyphs are discovered
        var toCheck = new Queue<ushort>(usedGlyphIds);
        var visited = new HashSet<ushort>(usedGlyphIds);

        while (toCheck.Count > 0)
        {
            var gid = toCheck.Dequeue();
            if (gid >= glyphOffsets.Length - 1) continue;

            int offset = glyfOffset + glyphOffsets[gid];
            int nextOffset = glyfOffset + glyphOffsets[gid + 1];
            if (offset >= nextOffset) continue; // empty glyph

            short numberOfContours = ReadInt16(data, offset);
            if (numberOfContours >= 0) continue; // simple glyph, not composite

            // Composite glyph: parse component glyph IDs
            int pos = offset + 10; // skip header (numberOfContours + xMin + yMin + xMax + yMax)
            while (pos + 4 <= data.Length)
            {
                ushort flags = ReadUInt16(data, pos);
                ushort componentGlyphId = ReadUInt16(data, pos + 2);
                pos += 4;

                if (componentGlyphId != 0 && visited.Add(componentGlyphId))
                {
                    usedGlyphIds.Add(componentGlyphId);
                    toCheck.Enqueue(componentGlyphId);
                }

                // Skip arguments based on flags
                if ((flags & 0x0001) != 0) pos += 4; // ARG_1_AND_2_ARE_WORDS
                else pos += 2;

                if ((flags & 0x0008) != 0) pos += 2;       // WE_HAVE_A_SCALE
                else if ((flags & 0x0040) != 0) pos += 4;  // WE_HAVE_AN_X_AND_Y_SCALE
                else if ((flags & 0x0080) != 0) pos += 8;  // WE_HAVE_A_TWO_BY_TWO

                if ((flags & 0x0020) == 0) break; // MORE_COMPONENTS flag not set
            }
        }
    }

    private static byte[] BuildSubsetFont(byte[] data, Dictionary<string, (int Offset, int Length)> tables,
        int[] glyphOffsets, SortedSet<ushort> usedGlyphIds, Dictionary<ushort, ushort> oldToNew,
        int locaFormat, TrueTypeFont font)
    {
        int newNumGlyphs = usedGlyphIds.Count;

        // Build new glyf table
        using var glyfStream = new MemoryStream();
        var newLocaOffsets = new List<int>();

        if (tables.TryGetValue("glyf", out var glyfEntry))
        {
            foreach (var gid in usedGlyphIds)
            {
                newLocaOffsets.Add((int)glyfStream.Position);
                if (gid < glyphOffsets.Length - 1)
                {
                    int start = glyfEntry.Offset + glyphOffsets[gid];
                    int end = glyfEntry.Offset + glyphOffsets[gid + 1];
                    int length = end - start;
                    if (length > 0 && start + length <= data.Length)
                        glyfStream.Write(data, start, length);
                }
                // Pad to 4-byte boundary
                while (glyfStream.Position % 4 != 0)
                    glyfStream.WriteByte(0);
            }
        }
        newLocaOffsets.Add((int)glyfStream.Position);
        byte[] newGlyf = glyfStream.ToArray();

        // Build new loca table (always use long format for simplicity)
        byte[] newLoca = new byte[newLocaOffsets.Count * 4];
        for (int i = 0; i < newLocaOffsets.Count; i++)
            WriteUInt32(newLoca, i * 4, (uint)newLocaOffsets[i]);

        // Build new hmtx table (4 bytes per glyph: advanceWidth + lsb)
        byte[] newHmtx = new byte[newNumGlyphs * 4];
        if (tables.TryGetValue("hmtx", out var hmtxEntry))
        {
            int idx = 0;
            foreach (var gid in usedGlyphIds)
            {
                int srcOffset = hmtxEntry.Offset + gid * 4;
                if (srcOffset + 4 <= data.Length)
                {
                    Array.Copy(data, srcOffset, newHmtx, idx * 4, 4);
                }
                idx++;
            }
        }

        // Copy head table, update indexToLocFormat to 1 (long)
        byte[] newHead = CopyTable(data, tables, "head");
        if (newHead.Length >= 52)
        {
            newHead[50] = 0;
            newHead[51] = 1; // indexToLocFormat = 1 (long)
        }

        // Copy hhea table, update numberOfHMetrics
        byte[] newHhea = CopyTable(data, tables, "hhea");
        if (newHhea.Length >= 36)
            WriteUInt16(newHhea, 34, (ushort)newNumGlyphs);

        // Copy maxp table, update numGlyphs
        byte[] newMaxp = CopyTable(data, tables, "maxp");
        if (newMaxp.Length >= 6)
            WriteUInt16(newMaxp, 4, (ushort)newNumGlyphs);

        // Copy other tables as-is
        byte[] newOs2 = CopyTable(data, tables, "OS/2");
        byte[] newName = CopyTable(data, tables, "name");
        byte[] newPost = CopyTable(data, tables, "post");
        byte[] newCvt = CopyTable(data, tables, "cvt ");
        byte[] newFpgm = CopyTable(data, tables, "fpgm");
        byte[] newPrep = CopyTable(data, tables, "prep");

        // Assemble the subset font
        var outputTables = new List<(string Tag, byte[] Data)>();
        if (newHead.Length > 0) outputTables.Add(("head", newHead));
        if (newHhea.Length > 0) outputTables.Add(("hhea", newHhea));
        if (newMaxp.Length > 0) outputTables.Add(("maxp", newMaxp));
        if (newOs2.Length > 0) outputTables.Add(("OS/2", newOs2));
        if (newName.Length > 0) outputTables.Add(("name", newName));
        outputTables.Add(("hmtx", newHmtx));
        outputTables.Add(("loca", newLoca));
        outputTables.Add(("glyf", newGlyf));
        if (newPost.Length > 0) outputTables.Add(("post", newPost));
        if (newCvt.Length > 0) outputTables.Add(("cvt ", newCvt));
        if (newFpgm.Length > 0) outputTables.Add(("fpgm", newFpgm));
        if (newPrep.Length > 0) outputTables.Add(("prep", newPrep));

        return AssembleTtf(outputTables);
    }

    private static byte[] CopyTable(byte[] data, Dictionary<string, (int Offset, int Length)> tables, string tag)
    {
        if (!tables.TryGetValue(tag, out var entry)) return [];
        if (entry.Offset + entry.Length > data.Length) return [];
        var result = new byte[entry.Length];
        Array.Copy(data, entry.Offset, result, 0, entry.Length);
        return result;
    }

    private static byte[] AssembleTtf(List<(string Tag, byte[] Data)> tables)
    {
        int numTables = tables.Count;

        // Calculate searchRange, entrySelector, rangeShift
        int searchRange = 1;
        int entrySelector = 0;
        while (searchRange * 2 <= numTables)
        {
            searchRange *= 2;
            entrySelector++;
        }
        searchRange *= 16;
        int rangeShift = numTables * 16 - searchRange;

        // Header (12 bytes) + table directory (16 bytes per table)
        int headerSize = 12 + numTables * 16;
        int totalSize = headerSize;
        foreach (var (_, tableData) in tables)
            totalSize += (tableData.Length + 3) & ~3; // pad to 4 bytes

        var output = new byte[totalSize];

        // Offset table
        WriteUInt32(output, 0, 0x00010000); // sfVersion
        WriteUInt16(output, 4, (ushort)numTables);
        WriteUInt16(output, 6, (ushort)searchRange);
        WriteUInt16(output, 8, (ushort)entrySelector);
        WriteUInt16(output, 10, (ushort)rangeShift);

        // Write table directory and data
        int dataOffset = headerSize;
        for (int i = 0; i < numTables; i++)
        {
            var (tag, tableData) = tables[i];
            int dirOffset = 12 + i * 16;

            // Tag
            var tagBytes = Encoding.ASCII.GetBytes(tag.PadRight(4));
            Array.Copy(tagBytes, 0, output, dirOffset, 4);

            // Checksum (simplified — not critical for PDF embedding)
            uint checksum = ComputeTableChecksum(tableData);
            WriteUInt32(output, dirOffset + 4, checksum);

            // Offset and length
            WriteUInt32(output, dirOffset + 8, (uint)dataOffset);
            WriteUInt32(output, dirOffset + 12, (uint)tableData.Length);

            // Copy table data
            Array.Copy(tableData, 0, output, dataOffset, tableData.Length);
            dataOffset += (tableData.Length + 3) & ~3;
        }

        return output;
    }

    private static uint ComputeTableChecksum(byte[] data)
    {
        uint sum = 0;
        int len = (data.Length + 3) & ~3;
        for (int i = 0; i < len; i += 4)
        {
            uint val = 0;
            for (int j = 0; j < 4 && i + j < data.Length; j++)
                val = (val << 8) | data[i + j];
            sum += val;
        }
        return sum;
    }

    // ── Binary helpers ──────────────────────────────────────────────────

    private static ushort ReadUInt16(byte[] data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    private static short ReadInt16(byte[] data, int offset) =>
        (short)((data[offset] << 8) | data[offset + 1]);

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);

    private static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }
}
