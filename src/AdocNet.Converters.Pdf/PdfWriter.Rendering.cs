using System.Globalization;
using System.Text;

namespace AdocNet.Converters.Pdf;

internal sealed partial class PdfWriter
{
    internal float WriteWrappedVerbatimText(string text, string font, float fontSize, float leading)
    {
        float consumed = 0;
        float charWidth = MeasureText("M", font, fontSize); // monospace: all chars same width
        int charsPerLine = Math.Max(1, (int)(ContentWidth / charWidth));

        // Empty line: still consume vertical space (blank line in code block)
        if (text.Length == 0)
        {
            EnsurePage();
            ReserveFirstLineLeading(leading);
            DrawCodeLineBackground();
            _cursorY -= leading;
            return leading;
        }

        int pos = 0;
        while (pos < text.Length)
        {
            int remaining = text.Length - pos;
            int lineLen = Math.Min(remaining, charsPerLine);
            int skip = 0;
            // Themed output wraps code like Prawn: at the last space that fits (the space is
            // dropped), falling back to a hard break inside a token longer than the line.
            if (VerbatimWrapAtSpaces && lineLen < remaining)
            {
                int space = text.LastIndexOf(' ', pos + lineLen, lineLen + 1);
                if (space > pos)
                {
                    lineLen = space - pos;
                    skip = 1;
                }
            }
            string line = text.Substring(pos, lineLen);

            EnsurePage();
            ReserveFirstLineLeading(leading);
            DrawCodeLineBackground();
            WriteText(line, font, fontSize, MarginLeftValue, _cursorY);
            _cursorY -= leading;
            consumed += leading;
            pos += lineLen + skip;
        }
        return consumed;
    }

    /// <summary>
    /// Returns the number of wrapped lines a verbatim text line would produce at the given font size.
    /// </summary>
    internal int CountVerbatimLines(string text, string font, float fontSize)
    {
        float charWidth = MeasureText("M", font, fontSize);
        int charsPerLine = Math.Max(1, (int)(ContentWidth / charWidth));
        if (text.Length == 0) return 1;
        return (text.Length + charsPerLine - 1) / charsPerLine;
    }

    internal void MoveCursor(float dy) => _cursorY -= dy;

    // ── Drawing operations ──────────────────────────────────────────────

    internal void DrawLine(float x1, float y1, float x2, float y2, float lineWidth = 0.5f)
    {
        Ink(Math.Max(y1, y2));
        _currentStream!.Append($"{Fmt(lineWidth)} w\n");
        _currentStream.Append($"{Fmt(x1)} {Fmt(y1)} m {Fmt(x2)} {Fmt(y2)} l S\n");
    }

    internal void DrawRect(float x, float y, float w, float h, bool fill = false)
    {
        Ink(y + h);
        _currentStream!.Append($"{Fmt(x)} {Fmt(y)} {Fmt(w)} {Fmt(h)} re ");
        _currentStream.Append(fill ? "f\n" : "S\n");
    }

