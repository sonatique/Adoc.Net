using AdocNet.Ast;
namespace AdocNet.Converters.Pdf;
public sealed partial class PdfRenderer
{
    /// <summary>
    /// Renders source content with syntax highlighting, colorizing tokens per line.
    /// </summary>
    private void RenderHighlightedVerbatim(PdfWriter w, string content, string language)
    {
        foreach (var line in content.Split('\n'))
        {
            w.EnsurePage();
            w.DrawCodeLineBackground();
            if (line.Length == 0) { w.MoveCursor(_codeLeading); continue; }
            var tokens = Highlighting.SyntaxTokenizer.Tokenize(line, language);
            float x = w.MarginLeftValue;
            foreach (var token in tokens)
            {
                var color = _syntaxColors!.GetColor(token.Kind);
                if (color is { } c) w.SetFillColor(c.R, c.G, c.B);
                else w.SetFillColor(0, 0, 0);
                w.WriteText(token.Text, _fontMono, _codeFontSize, x, w.CursorY);
                x += w.MeasureText(token.Text, _fontMono, _codeFontSize);
            }
            w.SetFillColor(0, 0, 0);
            w.MoveCursor(_codeLeading);
        }
    }

    private void RenderTable(PdfWriter w, TableNode table, FootnoteState footnotes)
    {
        if (table.Children.Count == 0) return;

        w.EnsurePage();

        // Calculate column count from first row
        int colCount = 0;
        if (table.Children[0] is TableRowNode firstRow)
            colCount = firstRow.Children.Count;
        if (colCount == 0) return;

        // Build column widths array (proportional)
        float[] colWidths = new float[colCount];

        // Check if column specs have varying weights (user explicitly set different sizes)
        bool hasVaryingWeights = false;
        if (table.Columns is { Count: > 0 })
        {
            int firstWeight = table.Columns[0].Width;
            foreach (var col in table.Columns)
            {
                if (col.Width != firstWeight)
                {
                    hasVaryingWeights = true;
                    break;
                }
            }
        }

        if (hasVaryingWeights && table.Columns is { Count: > 0 })
        {
            // User explicitly set different column widths — respect them
            int totalWeight = 0;
            foreach (var col in table.Columns)
                totalWeight += col.Width;
            for (int c = 0; c < colCount; c++)
            {
                int weight = c < table.Columns.Count ? table.Columns[c].Width : 1;
                colWidths[c] = w.ContentWidth * weight / totalWeight;
            }
        }
        else
        {
            // Auto-size columns using two metrics per column:
            // 1. minWidth: the longest single word (column can't be narrower)
            // 2. totalChars: total character count across all rows (text volume)
            // Columns get their minWidth first, then remaining space is distributed
            // proportionally to text volume.

            float[] minWidths = new float[colCount];
            float[] totalChars = new float[colCount];
            float cellPad = 4f;

            foreach (var child in table.Children)
            {
                if (child is TableRowNode r)
                {
                    int ci = 0;
                    foreach (var cell in r.Children)
                    {
                        if (cell is TableCellNode c && ci < colCount)
                        {
                            string text = GetPlainText(c.Inlines, c.Text);
                            totalChars[ci] += text.Length;

                            // Minimum width = longest word + padding
                            foreach (var word in text.Split(' '))
                            {
                                float ww = w.MeasureText(word, _fontRegular, _bodyFontSize) + 2 * cellPad;
                                if (ww > minWidths[ci])
                                    minWidths[ci] = ww;
                            }
                            ci += c.ColSpan;
                        }
                    }
                }
            }

            // Start each column at its minimum width
            float usedWidth = 0;
            for (int c = 0; c < colCount; c++)
            {
                colWidths[c] = minWidths[c];
                usedWidth += minWidths[c];
            }

            // Distribute remaining space proportionally to text volume
            float remaining = w.ContentWidth - usedWidth;
            if (remaining > 0)
            {
                float totalVol = totalChars.Sum();
                if (totalVol > 0)
                {
                    for (int c = 0; c < colCount; c++)
                        colWidths[c] += remaining * totalChars[c] / totalVol;
                }
                else
                {
                    for (int c = 0; c < colCount; c++)
                        colWidths[c] += remaining / colCount;
                }
            }
            else
            {
                // Content doesn't fit — normalize proportionally
                float total = colWidths.Sum();
                for (int c = 0; c < colCount; c++)
                    colWidths[c] = colWidths[c] * w.ContentWidth / total;
            }
        }

        float cellPadding = 4f;

        // Border/grid color from theme
        var gridColor = _tableBorderColor;

        int startRow = 0;
        // Header row
        if (table.HasHeader && table.Children[0] is TableRowNode headerRow)
        {
            RenderTableHeader(w, headerRow, colWidths, cellPadding, table.Columns);
            startRow = 1;
        }

        // Body rows — repeat header on continuation pages
        TableRowNode? repeatHeader = _repeatTableHeader && table.HasHeader && startRow == 1
            ? table.Children[0] as TableRowNode : null;

        for (int i = startRow; i < table.Children.Count; i++)
        {
            if (table.Children[i] is TableRowNode row)
            {
                int pageBefore = w.CurrentPageNumber;
                w.EnsurePage();

                // If EnsurePage moved to a new page, repeat the header
                if (repeatHeader is not null && w.CurrentPageNumber != pageBefore)
                    RenderTableHeader(w, repeatHeader, colWidths, cellPadding, table.Columns);

                RenderTableRow(w, row, colWidths, cellPadding, _fontRegular, _bodyFontSize, table.Columns);

                // Draw a separator line between body rows
                if (i < table.Children.Count - 1)
                {
                    if (gridColor is { } gc)
                        w.SetStrokeColor(gc.R, gc.G, gc.B);
                    else
                        w.SetStrokeColor(0.85f, 0.85f, 0.85f);
                    w.DrawLine(w.MarginLeftValue, w.CursorY + _bodyLeading - 2, w.MarginLeftValue + w.ContentWidth, w.CursorY + _bodyLeading - 2, 0.25f);
                    w.SetStrokeColor(0, 0, 0);
                }
            }
        }

        w.MoveCursor(_paragraphSpacingAfter);
    }

