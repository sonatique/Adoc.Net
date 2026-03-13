namespace AdocNet;

/// <summary>
/// Shared text processing utilities used across parser and include expansion.
/// </summary>
internal static class TextUtility
{
    /// <summary>
    /// Splits text into lines, normalizing line endings (\r\n, \r, \n) in a single pass
    /// without creating intermediate string copies.
    /// </summary>
    public static string[] SplitLines(string text)
    {
        if (text.Length == 0) return [""];

        // Count lines first to allocate exactly once.
        int lineCount = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                lineCount++;
            else if (text[i] == '\r')
            {
                lineCount++;
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++; // skip \n in \r\n pair
            }
        }

        var lines = new string[lineCount];
        int lineIndex = 0;
        int lineStart = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines[lineIndex++] = text[lineStart..i];
                lineStart = i + 1;
            }
            else if (text[i] == '\r')
            {
                lines[lineIndex++] = text[lineStart..i];
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++; // skip \n in \r\n pair
                lineStart = i + 1;
            }
        }

        // Final line (after last newline, or the entire text if no newlines).
        lines[lineIndex] = text[lineStart..];
        return lines;
    }

    /// <summary>
    /// Returns the trimmed-end length of a string without allocating a new string.
    /// </summary>
    public static int TrimmedEndLength(string s)
    {
        int len = s.Length;
        while (len > 0 && char.IsWhiteSpace(s[len - 1]))
            len--;
        return len;
    }
}
