using System.Text;
using System.Text.RegularExpressions;
using AdocNet;
using AdocNet.Internal.Compatibility;

namespace AdocNet.Parser;

/// <summary>
/// Preprocesses AsciiDoc source text to expand <c>include::path/to/file[]</c> directives.
/// <para>
/// Supported syntax: <c>include::relative/path.adoc[]</c> — local file paths only,
/// resolved relative to the including document's directory.
/// </para>
/// <para>
/// Unsupported forms (URLs) produce a diagnostic but do not crash.
/// Attribute references (<c>{name}</c>) in include paths are resolved using document
/// attributes and API-provided attributes.
/// </para>
/// </summary>
internal static class IncludeExpander
{
    /// <summary>Default maximum nesting depth for recursive includes.</summary>
    public const int DefaultMaxDepth = 10;

    // Matches: include::path[optional-attributes]
    // Group 1 = path, Group 2 = bracket contents (may be empty).
    private static readonly Regex IncludePattern = new(
        @"^include::(.+?)\[(.*)\]\s*$",
        RegexOptions.Compiled);

    // Matches :name: value attribute definitions.
    private static readonly Regex AttributeDefPattern = new(
        @"^:([a-zA-Z0-9_][\w-]*?):\s*(.*?)\s*$",
        RegexOptions.Compiled);

    // Matches {name} attribute references.
    private static readonly Regex AttributeRefPattern = new(
        @"\{([a-zA-Z0-9_][\w-]*?)\}",
        RegexOptions.Compiled);