    private void RenderTableHeader(PdfWriter w, TableRowNode headerRow, float[] colWidths,
        float cellPadding, IReadOnlyList<TableColumnSpec>? columns)
    {
        // Measure header row height first (need it for background fill)
        var headerFont = _fontBold;
        var headerFontSize = _bodyFontSize;
        float rowHeight = MeasureRowHeight(w, headerRow, colWidths, cellPadding, headerFont, headerFontSize);

        // Draw header background if configured
        if (_tableHeaderBackground is { } bg)
        {
            w.SetFillColor(bg.R, bg.G, bg.B);
            w.DrawRect(w.MarginLeftValue, w.CursorY - rowHeight + headerFontSize * 0.75f,
                w.ContentWidth, rowHeight, fill: true);
            w.SetFillColor(0, 0, 0);
        }

        // Set header text color (explicit, or white on dark background, or black)
        var headerFontColor = _tableHeaderFontColor
            ?? (_tableHeaderBackground is not null ? new PdfColor(1, 1, 1) : (PdfColor?)null);
        if (headerFontColor is { } fc) w.SetFillColor(fc.R, fc.G, fc.B);

        RenderTableRow(w, headerRow, colWidths, cellPadding, headerFont, headerFontSize, columns);

        if (headerFontColor is not null) w.SetFillColor(0, 0, 0);

        // Draw line under header using border color or background color
        if (_tableBorderColor is { } tbc)
            w.SetStrokeColor(tbc.R, tbc.G, tbc.B);
        else if (_tableHeaderBackground is { } hbg)
            w.SetStrokeColor(hbg.R, hbg.G, hbg.B);
        else
            w.SetStrokeColor(0, 0, 0);
        w.DrawLine(w.MarginLeftValue, w.CursorY + _bodyLeading - 2, w.MarginLeftValue + w.ContentWidth, w.CursorY + _bodyLeading - 2, 1f);
        w.SetStrokeColor(0, 0, 0);
    }

