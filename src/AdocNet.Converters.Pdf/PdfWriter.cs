using System.Globalization;
using System.IO.Compression;
using System.Text;
#if NETSTANDARD2_0
using AdocNet.Internal.Compatibility;
#endif

namespace AdocNet.Converters.Pdf;

/// <summary>
/// Low-level PDF file writer. Builds a valid PDF 1.4 document using
/// the 14 standard fonts (no embedding required). All output is
/// deterministic — fixed metadata, consistent object numbering,
/// and sorted dictionary keys.
/// </summary>
internal sealed partial class PdfWriter
{
    // ── Default dimensions ───────────────────────────────────────────────
    private const float DefaultPageWidth = 595f;  // A4 in points (72 dpi)
    private const float DefaultPageHeight = 842f;
    private const float DefaultMarginLeft = 72f;
    private const float DefaultMarginRight = 72f;
    private const float DefaultMarginTop = 72f;
    private const float DefaultMarginBottom = 72f;

#if NET10_0_OR_GREATER
    private static readonly byte[] PdfHeader = "%PDF-1.4\n%\xe2\xe3\xcf\xd3\n"u8.ToArray();
#else
    private static readonly byte[] PdfHeader = Encoding.UTF8.GetBytes("%PDF-1.4\n%\xe2\xe3\xcf\xd3\n");
#endif

    // ── Instance dimensions ──────────────────────────────────────────────
    private readonly float _marginLeft;
    private readonly float _marginRight;
    private readonly float _marginTop;
    private readonly float _marginBottom;

    // ── Object storage ──────────────────────────────────────────────────
    private readonly List<PdfObject> _objects = [];
    private readonly List<int> _pageObjectIds = [];

    // Font object IDs (allocated lazily)
    private int _fontHelveticaId;
    private int _fontHelveticaBoldId;
    private int _fontHelveticaObliqueId;
    private int _fontCourierId;
    private int _fontCourierBoldId;
    private int _fontCourierObliqueId;
    private int _fontCourierBoldObliqueId;

    // ── Embedded TrueType font support ──────────────────────────────────
    private readonly Dictionary<string, TrueTypeFont> _embeddedFonts = [];
    private readonly HashSet<string> _monospaceFonts = [];
    private readonly Dictionary<string, HashSet<int>> _usedCodePoints = [];
    private readonly Dictionary<string, int> _embeddedFontPlaceholders = [];
    private int _nextEmbeddedFontId = 8; // F8, F9, F10, ... (F4-F7 reserved for standard Courier variants)

    // ── Current page state ──────────────────────────────────────────────
    private StringBuilder? _currentStream;
    private float _cursorY;
    private float _pageWidth;
    private float _pageHeight;
    private float _contentWidth;
    private int _currentPageNumber;

    // ── Link annotations for the current page ─────────────────────────
    private readonly List<PdfAnnotation> _currentAnnotations = [];
    // Internal link annotations (GoTo destinations within the document)
    private readonly List<PdfInternalLink> _currentInternalLinks = [];

    // ── Image XObjects for the current page ─────────────────────────────
    private readonly Dictionary<string, int> _currentPageImages = [];
    private int _imageCounter;

    // ── Outline / bookmark tree ─────────────────────────────────────────
    private readonly List<OutlineEntry> _outlineEntries = [];
    // Named destinations: id -> (pageIndex, yPosition) for cross-references
    private readonly Dictionary<string, (int PageIndex, float Y)> _namedDestinations = [];
    // Internal links deferred for resolution after all pages are rendered
    private readonly List<DeferredInternalLink> _deferredInternalLinks = [];

    // ── Link styling ────────────────────────────────────────────────────
    /// <summary>RGB color used to render link text. Null = body color.</summary>
    internal PdfColor? LinkColor { get; set; }
    /// <summary>RGB body text color. Used to restore fill color after temporary
    /// segment-level color overrides (link blue, codespan red, etc.).</summary>
    internal PdfColor? BodyColor { get; set; }

    // ── Header/Footer ───────────────────────────────────────────────────
    internal bool ShowPageNumbers { get; set; }
    internal string? HeaderTemplate { get; set; }
    internal string? FooterTemplate { get; set; }
    internal float HeaderFontSize { get; set; } = 9f;
    internal float FooterFontSize { get; set; } = 9f;
    internal PdfColor? HeaderFontColor { get; set; }
    internal PdfColor? FooterFontColor { get; set; }
    internal PdfAlignment HeaderAlignment { get; set; } = PdfAlignment.Center;
    internal PdfAlignment FooterAlignment { get; set; } = PdfAlignment.Center;
    internal float HeaderHeight { get; set; }
    internal float FooterHeight { get; set; }
    /// <summary>Body line leading (font-size × line-spacing). When set, the first text baseline
    /// on each page is positioned one leading below the top margin so text fits inside the body
    /// area instead of climbing into the top margin (matches Asciidoctor PDF behavior).</summary>
    internal float BodyLeading { get; set; }
    /// <summary>Section title for the {section-title} placeholder. Updated by SetSectionTitleForPage.
    /// Carries over across pages until a new section starts on a page.</summary>
    internal string? CurrentSectionTitle { get; private set; }

