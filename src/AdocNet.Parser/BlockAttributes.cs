using AdocNet;

namespace AdocNet.Parser;

/// <summary>
/// Parsed block attribute list from <c>[...]</c> lines.
/// Supports positional values, named key=value pairs, and shorthand notation (#id, .role, %option).
/// </summary>
internal sealed class BlockAttributes
{
    /// <summary>First positional value — the block style (source, quote, verse, listing, etc.).</summary>
    public string? Style { get; set; }

    /// <summary>All positional values in order.</summary>
    public List<string> Positional { get; } = [];

    /// <summary>Named key=value pairs.</summary>
    public Dictionary<string, string> Named { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Block ID from #id shorthand.</summary>
    public string? Id { get; set; }

    /// <summary>Roles from .role shorthand or role="..." named attribute.</summary>
    public List<string> Roles { get; } = [];

    /// <summary>Options from %option shorthand or options="..." named attribute.</summary>
    public List<string> Options { get; } = [];

    /// <summary>Substitution override from subs="..." attribute.</summary>
    public SubstitutionKind? Subs { get; set; }

    /// <summary>When true, <see cref="Subs"/> is ignored and incremental add/remove fields are used instead.</summary>
    public bool SubsIsIncremental { get; set; }

    /// <summary>Substitutions to add to the block's default set (incremental mode only).</summary>
    public SubstitutionKind SubsToAdd { get; set; }

    /// <summary>Substitutions to remove from the block's default set (incremental mode only).</summary>
    public SubstitutionKind SubsToRemove { get; set; }

    /// <summary>
    /// Parses a block attribute line. Returns null if the line is not a valid attribute list.
    /// </summary>
    public static BlockAttributes? Parse(string line)
    {
        if (line.Length < 2 || line[0] != '[' || line[^1] != ']')
            return null;

        var content = line[1..^1];
        var result = new BlockAttributes();

        if (content.Length == 0)
            return result;

        var entries = SplitRespectingQuotes(content);

        int positionalIndex = 0;
        foreach (var rawEntry in entries)
        {
            var entry = rawEntry.Trim();
            if (entry.Length == 0)
            {
                result.Positional.Add("");
                positionalIndex++;
                continue;
            }

            // Shorthand: #id, .role, %option (only as first entry)
            if (positionalIndex == 0 && entry.Length > 0 && (entry[0] == '#' || entry[0] == '.' || entry[0] == '%'))
            {
                ParseShorthands(entry, result);
                continue;
            }

            // Style with embedded shorthands: style#id, style.role, style%option (first entry only)
            if (positionalIndex == 0 && entry.Length > 1)
            {
                var shorthandStart = FindFirstShorthandMarker(entry);
                if (shorthandStart > 0)
                {
                    result.Positional.Add(entry[..shorthandStart]);
                    positionalIndex++;
                    ParseShorthands(entry[shorthandStart..], result);
                    continue;
                }
            }

            // Named: name=value
            var eqIdx = FindEqualsOutsideQuotes(entry);
            if (eqIdx > 0)
            {
                var name = entry[..eqIdx].Trim();
                var value = Unquote(entry[(eqIdx + 1)..].Trim());
                result.Named[name] = value;
                continue;
            }

            result.Positional.Add(entry);
            positionalIndex++;
        }

        // Style = first positional
        if (result.Positional.Count > 0)
        {
            var first = result.Positional[0].Trim();
            result.Style = first.Length > 0 ? first : null;
        }

        // Extract roles from named "role" attribute
        if (result.Named.TryGetValue("role", out var roleValue))
        {
            foreach (var role in roleValue.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                result.Roles.Add(role);
        }

        // Extract options from named "options" or "opts" attribute
        if (result.Named.TryGetValue("options", out var optionsValue) ||
            result.Named.TryGetValue("opts", out optionsValue))
        {
            foreach (var opt in optionsValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
                result.Options.Add(opt.Trim());
        }

        // Parse subs
        if (result.Named.TryGetValue("subs", out var subsValue))
        {
            if (IsIncrementalSubsSpec(subsValue))
            {
                result.SubsIsIncremental = true;
                ParseIncrementalSubs(subsValue, out var toAdd, out var toRemove);
                result.SubsToAdd = toAdd;
                result.SubsToRemove = toRemove;
            }
            else
            {
                result.Subs = ParseSubstitutions(subsValue);
            }
        }

        return result;
    }

    private static List<string> SplitRespectingQuotes(string input)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        char? inQuote = null;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (inQuote.HasValue)
            {
                if (c == inQuote.Value)
                    inQuote = null;
                current.Append(c);
            }
            else if (c == '"' || c == '\'')
            {
                inQuote = c;
                current.Append(c);
            }
            else if (c == ',')
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        parts.Add(current.ToString());
        return parts;
    }

    /// <summary>
    /// Returns the index of the first shorthand marker (#, ., %) in the entry,
    /// or -1 if none is found. Used to split "style#id" into style + shorthands.
    /// </summary>
    private static int FindFirstShorthandMarker(string entry)
    {
        for (int i = 0; i < entry.Length; i++)
        {
            if (entry[i] is '#' or '.' or '%')
                return i;
        }
        return -1;
    }

    private static void ParseShorthands(string entry, BlockAttributes result)
    {
        int i = 0;
        while (i < entry.Length)
        {
            char marker = entry[i];
            i++;
            int start = i;
            while (i < entry.Length && entry[i] != '#' && entry[i] != '.' && entry[i] != '%')
                i++;
            var value = entry[start..i].Trim();
            if (value.Length == 0) continue;
            switch (marker)
            {
                case '#': result.Id = value; break;
                case '.': result.Roles.Add(value); break;
                case '%': result.Options.Add(value); break;
            }
        }
    }

    private static int FindEqualsOutsideQuotes(string entry)
    {
        char? inQuote = null;
        for (int i = 0; i < entry.Length; i++)
        {
            char c = entry[i];
            if (inQuote.HasValue) { if (c == inQuote.Value) inQuote = null; }
            else if (c == '"' || c == '\'') { inQuote = c; }
            else if (c == '=') { return i; }
        }
        return -1;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))
                return value[1..^1];
        }
        return value;
    }

    internal static SubstitutionKind ParseSubstitutions(string spec)
    {
        var trimmed = spec.Trim();
        return trimmed switch
        {
            "normal" => SubstitutionKind.Normal,
            "none" => SubstitutionKind.None,
            "verbatim" => SubstitutionKind.Verbatim,
            _ => ParseSubstitutionList(trimmed),
        };
    }

    private static SubstitutionKind ParseSubstitutionList(string spec)
    {
        var result = SubstitutionKind.None;
        bool additive = false;

        foreach (var part in spec.Split(','))
        {
            var name = part.Trim();

            // Handle additive suffix: "attributes+" means add to defaults
            if (name.EndsWith('+'))
            {
                additive = true;
                name = name[..^1];
            }

            result |= ResolveSingleSub(name);
        }

        // When additive, the caller's default subs are preserved;
        // signal this by including the Verbatim base set (SpecialCharacters).
        if (additive)
            result |= SubstitutionKind.Verbatim;

        return result;
    }

    /// <summary>
    /// Returns true if the subs spec contains any incremental prefixes (+name or -name).
    /// </summary>
    private static bool IsIncrementalSubsSpec(string spec)
    {
        var trimmed = spec.Trim();
        if (trimmed is "normal" or "none" or "verbatim")
            return false;

        foreach (var part in trimmed.Split(','))
        {
            var name = part.Trim();
            if (name.Length > 0 && (name[0] == '+' || name[0] == '-'))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Parses an incremental subs spec into additions and removals.
    /// </summary>
    private static void ParseIncrementalSubs(string spec, out SubstitutionKind toAdd, out SubstitutionKind toRemove)
    {
        toAdd = SubstitutionKind.None;
        toRemove = SubstitutionKind.None;

        foreach (var part in spec.Split(','))
        {
            var name = part.Trim();
            if (name.Length == 0) continue;

            if (name[0] == '+')
            {
                toAdd |= ResolveSingleSub(name[1..]);
            }
            else if (name[0] == '-')
            {
                toRemove |= ResolveSingleSub(name[1..]);
            }
        }
    }

    private static SubstitutionKind ResolveSingleSub(string name) =>
        name switch
        {
            "specialcharacters" or "specialchars" => SubstitutionKind.SpecialCharacters,
            "quotes" => SubstitutionKind.Quotes,
            "attributes" => SubstitutionKind.Attributes,
            "replacements" => SubstitutionKind.Replacements,
            "macros" => SubstitutionKind.Macros,
            "post_replacements" => SubstitutionKind.PostReplacements,
            _ => SubstitutionKind.None,
        };
}