    /// <summary>
    /// Draws a rounded rectangle using cubic Bezier curves for corners.
    /// </summary>
    internal void DrawRoundedRect(float x, float y, float w, float h, float r, string mode = "S")
    {
        // Clamp radius to half the smaller dimension
        Ink(y + h);
        r = Math.Min(r, Math.Min(w / 2, h / 2));
        // Bezier control point offset for quarter-circle approximation
        float k = r * 0.5523f;

        var s = _currentStream!;
        // Start at bottom-left, just above the corner
        s.Append($"{Fmt(x)} {Fmt(y + r)} m\n");
        // Bottom-left corner
        s.Append($"{Fmt(x)} {Fmt(y + r - k)} {Fmt(x + r - k)} {Fmt(y)} {Fmt(x + r)} {Fmt(y)} c\n");
        // Bottom edge
        s.Append($"{Fmt(x + w - r)} {Fmt(y)} l\n");
        // Bottom-right corner
        s.Append($"{Fmt(x + w - r + k)} {Fmt(y)} {Fmt(x + w)} {Fmt(y + r - k)} {Fmt(x + w)} {Fmt(y + r)} c\n");
        // Right edge
        s.Append($"{Fmt(x + w)} {Fmt(y + h - r)} l\n");
        // Top-right corner
        s.Append($"{Fmt(x + w)} {Fmt(y + h - r + k)} {Fmt(x + w - r + k)} {Fmt(y + h)} {Fmt(x + w - r)} {Fmt(y + h)} c\n");
        // Top edge
        s.Append($"{Fmt(x + r)} {Fmt(y + h)} l\n");
        // Top-left corner
        s.Append($"{Fmt(x + r - k)} {Fmt(y + h)} {Fmt(x)} {Fmt(y + h - r + k)} {Fmt(x)} {Fmt(y + h - r)} c\n");
        // Close and paint
        s.Append($"h {mode}\n");
    }

    /// <summary>
    /// Draws a circle centered at (cx, cy) with the given radius using cubic Bezier
    /// approximation. Mode: "S" stroke, "f" fill, "B" both. Used for admonition icon
    /// glyphs in the PDF asciidoctor theme.
    /// </summary>
    internal void DrawCircle(float cx, float cy, float r, string mode = "f")
    {
        Ink(cy + r);
        // 4-segment Bezier circle approximation. Magic constant ≈ 0.5523.
        float k = r * 0.5523f;
        var s = _currentStream!;
        s.Append($"{Fmt(cx - r)} {Fmt(cy)} m\n");
        s.Append($"{Fmt(cx - r)} {Fmt(cy + k)} {Fmt(cx - k)} {Fmt(cy + r)} {Fmt(cx)} {Fmt(cy + r)} c\n");
        s.Append($"{Fmt(cx + k)} {Fmt(cy + r)} {Fmt(cx + r)} {Fmt(cy + k)} {Fmt(cx + r)} {Fmt(cy)} c\n");
        s.Append($"{Fmt(cx + r)} {Fmt(cy - k)} {Fmt(cx + k)} {Fmt(cy - r)} {Fmt(cx)} {Fmt(cy - r)} c\n");
        s.Append($"{Fmt(cx - k)} {Fmt(cy - r)} {Fmt(cx - r)} {Fmt(cy - k)} {Fmt(cx - r)} {Fmt(cy)} c\n");
        s.Append($"h {mode}\n");
    }

    internal void SetFillColor(float r, float g, float b)
    {
        _fill = new PdfColor(r, g, b);
        _currentStream!.Append($"{FmtColor(r)} {FmtColor(g)} {FmtColor(b)} rg\n");
    }

    internal void SetStrokeColor(float r, float g, float b)
    {
        _currentStream!.Append($"{FmtColor(r)} {FmtColor(g)} {FmtColor(b)} RG\n");
    }

    // ── Link annotations ─────────────────────────────────────────────────

    internal void AddLinkAnnotation(float x, float y, float width, float height, string uri)
    {
        _currentAnnotations.Add(new PdfAnnotation(x, y, width, height, uri));
    }

    /// <summary>
    /// Adds an internal GoTo link annotation for cross-references within the document.
    /// </summary>
    internal void AddInternalLinkAnnotation(float x, float y, float width, float height, string destinationId)
    {
        _currentInternalLinks.Add(new PdfInternalLink(x, y, width, height, destinationId));
    }

    // ── Outline / bookmark support ──────────────────────────────────────

    /// <summary>
    /// Registers a section heading as an outline/bookmark entry.
    /// Called during rendering when a section heading is encountered.
    /// </summary>
    internal void AddOutlineEntry(string title, int level, string? id)
    {
        int pageIndex = _currentPageNumber - 1; // 0-based
        float y = _cursorY;
        _outlineEntries.Add(new OutlineEntry(title, level, pageIndex, y));

        // Also register as a named destination for cross-references
        if (id is not null)
            _namedDestinations[id] = (pageIndex, y);
    }

