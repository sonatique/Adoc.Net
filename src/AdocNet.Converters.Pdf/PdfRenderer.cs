using AdocNet;
using AdocNet.Ast;

namespace AdocNet.Converters.Pdf;

/// <summary>
/// Renders a <see cref="DocumentNode"/> AST to a PDF byte array.
/// Uses only managed code with the 14 standard PDF fonts (Helvetica, Courier).
/// Output is deterministic — fixed metadata, consistent object numbering.
/// </summary>
public sealed partial class PdfRenderer : DocumentRendererBase
{
    // ── Font configuration ──────────────────────────────────────────────
    // These default to the standard PDF font keys, but may be replaced
    // with embedded TrueType font keys when FontPath options are set.
    private string _fontRegular = "F1";        // Helvetica
    private string _fontBold = "F2";            // Helvetica-Bold
    private string _fontItalic = "F3";          // Helvetica-Oblique
    private string _fontMono = "F4";            // Courier

    // ── Size configuration (initialized from PdfRenderOptions) ─────────
    private float _titleFontSize = 24f;
    private float _h2FontSize = 20f;
    private float _h3FontSize = 16f;
    private float _h4FontSize = 14f;
    private float _h5FontSize = 12f;
    private float _bodyFontSize = 11f;
    private float _codeFontSize = 9f;
    private float _smallFontSize = 9f;

    private float _titleLeading = 30f;
    private float _headingLeading = 24f;
    private float _bodyLeading = 15f;
    private float _codeLeading = 12f;

    private const float ParagraphSpacing = 8f;
    private const float SectionSpacing = 16f;
    private const float ListIndent = 18f;
    private const float BlockIndent = 24f;

    // ── Visual styling (initialized from PdfRenderOptions) ──────────────
    private PdfColor? _linkColor;
    private PdfColor? _codeBackground;
    private float _admonitionBorderWidth = 2f;
    private bool _repeatTableHeader = true;

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

        // Initialize typography from options
        _titleFontSize = pdfOptions.TitleFontSize;
        _bodyFontSize = pdfOptions.FontSize;
        _codeFontSize = pdfOptions.CodeFontSize;
        _smallFontSize = pdfOptions.CodeFontSize;
        _h2FontSize = _titleFontSize * pdfOptions.HeadingScale;
        _h3FontSize = _h2FontSize * pdfOptions.HeadingScale;
        _h4FontSize = _h3FontSize * pdfOptions.HeadingScale;
        _h5FontSize = _h4FontSize * pdfOptions.HeadingScale;
        _titleLeading = _titleFontSize * pdfOptions.LineSpacing;
        _headingLeading = _h2FontSize * pdfOptions.LineSpacing;
        _bodyLeading = _bodyFontSize * pdfOptions.LineSpacing;
        _codeLeading = _codeFontSize * pdfOptions.LineSpacing;

        // Visual styling from options
        _linkColor = pdfOptions.LinkColor;
        _codeBackground = pdfOptions.CodeBackground;
        _admonitionBorderWidth = pdfOptions.AdmonitionBorderWidth;
        _repeatTableHeader = pdfOptions.RepeatTableHeader;

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
            w.WriteWrappedText(document.Title, _fontBold, _titleFontSize, _titleLeading);
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
            w.WriteWrappedText($"{number}. {text}", _fontRegular, _smallFontSize, _codeLeading);
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
            1 => (_h2FontSize, _headingLeading),
            2 => (_h3FontSize, _headingLeading),
            3 => (_h4FontSize, _headingLeading),
            _ => (_h5FontSize, _bodyLeading),
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

        var segments = BuildInlineSegments(paragraph.Inlines, paragraph.Text, _fontRegular, _bodyFontSize, footnotes);
        w.WriteWrappedSegments(segments, _bodyLeading);
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

                var segments = BuildInlineSegments(item.Inlines, item.Text, _fontRegular, _bodyFontSize, footnotes);
                if (segments.Count > 0)
                {
                    segments.Insert(0, new TextSegment(prefix, _fontRegular, _bodyFontSize));
                }

