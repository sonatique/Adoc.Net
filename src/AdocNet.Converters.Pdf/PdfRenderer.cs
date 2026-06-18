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
    // Serializes concurrent renders on the same instance. Instance fields store
    // per-render state (fonts, sizes, counters) that would be clobbered if two
    // threads entered RenderDocument simultaneously.
    private readonly object _renderLock = new();

    // ── Font configuration ──────────────────────────────────────────────
    // These default to the standard PDF font keys, but may be replaced
    // with embedded TrueType font keys when FontPath options are set.
    private string _fontRegular = "F1";        // Helvetica
    private string _fontBold = "F2";            // Helvetica-Bold
    private string _fontItalic = "F3";          // Helvetica-Oblique
    private string _fontMono = "F4";            // Courier
    private string _fontMonoBold = "F5";        // Courier-Bold
    private string _fontMonoItalic = "F6";      // Courier-Oblique
    private string _fontMonoBoldItalic = "F7";  // Courier-BoldOblique
    private string _fontHeading = "F2";         // Heading font (defaults to bold)
    private string? _fontAwesome;                // FontAwesome 5 Solid for admonition / inline icons
    private string? _fontAwesomeRegular;          // FontAwesome 5 Regular (TIP fa-lightbulb etc.)

    // ── Size configuration (initialized from PdfRenderOptions) ─────────
    private float _titleFontSize = 24f;
    private PdfAlignment _titleAlignment = PdfAlignment.Left;
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

    private float _paragraphSpacingBefore;
    private float _paragraphSpacingAfter = 12f;
    private float _sectionSpacing = 16f;
    private float _titleMarginTop;
    private float _titleMarginBottom = 16f;
    private float _titleFirstPageTop = 36f;
    private float _pageHeight;
    private float _marginTop;
    private float _h2MarginBottom = 4f;
    private float _h3MarginBottom = 4f;
    private float _h4MarginBottom = 4f;
    private float _h5MarginBottom = 4f;
    private const float ListIndent = 18f;
    private float _blockIndent = 24f;

    // ── Visual styling (initialized from PdfRenderOptions) ──────────────
    private PdfColor? _linkColor;
    private PdfColor? _codespanColor;
    private PdfColor? _codeBackground;

    /// <summary>
    /// Resets the writer's fill color to the body color (when configured) so
    /// subsequent text doesn't render in the leftover heading color. Use
    /// instead of SetFillColor(0,0,0) wherever we want to "return to normal".
    /// </summary>
    private void RestoreBodyFill(PdfWriter w)
    {
        if (_bodyColor is { } bc) w.SetFillColor(bc.R, bc.G, bc.B);
        else w.SetFillColor(0, 0, 0);
    }

    private PdfColor? _codespanBackground;
    private float _admonitionBorderWidth = 2f;
    private SyntaxColorScheme? _syntaxColors;
    private PdfColor? _headingColor;  // used for document title and as fallback
    private PdfColor? _h2Color;
    private PdfColor? _h3Color;
    private PdfColor? _h4Color;
    private PdfColor? _h5Color;
    private PdfColor? _bodyColor;
    private PdfColor? _tableHeaderBackground;
    private PdfColor? _tableBorderColor;
    private PdfColor? _tableHeaderFontColor;
    private PdfColor? _codeBorderColor;
    private bool _repeatTableHeader = true;
    private bool _hasTrueTypeBodyFont;
    private string? _runningContentStartAt;

    // ── Section numbering ──────────────────────────────────────────────
    private bool _sectnumsEnabled;
    private bool _useIconAdmonitions;
    private int _sectnumMaxLevel = 3;
    private readonly int[] _sectionCounters = new int[6];

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
        lock (_renderLock)
        {
        // Two-pass rendering when a TOC is present: pass 1 captures section
        // page numbers into a discarded MemoryStream, pass 2 renders to the
        // real output stream with destinations pre-seeded so the TOC renderer
        // can emit "Title ............ N" with correct page numbers.
        bool hasToc = false;
        foreach (var c in context.Document.Children)
        {
            if (c is TocNode) { hasToc = true; break; }
        }
        Dictionary<string, int>? seededPages = null;
        if (hasToc)
        {
            using var probe = new MemoryStream();
            RenderToStreamCore(context, probe, seedDestinations: null, capturedPages: out seededPages);
        }
        RenderToStreamCore(context, output, seededPages, capturedPages: out _);
        } // lock
    }

    private void RenderToStreamCore(RenderContext context, Stream output,
        IReadOnlyDictionary<string, int>? seedDestinations,
        out Dictionary<string, int> capturedPages)
    {
        var pdfOptions = context.Options as PdfRenderOptions ?? PdfRenderOptions.Default;
        var writer = new PdfWriter(pdfOptions.PageWidth, pdfOptions.PageHeight,
            pdfOptions.MarginLeft, pdfOptions.MarginRight, pdfOptions.MarginTop, pdfOptions.MarginBottom);
        if (seedDestinations is not null)
            writer.SeedDestinations(seedDestinations);

        writer.ShowPageNumbers = pdfOptions.ShowPageNumbers;
        writer.HeaderTemplate = pdfOptions.HeaderText;
        writer.FooterTemplate = pdfOptions.FooterText ?? (pdfOptions.ShowPageNumbers ? "Page {page}" : null);
        writer.HeaderFontSize = pdfOptions.HeaderFontSize;
        writer.FooterFontSize = pdfOptions.FooterFontSize;
        writer.HeaderFontColor = pdfOptions.HeaderFontColor;
        writer.FooterFontColor = pdfOptions.FooterFontColor;
        writer.HeaderAlignment = pdfOptions.HeaderAlignment;
        // Load footer SVG image if specified
        if (pdfOptions.FooterImagePath is not null && File.Exists(pdfOptions.FooterImagePath))
        {
            try
            {
                var svgData = File.ReadAllBytes(pdfOptions.FooterImagePath);
                writer.FooterImage = SvgParser.Parse(svgData);
                writer.FooterImageWidth = pdfOptions.FooterImageWidth;
            }
            catch { /* Ignore SVG load errors */ }
        }
        writer.FooterAlignment = pdfOptions.FooterAlignment;
        writer.HeaderHeight = pdfOptions.HeaderHeight;
        writer.FooterHeight = pdfOptions.FooterHeight;
        writer.DocumentTitle = context.Document.Title;
        if (context.Document.Attributes.TryGetValue("header-title", out var ht))
            writer.HeaderTitle = ht;

        _baseDirectory = pdfOptions.BaseDirectory;
        _hasTrueTypeBodyFont = false;
        _runningContentStartAt = pdfOptions.RunningContentStartAt;

        // Initialize typography from options
        _titleFontSize = pdfOptions.TitleFontSize;
        _titleAlignment = pdfOptions.TitleAlignment;
        _bodyFontSize = pdfOptions.FontSize;
        _codeFontSize = pdfOptions.CodeFontSize;
        _smallFontSize = pdfOptions.CodeFontSize;

        // Per-heading sizes: explicit overrides take priority over HeadingScale calculation
        float scaledH2 = _titleFontSize * pdfOptions.HeadingScale;
        float scaledH3 = scaledH2 * pdfOptions.HeadingScale;
        float scaledH4 = scaledH3 * pdfOptions.HeadingScale;
        float scaledH5 = scaledH4 * pdfOptions.HeadingScale;
        _h2FontSize = pdfOptions.Heading2FontSize ?? scaledH2;
        _h3FontSize = pdfOptions.Heading3FontSize ?? scaledH3;
        _h4FontSize = pdfOptions.Heading4FontSize ?? scaledH4;
        _h5FontSize = pdfOptions.Heading5FontSize ?? scaledH5;

        // Asciidoctor-pdf multiplies the theme's line_height by the font's
        // natural built-in leading factor (NotoSerif's is 1.36). AdocNet's
        // writer skips that addition for built-in fonts (Helvetica/Courier
        // metrics differ), so only apply the factor when an embedded body
        // font is in play. Detected up front by checking the font path —
        // RegisterEmbeddedFont happens later but the option is known now.
        bool willEmbedBodyFont = pdfOptions.FontPath is not null && File.Exists(pdfOptions.FontPath);
        if (willEmbedBodyFont)
        {
            // Asciidoctor-pdf parity: empirically calibrated against asciidoctor-pdf
            // user-manual output (NotoSerif 10.5pt). REF baseline-to-baseline of
            // 15.78pt for body and 22pt heading bbox = font_size × 1.0 (heading.line_height=1).
            // Body factor ~1.503: 1.143 (theme) × 1.315.
            const float BodyLeadingFactor = 1.315f;
            _titleLeading = _titleFontSize * (pdfOptions.TitleLineHeight ?? 1f) * 1.36f;
            _headingLeading = _h2FontSize * 1.0f; // line_height: 1 (compact)
            _bodyLeading = _bodyFontSize * pdfOptions.LineSpacing * BodyLeadingFactor;
        }
        else
        {
            // Built-in fonts (Helvetica/Courier): keep the legacy multiplier
            // chain so default-theme regression tests stay locked.
            _titleLeading = _titleFontSize * (pdfOptions.TitleLineHeight ?? pdfOptions.LineSpacing);
            _headingLeading = _h2FontSize * pdfOptions.LineSpacing;
            _bodyLeading = _bodyFontSize * pdfOptions.LineSpacing;
        }
        _codeLeading = _codeFontSize * pdfOptions.LineSpacing;
        // Set BodyLeading on writer AFTER it's computed for this render (writer uses it
        // in StartPage to position the first text baseline below the top margin).
        writer.BodyLeading = _bodyLeading;

        // Visual styling from options
        _linkColor = pdfOptions.LinkColor;
        writer.LinkColor = pdfOptions.LinkColor;
        _codespanColor = pdfOptions.CodespanColor;
        writer.BodyColor = pdfOptions.BodyColor;
        _codeBackground = pdfOptions.CodeBackground;
        _codespanBackground = pdfOptions.CodespanBackground;
        _codeBorderColor = pdfOptions.CodeBorderColor;
        _admonitionBorderWidth = pdfOptions.AdmonitionBorderWidth;
        _repeatTableHeader = pdfOptions.RepeatTableHeader;

        // Syntax highlighting and styling
        _syntaxColors = pdfOptions.SyntaxColors;
        _headingColor = pdfOptions.HeadingColor;
        _h2Color = pdfOptions.Heading2Color ?? pdfOptions.HeadingColor;
        _h3Color = pdfOptions.Heading3Color ?? pdfOptions.HeadingColor;
        _h4Color = pdfOptions.Heading4Color ?? pdfOptions.HeadingColor;
        _h5Color = pdfOptions.Heading5Color ?? pdfOptions.HeadingColor;
        _bodyColor = pdfOptions.BodyColor;
        _tableHeaderBackground = pdfOptions.TableHeaderBackground;
        _tableBorderColor = pdfOptions.TableBorderColor;
        _tableHeaderFontColor = pdfOptions.TableHeaderFontColor;
        _sectionSpacing = pdfOptions.SectionSpacing;
        _titleMarginTop = pdfOptions.TitleMarginTop;
        _titleMarginBottom = pdfOptions.TitleMarginBottom;
        _titleFirstPageTop = pdfOptions.TitleFirstPageTop;
        _pageHeight = pdfOptions.PageHeight;
        _marginTop = pdfOptions.MarginTop;
        _blockIndent = pdfOptions.BlockIndent;

        // Typography options
        writer.HyphenationEnabled = pdfOptions.EnableHyphenation;
        _paragraphSpacingBefore = pdfOptions.ParagraphSpacingBefore;
        _paragraphSpacingAfter = pdfOptions.ParagraphSpacingAfter;
        float defaultHeadingMarginBottom = _paragraphSpacingAfter / 2;
        _h2MarginBottom = pdfOptions.Heading2MarginBottom ?? defaultHeadingMarginBottom;
        _h3MarginBottom = pdfOptions.Heading3MarginBottom ?? defaultHeadingMarginBottom;
        _h4MarginBottom = pdfOptions.Heading4MarginBottom ?? defaultHeadingMarginBottom;
        _h5MarginBottom = pdfOptions.Heading5MarginBottom ?? defaultHeadingMarginBottom;

        // Register embedded TrueType fonts if configured
        _fontRegular = "F1";
        _fontBold = "F2";
        _fontItalic = "F3";
        _fontMono = "F4";
        _fontMonoBold = "F5";
        _fontMonoItalic = "F6";
        _fontMonoBoldItalic = "F7";

        if (pdfOptions.FontPath is not null && File.Exists(pdfOptions.FontPath))
        {
            var font = TrueTypeFont.Parse(File.ReadAllBytes(pdfOptions.FontPath));
            _fontRegular = writer.RegisterEmbeddedFont("F1", font);
            _hasTrueTypeBodyFont = true;
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

        if (pdfOptions.MonoBoldFontPath is not null && File.Exists(pdfOptions.MonoBoldFontPath))
        {
            var font = TrueTypeFont.Parse(File.ReadAllBytes(pdfOptions.MonoBoldFontPath));
            _fontMonoBold = writer.RegisterEmbeddedFont("F5", font);
        }

        if (pdfOptions.MonoItalicFontPath is not null && File.Exists(pdfOptions.MonoItalicFontPath))
        {
            var font = TrueTypeFont.Parse(File.ReadAllBytes(pdfOptions.MonoItalicFontPath));
            _fontMonoItalic = writer.RegisterEmbeddedFont("F6", font);
        }

        if (pdfOptions.MonoBoldItalicFontPath is not null && File.Exists(pdfOptions.MonoBoldItalicFontPath))
        {
            var font = TrueTypeFont.Parse(File.ReadAllBytes(pdfOptions.MonoBoldItalicFontPath));
            _fontMonoBoldItalic = writer.RegisterEmbeddedFont("F7", font);
        }

        // Heading font — separate from body bold, used for section titles
        _fontHeading = _fontBold; // default: same as bold
        if (pdfOptions.HeadingFontPath is not null && File.Exists(pdfOptions.HeadingFontPath))
        {
            var font = TrueTypeFont.Parse(File.ReadAllBytes(pdfOptions.HeadingFontPath));
            _fontHeading = writer.RegisterEmbeddedFont("FH", font);
        }

        // :icons: any non-empty value enables icon-style admonitions (asciidoctor
        // treats `:icons: font` as the canonical opt-in but the legacy `:icons:`
        // bare attribute also activates them).
        _useIconAdmonitions = context.Document.Attributes.ContainsKey("icons");

        // FontAwesome 5 Solid + Regular embedded as resources — used to draw
        // admonition icons (fa-info-circle, fa-warning, etc.) and inline icon:
        // macros with the exact same glyphs asciidoctor-pdf ships. Loaded only
        // when icons are actually used; null if loading fails or icons aren't
        // enabled (drawn-glyph fallback then kicks in).
        // TIP admonition uses far-lightbulb (FA Regular variant) — different
        // outline-style design than the solid version.
        _fontAwesome = _useIconAdmonitions
            ? TryLoadEmbeddedFont(writer, "AdocNet.Converters.Pdf.Resources.fa-solid.ttf", "FA")
            : null;
        _fontAwesomeRegular = _useIconAdmonitions
            ? TryLoadEmbeddedFont(writer, "AdocNet.Converters.Pdf.Resources.fa-regular.ttf", "FAR")
            : null;

        // Section numbering
        _sectnumsEnabled = context.Document.Attributes.ContainsKey("sectnums");
        _sectnumMaxLevel = 3;
        if (context.Document.Attributes.TryGetValue("sectnumlevels", out var snl)
            && int.TryParse(snl, out var snlv) && snlv >= 0)
            _sectnumMaxLevel = snlv;
        Array.Clear(_sectionCounters, 0, _sectionCounters.Length);

        var footnotes = context.GetOrCreate(() => new FootnoteState());

        writer.StartPage();
        // Set the document-wide body fill color ONCE up front so default text
        // (lists, paragraphs, TOC entries) renders in the configured body color
        // instead of falling back to pure black after a heading reset.
        RestoreBodyFill(writer);
        RenderDocumentContent(writer, context.Document, footnotes);
        RenderFootnotesSection(writer, footnotes);

        capturedPages = writer.CaptureDestinationPages();
        byte[] bytes = writer.ToBytes();
        output.Write(bytes, 0, bytes.Length);
    }

    // ── Document rendering ──────────────────────────────────────────────

    private void RenderDocumentContent(PdfWriter w, DocumentNode document, FootnoteState footnotes)
    {
        if (document.Title is not null)
        {
            w.EnsurePage();
            // Position the document title at TitleFirstPageTop from the top of
            // page 1 (matches asciidoctor-pdf's 0.5in title offset). The cursor
            // is currently at MarginTop after EnsurePage; move it so the title
            // top lands at the requested offset. Subsequent pages still use the
            // standard MarginTop for body content. TitleMarginTop is skipped on
            // page 1 — TitleFirstPageTop fully specifies the position.
            float deltaToFirstPageTop = _titleFirstPageTop - _marginTop;
            if (deltaToFirstPageTop != 0)
                w.MoveCursor(deltaToFirstPageTop);
            // Compensate for ReserveFirstLineLeading inside WriteWrappedText: it adds
            // (titleLeading - BodyLeading) to push the ascender below the top margin,
            // but for an explicit-position title we want bbox top = titleFirstPageTop,
            // i.e. baseline = titleFirstPageTop + ascender. Cancel the reservation
            // overshoot (titleLeading - ascender ≈ 0.291 × fontSize for NotoSerif).
            float ascenderApprox = _titleFontSize * 1.069f;
            float overshoot = _titleLeading - ascenderApprox;
            if (overshoot > 0)
                w.MoveCursor(-overshoot);
            // Register document title as outline entry. Use level 1 so it appears
            // as a sibling of top-level sections (matches Asciidoctor's flat outline).
            w.AddOutlineEntry(document.Title, 1, "_document_title");
            if (_headingColor is { } hc) w.SetFillColor(hc.R, hc.G, hc.B);
            w.WriteWrappedText(document.Title, _fontHeading, _titleFontSize, _titleLeading, _titleAlignment);
            if (_headingColor is not null) RestoreBodyFill(w);
            w.MoveCursor(_titleMarginBottom);
        }

        // Find TocNode if present — render it after content to get page numbers
        TocNode? tocNode = null;
        foreach (var child in document.Children)
        {
            if (child is TocNode toc)
                tocNode = toc;
        }

        // Render TOC placeholder or actual TOC
        if (tocNode is not null && tocNode.Entries.Count > 0)
        {
            RenderToc(w, tocNode, footnotes);
        }

        // If running-content starts after-toc, suppress headers/footers until now
        if (_runningContentStartAt == "after-toc")
            w.RunningContentStartPage = w.CurrentPageNumber;

        foreach (var child in document.Children)
        {
            if (child is TocNode) continue; // already rendered above
            RenderBlock(w, child, indentLevel: 0, footnotes);
        }
    }

    private void RenderFootnotesSection(PdfWriter w, FootnoteState footnotes)
    {
        if (footnotes.Footnotes.Count == 0) return;

        w.MoveCursor(_sectionSpacing);
        w.EnsurePage();

        // Draw a horizontal rule
        w.SetStrokeColor(0.5f, 0.5f, 0.5f);
        w.DrawLine(w.MarginLeftValue, w.CursorY, w.MarginLeftValue + w.ContentWidth, w.CursorY, 0.5f);
        w.SetStrokeColor(0, 0, 0);
        w.MoveCursor(_paragraphSpacingAfter);

        foreach (var (number, _, node) in footnotes.Footnotes)
        {
            w.EnsurePage();
            var text = GetPlainText(node.Inlines, node.Text ?? string.Empty);
            w.WriteWrappedText($"{number}. {text}", _fontRegular, _smallFontSize, _codeLeading);
        }
    }

    // ── TOC rendering ──────────────────────────────────────────────────

    private void RenderToc(PdfWriter w, TocNode toc, FootnoteState footnotes)
    {
        // TOC title
        w.EnsurePage();
        if (_headingColor is { } hc) w.SetFillColor(hc.R, hc.G, hc.B);
        w.WriteWrappedText("Table of Contents", _fontHeading, _h2FontSize, _headingLeading);
        if (_headingColor is not null) RestoreBodyFill(w);
        // Asciidoctor-pdf uses tighter spacing here than after a body paragraph:
        // ref baseline-to-baseline (TOC heading→first entry) ≈ 30.5pt.
        // With heading_leading 22pt + this MoveCursor, total ≈ 30.5 ⇒ 8.5pt move.
        w.MoveCursor(_paragraphSpacingAfter / 2f);

        // Asciidoctor-pdf renders TOC entries in body text color (not link
        // blue). Suppress link coloring for the TOC and restore afterwards.
        // Also set fill color to the body color so the TOC renders in #333333
        // rather than the default black left over from prior heading rendering.
        var savedLinkColor = w.LinkColor;
        w.LinkColor = null;
        RestoreBodyFill(w);
        RenderTocEntries(w, toc.Entries, 0, parentNumber: "");
        w.LinkColor = savedLinkColor;
        // Trailing space matches asciidoctor-pdf TOC→first-section gap (~19pt).
        // Combined with RenderSection's _sectionSpacing prefix, this lands near
        // the REF measurement.
        w.MoveCursor(_paragraphSpacingAfter);
    }

    private void RenderTocEntries(PdfWriter w, IReadOnlyList<TocEntry> entries, int depth, string parentNumber)
    {
        // Asciidoctor-pdf TOC line spacing measured at ~18.5pt baseline-to-baseline
        // for 10.5pt body (factor ~1.76). Theme says toc.line-height: 1.4 but the
        // observed visual leading is wider; matches REF measurement.
        float tocLeading = _bodyFontSize * 1.76f;

        int counter = 0;
        foreach (var entry in entries)
        {
            counter++;
            // Compute the section number prefix when :sectnums: is enabled.
            // Concatenates parent + counter, e.g. "1.", "1.1.", "1.1.1.".
            string numberPrefix = "";
            if (_sectnumsEnabled && entry.Level <= _sectnumMaxLevel)
                numberPrefix = parentNumber.Length > 0
                    ? $"{parentNumber}{counter}. "
                    : $"{counter}. ";

            w.EnsurePage();

            float indent = depth * ListIndent;
            float savedIndent = w.PushIndent(indent);

            // Asciidoctor-pdf renders TOC entries in regular weight at the body
            // font size for all levels — indentation is the only differentiator.
            float fontSize = _bodyFontSize;
            string font = _fontRegular;

            // Look up the page number for this section's id. Populated by the
            // first pass of two-pass rendering (see RenderToStreamCore). When
            // present, emit "Title ............ N" with dot-leader filling the
            // line — matches asciidoctor-pdf.
            int? page = entry.Id is not null ? w.GetDestinationPage(entry.Id) : null;
            var displayTitle = numberPrefix + entry.Title;
            if (page is int pageNum)
            {
                var pageText = pageNum.ToString();
                float titleWidth = w.MeasureText(displayTitle, font, fontSize);
                float pageWidth = w.MeasureText(pageText, _fontRegular, fontSize);
                float spaceWidth = w.MeasureText(" ", _fontRegular, fontSize);
                // ". " (dot+space) leader unit, matching asciidoctor-pdf.
                float unitWidth = w.MeasureText(". ", _fontRegular, fontSize);

                // Pin the page number's right edge to the content right margin so it
                // aligns across ALL entries regardless of nesting depth. MarginLeftValue
                // + ContentWidth is constant under indentation (the indent shifts the
                // left edge in and narrows ContentWidth by the same amount), so the
                // right edge does not move. Previously the page number floated after a
                // dot leader whose integer-dot rounding left a per-entry remainder, so
                // nested entries' numbers sat a few points left of the top-level ones (#51).
                float leftX = w.MarginLeftValue;            // indented title start
                float rightEdge = w.MarginLeftValue + w.ContentWidth; // fixed content right margin
                float pageX = rightEdge - pageWidth;        // page number, right-aligned
                float titleEnd = leftX + titleWidth;
                string? titleLink = entry.Id is not null ? $"#internal#{entry.Id}" : null;

                if (titleEnd + 2 * spaceWidth >= pageX)
                {
                    // Title (with number) is too wide to share the line — let it wrap;
                    // append the page number rather than risk an overlap.
                    var segments = new List<TextSegment>
                    {
                        new(displayTitle, font, fontSize, titleLink),
                        new(" " + pageText, _fontRegular, fontSize, null),
                    };
                    w.WriteWrappedSegments(segments, tocLeading, justify: false);
                }
                else
                {
                    w.EnsurePage();
                    w.ReserveFirstLineLeading(tocLeading);
                    float y = w.CursorY;

                    // Title (carries the clickable internal link).
                    w.WriteTextSegments(
                        new List<TextSegment> { new(displayTitle, font, fontSize, titleLink) }, leftX, y);

                    // Dot leader ending just before the page number (so the dots lead
                    // up to it and any rounding remainder sits next to the title).
                    float gapStart = titleEnd + spaceWidth;
                    float gapEnd = pageX - spaceWidth;
                    if (gapEnd > gapStart && unitWidth > 0)
                    {
                        int dots = (int)((gapEnd - gapStart) / unitWidth);
                        if (dots > 0)
                        {
                            var leader = string.Concat(System.Linq.Enumerable.Repeat(". ", dots));
                            float leaderWidth = w.MeasureText(leader, _fontRegular, fontSize);
                            w.WriteText(leader, _fontRegular, fontSize, gapEnd - leaderWidth, y);
                        }
                    }

                    // Page number, right-aligned at the fixed content margin.
                    w.WriteText(pageText, _fontRegular, fontSize, pageX, y);
                    w.MoveCursor(tocLeading);
                }
            }
            else
            {
                // No registered page (e.g. for entries that aren't section anchors).
                var segments = new List<TextSegment>
                {
                    new(displayTitle, font, fontSize, entry.Id is not null ? $"#internal#{entry.Id}" : null)
                };
                w.WriteWrappedSegments(segments, tocLeading, justify: false);
            }
            w.PopIndent(savedIndent);

            // Recurse into children with this entry's accumulated number as the
            // parent for nested numbering ("1." → "1.1", "1.1.1", ...).
            if (entry.Children.Count > 0)
            {
                var childParent = _sectnumsEnabled && entry.Level <= _sectnumMaxLevel
                    ? (parentNumber.Length > 0 ? $"{parentNumber}{counter}." : $"{counter}.")
                    : "";
                RenderTocEntries(w, entry.Children, depth + 1, childParent);
            }
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
            case TocNode:
                // TOC is rendered in RenderDocumentContent, skip here
                break;
            case PageBreakNode:
                w.StartPage();
                break;
            case ThematicBreakNode:
                w.EnsurePage();
                w.SetStrokeColor(0.5f, 0.5f, 0.5f);
                w.DrawLine(w.MarginLeftValue, w.CursorY, w.MarginLeftValue + w.ContentWidth, w.CursorY, 0.5f);
                w.SetStrokeColor(0, 0, 0);
                w.MoveCursor(_paragraphSpacingAfter);
                break;
        }
    }

    private void RenderSection(PdfWriter w, SectionNode section, int indentLevel, FootnoteState footnotes)
    {
        w.MoveCursor(_sectionSpacing);
        w.EnsurePage();

        // Per-level leading and margin-bottom calibrated against asciidoctor-pdf:
        // heading uses tight leading (font_size × 1.0, matching theme heading.line_height=1).
        // Per-level margin-bottom is set so total advance (leading + margin_bottom)
        // matches REF baseline-to-baseline (29.20pt for h1 22pt, 27.98pt for h2 18pt).
        float headingFactor = _headingLeading > 0 && _h2FontSize > 0
            ? _headingLeading / _h2FontSize : 1.0f;
        var (fontSize, leading, marginBottom, color) = section.Level switch
        {
            1 => (_h2FontSize, _h2FontSize * headingFactor, _h2MarginBottom, _h2Color),
            2 => (_h3FontSize, _h3FontSize * headingFactor, _h3MarginBottom, _h3Color),
            3 => (_h4FontSize, _h4FontSize * headingFactor, _h4MarginBottom, _h4Color),
            _ => (_h5FontSize, _h5FontSize * headingFactor, _h5MarginBottom, _h5Color),
        };

        // Build section number prefix (e.g. "1. ", "1.2. ")
        string? secNumPrefix = null;
        var numbering = section.SectnumsEnabled ?? _sectnumsEnabled;
        if (numbering && !section.IsDiscrete && section.Level >= 1 && section.Level <= _sectnumMaxLevel)
        {
            int idx = section.Level - 1;
            _sectionCounters[idx]++;
            for (int i = idx + 1; i < _sectionCounters.Length; i++)
                _sectionCounters[i] = 0;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i <= idx; i++)
            {
                sb.Append(_sectionCounters[i]);
                sb.Append('.');
            }
            sb.Append(' ');
            secNumPrefix = sb.ToString();
        }

        // Register as outline entry and named destination for bookmarks/cross-refs.
        // Include section number prefix in outline title when sectnums is enabled.
        string outlineTitle = secNumPrefix is not null ? $"{secNumPrefix}{section.Title}" : section.Title;
        w.AddOutlineEntry(outlineTitle, section.Level, section.Id);

        // Track section title for header/footer {section-title} placeholder.
        // Only level-1 sections (== headings) are tracked, matching Asciidoctor behavior.
        // Include the section number prefix when sectnums is enabled.
        // SetSectionTitleForPage only takes effect on the FIRST level-1 section per page;
        // subsequent sections on the same page don't override (matches Asciidoctor).
        if (section.Level == 1)
            w.SetSectionTitleForPage(secNumPrefix is not null ? $"{secNumPrefix}{section.Title}" : section.Title);

        // Render section title with heading font, per-level color
        if (color is { } hc) w.SetFillColor(hc.R, hc.G, hc.B);
        var segments = BuildInlineSegments(section.TitleInlines, section.Title, _fontHeading, fontSize, footnotes);
        if (secNumPrefix is not null)
            segments.Insert(0, new TextSegment(secNumPrefix, _fontHeading, fontSize));
        w.WriteWrappedSegments(segments, leading);
        if (color is not null) RestoreBodyFill(w);
        w.MoveCursor(marginBottom);

        foreach (var child in section.Children)
            RenderBlock(w, child, indentLevel, footnotes);
    }

    private void RenderParagraph(PdfWriter w, ParagraphNode paragraph, int indentLevel, FootnoteState footnotes)
    {
        if (_paragraphSpacingBefore > 0)
            w.MoveCursor(_paragraphSpacingBefore);
        w.EnsurePage();
        w.EnsureSpaceForLine(_bodyLeading);

        // Body color is set globally at start of document; no per-paragraph
        // override needed unless we change colors mid-paragraph.
        var segments = BuildInlineSegments(paragraph.Inlines, paragraph.Text, _fontRegular, _bodyFontSize, footnotes);
        w.WriteWrappedSegments(segments, _bodyLeading);
        w.MoveCursor(_paragraphSpacingAfter);
    }

    private void RenderList(PdfWriter w, ListNode list, int indentLevel, FootnoteState footnotes)
    {
        // Indent nested lists from their parent (additive to current indent)
        float savedIndent = indentLevel > 0
            ? w.PushIndent(w.MarginLeftValue - w.MarginLeftBase + ListIndent)
            : -1;

        int itemNumber = 1;
        foreach (var child in list.Children)
        {
            if (child is ListItemNode item)
            {
                w.EnsurePage();
                // Reserve space for the line we're about to draw — prevents the line
                // from rendering with descender intruding into the footer area
                // (or the line clipping below the page bottom margin).
                w.EnsureSpaceForLine(_bodyLeading);

                // Bullet or number prefix
                // Standard PDF fonts lack the bullet glyph; use it only when a TrueType body font is embedded.
                // Asciidoctor-pdf uses different bullets per nesting level:
                // level 0: filled disc (•), level 1: hollow circle (◦), level 2+: filled square (▪)
                string prefix;
                if (list.ListKind == ListKind.Unordered)
                {
                    if (_hasTrueTypeBodyFont)
                    {
                        // Checklist items: use Unicode ballot boxes (matches asciidoctor-pdf
                        // exactly — same characters in NotoSerif).
                        if (item.Checked == true)
                            prefix = "\u2611 "; // ☑ ballot box with check
                        else if (item.Checked == false)
                            prefix = "\u2610 "; // ☐ ballot box
                        else
                            prefix = indentLevel switch
                            {
                                0 => "\u2022 ",  // • filled disc
                                1 => "\u25E6 ",  // ◦ hollow circle (white bullet)
                                _ => "\u25AA ",  // ▪ filled small square
                            };
                    }
                    else
                    {
                        prefix = item.Checked == true ? "[x] "
                              : item.Checked == false ? "[ ] "
                              : "- ";
                    }
                }
                else
                {
                    prefix = $"{itemNumber}. ";
                }

                var segments = BuildInlineSegments(item.Inlines, item.Text, _fontRegular, _bodyFontSize, footnotes);
                if (segments.Count > 0)
                {
                    segments.Insert(0, new TextSegment(prefix, _fontRegular, _bodyFontSize));
                }

                w.WriteWrappedSegments(segments, _bodyLeading);
                // Asciidoctor-pdf adds vertical breathing room between list
                // items (matches its block.margin_bottom = vertical_rhythm).
                // Without this each bullet runs straight into the next.
                w.MoveCursor(_paragraphSpacingAfter / 2);

                // Render child blocks (list continuation: code blocks, paragraphs, etc.)
                // and nested lists with additional indentation
                foreach (var nested in item.Children)
                {
                    if (nested is ListNode nestedList)
                    {
                        RenderList(w, nestedList, indentLevel + 1, footnotes);
                    }
                    else
                    {
                        // Asciidoctor-pdf adds extra breathing room above continuation
                        // delimited blocks (code/listing/example) within list items —
                        // the visible gap is about half a vertical_rhythm wider than
                        // a plain paragraph continuation. Match the REF measurement.
                        if (nested is DelimitedBlockNode db &&
                            (db.BlockKind == DelimitedBlockKind.Listing
                             || db.BlockKind == DelimitedBlockKind.Source
                             || db.BlockKind == DelimitedBlockKind.Literal))
                        {
                            w.MoveCursor(_paragraphSpacingAfter / 2);
                        }
                        // Push indent so continuation blocks (code, paragraphs) render
                        // at the same indentation as their parent list item's text
                        float contSaved = w.PushIndent(w.MarginLeftValue - w.MarginLeftBase + ListIndent);
                        RenderBlock(w, nested, indentLevel + 1, footnotes);
                        w.PopIndent(contSaved);
                    }
                }

                itemNumber++;
            }
        }
        if (savedIndent >= 0)
            w.PopIndent(savedIndent);
        // Last item already added paragraphSpacingAfter/2 inside the loop; add
        // the other half here so total trailing space matches asciidoctor-pdf
        // (a list ends with a tighter gap than a paragraph because list items
        // already self-space).
        w.MoveCursor(_paragraphSpacingAfter / 2);
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

        // Enable per-line background drawing (handles page breaks correctly)
        if (_codeBackground is { } bg)
            w.BeginCodeBlockBackground(bg, _codeFontSize, _codeLeading, _codeBorderColor);

        // Asciidoctor-pdf doesn't print the language label inside the code
        // block — the syntax highlighter's color cues are the only visual
        // language hint. Emitting the label takes a line of vertical space
        // and adds visual noise that doesn't appear in the asciidoctor PDF.

        // Render content: highlighted if supported, plain monospace otherwise
        if (_syntaxColors is not null && block.BlockKind == DelimitedBlockKind.Source
            && block.Language is not null
            && Highlighting.SyntaxTokenizer.IsLanguageSupported(block.Language))
        {
            RenderHighlightedVerbatim(w, content, block.Language);
        }
        else
        {
            foreach (var line in content.Split('\n'))
                w.WriteWrappedVerbatimText(line, _fontMono, _codeFontSize, _codeLeading);
        }

        w.EndCodeBlockBackground();
        w.MoveCursor(_bodyLeading);

        // Render callout list — asciidoctor-pdf renders the conum (callout
        // number) as a circled-number glyph (U+2460 ① for 1, U+2461 ② for 2,
        // etc.) in the codespan (mono) font with the codespan accent color
        // (#B12146). Numbers >20 fall back to "(N)".
        if (block.Callouts is { Count: > 0 })
        {
            int num = 1;
            foreach (var entry in block.Callouts)
            {
                w.EnsurePage();
                var conumGlyph = num <= 20
                    ? char.ConvertFromUtf32(0x245F + num) // U+2460 = ①, +1 each
                    : $"({num})";
                var conumColor = _codespanColor ?? new PdfColor(0.694f, 0.129f, 0.275f); // #B12146
                var segments = new List<TextSegment>
                {
                    new(conumGlyph, _fontMono, _bodyFontSize, Color: conumColor),
                    new(" " + entry.Text, _fontRegular, _bodyFontSize),
                };
                w.WriteWrappedSegments(segments, _bodyLeading, justify: false);
                num++;
            }
            w.MoveCursor(_paragraphSpacingAfter);
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
        float lineBottom = w.CursorY + _bodyLeading + _paragraphSpacingAfter - descent;
        w.DrawLine(borderX, lineTop, borderX, lineBottom, _admonitionBorderWidth);
        w.SetStrokeColor(0, 0, 0);

        w.PopIndent(savedIndent);
        w.MoveCursor(_paragraphSpacingAfter);
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

    /// <summary>
    /// Loads an embedded TTF resource and registers it as a PDF font.
    /// Returns the font reference name on success, or null if the resource is
    /// missing or fails to parse (caller's drawn-glyph fallback then kicks in).
    /// </summary>
    private static string? TryLoadEmbeddedFont(PdfWriter writer, string resourceName, string pdfRefName)
    {
        try
        {
            var asm = typeof(PdfRenderer).Assembly;
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is null) return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var font = TrueTypeFont.Parse(ms.ToArray());
            return writer.RegisterEmbeddedFont(pdfRefName, font);
        }
        catch
        {
            return null;
        }
    }
}
