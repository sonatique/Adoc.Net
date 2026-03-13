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
internal sealed class PdfWriter
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

    internal float ContentWidth => _contentWidth;
    internal float CursorY => _cursorY;
    internal float PageHeight => _pageHeight;
    internal float MarginBottomValue => _marginBottom;
    internal float MarginLeftValue => _marginLeft;
    internal float MarginTopValue => _marginTop;

    // ── Font metrics (approximate widths for standard fonts at size 1) ──
    // Helvetica average character widths per 1000 units
    private const float HelveticaAvgWidth = 0.52f;
    private const float HelveticaBoldAvgWidth = 0.55f;
    private const float CourierAvgWidth = 0.6f;

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

    private void AppendHeaderFooter()
    {
        if (_currentStream is null) return;

        string? footer = FooterTemplate;
        if (footer is null && ShowPageNumbers)
            footer = "Page {page}";

        if (footer is not null)
        {
            string footerText = footer.Replace("{page}", _currentPageNumber.ToString(CultureInfo.InvariantCulture));
            float textWidth = MeasureText(footerText, "F1", 9f);
            float x = _marginLeft + (_contentWidth - textWidth) / 2;
            float y = _marginBottom - 20;

            _currentStream.Append("BT\n");
            _currentStream.Append($"/F1 {Fmt(9f)} Tf\n");
            _currentStream.Append($"{Fmt(x)} {Fmt(y)} Td\n");
            _currentStream.Append('(');
            _currentStream.Append(EscapePdfString(footerText));
            _currentStream.Append(") Tj\n");
            _currentStream.Append("ET\n");
        }

        if (HeaderTemplate is not null)
        {
            string headerText = HeaderTemplate.Replace("{page}", _currentPageNumber.ToString(CultureInfo.InvariantCulture));
            float textWidth = MeasureText(headerText, "F1", 9f);
            float x = _marginLeft + (_contentWidth - textWidth) / 2;
            float y = _pageHeight - _marginTop + 20;

            _currentStream.Append("BT\n");
            _currentStream.Append($"/F1 {Fmt(9f)} Tf\n");
            _currentStream.Append($"{Fmt(x)} {Fmt(y)} Td\n");
            _currentStream.Append('(');
            _currentStream.Append(EscapePdfString(headerText));
            _currentStream.Append(") Tj\n");
            _currentStream.Append("ET\n");
        }
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
        var lines = WrapText(text, font, fontSize, _contentWidth);
        float consumed = 0;
        foreach (var line in lines)
        {
            EnsurePage();
            WriteText(line, font, fontSize, _marginLeft, _cursorY);
            _cursorY -= leading;
            consumed += leading;
        }
        return consumed;
    }

    /// <summary>
    /// Word-wraps mixed-style segments and writes them line by line.
    /// </summary>
    internal float WriteWrappedSegments(List<TextSegment> segments, float leading)
    {
        var lines = WrapSegments(segments, _contentWidth);
        float consumed = 0;
        foreach (var line in lines)
        {
            EnsurePage();
            WriteTextSegments(line, _marginLeft, _cursorY);
            _cursorY -= leading;
            consumed += leading;
        }
        return consumed;
    }

    internal void MoveCursor(float dy) => _cursorY -= dy;

    // ── Drawing operations ──────────────────────────────────────────────

    internal void DrawLine(float x1, float y1, float x2, float y2, float lineWidth = 0.5f)
    {
        _currentStream!.Append($"{Fmt(lineWidth)} w\n");
        _currentStream.Append($"{Fmt(x1)} {Fmt(y1)} m {Fmt(x2)} {Fmt(y2)} l S\n");
    }

    internal void DrawRect(float x, float y, float w, float h, bool fill = false)
    {
        _currentStream!.Append($"{Fmt(x)} {Fmt(y)} {Fmt(w)} {Fmt(h)} re ");
        _currentStream.Append(fill ? "f\n" : "S\n");
    }

    internal void SetFillColor(float r, float g, float b)
    {
        _currentStream!.Append($"{Fmt(r)} {Fmt(g)} {Fmt(b)} rg\n");
    }

    internal void SetStrokeColor(float r, float g, float b)
    {
        _currentStream!.Append($"{Fmt(r)} {Fmt(g)} {Fmt(b)} RG\n");
    }

    // ── Link annotations ─────────────────────────────────────────────────

    internal void AddLinkAnnotation(float x, float y, float width, float height, string uri)
    {
        _currentAnnotations.Add(new PdfAnnotation(x, y, width, height, uri));
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
        _currentStream!.Append("q\n");
        _currentStream.Append($"{Fmt(width)} 0 0 {Fmt(height)} {Fmt(x)} {Fmt(y)} cm\n");
        _currentStream.Append($"/{imageRef} Do\n");
        _currentStream.Append("Q\n");
    }

    // ── Text measurement ────────────────────────────────────────────────

    internal float MeasureText(string text, string font, float fontSize)
    {
        if (_embeddedFonts.TryGetValue(font, out var ttFont))
        {
            return ttFont.MeasureText(text, fontSize);
        }

        return MeasureStandardText(text, font, fontSize);
    }

    internal static float MeasureStandardText(string text, string font, float fontSize)
    {
        float avgWidth = font switch
        {
            "F2" => HelveticaBoldAvgWidth,
            "F4" => CourierAvgWidth,
            _ => HelveticaAvgWidth,
        };
        return text.Length * fontSize * avgWidth;
    }

    // ── Word wrapping ───────────────────────────────────────────────────

    internal List<string> WrapText(string text, string font, float fontSize, float maxWidth)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            result.Add("");
            return result;
        }

        var words = text.Split(' ');
        var currentLine = new StringBuilder();
        float currentWidth = 0;
        float spaceWidth = MeasureText(" ", font, fontSize);

        foreach (var word in words)
        {
            float wordWidth = MeasureText(word, font, fontSize);

            if (currentLine.Length > 0 && currentWidth + spaceWidth + wordWidth > maxWidth)
            {
                result.Add(currentLine.ToString());
                currentLine.Clear();
                currentWidth = 0;
            }

            if (currentLine.Length > 0)
            {
                currentLine.Append(' ');
                currentWidth += spaceWidth;
            }

            currentLine.Append(word);
            currentWidth += wordWidth;
        }

        if (currentLine.Length > 0)
            result.Add(currentLine.ToString());

        return result;
    }

    internal List<List<TextSegment>> WrapSegments(List<TextSegment> segments, float maxWidth)
    {
        var result = new List<List<TextSegment>>();
        var currentLine = new List<TextSegment>();
        float currentWidth = 0;

        foreach (var seg in segments)
        {
            float segWidth = MeasureText(seg.Text, seg.Font, seg.FontSize);

            if (currentLine.Count > 0 && currentWidth + segWidth > maxWidth)
            {
                result.Add(currentLine);
                currentLine = [];
                currentWidth = 0;
            }

            currentLine.Add(seg);
            currentWidth += segWidth;
        }

        if (currentLine.Count > 0)
            result.Add(currentLine);

        if (result.Count == 0)
            result.Add([]);

        return result;
    }

    // ── Final PDF assembly ──────────────────────────────────────────────

    internal byte[] ToBytes()
    {
        // Ensure last page is finished
        if (_currentStream is not null)
            FinishPage();

        // Embed registered TrueType fonts (fills placeholder objects)
        EmbedRegisteredFonts();

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

        // Catalog
        int catalogId = AllocObject(new PdfObject(
            $"<< /Type /Catalog /Pages {pagesId} 0 R >>"));

        // Info dictionary (deterministic)
        int infoId = AllocObject(new PdfObject(
            "<< /Producer (AdocNet PDF Renderer) /CreationDate (D:20260101000000+00'00') >>"));

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

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string EscapePdfString(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '(':  sb.Append("\\("); break;
                case ')':  sb.Append("\\)"); break;
                case '\\': sb.Append("\\\\"); break;
                default:
                    if (ch < 128)
                        sb.Append(ch);
                    else
                        sb.Append('?'); // Non-ASCII simplified to ? for standard fonts
                    break;
            }
        }
        return sb.ToString();
    }

    private static string Fmt(float value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    // ── Embedded font helpers ───────────────────────────────────────────

    private void TrackCodePoints(string fontKey, string text)
    {
        if (_usedCodePoints.TryGetValue(fontKey, out var codePoints))
        {
            foreach (var ch in text)
                codePoints.Add(ch);
        }
    }

    private static string EncodeTextAsGlyphIds(string text, TrueTypeFont font)
    {
        var sb = new StringBuilder(text.Length * 4);
        foreach (var ch in text)
        {
            var gid = font.GetGlyphId(ch);
            sb.Append($"{gid:X4}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Embeds all registered TrueType fonts into the PDF, filling in the placeholder objects.
    /// Must be called during ToBytes() after all text has been written.
    /// </summary>
    private void EmbedRegisteredFonts()
    {
        foreach (var (fontKey, font) in _embeddedFonts)
        {
            var usedCps = _usedCodePoints[fontKey];
            int placeholderId = _embeddedFontPlaceholders[fontKey];

            // 1. Compress font data
            byte[] compressedFont = Compress(font.FontData);

            // 2. Create font stream object
            int fontStreamId = AllocObject(new PdfObject(
                $"<< /Length {compressedFont.Length} /Length1 {font.FontData.Length} /Filter /FlateDecode >>",
                compressedFont));

            // 3. Build /W array (glyph widths for used glyphs)
            var wEntries = new StringBuilder();
            foreach (var cp in usedCps.OrderBy(c => c))
            {
                var gid = font.GetGlyphId(cp);
                var width = (int)(font.GetGlyphWidth(gid) * 1000.0 / font.UnitsPerEm);
                wEntries.Append($"{gid} [{width}] ");
            }

            // 4. Create font descriptor
            int descriptorId = AllocObject(new PdfObject(
                $"<< /Type /FontDescriptor /FontName /{font.FontName} " +
                $"/Flags 32 /ItalicAngle 0 " +
                $"/Ascent {font.Ascender * 1000 / font.UnitsPerEm} " +
                $"/Descent {font.Descender * 1000 / font.UnitsPerEm} " +
                $"/FontBBox [0 {font.Descender * 1000 / font.UnitsPerEm} 1000 {font.Ascender * 1000 / font.UnitsPerEm}] " +
                $"/FontFile2 {fontStreamId} 0 R >>"));

            // 5. Create CIDFont object
            int cidFontId = AllocObject(new PdfObject(
                $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{font.FontName} " +
                $"/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                $"/W [{wEntries}] /FontDescriptor {descriptorId} 0 R " +
                $"/CIDToGIDMap /Identity >>"));

            // 6. Build ToUnicode CMap
            var toUnicode = BuildToUnicodeCMap(font, usedCps);
            byte[] toUnicodeBytes = Encoding.ASCII.GetBytes(toUnicode);
            int toUnicodeId = AllocObject(new PdfObject(
                $"<< /Length {toUnicodeBytes.Length} >>",
                toUnicodeBytes));

            // 7. Create Type0 (composite) font object — fill the placeholder
            SetObject(placeholderId, new PdfObject(
                $"<< /Type /Font /Subtype /Type0 /BaseFont /{font.FontName} " +
                $"/Encoding /Identity-H " +
                $"/DescendantFonts [{cidFontId} 0 R] " +
                $"/ToUnicode {toUnicodeId} 0 R >>"));
        }
    }

    private static string BuildToUnicodeCMap(TrueTypeFont font, HashSet<int> usedCodePoints)
    {
        var sb = new StringBuilder();
        sb.AppendLine("/CIDInit /ProcSet findresource begin");
        sb.AppendLine("12 dict begin");
        sb.AppendLine("begincmap");
        sb.AppendLine("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def");
        sb.AppendLine("/CMapName /Adobe-Identity-UCS def");
        sb.AppendLine("/CMapType 2 def");
        sb.AppendLine("1 begincodespacerange");
        sb.AppendLine("<0000> <FFFF>");
        sb.AppendLine("endcodespacerange");

        var entries = usedCodePoints.OrderBy(cp => cp).ToList();
        // Write in batches of 100 (PDF limit per beginbfchar)
        for (int i = 0; i < entries.Count; i += 100)
        {
            int count = Math.Min(100, entries.Count - i);
            sb.AppendLine($"{count} beginbfchar");
            for (int j = i; j < i + count; j++)
            {
                var cp = entries[j];
                var gid = font.GetGlyphId(cp);
                sb.AppendLine($"<{gid:X4}> <{cp:X4}>");
            }
            sb.AppendLine("endbfchar");
        }

        sb.AppendLine("endcmap");
        sb.AppendLine("CMapName currentdict /CMap defineresource pop");
        sb.AppendLine("end");
        sb.AppendLine("end");
        return sb.ToString();
    }

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        // Write zlib header
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }
        // Write Adler-32 checksum
        uint adler = ComputeAdler32(data);
        ms.WriteByte((byte)(adler >> 24));
        ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >> 8));
        ms.WriteByte((byte)adler);
        return ms.ToArray();
    }

    private static uint ComputeAdler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (byte d in data)
        {
            a = (a + d) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }
}

/// <summary>Raw PDF object content, optionally with a binary stream.</summary>
internal readonly record struct PdfObject(string Content, byte[]? BinaryStream = null);

/// <summary>A link annotation to be added to a PDF page.</summary>
internal record struct PdfAnnotation(float X, float Y, float Width, float Height, string Uri);

/// <summary>A text segment with font and size info for mixed-style rendering.</summary>
internal readonly record struct TextSegment(string Text, string Font, float FontSize, string? LinkUri = null);