    /// <summary>
    /// Registers a named destination at the current cursor position.
    /// Used for anchors and cross-reference targets.
    /// </summary>
    internal void AddNamedDestination(string id)
    {
        _namedDestinations[id] = (_currentPageNumber - 1, _cursorY);
    }

    /// <summary>
    /// Registers a named destination on the current page at an explicit vertical
    /// position (rather than the cursor). Used to anchor a destination at a
    /// segment's rendered baseline — e.g. a footnote marker so the footnote
    /// entry can link back to it (issue #64). The first registration for an id
    /// wins, so a footnote referenced multiple times links back to its first
    /// occurrence (matching asciidoctor-pdf).
    /// </summary>
    internal void AddNamedDestination(string id, float y)
    {
        if (!_namedDestinations.ContainsKey(id))
            _namedDestinations[id] = (_currentPageNumber - 1, y);
    }

    /// <summary>
    /// Returns the 1-based page number where the named destination was registered,
    /// or null if no destination with that id exists. Used by the TOC renderer
    /// to fill in page numbers after content rendering completes.
    /// </summary>
    internal int? GetDestinationPage(string id) =>
        _namedDestinations.TryGetValue(id, out var dest) ? dest.PageIndex + 1 : null;

    /// <summary>
    /// Pre-seeds the named-destinations dictionary from a previous (discarded)
    /// render pass. Used for two-pass TOC rendering: pass 1 collects section
    /// page numbers, pass 2 reads them when emitting the TOC. The seed is
    /// applied before any content renders, so subsequent AddOutlineEntry /
    /// AddNamedDestination calls overwrite the seeded values with the
    /// authoritative pass-2 positions.
    /// </summary>
    internal void SeedDestinations(IReadOnlyDictionary<string, int> firstPassPages)
    {
        foreach (var kvp in firstPassPages)
            _namedDestinations[kvp.Key] = (kvp.Value - 1, 0f);
    }

    /// <summary>
    /// Captures a snapshot of the current id → 1-based-page map for use by a
    /// two-pass renderer.
    /// </summary>
    internal Dictionary<string, int> CaptureDestinationPages()
    {
        var snapshot = new Dictionary<string, int>(_namedDestinations.Count, StringComparer.Ordinal);
        foreach (var kvp in _namedDestinations)
            snapshot[kvp.Key] = kvp.Value.PageIndex + 1;
        return snapshot;
    }

    // ── Image embedding ────────────────────────────────────────────────

    /// <summary>
    /// Embeds an image as a PDF XObject and returns the image reference name (e.g. "Im1").
    /// The image is automatically added to the current page's resources.
    /// </summary>
    internal string EmbedImage(ImageParser.ImageInfo image)
    {
        string colorSpace = image.Components switch
        {
            1 => "/DeviceGray",
            3 => "/DeviceRGB",
            4 => "/DeviceCMYK",
            _ => "/DeviceRGB"
        };

        string filter = image.Format == ImageParser.ImageFormat.Jpeg ? "/DCTDecode" : "/FlateDecode";

        // Create SMask for alpha channel if present
        int? smaskId = null;
        if (image.AlphaData is not null)
        {
            string smaskHeader =
                $"<< /Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} " +
                $"/ColorSpace /DeviceGray /BitsPerComponent {image.BitsPerComponent} /Filter /FlateDecode " +
                $"/Length {image.AlphaData.Length} >>";
            smaskId = AllocObject(new PdfObject(smaskHeader, image.AlphaData));
        }

        // Build image XObject header
        string smaskEntry = smaskId.HasValue ? $"/SMask {smaskId.Value} 0 R " : "";
        string imageHeader =
            $"<< /Type /XObject /Subtype /Image /Width {image.Width} /Height {image.Height} " +
            $"/ColorSpace {colorSpace} /BitsPerComponent {image.BitsPerComponent} /Filter {filter} " +
            $"{smaskEntry}/Length {image.Data.Length} >>";

        int imageObjId = AllocObject(new PdfObject(imageHeader, image.Data));

        _imageCounter++;
        string imageRef = $"Im{_imageCounter}";
        _currentPageImages[imageRef] = imageObjId;

        return imageRef;
    }

