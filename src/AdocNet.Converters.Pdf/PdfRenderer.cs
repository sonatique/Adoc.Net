using AdocNet;
using AdocNet.Ast;

namespace AdocNet.Converters.Pdf;

/// <summary>
/// Renders a <see cref="DocumentNode"/> AST to a PDF byte array.
/// Uses only managed code with the 14 standard PDF fonts (Helvetica, Courier).
/// Output is deterministic — fixed metadata, consistent object numbering.
/// </summary>
public sealed class PdfRenderer : DocumentRendererBase
{
    // ── Font configuration ──────────────────────────────────────────────
    // These default to the standard PDF font keys, but may be replaced
    // with embedded TrueType font keys when FontPath options are set.
    private string _fontRegular = "F1";        // Helvetica
    private string _fontBold = "F2";            // Helvetica-Bold
    private string _fontItalic = "F3";          // Helvetica-Oblique
    private string _fontMono = "F4";            // Courier

    // ── Size configuration ──────────────────────────────────────────────
    private const float TitleFontSize = 24f;
    private const float H2FontSize = 20f;
    private const float H3FontSize = 16f;
    private const float H4FontSize = 14f;
    private const float H5FontSize = 12f;
    private const float BodyFontSize = 11f;
    private const float CodeFontSize = 9f;
    private const float SmallFontSize = 9f;

    private const float TitleLeading = 30f;
    private const float HeadingLeading = 24f;
    private const float BodyLeading = 15f;
    private const float CodeLeading = 12f;

    private const float ParagraphSpacing = 8f;
    private const float SectionSpacing = 16f;
    private const float ListIndent = 18f;
    private const float BlockIndent = 24f;

    /// <summary>
    /// Tracks footnotes collected during PDF rendering.
    /// </summary>
    private sealed class FootnoteState
    {
        public List<(int Number, string? Id, FootnoteInlineNode Node)> Footnotes { get; } = [];
        private int _nextNumber = 1;

        public int Register(FootnoteInlineNode node)
        {
            if (node.Text is null && node.Id is not null)
            {
                foreach (var (num, id, _) in Footnotes)
                {
                    if (id == node.Id) return num;
                }
            }

            if (node.Id is not null)
            {
                foreach (var (num, id, _) in Footnotes)
                {
                    if (id == node.Id) return num;
                }
            }

            int number = _nextNumber++;
            Footnotes.Add((number, node.Id, node));
            return number;
        }
    }

    // ── Public API ──────────────────────────────────────────────────────

    public override string Format => "pdf";

    // ── Image resolution state ─────────────────────────────────────────
    private string? _baseDirectory;

    protected override void RenderDocument(RenderContext context, Stream output)
    {
        var pdfOptions = context.Options as PdfRenderOptions ?? PdfRenderOptions.Default;
        var writer = new PdfWriter(pdfOptions.PageWidth, pdfOptions.PageHeight,
            pdfOptions.MarginLeft, pdfOptions.MarginRight, pdfOptions.MarginTop, pdfOptions.MarginBottom);

        writer.ShowPageNumbers = pdfOptions.ShowPageNumbers;
        writer.HeaderTemplate = pdfOptions.HeaderText;
        writer.FooterTemplate = pdfOptions.FooterText ?? (pdfOptions.ShowPageNumbers ? "Page {page}" : null);

        _baseDirectory = pdfOptions.BaseDirectory;

        // Register embedded TrueType fonts if configured
        _fontRegular = "F1";
        _fontBold = "F2";
        _fontItalic = "F3";
        _fontMono = "F4";

        if (pdfOptions.FontPath is not null && File.Exists(pdfOptions.FontPath))
        {
            var font = TrueTypeFont.Parse(File.ReadAllBytes(pdfOptions.FontPath));
            _fontRegular = writer.RegisterEmbeddedFont("F1", font);
        }

        if (pdfOptions.BoldFontPath is not null && File.Exists(pdfOptions.BoldFontPath))
        {
            var font = TrueTypeFont.Parse(File.ReadAllBytes(pdfOptions.BoldFontPath));
            _fontBold = writer.RegisterEmbeddedFont("F2", font);
        }

        if (pdfOptions.ItalicFontPath is not null && File.Exists(pdfOptions.ItalicFontPath))
        {
            var font = TrueTypeFont.Parse(File.ReadAllBytes(pdfOptions.ItalicFontPath));
            _fontItalic = writer.RegisterEmbeddedFont("F3", font);
        }

        if (pdfOptions.MonoFontPath is not null && File.Exists(pdfOptions.MonoFontPath))
        {
            var font = TrueTypeFont.Parse(File.ReadAllBytes(pdfOptions.MonoFontPath));
            _fontMono = writer.RegisterEmbeddedFont("F4", font);
        }

        var footnotes = context.GetOrCreate(() => new FootnoteState());

        writer.StartPage();
        RenderDocumentContent(writer, context.Document, footnotes);
        RenderFootnotesSection(writer, footnotes);

        byte[] bytes = writer.ToBytes();
        output.Write(bytes, 0, bytes.Length);
    }