    private bool _sectionTitleSetThisPage;

    /// <summary>Set the current section title. Called by the renderer when a level-1 section starts.
    /// Within a single page, only the FIRST call takes effect — subsequent sections on the same page
    /// don't override the title (matches Asciidoctor: footer shows the first section that started on
    /// the page, or the section that carried over from the previous page).</summary>
    internal void SetSectionTitleForPage(string title)
    {
        if (_sectionTitleSetThisPage) return;
        CurrentSectionTitle = title;
        _sectionTitleSetThisPage = true;
    }
    internal string? DocumentTitle { get; set; }
    /// <summary>Optional override for {header-title} template token. Falls back to DocumentTitle.</summary>
    internal string? HeaderTitle { get; set; }
    internal SvgParser.SvgDocument? FooterImage { get; set; }
    internal float FooterImageWidth { get; set; } = 64f;

    /// <summary>Page number from which headers/footers start appearing (1-based). Default: 1 (all pages).</summary>
    internal int RunningContentStartPage { get; set; } = 1;

    // ── Hyphenation ──────────────────────────────────────────────────────
    internal bool HyphenationEnabled { get; set; }

    // ── Code block background ────────────────────────────────────────────
    private PdfColor? _codeBlockBg;
    private float _codeBlockLeading;
    private float _codeBlockFontSize;
    private float _codeBlockStartY;
    private int _codeBlockStartPage;
    private int _codeBlockStreamInsertPos;
    private PdfColor? _codeBlockBorderColor;
    private float _codeBlockSavedIndent;
    private int _codeBlockOriginalPage;
    private const float CodeBlockPadding = 10f;
    private const float CodeBlockRadius = 4f;
    private const float CodeBlockBorderWidth = 0.5f;

    /// <summary>
    /// Begins a code block region. Pushes padding indent and records the
    /// stream position so the background can be inserted behind the text.
    /// </summary>
    internal void BeginCodeBlockBackground(PdfColor bg, float fontSize, float leading,
        PdfColor? borderColor = null)
    {
        _codeBlockBg = bg;
        _codeBlockFontSize = fontSize;
        _codeBlockLeading = leading;
        _codeBlockBorderColor = borderColor;

        // Add vertical padding before content
        _cursorY -= CodeBlockPadding;
        _codeBlockStartY = _cursorY + fontSize * 0.75f + CodeBlockPadding;
        _codeBlockStartPage = _currentPageNumber;
        _codeBlockOriginalPage = _currentPageNumber;

        // Record stream position so we can insert the background BEFORE the text
        _codeBlockStreamInsertPos = _currentStream!.Length;

        // Push horizontal padding so code text is inset from the background edges
        _codeBlockSavedIndent = PushIndent(_contentLeftIndent + CodeBlockPadding);
    }

    /// <summary>Ends the code block region. Inserts the rounded-rect background
    /// at the saved stream position so it appears behind the text.</summary>
    internal void EndCodeBlockBackground()
    {
        // Restore indent
        PopIndent(_codeBlockSavedIndent);

        if (_codeBlockBg is { } bg)
        {
            float descent = _codeBlockFontSize * 0.25f;
            float top = _codeBlockStartY;
            // Bottom: padding below the last line's descender
            // Last baseline = _cursorY + _codeBlockLeading (cursor moved down after last line)
            float lastBaseline = _cursorY + _codeBlockLeading;
            float bottom = lastBaseline - descent - CodeBlockPadding;

            if (top > bottom)
            {
                // Build the background drawing commands.
                // Background aligns with the current content area (inside the page margins,
                // and inside any list/quote/section indent). The inner text padding is provided
                // by the PushIndent above (CodeBlockPadding); we don't extend the background
                // outward into the page margins as Asciidoctor keeps it inside the body width.
                var bgCmd = new StringBuilder();
                // MarginLeftValue already includes _contentLeftIndent, so don't add it again.
                // For top-level: rx=48 (page margin). For nested (e.g. in a list): rx=48+listIndent.
                float rx = MarginLeftValue;
                float rw = ContentWidth;
                float rh = top - bottom;

                if (_codeBlockOriginalPage == _currentPageNumber)
                {
                    // Single-page: rounded rect with border
                    AppendRoundedRectFill(bgCmd, bg, rx, bottom, rw, rh);
                    if (_codeBlockBorderColor is { } bc)
                        AppendRoundedRectStroke(bgCmd, bc, rx, bottom, rw, rh);
                }
                else
                {
                    // Multi-page: plain rect on this (last) page
                    bgCmd.Append("q\n");
                    bgCmd.Append($"{Fmt(bg.R)} {Fmt(bg.G)} {Fmt(bg.B)} rg\n");
                    bgCmd.Append($"{Fmt(rx)} {Fmt(bottom)} {Fmt(rw)} {Fmt(rh)} re f\n");
                    bgCmd.Append("Q\n");
                    if (_codeBlockBorderColor is { } bc)
                    {
                        bgCmd.Append("q\n");
                        bgCmd.Append($"{Fmt(CodeBlockBorderWidth)} w\n");
                        bgCmd.Append($"{Fmt(bc.R)} {Fmt(bc.G)} {Fmt(bc.B)} RG\n");
                        bgCmd.Append($"{Fmt(rx)} {Fmt(bottom)} {Fmt(rw)} {Fmt(rh)} re S\n");
                        bgCmd.Append("Q\n");
                    }
                }

                // Insert background at the saved position (before the text)
                _currentStream!.Insert(_codeBlockStreamInsertPos, bgCmd.ToString());
            }

            // Add vertical padding after content
            _cursorY -= CodeBlockPadding;
        }
        _codeBlockBg = null;
        _codeBlockBorderColor = null;
    }

