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
            _cursorY -= leading;
            return leading;
        }

        int pos = 0;
        while (pos < text.Length)
        {
            int remaining = text.Length - pos;
            int lineLen = Math.Min(remaining, charsPerLine);
            string line = text.Substring(pos, lineLen);

            EnsurePage();
            WriteText(line, font, fontSize, MarginLeftValue, _cursorY);
            _cursorY -= leading;
            consumed += leading;
            pos += lineLen;
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
        if (font == "F4") // Courier — monospace
            return text.Length * fontSize * HelveticaMetrics.CourierWidth;

        float total = 0;
        foreach (var ch in text)
            total += HelveticaMetrics.MeasureChar(ch, font, fontSize);

        return total;
    }

    // ── Word wrapping ───────────────────────────────────────────────────

    /// <summary>Characters that must never appear at the start of a wrapped line.</summary>
    private static readonly HashSet<char> NoStartChars =
    [
        ')', ']', '}', '>', ',', '.', ';', ':', '!', '?',
        '\u2014', // em dash
        '\u2013', // en dash
        '\u2019', // right single quote
        '\u201D', // right double quote
        '\u2010', // hyphen
        '\u2026', // ellipsis
    ];

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

        // Post-process: pull no-start punctuation back to previous line
        FixLineStartPunctuation(result);

        return result;
    }

    /// <summary>
    /// If a line starts with a character from <see cref="NoStartChars"/>,
    /// move that character (and any preceding space) back to the previous line.
    /// </summary>
    private static void FixLineStartPunctuation(List<string> lines)
    {
        for (int i = 1; i < lines.Count; i++)
        {
            if (lines[i].Length > 0 && NoStartChars.Contains(lines[i][0]))
            {
                // Find how many leading no-start characters to pull back
                int pullCount = 0;
                while (pullCount < lines[i].Length && NoStartChars.Contains(lines[i][pullCount]))
                    pullCount++;

                string pulled = lines[i].Substring(0, pullCount);
                string remaining = lines[i].Substring(pullCount).TrimStart();

                lines[i - 1] += pulled;

                if (remaining.Length > 0)
                    lines[i] = remaining;
                else
                {
                    lines.RemoveAt(i);
                    i--;
                }
            }
        }
    }

    internal List<List<TextSegment>> WrapSegments(List<TextSegment> segments, float maxWidth)
    {
        var result = new List<List<TextSegment>>();
        var currentLine = new List<TextSegment>();
        float currentWidth = 0;

        foreach (var seg in segments)
        {
            float spaceWidth = MeasureText(" ", seg.Font, seg.FontSize);

            // Split segment text into words for word-level wrapping
            var words = seg.Text.Split(' ');
            var wordBuffer = new StringBuilder();

            for (int i = 0; i < words.Length; i++)
            {
                var word = words[i];
                float wordWidth = MeasureText(word, seg.Font, seg.FontSize);
                float neededWidth = wordBuffer.Length > 0 || currentWidth > 0
                    ? spaceWidth + wordWidth
                    : wordWidth;

                if (currentWidth + neededWidth > maxWidth && (currentLine.Count > 0 || wordBuffer.Length > 0))
                {
                    // Flush word buffer as a segment on the current line
                    if (wordBuffer.Length > 0)
                    {
                        currentLine.Add(new TextSegment(wordBuffer.ToString(), seg.Font, seg.FontSize, seg.LinkUri));
                        wordBuffer.Clear();
                    }

                    result.Add(currentLine);
                    currentLine = [];
                    currentWidth = 0;
                    neededWidth = wordWidth;
                }

                if (wordBuffer.Length > 0)
                    wordBuffer.Append(' ');
                else if (currentWidth > 0 && i == 0)
                {
                    // Add space between previous segment and this one
                    wordBuffer.Append(' ');
                }

                wordBuffer.Append(word);
                currentWidth += neededWidth;
            }

            // Flush remaining words in buffer
            if (wordBuffer.Length > 0)
            {
                currentLine.Add(new TextSegment(wordBuffer.ToString(), seg.Font, seg.FontSize, seg.LinkUri));
            }
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

        byte[] result = ms.ToArray();

        // Replace total page count placeholders with actual count (same byte length)
        ReplaceTotalPagesPlaceholder(result, _pageObjectIds.Count);

        return result;
    }

    /// <summary>
    /// Scans the PDF bytes for the total pages placeholder and replaces it
    /// with the actual page count, padded to maintain byte offsets.
    /// </summary>
    private static void ReplaceTotalPagesPlaceholder(byte[] data, int totalPages)
    {
        byte[] placeholder = Encoding.ASCII.GetBytes(TotalPagesPlaceholder);
        string replacement = totalPages.ToString(CultureInfo.InvariantCulture);
        // Pad replacement to match placeholder length
        byte[] replacementBytes = Encoding.ASCII.GetBytes(replacement.PadRight(placeholder.Length));

        for (int i = 0; i <= data.Length - placeholder.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < placeholder.Length; j++)
            {
                if (data[i + j] != placeholder[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                Array.Copy(replacementBytes, 0, data, i, replacementBytes.Length);
                i += placeholder.Length - 1; // skip past replacement
            }
        }
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
                    {
                        sb.Append(ch);
                    }
                    else if (ch <= 255)
                    {
                        // WinAnsiEncoding: emit as octal escape
                        sb.Append('\\');
                        sb.Append(Convert.ToString(ch, 8).PadLeft(3, '0'));
                    }
                    else
                    {
                        // Outside WinAnsi range — best effort: try to map common Unicode chars
                        sb.Append(MapUnicodeToWinAnsi(ch));
                    }
                    break;
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

    // ── Embedded font helpers ───────────────────────────────────────────

    private void TrackCodePoints(string fontKey, string text) =>
        PdfFontEmbedder.TrackCodePoints(_usedCodePoints, fontKey, text);

    private static string EncodeTextAsGlyphIds(string text, TrueTypeFont font) =>
        PdfFontEmbedder.EncodeTextAsGlyphIds(text, font);

    private void EmbedRegisteredFonts() =>
        PdfFontEmbedder.EmbedFonts(_embeddedFonts, _usedCodePoints, _embeddedFontPlaceholders,
            obj => AllocObject(obj), (id, obj) => SetObject(id, obj));

    private static byte[] Compress(byte[] data) =>
        PdfFontEmbedder.Compress(data);
}