    // ── Document rendering ──────────────────────────────────────────────

    private void RenderDocumentContent(PdfWriter w, DocumentNode document, FootnoteState footnotes)
    {
        if (document.Title is not null)
        {
            w.EnsurePage();
            w.WriteWrappedText(document.Title, _fontBold, TitleFontSize, TitleLeading);
            w.MoveCursor(SectionSpacing);
        }

        foreach (var child in document.Children)
            RenderBlock(w, child, indentLevel: 0, footnotes);
    }

    private void RenderFootnotesSection(PdfWriter w, FootnoteState footnotes)
    {
        if (footnotes.Footnotes.Count == 0) return;

        w.MoveCursor(SectionSpacing);
        w.EnsurePage();

        // Draw a horizontal rule
        w.SetStrokeColor(0.5f, 0.5f, 0.5f);
        w.DrawLine(w.MarginLeftValue, w.CursorY, w.MarginLeftValue + w.ContentWidth, w.CursorY, 0.5f);
        w.SetStrokeColor(0, 0, 0);
        w.MoveCursor(ParagraphSpacing);

        foreach (var (number, _, node) in footnotes.Footnotes)
        {
            w.EnsurePage();
            var text = GetPlainText(node.Inlines, node.Text ?? string.Empty);
            w.WriteWrappedText($"{number}. {text}", _fontRegular, SmallFontSize, CodeLeading);
        }
    }

    // ── Block rendering ─────────────────────────────────────────────────

    private void RenderBlock(PdfWriter w, AstNode node, int indentLevel, FootnoteState footnotes)
    {
        switch (node)
        {
            case SectionNode section:
                RenderSection(w, section, indentLevel, footnotes);
                break;
            case ParagraphNode paragraph:
                RenderParagraph(w, paragraph, indentLevel, footnotes);
                break;
            case ListNode list:
                RenderList(w, list, indentLevel, footnotes);
                break;
            case DelimitedBlockNode block:
                RenderDelimitedBlock(w, block, indentLevel, footnotes);
                break;
            case TableNode table:
                RenderTable(w, table, footnotes);
                break;
            case BlockImageNode blockImage:
                RenderBlockImage(w, blockImage, indentLevel);
                break;
            case DescriptionListNode descList:
                RenderDescriptionList(w, descList, indentLevel, footnotes);
                break;
            case AdmonitionNode admonition:
                RenderAdmonition(w, admonition, indentLevel, footnotes);
                break;
            case BibliographyEntryNode bibEntry:
                RenderBibliographyEntry(w, bibEntry, footnotes);
                break;
            case PageBreakNode:
                w.StartPage();
                break;
            case ThematicBreakNode:
                w.EnsurePage();
                w.SetStrokeColor(0.5f, 0.5f, 0.5f);
                w.DrawLine(w.MarginLeftValue, w.CursorY, w.MarginLeftValue + w.ContentWidth, w.CursorY, 0.5f);
                w.SetStrokeColor(0, 0, 0);
                w.MoveCursor(ParagraphSpacing);
                break;
        }
    }

    private void RenderSection(PdfWriter w, SectionNode section, int indentLevel, FootnoteState footnotes)
    {
        w.MoveCursor(SectionSpacing);
        w.EnsurePage();

        var (fontSize, leading) = section.Level switch
        {
            1 => (H2FontSize, HeadingLeading),
            2 => (H3FontSize, HeadingLeading),
            3 => (H4FontSize, HeadingLeading),
            _ => (H5FontSize, BodyLeading),
        };

        // Render section title with inline formatting
        var segments = BuildInlineSegments(section.TitleInlines, section.Title, _fontBold, fontSize, footnotes);
        w.WriteWrappedSegments(segments, leading);
        w.MoveCursor(ParagraphSpacing / 2);

        foreach (var child in section.Children)
            RenderBlock(w, child, indentLevel, footnotes);
    }

