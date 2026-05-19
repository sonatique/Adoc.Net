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
            // Auto-size columns: weight each column by its natural unwrapped
            // content width (max over all cells), then scale to fit the page
            // width. Per-column "longest word" widths act as floors so a single
            // word never overflows its column. Columns with prose ask for —
            // and get — far more space than columns with short identifiers.
            //
            // The previous algorithm pinned each column to its longest-word
            // width up front and distributed only the leftover by character
            // count. When a wide table mixed long identifiers with prose, the
            // identifier columns soaked up the budget and prose columns
            // collapsed to one-word-per-line (see issue #17).

            float[] naturalWidths = new float[colCount];
            float[] minWidths = new float[colCount];
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
                            float fullWidth = w.MeasureText(text, _fontRegular, _bodyFontSize) + 2 * cellPad;

                            // Spanning cells contribute natural width spread
                            // evenly across the columns they cover; no single
                            // column gets the whole span's natural width.
                            int span = c.ColSpan > 0 ? c.ColSpan : 1;
                            float perColNatural = fullWidth / span;
                            for (int s = 0; s < span && ci + s < colCount; s++)
                            {
                                if (perColNatural > naturalWidths[ci + s])
                                    naturalWidths[ci + s] = perColNatural;
                            }

                            // Min width = longest single word + padding, attributed
                            // to the cell's starting column. Spanning cells with
                            // long unbreakable words still pin only the first col.
                            foreach (var word in text.Split(' '))
                            {
                                float ww = w.MeasureText(word, _fontRegular, _bodyFontSize) + 2 * cellPad;
                                if (ww > minWidths[ci])
                                    minWidths[ci] = ww;
                            }
                            ci += span;
                        }
                    }
                }
            }

            float totalNatural = naturalWidths.Sum();
            float totalMin = minWidths.Sum();

            if (totalNatural <= 0)
            {
                // Empty table — fall back to equal split.
                for (int c = 0; c < colCount; c++)
                    colWidths[c] = w.ContentWidth / colCount;
            }
            else if (totalMin >= w.ContentWidth)
            {
                // Even minimum word widths don't fit on the page. Scale natural
                // widths to ContentWidth and accept some single-word overflow —
                // there's no better placement available.
                for (int c = 0; c < colCount; c++)
                    colWidths[c] = naturalWidths[c] * w.ContentWidth / totalNatural;
            }
            else if (totalNatural <= w.ContentWidth)
            {
                // Whole table fits naturally. Scale natural widths up so the
                // table fills ContentWidth, preserving proportions.
                for (int c = 0; c < colCount; c++)
                    colWidths[c] = naturalWidths[c] * w.ContentWidth / totalNatural;
            }
            else
            {
                // Need to compress. Give each column its longest-word minimum,
                // then distribute the remainder proportional to "excess" content
                // (naturalWidth − minWidth). Columns with long prose ask for the
                // most excess and get the largest share of the slack.
                float remaining = w.ContentWidth - totalMin;
                float[] excess = new float[colCount];
                float totalExcess = 0;
                for (int c = 0; c < colCount; c++)
                {
                    excess[c] = naturalWidths[c] - minWidths[c];
                    if (excess[c] < 0) excess[c] = 0;
                    totalExcess += excess[c];
                }
                for (int c = 0; c < colCount; c++)
                {
                    colWidths[c] = minWidths[c];
                    if (totalExcess > 0)
                        colWidths[c] += remaining * excess[c] / totalExcess;
                    else
                        colWidths[c] += remaining / colCount;
                }
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

        // Asciidoctor-pdf admonition layout (default theme):
        //   admonition.padding = [vertical_rhythm/3, horizontal_rhythm,
        //                          vertical_rhythm/3, horizontal_rhythm]
        //                      = [4, 12, 4, 12]
        //   icon column width = icon_size * 1.5 = 36pt (when icon_size = 24)
        //   column rule between icon column and content (base.border_color
        //   = #EEEEEE, base.border_width = 0.5pt)
        //   content column = remaining width minus right padding
        var color = AdmonitionColor(admonition.AdmonitionType);
        const float AdmonPaddingLeft = 12f;    // horizontal_rhythm
        const float IconColumnWidth = 36f;     // icon_size (24) * 1.5
        const float LabelPaddingRight = 12f;   // padding between icon column and rule
        const float ContentLeftPadding = 12f;  // padding between rule and content
        const float RuleWidth = 0.5f;          // base.border_width
        const float BarThickness = 3f;         // for text-label fallback only
        const float BarPadding = 4f;
        // X coordinate of the vertical column rule (between icon column and content).
        // Total horizontal layout per asciidoctor-pdf:
        //   [page_margin] [pad_left=12] [icon_col=36] [pad_right=12] [rule=0.5] [pad_left=12] [content]
        float ruleX = w.MarginLeftValue + AdmonPaddingLeft + IconColumnWidth + LabelPaddingRight;
        // Body content starts to the right of the rule + padding
        float labelWidth = AdmonPaddingLeft + IconColumnWidth + LabelPaddingRight + RuleWidth + ContentLeftPadding;

        float startY = w.CursorY;
        float startX = w.MarginLeftValue;

        // Compute icon vertical center upfront so the column rule (drawn after
        // body content) can be centered on it (matches asciidoctor-pdf).
        float bodyAscent = _bodyFontSize * 1.069f;
        float iconCenterY = startY - bodyAscent / 2f;

        if (_useIconAdmonitions)
        {
            // Asciidoctor-pdf renders admonition icons as Font Awesome 5 Solid glyphs
            // (fa-info-circle for NOTE, fa-exclamation-triangle for WARNING, etc.) at
            // ~24pt, in the type's accent color, with NO surrounding circle background.
            // We try to mirror that with the embedded FA font; fall back to a drawn
            // circle+glyph if FA loading failed (e.g. resource missing).
            // Icon is centered in its column (icon column width 36pt at offset 12pt).
            float iconCenterX = startX + AdmonPaddingLeft + IconColumnWidth / 2f;
            // Pick the right FA variant for this admonition (TIP uses FA Regular
            // far-lightbulb, others use FA Solid). Fall back to Solid if Regular
            // failed to load.
            bool useRegular = string.Equals(admonition.AdmonitionType, "tip", StringComparison.OrdinalIgnoreCase)
                              && _fontAwesomeRegular is not null;
            string? faFont = useRegular ? _fontAwesomeRegular : _fontAwesome;
            if (faFont is not null)
            {
                // Draw the FA glyph in the admonition's accent color, centered
                // on the body's first line baseline.
                string fa = AdmonitionFaGlyph(admonition.AdmonitionType);
                float faSize = 24f;
                w.SetFillColor(color.R, color.G, color.B);
                float gw = w.MeasureText(fa, faFont, faSize);
                // Vertical centering: glyph baseline ≈ glyph_top - cap_height. For FA
                // (cap_height ≈ 0.7 × font_size), baseline = center + 0.35×size.
                float baselineY = iconCenterY - faSize * 0.35f;
                w.WriteText(fa, faFont, faSize, iconCenterX - gw / 2f, baselineY);
            }
            else
            {
                // Fallback: drawn circle + vector glyph
                float iconRadius = 9f;
                w.SetFillColor(color.R, color.G, color.B);
                w.DrawCircle(iconCenterX, iconCenterY, iconRadius, "f");
                w.SetFillColor(1f, 1f, 1f);
                DrawAdmonitionGlyph(w, admonition.AdmonitionType, iconCenterX, iconCenterY);
            }
            // Do NOT advance cursor — body text draws at startY, alongside the icon
            // (icon is in the LabelWidth gutter to the left of the indented body).
        }
        else
        {
            // Text label fallback (asciidoctor's behavior when :icons: is unset)
            var labelText = admonition.AdmonitionType.ToUpperInvariant();
            w.SetFillColor(color.R, color.G, color.B);
            w.WriteWrappedText(labelText, _fontBold, _bodyFontSize, _bodyLeading);
        }
        w.SetFillColor(0, 0, 0);

        // Indent body content to the position right of the column rule.
        float savedIndent = w.PushIndent(labelWidth);
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
        w.PopIndent(savedIndent);

        float endY = w.CursorY;
        if (_useIconAdmonitions)
        {
            // Asciidoctor-pdf draws a thin grey vertical "column rule"
            // (base.border_color = #EEEEEE, base.border_width = 0.5pt)
            // between the icon column and the content column. The rule is
            // VERTICALLY CENTERED ON THE ICON (REF measurement: rule center
            // y = icon center y to within rounding). Shift the rule so its
            // center aligns with iconCenterY while preserving its height.
            float ruleHeight = startY - endY;
            float ruleCenterCurrent = (startY + endY) / 2f;
            float ruleShift = iconCenterY - ruleCenterCurrent;
            float shiftedBottom = endY + ruleShift;
            w.SetFillColor(0xEE / 255f, 0xEE / 255f, 0xEE / 255f);
            w.DrawRect(ruleX, shiftedBottom, RuleWidth, ruleHeight, fill: true);
            w.SetFillColor(0, 0, 0);
        }
        else
        {
            // Text-label mode: thick colored accent bar in the indent gutter
            // (asciidoctor-pdf's behavior when :icons: is unset).
            float barX = startX + labelWidth - BarThickness - BarPadding;
            w.SetFillColor(color.R, color.G, color.B);
            w.DrawRect(barX, endY, BarThickness, startY - endY, fill: true);
            w.SetFillColor(0, 0, 0);
        }

        w.MoveCursor(_paragraphSpacingAfter);
    }

    /// <summary>
    /// Returns the FontAwesome 5 Solid codepoint for the given admonition type.
    /// These match asciidoctor-pdf's default theme icon mappings.
    /// </summary>
    private static string AdmonitionFaGlyph(string type) => type.ToLowerInvariant() switch
    {
        "note"      => "\uf05a", // fa-info-circle
        "tip"       => "\uf0eb", // fa-lightbulb
        "warning"   => "\uf071", // fa-exclamation-triangle
        "caution"   => "\uf06d", // fa-fire
        "important" => "\uf06a", // fa-exclamation-circle
        _           => "\uf05a", // default to info-circle
    };

    /// <summary>
    /// Draws the white glyph inside an admonition icon circle, using vector
    /// primitives (rectangles + small circles) to mimic FontAwesome's
    /// fa-info-circle / fa-warning / etc. without requiring font embedding.
    /// (cx, cy) is the circle's center; the circle radius is assumed to be 9pt.
    /// PDF y-axis: larger y = higher on page (top of icon = cy + offset).
    /// </summary>
    private static void DrawAdmonitionGlyph(PdfWriter w, string type, float cx, float cy)
    {
        switch (type.ToLowerInvariant())
        {
            case "note":
            case "tip":
                // "i" — info-style: small dot at TOP, taller bar BELOW.
                w.DrawCircle(cx, cy + 4f, 1.3f, "f");                    // dot above
                w.DrawRect(cx - 1.3f, cy - 4.5f, 2.6f, 6f, fill: true);  // bar below (y0=cy-4.5 → extends up to cy+1.5)
                break;
            case "warning":
            case "caution":
            case "important":
                // "!" — exclamation: taller bar ABOVE, small dot BELOW.
                w.DrawRect(cx - 1.3f, cy - 1.5f, 2.6f, 6f, fill: true);  // bar above (y0=cy-1.5 → extends up to cy+4.5)
                w.DrawCircle(cx, cy - 4f, 1.3f, "f");                    // dot below
                break;
            default:
                w.DrawCircle(cx, cy, 1.5f, "f");
                break;
        }
    }

    /// <summary>
    /// Returns asciidoctor-pdf's accent color for the given admonition type.
    /// These match the exact stroke_color values from asciidoctor-pdf's
    /// AdmonitionIcons constant (lib/asciidoctor/pdf/converter.rb).
    /// </summary>
    private static PdfColor AdmonitionColor(string type) => type.ToLowerInvariant() switch
    {
        "note"      => new PdfColor(0x19 / 255f, 0x40 / 255f, 0x7C / 255f), // #19407C dark navy
        "tip"       => new PdfColor(0x11 / 255f, 0x11 / 255f, 0x11 / 255f), // #111111 near-black
        "warning"   => new PdfColor(0xBF / 255f, 0x69 / 255f, 0x00 / 255f), // #BF6900 dark orange
        "caution"   => new PdfColor(0xBF / 255f, 0x34 / 255f, 0x00 / 255f), // #BF3400 red-orange
        "important" => new PdfColor(0xBF / 255f, 0x00 / 255f, 0x00 / 255f), // #BF0000 red
        _           => new PdfColor(0.5f, 0.5f, 0.5f),
    };

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
        PdfColor? background = null, bool isBold = false, bool isItalic = false,
        PdfColor? color = null)
    {
        switch (node)
        {
            case TextInlineNode text:
                // Replace newlines with spaces — PDF text operators can't render \n
                string value = text.Value.Contains('\n') ? text.Value.Replace('\n', ' ') : text.Value;
                segments.Add(new TextSegment(value, defaultFont, defaultFontSize, Background: background, Color: color));
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
                        background: _codespanBackground, isBold: isBold, isItalic: isItalic,
                        color: _codespanColor);
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
                else if (macro.Name == "icon" && _useIconAdmonitions && _fontAwesome is not null)
                    AppendInlineIconSegments(segments, macro, defaultFontSize);
                else
                    segments.Add(new TextSegment(macro.Content, _fontRegular, defaultFontSize));
                break;
        }
    }

    /// <summary>
    /// Renders an inline `icon:name[]` macro as a FontAwesome glyph, mirroring
    /// asciidoctor-pdf's inline icon support. Looks up the icon name (e.g.
    /// "info-circle", "rocket") in the FA codepoint map and emits a single
    /// segment using the FA Solid (default) or Regular font as appropriate.
    /// </summary>
    private void AppendInlineIconSegments(List<TextSegment> segments, InlineMacroNode macro, float fontSize)
    {
        var codepoint = InlineIconCodepoint(macro.Target);
        if (codepoint is null)
        {
            // Unknown icon — fall back to literal name in regular font
            segments.Add(new TextSegment($"[{macro.Target}]", _fontRegular, fontSize));
            return;
        }
        // Asciidoctor-pdf's prawn-icon resolves icons by trying FA variants in
        // order: brands → regular → solid. For icons that exist in both Regular
        // and Solid (e.g. star, heart, user), Regular wins (outline style).
        var font = (_fontAwesomeRegular is not null && IsRegularIcon(macro.Target))
            ? _fontAwesomeRegular
            : _fontAwesome!;
        segments.Add(new TextSegment(codepoint, font, fontSize));
    }

    /// <summary>
    /// Returns true for icon names that exist in FontAwesome Regular and where
    /// the Regular variant should be preferred (matches asciidoctor-pdf's
    /// resolution order: brands → regular → solid).
    /// </summary>
    private static bool IsRegularIcon(string? name) => name?.ToLowerInvariant() switch
    {
        "lightbulb" or "lightbulb-o"
        or "bell" or "bookmark" or "building" or "calendar" or "chart-bar"
        or "check-circle" or "check-square" or "circle" or "clock" or "comment"
        or "envelope" or "file" or "flag" or "folder" or "handshake" or "heart"
        or "image" or "moon" or "newspaper" or "paper-plane" or "registered"
        or "save" or "smile" or "snowflake" or "square" or "star" or "sun"
        or "thumbs-down" or "thumbs-up" or "user"
        or "window-maximize" or "window-minimize" or "window-restore" => true,
        _ => false,
    };

    /// <summary>
    /// Maps common asciidoctor icon: macro names to FontAwesome 5 codepoints.
    /// Extend as needed; unknown names fall back to literal `[name]` text.
    /// </summary>
    private static string? InlineIconCodepoint(string? name) => name?.ToLowerInvariant() switch
    {
        null              => null,
        "info-circle"     => "\uf05a",
        "info"            => "\uf129",
        "lightbulb"       => "\uf0eb",
        "lightbulb-o"     => "\uf0eb",
        "warning"         => "\uf071",
        "exclamation-triangle" => "\uf071",
        "exclamation-circle"   => "\uf06a",
        "fire"            => "\uf06d",
        "check"           => "\uf00c",
        "check-square"    => "\uf14a",
        "square"          => "\uf0c8",
        "times"           => "\uf00d",
        "rocket"          => "\uf135",
        "cog"             => "\uf013",
        "user"            => "\uf007",
        "home"            => "\uf015",
        "bookmark"        => "\uf02e",
        "tag"             => "\uf02b",
        "tags"            => "\uf02c",
        "link"            => "\uf0c1",
        "globe"           => "\uf0ac",
        "search"          => "\uf002",
        "envelope"        => "\uf0e0",
        "phone"           => "\uf095",
        "calendar"        => "\uf073",
        "clock-o"         => "\uf017",
        "clock"           => "\uf017",
        "star"            => "\uf005",
        "heart"           => "\uf004",
        "thumbs-up"       => "\uf164",
        "thumbs-down"     => "\uf165",
        _                 => null,
    };

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
