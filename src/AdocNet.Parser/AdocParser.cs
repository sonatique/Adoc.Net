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

        // ── Include expansion ─────────────────────────────────────────────
        if (options.ShouldExpandIncludes())
        {
            var baseDir = options.ResolveBaseDirectory()!;
            var expandResult = IncludeExpander.Expand(
                sourceText, baseDir, reader: options.IncludeReader, maxDepth: options.IncludeMaxDepth, attributes: options.Attributes, allowUriRead: options.AllowUriRead);
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

        return new ParseResult(parseResult.Document, allDiagnostics);
    }
}