    private void RenderParagraph(PdfWriter w, ParagraphNode paragraph, int indentLevel, FootnoteState footnotes)
    {
        w.EnsurePage();

        var segments = BuildInlineSegments(paragraph.Inlines, paragraph.Text, _fontRegular, BodyFontSize, footnotes);
        w.WriteWrappedSegments(segments, BodyLeading);
        w.MoveCursor(ParagraphSpacing);
    }

    private void RenderList(PdfWriter w, ListNode list, int indentLevel, FootnoteState footnotes)
    {
        int itemNumber = 1;
        foreach (var child in list.Children)
        {
            if (child is ListItemNode item)
            {
                w.EnsurePage();

                // Bullet or number prefix
                string prefix = list.ListKind == ListKind.Unordered
                    ? "\u2022 "  // bullet character — will be rendered as '?' in standard fonts, use dash instead
                    : $"{itemNumber}. ";

                // Standard fonts don't have bullet char, use a dash
                if (list.ListKind == ListKind.Unordered)
                    prefix = "- ";

                var segments = BuildInlineSegments(item.Inlines, item.Text, _fontRegular, BodyFontSize, footnotes);
                if (segments.Count > 0)
                {
                    segments.Insert(0, new TextSegment(prefix, _fontRegular, BodyFontSize));
                }

                w.WriteWrappedSegments(segments, BodyLeading);

                // Nested lists
                foreach (var nested in item.Children)
                {
                    if (nested is ListNode nestedList)
                        RenderList(w, nestedList, indentLevel + 1, footnotes);
                }

                itemNumber++;
            }
        }
        w.MoveCursor(ParagraphSpacing);
    }

    private void RenderDelimitedBlock(PdfWriter w, DelimitedBlockNode block, int indentLevel, FootnoteState footnotes)
    {
        // Render optional title
        if (block.Title is not null)
        {
            w.EnsurePage();
            w.WriteWrappedText(block.Title, _fontBold, SmallFontSize, CodeLeading);
        }

        switch (block.BlockKind)
        {
            case DelimitedBlockKind.Literal:
            case DelimitedBlockKind.Listing:
            case DelimitedBlockKind.Source:
            case DelimitedBlockKind.Passthrough:
                RenderVerbatimBlock(w, block);
                break;

            case DelimitedBlockKind.Example:
            case DelimitedBlockKind.Quote:
            case DelimitedBlockKind.Sidebar:
                RenderStructuralBlock(w, block, indentLevel, footnotes);
                break;
        }
    }

    private void RenderVerbatimBlock(PdfWriter w, DelimitedBlockNode block)
    {
        var content = block.Content ?? string.Empty;
        w.EnsurePage();

        // Draw a light gray background (account for wrapped lines)
        int totalLines = 0;
        foreach (var line in content.Split('\n'))
            totalLines += w.CountVerbatimLines(line, _fontMono, CodeFontSize);
        float estimatedHeight = totalLines * CodeLeading + 8;
        w.SetFillColor(0.95f, 0.95f, 0.95f);
        w.DrawRect(w.MarginLeftValue - 4, w.CursorY - estimatedHeight + 4, w.ContentWidth + 8, estimatedHeight, fill: true);
        w.SetFillColor(0, 0, 0); // Reset to black

        // Language label
        if (block.Language is not null)
        {
            w.WriteText(block.Language, _fontItalic, 8f, w.MarginLeftValue, w.CursorY);
            w.MoveCursor(CodeLeading);
        }

        foreach (var line in content.Split('\n'))
        {
            w.WriteWrappedVerbatimText(line, _fontMono, CodeFontSize, CodeLeading);
        }
        w.MoveCursor(ParagraphSpacing);

        // Render callout list
        if (block.Callouts is { Count: > 0 })
        {
            int num = 1;
            foreach (var entry in block.Callouts)
            {
                w.EnsurePage();
                w.WriteWrappedText($"({num}) {entry.Text}", _fontRegular, BodyFontSize, BodyLeading);
                num++;
            }
            w.MoveCursor(ParagraphSpacing);
        }
    }

