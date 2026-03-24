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

    // ── Embedded TrueType font support ──────────────────────────────────
    private readonly Dictionary<string, TrueTypeFont> _embeddedFonts = [];
    private readonly Dictionary<string, HashSet<int>> _usedCodePoints = [];
    private readonly Dictionary<string, int> _embeddedFontPlaceholders = [];
    private int _nextEmbeddedFontId = 5; // F5, F6, F7, ...

    // ── Current page state ──────────────────────────────────────────────
    private StringBuilder? _currentStream;
    private float _cursorY;
    private float _pageWidth;
    private float _pageHeight;
    private float _contentWidth;
    private int _currentPageNumber;

    // ── Link annotations for the current page ─────────────────────────
    private readonly List<PdfAnnotation> _currentAnnotations = [];

    // ── Image XObjects for the current page ─────────────────────────────
    private readonly Dictionary<string, int> _currentPageImages = [];
    private int _imageCounter;

    // ── Header/Footer ───────────────────────────────────────────────────
    internal bool ShowPageNumbers { get; set; }
    internal string? HeaderTemplate { get; set; }
    internal string? FooterTemplate { get; set; }

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
        _cursorY = _pageHeight - _marginTop;
        _currentPageNumber++;
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

        var fontResourcesSb = new StringBuilder();
        fontResourcesSb.Append($"/F1 {_fontHelveticaId} 0 R ");
        fontResourcesSb.Append($"/F2 {_fontHelveticaBoldId} 0 R ");
        fontResourcesSb.Append($"/F3 {_fontHelveticaObliqueId} 0 R ");
        fontResourcesSb.Append($"/F4 {_fontCourierId} 0 R");
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

        string? footer = FooterTemplate;
        if (footer is null && ShowPageNumbers)
            footer = "Page {page}";

        if (footer is not null)
            AppendHeaderFooterText(footer, _marginBottom - 20);

        if (HeaderTemplate is not null)
            AppendHeaderFooterText(HeaderTemplate, _pageHeight - _marginTop + 20);
    }

    private void AppendHeaderFooterText(string template, float y)
    {
        string text = template
            .Replace("{page}", _currentPageNumber.ToString(CultureInfo.InvariantCulture))
            .Replace("{pages}", TotalPagesPlaceholder);

        float textWidth = MeasureText(text, "F1", 9f);
        float x = _marginLeft + (_contentWidth - textWidth) / 2;

        _currentStream!.Append("BT\n");
        _currentStream.Append($"/F1 {Fmt(9f)} Tf\n");
        _currentStream.Append($"{Fmt(x)} {Fmt(y)} Td\n");
        _currentStream.Append('(');
        _currentStream.Append(EscapePdfString(text));
        _currentStream.Append(") Tj\n");
        _currentStream.Append("ET\n");
    }

    internal bool NeedsNewPage() => _cursorY < _marginBottom;

    internal void EnsurePage()
    {
        if (_currentStream is null || NeedsNewPage())
        {
            if (_currentStream is not null)
                FinishPage();
            StartPage();
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

        float currentX = x;
        _currentStream!.Append("BT\n");
        _currentStream.Append($"{Fmt(x)} {Fmt(y)} Td\n");

        foreach (var seg in segments)
        {
            _currentStream.Append($"/{seg.Font} {Fmt(seg.FontSize)} Tf\n");

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

            float segWidth = MeasureText(seg.Text, seg.Font, seg.FontSize);
            if (seg.LinkUri is not null)
            {
                AddLinkAnnotation(currentX, y - 2, segWidth, seg.FontSize + 4, seg.LinkUri);
            }
            currentX += segWidth;
        }

        _currentStream.Append("ET\n");
    }

    /// <summary>
    /// Word-wraps text and writes it line by line, advancing the cursor.
    /// Returns the number of points consumed vertically.
    /// </summary>
    internal float WriteWrappedText(string text, string font, float fontSize, float leading)
    {
        var lines = WrapText(text, font, fontSize, ContentWidth);
        float consumed = 0;
        foreach (var line in lines)
        {
            EnsurePage();
            WriteText(line, font, fontSize, MarginLeftValue, _cursorY);
            _cursorY -= leading;
            consumed += leading;
        }
        return consumed;
    }

    /// <summary>
    /// Word-wraps mixed-style segments and writes them line by line.
    /// When <paramref name="justify"/> is true, full lines are stretched to fill the content width.
    /// </summary>
    internal float WriteWrappedSegments(List<TextSegment> segments, float leading, bool justify = true)
    {
        var lines = WrapSegments(segments, ContentWidth);
        float consumed = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            EnsurePage();
            bool isLastLine = i == lines.Count - 1;
            if (justify && !isLastLine)
                WriteJustifiedSegments(lines[i], MarginLeftValue, _cursorY, ContentWidth);
            else
                WriteTextSegments(lines[i], MarginLeftValue, _cursorY);
            _cursorY -= leading;
            consumed += leading;
        }
        return consumed;
    }

    /// <summary>
    /// Writes a line of segments justified to fill the given width.
    /// Extra space is distributed evenly across word gaps.
    /// </summary>
    private void WriteJustifiedSegments(List<TextSegment> segments, float x, float y, float targetWidth)
    {
        if (segments.Count == 0) return;

        // Measure natural width and count spaces
        float naturalWidth = 0;
        int spaceCount = 0;
        foreach (var seg in segments)
        {
            naturalWidth += MeasureText(seg.Text, seg.Font, seg.FontSize);
            foreach (var ch in seg.Text)
                if (ch == ' ') spaceCount++;
        }

        float extraSpacing = spaceCount > 0 ? (targetWidth - naturalWidth) / spaceCount : 0;

        // Clamp to 2× normal space width to avoid absurd stretching
        float maxSpacing = MeasureText(" ", segments[0].Font, segments[0].FontSize) * 2;
        if (extraSpacing < 0) extraSpacing = 0;
        if (extraSpacing > maxSpacing) extraSpacing = 0; // fall back to left-aligned if gap is too large

        float currentX = x;
        _currentStream!.Append("BT\n");
        _currentStream.Append($"{Fmt(x)} {Fmt(y)} Td\n");
        _currentStream.Append($"{Fmt(extraSpacing)} Tw\n");

        foreach (var seg in segments)
        {
            _currentStream.Append($"/{seg.Font} {Fmt(seg.FontSize)} Tf\n");

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

            float segWidth = MeasureText(seg.Text, seg.Font, seg.FontSize);
            // Account for extra spacing per space in this segment
            int segSpaces = 0;
            foreach (var ch in seg.Text)
                if (ch == ' ') segSpaces++;
            float adjustedWidth = segWidth + segSpaces * extraSpacing;

            if (seg.LinkUri is not null)
                AddLinkAnnotation(currentX, y - 2, adjustedWidth, seg.FontSize + 4, seg.LinkUri);

            currentX += adjustedWidth;
        }

        _currentStream.Append("0 Tw\n"); // reset word spacing
        _currentStream.Append("ET\n");
    }

}

/// <summary>Raw PDF object content, optionally with a binary stream.</summary>
internal readonly record struct PdfObject(string Content, byte[]? BinaryStream = null);

/// <summary>A link annotation to be added to a PDF page.</summary>
internal record struct PdfAnnotation(float X, float Y, float Width, float Height, string Uri);

/// <summary>A text segment with font and size info for mixed-style rendering.</summary>
internal readonly record struct TextSegment(string Text, string Font, float FontSize, string? LinkUri = null);