    /// <summary>
    /// Draws a previously embedded image at the given position and size.
    /// </summary>
    internal void DrawImage(string imageRef, float x, float y, float width, float height)
    {
        Ink(y + height);
        _currentStream!.Append("q\n");
        _currentStream.Append($"{Fmt(width)} 0 0 {Fmt(height)} {Fmt(x)} {Fmt(y)} cm\n");
        _currentStream.Append($"/{imageRef} Do\n");
        _currentStream.Append("Q\n");
    }

    // ── SVG rendering ────────────────────────────────────────────────────

    /// <summary>
    /// Draws a parsed SVG document as vector graphics at the given position and size.
    /// SVG shapes are rendered as filled PDF paths.
    /// </summary>
    internal void DrawSvg(SvgParser.SvgDocument svg, float x, float y, float width, float height)
    {
        float scaleX = width / svg.ViewBoxWidth;
        float scaleY = height / svg.ViewBoxHeight;
        Ink(y + height);
        if (Recorder is { Capturing: true } recorder)
            recorder.Add(_currentPageNumber, new PdfPageImage(null,
                svg.Shapes.Select(s => new PdfPageShape(s.Fill, s.PathData)).ToList(),
                svg.ViewBoxWidth, svg.ViewBoxHeight, x, _pageHeight - y - height, width, height));
        var savedFill = _fill;

        _currentStream!.Append("q\n"); // Save graphics state

        foreach (var shape in svg.Shapes)
        {

            // Set fill color
            if (shape.Fill is { } fill)
                SetFillColor(fill.R, fill.G, fill.B);
            else
                SetFillColor(0, 0, 0); // Default to black

            var pathOps = SvgParser.ToPdfPathOps(shape.PathData, scaleX, scaleY,
                x, y, svg.ViewBoxHeight);
            _currentStream.Append(pathOps);
            _currentStream.Append("f\n"); // Fill the path
        }

        _currentStream.Append("Q\n"); // Restore graphics state
        _fill = savedFill;
    }

    // ── Text measurement ────────────────────────────────────────────────

    internal float MeasureText(string text, string font, float fontSize)
    {
        // Account for any characters the primary font can't show that are routed
        // to a fallback font (#52) — each run is measured in its own font. The
        // fast path (no fallback) returns null and measures the string as-is.
        var runs = SplitFontRuns(text, font);
        if (runs is null)
            return MeasureRaw(text, font, fontSize);

        float total = 0;
        foreach (var (runText, runFont) in runs)
            total += MeasureRaw(runText, runFont, fontSize);
        return total;
    }

    private float MeasureRaw(string text, string font, float fontSize)
    {
        if (_embeddedFonts.TryGetValue(font, out var ttFont))
        {
            return ttFont.MeasureText(text, fontSize, Kerning);
        }

        return MeasureStandardText(text, font, fontSize);
    }

    internal static float MeasureStandardText(string text, string font, float fontSize)
    {
        // Courier and all variants (Bold/Oblique/BoldOblique) — monospace
        if (font == "F4" || font == "F5" || font == "F6" || font == "F7")
            return text.Length * fontSize * HelveticaMetrics.CourierWidth;

        float total = 0;
        foreach (var ch in text)
            total += HelveticaMetrics.MeasureChar(ch, font, fontSize);

        return total;
    }

    // ── Final PDF assembly ──────────────────────────────────────────────

