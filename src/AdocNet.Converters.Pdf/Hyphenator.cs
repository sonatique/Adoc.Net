namespace AdocNet.Converters.Pdf;

/// <summary>
/// Liang/Knuth pattern-based hyphenation engine.
/// Computes valid hyphenation points for English words using TeX-style patterns.
/// </summary>
internal static class Hyphenator
{
    /// <summary>Minimum characters before first hyphen.</summary>
    private const int LeftMin = 2;

    /// <summary>Minimum characters after last hyphen.</summary>
    private const int RightMin = 3;

    /// <summary>Minimum word length to attempt hyphenation.</summary>
    private const int MinWordLength = 5;

    private static readonly Dictionary<string, byte[]> Patterns = BuildPatternDictionary();

    /// <summary>
    /// Returns possible hyphenation points for a word.
    /// Each int is a character index where a hyphen may be inserted
    /// (i.e., the break occurs after that index).
    /// Returns empty if the word is too short or cannot be hyphenated.
    /// </summary>
    internal static List<int> GetBreakPoints(string word)
    {
        var result = new List<int>();
        if (word.Length < MinWordLength)
            return result;

        // The Liang algorithm works on a lowercased word wrapped with boundary markers.
        string lowerWord = word.ToLowerInvariant();
        string wrapped = "." + lowerWord + ".";
        int len = wrapped.Length;

        // Levels array: one more than the number of characters in the original word.
        // levels[i] > 0 at odd values means a break is allowed before character i.
        byte[] levels = new byte[len + 1];

        // Apply all matching patterns
        for (int i = 0; i < len; i++)
        {
            for (int j = i + 1; j <= len; j++)
            {
                string fragment = wrapped.Substring(i, j - i);
                if (Patterns.TryGetValue(fragment, out var patternLevels))
                {
                    for (int k = 0; k < patternLevels.Length; k++)
                    {
                        int pos = i + k;
                        if (pos < levels.Length && patternLevels[k] > levels[pos])
                            levels[pos] = patternLevels[k];
                    }
                }
            }
        }

        // Extract break points: odd levels indicate valid hyphenation points.
        // levels[0] corresponds to before the '.' prefix, so real character positions
        // start at index 1. A break "before character i in the original word" means
        // levels[i+1] is odd (accounting for the '.' prefix).
        for (int i = LeftMin; i <= lowerWord.Length - RightMin; i++)
        {
            if (levels[i + 1] % 2 != 0)
                result.Add(i);
        }

        return result;
    }

    /// <summary>
    /// Parses TeX-style hyphenation patterns into a dictionary.
    /// Pattern format: "ab1cd2ef" means the substring "abcdef" has levels [0,0,1,0,2,0,0].
    /// </summary>
    private static Dictionary<string, byte[]> BuildPatternDictionary()
    {
        var dict = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var patternData = HyphenationPatterns.EnglishPatterns;
        int start = 0;

        while (start < patternData.Length)
        {
            // Skip whitespace
            while (start < patternData.Length && char.IsWhiteSpace(patternData[start]))
                start++;
            if (start >= patternData.Length)
                break;

            // Read one pattern token
            int end = start;
            while (end < patternData.Length && !char.IsWhiteSpace(patternData[end]))
                end++;

            string token = patternData.Substring(start, end - start);
            start = end;

            ParsePattern(token, dict);
        }

        return dict;
    }

    /// <summary>
    /// Parses a single TeX hyphenation pattern (e.g., ".ab4c" or "a1bc")
    /// into its key string (letters + dots) and levels array.
    /// </summary>
    private static void ParsePattern(string token, Dictionary<string, byte[]> dict)
    {
        // Separate letters from digits
        var keyChars = new System.Text.StringBuilder();
        var levelList = new List<byte>();

        for (int i = 0; i < token.Length; i++)
        {
            char ch = token[i];
            if (ch >= '0' && ch <= '9')
            {
                levelList.Add((byte)(ch - '0'));
            }
            else
            {
                // If the previous character wasn't a digit, insert a zero level
                if (levelList.Count <= keyChars.Length)
                    levelList.Add(0);
                keyChars.Append(ch);
            }
        }

        // Ensure there's a trailing level
        while (levelList.Count <= keyChars.Length)
            levelList.Add(0);

        string key = keyChars.ToString();
        if (key.Length > 0)
            dict[key] = levelList.ToArray();
    }
}
