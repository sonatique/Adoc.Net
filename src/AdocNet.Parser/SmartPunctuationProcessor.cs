using System.Text;

namespace AdocNet.Parser;

/// <summary>
/// Applies smart typographic replacements to plain text.
/// Longest-match-first: --- before --, ... before individual dots.
/// </summary>
internal static class SmartPunctuationProcessor
{
    /// <summary>
    /// Applies smart punctuation replacements to <paramref name="text"/>.
    /// Returns the original string if no replacements were made (zero allocation).
    /// </summary>
    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Fast path: check if any trigger characters exist
        bool hasDash = false, hasDots = false, hasApostrophe = false, hasBacktick = false;
        foreach (char c in text)
        {
            if (c == '-') hasDash = true;
            else if (c == '.') hasDots = true;
            else if (c == '\'') hasApostrophe = true;
            else if (c == '`') hasBacktick = true;
        }
        if (!hasDash && !hasDots && !hasApostrophe && !hasBacktick) return text;

        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            // " -- " (space-dash-dash-space) → thin space + em dash + thin space
            // Must check before --- to avoid consuming the leading space.
            if (hasDash && i + 3 < text.Length && text[i] == ' ' && text[i + 1] == '-' && text[i + 2] == '-' && text[i + 3] == ' ')
            {
                // Check that these are exactly two dashes (not three)
                if (!(i + 3 < text.Length && text[i + 3] == '-'))
                {
                    sb.Append('\u2009'); // thin space
                    sb.Append('\u2014'); // em dash —
                    sb.Append('\u2009'); // thin space
                    i += 4; // skip " -- " (4 chars)
                    continue;
                }
            }

            // --- → em dash when NOT between word characters.
            // word---word: Asciidoctor does NOT convert --- between word characters (literal).
            if (hasDash && i + 2 < text.Length && text[i] == '-' && text[i + 1] == '-' && text[i + 2] == '-')
            {
                bool prevIsWord = i > 0 && char.IsLetterOrDigit(text[i - 1]);
                bool nextIsWord = i + 3 < text.Length && char.IsLetterOrDigit(text[i + 3]);
                if (prevIsWord && nextIsWord)
                {
                    // word---word: leave as literal dashes (Asciidoctor behavior)
                    sb.Append("---");
                    i += 3;
                    continue;
                }
                sb.Append('\u2014'); // —
                i += 3;
                continue;
            }

            // word--word → em dash + zero-width space (between word characters)
            // Only convert when both adjacent chars are word characters.
            // Otherwise leave as literal dashes (Asciidoctor behavior).
            if (hasDash && i + 1 < text.Length && text[i] == '-' && text[i + 1] == '-')
            {
                bool prevIsWord = i > 0 && char.IsLetterOrDigit(text[i - 1]);
                bool nextIsWord = i + 2 < text.Length && char.IsLetterOrDigit(text[i + 2]);
                if (prevIsWord && nextIsWord)
                {
                    sb.Append('\u2014'); // em dash —
                    sb.Append('\u200B'); // zero-width space
                    i += 2;
                    continue;
                }
                // Not between word characters — leave as literal dashes
            }

            // `' → right single quotation mark (curly apostrophe)
            if (hasBacktick && hasApostrophe && i + 1 < text.Length && text[i] == '`' && text[i + 1] == '\'')
            {
                sb.Append('\u2019'); // '
                i += 2;
                continue;
            }

            // ... → ellipsis + zero-width space (Asciidoctor behavior)
            if (hasDots && i + 2 < text.Length && text[i] == '.' && text[i + 1] == '.' && text[i + 2] == '.')
            {
                sb.Append('\u2026'); // …
                sb.Append('\u200B'); // zero-width space
                i += 3;
                continue;
            }

            // Apostrophe in contractions: letter'letter → right single quote
            if (hasApostrophe && text[i] == '\'' && i > 0 && i + 1 < text.Length
                && char.IsLetter(text[i - 1]) && char.IsLetter(text[i + 1]))
            {
                sb.Append('\u2019'); // '
                i++;
                continue;
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }
}