    /// <summary>
    /// Draws the code block background on the current page (used at page breaks).
    /// Uses a plain rectangle (no rounded corners) for continuation pages.
    /// </summary>
    private void FlushCodeBlockBackgroundOnPage()
    {
        if (_codeBlockBg is not { } bg) return;

        float top = _codeBlockStartY;
        float bottom = _marginBottom;
        float pad = CodeBlockPadding;

        if (top > bottom)
        {
            var bgCmd = new StringBuilder();
            float rx = MarginLeftValue - pad;
            float rw = ContentWidth + pad * 2;
            float rh = top - bottom;

            // Continuation page: simple rect (no rounded corners at page boundary)
            bgCmd.Append("q\n");
            bgCmd.Append($"{Fmt(bg.R)} {Fmt(bg.G)} {Fmt(bg.B)} rg\n");
            bgCmd.Append($"{Fmt(rx)} {Fmt(bottom)} {Fmt(rw)} {Fmt(rh)} re f\n");
            bgCmd.Append("Q\n");
            if (_codeBlockBorderColor is { } bc)
            {
                bgCmd.Append("q\n");
                bgCmd.Append($"{Fmt(CodeBlockBorderWidth)} w\n");
                bgCmd.Append($"{Fmt(bc.R)} {Fmt(bc.G)} {Fmt(bc.B)} RG\n");
                bgCmd.Append($"{Fmt(rx)} {Fmt(bottom)} {Fmt(rw)} {Fmt(rh)} re S\n");
                bgCmd.Append("Q\n");
            }

            _currentStream!.Insert(_codeBlockStreamInsertPos, bgCmd.ToString());
        }

        // Clear bg so FinishPage doesn't try to draw it again
        _codeBlockBg = null;
        _codeBlockBorderColor = null;
    }

    private void AppendRoundedRectFill(StringBuilder sb, PdfColor color,
        float x, float y, float w, float h)
    {
        float r = CodeBlockRadius;
        r = Math.Min(r, Math.Min(w / 2, h / 2));
        float k = r * 0.5523f;
        sb.Append("q\n");
        sb.Append($"{Fmt(color.R)} {Fmt(color.G)} {Fmt(color.B)} rg\n");
        AppendRoundedRectPath(sb, x, y, w, h, r, k);
        sb.Append("f\n");
        sb.Append("Q\n");
    }

    private void AppendRoundedRectStroke(StringBuilder sb, PdfColor color,
        float x, float y, float w, float h)
    {
        float r = CodeBlockRadius;
        r = Math.Min(r, Math.Min(w / 2, h / 2));
        float k = r * 0.5523f;
        sb.Append("q\n");
        sb.Append($"{Fmt(CodeBlockBorderWidth)} w\n");
        sb.Append($"{Fmt(color.R)} {Fmt(color.G)} {Fmt(color.B)} RG\n");
        AppendRoundedRectPath(sb, x, y, w, h, r, k);
        sb.Append("S\n");
        sb.Append("Q\n");
    }