    internal byte[] ToBytes()
    {
        // Ensure last page is finished
        if (_currentStream is not null)
            FinishPage();

        // Running content needs the final page count; write it before the fonts are subset.
        WriteDeferredRunningContent();

        // Embed registered TrueType fonts (fills placeholder objects)
        EmbedRegisteredFonts();

        // Resolve deferred internal links (cross-references)
        ResolveInternalLinks();

        // Build Pages object
        var kidsStr = string.Join(" ", _pageObjectIds.Select(id => $"{id} 0 R"));
        int pagesId = AllocObject(new PdfObject(
            $"<< /Type /Pages /Kids [{kidsStr}] /Count {_pageObjectIds.Count} >>"));

        // Patch page parent references
        foreach (var pageId in _pageObjectIds)
        {
            var obj = _objects[pageId - 1];
            SetObject(pageId, new PdfObject(obj.Content.Replace("{PAGES}", $"{pagesId} 0 R"), obj.BinaryStream));
        }

        // Build outline/bookmark tree
        int? outlineId = BuildOutlineTree();

        // Build named destinations dictionary for cross-references
        int? namesId = BuildNamesDictionary();

        // Build page labels (logical page numbers shown in PDF reader page panel)
        int? pageLabelsId = BuildPageLabels();

        // Catalog
        var catalogSb = new StringBuilder();
        catalogSb.Append($"<< /Type /Catalog /Pages {pagesId} 0 R");
        if (outlineId.HasValue)
            catalogSb.Append($" /Outlines {outlineId.Value} 0 R /PageMode /UseOutlines");
        if (namesId.HasValue)
            catalogSb.Append($" /Names {namesId.Value} 0 R");
        if (pageLabelsId.HasValue)
            catalogSb.Append($" /PageLabels {pageLabelsId.Value} 0 R");
        if (_pageObjectIds.Count > 0)
            catalogSb.Append($" /OpenAction [{_pageObjectIds[0]} 0 R /FitH {Fmt(_pageHeight)}]");
        catalogSb.Append(" /ViewerPreferences << /DisplayDocTitle true >>");
        catalogSb.Append(" >>");
        int catalogId = AllocObject(new PdfObject(catalogSb.ToString()));

        // Info dictionary (deterministic) — include document title if set
        var infoDict = "<< /Producer (AdocNet PDF Renderer) /CreationDate (D:20260101000000+00'00')";
        if ((MetadataTitle ?? DocumentTitle) is { } infoTitle)
            infoDict += $" /Title {PdfTextString(infoTitle)}";
        infoDict += " >>";
        int infoId = AllocObject(new PdfObject(infoDict));

        // Serialize
        using var ms = new MemoryStream();
        ms.Write(PdfHeader);

        var offsets = new long[_objects.Count];
        for (int i = 0; i < _objects.Count; i++)
        {
            offsets[i] = ms.Position;
            var obj = _objects[i];

            if (obj.BinaryStream is not null)
            {
                var header = $"{i + 1} 0 obj\n{obj.Content}\nstream\n";
                ms.Write(Encoding.ASCII.GetBytes(header));
                ms.Write(obj.BinaryStream);
                ms.Write(Encoding.ASCII.GetBytes("\nendstream\nendobj\n"));
            }
            else
            {
                var line = $"{i + 1} 0 obj\n{obj.Content}\nendobj\n";
                ms.Write(Encoding.ASCII.GetBytes(line));
            }
        }

        // Cross-reference table
        long xrefOffset = ms.Position;
        ms.Write(Encoding.ASCII.GetBytes($"xref\n0 {_objects.Count + 1}\n"));
        ms.Write(Encoding.ASCII.GetBytes("0000000000 65535 f \n"));
        foreach (var offset in offsets)
        {
            ms.Write(Encoding.ASCII.GetBytes(
                $"{offset:D10} 00000 n \n"));
        }

        // Trailer
        ms.Write(Encoding.ASCII.GetBytes(
            $"trailer\n<< /Size {_objects.Count + 1} /Root {catalogId} 0 R /Info {infoId} 0 R >>\n" +
            $"startxref\n{xrefOffset}\n%%EOF\n"));

        return ms.ToArray();
    }