                w.WriteWrappedSegments(segments, _bodyLeading);

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
            w.WriteWrappedText(block.Title, _fontBold, _smallFontSize, _codeLeading);
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

        // Draw a light gray background sized from the visual content bounds.
        // The rect covers from above the first text's ascenders to below the
        // last text's descenders, with symmetric visual padding (gap) on each side.
        int totalLines = 0;
        foreach (var line in content.Split('\n'))
            totalLines += w.CountVerbatimLines(line, _fontMono, _codeFontSize);

        float ascent = _codeFontSize * 0.75f;
        float descent = _codeFontSize * 0.25f;
        float gap = 4f; // visual padding between rect edge and text extremes

        // Baseline-to-baseline distance across all lines (including language label)
        int lineSlots = totalLines + (block.Language is not null ? 1 : 0);
        float interline = lineSlots > 1 ? (lineSlots - 1) * _codeLeading : 0;

        // bgHeight = gap + ascent + interline + descent + gap
        float bgHeight = ascent + interline + descent + gap * 2;

        if (_codeBackground is { } bg)
        {
            w.SetFillColor(bg.R, bg.G, bg.B);
            w.DrawRect(w.MarginLeftValue - 4, w.CursorY - bgHeight, w.ContentWidth + 8, bgHeight, fill: true);
            w.SetFillColor(0, 0, 0);
        }

        // Move cursor to first text baseline: gap + ascent below rect top
        w.MoveCursor(gap + ascent);

        // Language label
        if (block.Language is not null)
        {
            w.WriteText(block.Language, _fontItalic, 8f, w.MarginLeftValue, w.CursorY);
            w.MoveCursor(_codeLeading);
        }

        foreach (var line in content.Split('\n'))
        {
            w.WriteWrappedVerbatimText(line, _fontMono, _codeFontSize, _codeLeading);
        }
        // Cursor is now _codeLeading below the last baseline. The rect bottom
        // is at CursorY_start - bgHeight. Move cursor to below rect + body leading.
        float cursorBelowRectBottom = _codeLeading - descent - gap;
        float remainingToRectBottom = cursorBelowRectBottom > 0 ? 0 : -cursorBelowRectBottom;
        w.MoveCursor(remainingToRectBottom + _bodyLeading);

        // Render callout list
        if (block.Callouts is { Count: > 0 })
        {
            int num = 1;
            foreach (var entry in block.Callouts)
            {
                w.EnsurePage();
                w.WriteWrappedText($"({num}) {entry.Text}", _fontRegular, _bodyFontSize, _bodyLeading);
                num++;
            }
            w.MoveCursor(ParagraphSpacing);
        }
    }

    private void RenderStructuralBlock(PdfWriter w, DelimitedBlockNode block, int indentLevel, FootnoteState footnotes)
    {
        w.EnsurePage();

        // Border line at the current left margin, text indented past it
        float borderX = w.MarginLeftValue;
        float indent = _admonitionBorderWidth + 6; // border width + gap
        float savedIndent = w.PushIndent(w.MarginLeftValue - w.MarginLeftBase + indent);

        float ascent = _bodyFontSize * 0.75f;
        float descent = _bodyFontSize * 0.25f;
        float lineTop = w.CursorY + ascent;
        w.SetStrokeColor(0.7f, 0.7f, 0.7f);

        foreach (var child in block.Children)
            RenderBlock(w, child, indentLevel + 1, footnotes);

        // Cursor is _bodyLeading + ParagraphSpacing below the last text baseline.
        // Line bottom = last baseline - descent.
        float lineBottom = w.CursorY + _bodyLeading + ParagraphSpacing - descent;
        w.DrawLine(borderX, lineTop, borderX, lineBottom, _admonitionBorderWidth);
        w.SetStrokeColor(0, 0, 0);

        w.PopIndent(savedIndent);
        w.MoveCursor(ParagraphSpacing);
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