    private static void AppendRoundedRectPath(StringBuilder sb,
        float x, float y, float w, float h, float r, float k)
    {
        sb.Append($"{Fmt(x)} {Fmt(y + r)} m\n");
        sb.Append($"{Fmt(x)} {Fmt(y + r - k)} {Fmt(x + r - k)} {Fmt(y)} {Fmt(x + r)} {Fmt(y)} c\n");
        sb.Append($"{Fmt(x + w - r)} {Fmt(y)} l\n");
        sb.Append($"{Fmt(x + w - r + k)} {Fmt(y)} {Fmt(x + w)} {Fmt(y + r - k)} {Fmt(x + w)} {Fmt(y + r)} c\n");
        sb.Append($"{Fmt(x + w)} {Fmt(y + h - r)} l\n");
        sb.Append($"{Fmt(x + w)} {Fmt(y + h - r + k)} {Fmt(x + w - r + k)} {Fmt(y + h)} {Fmt(x + w - r)} {Fmt(y + h)} c\n");
        sb.Append($"{Fmt(x + r)} {Fmt(y + h)} l\n");
        sb.Append($"{Fmt(x + r - k)} {Fmt(y + h)} {Fmt(x)} {Fmt(y + h - r + k)} {Fmt(x)} {Fmt(y + h - r)} c\n");
        sb.Append("h\n");
    }

    /// <summary>
    /// No-op for single-rect code blocks. Kept for API compatibility.
    /// </summary>
    internal void DrawCodeLineBackground()
    {
        // Background is now drawn as a single rect in EndCodeBlockBackground.
    }

    // ── Content indent (for nested blocks like quotes) ─────────────────
    private float _contentLeftIndent;

    /// <summary>Effective content width accounting for indent.</summary>
    internal float ContentWidth => _contentWidth - _contentLeftIndent;

    internal float CursorY => _cursorY;
    internal float PageHeight => _pageHeight;
    internal float MarginBottomValue => _marginBottom;

    /// <summary>Effective left edge accounting for indent.</summary>
    internal float MarginLeftValue => _marginLeft + _contentLeftIndent;

    /// <summary>Base left margin without indent (for positioning border lines).</summary>
    internal float MarginLeftBase => _marginLeft;

    internal float MarginTopValue => _marginTop;
    internal int CurrentPageNumber => _currentPageNumber;

    /// <summary>
    /// Temporarily indents all content by the given amount from the left margin.
    /// Returns the previous indent so it can be restored.
    /// </summary>
    internal float PushIndent(float indent)
    {
        float previous = _contentLeftIndent;
        _contentLeftIndent = indent;
        return previous;
    }

    /// <summary>Restores a previously saved indent value.</summary>
    internal void PopIndent(float previous) => _contentLeftIndent = previous;

    // Font metrics are in HelveticaMetrics.cs

    internal PdfWriter(float pageWidth = DefaultPageWidth, float pageHeight = DefaultPageHeight,
        float marginLeft = DefaultMarginLeft, float marginRight = DefaultMarginRight,
        float marginTop = DefaultMarginTop, float marginBottom = DefaultMarginBottom)
    {
        _pageWidth = pageWidth;
        _pageHeight = pageHeight;
        _marginLeft = marginLeft;
        _marginRight = marginRight;
        _marginTop = marginTop;
        _marginBottom = marginBottom;
        _contentWidth = _pageWidth - _marginLeft - _marginRight;
        AllocateFontObjects();
    }

    // ── Font allocation ─────────────────────────────────────────────────