    /// <summary>
    /// Resolves deferred internal link annotations by looking up named destinations.
    /// Links to unknown destinations become no-ops (empty annotation).
    /// </summary>
    private void ResolveInternalLinks()
    {
        foreach (var deferred in _deferredInternalLinks)
        {
            var link = deferred.Link;
            if (_namedDestinations.TryGetValue(link.DestinationId, out var dest)
                && dest.PageIndex < _pageObjectIds.Count)
            {
                int targetPageObjId = _pageObjectIds[dest.PageIndex];
                SetObject(deferred.PlaceholderObjId, new PdfObject(
                    $"<< /Type /Annot /Subtype /Link " +
                    $"/Rect [{Fmt(link.X)} {Fmt(link.Y)} {Fmt(link.X + link.Width)} {Fmt(link.Y + link.Height)}] " +
                    $"/Border [0 0 0] " +
                    $"/Dest [{targetPageObjId} 0 R /XYZ 0 {Fmt(dest.Y + 10)} null] >>"));
            }
            else
            {
                // Destination not found — render as invisible annotation (no action)
                SetObject(deferred.PlaceholderObjId, new PdfObject(
                    $"<< /Type /Annot /Subtype /Link " +
                    $"/Rect [0 0 0 0] /Border [0 0 0] >>"));
            }
        }
    }

    /// <summary>
    /// Builds the PDF outline (bookmark) tree from collected outline entries.
    /// Returns the outline root object ID, or null if no entries exist.
    /// </summary>
    private int? BuildOutlineTree()
    {
        if (_outlineEntries.Count == 0 || _pageObjectIds.Count == 0)
            return null;

        // Build a nested tree from the flat list of entries by level
        var root = new OutlineEntry("Document", -1, 0, 0);
        var stack = new List<OutlineEntry> { root };

        foreach (var entry in _outlineEntries)
        {
            // Pop stack until we find a parent with lower level
            while (stack.Count > 1 && stack[^1].Level >= entry.Level)
                stack.RemoveAt(stack.Count - 1);

            stack[^1].Children.Add(entry);
            stack.Add(entry);
        }

        // Emit PDF objects for the tree
        int outlineRootId = AllocPlaceholder();
        var entryIds = new Dictionary<OutlineEntry, int>();
        AllocOutlineIds(root.Children, entryIds);

        // Fill each entry with parent, prev/next sibling, and child references
        EmitOutlineChildren(root.Children, outlineRootId, entryIds);

        // Fill the root outline object
        int totalCount = CountOutlineEntries(root.Children);
        SetObject(outlineRootId, new PdfObject(
            $"<< /Type /Outlines /First {entryIds[root.Children[0]]} 0 R " +
            $"/Last {entryIds[root.Children[^1]]} 0 R /Count {totalCount} >>"));

        return outlineRootId;
    }

    private void AllocOutlineIds(List<OutlineEntry> entries, Dictionary<OutlineEntry, int> ids)
    {
        foreach (var entry in entries)
        {
            ids[entry] = AllocPlaceholder();
            AllocOutlineIds(entry.Children, ids);
        }
    }