    private float MeasureRowHeight(PdfWriter w, TableRowNode row, float[] colWidths,
        float cellPadding, string font, float fontSize)
    {
        int colIndex = 0;
        int maxLines = 1;
        foreach (var child in row.Children)
        {
            if (child is TableCellNode cell)
            {
                float cellWidth = 0;
                for (int s = 0; s < cell.ColSpan && colIndex + s < colWidths.Length; s++)
                    cellWidth += colWidths[colIndex + s];
                string text = GetPlainText(cell.Inlines, cell.Text);
                var lines = w.WrapText(text, font, fontSize, cellWidth - 2 * cellPadding);
                if (lines.Count > maxLines) maxLines = lines.Count;
                colIndex += cell.ColSpan;
            }
        }
        return maxLines * _bodyLeading;
    }

    private void RenderTableRow(PdfWriter w, TableRowNode row, float[] colWidths,
        float cellPadding, string font, float fontSize, IReadOnlyList<TableColumnSpec>? columns)
    {
        // First pass: wrap text and determine row height
        var cellWrapped = new List<(List<string> Lines, float CellWidth, TableAlignment? Align, int ColSpan)>();
        int colIndex = 0;

        foreach (var child in row.Children)
        {
            if (child is TableCellNode cell)
            {
                string text = GetPlainText(cell.Inlines, cell.Text);

                float cellWidth = 0;
                for (int s = 0; s < cell.ColSpan && colIndex + s < colWidths.Length; s++)
                    cellWidth += colWidths[colIndex + s];

                var lines = w.WrapText(text, font, fontSize, cellWidth - 2 * cellPadding);

                var align = cell.Alignment;
                if (align is null && columns is not null && colIndex < columns.Count)
                    align = columns[colIndex].Alignment;

                cellWrapped.Add((lines, cellWidth, align, cell.ColSpan));
                colIndex += cell.ColSpan;
            }
        }

        // Row height = max number of lines * leading
        int maxLines = cellWrapped.Count > 0 ? cellWrapped.Max(c => c.Lines.Count) : 1;
        float rowHeight = maxLines * _bodyLeading;

        // Check if we need a page break for this row
        if (w.CursorY - rowHeight < w.MarginBottomValue)
        {
            w.EnsurePage(); // Force new page
        }

        // Second pass: render each cell's lines
        float x = w.MarginLeftValue;
        float baseY = w.CursorY;

        foreach (var (lines, cellWidth, align, colSpan) in cellWrapped)
        {
            float lineY = baseY;
            float availWidth = cellWidth - 2 * cellPadding;
            for (int li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                float textWidth = w.MeasureText(line, font, fontSize);
                bool isLastLine = li == lines.Count - 1;

                float textX = align switch
                {
                    TableAlignment.Right => x + cellWidth - cellPadding - textWidth,
                    TableAlignment.Center => x + (cellWidth - textWidth) / 2,
                    _ => x + cellPadding,
                };

                // Justify non-last lines in left-aligned cells
                if (align is null or TableAlignment.Left && !isLastLine && lines.Count > 1)
                {
                    int spaceCount = 0;
                    foreach (var ch in line)
                        if (ch == ' ') spaceCount++;
                    if (spaceCount > 0)
                    {
                        float extraSpacing = (availWidth - textWidth) / spaceCount;
                        float maxTableSpacing = w.MeasureText(" ", font, fontSize) * 2;
                        if (extraSpacing > 0 && extraSpacing <= maxTableSpacing)
                        {
                            w.WriteJustifiedText(line, font, fontSize, x + cellPadding, lineY, extraSpacing);
                            lineY -= _bodyLeading;
                            continue;
                        }
                    }
                }

                w.WriteText(line, font, fontSize, textX, lineY);
                lineY -= _bodyLeading;
            }
            x += cellWidth;
        }

        // Move cursor past the entire row
        w.MoveCursor(rowHeight);
    }

