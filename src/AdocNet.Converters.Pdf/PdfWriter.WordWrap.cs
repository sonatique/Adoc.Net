using System.Text;

namespace AdocNet.Converters.Pdf;

internal sealed partial class PdfWriter
{
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
        float hyphenWidth = MeasureText("-", font, fontSize);

        foreach (var word in words)
        {
            float wordWidth = MeasureText(word, font, fontSize);

            if (currentLine.Length > 0 && currentWidth + spaceWidth + wordWidth > maxWidth)
            {
                // Try hyphenation before breaking to next line
                if (HyphenationEnabled && TryHyphenate(word, font, fontSize,
                    maxWidth - currentWidth - spaceWidth, hyphenWidth,
                    out var firstPart, out var remainder))
                {
                    currentLine.Append(' ');
                    currentLine.Append(firstPart);
                    currentLine.Append('-');
                    result.Add(currentLine.ToString());
                    currentLine.Clear();
                    currentLine.Append(remainder);
                    currentWidth = MeasureText(remainder, font, fontSize);
                    continue;
                }

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
    /// Attempts to break a word at a hyphenation point so the first part
    /// (plus a trailing hyphen) fits within the available width.
    /// Returns false if no suitable break point exists.
    /// </summary>
    private bool TryHyphenate(string word, string font, float fontSize,
        float availableWidth, float hyphenWidth,
        out string firstPart, out string remainder)
    {
        firstPart = "";
        remainder = "";

        var breakPoints = Hyphenator.GetBreakPoints(word);
        if (breakPoints.Count == 0)
            return false;

        // Try break points from largest to smallest to find the longest fragment that fits
        for (int i = breakPoints.Count - 1; i >= 0; i--)
        {
            int bp = breakPoints[i];
            string candidate = word.Substring(0, bp);
            float candidateWidth = MeasureText(candidate, font, fontSize) + hyphenWidth;

            if (candidateWidth <= availableWidth)
            {
                firstPart = candidate;
                remainder = word.Substring(bp);
                return true;
            }
        }

        return false;
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

    // ── Wrapped text output ─────────────────────────────────────────────

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

        // Clamp to avoid absurd stretching: 1.5x with hyphenation, 2x without
        float maxMultiplier = HyphenationEnabled ? 1.5f : 2f;
        float maxSpacing = MeasureText(" ", segments[0].Font, segments[0].FontSize) * maxMultiplier;
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