    private void EmitOutlineChildren(List<OutlineEntry> siblings, int parentId,
        Dictionary<OutlineEntry, int> entryIds)
    {
        for (int i = 0; i < siblings.Count; i++)
        {
            var entry = siblings[i];
            int myId = entryIds[entry];
            int pageObjId = entry.PageIndex < _pageObjectIds.Count
                ? _pageObjectIds[entry.PageIndex] : _pageObjectIds[^1];

            var sb = new StringBuilder();
            sb.Append($"<< /Title {PdfTextString(entry.Title)} ");
            sb.Append($"/Parent {parentId} 0 R ");
            sb.Append($"/Dest [{pageObjId} 0 R /XYZ 0 {Fmt(entry.Y + 10)} null] ");

            if (i > 0)
                sb.Append($"/Prev {entryIds[siblings[i - 1]]} 0 R ");
            if (i < siblings.Count - 1)
                sb.Append($"/Next {entryIds[siblings[i + 1]]} 0 R ");

            if (entry.Children.Count > 0)
            {
                int count = CountOutlineEntries(entry.Children);
                sb.Append($"/First {entryIds[entry.Children[0]]} 0 R ");
                sb.Append($"/Last {entryIds[entry.Children[^1]]} 0 R ");
                sb.Append($"/Count {count} ");
            }

            sb.Append(">>");
            SetObject(myId, new PdfObject(sb.ToString()));

            if (entry.Children.Count > 0)
                EmitOutlineChildren(entry.Children, myId, entryIds);
        }
    }

    private static int CountOutlineEntries(List<OutlineEntry> entries)
    {
        int count = 0;
        foreach (var entry in entries)
        {
            count++;
            count += CountOutlineEntries(entry.Children);
        }
        return count;
    }

    /// <summary>
    /// Builds the /Names dictionary with named destinations for cross-references.
    /// Returns the object ID of the Names dict, or null if no destinations exist.
    /// </summary>
    private int? BuildNamesDictionary()
    {
        if (_namedDestinations.Count == 0) return null;

        var sb = new StringBuilder();
        sb.Append("<< /Names [");
        foreach (var kvp in _namedDestinations.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (kvp.Value.PageIndex >= _pageObjectIds.Count) continue;
            int pageObjId = _pageObjectIds[kvp.Value.PageIndex];
            sb.Append($" ({EscapePdfString(kvp.Key)}) [{pageObjId} 0 R /XYZ 0 {Fmt(kvp.Value.Y + 10)} null]");
        }
        sb.Append(" ] >>");
        int destsId = AllocObject(new PdfObject(sb.ToString()));

        return AllocObject(new PdfObject($"<< /Dests {destsId} 0 R >>"));
    }