    private void RenderBlockImage(PdfWriter w, BlockImageNode image, int indentLevel)
    {
        w.EnsurePage();

        if (image.Title is not null)
        {
            w.WriteWrappedText(image.Title, _fontItalic, _smallFontSize, _codeLeading);
        }

        // Try SVG first (vector), then raster images
        if (TryLoadSvg(image.Target, out var svgDoc))
        {
            RenderSvgImage(w, svgDoc, w.ContentWidth);
        }
        else if (TryLoadImage(image.Target, out var imageInfo))
        {
            string imageRef = w.EmbedImage(imageInfo);

            // Scale to fit content width, maintaining aspect ratio
            float imgWidth = imageInfo.Width;
            float imgHeight = imageInfo.Height;
            float maxWidth = w.ContentWidth;
            float scale = Math.Min(1f, maxWidth / imgWidth);
            float displayWidth = imgWidth * scale;
            float displayHeight = imgHeight * scale;

            if (w.CursorY - displayHeight < w.MarginBottomValue)
                w.EnsurePage();

            w.MoveCursor(displayHeight);
            w.DrawImage(imageRef, w.MarginLeftValue, w.CursorY, displayWidth, displayHeight);
        }
        else
        {
            // Fallback: gray placeholder
            w.SetFillColor(0.9f, 0.9f, 0.9f);
            w.DrawRect(w.MarginLeftValue, w.CursorY - 60, w.ContentWidth, 60, fill: true);
            w.SetFillColor(0, 0, 0);

            string label = string.IsNullOrEmpty(image.Alt) ? $"[Image: {image.Target}]" : $"[Image: {image.Alt}]";
            w.WriteText(label, _fontItalic, _bodyFontSize, w.MarginLeftValue + 8, w.CursorY - 35);
            w.MoveCursor(68f);
        }

        w.MoveCursor(_paragraphSpacingAfter);
    }

    /// <summary>
    /// Renders an SVG at the current cursor position, scaling to fit the given max width.
    /// </summary>
    private void RenderSvgImage(PdfWriter w, SvgParser.SvgDocument svg, float maxWidth)
    {
        float aspectRatio = svg.ViewBoxHeight / svg.ViewBoxWidth;
        float displayWidth = Math.Min(svg.Width, maxWidth);
        float displayHeight = displayWidth * aspectRatio;

        if (w.CursorY - displayHeight < w.MarginBottomValue)
            w.EnsurePage();

        w.MoveCursor(displayHeight);
        w.DrawSvg(svg, w.MarginLeftValue, w.CursorY, displayWidth, displayHeight);
    }

