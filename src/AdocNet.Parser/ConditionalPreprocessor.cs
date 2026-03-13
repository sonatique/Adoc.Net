using System.Text.RegularExpressions;

namespace AdocNet.Parser;

/// <summary>
/// Processes ifdef/ifndef/ifeval/endif directives as a text preprocessor,
/// running after include expansion but before block parsing.
/// Conditional directives are resolved and removed from the output text;
/// they do not appear in the AST.
/// </summary>
#if NET10_0_OR_GREATER
internal static partial class ConditionalPreprocessor
#else
internal static class ConditionalPreprocessor
#endif
{
    private const int MaxConditionalDepth = 64;

    // ifdef::name[inline content]  or  ifdef::name[]  (block form opener)
    // ifndef::name[inline content] or  ifndef::name[]  (block form opener)
#if NET10_0_OR_GREATER
    [GeneratedRegex(@"^(ifdef|ifndef)::([^\[]+)\[(.*)\]\s*$")]
    private static partial Regex ConditionalDirectiveRegex();
#else
    private static readonly Regex s_conditionalDirectiveRegex = new(@"^(ifdef|ifndef)::([^\[]+)\[(.*)\]\s*$", RegexOptions.Compiled);
    private static Regex ConditionalDirectiveRegex() => s_conditionalDirectiveRegex;
#endif

    // endif::[]  or  endif::name[]
#if NET10_0_OR_GREATER
    [GeneratedRegex(@"^endif::([^\[]*)\[\]\s*$")]
    private static partial Regex EndifRegex();
#else
    private static readonly Regex s_endifRegex = new(@"^endif::([^\[]*)\[\]\s*$", RegexOptions.Compiled);
    private static Regex EndifRegex() => s_endifRegex;
#endif

    // ifeval::[expression]
#if NET10_0_OR_GREATER
    [GeneratedRegex(@"^ifeval::\[(.+)\]\s*$")]
    private static partial Regex IfevalRegex();
#else
    private static readonly Regex s_ifevalRegex = new(@"^ifeval::\[(.+)\]\s*$", RegexOptions.Compiled);
    private static Regex IfevalRegex() => s_ifevalRegex;
#endif

    // Attribute entry: :name: value
#if NET10_0_OR_GREATER
    [GeneratedRegex(@"^:([A-Za-z0-9_][\w-]*):(.*)$")]
    private static partial Regex AttributeEntryRegex();
#else
    private static readonly Regex s_attributeEntryRegex = new(@"^:([A-Za-z0-9_][\w-]*):(.*)$", RegexOptions.Compiled);
    private static Regex AttributeEntryRegex() => s_attributeEntryRegex;
#endif

    // Attribute unset: :!name: or :name!:
#if NET10_0_OR_GREATER
    [GeneratedRegex(@"^:!([A-Za-z0-9_][\w-]*):")]
    private static partial Regex AttributeUnsetBangPrefixRegex();
#else
    private static readonly Regex s_attributeUnsetBangPrefixRegex = new(@"^:!([A-Za-z0-9_][\w-]*):", RegexOptions.Compiled);
    private static Regex AttributeUnsetBangPrefixRegex() => s_attributeUnsetBangPrefixRegex;
#endif

#if NET10_0_OR_GREATER
    [GeneratedRegex(@"^:([A-Za-z0-9_][\w-]*)!:")]
    private static partial Regex AttributeUnsetBangSuffixRegex();
#else
    private static readonly Regex s_attributeUnsetBangSuffixRegex = new(@"^:([A-Za-z0-9_][\w-]*)!:", RegexOptions.Compiled);
    private static Regex AttributeUnsetBangSuffixRegex() => s_attributeUnsetBangSuffixRegex;
#endif

    /// <summary>
    /// Processes ifdef/ifndef/ifeval directives, returning filtered text
    /// and any diagnostics generated during conditional evaluation.
    /// </summary>
    internal static (string FilteredText, IReadOnlyList<Diagnostic> Diagnostics) Process(
        string text,
        IReadOnlyDictionary<string, string>? externalAttributes = null)
    {
        var lines = TextUtility.SplitLines(text);
        var diagnostics = new List<Diagnostic>();
        var outputLines = new List<string>(lines.Length);

        // Pre-scan header attributes so conditionals at the top of the document work.
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (externalAttributes is not null)
        {
            foreach (var kvp in externalAttributes)
                attributes[kvp.Key] = kvp.Value;
        }

        // Snapshot locked attribute names (defaults + API-provided) BEFORE pre-scan.
        // These must not be overridden by document header/body entries.
        var lockedNames = new HashSet<string>(attributes.Keys, StringComparer.OrdinalIgnoreCase);

        PreScanHeaderAttributes(lines, attributes);

        // Stack of conditional blocks: each entry is (active, directive-line-number).
        // "active" means the condition evaluated to true (content should be included).
        var condStack = new Stack<(bool Active, int LineNumber)>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            int lineNumber = i + 1;

            // ── endif ────────────────────────────────────────────────────
            var endifMatch = EndifRegex().Match(line);
            if (endifMatch.Success)
            {
                if (condStack.Count == 0)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        "Unexpected endif directive without matching ifdef/ifndef/ifeval",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                }
                else
                {
                    condStack.Pop();
                }

                continue; // endif line is always consumed
            }

            // ── ifeval ───────────────────────────────────────────────────
            var ievalMatch = IfevalRegex().Match(line);
            if (ievalMatch.Success)
            {
                if (condStack.Count >= MaxConditionalDepth)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Conditional nesting depth exceeded ({MaxConditionalDepth}) at line {lineNumber}",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                    if (IsOutputActive(condStack))
                        outputLines.Add(line);
                    continue;
                }

                var expression = ievalMatch.Groups[1].Value;
                bool result = EvaluateIfeval(expression, attributes);
                condStack.Push((result, lineNumber));
                continue; // directive line consumed
            }

            // ── ifdef / ifndef ───────────────────────────────────────────
            var condMatch = ConditionalDirectiveRegex().Match(line);
            if (condMatch.Success)
            {
                var directive = condMatch.Groups[1].Value;   // "ifdef" or "ifndef"
                var attrExpr = condMatch.Groups[2].Value;    // "name" or "a,b" or "a+b"
                var inlineContent = condMatch.Groups[3].Value;

                bool conditionMet = EvaluateAttributeCondition(attrExpr, attributes);
                if (directive == "ifndef")
                    conditionMet = !conditionMet;

                if (inlineContent.Length > 0)
                {
                    // Single-line form: ifdef::name[content here]
                    if (conditionMet && IsOutputActive(condStack))
                        outputLines.Add(inlineContent);
                }
                else
                {
                    // Block form: ifdef::name[] ... endif::[]
                    if (condStack.Count >= MaxConditionalDepth)
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Warning,
                            $"Conditional nesting depth exceeded ({MaxConditionalDepth}) at line {lineNumber}",
                            new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                        if (IsOutputActive(condStack))
                            outputLines.Add(line);
                        continue;
                    }

                    condStack.Push((conditionMet, lineNumber));
                }

                continue; // directive line consumed
            }

            // ── Regular line ─────────────────────────────────────────────
            if (IsOutputActive(condStack))
            {
                outputLines.Add(line);

                // Track attribute entries in body for subsequent conditionals.
                // Pass lockedNames so body entries can't override defaults/API attributes.
                TryApplyAttributeEntry(line, attributes, lockedNames);
            }
        }

        // Warn about unclosed conditionals
        while (condStack.Count > 0)
        {
            var (_, openLine) = condStack.Pop();
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                "Unclosed conditional directive (missing endif::[])",
                new SourceRange(new(openLine, 1), new(openLine, 1))));
        }

        var filteredText = string.Join("\n", outputLines);
        return (filteredText, diagnostics);
    }

    /// <summary>
    /// Pre-scans lines for header attribute entries (:name: value)
    /// before the first non-attribute, non-title, non-blank line.
    /// Attributes already present in the dictionary (from defaults or API) are treated
    /// as locked and will not be overridden by document header entries, matching Asciidoctor.
    /// </summary>
    private static void PreScanHeaderAttributes(string[] lines, Dictionary<string, string> attributes)
    {
        // Snapshot the pre-existing (locked) attribute names so header entries can't override them.
        var lockedNames = new HashSet<string>(attributes.Keys, StringComparer.OrdinalIgnoreCase);

        bool pastTitle = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (string.IsNullOrWhiteSpace(line))
            {
                if (pastTitle)
                    break; // Blank line after title ends header
                continue;
            }

            // Document title: = Title
            if (!pastTitle && line.Length > 2 && line[0] == '=' && line[1] == ' ')
            {
                pastTitle = true;
                continue;
            }

            // Attribute entry (set or unset) — locked attributes can't be overridden
            if (line[0] == ':' && TryApplyAttributeEntry(line, attributes, lockedNames))
            {
                continue;
            }

            // Any other non-attribute line in header position → stop scanning
            // (but only after we've seen the title; before title, conditional directives
            // could appear, so we keep scanning past them)
            if (pastTitle)
                break;

            // Before the title, if we hit something that isn't an attribute or title,
            // it could be a conditional. Just skip to next line; the conditional
            // preprocessor will handle it.
            if (line.StartsWith("ifdef::", StringComparison.Ordinal) ||
                line.StartsWith("ifndef::", StringComparison.Ordinal) ||
                line.StartsWith("ifeval::", StringComparison.Ordinal) ||
                line.StartsWith("endif::", StringComparison.Ordinal))
            {
                continue;
            }

            break; // Not an attribute, not a title, not a conditional → end of header
        }
    }

    /// <summary>
    /// Returns true if all entries on the condition stack are active.
    /// (An empty stack means we're at the top level, so output is active.)
    /// </summary>
    private static bool IsOutputActive(Stack<(bool Active, int LineNumber)> condStack)
    {
        foreach (var (active, _) in condStack)
        {
            if (!active)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Evaluates an attribute condition expression (the part between :: and [).
    /// Supports: single attribute, comma-separated (OR/any), plus-separated (AND/all).
    /// </summary>
    private static bool EvaluateAttributeCondition(
        string attrExpr, IReadOnlyDictionary<string, string> attributes)
    {
        // OR: a,b,c — any attribute set
        if (attrExpr.Contains(','))
        {
            var names = attrExpr.Split(',');
            foreach (var name in names)
            {
                if (attributes.ContainsKey(name.Trim()))
                    return true;
            }

            return false;
        }

        // AND: a+b+c — all attributes set
        if (attrExpr.Contains('+'))
        {
            var names = attrExpr.Split('+');
            foreach (var name in names)
            {
                if (!attributes.ContainsKey(name.Trim()))
                    return false;
            }

            return true;
        }

        // Single attribute
        return attributes.ContainsKey(attrExpr.Trim());
    }

    /// <summary>
    /// Evaluates an ifeval expression. Supports:
    /// <c>"{name}" == "value"</c>, <c>"{name}" != "value"</c>,
    /// and numeric comparisons: ==, !=, &lt;, &gt;, &lt;=, &gt;=
    /// Attribute references ({name}) are substituted before comparison.
    /// </summary>
    private static bool EvaluateIfeval(
        string expression, IReadOnlyDictionary<string, string> attributes)
    {
        // Substitute attribute references
        var expr = SubstituteAttributes(expression, attributes);

        // Try to match: LHS operator RHS
        // Both sides can be quoted strings or bare values
        var match = IfevalComparisonRegex().Match(expr);
        if (!match.Success)
            return false;

        var lhs = Unquote(match.Groups[1].Value.Trim());
        var op = match.Groups[2].Value.Trim();
        var rhs = Unquote(match.Groups[3].Value.Trim());

        // Try numeric comparison: integer first, then floating-point for Asciidoctor compatibility
        if (int.TryParse(lhs, out var lhsInt) && int.TryParse(rhs, out var rhsInt))
        {
            return op switch
            {
                "==" => lhsInt == rhsInt,
                "!=" => lhsInt != rhsInt,
                "<" => lhsInt < rhsInt,
                ">" => lhsInt > rhsInt,
                "<=" => lhsInt <= rhsInt,
                ">=" => lhsInt >= rhsInt,
                _ => false
            };
        }

#if NET10_0_OR_GREATER
        if (double.TryParse(lhs, System.Globalization.CultureInfo.InvariantCulture, out var lhsDbl)
            && double.TryParse(rhs, System.Globalization.CultureInfo.InvariantCulture, out var rhsDbl))
#else
        if (double.TryParse(lhs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lhsDbl)
            && double.TryParse(rhs, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rhsDbl))
#endif
        {
            return op switch
            {
                "==" => lhsDbl == rhsDbl,
                "!=" => lhsDbl != rhsDbl,
                "<" => lhsDbl < rhsDbl,
                ">" => lhsDbl > rhsDbl,
                "<=" => lhsDbl <= rhsDbl,
                ">=" => lhsDbl >= rhsDbl,
                _ => false
            };
        }

        // String comparison
        return op switch
        {
            "==" => string.Equals(lhs, rhs, StringComparison.Ordinal),
            "!=" => !string.Equals(lhs, rhs, StringComparison.Ordinal),
            _ => false
        };
    }

#if NET10_0_OR_GREATER
    [GeneratedRegex(@"^(.+?)\s*(==|!=|<=|>=|<|>)\s*(.+)$")]
    private static partial Regex IfevalComparisonRegex();
#else
    private static readonly Regex s_ifevalComparisonRegex = new(@"^(.+?)\s*(==|!=|<=|>=|<|>)\s*(.+)$", RegexOptions.Compiled);
    private static Regex IfevalComparisonRegex() => s_ifevalComparisonRegex;
#endif

    /// <summary>
    /// Substitutes {name} attribute references in a string.
    /// </summary>
    private static string SubstituteAttributes(
        string text, IReadOnlyDictionary<string, string> attributes)
    {
        // Simple regex-free approach for {name} substitution
        var result = text;
        int searchFrom = 0;
        int startIdx;
        while (searchFrom < result.Length && (startIdx = result.IndexOf('{', searchFrom)) >= 0)
        {
            var endIdx = result.IndexOf('}', startIdx);
            if (endIdx < 0)
                break;

            var name = result[(startIdx + 1)..endIdx];
            if (attributes.TryGetValue(name, out var value))
            {
                result = string.Concat(result.AsSpan(0, startIdx), value, result.AsSpan(endIdx + 1));
                searchFrom = startIdx + value.Length;
            }
            else
            {
                // Leave unresolved references as-is; advance past this brace
                searchFrom = endIdx + 1;
            }
        }

        return result;
    }

    /// <summary>
    /// Removes surrounding double quotes from a string, if present.
    /// </summary>
    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1];
        return value;
    }

    /// <summary>
    /// Tracks attribute set/unset in body lines so that subsequent conditionals
    /// can reference attributes defined earlier in the document body.
    /// </summary>
    private static void TrackAttributeChange(string line, Dictionary<string, string> attributes)
        => TryApplyAttributeEntry(line, attributes, lockedNames: null);

    /// <summary>
    /// Shared helper: attempts to parse a line as an attribute entry (<c>:name: value</c>)
    /// or attribute unset (<c>:!name:</c> / <c>:name!:</c>), and applies the change
    /// to the <paramref name="attributes"/> dictionary.
    /// When <paramref name="lockedNames"/> is provided, attributes in that set are skipped
    /// (the line is still recognized as an attribute entry but the value is not applied).
    /// Returns <c>true</c> if the line was an attribute entry (set or unset).
    /// </summary>
    private static bool TryApplyAttributeEntry(string line, Dictionary<string, string> attributes,
        HashSet<string>? lockedNames)
    {
        if (line.Length < 3 || line[0] != ':')
            return false;

        // Unset: :!name:
        var unsetPrefixMatch = AttributeUnsetBangPrefixRegex().Match(line);
        if (unsetPrefixMatch.Success)
        {
            var name = unsetPrefixMatch.Groups[1].Value;
            if (lockedNames is null || !lockedNames.Contains(name))
                attributes.Remove(name);
            return true;
        }

        // Unset: :name!:
        var unsetSuffixMatch = AttributeUnsetBangSuffixRegex().Match(line);
        if (unsetSuffixMatch.Success)
        {
            var name = unsetSuffixMatch.Groups[1].Value;
            if (lockedNames is null || !lockedNames.Contains(name))
                attributes.Remove(name);
            return true;
        }

        // Set: :name: value
        var attrMatch = AttributeEntryRegex().Match(line);
        if (attrMatch.Success)
        {
            var name = attrMatch.Groups[1].Value;
            if (lockedNames is null || !lockedNames.Contains(name))
                attributes[name] = attrMatch.Groups[2].Value.Trim();
            return true;
        }

        return false;
    }
}