    /// <summary>
    /// Builds the /PageLabels number tree. Each page is labeled with its decimal page number.
    /// </summary>
    private int? BuildPageLabels()
    {
        if (_pageObjectIds.Count == 0) return null;
        // Pages before numbering starts (the title page) get lower-case roman labels (i, ii...),
        // then decimal from 1, like asciidoctor-pdf; otherwise decimal throughout.
        if (PageNumberOffset > 0 && PageNumberOffset < _pageObjectIds.Count)
            return AllocObject(new PdfObject($"<< /Nums [ 0 << /S /r >> {PageNumberOffset} << /S /D >> ] >>"));
        return AllocObject(new PdfObject("<< /Nums [ 0 << /S /D >> ] >>"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// A PDF text string (document information, outline titles): a literal string for ASCII,
    /// otherwise UTF-16BE with a byte-order mark as a hex string, so any Unicode text survives
    /// (ISO 32000-1 7.9.2.2).
    /// </summary>
    internal static string PdfTextString(string text)
    {
        bool ascii = true;
        foreach (var ch in text)
            if (ch > 0x7E || (ch < 0x20 && ch != '\t')) { ascii = false; break; }
        if (ascii) return "(" + EscapePdfString(text) + ")";
        var sb = new StringBuilder("<FEFF", 6 + text.Length * 4);
        foreach (var ch in text)
            sb.Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture)); // UTF-16 code units, surrogates included
        return sb.Append('>').ToString();
    }

    private static string EscapePdfString(string text)
    {
        var sb = new StringBuilder(text.Length);
        // Iterate by codepoint so a non-BMP character (a surrogate pair) becomes a
        // single missing-glyph indicator rather than two '?'s (issue #72).
        foreach (var cp in PdfFontEmbedder.EnumerateCodePoints(text))
        {
            if (cp == '(') sb.Append("\\(");
            else if (cp == ')') sb.Append("\\)");
            else if (cp == '\\') sb.Append("\\\\");
            else if (cp < 128)
            {
                sb.Append((char)cp);
            }
            else if (cp <= 255)
            {
                // WinAnsiEncoding: emit as octal escape
                sb.Append('\\');
                sb.Append(Convert.ToString(cp, 8).PadLeft(3, '0'));
            }
            else if (cp <= 0xFFFF)
            {
                // Outside WinAnsi range — best effort: map common Unicode chars
                sb.Append(MapUnicodeToWinAnsi((char)cp));
            }
            else
            {
                // Non-BMP, not representable in a WinAnsi base font.
                sb.Append('?');
            }
        }
        return sb.ToString();
    }

    private static string MapUnicodeToWinAnsi(char ch)
    {
        // Map common Unicode characters to WinAnsi equivalents
        return ch switch
        {
            '\u2013' => "\\226", // en dash
            '\u2014' => "\\227", // em dash
            '\u2018' => "\\221", // left single quote
            '\u2019' => "\\222", // right single quote / apostrophe
            '\u201C' => "\\223", // left double quote
            '\u201D' => "\\224", // right double quote
            '\u2022' => "\\225", // bullet
            '\u2026' => "\\205", // ellipsis
            '\u2122' => "\\231", // trademark
            '\u20AC' => "\\200", // euro sign
            _ => "?",
        };
    }

    private static string Fmt(float value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    // Colour components need ~1/255 resolution; two decimals shifted #909090 to #8f8f8f.
    private static string FmtColor(float value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    // ── Embedded font helpers ───────────────────────────────────────────

    private void TrackCodePoints(string fontKey, string text) =>
        PdfFontEmbedder.TrackCodePoints(_usedCodePoints, fontKey, text);

    private static string EncodeTextAsGlyphIds(string text, TrueTypeFont font) =>
        PdfFontEmbedder.EncodeTextAsGlyphIds(text, font);

    /// <summary>
    /// Appends a show-text operator for an embedded font: <c>&lt;glyphs&gt; Tj</c>, or with
    /// <see cref="Kerning"/> a <c>[&lt;glyphs&gt; adj &lt;glyphs&gt;] TJ</c> array carrying the 'kern'
    /// adjustments (positive numbers move the next glyph left, in 1/1000 em).
    /// </summary>
    private void AppendGlyphShow(string text, TrueTypeFont font)
    {
        if (!Kerning || !font.HasKerning || text.Length < 2)
        {
            _currentStream!.Append('<').Append(EncodeTextAsGlyphIds(text, font)).Append("> Tj\n");
            return;
        }
        var sb = _currentStream!;
        sb.Append('[');
        int start = 0;
        for (int i = 1; i < text.Length; i++)
        {
            short kern = font.GetKerning(font.GetGlyphId(text[i - 1]), font.GetGlyphId(text[i]));
            if (kern == 0) continue;
            sb.Append('<').Append(EncodeTextAsGlyphIds(text.Substring(start, i - start), font)).Append('>');
            sb.Append(' ').Append(Fmt(-kern * 1000f / font.UnitsPerEm)).Append(' ');
            start = i;
        }
        sb.Append('<').Append(EncodeTextAsGlyphIds(text.Substring(start), font)).Append(">] TJ\n");
    }

    private void EmbedRegisteredFonts() =>
        PdfFontEmbedder.EmbedFonts(_embeddedFonts, _usedCodePoints, _embeddedFontPlaceholders,
            obj => AllocObject(obj), (id, obj) => SetObject(id, obj), _monospaceFonts);

    private static byte[] Compress(byte[] data) =>
        PdfFontEmbedder.Compress(data);
}