    /// <summary>
    /// Tries to load an SVG file from the base directory.
    /// </summary>
    private bool TryLoadSvg(string target, out SvgParser.SvgDocument svgDoc)
    {
        svgDoc = default;
        if (_baseDirectory is null) return false;
        if (!target.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return false;
        string fullPath = Path.Combine(_baseDirectory, target);
        if (!File.Exists(fullPath)) return false;
        byte[] data;
        try { data = File.ReadAllBytes(fullPath); }
        catch { return false; }
        var result = SvgParser.Parse(data);
        if (result is null) return false;
        svgDoc = result.Value;
        return true;
    }

    /// <summary>
    /// Tries to load an image file from the base directory and parse it.
    /// </summary>
    private bool TryLoadImage(string target, out ImageParser.ImageInfo info)
    {
        info = default;
        if (_baseDirectory is null) return false;
        string fullPath = Path.Combine(_baseDirectory, target);
        if (!File.Exists(fullPath)) return false;
        byte[] data;
        try { data = File.ReadAllBytes(fullPath); }
        catch { return false; }
        var result = ImageParser.TryParseJpeg(data) ?? ImageParser.TryParsePng(data);
        if (result is null) return false;
        info = result.Value;
        return true;
    }

    private void RenderBibliographyEntry(PdfWriter w, BibliographyEntryNode entry, FootnoteState footnotes)
    {
        w.EnsurePage();
        var label = entry.Label ?? entry.RefId;
        var segments = BuildInlineSegments(entry.Inlines, entry.Text, _fontRegular, _bodyFontSize, footnotes);
        segments.Insert(0, new TextSegment($"[{label}] ", _fontBold, _bodyFontSize));
        w.WriteWrappedSegments(segments, _bodyLeading);
        w.MoveCursor(_paragraphSpacingAfter / 2);
    }

    private void RenderDescriptionList(PdfWriter w, DescriptionListNode list, int indentLevel, FootnoteState footnotes)
    {
        foreach (var child in list.Children)
        {
            if (child is DescriptionItemNode item)
            {
                w.EnsurePage();

                // Term in bold
                var termSegments = BuildInlineSegments(item.TermInlines, item.Terms.Count > 0 ? item.Terms[0] : "", _fontBold, _bodyFontSize, footnotes);
                w.WriteWrappedSegments(termSegments, _bodyLeading);

                // Description indented (only when present — empty when the item
                // body comes entirely from continuation blocks below).
                bool hasInlineDescription = item.DescriptionInlines.Count > 0
                    || !string.IsNullOrEmpty(item.Description);
                if (hasInlineDescription)
                {
                    var descSegments = BuildInlineSegments(item.DescriptionInlines, item.Description, _fontRegular, _bodyFontSize, footnotes);
                    w.WriteWrappedSegments(descSegments, _bodyLeading);
                }

                // Continuation blocks (source/listing blocks attached via "+",
                // nested lists, etc.) live in item.Children. Render them as
                // child blocks of the description entry — matches HTML/DocBook.
                foreach (var grandChild in item.Children)
                {
                    if (grandChild is BlockNode block)
                        RenderBlock(w, block, indentLevel, footnotes);
                }
                w.MoveCursor(_paragraphSpacingAfter / 2);
            }
        }
        w.MoveCursor(_paragraphSpacingAfter);
    }

    private void RenderAdmonition(PdfWriter w, AdmonitionNode admonition, int indentLevel, FootnoteState footnotes)
    {
        w.EnsurePage();

        // Admonition type label in bold
        w.WriteWrappedText($"{admonition.AdmonitionType}:", _fontBold, _bodyFontSize, _bodyLeading);

        if (admonition.Children.Count > 0)
        {
            foreach (var child in admonition.Children)
                RenderBlock(w, child, indentLevel, footnotes);
        }
        else
        {
            var segments = BuildInlineSegments(admonition.Inlines, admonition.Text ?? string.Empty,
                _fontRegular, _bodyFontSize, footnotes);
            w.WriteWrappedSegments(segments, _bodyLeading);
        }

        w.MoveCursor(_paragraphSpacingAfter);
    }

    // ── Inline segment building ─────────────────────────────────────────

    /// <summary>
    /// Converts inline AST nodes into styled text segments for the PDF writer.
    /// Falls back to plain text if no inlines are parsed.
    /// </summary>
    private List<TextSegment> BuildInlineSegments(
        IReadOnlyList<InlineNode> inlines, string fallbackText,
        string defaultFont, float defaultFontSize, FootnoteState footnotes)
    {
        var segments = new List<TextSegment>();

        if (inlines.Count > 0)
        {
            foreach (var inline in inlines)
                AppendInlineSegments(segments, inline, defaultFont, defaultFontSize, footnotes);
        }
        else
        {
            segments.Add(new TextSegment(fallbackText, defaultFont, defaultFontSize));
        }

        return segments;
    }

    private void AppendInlineSegments(List<TextSegment> segments, InlineNode node,
        string defaultFont, float defaultFontSize, FootnoteState footnotes,
        PdfColor? background = null, bool isBold = false, bool isItalic = false)
    {
        switch (node)
        {
            case TextInlineNode text:
                // Replace newlines with spaces — PDF text operators can't render \n
                string value = text.Value.Contains('\n') ? text.Value.Replace('\n', ' ') : text.Value;
                segments.Add(new TextSegment(value, defaultFont, defaultFontSize, Background: background));
                break;

            case StrongInlineNode strong:
                foreach (var child in strong.Children)
                    AppendInlineSegments(segments, child, _fontBold, defaultFontSize, footnotes,
                        background: background, isBold: true, isItalic: isItalic);
                break;

            case EmphasisInlineNode emphasis:
                foreach (var child in emphasis.Children)
                    AppendInlineSegments(segments, child, _fontItalic, defaultFontSize, footnotes,
                        background: background, isBold: isBold, isItalic: true);
                break;

            case MonospaceInlineNode monospace:
                // Choose appropriate monospace variant based on parent formatting context
                string monoFont = ResolveMonoFont(isBold, isItalic);
                foreach (var child in monospace.Children)
                    AppendInlineSegments(segments, child, monoFont, _codeFontSize, footnotes,
                        background: _codespanBackground, isBold: isBold, isItalic: isItalic);
                break;

            case LinkInlineNode link:
                segments.Add(new TextSegment(link.Url, _fontRegular, defaultFontSize, link.Url));
                break;

            case InlineLinkMacroNode linkMacro:
                var linkMacroText = string.IsNullOrEmpty(linkMacro.Label) ? linkMacro.Url : linkMacro.Label;
                segments.Add(new TextSegment(linkMacroText, _fontRegular, defaultFontSize, linkMacro.Url));
                break;

            case InlineImageNode inlineImage:
                string alt = string.IsNullOrEmpty(inlineImage.Alt)
                    ? $"[image:{inlineImage.Target}]"
                    : $"[{inlineImage.Alt}]";
                segments.Add(new TextSegment(alt, _fontItalic, defaultFontSize));
                break;

            case SuperscriptInlineNode superscript:
                segments.Add(new TextSegment(superscript.Content, defaultFont, defaultFontSize));
                break;

            case SubscriptInlineNode subscript:
                segments.Add(new TextSegment(subscript.Content, defaultFont, defaultFontSize));
                break;

            case PassthroughInlineNode passthrough:
                segments.Add(new TextSegment(passthrough.Content, defaultFont, defaultFontSize));
                break;

            case CrossReferenceInlineNode xref:
                var xrefLabel = xref.Label ?? xref.Target;
                // Use internal link for cross-references (resolved in ToBytes)
                segments.Add(new TextSegment(xrefLabel, defaultFont, defaultFontSize,
                    $"#internal#{xref.Target}"));
                break;

            case FootnoteInlineNode footnote:
                int fnNum = footnotes.Register(footnote);
                segments.Add(new TextSegment($"[{fnNum}]", defaultFont, defaultFontSize));
                break;

            case InlineMacroNode macro:
                if (macro.Name == "kbd")
                    segments.Add(new TextSegment(macro.Content, _fontMono, defaultFontSize));
                else if (macro.Name == "menu")
                    segments.Add(new TextSegment($"{macro.Target} > {macro.Content}", _fontRegular, defaultFontSize));
                else
                    segments.Add(new TextSegment(macro.Content, _fontRegular, defaultFontSize));
                break;
        }
    }

    /// <summary>
    /// Resolves the appropriate monospace font variant based on the parent formatting context.
    /// Matches Asciidoctor behavior where codespans inherit bold/italic from surrounding text.
    /// </summary>
    private string ResolveMonoFont(bool isBold, bool isItalic)
    {
        if (isBold && isItalic) return _fontMonoBoldItalic;
        if (isBold) return _fontMonoBold;
        if (isItalic) return _fontMonoItalic;
        return _fontMono;
    }
}