    /// <summary>Shared HttpClient for URL includes (lazily created).</summary>
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 10 * 1024 * 1024, // 10 MB safety limit
        };
        return client;
    }

    /// <summary>
    /// Expands all <c>include::</c> directives in <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The AsciiDoc source text.</param>
    /// <param name="baseDirectory">
    /// Directory used to resolve relative include paths.
    /// Typically the directory containing the document being parsed.
    /// </param>
    /// <param name="reader">
    /// File reader abstraction. Pass <c>null</c> to use the default <see cref="FileIncludeReader"/>.
    /// </param>
    /// <param name="maxDepth">Maximum nesting depth for recursive includes.</param>
    /// <param name="attributes">Optional document attributes used for conditional-aware include skipping.</param>
    /// <returns>The expanded text and any diagnostics produced during expansion.</returns>
    public static ExpandResult Expand(
        string text,
        string baseDirectory,
        IIncludeReader? reader = null,
        int maxDepth = DefaultMaxDepth,
        IReadOnlyDictionary<string, string>? attributes = null)
        => Expand(text, baseDirectory, reader, maxDepth, attributes, allowUriRead: false);

    /// <summary>
    /// Expands all <c>include::</c> directives in <paramref name="text"/>.
    /// </summary>
    public static ExpandResult Expand(
        string text,
        string baseDirectory,
        IIncludeReader? reader,
        int maxDepth,
        IReadOnlyDictionary<string, string>? attributes,
        bool allowUriRead)
        => Expand(text, baseDirectory, reader, maxDepth, attributes, allowUriRead, SafeMode.Unsafe);

    /// <summary>
    /// Expands all <c>include::</c> directives in <paramref name="text"/> with safe mode enforcement.
    /// </summary>
    public static ExpandResult Expand(
        string text,
        string baseDirectory,
        IIncludeReader? reader,
        int maxDepth,
        IReadOnlyDictionary<string, string>? attributes,
        bool allowUriRead,
        SafeMode safeMode)
    {
        Guard.NotNull(text);
        Guard.NotNull(baseDirectory);

        reader ??= FileIncludeReader.Instance;
        var diagnostics = new List<Diagnostic>();
        var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // First pass: build preliminary attribute map from document attribute definitions.
        var attrMap = BuildAttributeMap(text, attributes);

        var expanded = ExpandRecursive(text, baseDirectory, reader, diagnostics, visitedPaths, 0, maxDepth, attrMap, allowUriRead, safeMode);
        return new ExpandResult(expanded, diagnostics);
    }

    private static string ExpandRecursive(
        string text,
        string baseDirectory,
        IIncludeReader reader,
        List<Diagnostic> diagnostics,
        HashSet<string> visitedPaths,
        int currentDepth,
        int maxDepth,
        IReadOnlyDictionary<string, string>? attributes,
        bool allowUriRead = false,
        SafeMode safeMode = SafeMode.Unsafe)
    {
        var lines = TextUtility.SplitLines(text);
        var result = new StringBuilder();
        var condStack = new Stack<bool>(); // true = active

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            int lineNumber = i + 1;

            // Handle conditional directives — always pass through to output
            if (TryHandleConditional(line, condStack, attributes))
            {
                if (result.Length > 0 || i > 0)
                    result.Append('\n');
                result.Append(line);
                continue;
            }

            // Inside a false conditional block — skip include expansion
            if (condStack.Count > 0 && condStack.Any(a => !a))
            {
                if (result.Length > 0 || i > 0)
                    result.Append('\n');
                result.Append(line);
                continue;
            }

            var match = IncludePattern.Match(line);
            if (!match.Success)
            {
                if (result.Length > 0 || i > 0)
                    result.Append('\n');
                result.Append(line);
                continue;
            }

            var rawPath = match.Groups[1].Value.Trim();
            var bracketContent = match.Groups[2].Value.Trim();

            // ── Safe mode enforcement ──
            if (safeMode >= SafeMode.Server)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"Include disabled by safe mode ({safeMode}): {rawPath} (line {lineNumber})",
                    new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                if (result.Length > 0 || i > 0)
                    result.Append('\n');
                result.Append(line);
                continue;
            }

            // ── Parse bracket attributes ──
            string? linesValue = null;
            string? tagValue = null;
            string? tagsValue = null;
            int? levelOffset = null;
            int? indentValue = null;
            bool hasUnsupportedAttributes = false;

            if (bracketContent.Length > 0)
            {
                var attrs = ParseIncludeAttributes(bracketContent);
                if (attrs.TryGetValue("lines", out var lv))
                    linesValue = lv;
                if (attrs.TryGetValue("tag", out var tv))
                    tagValue = tv;
                if (attrs.TryGetValue("tags", out var tsv))
                    tagsValue = tsv;
                if (attrs.TryGetValue("leveloffset", out var lo))
                {
                    if (int.TryParse(lo, out var parsed))
                        levelOffset = parsed;
                }
                if (attrs.TryGetValue("indent", out var iv))
                {
                    if (int.TryParse(iv, out var parsed) && parsed >= 0)
                        indentValue = parsed;
                }

                // Warn about any attributes we don't support yet.
                foreach (var key in attrs.Keys)
                {
                    if (!string.Equals(key, "lines", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(key, "tag", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(key, "tags", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(key, "leveloffset", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(key, "indent", StringComparison.OrdinalIgnoreCase))
                        hasUnsupportedAttributes = true;
                }

                if (hasUnsupportedAttributes)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Include attributes not supported: [{bracketContent}] (line {lineNumber})",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                }
            }

            // ── URL includes ──
            if (rawPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                rawPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowUriRead)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"URL includes not allowed (AllowUriRead is false): {rawPath} (line {lineNumber})",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));

                    // Emit the directive as-is so it becomes visible paragraph text.
                    if (result.Length > 0 || i > 0)
                        result.Append('\n');
                    result.Append(line);
                    continue;
                }

                // Depth guard applies to URL includes too.
                if (currentDepth >= maxDepth)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Error,
                        $"Maximum include depth ({maxDepth}) exceeded at line {lineNumber}",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));

                    if (result.Length > 0 || i > 0)
                        result.Append('\n');
                    result.Append(line);
                    continue;
                }

                // Fetch remote content
                string? urlContent = null;
                try
                {
                    urlContent = SharedHttpClient.GetStringAsync(rawPath).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Failed to fetch URL include: {rawPath} — {ex.Message} (line {lineNumber})",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));

                    if (result.Length > 0 || i > 0)
                        result.Append('\n');
                    result.Append(line);
                    continue;
                }

                // Apply tag/lines/leveloffset filtering
                if (tagValue is not null || tagsValue is not null)
                {
                    string[] urlTagNames;
                    bool urlNegate = false;

                    if (tagValue is not null)
                    {
                        var tv2 = tagValue;
                        if (tv2.StartsWith('!'))
                        {
                            urlNegate = true;
                            tv2 = tv2[1..];
                        }
                        urlTagNames = [tv2];
                    }
                    else
                    {
                        urlTagNames = tagsValue!.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    }

                    var urlLines = TextUtility.SplitLines(urlContent);
                    var (urlFiltered, urlMatched) = ExtractTaggedRegions(urlLines, urlTagNames, urlNegate);

                    if (urlMatched)
                        urlContent = string.Join("\n", urlFiltered);
                }
                else if (linesValue is not null)
                {
                    var ranges = ParseLineRanges(linesValue);
                    var urlLines = TextUtility.SplitLines(urlContent);
                    var urlFiltered = new List<string>();
                    foreach (var (start, end) in ranges)
                    {
                        for (int ln = start; ln <= end; ln++)
                        {
                            if (ln >= 1 && ln <= urlLines.Length)
                                urlFiltered.Add(urlLines[ln - 1]);
                        }
                    }
                    urlContent = string.Join("\n", urlFiltered);
                }

                if (indentValue is not null)
                    urlContent = ApplyIndent(urlContent, indentValue.Value);

                if (levelOffset is not null && levelOffset.Value != 0)
                    urlContent = ApplyLevelOffset(urlContent, levelOffset.Value);

                var urlExpanded = ExpandRecursive(
                    urlContent, baseDirectory, reader, diagnostics, visitedPaths,
                    currentDepth + 1, maxDepth, attributes, allowUriRead, safeMode);

                urlExpanded = urlExpanded.TrimEnd('\n');

                if (result.Length > 0 || i > 0)
                    result.Append('\n');
                result.Append(urlExpanded);
                continue;
            }

            // ── Expand attribute references in path ──
            if (rawPath.Contains('{') && rawPath.Contains('}'))
            {
                bool hasUndefined = false;
                rawPath = AttributeRefPattern.Replace(rawPath, m =>
                {
                    var attrName = m.Groups[1].Value;
                    if (attributes is not null && attributes.TryGetValue(attrName, out var val))
                        return val;
                    hasUndefined = true;
                    return m.Value; // leave as-is
                });

                if (hasUndefined)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Undefined attribute reference in include path: {rawPath} (line {lineNumber})",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));

                    if (result.Length > 0 || i > 0)
                        result.Append('\n');
                    result.Append(line);
                    continue;
                }
            }

            // ── Depth guard ──
            if (currentDepth >= maxDepth)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    $"Maximum include depth ({maxDepth}) exceeded at line {lineNumber}",
                    new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));

                if (result.Length > 0 || i > 0)
                    result.Append('\n');
                result.Append(line);
                continue;
            }

            // ── Resolve path ──
            var resolvedPath = Path.IsPathRooted(rawPath)
                ? Path.GetFullPath(rawPath)
                : Path.GetFullPath(Path.Combine(baseDirectory, rawPath));

            // ── Safe mode: restrict to base directory ──
            if (safeMode >= SafeMode.Safe)
            {
                if (!IsWithinBaseDirectory(resolvedPath, baseDirectory))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Include path outside base directory blocked by safe mode: {rawPath} (line {lineNumber})",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                    if (result.Length > 0 || i > 0)
                        result.Append('\n');
                    result.Append(line);
                    continue;
                }
            }

            // ── Circular include detection ──
            if (!visitedPaths.Add(resolvedPath))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    $"Circular include detected: {rawPath} (line {lineNumber})",
                    new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));

                if (result.Length > 0 || i > 0)
                    result.Append('\n');
                result.Append(line);
                continue;
            }

            // ── Missing file ──
            if (!reader.Exists(resolvedPath))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    $"Include file not found: {rawPath} (line {lineNumber})",
                    new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));

                // Remove from visited so a later (non-circular) reference to the same file
                // gets its own diagnostic rather than a misleading "circular" error.
                visitedPaths.Remove(resolvedPath);

                if (result.Length > 0 || i > 0)
                    result.Append('\n');
                result.Append(line);
                continue;
            }

            // ── Read and optionally filter by tag/tags or lines= ──
            string includeContent;
            try
            {
                includeContent = reader.Read(resolvedPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    $"Failed to read include file: {rawPath} — {ex.Message} (line {lineNumber})",
                    new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                visitedPaths.Remove(resolvedPath);
                if (result.Length > 0 || i > 0)
                    result.Append('\n');
                result.Append(line);
                continue;
            }

            if (tagValue is not null || tagsValue is not null)
            {
                // tag/tags takes precedence over lines=
                if (linesValue is not null)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Both tag/tags and lines attributes specified; lines ignored (line {lineNumber})",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                }

                // Build tag list and negate flag
                string[] tagNames;
                bool negate = false;

                if (tagValue is not null)
                {
                    var tv = tagValue;
                    if (tv.StartsWith('!'))
                    {
                        negate = true;
                        tv = tv[1..];
                    }
                    tagNames = [tv];
                }
                else
                {
                    tagNames = tagsValue!.Split(';', StringSplitOptions.RemoveEmptyEntries);
                }

                var allLines = TextUtility.SplitLines(includeContent);
                var (filtered, matched) = ExtractTaggedRegions(allLines, tagNames, negate);

                if (!matched)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Tag(s) not found in included file: {string.Join(", ", tagNames)} (line {lineNumber})",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                    // Fall back to entire file content — don't filter
                }
                else
                {
                    includeContent = string.Join("\n", filtered);
                }
            }
            else if (linesValue is not null)
            {
                var ranges = ParseLineRanges(linesValue);
                var allLines = TextUtility.SplitLines(includeContent);
                var filtered = new List<string>();
                foreach (var (start, end) in ranges)
                {
                    for (int ln = start; ln <= end; ln++)
                    {
                        if (ln >= 1 && ln <= allLines.Length)
                            filtered.Add(allLines[ln - 1]);
                    }
                }
                includeContent = string.Join("\n", filtered);
            }

            // ── Apply indent ──
            if (indentValue is not null)
            {
                includeContent = ApplyIndent(includeContent, indentValue.Value);
            }

            // ── Apply level offset ──
            if (levelOffset is not null && levelOffset.Value != 0)
            {
                includeContent = ApplyLevelOffset(includeContent, levelOffset.Value);
            }

            var includeDir = Path.GetDirectoryName(resolvedPath) ?? baseDirectory;

            var expandedContent = ExpandRecursive(
                includeContent, includeDir, reader, diagnostics, visitedPaths,
                currentDepth + 1, maxDepth, attributes, allowUriRead, safeMode);

            // Remove trailing newline from included content to avoid double-blank-lines.
            expandedContent = expandedContent.TrimEnd('\n');

            if (result.Length > 0 || i > 0)
                result.Append('\n');
            result.Append(expandedContent);

            // Allow the same file to be included again at a different call-site
            // (only *recursive* cycles are blocked, not diamond includes).
            visitedPaths.Remove(resolvedPath);
        }

        return result.ToString();
    }

    /// <summary>
    /// Returns true when <paramref name="resolvedPath"/> is the base directory itself or a
    /// path strictly beneath it. Uses a directory-separator boundary so that a sibling whose
    /// name merely shares the base as a string prefix (e.g. base <c>/wiki/docs</c> vs.
    /// <c>/wiki/docs-private/secret.adoc</c>) is correctly rejected — a naive
    /// <see cref="string.StartsWith(string, StringComparison)"/> check would let it through.
    /// </summary>
    private static bool IsWithinBaseDirectory(string resolvedPath, string baseDirectory)
    {
        var normalizedBase = Path.GetFullPath(baseDirectory);
        if (resolvedPath.Equals(normalizedBase, StringComparison.OrdinalIgnoreCase))
            return true;

        var baseWithSeparator = normalizedBase.Length > 0 && normalizedBase[^1] == Path.DirectorySeparatorChar
            ? normalizedBase
            : normalizedBase + Path.DirectorySeparatorChar;
        return resolvedPath.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lightweight conditional tracking for include expansion.
    /// Recognizes ifdef/ifndef/endif/ifeval directives to determine whether
    /// include directives should be expanded. The conditional lines themselves
    /// are always passed through (ConditionalPreprocessor handles them later).
    /// </summary>
    private static bool TryHandleConditional(string line, Stack<bool> condStack, IReadOnlyDictionary<string, string>? attributes)
    {
        // endif::[] or endif::name[]
        if (line.StartsWith("endif::", StringComparison.Ordinal) && line.TrimEnd().EndsWith("[]"))
        {
            if (condStack.Count > 0) condStack.Pop();
            return true;
        }

        // ifdef::name[] (block form only — empty brackets)
        if (line.StartsWith("ifdef::", StringComparison.Ordinal) && line.TrimEnd().EndsWith("[]"))
        {
            var attrName = ExtractConditionalAttribute(line, "ifdef::".Length);
            bool active = attributes?.ContainsKey(attrName) == true;
            condStack.Push(active);
            return true;
        }

        // ifndef::name[]
        if (line.StartsWith("ifndef::", StringComparison.Ordinal) && line.TrimEnd().EndsWith("[]"))
        {
            var attrName = ExtractConditionalAttribute(line, "ifndef::".Length);
            bool active = attributes?.ContainsKey(attrName) != true;
            condStack.Push(active);
            return true;
        }

        // ifeval::[expr] — too complex to evaluate here, assume true (safe default)
        if (line.StartsWith("ifeval::", StringComparison.Ordinal))
        {
            condStack.Push(true); // conservative: don't skip includes
            return true;
        }

        return false;
    }

    /// <summary>
    /// Extracts the attribute name from a conditional directive line,
    /// given the start offset (after the "ifdef::" or "ifndef::" prefix).
    /// </summary>
    private static string ExtractConditionalAttribute(string line, int prefixLength)
    {
        var trimmed = line.TrimEnd();
        // The attribute name is between the prefix and the trailing "[]"
        var bracketStart = trimmed.IndexOf('[', prefixLength);
        if (bracketStart < 0) return string.Empty;
        return trimmed[prefixLength..bracketStart].Trim();
    }

    /// <summary>
    /// Adjusts indentation of each line in <paramref name="text"/>.
    /// When <paramref name="indent"/> is 0, strips all leading whitespace.
    /// When positive, prepends that many spaces to each line.
    /// </summary>
    internal static string ApplyIndent(string text, int indent)
    {
        var lines = TextUtility.SplitLines(text);

        // Asciidoctor semantics: remove the block's COMMON (minimum) leading indentation across
        // non-blank lines, then re-indent every non-blank line by `indent` spaces. This preserves
        // the relative indentation of nested constructs (Python/YAML/etc.); a per-line TrimStart
        // would flatten them and corrupt the code. Tabs and spaces are counted as one column each.
        int common = int.MaxValue;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            int leading = 0;
            while (leading < line.Length && (line[leading] == ' ' || line[leading] == '\t'))
                leading++;
            if (leading < common) common = leading;
        }
        if (common == int.MaxValue) common = 0; // no non-blank lines

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue; // blank lines stay blank (no trailing indentation)

            var stripped = common <= line.Length ? line.Substring(common) : line.TrimStart();
            if (indent > 0)
                sb.Append(' ', indent);
            sb.Append(stripped);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Adjusts section heading levels in <paramref name="text"/> by <paramref name="offset"/>.
    /// Headings are lines starting with one or more <c>=</c> characters followed by a space.
    /// The heading level is clamped to [1, 6] (i.e. <c>=</c> through <c>======</c>).
    /// Non-heading lines pass through unchanged.
    /// </summary>
    internal static string ApplyLevelOffset(string text, int offset)
    {
        var lines = TextUtility.SplitLines(text);
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            // Count leading '=' characters
            int eqCount = 0;
            while (eqCount < line.Length && line[eqCount] == '=')
                eqCount++;

            // A section heading requires at least one '=' followed by a space
            if (eqCount >= 1 && eqCount < line.Length && line[eqCount] == ' ')
            {
#if !NETSTANDARD2_0
                int newCount = Math.Clamp(eqCount + offset, 1, 6);
#else
                int newCount = MathCompat.Clamp(eqCount + offset, 1, 6);
#endif
                lines[i] = new string('=', newCount) + line[eqCount..];
            }
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Parses bracket content into key=value pairs.
    /// E.g. <c>lines=2..4</c> or <c>tag=main,lines="1..2;4..5"</c>.
    /// </summary>
    private static Dictionary<string, string> ParseIncludeAttributes(string bracketContent)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Simple parsing: split by comma, then key=value
        foreach (var part in bracketContent.Split(','))
        {
            var trimmed = part.Trim();
            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = trimmed[..eqIndex].Trim();
                var value = trimmed[(eqIndex + 1)..].Trim();
                // Strip surrounding quotes if present
                if (value.Length >= 2 &&
                    ((value[0] == '"' && value[^1] == '"') ||
                     (value[0] == '\'' && value[^1] == '\'')))
                {
                    value = value[1..^1];
                }
                result[key] = value;
            }
        }
        return result;
    }

    /// <summary>
    /// Tag marker prefixes recognized in source files.
    /// </summary>
    private static readonly string[] TagPrefixes = ["// ", "# ", "<!-- ", "; ", "% "];

    /// <summary>
    /// Extracts tagged regions from source lines.
    /// </summary>
    /// <param name="lines">All lines of the included file.</param>
    /// <param name="tagNames">Tag names to match.</param>
    /// <param name="negate">If true, include everything except the tagged regions (and marker lines).</param>
    /// <returns>Filtered lines and whether any tag was matched.</returns>
    internal static (List<string> Lines, bool Matched) ExtractTaggedRegions(string[] lines, string[] tagNames, bool negate)
    {
        var tagSet = new HashSet<string>(tagNames, StringComparer.Ordinal);
        var result = new List<string>();
        var activeTags = new HashSet<string>(StringComparer.Ordinal);
        bool anyMatched = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            // Check if this is a tag marker line
            string? markerTag = null;
            bool isStartMarker = false;
            bool isEndMarker = false;

            foreach (var prefix in TagPrefixes)
            {
                if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var afterPrefix = trimmed[prefix.Length..];
                    if (afterPrefix.StartsWith("tag::", StringComparison.Ordinal))
                    {
                        var nameAndBracket = afterPrefix["tag::".Length..];
                        var bracketIdx = nameAndBracket.IndexOf('[');
                        if (bracketIdx > 0 && nameAndBracket.EndsWith("[]"))
                        {
                            markerTag = nameAndBracket[..bracketIdx];
                            isStartMarker = true;
                        }
                    }
                    else if (afterPrefix.StartsWith("end::", StringComparison.Ordinal))
                    {
                        var nameAndBracket = afterPrefix["end::".Length..];
                        var bracketIdx = nameAndBracket.IndexOf('[');
                        if (bracketIdx > 0 && nameAndBracket.EndsWith("[]"))
                        {
                            markerTag = nameAndBracket[..bracketIdx];
                            isEndMarker = true;
                        }
                    }

                    if (markerTag is not null)
                        break;
                }
            }

            // Process marker lines
            if (isStartMarker && markerTag is not null)
            {
                if (tagSet.Contains(markerTag))
                {
                    activeTags.Add(markerTag);
                    anyMatched = true;
                }
                continue; // Always skip marker lines from output
            }

            if (isEndMarker && markerTag is not null)
            {
                activeTags.Remove(markerTag);
                continue; // Always skip marker lines from output
            }

            // Collect lines based on mode
            if (negate)
            {
                // Negated: include lines NOT inside any matched tag region
                if (activeTags.Count == 0)
                    result.Add(line);
            }
            else
            {
                // Normal: include lines inside any matched tag region
                if (activeTags.Count > 0)
                    result.Add(line);
            }
        }

        return (result, anyMatched);
    }

    /// <summary>
    /// Scans input text for <c>:name: value</c> attribute definitions and merges them
    /// with API-provided attributes. API-provided attributes take precedence.
    /// </summary>
    private static Dictionary<string, string> BuildAttributeMap(
        string text,
        IReadOnlyDictionary<string, string>? apiAttributes)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Scan document for attribute definitions
        var lines = TextUtility.SplitLines(text);
        foreach (var line in lines)
        {
            var match = AttributeDefPattern.Match(line);
            if (match.Success)
            {
                map[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }

        // API-provided attributes take precedence
        if (apiAttributes is not null)
        {
            foreach (var kvp in apiAttributes)
            {
                map[kvp.Key] = kvp.Value;
            }
        }

        return map;
    }

    /// <summary>
    /// Parses a <c>lines</c> attribute value into a list of 1-based (start, end) ranges.
    /// <list type="bullet">
    /// <item><c>"3"</c> → <c>[(3,3)]</c></item>
    /// <item><c>"2..4"</c> → <c>[(2,4)]</c></item>
    /// <item><c>"1..2;4..5"</c> → <c>[(1,2),(4,5)]</c></item>
    /// </list>
    /// </summary>
    internal static List<(int Start, int End)> ParseLineRanges(string value)
    {
        var ranges = new List<(int Start, int End)>();
        // A quoted lines= value may separate ranges with ';' or ',' (AsciiDoc
        // requires the quotes precisely so a ',' isn't read as an attribute
        // separator), e.g. lines="1..2,5..6" or lines="1;3..4".
        foreach (var segment in value.Split(';', ','))
        {
            var trimmed = segment.Trim();
            if (trimmed.Length == 0) continue;

            var dotDot = trimmed.IndexOf("..", StringComparison.Ordinal);
            if (dotDot >= 0)
            {
                if (int.TryParse(trimmed[..dotDot], out var start) &&
                    int.TryParse(trimmed[(dotDot + 2)..], out var end))
                {
                    ranges.Add((start, end));
                }
            }
            else
            {
                if (int.TryParse(trimmed, out var single))
                    ranges.Add((single, single));
            }
        }
        return ranges;
    }
}
