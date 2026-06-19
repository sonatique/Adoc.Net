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

        // Number of leading lines removed by front-matter stripping. Top-level
        // (non-included) line origins are shifted by this so they report the line
        // in the ORIGINAL document — the buffer the author actually edits.
        int frontMatterOffset = 0;

        // ── Front matter stripping (step 0) ──────────────────────────────
        if (options.Attributes?.ContainsKey("skip-front-matter") == true)
        {
            var beforeLineCount = TextUtility.SplitLines(sourceText).Length;
            var (stripped, fm, fmDiag) = StripFrontMatter(sourceText);
            if (fm is not null) // front matter was actually present and removed
                frontMatterOffset = beforeLineCount - TextUtility.SplitLines(stripped).Length;
            sourceText = stripped;
            frontMatterContent = fm;
            if (fmDiag is not null)
                allDiagnostics.Add(fmDiag);
        }

        // ── Include expansion ─────────────────────────────────────────────
        // `lineOrigins` tracks, per line of `sourceText`, the file + line the author
        // edits (issue #46). It is built here and carried through conditional
        // preprocessing so the final list aligns with the AST's line coordinates.
        IReadOnlyList<LineOrigin> lineOrigins;
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
                attributes: options.Attributes, allowUriRead: options.AllowUriRead, safeMode: options.SafeMode,
                sourceFile: options.SourceFilePath);
            sourceText = expandResult.Text;
            allDiagnostics.AddRange(expandResult.Diagnostics);
            lineOrigins = expandResult.Origins;
        }
        else
        {
            // No expansion: every line maps to itself in the primary source.
            lineOrigins = BuildIdentityOrigins(sourceText, options.SourceFilePath);
        }

        // Shift top-level (non-synthetic) origins past any stripped front matter so
        // their SourceLine reflects the original, un-stripped document.
        if (frontMatterOffset > 0)
            lineOrigins = ApplyFrontMatterOffset(lineOrigins, frontMatterOffset);

        // ── Conditional preprocessing ─────────────────────────────────────
        // Build a complete attribute context: defaults (lowest priority) + external (API-provided).
        // These are passed to the preprocessor so ifdef/ifndef/ifeval can reference them.
        var condAttrs = BlockParser.GetDefaultAttributes();
        if (options.Attributes is not null)
        {
            foreach (var kvp in options.Attributes)
                condAttrs[kvp.Key] = kvp.Value;
        }
        var (filteredText, condDiagnostics, filteredOrigins) = ConditionalPreprocessor.Process(
            sourceText, lineOrigins, condAttrs);
        sourceText = filteredText;
        // Conditional diagnostics index into the pre-filter (post-include) text, so
        // translate them to original-source coordinates against the origins valid at
        // that point — before `lineOrigins` advances to the filtered space (issue #67).
        foreach (var cd in condDiagnostics)
            allDiagnostics.Add(TranslateToSource(cd, lineOrigins));
        lineOrigins = filteredOrigins;

        // ── Block + inline parsing ────────────────────────────────────────
        // Pass LockedAttributes so document-defined attributes whose names are locked
        // by the host cannot override them (the BlockParser honours this set).
        var parseResult = BlockParser.Parse(sourceText, options.Attributes, options.LockedAttributes);
        // BlockParser diagnostics count in the fully-expanded+filtered text. Report
        // them in original-source coordinates (matching asciidoctor) so file:line is
        // directly usable even when an include precedes the diagnostic (issue #67).
        foreach (var pd in parseResult.Diagnostics)
            allDiagnostics.Add(TranslateToSource(pd, lineOrigins));

        // ── Stamp FilePath on diagnostics when a source file is known ─────
        // Translation above already names the included file for diagnostics that
        // originate inside an include; this fills the remaining (main-document)
        // diagnostics with the primary source path so every diagnostic is locatable.
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

        // ── File mtime → :docdate: / :docyear: (Asciidoctor parity) ───────
        // When parsing from a file, override the parser's "now" defaults with the
        // file's last-write timestamp so renderers (DocBook <date>, EPUB dc:date,
        // HTML footer) emit a stable, file-derived date. Honours :reproducible:
        // (suppresses the override) and explicit :docdate:/:revdate: from the
        // document header (which already won via attribute precedence).
        if (filePath is not null)
        {
            var attrs = parseResult.Document.Attributes;
            bool reproducible = attrs.ContainsKey("reproducible");
            bool explicitDocdate = attrs.TryGetValue("docdate", out var dd) &&
                                   !string.IsNullOrWhiteSpace(dd) &&
                                   !LooksLikeParserDefault(dd);
            if (!reproducible && !explicitDocdate)
            {
                try
                {
                    var mtime = System.IO.File.GetLastWriteTime(filePath);
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    // .NET "zzz" formats UTC offset as "+01:00"; asciidoctor (Ruby
                    // strftime %z) formats it as "+0100" with no colon. Strip.
                    var tz = mtime.ToString("zzz", inv).Replace(":", "");
                    parseResult.Document.SetAttribute("docdate",
                        mtime.ToString("yyyy-MM-dd", inv));
                    parseResult.Document.SetAttribute("docyear",
                        mtime.Year.ToString(inv));
                    parseResult.Document.SetAttribute("doctime",
                        mtime.ToString("HH:mm:ss", inv) + " " + tz);
                    parseResult.Document.SetAttribute("docdatetime",
                        mtime.ToString("yyyy-MM-dd HH:mm:ss", inv) + " " + tz);
                }
                catch (System.IO.IOException) { /* file moved/deleted between read+stat */ }
                catch (UnauthorizedAccessException) { /* unreadable */ }
            }

            // Asciidoctor parity: file-path-derived intrinsic attributes.
            // docname     = basename without extension          (e.g. "chapter1")
            // docfilesuffix = the extension with leading dot     (e.g. ".adoc")
            // docfile     = absolute path                       (e.g. "/abs/path/chapter1.adoc")
            // Renderers (Man's .TH, EPUB metadata, etc.) consume these.
            var pathAttrs = parseResult.Document.Attributes;
            try
            {
                if (!pathAttrs.ContainsKey("docname"))
                    parseResult.Document.SetAttribute("docname",
                        System.IO.Path.GetFileNameWithoutExtension(filePath));
                if (!pathAttrs.ContainsKey("docfilesuffix"))
                {
                    var ext = System.IO.Path.GetExtension(filePath);
                    if (!string.IsNullOrEmpty(ext))
                        parseResult.Document.SetAttribute("docfilesuffix", ext);
                }
                if (!pathAttrs.ContainsKey("docfile"))
                    parseResult.Document.SetAttribute("docfile", filePath);
            }
            catch (ArgumentException) { /* malformed path */ }
        }

        return new ParseResult(parseResult.Document, allDiagnostics) { LineOrigins = lineOrigins };
    }

    /// <summary>
    /// Re-expresses a diagnostic's range in original-source coordinates by mapping
    /// each endpoint line through <paramref name="origins"/> (expanded → source). When
    /// the diagnostic originates inside an included file, its <see cref="Diagnostic.FilePath"/>
    /// is set to that file so <c>file:line</c> is directly usable (issue #67).
    /// Columns are preserved; an out-of-range or unknown line keeps its value.
    /// </summary>
    private static Diagnostic TranslateToSource(Diagnostic d, IReadOnlyList<LineOrigin> origins)
    {
        if (d.Range.IsNone || origins.Count == 0)
            return d;

        var (startLine, startOrigin) = MapToSource(d.Range.Start.Line, origins);
        var (endLine, _) = MapToSource(d.Range.End.Line, origins);

        var range = new SourceRange(
            new SourcePosition(startLine, d.Range.Start.Column),
            new SourcePosition(endLine, d.Range.End.Column));

        // Name the included file when the diagnostic falls inside one; main-document
        // diagnostics keep FilePath null here (the caller fills the primary path).
        string? filePath = d.FilePath;
        if (startOrigin.IsSynthetic && startOrigin.SourceFile is not null)
            filePath = startOrigin.SourceFile;

        return d with { Range = range, FilePath = filePath };
    }

    /// <summary>
    /// Maps a 1-based expanded line to its original-source line via
    /// <paramref name="origins"/>, returning the matching <see cref="LineOrigin"/> too.
    /// Falls back to the input line when out of range or the source line is unknown.
    /// </summary>
    private static (int Line, LineOrigin Origin) MapToSource(int expandedLine, IReadOnlyList<LineOrigin> origins)
    {
        if (expandedLine >= 1 && expandedLine <= origins.Count)
        {
            var o = origins[expandedLine - 1];
            return (o.SourceLine > 0 ? o.SourceLine : expandedLine, o);
        }
        return (expandedLine, LineOrigin.None);
    }

    /// <summary>
    /// Builds an identity provenance table for un-expanded source: line <c>i + 1</c>
    /// maps to line <c>i + 1</c> of the primary file (never synthetic).
    /// </summary>
    private static IReadOnlyList<LineOrigin> BuildIdentityOrigins(string text, string? sourceFile)
    {
        var lines = TextUtility.SplitLines(text);
        var origins = new LineOrigin[lines.Length];
        for (int i = 0; i < lines.Length; i++)
            origins[i] = new LineOrigin(sourceFile, i + 1, false);
        return origins;
    }

    /// <summary>
    /// Adds <paramref name="offset"/> to the <see cref="LineOrigin.SourceLine"/> of every
    /// non-synthetic (top-level) origin, leaving included-content origins untouched.
    /// </summary>
    private static IReadOnlyList<LineOrigin> ApplyFrontMatterOffset(IReadOnlyList<LineOrigin> origins, int offset)
    {
        var shifted = new LineOrigin[origins.Count];
        for (int i = 0; i < origins.Count; i++)
        {
            var o = origins[i];
            shifted[i] = o.IsSynthetic ? o : o with { SourceLine = o.SourceLine + offset };
        }
        return shifted;
    }

    /// <summary>
    /// True if the docdate string looks like a parser-default (today's date in YYYY-MM-DD).
    /// Used to detect whether the document explicitly set :docdate: or whether it was
    /// auto-populated by the parser — only the latter should be overridden by file mtime.
    /// </summary>
    private static bool LooksLikeParserDefault(string value)
    {
        var today = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return value == today;
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