    private void AllocateFontObjects()
    {
        _fontHelveticaId = AllocObject(new PdfObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        _fontHelveticaBoldId = AllocObject(new PdfObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
        _fontHelveticaObliqueId = AllocObject(new PdfObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Oblique /Encoding /WinAnsiEncoding >>"));
        _fontCourierId = AllocObject(new PdfObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"));
        _fontCourierBoldId = AllocObject(new PdfObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Bold /Encoding /WinAnsiEncoding >>"));
        _fontCourierObliqueId = AllocObject(new PdfObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier-Oblique /Encoding /WinAnsiEncoding >>"));
        _fontCourierBoldObliqueId = AllocObject(new PdfObject(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier-BoldOblique /Encoding /WinAnsiEncoding >>"));
    }

    /// <summary>
    /// Registers a TrueType font for embedding. Returns the font key (e.g. "F5")
    /// that should be used instead of the standard font key.
    /// </summary>
    internal string RegisterEmbeddedFont(string standardFontKey, TrueTypeFont font)
    {
        string embeddedKey = $"F{_nextEmbeddedFontId++}";
        _embeddedFonts[embeddedKey] = font;
        _usedCodePoints[embeddedKey] = [];
        _embeddedFontPlaceholders[embeddedKey] = AllocPlaceholder();

        // Track monospace fonts (F4-F7 = Courier variants/monospace)
        if (standardFontKey == "F4" || standardFontKey == "F5"
            || standardFontKey == "F6" || standardFontKey == "F7")
            _monospaceFonts.Add(embeddedKey);

        // Map the standard key to the embedded key for resolution
        _fontKeyMapping[standardFontKey] = embeddedKey;
        return embeddedKey;
    }

    // Maps standard font keys (F1-F4) to embedded font keys (F5+)
    private readonly Dictionary<string, string> _fontKeyMapping = [];

    /// <summary>
    /// Resolves a font key: if an embedded font is registered for the given standard key,
    /// returns the embedded key; otherwise returns the original key.
    /// </summary>
    internal string ResolveFontKey(string fontKey) =>
        _fontKeyMapping.GetValueOrDefault(fontKey, fontKey);

    /// <summary>Returns true if the given font key refers to an embedded TrueType font.</summary>
    internal bool IsEmbeddedFont(string fontKey) => _embeddedFonts.ContainsKey(fontKey);

    /// <summary>Gets the TrueType font for the given embedded font key, or null.</summary>
    internal TrueTypeFont? GetEmbeddedFont(string fontKey) =>
        _embeddedFonts.GetValueOrDefault(fontKey);

    // ── Object management ───────────────────────────────────────────────

    private int AllocObject(PdfObject obj)
    {
        _objects.Add(obj);
        return _objects.Count; // 1-based ID
    }

    private int AllocPlaceholder()
    {
        _objects.Add(new PdfObject(""));
        return _objects.Count;
    }

    private void SetObject(int id, PdfObject obj) => _objects[id - 1] = obj;

    // ── Page management ─────────────────────────────────────────────────

    internal void StartPage()
    {
        _currentStream = new StringBuilder();
        // First text baseline starts one body-leading below the top margin so text ascender
        // doesn't climb above the top margin into the header area. Matches Asciidoctor PDF.
        _cursorY = _pageHeight - _marginTop - BodyLeading;
        _currentPageNumber++;
        _atTopOfPage = true;
        // Allow the next section on this page to set the {section-title} for footer/header.
        // CurrentSectionTitle carries over from the previous page until then.
        _sectionTitleSetThisPage = false;
    }

    /// <summary>
    /// Tracks whether the cursor is still at the page-top reset position (no content drawn yet).
    /// Used to apply a one-time leading adjustment when the first line is taller than body text.
    /// </summary>
    private bool _atTopOfPage;

    /// <summary>
    /// Reserves additional vertical space above the next baseline if the upcoming line's
    /// leading is larger than BodyLeading. Called before writing the first line on a page.
    /// This ensures the line's ascent does not extend into the top margin / header zone.
    /// </summary>
    internal void ReserveFirstLineLeading(float leading)
    {
        if (_atTopOfPage && leading > BodyLeading)
        {
            _cursorY -= (leading - BodyLeading);
        }
        _atTopOfPage = false;
    }

    internal void FinishPage()
    {
        if (_currentStream is null) return;

        // Append header/footer text before finalizing the page stream
        AppendHeaderFooter();

        var streamContent = _currentStream.ToString();
        var streamBytes = Encoding.ASCII.GetByteCount(streamContent);
        int streamObjId = AllocObject(new PdfObject(
            $"<< /Length {streamBytes} >>\nstream\n{streamContent}endstream"));

        // Create annotation objects for links on this page
        var annotIds = new List<int>();
        foreach (var annot in _currentAnnotations)
        {
            int annotId = AllocObject(new PdfObject(
                $"<< /Type /Annot /Subtype /Link " +
                $"/Rect [{Fmt(annot.X)} {Fmt(annot.Y)} {Fmt(annot.X + annot.Width)} {Fmt(annot.Y + annot.Height)}] " +
                $"/Border [0 0 0] " +
                $"/A << /Type /Action /S /URI /URI ({EscapePdfString(annot.Uri)}) >> >>"));
            annotIds.Add(annotId);
        }
        _currentAnnotations.Clear();

        // Create internal link annotations (deferred — resolved in ToBytes)
        foreach (var link in _currentInternalLinks)
        {
            int annotId = AllocPlaceholder(); // will be filled in ToBytes
            _deferredInternalLinks.Add(new DeferredInternalLink(annotId, link));
            annotIds.Add(annotId);
        }
        _currentInternalLinks.Clear();

        var fontResourcesSb = new StringBuilder();
        fontResourcesSb.Append($"/F1 {_fontHelveticaId} 0 R ");
        fontResourcesSb.Append($"/F2 {_fontHelveticaBoldId} 0 R ");
        fontResourcesSb.Append($"/F3 {_fontHelveticaObliqueId} 0 R ");
        fontResourcesSb.Append($"/F4 {_fontCourierId} 0 R ");
        fontResourcesSb.Append($"/F5 {_fontCourierBoldId} 0 R ");
        fontResourcesSb.Append($"/F6 {_fontCourierObliqueId} 0 R ");
        fontResourcesSb.Append($"/F7 {_fontCourierBoldObliqueId} 0 R");
        foreach (var (key, placeholderId) in _embeddedFontPlaceholders)
        {
            fontResourcesSb.Append($" /{key} {placeholderId} 0 R");
        }
        var fontResources = fontResourcesSb.ToString();

        string annotsEntry = annotIds.Count > 0
            ? $"/Annots [{string.Join(" ", annotIds.Select(id => $"{id} 0 R"))}] "
            : "";

        string xobjectEntry = _currentPageImages.Count > 0
            ? $"/XObject << {string.Join(" ", _currentPageImages.Select(kv => $"/{kv.Key} {kv.Value} 0 R"))} >> "
            : "";

        int pageId = AllocObject(new PdfObject(
            $"<< /Type /Page /MediaBox [0 0 {Fmt(_pageWidth)} {Fmt(_pageHeight)}] " +
            $"/Contents {streamObjId} 0 R " +
            $"{annotsEntry}" +
            $"/Resources << /Font << {fontResources} >> {xobjectEntry}>> " +
            $"/Parent {{PAGES}} >>"));

        _pageObjectIds.Add(pageId);
        _currentStream = null;
        _currentPageImages.Clear();
    }

    /// <summary>
    /// Fixed-width placeholder for total page count. Must be exactly the same length
    /// as the replacement value (padded with spaces) to avoid shifting byte offsets.
    /// </summary>
    private const string TotalPagesPlaceholder = "___TOTAL___";

    private void AppendHeaderFooter()
    {
        if (_currentStream is null) return;

        // Suppress headers/footers before the configured start page
        if (_currentPageNumber < RunningContentStartPage) return;

        string? footer = FooterTemplate;
        if (footer is null && ShowPageNumbers)
            footer = "Page {page}";

        if (footer is not null)
        {
            // Position footer text vertically within the footer area.
            // Match Asciidoctor PDF: text is top-aligned in the footer area (Y=0 to Y=footerHeight).
            // Baseline = footerHeight - font_size * 1.30 (accounts for font ascent + line spacing).
            // Verified against Asciidoctor: footer.height=48, font=11pt => Y=33.75 ≈ 48 - 11*1.295.
            float footerY = FooterHeight > 0
                ? FooterHeight - FooterFontSize * 1.30f
                : _marginBottom - 20;
            AppendHeaderFooterText(footer, footerY, FooterFontSize, FooterFontColor, FooterAlignment);
        }

        // Render footer image (e.g., SVG logo) in the bottom-left corner
        if (FooterImage is { } svgImg)
        {
            float imgWidth = FooterImageWidth;
            float aspectRatio = svgImg.ViewBoxHeight / svgImg.ViewBoxWidth;
            float imgHeight = imgWidth * aspectRatio;
            float imgX = _marginLeft;
            // Align logo TOP with footer area TOP, matching Asciidoctor `position=top left`.
            // imgY is the BOTTOM-LEFT corner; placing top at FooterHeight means imgY = FooterHeight - imgHeight.
            float imgY = FooterHeight > 0
                ? FooterHeight - imgHeight
                : _marginBottom - imgHeight - 5;
            DrawSvg(svgImg, imgX, imgY, imgWidth, imgHeight);
        }

        if (HeaderTemplate is not null)
        {
            // Position header text within the header area (centered, matching Asciidoctor PDF default valign:middle).
            // Baseline = pageTop - headerHeight/2 - fontSize * 0.15 places the text visually centered in the area.
            // Verified against Asciidoctor: header.height=64, font=11pt, page=842 => Y=808.35.
            float headerY = HeaderHeight > 0
                ? _pageHeight - HeaderHeight / 2 - HeaderFontSize * 0.15f
                : _pageHeight - _marginTop / 2;
            AppendHeaderFooterText(HeaderTemplate, headerY, HeaderFontSize, HeaderFontColor, HeaderAlignment);
        }
    }

    private void AppendHeaderFooterText(string template, float y, float fontSize, PdfColor? fontColor, PdfAlignment alignment)
    {
        string text = template
            .Replace("{page}", _currentPageNumber.ToString(CultureInfo.InvariantCulture))
            .Replace("{pages}", TotalPagesPlaceholder)
            .Replace("{page-number}", _currentPageNumber.ToString(CultureInfo.InvariantCulture))
            .Replace("{page-count}", TotalPagesPlaceholder)
            .Replace("{section-title}", CurrentSectionTitle ?? "")
            .Replace("{document-title}", DocumentTitle ?? "")
            .Replace("{header-title}", HeaderTitle ?? DocumentTitle ?? "");

        // Use the body font (F1) for headers/footers — embedded TrueType if available
        string font = ResolveFontKey("F1");

        // For width measurement (used to compute X position), substitute the placeholder
        // with a single digit. The placeholder is wider than the eventual replacement
        // ("3" + 10 trailing spaces), so measuring with the placeholder would shift
        // right-aligned text away from the right edge. After byte-level replacement,
        // trailing spaces extend invisibly past the right edge — visible text aligns correctly.
        string measureText = text.Replace(TotalPagesPlaceholder, "1");
        float textWidth = MeasureText(measureText, font, fontSize);

        float x = alignment switch
        {
            PdfAlignment.Left => _marginLeft,
            PdfAlignment.Right => _marginLeft + _contentWidth - textWidth,
            _ => _marginLeft + (_contentWidth - textWidth) / 2,
        };

        if (fontColor is { } fc)
        {
            _currentStream!.Append($"{Fmt(fc.R)} {Fmt(fc.G)} {Fmt(fc.B)} rg\n");
        }

        _currentStream!.Append("BT\n");
        _currentStream.Append($"/{font} {Fmt(fontSize)} Tf\n");
        _currentStream.Append($"{Fmt(x)} {Fmt(y)} Td\n");

        if (_embeddedFonts.TryGetValue(font, out var ttFont))
        {
            TrackCodePoints(font, text);
            _currentStream.Append('<');
            _currentStream.Append(EncodeTextAsGlyphIds(text, ttFont));
            _currentStream.Append("> Tj\n");
        }
        else
        {
            _currentStream.Append('(');
            _currentStream.Append(EscapePdfString(text));
            _currentStream.Append(") Tj\n");
        }

        _currentStream.Append("ET\n");

        if (fontColor is not null)
        {
            _currentStream.Append("0 0 0 rg\n"); // Reset to black
        }
    }

    internal bool NeedsNewPage() => _cursorY < _marginBottom;

    internal void EnsurePage()
    {
        if (_currentStream is null || NeedsNewPage())
        {
            // If inside a code block, flush the background on the current page
            // before finishing it, so each page gets its own background rect.
            bool inCodeBlock = _codeBlockBg is not null && _currentStream is not null;
            PdfColor? savedBg = null;
            PdfColor? savedBorder = null;
            if (inCodeBlock)
            {
                savedBg = _codeBlockBg;
                savedBorder = _codeBlockBorderColor;
                FlushCodeBlockBackgroundOnPage();
            }

            if (_currentStream is not null)
                FinishPage();
            StartPage();

            // Resume code block tracking on the new page
            if (inCodeBlock && savedBg is { } bg)
            {
                _codeBlockBg = bg;
                _codeBlockBorderColor = savedBorder;
                _codeBlockStartY = _cursorY + _codeBlockFontSize * 0.75f;
                _codeBlockStartPage = _currentPageNumber;
                _codeBlockStreamInsertPos = _currentStream!.Length;
            }
        }
    }

    // ── Text operations ─────────────────────────────────────────────────

    /// <summary>Font identifiers: F1=Helvetica, F2=Helvetica-Bold, F3=Helvetica-Oblique, F4=Courier.</summary>
    internal void WriteText(string text, string font, float fontSize, float x, float y)
    {
        _currentStream!.Append("BT\n");
        _currentStream.Append($"/{font} {Fmt(fontSize)} Tf\n");
        _currentStream.Append($"{Fmt(x)} {Fmt(y)} Td\n");

        if (_embeddedFonts.TryGetValue(font, out var ttFont))
        {
            TrackCodePoints(font, text);
            _currentStream.Append('<');
            _currentStream.Append(EncodeTextAsGlyphIds(text, ttFont));
            _currentStream.Append("> Tj\n");
        }
        else
        {
            _currentStream.Append('(');
            _currentStream.Append(EscapePdfString(text));
            _currentStream.Append(") Tj\n");
        }

        _currentStream.Append("ET\n");
    }

    /// <summary>
    /// Writes a single line of text with extra word spacing for justification.
    /// </summary>
    internal void WriteJustifiedText(string text, string font, float fontSize, float x, float y, float wordSpacing)
    {
        _currentStream!.Append("BT\n");
        _currentStream.Append($"/{font} {Fmt(fontSize)} Tf\n");
        _currentStream.Append($"{Fmt(x)} {Fmt(y)} Td\n");
        _currentStream.Append($"{Fmt(wordSpacing)} Tw\n");

        if (_embeddedFonts.TryGetValue(font, out var ttFont))
        {
            TrackCodePoints(font, text);
            _currentStream.Append('<');
            _currentStream.Append(EncodeTextAsGlyphIds(text, ttFont));
            _currentStream.Append("> Tj\n");
        }
        else
        {
            _currentStream.Append('(');
            _currentStream.Append(EscapePdfString(text));
            _currentStream.Append(") Tj\n");
        }

        _currentStream.Append("0 Tw\n");
        _currentStream.Append("ET\n");
    }

    /// <summary>
    /// Writes a line of mixed-style text segments at the current cursor position.
    /// Each segment has its own font and the text is positioned relative to the previous segment.
    /// </summary>
    internal void WriteTextSegments(List<TextSegment> segments, float x, float y)
    {
        if (segments.Count == 0) return;

        // Draw background rectangles before text (PDF draws back-to-front)
        float bgX = x;
        foreach (var seg in segments)
        {
            float segW = MeasureText(seg.Text, seg.Font, seg.FontSize);
            if (seg.Background is { } bg)
            {
                const float pad = 1.5f;
                _currentStream!.Append("q\n");
                _currentStream.Append($"{Fmt(bg.R)} {Fmt(bg.G)} {Fmt(bg.B)} rg\n");
                _currentStream.Append($"{Fmt(bgX - pad)} {Fmt(y - 2f)} {Fmt(segW + pad * 2)} {Fmt(seg.FontSize + 3f)} re f\n");
                _currentStream.Append("Q\n");
            }
            bgX += segW;
        }

        float currentX = x;
        _currentStream!.Append("BT\n");
        _currentStream.Append($"{Fmt(x)} {Fmt(y)} Td\n");

        foreach (var seg in segments)
        {
            _currentStream.Append($"/{seg.Font} {Fmt(seg.FontSize)} Tf\n");

            // Apply explicit color (e.g. codespan red) or link color when set.
            // Explicit Color takes precedence over LinkColor.
            PdfColor? activeColor = seg.Color
                ?? (seg.LinkUri is not null ? LinkColor : null);
            if (activeColor is not null)
            {
                var c = activeColor.Value;
                _currentStream.Append($"{Fmt(c.R)} {Fmt(c.G)} {Fmt(c.B)} rg\n");
            }
            if (_embeddedFonts.TryGetValue(seg.Font, out var ttFont))
            {
                TrackCodePoints(seg.Font, seg.Text);
                _currentStream.Append('<');
                _currentStream.Append(EncodeTextAsGlyphIds(seg.Text, ttFont));
                _currentStream.Append("> Tj\n");
            }
            else
            {
                _currentStream.Append('(');
                _currentStream.Append(EscapePdfString(seg.Text));
                _currentStream.Append(") Tj\n");
            }
            if (activeColor is not null)
            {
                if (BodyColor is { } bc)
                    _currentStream.Append($"{Fmt(bc.R)} {Fmt(bc.G)} {Fmt(bc.B)} rg\n");
                else
                    _currentStream.Append("0 0 0 rg\n");
            }

            float segWidth = MeasureText(seg.Text, seg.Font, seg.FontSize);
            if (seg.LinkUri is not null)
            {
                if (seg.LinkUri.StartsWith("#internal#"))
                    AddInternalLinkAnnotation(currentX, y - 2, segWidth, seg.FontSize + 4,
                        seg.LinkUri.Substring(10));
                else
                    AddLinkAnnotation(currentX, y - 2, segWidth, seg.FontSize + 4, seg.LinkUri);
            }
            currentX += segWidth;
        }

        _currentStream.Append("ET\n");
    }

}

/// <summary>Raw PDF object content, optionally with a binary stream.</summary>
internal readonly record struct PdfObject(string Content, byte[]? BinaryStream = null);

/// <summary>A link annotation to be added to a PDF page.</summary>
internal record struct PdfAnnotation(float X, float Y, float Width, float Height, string Uri);

/// <summary>An internal link annotation (GoTo destination within the document).</summary>
internal record struct PdfInternalLink(float X, float Y, float Width, float Height, string DestinationId);

/// <summary>Deferred internal link — placeholder object ID + link data, resolved in ToBytes.</summary>
internal readonly record struct DeferredInternalLink(int PlaceholderObjId, PdfInternalLink Link);

/// <summary>A text segment with font and size info for mixed-style rendering.</summary>
internal readonly record struct TextSegment(string Text, string Font, float FontSize, string? LinkUri = null, PdfColor? Background = null, PdfColor? Color = null);

/// <summary>An outline/bookmark entry for the PDF document outline tree.</summary>
internal sealed class OutlineEntry
{
    public string Title { get; }
    public int Level { get; }
    public int PageIndex { get; }
    public float Y { get; }
    public List<OutlineEntry> Children { get; } = [];

    public OutlineEntry(string title, int level, int pageIndex, float y)
    {
        Title = title;
        Level = level;
        PageIndex = pageIndex;
        Y = y;
    }
}