    private void RenderStructuralBlock(PdfWriter w, DelimitedBlockNode block, int indentLevel, FootnoteState footnotes)
    {
        // Draw a left border line for visual indication
        w.EnsurePage();
        float startY = w.CursorY;
        w.SetStrokeColor(0.7f, 0.7f, 0.7f);

        foreach (var child in block.Children)
            RenderBlock(w, child, indentLevel + 1, footnotes);

        float endY = w.CursorY;
        w.DrawLine(w.MarginLeftValue - 2, startY, w.MarginLeftValue - 2, endY, 1.5f);
        w.SetStrokeColor(0, 0, 0); // Reset to black
        w.MoveCursor(ParagraphSpacing);
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
                                float ww = w.MeasureText(word, _fontRegular, BodyFontSize) + 2 * cellPad;
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

        int startRow = 0;
        // Header row
        if (table.HasHeader && table.Children[0] is TableRowNode headerRow)
        {
            RenderTableRow(w, headerRow, colWidths, cellPadding, _fontBold, BodyFontSize, table.Columns);
            // Draw a line under header
            w.SetStrokeColor(0, 0, 0);
            w.DrawLine(w.MarginLeftValue, w.CursorY + BodyLeading - 2, w.MarginLeftValue + w.ContentWidth, w.CursorY + BodyLeading - 2, 1f);
            startRow = 1;
        }

        // Body rows
        for (int i = startRow; i < table.Children.Count; i++)
        {
            if (table.Children[i] is TableRowNode row)
            {
                w.EnsurePage();
                RenderTableRow(w, row, colWidths, cellPadding, _fontRegular, BodyFontSize, table.Columns);

                // Draw a light gray separator line between body rows
                if (i < table.Children.Count - 1)
                {
                    w.SetStrokeColor(0.85f, 0.85f, 0.85f);
                    w.DrawLine(w.MarginLeftValue, w.CursorY + BodyLeading - 2, w.MarginLeftValue + w.ContentWidth, w.CursorY + BodyLeading - 2, 0.25f);
                    w.SetStrokeColor(0, 0, 0);
                }
            }
        }

        w.MoveCursor(ParagraphSpacing);
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
        float rowHeight = maxLines * BodyLeading;

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
                        if (extraSpacing > 0 && extraSpacing < 8)
                        {
                            w.WriteJustifiedText(line, font, fontSize, x + cellPadding, lineY, extraSpacing);
                            lineY -= BodyLeading;
                            continue;
                        }
                    }
                }

                w.WriteText(line, font, fontSize, textX, lineY);
                lineY -= BodyLeading;
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
            w.WriteWrappedText(image.Title, _fontItalic, SmallFontSize, CodeLeading);
        }

        // Try to load and embed the actual image
        if (TryLoadImage(image.Target, out var imageInfo))
        {
            string imageRef = w.EmbedImage(imageInfo);

            // Scale to fit content width, maintaining aspect ratio
            // Use 72 dpi as the base resolution (1 point = 1 pixel at 72 dpi)
            float imgWidth = imageInfo.Width;
            float imgHeight = imageInfo.Height;
            float maxWidth = w.ContentWidth;
            float scale = Math.Min(1f, maxWidth / imgWidth);
            float displayWidth = imgWidth * scale;
            float displayHeight = imgHeight * scale;

            // Check if it fits on the current page
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
            w.WriteText(label, _fontItalic, BodyFontSize, w.MarginLeftValue + 8, w.CursorY - 35);
            w.MoveCursor(68f);
        }

        w.MoveCursor(ParagraphSpacing);
    }

    /// <summary>
    /// Tries to load an image file from the base directory and parse it.
    /// </summary>
    private bool TryLoadImage(string target, out ImageParser.ImageInfo info)
    {
        info = default;

        if (_baseDirectory is null)
            return false;

        string fullPath = Path.Combine(_baseDirectory, target);
        if (!File.Exists(fullPath))
            return false;

        byte[] data;
        try
        {
            data = File.ReadAllBytes(fullPath);
        }
        catch
        {
            return false;
        }

        // Try JPEG first, then PNG
        var result = ImageParser.TryParseJpeg(data) ?? ImageParser.TryParsePng(data);
        if (result is null)
            return false;

        info = result.Value;
        return true;
    }

    private void RenderBibliographyEntry(PdfWriter w, BibliographyEntryNode entry, FootnoteState footnotes)
    {
        w.EnsurePage();
        var label = entry.Label ?? entry.RefId;
        var segments = BuildInlineSegments(entry.Inlines, entry.Text, _fontRegular, BodyFontSize, footnotes);
        segments.Insert(0, new TextSegment($"[{label}] ", _fontBold, BodyFontSize));
        w.WriteWrappedSegments(segments, BodyLeading);
        w.MoveCursor(ParagraphSpacing / 2);
    }

    private void RenderDescriptionList(PdfWriter w, DescriptionListNode list, int indentLevel, FootnoteState footnotes)
    {
        foreach (var child in list.Children)
        {
            if (child is DescriptionItemNode item)
            {
                w.EnsurePage();

                // Term in bold
                var termSegments = BuildInlineSegments(item.TermInlines, item.Term, _fontBold, BodyFontSize, footnotes);
                w.WriteWrappedSegments(termSegments, BodyLeading);

                // Description indented
                var descSegments = BuildInlineSegments(item.DescriptionInlines, item.Description, _fontRegular, BodyFontSize, footnotes);
                w.WriteWrappedSegments(descSegments, BodyLeading);
                w.MoveCursor(ParagraphSpacing / 2);
            }
        }
        w.MoveCursor(ParagraphSpacing);
    }

    private void RenderAdmonition(PdfWriter w, AdmonitionNode admonition, int indentLevel, FootnoteState footnotes)
    {
        w.EnsurePage();

        // Admonition type label in bold
        w.WriteWrappedText($"{admonition.AdmonitionType}:", _fontBold, BodyFontSize, BodyLeading);

        if (admonition.Children.Count > 0)
        {
            foreach (var child in admonition.Children)
                RenderBlock(w, child, indentLevel, footnotes);
        }
        else
        {
            var segments = BuildInlineSegments(admonition.Inlines, admonition.Text ?? string.Empty,
                _fontRegular, BodyFontSize, footnotes);
            w.WriteWrappedSegments(segments, BodyLeading);
        }

        w.MoveCursor(ParagraphSpacing);
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
        string defaultFont, float defaultFontSize, FootnoteState footnotes)
    {
        switch (node)
        {
            case TextInlineNode text:
                segments.Add(new TextSegment(text.Value, defaultFont, defaultFontSize));
                break;

            case StrongInlineNode strong:
                foreach (var child in strong.Children)
                    AppendInlineSegments(segments, child, _fontBold, defaultFontSize, footnotes);
                break;

            case EmphasisInlineNode emphasis:
                foreach (var child in emphasis.Children)
                    AppendInlineSegments(segments, child, _fontItalic, defaultFontSize, footnotes);
                break;

            case MonospaceInlineNode monospace:
                foreach (var child in monospace.Children)
                    AppendInlineSegments(segments, child, _fontMono, defaultFontSize, footnotes);
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
                segments.Add(new TextSegment($"[{xrefLabel}]", defaultFont, defaultFontSize));
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

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Extracts plain text from inline nodes or falls back to raw text.</summary>
    private static string GetPlainText(IReadOnlyList<InlineNode> inlines, string fallback)
    {
        if (inlines.Count == 0) return fallback;

        var sb = new System.Text.StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextInlineNode t: sb.Append(t.Value); break;
                case StrongInlineNode s: sb.Append(s.Content); break;
                case EmphasisInlineNode e: sb.Append(e.Content); break;
                case MonospaceInlineNode m: sb.Append(m.Content); break;
                case LinkInlineNode l: sb.Append(l.Url); break;
                case InlineLinkMacroNode lm: sb.Append(lm.Label); break;
                case InlineImageNode img: sb.Append(img.Alt); break;
                case SuperscriptInlineNode sup: sb.Append(sup.Content); break;
                case SubscriptInlineNode sub: sb.Append(sub.Content); break;
                case PassthroughInlineNode pt: sb.Append(pt.Content); break;
                case CrossReferenceInlineNode xref: sb.Append(xref.Label ?? xref.Target); break;
                case FootnoteInlineNode fn: sb.Append(fn.Text ?? string.Empty); break;
                case InlineMacroNode macro: sb.Append(macro.Content); break;
            }
        }
        return sb.ToString();
    }
}
