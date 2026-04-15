using AdocNet;
using AdocNet.Ast;
using AdocNet.Internal.Compatibility;

namespace AdocNet.Parser;

/// <summary>
/// The primary entry point for parsing AsciiDoc source text into an AST.
/// <para>
/// This is the recommended API for library consumers. It combines include expansion
/// and block/inline parsing into a single call and returns a <see cref="ParseResult"/>
/// containing the document AST and any diagnostics.
/// </para>
/// <example>
/// <code>
/// var result = AdocParser.Parse(sourceText, new ParseOptions
/// {
///     SourceFilePath = "chapter.adoc"
/// });
///
/// if (result.Diagnostics.Any(d =&gt; d.IsError))
///     Console.Error.WriteLine("Parse errors found.");
///
/// var html = new HtmlRenderer().RenderToString(result.Document);
/// </code>
/// </example>
/// </summary>
public static class AdocParser
{
    /// <summary>
    /// Parses AsciiDoc source text with default options (no include expansion).
    /// </summary>
    /// <param name="text">The AsciiDoc source text to parse.</param>
    /// <returns>A <see cref="ParseResult"/> containing the document AST and diagnostics.</returns>
    public static ParseResult Parse(string text)
        => Parse(text, ParseOptions.Default);

    /// <summary>
    /// Parses AsciiDoc source text with the specified options.
    /// </summary>
    /// <param name="text">The AsciiDoc source text to parse.</param>
    /// <param name="options">Options controlling parsing behavior.</param>
    /// <returns>A <see cref="ParseResult"/> containing the document AST and diagnostics.</returns>
    public static ParseResult Parse(string text, ParseOptions options)
    {
        Guard.NotNull(text);
        Guard.NotNull(options);

        var allDiagnostics = new List<Diagnostic>();
        var sourceText = text;
        string? frontMatterContent = null;

        // ── Front matter stripping (step 0) ──────────────────────────────
        if (options.Attributes?.ContainsKey("skip-front-matter") == true)
        {
            var (stripped, fm, fmDiag) = StripFrontMatter(sourceText);
            sourceText = stripped;
            frontMatterContent = fm;
            if (fmDiag is not null)
                allDiagnostics.Add(fmDiag);
        }

        // ── Include expansion ─────────────────────────────────────────────
        if (options.ShouldExpandIncludes())
        {
            var baseDir = options.ResolveBaseDirectory()!;

            // Cap max-include-depth: document attribute can only lower, never raise
            var effectiveMaxDepth = options.IncludeMaxDepth;
            if (options.Attributes?.TryGetValue("max-include-depth", out var midStr) == true
                && int.TryParse(midStr, out var midVal) && midVal >= 0)
            {
                effectiveMaxDepth = Math.Min(effectiveMaxDepth, midVal);
            }

            var expandResult = IncludeExpander.Expand(
                sourceText, baseDir, reader: options.IncludeReader, maxDepth: effectiveMaxDepth,
                attributes: options.Attributes, allowUriRead: options.AllowUriRead, safeMode: options.SafeMode);
            sourceText = expandResult.Text;
            allDiagnostics.AddRange(expandResult.Diagnostics);
        }

        // ── Conditional preprocessing ─────────────────────────────────────
        // Build a complete attribute context: defaults (lowest priority) + external (API-provided).
        // These are passed to the preprocessor so ifdef/ifndef/ifeval can reference them.
        var condAttrs = BlockParser.GetDefaultAttributes();
        if (options.Attributes is not null)
        {
            foreach (var kvp in options.Attributes)
                condAttrs[kvp.Key] = kvp.Value;
        }
        var (filteredText, condDiagnostics) = ConditionalPreprocessor.Process(
            sourceText, condAttrs);
        sourceText = filteredText;
        allDiagnostics.AddRange(condDiagnostics);

        // ── Block + inline parsing ────────────────────────────────────────
        var parseResult = BlockParser.Parse(sourceText, options.Attributes);
        allDiagnostics.AddRange(parseResult.Diagnostics);

        // ── Stamp FilePath on diagnostics when a source file is known ─────
        var filePath = options.SourceFilePath;
        if (filePath is not null)
        {
            for (int i = 0; i < allDiagnostics.Count; i++)
            {
                if (allDiagnostics[i].FilePath is null)
                    allDiagnostics[i] = allDiagnostics[i] with { FilePath = filePath };
            }
        }

        // ── Inject front-matter attribute if content was extracted ─────────
        if (frontMatterContent is not null)
            parseResult.Document.SetAttribute("front-matter", frontMatterContent);

        return new ParseResult(parseResult.Document, allDiagnostics);
    }

    /// <summary>
    /// Strips YAML front matter from the beginning of the source text.
    /// Front matter is recognized as <c>---</c> on the first line, followed by content,
    /// followed by a closing <c>---</c> line. Returns the text with front matter removed
    /// and the extracted content (without the <c>---</c> fences).
    /// </summary>
    private static (string Text, string? FrontMatter, Diagnostic? Diagnostic) StripFrontMatter(string text)
    {
        // Front matter must start on line 1 with exactly "---"
        if (!text.StartsWith("---", StringComparison.Ordinal))
            return (text, null, null);

        // Check that the first line is exactly "---" (possibly followed by \n or \r\n)
        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0)
            return (text, null, null); // Single line "---" with no content after

        var firstLine = text[..firstNewline].TrimEnd('\r');
        if (firstLine != "---")
            return (text, null, null);

        // Search for the closing "---" line
        var searchStart = firstNewline + 1;
        while (searchStart < text.Length)
        {
            var lineEnd = text.IndexOf('\n', searchStart);
            string line;
            if (lineEnd < 0)
            {
                line = text[searchStart..].TrimEnd('\r');
                if (line == "---")
                {
                    var content = text[(firstNewline + 1)..searchStart];
                    if (content.EndsWith("\r\n", StringComparison.Ordinal))
                        content = content[..^2];
                    else if (content.EndsWith('\n'))
                        content = content[..^1];
                    else if (content.EndsWith('\r'))
                        content = content[..^1];
                    return ("", content, null);
                }
                // Reached end without finding closing ---
                return (text, null, new Diagnostic(
                    DiagnosticSeverity.Warning,
                    "Unclosed front matter: opening --- found but no closing --- delimiter",
                    new SourceRange(new SourcePosition(1, 1), new SourcePosition(1, 3))));
            }

            line = text[searchStart..lineEnd].TrimEnd('\r');
            if (line == "---")
            {
                var content = text[(firstNewline + 1)..searchStart];
                if (content.EndsWith("\r\n", StringComparison.Ordinal))
                    content = content[..^2];
                else if (content.EndsWith('\n'))
                    content = content[..^1];
                else if (content.EndsWith('\r'))
                    content = content[..^1];
                var remaining = lineEnd + 1 < text.Length ? text[(lineEnd + 1)..] : "";
                return (remaining, content, null);
            }

            searchStart = lineEnd + 1;
        }

        // Reached end without closing ---
        return (text, null, new Diagnostic(
            DiagnosticSeverity.Warning,
            "Unclosed front matter: opening --- found but no closing --- delimiter",
            new SourceRange(new SourcePosition(1, 1), new SourcePosition(1, 3))));
    }
}
