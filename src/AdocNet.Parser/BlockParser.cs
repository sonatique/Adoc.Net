using AdocNet;
using AdocNet.Ast;
using AdocNet.Internal.Compatibility;

namespace AdocNet.Parser;

/// <summary>
/// A minimal block-level parser for AsciiDoc source text.
/// Supports: document title, document attributes, section titles, paragraphs,
/// unordered lists (* markers), ordered lists (. markers), basic nesting,
/// delimited blocks (literal ...., listing/source ----, example ====,
/// quote ____, sidebar ****), block titles (.Title), [source]/[source,lang]
/// attribute lines, basic pipe tables (|=== delimiters, optional [options="header"]),
/// block macros (image::target[alt]), description lists (term:: description),
/// and admonitions (NOTE:, TIP:, IMPORTANT:, WARNING:, CAUTION:).
/// Unsupported constructs are passed through as paragraph text without crashing.
/// </summary>
internal static class BlockParser
{
    private enum State { Header, Body }

    public static ParseResult Parse(string text)
        => Parse(text, externalAttributes: null);

    public static ParseResult Parse(string text, ParseOptions options)
    {
        Guard.NotNull(options);
        return Parse(text, options.Attributes, options.LockedAttributes);
    }

    /// <summary>
    /// Parses AsciiDoc source text with optional external attributes that are
    /// pre-populated before header parsing. Document-defined attributes override these.
    /// </summary>
    public static ParseResult Parse(string text, IReadOnlyDictionary<string, string>? externalAttributes)
        => Parse(text, externalAttributes, lockedAttributes: null);

    public static ParseResult Parse(string text, IReadOnlyDictionary<string, string>? externalAttributes, IReadOnlySet<string>? lockedAttributes)
    {
        Guard.NotNull(text);

        var lines = TextUtility.SplitLines(text);
        var document = new DocumentNode();
        var diagnostics = new List<Diagnostic>();

        // Populate intrinsic default attributes first (lowest priority).
        PopulateDefaultAttributes(document);

        // Pre-populate external attributes (API consumers can set e.g. backend=html5).
        // Document header/body attributes will override these.
        if (externalAttributes is not null)
        {
            foreach (var kvp in externalAttributes)
                document.SetAttribute(kvp.Key, kvp.Value);
        }

        // Counter state: tracks auto-incrementing counters declared via :counter:name:
        var counters = new Dictionary<string, int>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        var state = State.Header;
        AstNode currentContainer = document;
        // Section nesting: tracks open sections by level so subsections become
        // children of their parent section (Asciidoctor structure).
        var sectionStack = new List<SectionNode>();

        var paragraphLines = new List<string>();
        int paragraphStartLine = 0;

        // Each frame tracks one open ListNode and the last item added to it.
        // Frames are ordered from shallowest (index 0) to deepest (index Count-1).
        var listFrames = new List<(ListNode List, int Depth, ListItemNode? LastItem)>();
        var dlFrames = new List<(DescriptionListNode List, int Depth, DescriptionItemNode? LastItem)>();

        // Pending block metadata: consumed when the next delimited block opens.
        string? pendingBlockTitle = null;
        string? pendingSourceLang = null;
        string? pendingHighlight = null;
        bool hasPendingSource = false;
        bool hasPendingOptionsHeader = false;
        bool hasPendingAutoWidth = false;
        bool hasPendingFooter = false;
        string? pendingColSpec = null;
        string? pendingStripes = null;
        string? pendingGrid = null;
        string? pendingFrame = null;
        string? pendingFormat = null;
        string? pendingAdmonitionType = null;
        string? pendingBlockId = null;
        string? pendingBlockReftext = null;
        List<string>? pendingBlockRoles = null;
        string? pendingQuoteAttribution = null;
        string? pendingQuoteCitation = null;
        bool hasPendingBibliography = false;
        bool inBibliographySection = false;
        SubstitutionKind? pendingSubs = null;
        bool pendingSubsIsIncremental = false;
        SubstitutionKind pendingSubsToAdd = SubstitutionKind.None;
        SubstitutionKind pendingSubsToRemove = SubstitutionKind.None;
        bool pendingDiscrete = false;
        bool pendingHardbreaks = false;
        bool pendingAbstract = false;
        bool hasPendingVerse = false;
        bool hasPendingQuote = false;
        bool hasPendingListing = false;
        bool hasPendingLiteral = false;
        bool hasPendingExample = false;
        bool hasPendingSidebar = false;
        int? pendingListStart = null;
        string? pendingListStyle = null;
        List<string>? pendingBlockOptions = null;
        bool pendingCollapsible = false;
        string? pendingStem = null;
        string? pendingDlStyle = null;
        string? pendingSectionStyle = null;

        // Computes the effective "normal" substitutions: smart punctuation (PostReplacements)
        // is enabled by default and can be disabled via :!smartquotes:.
        SubstitutionKind EffectiveNormal() => document.Attributes.ContainsKey("smartquotes")
            ? SubstitutionKind.Normal
            : SubstitutionKind.Normal & ~SubstitutionKind.PostReplacements;

        // Resolves pending substitution overrides, handling both absolute and incremental modes.
        SubstitutionKind? ResolvePendingSubs(SubstitutionKind blockDefault)
        {
            if (pendingSubsIsIncremental)
                return (blockDefault | pendingSubsToAdd) & ~pendingSubsToRemove;
            return pendingSubs;
        }

        // Header author/revision line tracking.
        bool headerAuthorParsed = false;
        bool headerRevisionParsed = false;

        // Local helper: applies pendingBlockId to a block node with duplicate detection.
        void ApplyPendingId(BlockNode node, int lineNumber, int lineLength)
        {
            if (pendingBlockId is null) return;
            if (!seenIds.Add(pendingBlockId))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"Duplicate anchor ID '{pendingBlockId}'",
                    new SourceRange(new(lineNumber, 1), new(lineNumber, lineLength))));
            }
            node.Id = pendingBlockId;
            node.Reftext = pendingBlockReftext;
            pendingBlockId = null;
            pendingBlockReftext = null;
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            int lineNumber = i + 1;

            // ── Header state ───────────────────────────────────────────────
            if (state == State.Header)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    // Blank line ends the header only after a title has been seen.
                    if (document.Title is not null)
                        state = State.Body;
                    continue;
                }

                // Block comment in header: skip the entire block comment and stay in header.
                if (IsDelimiterLine(line, '/'))
                {
                    for (int j = i + 1; j < lines.Length; j++)
                    {
                        if (IsDelimiterLine(lines[j], '/'))
                        {
                            i = j;
                            break;
                        }
                    }
                    continue;
                }

                // Single-line comment in header: skip and stay in header.
                if (line.Length >= 2 && line[0] == '/' && line[1] == '/' && (line.Length == 2 || line[2] != '/'))
                    continue;

                // Block anchor in header: [[id]] — store as pending ID for the title.
                // Only handle [[id]] form here; [#id] and [#id.role] are handled in body state.
                if (line.Length >= 4 && line[0] == '[' && line[1] == '[' && line[^1] == ']' && line[^2] == ']')
                {
                    pendingBlockId = line[2..^2];
                    continue;
                }

                // Document title: exactly one '=' followed by a space.
                if (document.Title is null && IsDocTitle(line))
                {
                    document.Title = line[2..].Trim();
                    // Pending [[anchor]] before the title becomes the document id
                    // (asciidoctor writes it as the body id attribute and stores it
                    // as the :id: document attribute).
                    if (pendingBlockId is not null && !document.Attributes.ContainsKey("id"))
                        document.SetAttribute("id", pendingBlockId);
                    pendingBlockId = null;
                    pendingBlockReftext = null;
                    continue;
                }

                // Author line: first non-attribute line after the title.
                // Skip author parsing if the line looks like a revision line
                // (starts with v/V followed by a digit, or starts with a digit — i.e. a date).
                if (document.Title is not null && !headerAuthorParsed && line[0] != ':')
                {
                    headerAuthorParsed = true;
                    if (LooksLikeRevisionLine(line))
                    {
                        // No author — parse as revision directly.
                        headerRevisionParsed = true;
                        ParseRevisionLine(line, document);
                    }
                    else
                    {
                        ParseAuthorLine(line, document);
                    }
                    continue;
                }

                // Revision line: second non-attribute line after the title (immediately after author).
                if (document.Title is not null && headerAuthorParsed && !headerRevisionParsed && line[0] != ':')
                {
                    headerRevisionParsed = true;
                    ParseRevisionLine(line, document);
                    continue;
                }

                // Attribute entry: :name: value, :!name:, :name!:, :counter:name:
                if (line[0] == ':')
                {
                    // Once we see an attribute entry, author/revision lines are no longer possible.
                    headerAuthorParsed = true;
                    headerRevisionParsed = true;
                    if (TryParseAttributeUnset(line, out var unsetName))
                    {
                        if (lockedAttributes?.Contains(unsetName!) == true)
                            continue;
                        document.RemoveAttribute(unsetName!);
                        continue;
                    }
                    if (TryParseAttribute(line, lineNumber, diagnostics, out var name, out var value, allowFlagStyle: true))
                    {
                        if (lockedAttributes?.Contains(name!) == true)
                            continue;
                        value = ApplyLineContinuation(value!, lines, ref i);
                        lineNumber = i + 1;
                        value = ExpandAttributeValue(value, document.Attributes);
                        document.SetAttribute(name!, value);
                        continue;
                    }
                    // Malformed attribute: diagnostic already added; fall through to body.
                }

                // Anything else ends the header; fall through to body processing.
                state = State.Body;
            }

            // ── Body state ─────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes, pendingBlockId, pendingBlockRoles, seenIds, diagnostics, pendingHardbreaks, pendingAbstract ? "abstract" : null, subsOverride: ResolvePendingSubs(EffectiveNormal()));

                // Asciidoctor keeps a list open across blank lines as long as the
                // next non-blank line is a list item — same-kind items continue the
                // list, different-kind items nest inside the last item. Only a
                // non-list block (paragraph, section, etc.) terminates the list.
                bool preserveListContext = false;
                if (listFrames.Count > 0)
                {
                    for (int peek = i + 1; peek < lines.Length; peek++)
                    {
                        if (string.IsNullOrWhiteSpace(lines[peek]))
                            continue; // skip additional blank lines
                        if (TryParseListItem(lines[peek], out _, out _, out _))
                            preserveListContext = true;
                        break; // only check the first non-blank line
                    }
                }
                if (!preserveListContext)
                    listFrames.Clear();
                dlFrames.Clear();
                pendingBlockTitle = null;
                pendingSourceLang = null;
                pendingHighlight = null;
                hasPendingSource = false;
                hasPendingVerse = false;
                hasPendingQuote = false;
                hasPendingListing = false;
                hasPendingLiteral = false;
                hasPendingExample = false;
                hasPendingSidebar = false;
                pendingCollapsible = false;
                pendingStem = null;
                pendingDlStyle = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;

                hasPendingOptionsHeader = false;
                hasPendingAutoWidth = false;
                hasPendingFooter = false;
                pendingColSpec = null;
                pendingStripes = null;
                pendingGrid = null;
                pendingFrame = null;
                pendingFormat = null;
                pendingAdmonitionType = null;
                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;
                pendingDiscrete = false;
                pendingHardbreaks = false;
                pendingAbstract = false;
                pendingStem = null;
                continue;
            }

            // Body attribute entry: :name: value, :!name:, :name!:, :counter:name:
            // Only recognized at a block boundary (no pending paragraph lines).
            if (paragraphLines.Count == 0 && line.Length > 0 && line[0] == ':')
            {
                if (TryParseAttributeUnset(line, out var unsetName))
                {
                    if (lockedAttributes?.Contains(unsetName!) == true)
                        continue;
                    FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                    listFrames.Clear();
                dlFrames.Clear();
                    document.RemoveAttribute(unsetName!);
                    continue;
                }
                // Use a scratch diagnostics list so malformed body attributes don't emit warnings —
                // they silently fall through to paragraph text.
                var bodyDiag = new List<Diagnostic>();
                if (TryParseAttribute(line, lineNumber, bodyDiag, out var attrName, out var attrValue))
                {
                    if (lockedAttributes?.Contains(attrName!) == true)
                        continue;
                    FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                    listFrames.Clear();
                dlFrames.Clear();
                    attrValue = ApplyLineContinuation(attrValue!, lines, ref i);
                    lineNumber = i + 1;
                    attrValue = ExpandAttributeValue(attrValue, document.Attributes);
                    document.SetAttribute(attrName!, attrValue);
                    continue;
                }
                // Malformed: fall through to other body parsing (no diagnostic added).
            }

            // Section title: two or more '=' followed by a space, OR
            // Markdown-compatible: one or more '#' followed by a space (# = level 0, ## = level 1, etc.).
            var equalsCount = CountLeadingEquals(line);
            var hashCount = CountLeadingHashes(line);
            int sectionPrefixLen = 0;
            int sectionLevel = -1;
            if (equalsCount >= 2 && line.Length > equalsCount && line[equalsCount] == ' ')
            {
                sectionPrefixLen = equalsCount;
                sectionLevel = equalsCount - 1;
            }
            else if (hashCount >= 2 && hashCount <= 6 && line.Length > hashCount && line[hashCount] == ' ')
            {
                sectionPrefixLen = hashCount;
                sectionLevel = hashCount - 1;
            }
            if (sectionLevel >= 1)
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                pendingBlockTitle = null;
                pendingSourceLang = null;
                pendingHighlight = null;
                hasPendingSource = false;
                hasPendingVerse = false;
                hasPendingQuote = false;
                hasPendingListing = false;
                hasPendingLiteral = false;
                hasPendingExample = false;
                hasPendingSidebar = false;
                pendingCollapsible = false;
                pendingStem = null;
                pendingDlStyle = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;

                hasPendingOptionsHeader = false;
                hasPendingAutoWidth = false;
                hasPendingFooter = false;
                pendingColSpec = null;
                pendingStripes = null;
                pendingGrid = null;
                pendingFrame = null;
                pendingFormat = null;
                pendingAdmonitionType = null;

                var titleText = line[(sectionPrefixLen + 1)..].Trim();
                // Strip trailing '#' for Markdown-style headings (e.g. "## Title ##" → "Title")
                if (hashCount >= 2 && titleText.Length > 0 && titleText[^1] == '#')
                    titleText = titleText.TrimEnd('#').TrimEnd();

                if (pendingDiscrete)
                {
                    // Discrete heading: auto-generate ID from title if none explicitly provided.
                    string? discreteId = pendingBlockId ?? GenerateSectionId(titleText, document.Attributes);
                    if (discreteId is not null && !seenIds.Add(discreteId))
                    {
                        diagnostics.Add(new Diagnostic(
                            DiagnosticSeverity.Warning,
                            $"Duplicate anchor ID '{discreteId}'",
                            new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                    }
                    var discreteSection = new SectionNode
                    {
                        Level           = sectionLevel,
                        Title           = titleText,
                        TitleInlines    = InlineParser.Parse(titleText, EffectiveNormal(), document.Attributes),
                        Source          = new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length)),
                        Id              = discreteId,
                        Reftext         = pendingBlockReftext,
                        IsDiscrete      = true,
                        SectnumsEnabled = document.Attributes.ContainsKey("sectnums"),
                    };
                    if (pendingBlockRoles is not null)
                        discreteSection.Roles = pendingBlockRoles;
                    // Add as sibling to current container, NOT as a new section parent.
                    if (currentContainer is DocumentNode)
                        document.AddChild(discreteSection);
                    else
                        currentContainer.AddChild(discreteSection);
                    // Do NOT change currentContainer — discrete heading doesn't nest.
                    pendingDiscrete = false;
                    pendingBlockId = null;
                    pendingBlockReftext = null;
                    pendingBlockRoles = null;
                    pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;
                    continue;
                }

                // Expand attribute references before generating the section ID,
                // so {tool} → "Git" produces _getting_started_with_git, not _getting_started_with_tool.
                var expandedTitle = ExpandAttributeValue(titleText, document.Attributes);
                var sectionId = pendingBlockId ?? GenerateSectionId(expandedTitle, document.Attributes);
                if (pendingBlockId is not null && !seenIds.Add(pendingBlockId))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Duplicate anchor ID '{pendingBlockId}'",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                }
                else if (pendingBlockId is null)
                {
                    seenIds.Add(sectionId);
                }
                var section = new SectionNode
                {
                    Level           = sectionLevel,
                    Title           = titleText,
                    TitleInlines    = InlineParser.Parse(titleText, EffectiveNormal(), document.Attributes),
                    Source          = new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length)),
                    Id              = sectionId,
                    Reftext         = pendingBlockReftext,
                    SectnumsEnabled = document.Attributes.ContainsKey("sectnums"),
                    Style           = pendingSectionStyle,
                };
                if (pendingBlockRoles is not null)
                    section.Roles = pendingBlockRoles;
                // Nest sections: pop sections at same or deeper level from the stack,
                // then add this section as a child of the parent section (or document).
                while (sectionStack.Count > 0 && sectionStack[^1].Level >= sectionLevel)
                    sectionStack.RemoveAt(sectionStack.Count - 1);
                if (sectionStack.Count > 0)
                    sectionStack[^1].AddChild(section);
                else
                    document.AddChild(section);
                sectionStack.Add(section);
                currentContainer = section;
                inBibliographySection = hasPendingBibliography;
                hasPendingBibliography = false;
                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                pendingSectionStyle = null;
                pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;
                continue;
            }

            // Markdown blockquote: lines starting with "> " or bare ">".
            if (paragraphLines.Count == 0 && line.Length >= 1 && line[0] == '>'
                && (line.Length == 1 || line[1] == ' '))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();

                // Accumulate blockquote lines.
                var quoteLines = new List<string>();
                string firstContent = line.Length > 2 ? line[2..] : (line.Length == 2 ? "" : "");
                quoteLines.Add(firstContent);
                int quoteStartLine = lineNumber;

                while (i + 1 < lines.Length)
                {
                    var nextLine = lines[i + 1];
                    if (nextLine.Length >= 2 && nextLine[0] == '>' && nextLine[1] == ' ')
                    {
                        quoteLines.Add(nextLine[2..]);
                        i++;
                    }
                    else if (nextLine == ">")
                    {
                        quoteLines.Add("");
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }

                // Detect attribution: last non-empty line matching "-- Author"
                string? quoteAttribution = null;
                int lastNonEmpty = quoteLines.Count - 1;
                while (lastNonEmpty >= 0 && string.IsNullOrWhiteSpace(quoteLines[lastNonEmpty]))
                    lastNonEmpty--;
                if (lastNonEmpty >= 0 && quoteLines[lastNonEmpty].StartsWith("-- "))
                {
                    quoteAttribution = quoteLines[lastNonEmpty][3..].Trim();
                    quoteLines.RemoveAt(lastNonEmpty);
                    // Trim trailing blank lines after removing attribution
                    while (quoteLines.Count > 0 && string.IsNullOrWhiteSpace(quoteLines[^1]))
                        quoteLines.RemoveAt(quoteLines.Count - 1);
                }

                var quoteContent = string.Join("\n", quoteLines);
                var innerResult = BlockParser.Parse(quoteContent, document.Attributes);
                var quoteBlock = new DelimitedBlockNode
                {
                    BlockKind = DelimitedBlockKind.Quote,
                    Attribution = quoteAttribution ?? pendingQuoteAttribution,
                    CitationSource = pendingQuoteCitation,
                    Title = pendingBlockTitle,
                    Source = new SourceRange(new(quoteStartLine, 1), new(i + 1, lines[i].Length)),
                };
                ApplyPendingId(quoteBlock, quoteStartLine, line.Length);
                if (pendingBlockRoles is not null)
                    quoteBlock.Roles = pendingBlockRoles;
                foreach (var child in innerResult.Document.Children)
                    quoteBlock.AddChild(child);

                currentContainer.AddChild(quoteBlock);
                pendingBlockTitle = null;
                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;
                hasPendingQuote = false;
                continue;
            }

            // Block title: .Title (dot followed by a non-space, non-dot character).
            // Only recognised at a block boundary (no pending paragraph lines), so that a
            // .foo-like token mid-paragraph is treated as ordinary paragraph text.
            if (paragraphLines.Count == 0 && TryParseBlockTitle(line, out var blockTitleText))
            {
                // No FlushParagraph needed: paragraphLines.Count == 0 is already checked above.
                listFrames.Clear();
                dlFrames.Clear();
                pendingBlockTitle = blockTitleText;
                // hasPendingSource is intentionally preserved: [source] may come before or after .Title.
                continue;
            }

            // Pre-parse with unified BlockAttributes for subs/stripes/discrete extraction.
            // This runs before the existing TryParse* chain so that [subs="..."],
            // [stripes="..."], and [discrete] are captured regardless of which specific
            // attribute pattern follows.
            if (paragraphLines.Count == 0 && line.Length > 1 && line[0] == '[' && line[^1] == ']')
            {
                var blockAttrs = BlockAttributes.Parse(line);
                if (blockAttrs is not null && blockAttrs.SubsIsIncremental)
                {
                    pendingSubsIsIncremental = true;
                    pendingSubsToAdd = blockAttrs.SubsToAdd;
                    pendingSubsToRemove = blockAttrs.SubsToRemove;
                }
                else if (blockAttrs is not null && blockAttrs.Subs.HasValue)
                {
                    pendingSubs = blockAttrs.Subs;
                }
                // Extract table-related named attributes: stripes, grid, frame, format.
                // These are all consumed together so that a combined line like
                // [grid=rows,frame=none,stripes=even] is handled as a single attribute line.
                {
                    bool anyTableAttr = false;
                    if (blockAttrs is not null && blockAttrs.Named.TryGetValue("stripes", out var stripesVal))
                    {
                        pendingStripes = stripesVal;
                        anyTableAttr = true;
                    }
                    if (blockAttrs is not null && blockAttrs.Named.TryGetValue("grid", out var gridVal))
                    {
                        pendingGrid = gridVal;
                        anyTableAttr = true;
                    }
                    if (blockAttrs is not null && blockAttrs.Named.TryGetValue("frame", out var frameVal))
                    {
                        pendingFrame = frameVal;
                        anyTableAttr = true;
                    }
                    if (blockAttrs is not null && blockAttrs.Named.TryGetValue("format", out var formatVal))
                    {
                        pendingFormat = formatVal;
                        anyTableAttr = true;
                    }
                    if (anyTableAttr)
                    {
                        FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                        listFrames.Clear();
                        dlFrames.Clear();
                        // If the line only contains table attributes (no style or positional values
                        // that need to fall through), consume it now.
                        if (blockAttrs!.Style is null && blockAttrs.Positional.Count == 0)
                            continue;
                    }
                }
                // [discrete] or [discrete#id] or [discrete.role] — mark next heading as discrete.
                if (blockAttrs is not null && string.Equals(blockAttrs.Style, "discrete", StringComparison.OrdinalIgnoreCase))
                {
                    FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                    listFrames.Clear();
                    dlFrames.Clear();
                    pendingDiscrete = true;
                    if (blockAttrs.Id is not null)
                        pendingBlockId = blockAttrs.Id;
                    if (blockAttrs.Roles.Count > 0)
                        pendingBlockRoles = blockAttrs.Roles;
                    continue;
                }
                // [appendix], [glossary], [colophon], [dedication], [preface] — section styles.
                if (blockAttrs is not null && blockAttrs.Style is not null
                    && IsSectionStyleName(blockAttrs.Style))
                {
                    FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                    listFrames.Clear();
                    dlFrames.Clear();
                    pendingSectionStyle = blockAttrs.Style.ToLowerInvariant();
                    if (blockAttrs.Id is not null)
                        pendingBlockId = blockAttrs.Id;
                    if (blockAttrs.Roles.Count > 0)
                        pendingBlockRoles = blockAttrs.Roles;
                    continue;
                }
                // [%hardbreaks] — mark next paragraph for hard line breaks.
                if (blockAttrs is not null && blockAttrs.Options.Contains("hardbreaks"))
                {
                    FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                    listFrames.Clear();
                    dlFrames.Clear();
                    pendingHardbreaks = true;
                    if (blockAttrs.Id is not null)
                        pendingBlockId = blockAttrs.Id;
                    if (blockAttrs.Roles.Count > 0)
                        pendingBlockRoles = blockAttrs.Roles;
                    continue;
                }
                // [%option] lines — store options for the next block macro (video, audio, etc.).
                // Only capture and consume options that aren't handled by downstream checks
                // (hardbreaks, header, footer, autowidth are handled elsewhere).
                if (blockAttrs is not null && blockAttrs.Options.Count > 0)
                {
                    var downstreamOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                        { "hardbreaks", "header", "footer", "autowidth", "collapsible" };
                    if (blockAttrs.Options.Any(o => o.Equals("collapsible", StringComparison.OrdinalIgnoreCase)))
                        pendingCollapsible = true;
                    var mediaOptions = blockAttrs.Options.Where(o => !downstreamOptions.Contains(o)).ToList();
                    if (mediaOptions.Count > 0)
                    {
                        FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                        listFrames.Clear();
                        dlFrames.Clear();
                        pendingBlockOptions = mediaOptions;
                        if (blockAttrs.Id is not null)
                            pendingBlockId = blockAttrs.Id;
                        if (blockAttrs.Roles.Count > 0)
                            pendingBlockRoles = blockAttrs.Roles;
                        // If the line has only non-downstream options (no style or other patterns), consume it.
                        if (blockAttrs.Style is null && blockAttrs.Positional.Count == 0 && blockAttrs.Named.Count == 0
                            && blockAttrs.Options.All(o => !downstreamOptions.Contains(o)))
                            continue;
                    }
                    else if (blockAttrs.Style is null && blockAttrs.Positional.Count == 0
                        && blockAttrs.Named.All(kv => kv.Key is "options" or "opts"))
                    {
                        // All options are downstream (e.g. [options="header,footer"]).
                        // Set the pending flags and consume the line.
                        FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                        listFrames.Clear();
                        dlFrames.Clear();
                        foreach (var opt in blockAttrs.Options)
                        {
                            if (opt.Equals("header", StringComparison.OrdinalIgnoreCase))
                                hasPendingOptionsHeader = true;
                            else if (opt.Equals("footer", StringComparison.OrdinalIgnoreCase))
                                hasPendingFooter = true;
                            else if (opt.Equals("autowidth", StringComparison.OrdinalIgnoreCase))
                                hasPendingAutoWidth = true;
                            else if (opt.Equals("collapsible", StringComparison.OrdinalIgnoreCase))
                                pendingCollapsible = true;
                        }
                        continue;
                    }
                }

                // [start=N] and/or [loweralpha] etc. — ordered list attributes.
                if (blockAttrs is not null)
                {
                    if (blockAttrs.Named.TryGetValue("start", out var startVal) && int.TryParse(startVal, out var startNum))
                        pendingListStart = startNum;

                    var listStyleNames = new[] { "arabic", "loweralpha", "upperalpha", "lowerroman", "upperroman" };
                    if (blockAttrs.Style is not null && Array.Exists(listStyleNames, s => string.Equals(s, blockAttrs.Style, StringComparison.OrdinalIgnoreCase)))
                        pendingListStyle = blockAttrs.Style.ToLowerInvariant();
                }
                // [abstract] — mark next paragraph/open-block for abstract rendering.
                if (blockAttrs is not null && string.Equals(blockAttrs.Style, "abstract", StringComparison.OrdinalIgnoreCase))
                {
                    FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                    listFrames.Clear();
                    dlFrames.Clear();
                    pendingAbstract = true;
                    if (blockAttrs.Id is not null)
                        pendingBlockId = blockAttrs.Id;
                    if (blockAttrs.Roles.Count > 0)
                        pendingBlockRoles = blockAttrs.Roles;
                    continue;
                }
                // [stem], [latexmath], [asciimath] — mark next open block as a stem block.
                if (blockAttrs is not null && blockAttrs.Style is not null
                    && blockAttrs.Style is "stem" or "latexmath" or "asciimath")
                {
                    FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                    listFrames.Clear();
                    dlFrames.Clear();
                    if (string.Equals(blockAttrs.Style, "stem", StringComparison.OrdinalIgnoreCase))
                    {
                        // [stem] uses the document-level :stem: attribute value, default "latexmath"
                        pendingStem = document.Attributes.TryGetValue("stem", out var stemAttr) && stemAttr.Length > 0
                            ? stemAttr : "latexmath";
                    }
                    else
                    {
                        pendingStem = blockAttrs.Style.ToLowerInvariant();
                    }
                    if (blockAttrs.Id is not null)
                        pendingBlockId = blockAttrs.Id;
                    if (blockAttrs.Roles.Count > 0)
                        pendingBlockRoles = blockAttrs.Roles;
                    continue;
                }
                // Generic role/id-only attribute line (e.g. [.lead], [#myid], [.role1.role2], [#id,reftext="..."]).
                // Capture roles, ID, and reftext for the next block when no specific style was matched above.
                if (blockAttrs is not null && blockAttrs.Style is null
                    && (blockAttrs.Roles.Count > 0 || blockAttrs.Id is not null)
                    && blockAttrs.Options.Count == 0
                    && blockAttrs.Positional.Count == 0
                    && (blockAttrs.Named.Count == 0 || (blockAttrs.Named.Count == 1 && blockAttrs.Named.ContainsKey("reftext")))
                    && !blockAttrs.Subs.HasValue
                    && !blockAttrs.SubsIsIncremental)
                {
                    FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                    listFrames.Clear();
                    dlFrames.Clear();
                    if (blockAttrs.Id is not null)
                        pendingBlockId = blockAttrs.Id;
                    if (blockAttrs.Named.TryGetValue("reftext", out var reftext))
                        pendingBlockReftext = reftext;
                    if (blockAttrs.Roles.Count > 0)
                        pendingBlockRoles = blockAttrs.Roles;
                    continue;
                }
                // [horizontal] / [qanda] — style for description lists (consumed as pending style, applied to next dlist).
                if (blockAttrs is not null && (
                    string.Equals(blockAttrs.Style, "horizontal", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(blockAttrs.Style, "qanda", StringComparison.OrdinalIgnoreCase)))
                {
                    FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                    listFrames.Clear();
                    dlFrames.Clear();
                    pendingDlStyle = blockAttrs.Style!.ToLowerInvariant();
                    if (blockAttrs.Id is not null)
                        pendingBlockId = blockAttrs.Id;
                    if (blockAttrs.Roles.Count > 0)
                        pendingBlockRoles = blockAttrs.Roles;
                    continue;
                }
            }

            // [source] or [source,lang] or [source#id] attribute line.
            if (TryParseSourceAttribute(line, out var sourceLangValue, out var sourceBlockId, out var sourceRoles, out var sourceHighlight))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                pendingSourceLang = sourceLangValue;
                pendingHighlight = sourceHighlight;
                hasPendingSource = true;
                if (sourceBlockId is not null)
                    pendingBlockId = sourceBlockId;
                if (sourceRoles is not null)
                    pendingBlockRoles = sourceRoles;
                // pendingBlockTitle is intentionally preserved: .Title may come before [source].
                continue;
            }

            // [quote] or [quote, attribution] or [quote, attribution, citation source] attribute line.
            if (line.StartsWith("[quote") && line.EndsWith("]") && (line.Length == 7 || line[6] is ',' or ']'))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                hasPendingQuote = true;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;
                var content = line[1..^1]; // "quote" or "quote, Author" or "quote, Author, Source"
                if (content.Contains(','))
                {
                    var parts = content.Split(',', 3);
                    if (parts.Length >= 2 && parts[1].Trim().Length > 0)
                        pendingQuoteAttribution = parts[1].Trim();
                    if (parts.Length >= 3 && parts[2].Trim().Length > 0)
                        pendingQuoteCitation = parts[2].Trim();
                }
                continue;
            }

            // [verse] or [verse, attribution] or [verse, attribution, citation source] attribute line.
            if (line.StartsWith("[verse") && line.EndsWith("]") && (line.Length == 7 || line[6] is ',' or ']'))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                hasPendingVerse = true;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;
                var content = line[1..^1]; // "verse" or "verse, Author" or "verse, Author, Source"
                if (content.Contains(','))
                {
                    var parts = content.Split(',', 3);
                    if (parts.Length >= 2 && parts[1].Trim().Length > 0)
                        pendingQuoteAttribution = parts[1].Trim();
                    if (parts.Length >= 3 && parts[2].Trim().Length > 0)
                        pendingQuoteCitation = parts[2].Trim();
                }
                continue;
            }

            // [example] block style attribute — sets pending flag for chameleon routing.
            if (line.StartsWith("[example") && line.EndsWith("]") && (line.Length == 9 || line[8] is ',' or '%' or ']'))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                hasPendingExample = true;
                continue;
            }

            // [listing] block style attribute.
            if (line.StartsWith("[listing") && line.EndsWith("]") && (line.Length == 9 || line[8] is ',' or '%' or ']'))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                hasPendingListing = true;
                continue;
            }

            // [literal] block style attribute.
            if (line.StartsWith("[literal") && line.EndsWith("]") && (line.Length == 9 || line[8] is ',' or '%' or ']'))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                hasPendingLiteral = true;
                continue;
            }

            // [sidebar] block style attribute.
            if (line.StartsWith("[sidebar") && line.EndsWith("]") && (line.Length == 9 || line[8] is ',' or '%' or ']'))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                hasPendingSidebar = true;
                continue;
            }

            // [options="header"] attribute line (table header marker).
            if (line == "[options=\"header\"]")
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                hasPendingOptionsHeader = true;
                continue;
            }

            // [options="footer"] attribute line (table footer marker).
            if (line == "[options=\"footer\"]")
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                hasPendingFooter = true;
                continue;
            }

            // [%autowidth] shorthand option.
            if (line == "[%autowidth]")
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                hasPendingAutoWidth = true;
                continue;
            }

            // [%footer] shorthand option.
            if (line == "[%footer]")
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                hasPendingFooter = true;
                continue;
            }

            // [cols="..."] attribute line (table column specification), possibly combined
            // with other attributes like [cols="1,2,1", options="header"].
            if (TryParseColsAttribute(line, out var colSpecValue, out var colsLineOptions))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                pendingColSpec = colSpecValue;
                if (colsLineOptions is not null)
                {
                    if (colsLineOptions.Contains("header", StringComparison.OrdinalIgnoreCase))
                        hasPendingOptionsHeader = true;
                    if (colsLineOptions.Contains("footer", StringComparison.OrdinalIgnoreCase))
                        hasPendingFooter = true;
                    if (colsLineOptions.Contains("autowidth", StringComparison.OrdinalIgnoreCase))
                        hasPendingAutoWidth = true;
                }
                continue;
            }

            // Role attribute line: [role="value1 value2"] on a line by itself.
            if (paragraphLines.Count == 0 && TryParseRoleAttribute(line, out var roleAttrValues))
            {
                // No FlushParagraph needed: paragraphLines.Count == 0 is already checked above.
                listFrames.Clear();
                dlFrames.Clear();
                pendingBlockRoles = roleAttrValues;
                continue;
            }

            // Shorthand ID/role attribute: [#id], [#id.role], [.role], [.role1.role2] on a line by itself.
            if (paragraphLines.Count == 0 && TryParseShorthandIdOrRoles(line, out var shorthandId, out var shorthandRoles))
            {
                // No FlushParagraph needed: paragraphLines.Count == 0 is already checked above.
                listFrames.Clear();
                dlFrames.Clear();
                if (shorthandId is not null)
                    pendingBlockId = shorthandId;
                if (shorthandRoles is not null)
                    pendingBlockRoles = shorthandRoles;
                continue;
            }

            // Block anchor: [[id]] or [[id,reftext]] on a line by itself assigns an ID to the next block.
            if (paragraphLines.Count == 0 && TryParseBlockAnchor(line, out var anchorId, out var anchorReftext))
            {
                // No FlushParagraph needed: paragraphLines.Count == 0 is already checked above.
                listFrames.Clear();
                dlFrames.Clear();
                pendingBlockId = anchorId;
                if (anchorReftext is not null)
                    pendingBlockReftext = anchorReftext;
                continue;
            }

            // Admonition attribute line: [NOTE], [TIP], [IMPORTANT], [WARNING], [CAUTION]
            if (TryParseAdmonitionAttribute(line, out var admonAttrType))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                pendingAdmonitionType = admonAttrType;
                continue;
            }

            // Bibliography attribute line: [bibliography]
            if (line == "[bibliography]")
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                hasPendingBibliography = true;
                continue;
            }

            // Catch-all for unrecognized [...] attribute lines that carried subs.
            // If we pre-parsed a valid BlockAttributes with subs but none of the specific
            // handlers above consumed the line, consume it here so it doesn't leak into
            // paragraph text and reset pendingSubs.
            if (paragraphLines.Count == 0 && (pendingSubs is not null || pendingSubsIsIncremental)
                && line.Length > 1 && line[0] == '[' && line[^1] == ']')
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                continue;
            }

            // Block macro: image::target[alt]
            if (paragraphLines.Count == 0)
            {
                var expandedLine = InlineParser.ExpandAttributes(line, document.Attributes);
                var macroMatch = TryParseBlockMacro(expandedLine, out var blockMacroNode, out var unknownMacroName, pendingBlockOptions);
                if (macroMatch)
                {
                    // No FlushParagraph needed: paragraphLines.Count == 0 is already checked above.
                    listFrames.Clear();
                dlFrames.Clear();

                    if (blockMacroNode is BlockImageNode blockImage && pendingBlockTitle is not null)
                    {
                        // Apply pending block title to the image node.
                        blockMacroNode = new BlockImageNode
                        {
                            Target = blockImage.Target,
                            Alt    = blockImage.Alt,
                            Title  = pendingBlockTitle,
                            Width  = blockImage.Width,
                            Height = blockImage.Height,
                            Link   = blockImage.Link,
                            Roles  = blockImage.Roles,
                        };
                    }

                    blockMacroNode!.Source = new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length));
                    if (blockMacroNode is BlockNode blockMacroBlock)
                    {
                        ApplyPendingId(blockMacroBlock, lineNumber, line.Length);
                        if (pendingBlockRoles is not null)
                        {
                            // Merge pendingBlockRoles (from [.role] shorthand) with
                            // any alignment-derived roles already set on the macro node.
                            if (blockMacroBlock.Roles.Count == 0)
                                blockMacroBlock.Roles = pendingBlockRoles;
                            else
                            {
                                var merged = new List<string>(pendingBlockRoles.Count + blockMacroBlock.Roles.Count);
                                merged.AddRange(pendingBlockRoles);
                                foreach (var r in blockMacroBlock.Roles)
                                    if (!merged.Contains(r)) merged.Add(r);
                                blockMacroBlock.Roles = merged;
                            }
                        }
                    }
                    // toc::[] placeholder always goes to document level for post-parse replacement.
                    if (blockMacroNode is TocNode)
                        document.AddChild(blockMacroNode);
                    else
                        currentContainer.AddChild(blockMacroNode);
                    pendingBlockTitle = null;
                    pendingSourceLang = null;
                    pendingHighlight = null;
                    hasPendingSource = false;
                    hasPendingVerse = false;
                    hasPendingQuote = false;
                    hasPendingListing = false;
                    hasPendingLiteral = false;
                    hasPendingExample = false;
                    hasPendingSidebar = false;
                    pendingQuoteAttribution = null;
                    pendingQuoteCitation = null;
    
                    hasPendingOptionsHeader = false;
                hasPendingAutoWidth = false;
                hasPendingFooter = false;
                pendingColSpec = null;
                pendingStripes = null;
                pendingGrid = null;
                pendingFrame = null;
                pendingFormat = null;
                    pendingAdmonitionType = null;
                    pendingBlockId = null;
                    pendingBlockReftext = null;
                    pendingBlockRoles = null;
                    pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;
                    pendingBlockOptions = null;
                    continue;
                }

                if (unknownMacroName is not null)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Unknown block macro '{unknownMacroName}'",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                    // Fall through to paragraph text.
                }
            }

            // Table: |===, ,===, :===
            // Determine table format from delimiter or pending format attribute.
            string? detectedFormat = null;
            bool isTableStart = false;
            Func<string, bool>? tableDelimiterCheck = null;

            bool isNestedTable = false;

            if (IsTableDelimiter(line))
            {
                isTableStart = true;
                tableDelimiterCheck = IsTableDelimiter;
                // [format=csv/dsv/tsv] on |=== uses that format; otherwise pipe-based
                detectedFormat = pendingFormat;
            }
            else if (IsNestedTableDelimiter(line))
            {
                isTableStart = true;
                isNestedTable = true;
                tableDelimiterCheck = IsNestedTableDelimiter;
                detectedFormat = pendingFormat;
            }
            else if (IsCsvTableDelimiter(line))
            {
                isTableStart = true;
                tableDelimiterCheck = IsCsvTableDelimiter;
                detectedFormat = "csv";
            }
            else if (IsDsvTableDelimiter(line))
            {
                isTableStart = true;
                tableDelimiterCheck = IsDsvTableDelimiter;
                detectedFormat = "dsv";
            }

            if (isTableStart)
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();

                // Scan forward for closing delimiter
                int closingIdx = -1;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (tableDelimiterCheck!(lines[j]))
                    {
                        closingIdx = j;
                        break;
                    }
                }

                if (closingIdx < 0)
                {
                    // Unclosed table: emit diagnostic, treat delimiter as paragraph text.
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Unclosed table starting at line {lineNumber}",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                    pendingBlockTitle = null;
                    pendingSourceLang = null;
                    pendingHighlight = null;
                    hasPendingSource = false;
                    hasPendingVerse = false;
                    hasPendingQuote = false;
                    hasPendingListing = false;
                    hasPendingLiteral = false;
                    hasPendingExample = false;
                    hasPendingSidebar = false;
                    pendingQuoteAttribution = null;
                    pendingQuoteCitation = null;

                    hasPendingOptionsHeader = false;
                hasPendingAutoWidth = false;
                hasPendingFooter = false;
                pendingColSpec = null;
                pendingStripes = null;
                pendingGrid = null;
                pendingFrame = null;
                pendingFormat = null;
                    pendingAdmonitionType = null;
                    pendingBlockId = null;
                    pendingBlockReftext = null;
                    pendingBlockRoles = null;
                    pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;
                    if (paragraphLines.Count == 0) paragraphStartLine = lineNumber;
                    paragraphLines.Add(line);
                    continue;
                }

                TableNode table;
                if (detectedFormat is not null && (
                    string.Equals(detectedFormat, "csv", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(detectedFormat, "dsv", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(detectedFormat, "tsv", StringComparison.OrdinalIgnoreCase)))
                {
                    table = ParseSeparatedTableContent(lines, i + 1, closingIdx, detectedFormat, hasPendingOptionsHeader, hasPendingAutoWidth, hasPendingFooter, pendingColSpec, pendingStripes, pendingGrid, pendingFrame, document.Attributes);
                }
                else
                {
                    char cellSep = isNestedTable ? '!' : '|';
                    table = ParseTableContent(lines, i + 1, closingIdx, hasPendingOptionsHeader, hasPendingAutoWidth, hasPendingFooter, pendingColSpec, pendingStripes, pendingGrid, pendingFrame, detectedFormat, document.Attributes, cellSep);
                }
                table.Source = closingIdx < lines.Length
                    ? new SourceRange(new(lineNumber, 1), new(closingIdx + 1, lines[closingIdx].Length))
                    : new SourceRange(new(lineNumber, 1), new(lines.Length, lines[^1].Length));
                ApplyPendingId(table, lineNumber, line.Length);
                if (pendingBlockRoles is not null)
                    table.Roles = pendingBlockRoles;
                if (pendingBlockTitle is not null)
                    table.Title = pendingBlockTitle;

                currentContainer.AddChild(table);
                pendingBlockTitle = null;
                pendingSourceLang = null;
                pendingHighlight = null;
                hasPendingSource = false;
                hasPendingVerse = false;
                hasPendingQuote = false;
                hasPendingListing = false;
                hasPendingLiteral = false;
                hasPendingExample = false;
                hasPendingSidebar = false;
                pendingCollapsible = false;
                pendingStem = null;
                pendingDlStyle = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;

                hasPendingOptionsHeader = false;
                hasPendingAutoWidth = false;
                hasPendingFooter = false;
                pendingColSpec = null;
                pendingStripes = null;
                pendingGrid = null;
                pendingFrame = null;
                pendingFormat = null;
                pendingAdmonitionType = null;
                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;
                i = closingIdx;
                continue;
            }

            // Page break: <<< (3+ '<' characters on a line by themselves)
            if (IsBreakLine(line, '<'))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                var pageBreak = new PageBreakNode();
                pageBreak.Source = new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length));
                ApplyPendingId(pageBreak, lineNumber, line.Length);
                if (pendingBlockRoles is not null)
                {
                    pageBreak.Roles = pendingBlockRoles;
                    pendingBlockRoles = null;
                }
                currentContainer.AddChild(pageBreak);
                continue;
            }

            // Thematic break: ''' (3+ '\'' characters on a line by themselves)
            if (IsBreakLine(line, '\''))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                var thematicBreak = new ThematicBreakNode();
                thematicBreak.Source = new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length));
                ApplyPendingId(thematicBreak, lineNumber, line.Length);
                if (pendingBlockRoles is not null)
                {
                    thematicBreak.Roles = pendingBlockRoles;
                    pendingBlockRoles = null;
                }
                currentContainer.AddChild(thematicBreak);
                continue;
            }

            // Block comment: //// (4+ slashes on a line by themselves)
            if (IsDelimiterLine(line, '/'))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();

                // Scan forward for the matching closing delimiter.
                int closingIdx = -1;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (IsDelimiterLine(lines[j], '/'))
                    {
                        closingIdx = j;
                        break;
                    }
                }

                if (closingIdx < 0)
                {
                    // Unclosed comment block: emit diagnostic, discard everything to end.
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Unclosed block comment starting at line {lineNumber}",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                }

                // Discard all content (no AST node created).
                pendingBlockTitle = null;
                pendingSourceLang = null;
                pendingHighlight = null;
                hasPendingSource = false;
                hasPendingVerse = false;
                hasPendingQuote = false;
                hasPendingListing = false;
                hasPendingLiteral = false;
                hasPendingExample = false;
                hasPendingSidebar = false;
                pendingCollapsible = false;
                pendingStem = null;
                pendingDlStyle = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;

                hasPendingOptionsHeader = false;
                pendingAdmonitionType = null;
                i = closingIdx < 0 ? lines.Length - 1 : closingIdx;
                continue;
            }

            // Single-line comment: // followed by anything (but not /// or more slashes)
            if (line.Length >= 2 && line[0] == '/' && line[1] == '/' && (line.Length == 2 || line[2] != '/'))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                pendingBlockTitle = null;
                pendingSourceLang = null;
                pendingHighlight = null;
                hasPendingSource = false;
                hasPendingVerse = false;
                hasPendingQuote = false;
                hasPendingListing = false;
                hasPendingLiteral = false;
                hasPendingExample = false;
                hasPendingSidebar = false;
                pendingCollapsible = false;
                pendingStem = null;
                pendingDlStyle = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;

                hasPendingOptionsHeader = false;
                pendingAdmonitionType = null;
                continue;
            }

            // Open block delimiter: exactly "--" (2 dashes). This is a chameleon block
            // whose effective kind depends on the pending style attribute.
            if (IsOpenBlockDelimiter(line))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();

                // Scan forward for the matching closing "--" delimiter.
                int openClosingIdx = -1;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (IsOpenBlockDelimiter(lines[j]))
                    {
                        openClosingIdx = j;
                        break;
                    }
                }

                if (openClosingIdx < 0)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Unclosed delimited block starting at line {lineNumber}",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                    openClosingIdx = lines.Length; // virtual EOF acts as closing delimiter
                }

                var openContentLines = lines[(i + 1)..openClosingIdx];
                var openRawContent = string.Join("\n", openContentLines);

                // Chameleon style routing: determine effective kind from pending style.
                DelimitedBlockKind openKind;
                bool openIsStructural;

                if (hasPendingSource)
                {
                    openKind = DelimitedBlockKind.Source;
                    openIsStructural = false;
                }
                else if (hasPendingVerse)
                {
                    openKind = DelimitedBlockKind.Verse;
                    openIsStructural = false; // Verse: raw content, not parsed as nested blocks
                }
                else if (hasPendingQuote)
                {
                    openKind = DelimitedBlockKind.Quote;
                    openIsStructural = true;
                }
                else if (hasPendingListing)
                {
                    openKind = DelimitedBlockKind.Listing;
                    openIsStructural = false; // Listing: verbatim content
                }
                else if (hasPendingLiteral)
                {
                    openKind = DelimitedBlockKind.Literal;
                    openIsStructural = false; // Literal: verbatim content
                }
                else if (hasPendingExample)
                {
                    openKind = DelimitedBlockKind.Example;
                    openIsStructural = true; // Example: nested blocks
                }
                else if (hasPendingSidebar)
                {
                    openKind = DelimitedBlockKind.Sidebar;
                    openIsStructural = true; // Sidebar: nested blocks
                }
                else
                {
                    openKind = DelimitedBlockKind.Open;
                    openIsStructural = true;
                }

                // Stem blocks: create StemBlockNode instead of DelimitedBlockNode
                if (pendingStem is not null)
                {
                    var stemBlock = new StemBlockNode
                    {
                        Content = openRawContent,
                        StemType = pendingStem,
                        Title = pendingBlockTitle,
                        Substitutions = SubstitutionKind.Verbatim,
                        Source = openClosingIdx < lines.Length
                            ? new SourceRange(new(lineNumber, 1), new(openClosingIdx + 1, lines[openClosingIdx].Length))
                            : new SourceRange(new(lineNumber, 1), new(lines.Length, lines[^1].Length)),
                    };
                    ApplyPendingId(stemBlock, lineNumber, line.Length);
                    if (pendingBlockRoles is not null)
                        stemBlock.Roles = pendingBlockRoles;
                    currentContainer.AddChild(stemBlock);
                }
                else
                {
                    var openDefaultSubs = openIsStructural ? EffectiveNormal() : SubstitutionKind.Verbatim;
                    var openBlock = new DelimitedBlockNode
                    {
                        BlockKind = openKind,
                        Content = openIsStructural ? null : openRawContent,
                        Title = pendingBlockTitle,
                        Style = (openKind == DelimitedBlockKind.Open && pendingAbstract) ? "abstract" : null,
                        Language = openKind == DelimitedBlockKind.Source ? pendingSourceLang : null,
                        Highlight = openKind == DelimitedBlockKind.Source ? pendingHighlight : null,
                        Attribution = (openKind is DelimitedBlockKind.Quote or DelimitedBlockKind.Verse) ? pendingQuoteAttribution : null,
                        CitationSource = (openKind is DelimitedBlockKind.Quote or DelimitedBlockKind.Verse) ? pendingQuoteCitation : null,
                        IsCollapsible = pendingCollapsible,
                        Substitutions = ResolvePendingSubs(openDefaultSubs),
                        Source = openClosingIdx < lines.Length
                            ? new SourceRange(new(lineNumber, 1), new(openClosingIdx + 1, lines[openClosingIdx].Length))
                            : new SourceRange(new(lineNumber, 1), new(lines.Length, lines[^1].Length)),
                    };
                    ApplyPendingId(openBlock, lineNumber, line.Length);
                    if (pendingBlockRoles is not null)
                        openBlock.Roles = pendingBlockRoles;

                    if (openIsStructural)
                    {
                        var innerResult = BlockParser.Parse(openRawContent, document.Attributes);
                        foreach (var child in innerResult.Document.Children)
                            openBlock.AddChild(child);
                    }

                    currentContainer.AddChild(openBlock);
                }

                pendingBlockTitle = null;
                pendingSourceLang = null;
                pendingHighlight = null;
                hasPendingSource = false;
                hasPendingVerse = false;
                hasPendingQuote = false;
                hasPendingListing = false;
                hasPendingLiteral = false;
                hasPendingExample = false;
                hasPendingSidebar = false;
                pendingCollapsible = false;
                pendingStem = null;
                pendingDlStyle = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;
                hasPendingOptionsHeader = false;
                hasPendingAutoWidth = false;
                hasPendingFooter = false;
                pendingColSpec = null;
                pendingStripes = null;
                pendingGrid = null;
                pendingFrame = null;
                pendingFormat = null;
                pendingAdmonitionType = null;
                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;
                pendingAbstract = false;
                pendingStem = null;
                i = openClosingIdx;
                continue;
            }

            // Fenced code block: ``` or ```lang
            if (TryParseFencedCodeOpening(line, out var fencedLang))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();

                // Scan forward for closing fence (3+ backticks, no language).
                int fenceClosingIdx = -1;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (IsClosingFence(lines[j]))
                    {
                        fenceClosingIdx = j;
                        break;
                    }
                }

                if (fenceClosingIdx < 0)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Unclosed fenced code block starting at line {lineNumber}",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                    fenceClosingIdx = lines.Length;
                }

                var fenceContentLines = lines[(i + 1)..fenceClosingIdx];
                if (fenceClosingIdx >= lines.Length)
                {
                    int contentEnd = fenceContentLines.Length;
                    while (contentEnd > 0 && fenceContentLines[contentEnd - 1].Length == 0)
                        contentEnd--;
                    if (contentEnd < fenceContentLines.Length)
                        fenceContentLines = fenceContentLines[..contentEnd];
                }

                // Use pending [source,lang] language if fenced has no language.
                var effectiveLang = fencedLang ?? (hasPendingSource ? pendingSourceLang : null);

                // Strip callout markers from fenced source blocks.
                string fenceContent;
                List<CalloutEntry>? fenceCalloutEntries = null;
                Dictionary<int, List<int>>? fenceCalloutMap = null;
                {
                    var strippedLines = new string[fenceContentLines.Length];
                    int autoNumber = 1;
                    for (int cl = 0; cl < fenceContentLines.Length; cl++)
                    {
                        strippedLines[cl] = StripCalloutMarker(fenceContentLines[cl], out var nums);
                        if (nums is { Count: > 0 })
                        {
                            fenceCalloutMap ??= [];
                            for (int ni = 0; ni < nums.Count; ni++)
                            {
                                int num = nums[ni] == -1 ? autoNumber++ : nums[ni];
                                if (!fenceCalloutMap.TryGetValue(num, out var lineList))
                                {
                                    lineList = [];
                                    fenceCalloutMap[num] = lineList;
                                }
                                lineList.Add(cl);
                            }
                        }
                    }

                    for (int cl = 0; cl < strippedLines.Length; cl++)
                    {
                        if (strippedLines[cl].StartsWith('\\') &&
                            (strippedLines[cl].AsSpan(1).StartsWith("ifdef::") ||
                             strippedLines[cl].AsSpan(1).StartsWith("ifndef::") ||
                             strippedLines[cl].AsSpan(1).StartsWith("endif::") ||
                             strippedLines[cl].AsSpan(1).StartsWith("ifeval::") ||
                             strippedLines[cl].AsSpan(1).StartsWith("include::")))
                        {
                            strippedLines[cl] = strippedLines[cl][1..];
                        }
                    }

                    fenceContent = string.Join("\n", strippedLines);
                }

                // Parse callout list entries after the closing delimiter.
                int fenceAfterClosing = fenceClosingIdx;
                {
                    int ci = fenceClosingIdx + 1;
                    while (ci < lines.Length)
                    {
                        if (TryParseCalloutEntry(lines[ci], out int calloutNum, out string calloutText))
                        {
                            fenceCalloutEntries ??= [];
                            int entryNumber = calloutNum > 0 ? calloutNum : fenceCalloutEntries.Count + 1;
                            int lineNum = -1;
                            if (fenceCalloutMap is not null && fenceCalloutMap.TryGetValue(entryNumber, out var lineList) && lineList.Count > 0)
                            {
                                lineNum = lineList[0];
                                lineList.RemoveAt(0);
                            }
                            fenceCalloutEntries.Add(new CalloutEntry
                            {
                                Number = entryNumber,
                                Text = calloutText,
                                Inlines = InlineParser.Parse(calloutText, EffectiveNormal(), document.Attributes),
                                LineNumber = lineNum,
                            });
                            ci++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    fenceAfterClosing = ci - 1;

                    if (fenceCalloutEntries is null && fenceCalloutMap is { Count: > 0 })
                    {
                        fenceCalloutEntries = [];
                        foreach (var (num, lineNums) in fenceCalloutMap.OrderBy(kv => kv.Key))
                        {
                            foreach (var ln in lineNums)
                            {
                                fenceCalloutEntries.Add(new CalloutEntry
                                {
                                    Number = num,
                                    Text = string.Empty,
                                    Inlines = [],
                                    LineNumber = ln,
                                });
                            }
                        }
                    }
                }

                var fenceBlock = new DelimitedBlockNode
                {
                    BlockKind = DelimitedBlockKind.Source,
                    Content = fenceContent,
                    Title = pendingBlockTitle,
                    Language = effectiveLang,
                    Highlight = pendingHighlight,
                    Callouts = fenceCalloutEntries,
                    Substitutions = ResolvePendingSubs(SubstitutionKind.Verbatim),
                    Source = fenceClosingIdx < lines.Length
                        ? new SourceRange(new(lineNumber, 1), new(fenceClosingIdx + 1, lines[fenceClosingIdx].Length))
                        : new SourceRange(new(lineNumber, 1), new(lines.Length, lines[^1].Length)),
                };
                ApplyPendingId(fenceBlock, lineNumber, line.Length);
                if (pendingBlockRoles is not null)
                    fenceBlock.Roles = pendingBlockRoles;

                currentContainer.AddChild(fenceBlock);

                pendingBlockTitle = null;
                pendingSourceLang = null;
                pendingHighlight = null;
                hasPendingSource = false;
                hasPendingVerse = false;
                hasPendingQuote = false;
                hasPendingListing = false;
                hasPendingLiteral = false;
                hasPendingExample = false;
                hasPendingSidebar = false;
                pendingCollapsible = false;
                pendingStem = null;
                pendingDlStyle = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;
                hasPendingOptionsHeader = false;
                hasPendingAutoWidth = false;
                hasPendingFooter = false;
                pendingColSpec = null;
                pendingStripes = null;
                pendingGrid = null;
                pendingFrame = null;
                pendingFormat = null;
                pendingAdmonitionType = null;
                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;
                pendingBlockOptions = null;
                i = fenceAfterClosing < fenceClosingIdx ? fenceClosingIdx : fenceAfterClosing;
                continue;
            }

            // $$ stem block delimiter (only when :stem: is set)
            if (IsStemDelimiterLine(line) && document.Attributes.ContainsKey("stem"))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();

                // Scan for closing $$
                int stemClosingIdx = -1;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (IsStemDelimiterLine(lines[j]))
                    {
                        stemClosingIdx = j;
                        break;
                    }
                }

                if (stemClosingIdx < 0)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Unclosed $$ stem block starting at line {lineNumber}",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                    stemClosingIdx = lines.Length;
                }

                var stemContent = string.Join("\n", lines[(i + 1)..stemClosingIdx]);
                var stemBlock = new StemBlockNode
                {
                    Content = stemContent,
                    StemType = "latexmath",
                    Title = pendingBlockTitle,
                    Substitutions = SubstitutionKind.Verbatim,
                    Source = stemClosingIdx < lines.Length
                        ? new SourceRange(new(lineNumber, 1), new(stemClosingIdx + 1, lines[stemClosingIdx].Length))
                        : new SourceRange(new(lineNumber, 1), new(lines.Length, lines[^1].Length)),
                };
                ApplyPendingId(stemBlock, lineNumber, line.Length);
                if (pendingBlockRoles is not null)
                    stemBlock.Roles = pendingBlockRoles;
                currentContainer.AddChild(stemBlock);

                pendingBlockTitle = null;
                pendingStem = null;
                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                i = stemClosingIdx;
                continue;
            }

            // Delimited block: ...., ----, or ====
            if (TryGetDelimiterKind(line, out var delimChar, out var delimKind))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();

                // Scan forward for the matching closing delimiter.
                int closingIdx = -1;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (IsDelimiterLine(lines[j], delimChar))
                    {
                        closingIdx = j;
                        break;
                    }
                }

                if (closingIdx < 0)
                {
                    // Unclosed block: emit a diagnostic and consume all remaining content
                    // through EOF, matching Asciidoctor's behavior.
                    diagnostics.Add(new Diagnostic(
                        DiagnosticSeverity.Warning,
                        $"Unclosed delimited block starting at line {lineNumber}",
                        new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
                    closingIdx = lines.Length; // virtual EOF acts as closing delimiter
                }

                // Promote listing/literal block to source when [source] was pending.
                if ((delimKind == DelimitedBlockKind.Listing || delimKind == DelimitedBlockKind.Literal) && hasPendingSource)
                    delimKind = DelimitedBlockKind.Source;

                // Promote quote block to verse when [verse] was pending.
                if (delimKind == DelimitedBlockKind.Quote && hasPendingVerse)
                    delimKind = DelimitedBlockKind.Verse;

                var contentLines = lines[(i + 1)..closingIdx];
                // When block consumed to EOF (unclosed), trim trailing empty lines
                // (from file's trailing newline) to match Asciidoctor output.
                if (closingIdx >= lines.Length)
                {
                    int contentEnd = contentLines.Length;
                    while (contentEnd > 0 && contentLines[contentEnd - 1].Length == 0)
                        contentEnd--;
                    if (contentEnd < contentLines.Length)
                        contentLines = contentLines[..contentEnd];
                }

                bool isVerbatim = delimKind is DelimitedBlockKind.Source
                    or DelimitedBlockKind.Listing or DelimitedBlockKind.Literal;

                // Strip callout markers from source/listing blocks.
                string rawContent;
                Dictionary<int, List<int>>? calloutLineMap = null;
                if (isVerbatim && delimKind is DelimitedBlockKind.Source or DelimitedBlockKind.Listing)
                {
                    var strippedLines = new string[contentLines.Length];
                    int autoNumber = 1;
                    for (int cl = 0; cl < contentLines.Length; cl++)
                    {
                        strippedLines[cl] = StripCalloutMarker(contentLines[cl], out var nums);
                        if (nums is { Count: > 0 })
                        {
                            calloutLineMap ??= [];
                            for (int ni = 0; ni < nums.Count; ni++)
                            {
                                int num = nums[ni] == -1 ? autoNumber++ : nums[ni];
                                if (!calloutLineMap.TryGetValue(num, out var lineList))
                                {
                                    lineList = [];
                                    calloutLineMap[num] = lineList;
                                }
                                lineList.Add(cl);
                            }
                        }
                    }

                    // Strip escaped preprocessor directives in verbatim blocks
                    for (int cl = 0; cl < strippedLines.Length; cl++)
                    {
                        if (strippedLines[cl].StartsWith('\\') &&
                            (strippedLines[cl].AsSpan(1).StartsWith("ifdef::") ||
                             strippedLines[cl].AsSpan(1).StartsWith("ifndef::") ||
                             strippedLines[cl].AsSpan(1).StartsWith("endif::") ||
                             strippedLines[cl].AsSpan(1).StartsWith("ifeval::") ||
                             strippedLines[cl].AsSpan(1).StartsWith("include::")))
                        {
                            strippedLines[cl] = strippedLines[cl][1..];
                        }
                    }

                    rawContent = string.Join("\n", strippedLines);
                }
                else
                {
                    rawContent = string.Join("\n", contentLines);
                }

                bool isStructural = delimKind is DelimitedBlockKind.Example
                    or DelimitedBlockKind.Quote or DelimitedBlockKind.Sidebar
                    or DelimitedBlockKind.Open;

                // Parse callout list entries after the closing delimiter for source/listing blocks.
                List<CalloutEntry>? calloutEntries = null;
                int afterClosing = closingIdx;
                if (delimKind is DelimitedBlockKind.Source or DelimitedBlockKind.Listing)
                {
                    int ci = closingIdx + 1;
                    while (ci < lines.Length)
                    {
                        if (TryParseCalloutEntry(lines[ci], out int calloutNum, out string calloutText))
                        {
                            calloutEntries ??= [];
                            int entryNumber = calloutNum > 0 ? calloutNum : calloutEntries.Count + 1;
                            int lineNum = -1;
                            if (calloutLineMap is not null && calloutLineMap.TryGetValue(entryNumber, out var lineList) && lineList.Count > 0)
                            {
                                lineNum = lineList[0];
                                lineList.RemoveAt(0);
                            }
                            calloutEntries.Add(new CalloutEntry
                            {
                                Number = entryNumber,
                                Text = calloutText,
                                Inlines = InlineParser.Parse(calloutText, EffectiveNormal(), document.Attributes),
                                LineNumber = lineNum,
                            });
                            ci++;
                        }
                        else if (calloutEntries is { Count: > 0 } && lines[ci].Length > 0 && !string.IsNullOrWhiteSpace(lines[ci]))
                        {
                            // Continuation line for the previous callout entry
                            var prev = calloutEntries[^1];
                            var newText = prev.Text + "\n" + lines[ci].TrimStart();
                            calloutEntries[^1] = new CalloutEntry
                            {
                                Number = prev.Number,
                                Text = newText,
                                Inlines = InlineParser.Parse(newText, EffectiveNormal(), document.Attributes),
                                LineNumber = prev.LineNumber, // preserve source line mapping
                            };
                            ci++;
                        }
                        else
                        {
                            break;
                        }
                    }
                    afterClosing = ci - 1;

                    // When callout markers were stripped from source but no explanation list
                    // follows, create synthetic callout entries so the renderer still emits
                    // conum markers inline (matching Asciidoctor behavior).
                    if (calloutEntries is null && calloutLineMap is { Count: > 0 })
                    {
                        calloutEntries = [];
                        foreach (var (num, lineNums) in calloutLineMap.OrderBy(kv => kv.Key))
                        {
                            foreach (var ln in lineNums)
                            {
                                calloutEntries.Add(new CalloutEntry
                                {
                                    Number = num,
                                    Text = string.Empty,
                                    Inlines = [],
                                    LineNumber = ln,
                                });
                            }
                        }
                    }
                }

                // Block admonition: [NOTE]/[TIP]/etc. + ==== block.
                if (pendingAdmonitionType is not null && delimKind == DelimitedBlockKind.Example)
                {
                    var admonNode = new AdmonitionNode
                    {
                        AdmonitionType = pendingAdmonitionType,
                        Title = pendingBlockTitle,
                        Source = closingIdx < lines.Length
                            ? new SourceRange(new(lineNumber, 1), new(closingIdx + 1, lines[closingIdx].Length))
                            : new SourceRange(new(lineNumber, 1), new(lines.Length, lines[^1].Length)),
                    };
                    ApplyPendingId(admonNode, lineNumber, line.Length);
                    if (pendingBlockRoles is not null)
                        admonNode.Roles = pendingBlockRoles;
                    var innerResult = BlockParser.Parse(rawContent, document.Attributes);
                    foreach (var child in innerResult.Document.Children)
                        admonNode.AddChild(child);
                    currentContainer.AddChild(admonNode);
                }
                else
                {
                    var delimDefaultSubs = isStructural ? EffectiveNormal() : SubstitutionKind.Verbatim;
                    var block = new DelimitedBlockNode
                    {
                        BlockKind = delimKind,
                        Content = isStructural ? null : rawContent,
                        Title = pendingBlockTitle,
                        Language = delimKind == DelimitedBlockKind.Source ? pendingSourceLang : null,
                        Highlight = delimKind == DelimitedBlockKind.Source ? pendingHighlight : null,
                        Attribution = delimKind is DelimitedBlockKind.Quote or DelimitedBlockKind.Verse ? pendingQuoteAttribution : null,
                        CitationSource = delimKind is DelimitedBlockKind.Quote or DelimitedBlockKind.Verse ? pendingQuoteCitation : null,
                        IsCollapsible = pendingCollapsible,
                        Callouts = calloutEntries,
                        Substitutions = ResolvePendingSubs(delimDefaultSubs),
                        Source = closingIdx < lines.Length
                            ? new SourceRange(new(lineNumber, 1), new(closingIdx + 1, lines[closingIdx].Length))
                            : new SourceRange(new(lineNumber, 1), new(lines.Length, lines[^1].Length)),
                    };
                    ApplyPendingId(block, lineNumber, line.Length);
                    if (pendingBlockRoles is not null)
                        block.Roles = pendingBlockRoles;

                    // Structural blocks: recursively parse content into child nodes.
                    if (isStructural)
                    {
                        var innerResult = BlockParser.Parse(rawContent, document.Attributes);
                        foreach (var child in innerResult.Document.Children)
                            block.AddChild(child);
                    }

                    currentContainer.AddChild(block);
                }

                pendingBlockTitle = null;
                pendingSourceLang = null;
                pendingHighlight = null;
                hasPendingSource = false;
                hasPendingVerse = false;
                hasPendingQuote = false;
                hasPendingListing = false;
                hasPendingLiteral = false;
                hasPendingExample = false;
                hasPendingSidebar = false;
                pendingCollapsible = false;
                pendingStem = null;
                pendingDlStyle = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;

                hasPendingOptionsHeader = false;
                hasPendingAutoWidth = false;
                hasPendingFooter = false;
                pendingColSpec = null;
                pendingStripes = null;
                pendingGrid = null;
                pendingFrame = null;
                pendingFormat = null;
                pendingAdmonitionType = null;
                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;
                i = afterClosing; // advance past the closing delimiter line and callout list
                continue;
            }

            // Inline admonition: NOTE: text, TIP: text, etc.
            // Multi-line: continues until a blank line or structural element.
            if (paragraphLines.Count == 0 && TryParseInlineAdmonition(line, out var admonType, out var admonText))
            {
                // No FlushParagraph needed: paragraphLines.Count == 0 is already checked above.
                listFrames.Clear();
                dlFrames.Clear();
                int admonStartLine = lineNumber;

                // Gather continuation lines (non-blank, non-structural lines)
                var admonLines = new List<string> { admonText };
                while (i + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[i + 1]))
                {
                    var nextLine = lines[i + 1];
                    // Stop if the next line is a structural element
                    if (nextLine.StartsWith("= ") || nextLine.StartsWith("== ") || nextLine.StartsWith("=== ")
                        || nextLine.StartsWith("==== ") || nextLine.StartsWith("===== ")
                        || nextLine.StartsWith("* ") || nextLine.StartsWith(". ")
                        || nextLine.StartsWith("----") || nextLine.StartsWith("....", StringComparison.Ordinal)
                        || nextLine.StartsWith("|===")
                        || TryParseInlineAdmonition(nextLine, out _, out _))
                        break;
                    admonLines.Add(nextLine);
                    i++;
                }

                var fullText = string.Join("\n", admonLines);
                var admonNode = new AdmonitionNode
                {
                    AdmonitionType = admonType,
                    Title = pendingBlockTitle,
                    Text = fullText,
                    Inlines = InlineParser.Parse(fullText, EffectiveNormal(), document.Attributes),
                    Source = new SourceRange(new(admonStartLine, 1), new(i + 1, lines[i].Length)),
                };
                ApplyPendingId(admonNode, admonStartLine, line.Length);
                if (pendingBlockRoles is not null)
                    admonNode.Roles = pendingBlockRoles;
                currentContainer.AddChild(admonNode);
                pendingBlockTitle = null;
                pendingSourceLang = null;
                pendingHighlight = null;
                hasPendingSource = false;
                hasPendingVerse = false;
                hasPendingQuote = false;
                hasPendingListing = false;
                hasPendingLiteral = false;
                hasPendingExample = false;
                hasPendingSidebar = false;
                pendingCollapsible = false;
                pendingStem = null;
                pendingDlStyle = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;

                hasPendingOptionsHeader = false;
                hasPendingAutoWidth = false;
                hasPendingFooter = false;
                pendingColSpec = null;
                pendingStripes = null;
                pendingGrid = null;
                pendingFrame = null;
                pendingFormat = null;
                pendingAdmonitionType = null;
                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;
                continue;
            }

            // Description list item: term:: description (supports ::, :::, ::::)
            if (paragraphLines.Count == 0 && TryParseDescriptionItem(line, out var dlTerm, out var dlDesc, out int dlDepth))
            {
                // No FlushParagraph needed: paragraphLines.Count == 0 is already checked above.
                listFrames.Clear();

                // Pop deeper frames
                while (dlFrames.Count > 0 && dlFrames[^1].Depth > dlDepth)
                    dlFrames.RemoveAt(dlFrames.Count - 1);

                DescriptionListNode? dl;

                if (dlFrames.Count > 0 && dlFrames[^1].Depth == dlDepth)
                {
                    // Continue existing list at same depth
                    dl = dlFrames[^1].List;
                }
                else if (dlFrames.Count > 0 && dlFrames[^1].Depth < dlDepth)
                {
                    // Nest inside last item of parent frame
                    dl = new DescriptionListNode();
                    var parentItem = dlFrames[^1].LastItem;
                    if (parentItem is not null)
                        parentItem.AddChild(dl);
                    else
                        currentContainer.AddChild(dl);
                    dlFrames.Add((dl, dlDepth, null));
                }
                else
                {
                    // Fresh start (no frames or going back to root)
                    if (currentContainer.Children.Count > 0 &&
                        currentContainer.Children[^1] is DescriptionListNode existingDl)
                    {
                        dl = existingDl;
                        // Re-establish frame if needed
                        if (dlFrames.Count == 0)
                            dlFrames.Add((dl, dlDepth, null));
                    }
                    else
                    {
                        dl = new DescriptionListNode();
                        if (pendingDlStyle is not null)
                        {
                            dl.Style = pendingDlStyle;
                            pendingDlStyle = null;
                        }
                        ApplyPendingId(dl, lineNumber, line.Length);
                        currentContainer.AddChild(dl);
                        dlFrames.Add((dl, dlDepth, null));
                    }
                }

                // If description is empty, check if next lines are additional terms
                var allTerms = new List<string> { dlTerm };
                if (string.IsNullOrEmpty(dlDesc))
                {
                    int nextIdx = i + 1;
                    while (nextIdx < lines.Length
                        && TryParseDescriptionItem(lines[nextIdx], out var extraTerm, out var extraDesc, out int extraDepth)
                        && extraDepth == dlDepth
                        && string.IsNullOrEmpty(extraDesc))
                    {
                        allTerms.Add(extraTerm);
                        nextIdx++;
                    }
                    if (allTerms.Count > 1)
                    {
                        // Check if the line after the last term-only line has a description
                        if (nextIdx < lines.Length
                            && TryParseDescriptionItem(lines[nextIdx], out var finalTerm, out var finalDesc, out int finalDepth)
                            && finalDepth == dlDepth
                            && !string.IsNullOrEmpty(finalDesc))
                        {
                            allTerms.Add(finalTerm);
                            dlDesc = finalDesc;
                            i = nextIdx;
                        }
                        else
                        {
                            i = nextIdx - 1;
                        }
                    }
                }

                // If description is empty, check next lines for indented continuation
                ListNode? nestedListForDlItem = null;
                if (string.IsNullOrEmpty(dlDesc))
                {
                    int nextIdx = i + 1;
                    // Check if the indented continuation lines are list items
                    if (nextIdx < lines.Length && lines[nextIdx].Length > 0
                        && (lines[nextIdx][0] == ' ' || lines[nextIdx][0] == '\t')
                        && TryParseListItem(lines[nextIdx].Trim(), out var nestedKind, out var nestedDepth, out var nestedText))
                    {
                        // Parse indented list items as a nested list
                        nestedListForDlItem = new ListNode { ListKind = nestedKind };
                        while (nextIdx < lines.Length && lines[nextIdx].Length > 0
                            && (lines[nextIdx][0] == ' ' || lines[nextIdx][0] == '\t')
                            && !string.IsNullOrWhiteSpace(lines[nextIdx]))
                        {
                            var trimmedLine = lines[nextIdx].Trim();
                            if (TryParseListItem(trimmedLine, out _, out _, out var nestedItemText))
                            {
                                var nestedItem = new ListItemNode
                                {
                                    Text = nestedItemText,
                                    Inlines = InlineParser.Parse(nestedItemText, EffectiveNormal(), document.Attributes),
                                    Source = new SourceRange(new(nextIdx + 1, 1), new(nextIdx + 1, lines[nextIdx].Length)),
                                };
                                nestedListForDlItem.AddChild(nestedItem);
                            }
                            else
                            {
                                // Continuation line for the last list item
                                if (nestedListForDlItem.Children.Count > 0 &&
                                    nestedListForDlItem.Children[^1] is ListItemNode lastNestedItem)
                                {
                                    lastNestedItem.Text += "\n" + trimmedLine;
                                    lastNestedItem.Inlines = InlineParser.Parse(lastNestedItem.Text, EffectiveNormal(), document.Attributes);
                                }
                            }
                            nextIdx++;
                        }
                        i = nextIdx - 1;
                    }
                    else
                    {
                        var descLines = new List<string>();
                        // First try indented continuation
                        while (nextIdx < lines.Length && lines[nextIdx].Length > 0
                            && (lines[nextIdx][0] == ' ' || lines[nextIdx][0] == '\t')
                            && !string.IsNullOrWhiteSpace(lines[nextIdx]))
                        {
                            descLines.Add(lines[nextIdx].Trim());
                            nextIdx++;
                        }
                        // If no indented continuation found, try non-indented next line
                        // as description (AsciiDoc allows Term::\nDescription on next line)
                        if (descLines.Count == 0 && nextIdx < lines.Length
                            && !string.IsNullOrWhiteSpace(lines[nextIdx])
                            && lines[nextIdx].Trim() != "+"
                            && !TryParseDescriptionItem(lines[nextIdx], out _, out _, out _)
                            && !IsSectionHeader(lines[nextIdx])
                            && !TryParseListItem(lines[nextIdx], out _, out _, out _)
                            && !IsDelimitedBlockBoundary(lines[nextIdx])
                            && !(lines[nextIdx].Length > 1 && lines[nextIdx][0] == '[' && lines[nextIdx][^1] == ']'))
                        {
                            descLines.Add(lines[nextIdx].Trim());
                            nextIdx++;
                            // Continue consuming non-blank, non-structural lines
                            while (nextIdx < lines.Length
                                && !string.IsNullOrWhiteSpace(lines[nextIdx])
                                && lines[nextIdx].Trim() != "+"
                                && !TryParseDescriptionItem(lines[nextIdx], out _, out _, out _)
                                && !IsSectionHeader(lines[nextIdx])
                                && !TryParseListItem(lines[nextIdx], out _, out _, out _)
                                && !IsDelimitedBlockBoundary(lines[nextIdx])
                                && !(lines[nextIdx].Length > 1 && lines[nextIdx][0] == '[' && lines[nextIdx][^1] == ']'))
                            {
                                descLines.Add(lines[nextIdx].Trim());
                                nextIdx++;
                            }
                        }
                        if (descLines.Count > 0)
                        {
                            dlDesc = string.Join("\n", descLines);
                            i = nextIdx - 1; // -1 because the for loop will increment
                        }
                    }
                }

                var item = new DescriptionItemNode
                {
                    Terms = allTerms,
                    Description = dlDesc,
                    TermInlines = InlineParser.Parse(allTerms[0], EffectiveNormal(), document.Attributes),
                    AllTermInlines = allTerms.Count > 1
                        ? allTerms.Select(t => (IReadOnlyList<InlineNode>)InlineParser.Parse(t, EffectiveNormal(), document.Attributes)).ToList()
                        : null,
                    DescriptionInlines = InlineParser.Parse(dlDesc, EffectiveNormal(), document.Attributes),
                    Source = new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length)),
                };
                if (nestedListForDlItem is not null)
                    item.AddChild(nestedListForDlItem);
                dl.AddChild(item);
                dl.Source = new SourceRange(
                    dl.Source.IsNone ? item.Source.Start : dl.Source.Start,
                    item.Source.End);

                // Update last item at this depth
                if (dlFrames.Count > 0)
                    dlFrames[^1] = (dlFrames[^1].List, dlFrames[^1].Depth, item);

                pendingBlockTitle = null;
                pendingSourceLang = null;
                pendingHighlight = null;
                hasPendingSource = false;
                hasPendingVerse = false;
                hasPendingQuote = false;
                hasPendingListing = false;
                hasPendingLiteral = false;
                hasPendingExample = false;
                hasPendingSidebar = false;
                pendingCollapsible = false;
                pendingStem = null;
                pendingDlStyle = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;

                hasPendingOptionsHeader = false;
                hasPendingAutoWidth = false;
                hasPendingFooter = false;
                pendingColSpec = null;
                pendingStripes = null;
                pendingGrid = null;
                pendingFrame = null;
                pendingFormat = null;
                pendingAdmonitionType = null;
                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;
                continue;
            }

            // Bibliography entry: - [[[id]]] text or - [[[id,label]]] text
            if (inBibliographySection && TryParseBibliographyEntry(line, out var bibRefId, out var bibLabel, out var bibText))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                listFrames.Clear();
                dlFrames.Clear();
                var bibEntry = new BibliographyEntryNode
                {
                    RefId   = bibRefId,
                    Label   = bibLabel,
                    Text    = bibText,
                    Inlines = InlineParser.Parse(bibText, EffectiveNormal(), document.Attributes),
                    Source  = new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length)),
                };
                currentContainer.AddChild(bibEntry);
                pendingBlockTitle = null;
                pendingSourceLang = null;
                pendingHighlight = null;
                hasPendingSource = false;
                hasPendingVerse = false;
                hasPendingQuote = false;
                hasPendingListing = false;
                hasPendingLiteral = false;
                hasPendingExample = false;
                hasPendingSidebar = false;
                pendingCollapsible = false;
                pendingStem = null;
                pendingDlStyle = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;

                hasPendingOptionsHeader = false;
                hasPendingAutoWidth = false;
                hasPendingFooter = false;
                pendingColSpec = null;
                pendingStripes = null;
                pendingGrid = null;
                pendingFrame = null;
                pendingFormat = null;
                pendingAdmonitionType = null;
                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;
                continue;
            }

            // List continuation: + (on its own) attaches the next block to the last list item.
            if (line.Trim() == "+" && listFrames.Count > 0 && listFrames[^1].LastItem is not null)
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                // The next non-blank content belongs to the current list item.
                // Peek ahead to collect continuation blocks.
                var lastItem = listFrames[^1].LastItem!;
                int j = i + 1;
                // Skip blank lines after +
                while (j < lines.Length && string.IsNullOrWhiteSpace(lines[j])) j++;
                if (j < lines.Length)
                {
                    var nextLine = lines[j];

                    // Check for [source,lang] attribute before delimiter
                    string? contLang = pendingSourceLang;
                    string? contHighlight = pendingHighlight;
                    bool contIsSource = hasPendingSource;
                    if (TryParseSourceAttribute(nextLine, out var contSourceLang, out _, out _, out var contSourceHighlight))
                    {
                        contLang = contSourceLang;
                        contHighlight = contSourceHighlight;
                        contIsSource = true;
                        j++;
                        // Skip blank lines after [source]
                        while (j < lines.Length && string.IsNullOrWhiteSpace(lines[j])) j++;
                        if (j < lines.Length) nextLine = lines[j];
                    }

                    // Check if it's a delimited block
                    if (TryGetDelimiterKind(nextLine, out var contDelimChar, out var contDelimKind))
                    {
                        int contClosingIdx = -1;
                        for (int k = j + 1; k < lines.Length; k++)
                        {
                            if (IsDelimiterLine(lines[k], contDelimChar))
                            {
                                contClosingIdx = k;
                                break;
                            }
                        }
                        if (contClosingIdx > j)
                        {
                            // Promote listing to source if pending
                            if (contDelimKind == DelimitedBlockKind.Listing && contIsSource)
                                contDelimKind = DelimitedBlockKind.Source;

                            var contContentLines = lines[(j + 1)..contClosingIdx];
                            string contRawContent;
                            bool contIsVerbatim = contDelimKind is DelimitedBlockKind.Source
                                or DelimitedBlockKind.Listing or DelimitedBlockKind.Literal;
                            if (contIsVerbatim && contDelimKind is DelimitedBlockKind.Source or DelimitedBlockKind.Listing)
                            {
                                var stripped = new string[contContentLines.Length];
                                for (int cl = 0; cl < contContentLines.Length; cl++)
                                    stripped[cl] = StripCalloutMarker(contContentLines[cl]);
                                contRawContent = string.Join("\n", stripped);
                            }
                            else
                            {
                                contRawContent = string.Join("\n", contContentLines);
                            }

                            var contBlock = new DelimitedBlockNode
                            {
                                BlockKind = contDelimKind,
                                Content = contRawContent,
                                Language = contLang,
                                Highlight = contDelimKind == DelimitedBlockKind.Source ? contHighlight : null,
                                Source = new SourceRange(new(j + 1, 1), new(contClosingIdx + 1, lines[contClosingIdx].Length)),
                            };
                            lastItem.AddChild(contBlock);
                            i = contClosingIdx;
                            pendingSourceLang = null;
                            pendingHighlight = null;
                            hasPendingSource = false;
                            hasPendingVerse = false;
                            hasPendingQuote = false;
                            hasPendingListing = false;
                            hasPendingLiteral = false;
                            hasPendingExample = false;
                            hasPendingSidebar = false;
                            pendingQuoteAttribution = null;
                            pendingQuoteCitation = null;
            
                            continue;
                        }
                    }
                    else if (TryParseInlineAdmonition(nextLine, out var contAdmonType, out var contAdmonText))
                    {
                        // Admonition continuation (NOTE:, TIP:, etc.)
                        var admonLines = new List<string> { contAdmonText };
                        int aj = j + 1;
                        while (aj < lines.Length && !string.IsNullOrWhiteSpace(lines[aj])
                            && !TryParseInlineAdmonition(lines[aj], out _, out _)
                            && !TryParseListItem(lines[aj], out _, out _, out _)
                            && lines[aj].Trim() != "+")
                        {
                            admonLines.Add(lines[aj]);
                            aj++;
                        }
                        var fullText = string.Join("\n", admonLines);
                        var contAdmon = new AdmonitionNode
                        {
                            AdmonitionType = contAdmonType,
                            Text = fullText,
                            Inlines = InlineParser.Parse(fullText, EffectiveNormal(), document.Attributes),
                            Source = new SourceRange(new(j + 1, 1), new(aj, lines[aj - 1].Length)),
                        };
                        lastItem.AddChild(contAdmon);
                        i = aj - 1;
                        continue;
                    }
                    else if (IsOpenBlockDelimiter(nextLine))
                    {
                        // Open block continuation
                        int contClosingIdx = -1;
                        for (int k = j + 1; k < lines.Length; k++)
                        {
                            if (IsOpenBlockDelimiter(lines[k]))
                            {
                                contClosingIdx = k;
                                break;
                            }
                        }
                        if (contClosingIdx > j)
                        {
                            // Parse open block content as paragraphs inside a DelimitedBlockNode
                            var openBlock = new DelimitedBlockNode
                            {
                                BlockKind = DelimitedBlockKind.Open,
                            };
                            var openContent = lines[(j + 1)..contClosingIdx];
                            var currentPara = new List<string>();
                            foreach (var openLine in openContent)
                            {
                                if (string.IsNullOrWhiteSpace(openLine))
                                {
                                    if (currentPara.Count > 0)
                                    {
                                        var pText = string.Join("\n", currentPara);
                                        openBlock.AddChild(new ParagraphNode
                                        {
                                            Text = pText,
                                            Inlines = InlineParser.Parse(pText, EffectiveNormal(), document.Attributes),
                                        });
                                        currentPara.Clear();
                                    }
                                }
                                else
                                {
                                    currentPara.Add(openLine);
                                }
                            }
                            if (currentPara.Count > 0)
                            {
                                var pText = string.Join("\n", currentPara);
                                openBlock.AddChild(new ParagraphNode
                                {
                                    Text = pText,
                                    Inlines = InlineParser.Parse(pText, EffectiveNormal(), document.Attributes),
                                });
                            }
                            lastItem.AddChild(openBlock);
                            i = contClosingIdx;
                            continue;
                        }
                    }
                    else
                    {
                        // Non-delimited continuation: collect paragraph lines.
                        var contParagraphLines = new List<string>();
                        while (j < lines.Length && !string.IsNullOrWhiteSpace(lines[j])
                            && !TryParseListItem(lines[j], out _, out _, out _)
                            && lines[j].Trim() != "+")
                        {
                            contParagraphLines.Add(lines[j]);
                            j++;
                        }
                        if (contParagraphLines.Count > 0)
                        {
                            var contText = string.Join("\n", contParagraphLines);
                            var contPara = new ParagraphNode
                            {
                                Text = contText,
                                Inlines = InlineParser.Parse(contText, EffectiveNormal(), document.Attributes),
                            };
                            lastItem.AddChild(contPara);
                            i = j - 1; // position at last consumed line
                            continue;
                        }
                    }
                }
                continue;
            }

            // List continuation for description list items: + on its own attaches the next block to the last description item.
            if (line.Trim() == "+" && dlFrames.Count > 0 && dlFrames[^1].LastItem is not null)
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                var lastDlItem = dlFrames[^1].LastItem!;
                // If the last description item has a nested list, attach continuation
                // to the last item of that nested list instead of the description item.
                AstNode continuationTarget = lastDlItem;
                if (lastDlItem.Children.Count > 0 &&
                    lastDlItem.Children[^1] is ListNode nestedListInDl &&
                    nestedListInDl.Children.Count > 0 &&
                    nestedListInDl.Children[^1] is ListItemNode lastNestedListItem)
                {
                    continuationTarget = lastNestedListItem;
                }
                int j = i + 1;
                // Skip blank lines after +
                while (j < lines.Length && string.IsNullOrWhiteSpace(lines[j])) j++;
                if (j < lines.Length)
                {
                    var nextLine = lines[j];

                    // Check for [source,lang] attribute before delimiter
                    string? contLang = pendingSourceLang;
                    string? contHighlight = pendingHighlight;
                    bool contIsSource = hasPendingSource;
                    if (TryParseSourceAttribute(nextLine, out var contSourceLang, out _, out _, out var contSourceHighlight))
                    {
                        contLang = contSourceLang;
                        contHighlight = contSourceHighlight;
                        contIsSource = true;
                        j++;
                        // Skip blank lines after [source]
                        while (j < lines.Length && string.IsNullOrWhiteSpace(lines[j])) j++;
                        if (j < lines.Length) nextLine = lines[j];
                    }

                    // Check if it's a delimited block
                    if (TryGetDelimiterKind(nextLine, out var contDelimChar, out var contDelimKind))
                    {
                        int contClosingIdx = -1;
                        for (int k = j + 1; k < lines.Length; k++)
                        {
                            if (IsDelimiterLine(lines[k], contDelimChar))
                            {
                                contClosingIdx = k;
                                break;
                            }
                        }
                        if (contClosingIdx > j)
                        {
                            if (contDelimKind == DelimitedBlockKind.Listing && contIsSource)
                                contDelimKind = DelimitedBlockKind.Source;

                            var contContentLines = lines[(j + 1)..contClosingIdx];
                            string contRawContent;
                            bool contIsVerbatim = contDelimKind is DelimitedBlockKind.Source
                                or DelimitedBlockKind.Listing or DelimitedBlockKind.Literal;
                            if (contIsVerbatim && contDelimKind is DelimitedBlockKind.Source or DelimitedBlockKind.Listing)
                            {
                                var stripped = new string[contContentLines.Length];
                                for (int cl = 0; cl < contContentLines.Length; cl++)
                                    stripped[cl] = StripCalloutMarker(contContentLines[cl]);
                                contRawContent = string.Join("\n", stripped);
                            }
                            else
                            {
                                contRawContent = string.Join("\n", contContentLines);
                            }

                            var contBlock = new DelimitedBlockNode
                            {
                                BlockKind = contDelimKind,
                                Content = contRawContent,
                                Language = contLang,
                                Highlight = contDelimKind == DelimitedBlockKind.Source ? contHighlight : null,
                                Source = new SourceRange(new(j + 1, 1), new(contClosingIdx + 1, lines[contClosingIdx].Length)),
                            };
                            continuationTarget.AddChild(contBlock);
                            i = contClosingIdx;
                            pendingSourceLang = null;
                            pendingHighlight = null;
                            hasPendingSource = false;
                            hasPendingVerse = false;
                            hasPendingQuote = false;
                            hasPendingListing = false;
                            hasPendingLiteral = false;
                            hasPendingExample = false;
                            hasPendingSidebar = false;
                            pendingQuoteAttribution = null;
                            pendingQuoteCitation = null;

                            continue;
                        }
                    }
                    // Check if it's an admonition (TIP:, NOTE:, etc.)
                    else if (TryParseInlineAdmonition(nextLine, out var contAdmonType, out var contAdmonText))
                    {
                        var admonLines = new List<string> { contAdmonText };
                        int aj = j + 1;
                        while (aj < lines.Length && !string.IsNullOrWhiteSpace(lines[aj])
                            && !TryParseInlineAdmonition(lines[aj], out _, out _)
                            && !TryParseDescriptionItem(lines[aj], out _, out _, out _)
                            && lines[aj].Trim() != "+")
                        {
                            admonLines.Add(lines[aj]);
                            aj++;
                        }
                        var fullText = string.Join("\n", admonLines);
                        var contAdmon = new AdmonitionNode
                        {
                            AdmonitionType = contAdmonType,
                            Text = fullText,
                            Inlines = InlineParser.Parse(fullText, EffectiveNormal(), document.Attributes),
                            Source = new SourceRange(new(j + 1, 1), new(aj, lines[aj - 1].Length)),
                        };
                        continuationTarget.AddChild(contAdmon);
                        i = aj - 1;
                        continue;
                    }
                    else
                    {
                        // Non-delimited continuation: collect paragraph lines.
                        var contParagraphLines = new List<string>();
                        while (j < lines.Length && !string.IsNullOrWhiteSpace(lines[j])
                            && !TryParseListItem(lines[j], out _, out _, out _)
                            && !TryParseDescriptionItem(lines[j], out _, out _, out _)
                            && lines[j].Trim() != "+")
                        {
                            contParagraphLines.Add(lines[j]);
                            j++;
                        }
                        if (contParagraphLines.Count > 0)
                        {
                            var contText = string.Join("\n", contParagraphLines);
                            var contPara = new ParagraphNode
                            {
                                Text = contText,
                                Inlines = InlineParser.Parse(contText, EffectiveNormal(), document.Attributes),
                            };
                            continuationTarget.AddChild(contPara);
                            i = j - 1;
                            continue;
                        }
                    }
                }
                continue;
            }

            // List item.
            if (TryParseListItem(line, out var listKind, out var listDepth, out var itemText))
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes);
                bool isNewList = listFrames.Count == 0;
                AddListItem(currentContainer, listFrames, listKind, listDepth, itemText, lineNumber, line.Length, document.Attributes, pendingListStart, pendingListStyle);
                if (isNewList && listFrames.Count > 0)
                    ApplyPendingId(listFrames[0].List, lineNumber, line.Length);
                pendingListStart = null;
                pendingListStyle = null;
                pendingBlockTitle = null;
                pendingSourceLang = null;
                pendingHighlight = null;
                hasPendingSource = false;
                hasPendingVerse = false;
                hasPendingQuote = false;
                hasPendingListing = false;
                hasPendingLiteral = false;
                hasPendingExample = false;
                hasPendingSidebar = false;
                pendingCollapsible = false;
                pendingStem = null;
                pendingDlStyle = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;

                hasPendingOptionsHeader = false;
                hasPendingAutoWidth = false;
                hasPendingFooter = false;
                pendingColSpec = null;
                pendingStripes = null;
                pendingGrid = null;
                pendingFrame = null;
                pendingFormat = null;
                pendingAdmonitionType = null;
                // pendingBlockId is consumed by ApplyPendingId above if a new list was created
                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;

                // Implicit list item continuation: collect subsequent non-blank, non-marker lines
                if (listFrames.Count > 0 && listFrames[^1].LastItem is not null)
                {
                    var lastItem = listFrames[^1].LastItem!;
                    var continuationLines = new List<string>();
                    int peekIdx = i + 1;
                    while (peekIdx < lines.Length)
                    {
                        var peekLine = lines[peekIdx];

                        // Stop at blank lines
                        if (string.IsNullOrWhiteSpace(peekLine)) break;

                        // Stop at list markers (* or .)
                        if (TryParseListItem(peekLine, out _, out _, out _)) break;

                        // Stop at block delimiters (----, ====, ____, ****, ....)
                        if (TryGetDelimiterKind(peekLine, out _, out _)) break;

                        // Stop at section headings (= ...)
                        if (peekLine.Length >= 3 && peekLine[0] == '=' && peekLine[1] == '=' && peekLine[2] == ' ') break;
                        if (peekLine.Length >= 4 && peekLine.StartsWith("=== ")) break;
                        if (peekLine.Length >= 5 && peekLine.StartsWith("==== ")) break;
                        if (peekLine.Length >= 6 && peekLine.StartsWith("===== ")) break;

                        // Stop at description list items (term::)
                        if (TryParseDescriptionItem(peekLine, out _, out _, out _)) break;

                        // Stop at attribute lines (:attr:)
                        if (peekLine.Length > 1 && peekLine[0] == ':' && peekLine[1] != ':') break;

                        // Stop at block titles (.Title)
                        if (TryParseBlockTitle(peekLine, out _)) break;

                        // Stop at list continuation (+)
                        if (peekLine.Trim() == "+") break;

                        // Stop at admonitions (NOTE:, TIP:, etc.)
                        if (peekLine.StartsWith("NOTE:") || peekLine.StartsWith("TIP:") ||
                            peekLine.StartsWith("IMPORTANT:") || peekLine.StartsWith("WARNING:") ||
                            peekLine.StartsWith("CAUTION:")) break;

                        // Stop at attribute entries ([source], [,lang], etc.)
                        if (peekLine.StartsWith("[") && peekLine.EndsWith("]")) break;

                        // This is a continuation line
                        continuationLines.Add(peekLine);
                        peekIdx++;
                    }

                    if (continuationLines.Count > 0)
                    {
                        var fullText = lastItem.Text + "\n" + string.Join("\n", continuationLines);
                        lastItem.Text = fullText;
                        lastItem.Inlines = InlineParser.Parse(fullText, EffectiveNormal(), document.Attributes);
                        i = peekIdx - 1; // advance main loop index
                    }
                }

                continue;
            }

            // Paragraph-style quote block: [quote, Author] followed by paragraph text (no ____ delimiters).
            if (pendingQuoteAttribution is not null && paragraphLines.Count == 0)
            {
                FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lineNumber - 1, document.Attributes, pendingBlockId, pendingBlockRoles, seenIds, diagnostics, pendingHardbreaks, pendingAbstract ? "abstract" : null, subsOverride: ResolvePendingSubs(EffectiveNormal()));
                listFrames.Clear();
                dlFrames.Clear();

                // Collect paragraph lines for the quote content.
                var quoteLines = new List<string> { line };
                int quoteStart = lineNumber;
                while (i + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[i + 1]))
                {
                    i++;
                    quoteLines.Add(lines[i]);
                }

                var quoteContent = string.Join("\n", quoteLines);
                var quoteBlock = new DelimitedBlockNode
                {
                    BlockKind = DelimitedBlockKind.Quote,
                    Content = quoteContent,
                    Title = pendingBlockTitle,
                    Attribution = pendingQuoteAttribution,
                    CitationSource = pendingQuoteCitation,
                    Substitutions = ResolvePendingSubs(EffectiveNormal()),
                    Source = new SourceRange(new(quoteStart, 1), new(quoteStart + quoteLines.Count - 1, quoteLines[^1].Length)),
                };
                ApplyPendingId(quoteBlock, quoteStart, line.Length);
                if (pendingBlockRoles is not null)
                    quoteBlock.Roles = pendingBlockRoles;
                currentContainer.AddChild(quoteBlock);

                pendingBlockTitle = null;
                pendingSourceLang = null;
                pendingHighlight = null;
                hasPendingSource = false;
                hasPendingVerse = false;
                hasPendingQuote = false;
                hasPendingListing = false;
                hasPendingLiteral = false;
                hasPendingExample = false;
                hasPendingSidebar = false;
                pendingCollapsible = false;
                pendingStem = null;
                pendingDlStyle = null;
                pendingQuoteAttribution = null;
                pendingQuoteCitation = null;
                hasPendingOptionsHeader = false;
                hasPendingAutoWidth = false;
                hasPendingFooter = false;
                pendingColSpec = null;
                pendingStripes = null;
                pendingGrid = null;
                pendingFrame = null;
                pendingFormat = null;
                pendingAdmonitionType = null;
                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                pendingSubs = null;
                pendingSubsIsIncremental = false;
                pendingSubsToAdd = SubstitutionKind.None;
                pendingSubsToRemove = SubstitutionKind.None;
                continue;
            }

            // Paragraph line — also terminates any open list context and pending block metadata.
            listFrames.Clear();
            pendingBlockTitle = null;
            pendingSourceLang = null;
            pendingHighlight = null;
            hasPendingSource = false;
            hasPendingVerse = false;
            hasPendingListing = false;
            hasPendingLiteral = false;
            hasPendingExample = false;
            hasPendingSidebar = false;
            pendingQuoteAttribution = null;
            pendingQuoteCitation = null;
            hasPendingOptionsHeader = false;
            hasPendingAutoWidth = false;
            hasPendingFooter = false;
            pendingColSpec = null;
            pendingStripes = null;
            pendingGrid = null;
            pendingFrame = null;
            pendingFormat = null;
            pendingAdmonitionType = null;
            // pendingSubs is intentionally preserved: [subs="..."] before a paragraph applies to that paragraph.
            // pendingBlockId is intentionally preserved: [[id]] before a paragraph applies to that paragraph.

            // Indented literal paragraph: when starting a new paragraph and the line
            // begins with at least one space, collect contiguous indented lines and
            // emit a literal block instead of a regular paragraph.
            if (paragraphLines.Count == 0 && line.Length > 0 && line[0] == ' ')
            {
                // No FlushParagraph needed: paragraphLines.Count == 0 is already checked above.

                var literalLines = new List<string> { line };
                int literalStart = lineNumber;
                while (i + 1 < lines.Length)
                {
                    var nextLine = lines[i + 1];
                    if (nextLine.Length > 0 && nextLine[0] == ' ')
                    {
                        i++;
                        literalLines.Add(nextLine);
                    }
                    else if (string.IsNullOrWhiteSpace(nextLine))
                    {
                        // blank line ends the literal paragraph
                        break;
                    }
                    else
                    {
                        // non-indented, non-blank line ends the literal paragraph
                        break;
                    }
                }

                // Strip common leading whitespace indent
                int commonIndent = int.MaxValue;
                foreach (var l in literalLines)
                {
                    int spaces = 0;
                    while (spaces < l.Length && l[spaces] == ' ')
                        spaces++;
                    if (spaces < commonIndent)
                        commonIndent = spaces;
                }
                if (commonIndent == int.MaxValue)
                    commonIndent = 0;

                var strippedLines = literalLines.Select(l => l.Substring(commonIndent));
                var literalContent = string.Join("\n", strippedLines);

                var literalBlock = new DelimitedBlockNode
                {
                    BlockKind = DelimitedBlockKind.Literal,
                    Content = literalContent,
                    Title = null,
                    Substitutions = SubstitutionKind.Verbatim,
                    Source = new SourceRange(new(literalStart, 1), new(literalStart + literalLines.Count - 1, literalLines[^1].Length)),
                };
                ApplyPendingId(literalBlock, literalStart, line.Length);
                if (pendingBlockRoles is not null)
                    literalBlock.Roles = pendingBlockRoles;
                currentContainer.AddChild(literalBlock);

                pendingBlockId = null;
                pendingBlockReftext = null;
                pendingBlockRoles = null;
                continue;
            }

            if (paragraphLines.Count == 0)
                paragraphStartLine = lineNumber;
            paragraphLines.Add(line);
        }

        FlushParagraph(currentContainer, paragraphLines, ref paragraphStartLine, lines.Length, document.Attributes, pendingBlockId, pendingBlockRoles, seenIds, diagnostics, pendingHardbreaks, pendingAbstract ? "abstract" : null, subsOverride: ResolvePendingSubs(EffectiveNormal()));
        pendingBlockId = null;
        pendingBlockReftext = null;
        pendingBlockRoles = null;

        // Generate TOC node when :toc: attribute is set.
        if (document.Attributes.ContainsKey("toc"))
        {
            var tocValue = document.Attributes["toc"];
            var placement = tocValue switch
            {
                "left" => TocPlacement.Left,
                "right" => TocPlacement.Right,
                "preamble" => TocPlacement.Preamble,
                "macro" => TocPlacement.Macro,
                _ => TocPlacement.Auto,
            };

            int.TryParse(document.Attributes.GetValueOrDefault("toclevels", "2"), out var tocLevels);
            if (tocLevels <= 0) tocLevels = 2;

            var sections = new List<SectionNode>();
            CollectSections(document.Children, sections, tocLevels);

            var entries = BuildTocEntries(sections, tocLevels, document.Attributes);

            var tocNode = new TocNode
            {
                Placement = placement,
                Entries = entries,
            };

            if (placement == TocPlacement.Macro)
            {
                // Find the toc::[] placeholder and replace it with the populated TocNode.
                bool replaced = false;
                for (int ci = 0; ci < document.Children.Count; ci++)
                {
                    if (document.Children[ci] is TocNode placeholder
                        && placeholder.Placement == TocPlacement.Macro
                        && placeholder.Entries.Count == 0)
                    {
                        document.RemoveChildAt(ci);
                        document.InsertChild(ci, tocNode);
                        replaced = true;
                        break;
                    }
                }
                // If no placeholder found, fall back to position 0.
                if (!replaced)
                    document.InsertChild(0, tocNode);
            }
            else
            {
                // Insert TOC as first child of document (before section content).
                document.InsertChild(0, tocNode);
            }
        }

        // Populate IndexNode entries by collecting all index terms from the document.
        PopulateIndexNodes(document);

        return new ParseResult(document, diagnostics);
    }

    /// <summary>
    /// Walks the document AST to collect all <see cref="IndexTermNode"/> and
    /// <see cref="IndexTermHiddenNode"/> terms, then populates any <see cref="IndexNode"/>
    /// with sorted, deduplicated entries grouped by first letter.
    /// </summary>
    private static void PopulateIndexNodes(DocumentNode document)
    {
        // Find all IndexNode blocks in the document tree.
        var indexNodes = new List<IndexNode>();
        CollectNodes(document, indexNodes);
        if (indexNodes.Count == 0) return;

        // Collect all index terms from the entire document tree.
        var terms = new List<IReadOnlyList<string>>();
        CollectIndexTerms(document, terms);

        // Build sorted, deduplicated entries.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<IndexEntry>();
        foreach (var termList in terms)
        {
            if (termList.Count == 0) continue;
            var primary = termList[0];
            if (!seen.Add(primary)) continue;

            var subTerms = termList.Count > 1
                ? termList.Skip(1).ToList()
                : (IReadOnlyList<string>)[];

            entries.Add(new IndexEntry { Term = primary, SubTerms = subTerms });
        }

        entries.Sort((a, b) => string.Compare(a.Term, b.Term, StringComparison.OrdinalIgnoreCase));

        // Assign entries to all IndexNode instances.
        foreach (var indexNode in indexNodes)
            indexNode.Entries = entries;
    }

    private static void CollectNodes<T>(AstNode node, List<T> results) where T : AstNode
    {
        if (node is T match)
            results.Add(match);
        foreach (var child in node.Children)
            CollectNodes(child, results);
    }

    private static void CollectIndexTerms(AstNode node, List<IReadOnlyList<string>> terms)
    {
        switch (node)
        {
            case IndexTermNode indexTerm:
                terms.Add(indexTerm.Terms);
                break;
            case IndexTermHiddenNode indexTermHidden:
                terms.Add(indexTermHidden.Terms);
                break;
        }

        // For inline nodes that are inside paragraphs/cells, we need to check inline children too.
        foreach (var child in node.Children)
            CollectIndexTerms(child, terms);

        // Also check inline content in paragraphs and table cells.
        if (node is ParagraphNode paragraph)
        {
            foreach (var inline in paragraph.Inlines)
                CollectIndexTerms(inline, terms);
        }
        else if (node is TableCellNode cell)
        {
            foreach (var inline in cell.Inlines)
                CollectIndexTerms(inline, terms);
        }
        else if (node is ListItemNode listItem)
        {
            foreach (var inline in listItem.Inlines)
                CollectIndexTerms(inline, terms);
        }
    }

    // ── TOC helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Recursively collects non-discrete sections from a nested tree into a flat
    /// list in document order, suitable for <see cref="BuildTocEntries"/>.
    /// </summary>
    private static void CollectSections(IReadOnlyList<AstNode> children, List<SectionNode> result, int maxLevel)
    {
        foreach (var child in children)
        {
            if (child is SectionNode section && !section.IsDiscrete && section.Level <= maxLevel)
            {
                result.Add(section);
                CollectSections(section.Children, result, maxLevel);
            }
        }
    }

    /// <summary>
    /// Builds a nested list of <see cref="TocEntry"/> from a flat list of non-discrete
    /// sections, respecting level hierarchy.
    /// </summary>
    private static List<TocEntry> BuildTocEntries(List<SectionNode> sections, int maxLevel, IReadOnlyDictionary<string, string>? attributes = null)
    {
        var root = new List<TocEntry>();
        // Stack tracks (level, children-list-of-that-level's-entry) so we can nest.
        var stack = new Stack<(int Level, List<TocEntry> ChildrenList)>();
        stack.Push((0, root));

        foreach (var section in sections)
        {
            var children = new List<TocEntry>();
            var entry = new TocEntry
            {
                Level = section.Level,
                Id = section.Id ?? GenerateSectionId(section.Title, attributes),
                Title = section.Title,
                Children = children,
            };

            // Pop stack until we find a parent level that is strictly less than the current section level.
            while (stack.Count > 1 && stack.Peek().Level >= section.Level)
                stack.Pop();

            stack.Peek().ChildrenList.Add(entry);
            stack.Push((section.Level, children));
        }

        return root;
    }

    // ── Delimited-block helpers ────────────────────────────────────────────────

    private static bool TryGetDelimiterKind(string line, out char delimChar, out DelimitedBlockKind kind)
    {
        if (IsDelimiterLine(line, '.')) { delimChar = '.'; kind = DelimitedBlockKind.Literal;  return true; }
        if (IsDelimiterLine(line, '-')) { delimChar = '-'; kind = DelimitedBlockKind.Listing;  return true; }
        if (IsDelimiterLine(line, '=')) { delimChar = '='; kind = DelimitedBlockKind.Example;  return true; }
        if (IsDelimiterLine(line, '_')) { delimChar = '_'; kind = DelimitedBlockKind.Quote;    return true; }
        if (IsDelimiterLine(line, '*')) { delimChar = '*'; kind = DelimitedBlockKind.Sidebar;  return true; }
        if (IsDelimiterLine(line, '+')) { delimChar = '+'; kind = DelimitedBlockKind.Passthrough; return true; }
        delimChar = default;
        kind = default;
        return false;
    }

    /// <summary>
    /// Detects a Markdown fenced code block opening: 3+ backticks optionally
    /// followed by a language identifier.
    /// </summary>
    private static bool TryParseFencedCodeOpening(string line, out string? language)
    {
        language = null;
        int len = line.Length;
        int backtickCount = 0;
        while (backtickCount < len && line[backtickCount] == '`')
            backtickCount++;
        if (backtickCount < 3)
            return false;

        // Everything after backticks is the optional language identifier.
        var rest = line[backtickCount..].Trim();
        language = rest.Length > 0 ? rest : null;
        return true;
    }

    /// <summary>
    /// Detects a fenced code block closing line: 3+ backticks with no language.
    /// </summary>
    private static bool IsClosingFence(string line)
    {
        int len = TextUtility.TrimmedEndLength(line);
        if (len < 3) return false;
        for (int i = 0; i < len; i++)
            if (line[i] != '`') return false;
        return true;
    }

    private static bool IsDelimiterLine(string line, char ch)
    {
        int len = TextUtility.TrimmedEndLength(line);
        if (len < 4) return false;
        for (int i = 0; i < len; i++)
            if (line[i] != ch) return false;
        return true;
    }

    /// <summary>
    /// Returns true when <paramref name="line"/> consists of 3 or more
    /// repetitions of <paramref name="ch"/> (ignoring trailing whitespace).
    /// Used for page breaks (<c>&lt;&lt;&lt;</c>) and thematic breaks (<c>'''</c>).
    /// </summary>
    private static bool IsBreakLine(string line, char ch)
    {
        int len = TextUtility.TrimmedEndLength(line);
        if (len < 3) return false;
        for (int i = 0; i < len; i++)
            if (line[i] != ch) return false;
        return true;
    }

    private static bool IsOpenBlockDelimiter(string line)
    {
        int len = TextUtility.TrimmedEndLength(line);
        return len == 2 && line[0] == '-' && line[1] == '-';
    }

    /// <summary>
    /// Detects a <c>$$</c> stem block delimiter line (exactly two dollar signs).
    /// </summary>
    private static bool IsStemDelimiterLine(string line)
    {
        int len = TextUtility.TrimmedEndLength(line);
        return len == 2 && line[0] == '$' && line[1] == '$';
    }

    private static readonly string[] SectionStyleNames =
        ["appendix", "glossary", "colophon", "dedication", "preface"];

    private static bool IsSectionStyleName(string style)
        => Array.Exists(SectionStyleNames, s => string.Equals(s, style, StringComparison.OrdinalIgnoreCase));

    private static bool TryParseBlockTitle(string line, out string title)
    {
        // .Title — dot followed by a non-space, non-dot character.
        // Excludes ordered-list markers (". item") and literal delimiters ("....").
        if (line.Length >= 2 && line[0] == '.' && line[1] != ' ' && line[1] != '.')
        {
            title = line[1..];
            return true;
        }
        title = string.Empty;
        return false;
    }

    private static bool TryParseSourceAttribute(string line, out string? language, out string? blockId, out List<string>? roles, out string? highlight)
    {
        blockId = null;
        roles = null;
        highlight = null;
        if (!line.EndsWith("]"))
        {
            language = null;
            return false;
        }

        // Shorthand: [,lang] is equivalent to [source,lang]
        if (line.StartsWith("[,") && line.Length > 3)
        {
            language = line[2..^1].Trim();
            return true;
        }

        if (!line.StartsWith("[source"))
        {
            language = null;
            return false;
        }

        // Strip the outer brackets
        var content = line[1..^1]; // "source" or "source,lang" or "source,lang,role=\"...\"" etc.

        // Extract #id suffix if present (only outside quoted values)
        int hashIdx = content.IndexOf('#');
        if (hashIdx >= 0 && !content[..hashIdx].Contains('"'))
        {
            blockId = content[(hashIdx + 1)..];
            content = content[..hashIdx];
        }

        // Extract role="..." named attribute if present
        int roleIdx = content.IndexOf("role=\"", StringComparison.Ordinal);
        if (roleIdx >= 0)
        {
            int valueStart = roleIdx + 6; // after role="
            int valueEnd = content.IndexOf('"', valueStart);
            if (valueEnd > valueStart)
            {
                var roleValue = content[valueStart..valueEnd];
                roles = new List<string>();
                foreach (var r in roleValue.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    roles.Add(r);
                // Remove the role portion and any preceding comma from content
                content = content[..roleIdx].TrimEnd(',').TrimEnd();
            }
        }

        // Extract highlight="..." named attribute if present
        int hlIdx = content.IndexOf("highlight=\"", StringComparison.Ordinal);
        if (hlIdx >= 0)
        {
            int valueStart = hlIdx + 11; // after highlight="
            int valueEnd = content.IndexOf('"', valueStart);
            if (valueEnd > valueStart)
            {
                highlight = content[valueStart..valueEnd];
                content = content[..hlIdx].TrimEnd(',').TrimEnd();
            }
        }

        if (content == "source")
        {
            language = null;
            return true;
        }
        if (content.StartsWith("source,"))
        {
            var langPart = content[7..];
            // Only take the first positional parameter (the language);
            // additional named attributes are already extracted above.
            int commaIdx = langPart.IndexOf(',');
            language = (commaIdx >= 0 ? langPart[..commaIdx] : langPart).Trim();
            return true;
        }
        language = null;
        return false;
    }

    // ── List helpers ───────────────────────────────────────────────────────────

    private static void AddListItem(
        AstNode currentContainer,
        List<(ListNode List, int Depth, ListItemNode? LastItem)> frames,
        ListKind kind,
        int depth,
        string text,
        int lineNumber,
        int lineLength,
        IReadOnlyDictionary<string, string> attributes,
        int? pendingStart = null,
        string? pendingListStyle = null)
    {
        // Pop any frames whose depth exceeds the current item's depth (going shallower).
        while (frames.Count > 0 && frames[^1].Depth > depth)
            frames.RemoveAt(frames.Count - 1);

        ListNode targetList;

        if (frames.Count > 0 && frames[^1].Depth == depth && frames[^1].List.ListKind == kind)
        {
            // Existing list at same depth and kind — continue adding to it.
            targetList = frames[^1].List;
        }
        else if (frames.Count > 0 && frames[^1].Depth == depth && frames[^1].List.ListKind != kind)
        {
            // Different kind at same depth.
            // Check if there's a frame further back with matching kind and depth
            // — if so, we're returning to a parent list (pop the nested frame).
            targetList = null!;
            bool foundParent = false;
            for (int i = frames.Count - 2; i >= 0; i--)
            {
                if (frames[i].Depth == depth && frames[i].List.ListKind == kind)
                {
                    // Pop frames down to and including the mismatched top frame
                    while (frames.Count > i + 1)
                        frames.RemoveAt(frames.Count - 1);
                    targetList = frames[^1].List;
                    foundParent = true;
                    break;
                }
                if (frames[i].Depth < depth)
                    break;
            }

            if (!foundParent)
            {
                // No parent with same kind: nest inside the last item (mixed lists).
                var parentItem = frames[^1].LastItem;
                if (parentItem is not null)
                {
                    targetList = new ListNode { ListKind = kind, Start = pendingStart, ListStyle = pendingListStyle };
                    parentItem.AddChild(targetList);
                    // Push the nested list frame at the same depth — the parent frame stays
                    // on the stack so we can return to it when the original kind resumes.
                    frames.Add((targetList, depth, null));

                    // Add the item directly and return early
                    var mixedItem = new ListItemNode
                    {
                        Text    = text,
                        Inlines = InlineParser.Parse(text, EffectiveNormalSubs(attributes), attributes),
                        Checked = null,
                        Source  = new SourceRange(new(lineNumber, 1), new(lineNumber, lineLength)),
                    };
                    targetList.AddChild(mixedItem);
                    targetList.Source = new SourceRange(mixedItem.Source.Start, mixedItem.Source.End);
                    frames[^1] = (targetList, depth, mixedItem);
                    return;
                }
                // No parent item: fall through to fresh start
                frames.RemoveAt(frames.Count - 1);
                targetList = new ListNode { ListKind = kind, Start = pendingStart, ListStyle = pendingListStyle };

                if (frames.Count == 0)
                    currentContainer.AddChild(targetList);
                else
                {
                    var fallbackParent = frames[^1].LastItem;
                    if (fallbackParent is not null)
                        fallbackParent.AddChild(targetList);
                    else
                        currentContainer.AddChild(targetList);
                }

                frames.Add((targetList, depth, null));
            }
        }
        else
        {
            // Need a new list: deeper nesting or fresh start.
            if (frames.Count > 0 && frames[^1].Depth == depth)
                frames.RemoveAt(frames.Count - 1);

            targetList = new ListNode { ListKind = kind, Start = pendingStart, ListStyle = pendingListStyle };

            if (frames.Count == 0)
                currentContainer.AddChild(targetList);
            else
            {
                var parentItem = frames[^1].LastItem;
                if (parentItem is not null)
                    parentItem.AddChild(targetList);
                else
                    currentContainer.AddChild(targetList); // graceful fallback: no parent item yet
            }

            frames.Add((targetList, depth, null));
        }

        // Detect checklist prefix: [x], [X], [ ], or [*]
        bool? checkedState = null;
        var itemText = text;
        if (kind == ListKind.Unordered && text.Length >= 3)
        {
            if (text.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase) || text.StartsWith("[*] "))
            {
                checkedState = true;
                itemText = text[4..];
            }
            else if (text.StartsWith("[ ] "))
            {
                checkedState = false;
                itemText = text[4..];
            }
        }

        var item = new ListItemNode
        {
            Text    = itemText,
            Inlines = InlineParser.Parse(itemText, EffectiveNormalSubs(attributes), attributes),
            Checked = checkedState,
            Source  = new SourceRange(new(lineNumber, 1), new(lineNumber, lineLength)),
        };
        targetList.AddChild(item);

        // Keep the list's source range updated so it spans its first to last direct item.
        targetList.Source = new SourceRange(
            targetList.Source.IsNone ? item.Source.Start : targetList.Source.Start,
            item.Source.End);

        // Record the last item added at this depth for potential nested-list attachment.
        frames[^1] = (frames[^1].List, frames[^1].Depth, item);
    }

    private static bool TryParseListItem(string line, out ListKind kind, out int depth, out string text)
    {
        if (line.Length < 2)
        {
            kind = default; depth = 0; text = string.Empty;
            return false;
        }

        char marker = line[0];
        if (marker != '*' && marker != '.')
        {
            kind = default; depth = 0; text = string.Empty;
            return false;
        }

        kind = marker == '*' ? ListKind.Unordered : ListKind.Ordered;

        int count = 0;
        while (count < line.Length && line[count] == marker) count++;

        if (count >= line.Length || line[count] != ' ')
        {
            kind = default; depth = 0; text = string.Empty;
            return false;
        }

        depth = count;
        text = line[(count + 1)..].Trim();
        return true;
    }

    // ── Paragraph helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the effective "normal" substitutions for static helper methods.
    /// Smart punctuation (PostReplacements) is enabled by default and can be disabled via :!smartquotes:.
    /// </summary>
    private static SubstitutionKind EffectiveNormalSubs(IReadOnlyDictionary<string, string> attributes) =>
        attributes.ContainsKey("smartquotes")
            ? SubstitutionKind.Normal
            : SubstitutionKind.Normal & ~SubstitutionKind.PostReplacements;

    private static void FlushParagraph(
        AstNode container,
        List<string> lines,
        ref int startLine,
        int endLine,
        IReadOnlyDictionary<string, string> attributes,
        string? blockId = null,
        List<string>? blockRoles = null,
        HashSet<string>? seenIds = null,
        List<Diagnostic>? diagnostics = null,
        bool hasHardbreaks = false,
        string? style = null,
        SubstitutionKind? subsOverride = null)
    {
        if (lines.Count == 0) return;

        var rawText = string.Join("\n", lines);

        // Per-line hard break: a trailing " +" on any line (except the last) forces <br>.
        bool hasPerLineBreak = rawText.Contains(" +\n", StringComparison.Ordinal);
        if (hasPerLineBreak)
            rawText = rawText.Replace(" +\n", "\n");

        var paragraph = new ParagraphNode
        {
            Text    = rawText,
            Style   = style,
            Inlines = InlineParser.Parse(rawText, subsOverride ?? EffectiveNormalSubs(attributes), attributes),
            Source  = new SourceRange(new(startLine, 1), new(endLine, lines[^1].Length)),
            HasHardbreaks = hasHardbreaks || hasPerLineBreak || attributes.ContainsKey("hardbreaks-option"),
        };
        if (blockId is not null)
        {
            if (seenIds is not null && diagnostics is not null && !seenIds.Add(blockId))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Warning,
                    $"Duplicate anchor ID '{blockId}'",
                    new SourceRange(new(startLine, 1), new(startLine, lines[0].Length))));
            }
            paragraph.Id = blockId;
        }
        if (blockRoles is not null)
            paragraph.Roles = blockRoles;
        container.AddChild(paragraph);

        lines.Clear();
        startLine = 0;
    }

    private static bool IsDocTitle(string line) =>
        (line.Length > 2 && line[0] == '=' && line[1] == ' ') ||
        (line.Length > 2 && line[0] == '#' && line[1] == ' ' && (line.Length < 3 || line[2] != '#'));

    private static bool TryParseAttribute(
        string line,
        int lineNumber,
        List<Diagnostic> diagnostics,
        out string? name,
        out string? value,
        bool allowFlagStyle = false)
    {
        // Expected: :name: value  (line[0] == ':' guaranteed by caller)
        var closeColon = line.IndexOf(':', 1);

        if (closeColon <= 1)  // no closing colon, or empty name "::"
        {
            // Flag-style attribute: :name (no trailing colon).
            // Asciidoctor accepts this in the header as a boolean flag (sets to empty string).
            if (allowFlagStyle && closeColon == -1 && line.Length > 1)
            {
                var potentialName = line[1..].TrimEnd();
                if (potentialName.Length > 0 && IsValidAttributeName(potentialName))
                {
                    name = potentialName;
                    value = string.Empty;
                    return true;
                }
            }
            name = null;
            value = null;
            diagnostics.Add(new Diagnostic(
                DiagnosticSeverity.Warning,
                $"Malformed attribute entry: '{line}'",
                new SourceRange(new(lineNumber, 1), new(lineNumber, line.Length))));
            return false;
        }

        // Asciidoctor requires a space after the closing colon if there is a value.
        // ":name: value" is valid, ":name:" (empty) is valid, ":name:value" is NOT.
        var afterColon = closeColon + 1;
        if (afterColon < line.Length && line[afterColon] != ' ')
        {
            name = null;
            value = null;
            return false;
        }

        name = line[1..closeColon];
        value = afterColon < line.Length
            ? line[afterColon..].Trim()
            : string.Empty;
        return true;
    }

    private static bool IsValidAttributeName(string name)
    {
        if (name.Length == 0 || (!char.IsLetter(name[0]) && name[0] != '_'))
            return false;
        foreach (var c in name)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
                return false;
        }
        return true;
    }

    private static int CountLeadingEquals(string line)
    {
        int count = 0;
        while (count < line.Length && line[count] == '=') count++;
        return count;
    }

    private static int CountLeadingHashes(string line)
    {
        int count = 0;
        while (count < line.Length && line[count] == '#') count++;
        return count;
    }

    // ── Table helpers ──────────────────────────────────────────────────────────

    private static bool IsTableDelimiter(string line)
    {
        int len = TextUtility.TrimmedEndLength(line);
        return len == 4 && line[0] == '|' && line[1] == '=' && line[2] == '=' && line[3] == '=';
    }

    private static bool IsNestedTableDelimiter(string line)
    {
        int len = TextUtility.TrimmedEndLength(line);
        return len == 4 && line[0] == '!' && line[1] == '=' && line[2] == '=' && line[3] == '=';
    }

    private static bool IsCsvTableDelimiter(string line)
    {
        int len = TextUtility.TrimmedEndLength(line);
        return len == 4 && line[0] == ',' && line[1] == '=' && line[2] == '=' && line[3] == '=';
    }

    private static bool IsDsvTableDelimiter(string line)
    {
        int len = TextUtility.TrimmedEndLength(line);
        return len == 4 && line[0] == ':' && line[1] == '=' && line[2] == '=' && line[3] == '=';
    }

    /// <summary>
    /// Splits a string by comma, respecting double- and single-quoted values.
    /// E.g., <c>options="autoplay,loop",width=640</c> splits into
    /// <c>["options=\"autoplay,loop\"", "width=640"]</c>.
    /// </summary>
    private static List<string> SplitQuoteAware(string text)
    {
        var parts = new List<string>();
        int start = 0;
        bool inDouble = false;
        bool inSingle = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"' && !inSingle) inDouble = !inDouble;
            else if (c == '\'' && !inDouble) inSingle = !inSingle;
            else if (c == ',' && !inDouble && !inSingle)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }
        parts.Add(text[start..]);
        return parts;
    }

    /// <summary>
    /// Splits a CSV line into cells, respecting quoted fields.
    /// Commas inside double quotes are not treated as separators.
    /// </summary>
    private static List<string> SplitCsvLine(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++; // skip escaped quote
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }

    /// <summary>
    /// Splits a line by a simple delimiter character (colon for DSV, tab for TSV).
    /// </summary>
    private static List<string> SplitDelimitedLine(string line, char delimiter)
    {
        var parts = line.Split(delimiter);
        var cells = new List<string>();
        foreach (var part in parts)
            cells.Add(part.Trim());
        return cells;
    }

    /// <summary>
    /// Parses CSV/DSV/TSV table content into a TableNode.
    /// Each non-blank line is a row. Cells are separated by the format's delimiter.
    /// </summary>
    private static TableNode ParseSeparatedTableContent(string[] lines, int startIdx, int endIdx, string format, bool hasHeader, bool isAutoWidth, bool hasFooter, string? colSpec, string? stripes, string? grid, string? frame, IReadOnlyDictionary<string, string> attributes)
    {
        var columns = colSpec is not null ? ParseColumnSpec(colSpec) : null;

        // Detect header-by-blank-line
        bool headerByBlankLine = false;
        if (!hasHeader)
        {
            bool foundFirstRow = false;
            for (int i = startIdx; i < endIdx; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    if (foundFirstRow)
                    {
                        headerByBlankLine = true;
                        break;
                    }
                    continue;
                }
                if (foundFirstRow)
                    break;
                foundFirstRow = true;
            }
        }

        bool effectiveHeader = hasHeader || headerByBlankLine;
        var table = new TableNode { HasHeader = effectiveHeader, IsAutoWidth = isAutoWidth, HasFooter = hasFooter, Columns = columns, Stripes = stripes, Grid = grid, Frame = frame };

        for (int i = startIdx; i < endIdx; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            List<string> cellTexts;
            if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
                cellTexts = SplitCsvLine(line);
            else if (string.Equals(format, "tsv", StringComparison.OrdinalIgnoreCase))
                cellTexts = SplitDelimitedLine(line, '\t');
            else // dsv
                cellTexts = SplitDelimitedLine(line, ':');

            if (cellTexts.Count == 0) continue;

            var row = new TableRowNode();
            foreach (var cellText in cellTexts)
            {
                var cell = new TableCellNode
                {
                    Text = cellText,
                    Inlines = InlineParser.Parse(cellText, EffectiveNormalSubs(attributes), attributes),
                };
                row.AddChild(cell);
            }
            table.AddChild(row);
        }

        if (table.Children.Count == 0 && effectiveHeader)
            table = new TableNode { HasHeader = false, IsAutoWidth = isAutoWidth, HasFooter = hasFooter, Columns = columns, Stripes = stripes, Grid = grid, Frame = frame };

        return table;
    }

    /// <summary>
    /// Parses the content lines between opening and closing |=== delimiters into a TableNode.
    /// Each non-blank line that contains | is treated as one table row.
    /// Cell text is split by | and inline-parsed.
    /// Supports cell span prefixes (2+|, .2+|, 2.3+|) and header detection by blank line.
    /// </summary>
    private static TableNode ParseTableContent(string[] lines, int startIdx, int endIdx, bool hasHeader, bool isAutoWidth, bool hasFooter, string? colSpec, string? stripes, string? grid, string? frame, string? format, IReadOnlyDictionary<string, string> attributes, char cellSeparator = '|')
    {
        // Parse column specifications if provided.
        var columns = colSpec is not null ? ParseColumnSpec(colSpec) : null;

        // Detect header-by-blank-line: if the first non-blank row is followed by a blank line
        // before any other content row, treat first row as header.
        bool headerByBlankLine = false;
        if (!hasHeader)
        {
            bool foundFirstRow = false;
            for (int i = startIdx; i < endIdx; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (foundFirstRow)
                    {
                        headerByBlankLine = true;
                        break;
                    }
                    continue;
                }
                if (!line.Contains(cellSeparator)) continue;
                if (foundFirstRow)
                    break; // second content row before blank line — no header
                foundFirstRow = true;
            }
        }

        bool effectiveHeader = hasHeader || headerByBlankLine;

        var table = new TableNode { HasHeader = effectiveHeader, IsAutoWidth = isAutoWidth, HasFooter = hasFooter, Columns = columns, Stripes = stripes, Grid = grid, Frame = frame };

        // Determine column count from colSpec if available.
        int colCount = columns?.Count ?? 0;

        // Collect all cells from all lines.
        var allCells = new List<CellInfo>();
        for (int i = startIdx; i < endIdx; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.Contains(cellSeparator)) continue;

            var cellInfos = ParseTableCellsWithSpans(line, cellSeparator);
            allCells.AddRange(cellInfos);
        }

        // When no cols attribute but a header row was detected, infer column count
        // from the header row. This matches Asciidoctor behavior where the first row
        // (followed by a blank line) defines the column count.
        if (colCount == 0 && effectiveHeader && allCells.Count > 0)
        {
            // Find how many cells are on the first content line (the header).
            int firstLineCount = 0;
            for (int i = startIdx; i < endIdx; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.Contains(cellSeparator)) continue;
                firstLineCount = ParseTableCellsWithSpans(line, cellSeparator).Count;
                break;
            }
            if (firstLineCount > 1)
                colCount = firstLineCount;
        }

        // Use column-aware row grouping when column count is known
        // and cell count doesn't already divide evenly into rows by line count.
        bool useColumnGrouping = colCount > 0 && allCells.Count > colCount;

        if (useColumnGrouping)
        {
            // Column-aware: distribute cells into rows based on column count,
            // accounting for column spans and row spans.
            // activeRowSpans[col] tracks how many more rows a cell at that column occupies.
            var activeRowSpans = new int[colCount];
            var currentRow = new TableRowNode();
            int colsUsed = 0;

            // Account for columns already occupied by rowspans from previous rows.
            for (int c = 0; c < colCount; c++)
            {
                if (activeRowSpans[c] > 0)
                    colsUsed++;
            }

            foreach (var info in allCells)
            {
                // Skip columns occupied by active rowspans.
                while (colsUsed < colCount && activeRowSpans[colsUsed] > 0)
                    colsUsed++;

                currentRow.AddChild(CreateTableCell(info, attributes));

                // Record rowspan for this cell's columns.
                if (info.RowSpan > 1)
                {
                    for (int c = colsUsed; c < colsUsed + info.ColSpan && c < colCount; c++)
                        activeRowSpans[c] = info.RowSpan;
                }

                colsUsed += info.ColSpan;
                if (colsUsed >= colCount)
                {
                    table.AddChild(currentRow);
                    currentRow = new TableRowNode();
                    // Decrement active rowspans for the next row.
                    // Do NOT pre-count occupied columns — the while loop at
                    // cell placement time handles skipping occupied slots.
                    colsUsed = 0;
                    for (int c = 0; c < colCount; c++)
                    {
                        if (activeRowSpans[c] > 0)
                            activeRowSpans[c]--;
                    }
                    // Pre-skip any leading columns occupied by rowspans.
                    while (colsUsed < colCount && activeRowSpans[colsUsed] > 0)
                        colsUsed++;
                }
            }
            if (currentRow.Children.Count > 0)
            {
                // Count columns not occupied by active rowspans — these must be
                // filled by cells in this row.  If the row has fewer cells than
                // available slots, it is incomplete and is dropped (matching
                // Asciidoctor's "dropping cells from incomplete row" behavior).
                int availableSlots = 0;
                for (int c = 0; c < colCount; c++)
                {
                    if (activeRowSpans[c] <= 0)
                        availableSlots++;
                }
                int actualCells = 0;
                foreach (var c in currentRow.Children)
                {
                    if (c is TableCellNode tc)
                        actualCells += tc.ColSpan;
                }
                if (actualCells >= availableSlots)
                    table.AddChild(currentRow);
            }
        }
        else
        {
            // Original behavior: each line is a row.
            for (int i = startIdx; i < endIdx; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.Contains(cellSeparator)) continue;

                var cellInfos = ParseTableCellsWithSpans(line, cellSeparator);
                if (cellInfos.Count == 0) continue;

                var row = new TableRowNode();
                foreach (var info in cellInfos)
                {
                    row.AddChild(CreateTableCell(info, attributes));
                }
                table.AddChild(row);
            }
        }

        // HasHeader only meaningful when there is at least one row.
        if (table.Children.Count == 0 && effectiveHeader)
            table = new TableNode { HasHeader = false, IsAutoWidth = isAutoWidth, HasFooter = hasFooter, Columns = columns, Stripes = stripes, Grid = grid, Frame = frame };

        return table;
    }

    /// <summary>
    /// Creates a <see cref="TableCellNode"/> from a <see cref="CellInfo"/>.
    /// When the cell style is <see cref="TableCellStyle.AsciiDoc"/>, the cell text is parsed
    /// as block-level AsciiDoc and the resulting blocks are added as children.
    /// </summary>
    private static TableCellNode CreateTableCell(CellInfo info, IReadOnlyDictionary<string, string> attributes)
    {
        if (info.ContentStyle == TableCellStyle.AsciiDoc)
        {
            var innerResult = BlockParser.Parse(info.Text.Trim(), attributes);
            var cellNode = new TableCellNode
            {
                Text = info.Text,
                Inlines = [],
                ColSpan = info.ColSpan,
                RowSpan = info.RowSpan,
                Alignment = info.Alignment,
                ContentStyle = TableCellStyle.AsciiDoc,
            };
            foreach (var child in innerResult.Document.Children)
                cellNode.AddChild(child);
            return cellNode;
        }

        return new TableCellNode
        {
            Text    = info.Text,
            Inlines = InlineParser.Parse(info.Text, EffectiveNormalSubs(attributes), attributes),
            ColSpan = info.ColSpan,
            RowSpan = info.RowSpan,
            Alignment = info.Alignment,
            ContentStyle = info.ContentStyle,
        };
    }

    /// <summary>
    /// Parses a [cols="..."] attribute value into a list of column specs.
    /// Supports: "3*" (3 equal columns), "1,2,3" (proportional), "&lt;1,&gt;2,^3" (with alignment).
    /// </summary>
    private static List<TableColumnSpec> ParseColumnSpec(string spec)
    {
        var columns = new List<TableColumnSpec>();

        // "N*" form: N equal columns
        if (spec.EndsWith('*'))
        {
            var countStr = spec[..^1];
            if (int.TryParse(countStr, out int count) && count > 0)
            {
                for (int i = 0; i < count; i++)
                    columns.Add(new TableColumnSpec { Width = 1 });
                return columns;
            }
        }

        // Comma-separated: each entry is optional-alignment + optional-width
        var parts = spec.Split(',');
        foreach (var part in parts)
        {
            var p = part.Trim();
            if (p.Length == 0) continue;

            var horizAlign = TableAlignment.Left;
            var vertAlign = TableVerticalAlignment.Top;
            int parseIdx = 0;

            // Horizontal alignment prefix
            if (parseIdx < p.Length && p[parseIdx] is '<' or '>' or '^')
            {
                horizAlign = p[parseIdx] switch
                {
                    '>' => TableAlignment.Right,
                    '^' => TableAlignment.Center,
                    _ => TableAlignment.Left,
                };
                parseIdx++;
            }

            // Vertical alignment prefix: .< .> .^
            if (parseIdx + 1 < p.Length && p[parseIdx] == '.' && p[parseIdx + 1] is '<' or '>' or '^')
            {
                vertAlign = p[parseIdx + 1] switch
                {
                    '>' => TableVerticalAlignment.Bottom,
                    '^' => TableVerticalAlignment.Middle,
                    _ => TableVerticalAlignment.Top,
                };
                parseIdx += 2;
            }

            var widthStr = p[parseIdx..];
            int width = 1;
            if (widthStr.Length > 0 && int.TryParse(widthStr, out int parsed) && parsed > 0)
                width = parsed;

            columns.Add(new TableColumnSpec { Width = width, Alignment = horizAlign, VerticalAlignment = vertAlign });
        }

        return columns;
    }

    /// <summary>
    /// Tries to parse a [cols="..."] attribute line, possibly combined with other
    /// attributes like <c>[cols="1,2,1", options="header"]</c>.
    /// Returns true if cols was found. Also extracts options if present.
    /// </summary>
    private static bool TryParseColsAttribute(string line, out string? colSpec, out string? options)
    {
        colSpec = null;
        options = null;

        if (!line.StartsWith("[", StringComparison.Ordinal) || !line.EndsWith("]", StringComparison.Ordinal))
            return false;

        var inner = line[1..^1]; // strip [ and ]

        // Extract cols="..." using a simple scan (handles commas inside quotes)
        colSpec = ExtractQuotedAttribute(inner, "cols");
        if (colSpec is null)
            return false;

        options = ExtractQuotedAttribute(inner, "options");
        return true;
    }

    /// <summary>
    /// Extracts the value of a named quoted attribute from an attribute list string.
    /// For example, from <c>cols="1,2,1", options="header"</c> with name <c>cols</c>,
    /// returns <c>1,2,1</c>.
    /// </summary>
    private static string? ExtractQuotedAttribute(string attributeList, string name)
    {
        var searchFor = name + "=\"";
        int idx = 0;
        while (idx < attributeList.Length)
        {
            int pos = attributeList.IndexOf(searchFor, idx, StringComparison.Ordinal);
            if (pos < 0)
                return null;

            // Ensure it's at start or preceded by whitespace/comma (not part of another attribute name)
            if (pos > 0)
            {
                char before = attributeList[pos - 1];
                if (before != ' ' && before != ',' && before != '\t')
                {
                    idx = pos + 1;
                    continue;
                }
            }

            int valueStart = pos + searchFor.Length;
            int closeQuote = attributeList.IndexOf('"', valueStart);
            if (closeQuote < 0)
                return null;

            return attributeList[valueStart..closeQuote];
        }

        return null;
    }

    // ── Block macro helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Tries to parse a block macro from a line: <c>name::target[content]</c>.
    /// Currently supports only <c>image::target[alt]</c>.
    /// Returns true and sets <paramref name="node"/> for known macros.
    /// When the line matches the macro pattern but the name is unrecognized,
    /// returns false and sets <paramref name="unknownMacroName"/>.
    /// </summary>
    private static bool TryParseBlockMacro(string line, out AstNode? node, out string? unknownMacroName, List<string>? pendingOptions = null)
    {
        node = null;
        unknownMacroName = null;

        // Must contain :: and []
        int doubleColon = line.IndexOf("::", StringComparison.Ordinal);
        if (doubleColon < 1) return false; // need at least one char for name

        int openBracket = line.IndexOf('[', doubleColon + 2);
        if (openBracket < 0) return false;

        // Closing bracket must be last char on line (trimmed).
        int trimmedLen = TextUtility.TrimmedEndLength(line);
        if (trimmedLen == 0 || line[trimmedLen - 1] != ']') return false;

        int closeBracket = trimmedLen - 1;
        if (closeBracket <= openBracket) return false;

        var macroName = line[..doubleColon];
        var target = line[(doubleColon + 2)..openBracket];
        var bracketContent = line[(openBracket + 1)..closeBracket];

        if (macroName == "toc")
        {
            // toc::[] is a placement marker for the TOC when :toc: macro is set.
            node = new TocNode { Placement = TocPlacement.Macro };
            return true;
        }

        if (macroName == "image")
        {
            var imgAttrs = ParseImageAttributes(bracketContent);
            // Asciidoctor maps align=center → text-center role and float=left → left role.
            var imgRoles = BuildImageAlignmentRoles(imgAttrs.Align, imgAttrs.Float);
            node = new BlockImageNode
            {
                Target = target,
                Alt = imgAttrs.Alt,
                Width = imgAttrs.Width,
                Height = imgAttrs.Height,
                Link = imgAttrs.Link,
                Roles = imgRoles,
            };
            return true;
        }

        if (macroName is "video" or "audio")
        {
            // Parse named attributes from bracket content using simple key=value splitting.
            // We don't use BlockAttributes.Parse here because it has shorthand parsing
            // (e.g., treating '.' in values like 'poster=thumb.png' as role markers).
            var namedAttrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var positionalArgs = new List<string>();
            var inlineOptions = new List<string>();
            if (bracketContent.Length > 0)
            {
                // Split by comma but respect quoted values (e.g., options="autoplay,loop")
                foreach (var part in SplitQuoteAware(bracketContent))
                {
                    var trimmed = part.Trim();
                    var eqIdx = trimmed.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        var key = trimmed[..eqIdx].Trim();
                        var val = trimmed[(eqIdx + 1)..].Trim();
                        // Strip surrounding quotes if present.
                        if (val.Length >= 2 && ((val[0] == '"' && val[^1] == '"') || (val[0] == '\'' && val[^1] == '\'')))
                            val = val[1..^1];
                        namedAttrs[key] = val;
                    }
                    else if (trimmed.Length > 0 && trimmed[0] == '%')
                    {
                        // Inline options: %autoplay%loop%controls
                        for (int oi = 0; oi < trimmed.Length; )
                        {
                            if (trimmed[oi] == '%')
                            {
                                oi++;
                                int start = oi;
                                while (oi < trimmed.Length && trimmed[oi] != '%') oi++;
                                if (oi > start)
                                    inlineOptions.Add(trimmed[start..oi]);
                            }
                            else oi++;
                        }
                    }
                    else if (trimmed.Length > 0)
                    {
                        positionalArgs.Add(trimmed);
                    }
                }
            }

            // Handle options="autoplay,loop" named attribute → split into inlineOptions
            if (namedAttrs.TryGetValue("options", out var optionsVal))
            {
                foreach (var opt in optionsVal.Split(','))
                {
                    var trimmedOpt = opt.Trim();
                    if (trimmedOpt.Length > 0)
                        inlineOptions.Add(trimmedOpt);
                }
            }

            bool HasOption(string name) =>
                (pendingOptions is not null && pendingOptions.Contains(name)) ||
                inlineOptions.Contains(name) ||
                positionalArgs.Exists(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

            if (macroName == "video")
            {
                // Detect youtube/vimeo as positional arg: video::id[youtube]
                string? provider = HasOption("youtube") ? "youtube"
                    : HasOption("vimeo") ? "vimeo" : null;
                node = new VideoNode
                {
                    Target = target,
                    Provider = provider,
                    Width = namedAttrs.GetValueOrDefault("width"),
                    Height = namedAttrs.GetValueOrDefault("height"),
                    Poster = namedAttrs.GetValueOrDefault("poster"),
                    Autoplay = HasOption("autoplay"),
                    Loop = HasOption("loop"),
                    Controls = !HasOption("nocontrols"),
                };
            }
            else
            {
                node = new AudioNode
                {
                    Target = target,
                    Width = namedAttrs.GetValueOrDefault("width"),
                    Autoplay = HasOption("autoplay"),
                    Loop = HasOption("loop"),
                    Controls = !HasOption("nocontrols"),
                };
            }

            return true;
        }

        if (macroName == "index")
        {
            node = new IndexNode();
            return true;
        }

        // include:: is a preprocessor directive, not a block macro.
        // Don't flag it as unknown — it's handled by IncludeExpander.
        if (macroName == "include")
            return false;

        // Pattern matched but macro name is unrecognized.
        unknownMacroName = macroName;
        return false;
    }

    /// <summary>
    /// Parses image macro bracket content into structured attributes.
    /// Supports positional (<c>image::target[alt,width,height]</c>) and named
    /// (<c>image::target[alt=text,width=200,link=url]</c>) forms.
    /// </summary>
    internal static ImageAttributes ParseImageAttributes(string bracketContent)
    {
        if (bracketContent.Length == 0)
            return new ImageAttributes { Alt = "" };

        int commaIdx = bracketContent.IndexOf(',');
        if (commaIdx < 0)
            return new ImageAttributes { Alt = bracketContent };

        string? alt = null, width = null, height = null, link = null, align = null, floatVal = null;
        var positional = new List<string>();

        foreach (var part in bracketContent.Split(','))
        {
            var trimmed = part.Trim();
            var eqIdx = trimmed.IndexOf('=');
            if (eqIdx > 0)
            {
                var key = trimmed[..eqIdx].Trim().ToLowerInvariant();
                var value = trimmed[(eqIdx + 1)..].Trim().Trim('"');
                switch (key)
                {
                    case "alt": alt = value; break;
                    case "width": width = value; break;
                    case "height": height = value; break;
                    case "link": link = value; break;
                    case "align": align = value; break;
                    case "float": floatVal = value; break;
                }
            }
            else
            {
                positional.Add(trimmed);
            }
        }

        // Positional order: alt (1st), width (2nd), height (3rd)
        alt ??= positional.Count > 0 ? positional[0] : "";
        width ??= positional.Count > 1 ? positional[1] : null;
        height ??= positional.Count > 2 ? positional[2] : null;

        return new ImageAttributes { Alt = alt, Width = width, Height = height, Link = link, Align = align, Float = floatVal };
    }

    /// <summary>Parsed image macro attributes.</summary>
    internal readonly struct ImageAttributes
    {
        public string Alt { get; init; }
        public string? Width { get; init; }
        public string? Height { get; init; }
        public string? Link { get; init; }
        public string? Align { get; init; }
        public string? Float { get; init; }
    }

    /// <summary>
    /// Builds the role list for an image's alignment/float attributes, matching
    /// Asciidoctor's mapping: <c>align=center</c> → <c>text-center</c>; <c>float=left</c> → <c>left</c>.
    /// </summary>
    internal static IReadOnlyList<string> BuildImageAlignmentRoles(string? align, string? floatVal)
    {
        if (string.IsNullOrEmpty(align) && string.IsNullOrEmpty(floatVal))
            return Array.Empty<string>();
        var roles = new List<string>(2);
        if (!string.IsNullOrEmpty(align))
            roles.Add("text-" + align!.Trim().ToLowerInvariant());
        if (!string.IsNullOrEmpty(floatVal))
            roles.Add(floatVal!.Trim().ToLowerInvariant());
        return roles;
    }

    /// <summary>
    /// Holds parsed cell info including span and alignment metadata.
    /// </summary>
    private readonly record struct CellInfo(string Text, int ColSpan, int RowSpan, TableAlignment? Alignment, TableCellStyle ContentStyle = TableCellStyle.Default);

    private static readonly Dictionary<char, TableCellStyle> CellStyleLetters = new()
    {
        ['a'] = TableCellStyle.AsciiDoc,
        ['e'] = TableCellStyle.Emphasis,
        ['h'] = TableCellStyle.Header,
        ['l'] = TableCellStyle.Literal,
        ['m'] = TableCellStyle.Monospace,
        ['s'] = TableCellStyle.Strong,
    };

    /// <summary>
    /// Splits a table row line by the cell separator into cell info values with span support.
    /// In AsciiDoc, span prefixes appear immediately before the separator:
    /// <c>2+|content</c> (colspan), <c>.2+|content</c> (rowspan), <c>2.3+|content</c> (both).
    /// For the first cell, the prefix is before the first separator.
    /// For subsequent cells, the prefix is between the previous cell content (whitespace) and the separator.
    /// </summary>
    private static List<CellInfo> ParseTableCellsWithSpans(string line, char separator = '|')
    {
        var cells = new List<CellInfo>();

        // Find all separator positions
        var pipePositions = new List<int>();
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == separator)
                pipePositions.Add(i);
        }

        if (pipePositions.Count == 0) return cells;

        // For each pipe, determine:
        // 1. Its span prefix (text immediately before the pipe that matches span pattern)
        // 2. The cell content (text after the pipe until the next cell boundary)

        // Collect span prefixes for each pipe
        var prefixes = new string?[pipePositions.Count];

        // First pipe: prefix is everything before it (trimmed)
        {
            var before = line.AsSpan(0, pipePositions[0]).Trim();
            if (before.Length > 0)
            {
                var beforeStr = before.ToString();
                if (IsValidSpanPrefix(beforeStr))
                    prefixes[0] = beforeStr;
                else if (before.Length == 1 && CellStyleLetters.ContainsKey(before[0]))
                    prefixes[0] = beforeStr;
                else if (before.Length >= 2 && CellStyleLetters.ContainsKey(before[^1]) && IsValidSpanPrefix(beforeStr[..^1]))
                    prefixes[0] = beforeStr;
            }
        }

        // Subsequent pipes: extract trailing span prefix from segment between previous pipe and this one
        for (int p = 1; p < pipePositions.Count; p++)
        {
            int segStart = pipePositions[p - 1] + 1;
            int segEnd = pipePositions[p];
            var seg = line.AsSpan(segStart, segEnd - segStart);
            prefixes[p] = ExtractTrailingSpanPrefix(seg);
        }

        // Build cells
        for (int p = 0; p < pipePositions.Count; p++)
        {
            int colSpan = 1;
            int rowSpan = 1;
            TableAlignment? alignment = null;
            var cellStyle = TableCellStyle.Default;

            if (prefixes[p] is not null)
            {
                var prefix = prefixes[p]!;
                // Check if the last character is a style letter
                if (prefix.Length >= 1 && CellStyleLetters.TryGetValue(prefix[^1], out var style))
                {
                    cellStyle = style;
                    var spanPart = prefix[..^1];
                    if (spanPart.Length > 0)
                        ParseSpanPrefix(spanPart, out colSpan, out rowSpan, out alignment);
                }
                else
                {
                    ParseSpanPrefix(prefix, out colSpan, out rowSpan, out alignment);
                }
            }

            // Cell content: from this pipe+1 to end-of-line or start of next cell's prefix+pipe
            int contentStart = pipePositions[p] + 1;
            int contentEnd;

            if (p + 1 < pipePositions.Count)
            {
                contentEnd = pipePositions[p + 1];
                // Remove the next cell's span prefix from end of this content
                if (prefixes[p + 1] is not null)
                    contentEnd -= prefixes[p + 1]!.Length;
            }
            else
            {
                contentEnd = line.Length;
            }

            if (contentEnd < contentStart)
                contentEnd = contentStart;

            var content = line.AsSpan(contentStart, contentEnd - contentStart).Trim();
            cells.Add(new CellInfo(content.ToString(), colSpan, rowSpan, alignment, cellStyle));
        }

        return cells;
    }

    /// <summary>
    /// Extracts a trailing cell specifier prefix from a segment between two pipes.
    /// Returns null if no prefix is found.
    /// A prefix matches patterns like: "2+", ".3+", "2.3+", or a single style letter (a,e,h,l,m),
    /// or a span prefix followed by a style letter (e.g. "2+e"), preceded by whitespace.
    /// </summary>
#if NET10_0_OR_GREATER
    private static string? ExtractTrailingSpanPrefix(ReadOnlySpan<char> segment)
    {
        var trimmed = segment.TrimEnd();
#else
    private static string? ExtractTrailingSpanPrefix(string segment)
    {
        var trimmed = segment.TrimEnd();
#endif
        if (trimmed.Length == 0)
            return null;

        // Check if the last character is a style letter
        bool endsWithStyle = CellStyleLetters.ContainsKey(trimmed[^1]);

        // A single style letter preceded by whitespace or start of segment
        if (endsWithStyle && trimmed.Length == 1)
            return trimmed.ToString();
        if (endsWithStyle && trimmed.Length > 1 && char.IsWhiteSpace(trimmed[^2]))
            return trimmed[^1..].ToString();

        // Style letter preceded by a span prefix (ending in +)
        if (endsWithStyle && trimmed.Length >= 3 && trimmed[^2] == '+')
        {
            // Find start of the span prefix by scanning backwards from the '+'
            int prefixStart = trimmed.Length - 2; // position of '+'
            while (prefixStart > 0)
            {
                char c = trimmed[prefixStart - 1];
                if (char.IsDigit(c) || c == '.' || c == '<' || c == '>' || c == '^')
                    prefixStart--;
                else
                    break;
            }

            if (prefixStart > 0 && !char.IsWhiteSpace(trimmed[prefixStart - 1]))
                goto checkSpanOnly;

            var candidate = trimmed[prefixStart..].ToString();
            // Validate: everything except the last char should be a valid span prefix
            if (IsValidSpanPrefix(candidate[..^1]))
                return candidate;
        }

        checkSpanOnly:
        // Original logic: span prefix ending in '+'
        if (trimmed[^1] != '+')
            return null;

        // Find start of the prefix by scanning backwards
        {
            int prefixStart = trimmed.Length - 1;
            while (prefixStart > 0)
            {
                char c = trimmed[prefixStart - 1];
                if (char.IsDigit(c) || c == '.' || c == '<' || c == '>' || c == '^')
                    prefixStart--;
                else
                    break;
            }

            // The prefix must be preceded by whitespace (or be the entire segment)
            if (prefixStart > 0 && !char.IsWhiteSpace(trimmed[prefixStart - 1]))
                return null;

            var candidate = trimmed[prefixStart..].ToString();
            if (IsValidSpanPrefix(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// Validates that a string is a valid span prefix: N+, .N+, N.M+.
    /// </summary>
    private static bool IsValidSpanPrefix(string s)
    {
        if (s.Length < 2 || s[^1] != '+') return false;
        var body = s.AsSpan(0, s.Length - 1); // remove trailing +
        int pos = 0;

        // First part: optional digits (colspan)
        bool hasColDigits = false;
        while (pos < body.Length && char.IsDigit(body[pos]))
        {
            hasColDigits = true;
            pos++;
        }

        // Optional dot + digits (rowspan)
        bool hasDot = false;
        if (pos < body.Length && body[pos] == '.')
        {
            hasDot = true;
            pos++;
            bool hasRowDigits = false;
            while (pos < body.Length && char.IsDigit(body[pos]))
            {
                hasRowDigits = true;
                pos++;
            }
            if (!hasRowDigits) return false;
        }

        // Must have consumed everything
        if (pos != body.Length) return false;

        // Must have at least colspan or rowspan
        return hasColDigits || hasDot;
    }

    /// <summary>
    /// Parses a span prefix string into ColSpan, RowSpan, and Alignment values.
    /// Formats: "2+" (col=2), ".3+" (row=3), "2.3+" (col=2,row=3).
    /// </summary>
    private static void ParseSpanPrefix(string prefix, out int colSpan, out int rowSpan, out TableAlignment? alignment)
    {
        colSpan = 1;
        rowSpan = 1;
        alignment = null;

        if (prefix.Length < 2 || prefix[^1] != '+') return;
        var body = prefix.AsSpan(0, prefix.Length - 1);
        int pos = 0;

        // Parse optional colspan digits
        int colStart = pos;
        while (pos < body.Length && char.IsDigit(body[pos]))
            pos++;
        if (pos > colStart && int.TryParse(body[colStart..pos], out int cs) && cs > 0)
            colSpan = cs;

        // Parse optional .rowspan
        if (pos < body.Length && body[pos] == '.')
        {
            pos++;
            int rowStart = pos;
            while (pos < body.Length && char.IsDigit(body[pos]))
                pos++;
            if (pos > rowStart && int.TryParse(body[rowStart..pos], out int rs) && rs > 0)
                rowSpan = rs;
        }
    }

    // ── Admonition helpers ────────────────────────────────────────────────

    private static readonly string[] AdmonitionTypes =
        ["NOTE", "TIP", "IMPORTANT", "WARNING", "CAUTION"];

    /// <summary>
    /// Tries to parse an admonition attribute line: [NOTE], [TIP], etc.
    /// These precede an ==== block to create a block admonition.
    /// </summary>
    private static bool TryParseAdmonitionAttribute(string line, out string type)
    {
        type = string.Empty;
        if (line.Length < 3 || line[0] != '[' || line[^1] != ']') return false;
        var inner = line[1..^1];
        foreach (var t in AdmonitionTypes)
        {
            if (inner == t)
            {
                type = t;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Tries to parse an inline admonition: NOTE: text, TIP: text, etc.
    /// </summary>
    private static bool TryParseInlineAdmonition(string line, out string type, out string text)
    {
        type = string.Empty;
        text = string.Empty;
        foreach (var t in AdmonitionTypes)
        {
            if (line.Length > t.Length + 2 && line.StartsWith(t) && line[t.Length] == ':' && line[t.Length + 1] == ' ')
            {
                type = t;
                text = line[(t.Length + 2)..].Trim();
                return true;
            }
        }
        return false;
    }

    // ── Attribute unset / counter helpers ───────────────────────────────────

    /// <summary>
    /// Tries to parse an attribute unset line: <c>:!name:</c> or <c>:name!:</c>.
    /// </summary>
    private static bool TryParseAttributeUnset(string line, out string? name)
    {
        name = null;
        if (line.Length < 4 || line[0] != ':') return false;

        // :!name: form
        if (line[1] == '!')
        {
            int closeColon = line.IndexOf(':', 2);
            if (closeColon <= 2) return false;
            // Must end at the closing colon (with optional trailing whitespace).
            var afterClose = line[(closeColon + 1)..].Trim();
            if (afterClose.Length > 0) return false;
            name = line[2..closeColon];
            return true;
        }

        // :name!: form
        int bangIdx = line.IndexOf('!', 1);
        if (bangIdx < 2) return false;
        if (bangIdx + 1 >= line.Length || line[bangIdx + 1] != ':') return false;
        // Must end at the closing colon (with optional trailing whitespace).
        var afterClose2 = line[(bangIdx + 2)..].Trim();
        if (afterClose2.Length > 0) return false;
        name = line[1..bangIdx];
        return true;
    }

    /// <summary>
    /// Tries to parse a counter declaration: <c>:counter:name:</c> or <c>:counter:name:N</c>.
    /// </summary>
    private static bool TryParseCounter(string line, out string? name, out int? seed)
    {
        name = null;
        seed = null;
        const string prefix = ":counter:";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return false;
        if (line.Length <= prefix.Length) return false;

        var rest = line[prefix.Length..];
        // rest should be "name:" or "name:N"
        int closeColon = rest.IndexOf(':');
        if (closeColon < 1) return false;

        name = rest[..closeColon];
        var seedStr = rest[(closeColon + 1)..].Trim();
        if (seedStr.Length > 0)
        {
            if (int.TryParse(seedStr, out var parsedSeed))
                seed = parsedSeed;
            else
                return false; // invalid seed value
        }
        return true;
    }

    /// <summary>
    /// Applies a counter: increments an existing counter or initializes a new one with the given seed.
    /// The counter value is stored as a regular document attribute.
    /// </summary>
    private static void ApplyCounter(DocumentNode document, Dictionary<string, int> counters, string name, int? seed)
    {
        if (counters.TryGetValue(name, out var current))
        {
            // Existing counter: always increment by 1 regardless of seed.
            // This is intentional: the seed only applies to the first occurrence
            // of a counter. Subsequent :counter:name:N entries ignore the seed
            // value and simply increment from the current value. This matches
            // Asciidoctor behavior.
            current++;
            counters[name] = current;
        }
        else
        {
            // New counter: initialize with seed or 1.
            current = seed ?? 1;
            counters[name] = current;
        }
        document.SetAttribute(name, current.ToString());
    }

    /// <summary>
    /// Expands <c>{name}</c> references in an attribute value using existing document attributes.
    /// This enables nested attribute expansion like <c>:full: {first} {last}</c>.
    /// </summary>
    private static string ExpandAttributeValue(string value, IReadOnlyDictionary<string, string> attributes)
    {
        if (attributes.Count == 0 || !value.Contains('{'))
            return value;
        return InlineParser.ExpandAttributes(value, attributes);
    }

    /// <summary>
    /// Applies Asciidoctor-style attribute value line continuation.
    /// When a value ends with <c> \</c> (space + backslash) or is exactly <c>\</c>,
    /// the next line is appended with a space separator.
    /// </summary>
    private static string ApplyLineContinuation(string value, string[] lines, ref int i)
    {
        while (value.EndsWith(" \\") || value == "\\")
        {
            value = value.EndsWith(" \\") ? value[..^2] : "";
            if (i + 1 >= lines.Length)
                break;
            var nextLine = lines[i + 1].Trim();
            if (nextLine.Length == 0)
                break;
            // Stop if the next line looks like an attribute entry (:name: ...)
            if (nextLine[0] == ':')
                break;
            i++;
            value = value.Length > 0 ? value + " " + nextLine : nextLine;
        }
        return value;
    }

    // ── Block anchor / section ID helpers ──────────────────────────────────

    /// <summary>
    /// Tries to parse a block anchor: <c>[[id]]</c> on a line by itself.
    /// </summary>
    /// <summary>
    /// Tries to parse a shorthand ID/role attribute line:
    /// <c>[#id]</c>, <c>[#id.role]</c>, <c>[#id.role1.role2]</c>,
    /// <c>[.role]</c>, or <c>[.role1.role2]</c>.
    /// </summary>
    private static bool TryParseShorthandIdOrRoles(string line, out string? id, out List<string>? roles)
    {
        id = null;
        roles = null;
        if (line.Length < 4 || line[0] != '[') return false;
        if (line[1] != '#' && line[1] != '.') return false;

        // Find the closing ']', allowing optional trailing whitespace.
        var trimmed = line.AsSpan().TrimEnd();
        if (trimmed[^1] != ']') return false;

        var inner = trimmed[1..^1]; // content between [ and ]
        if (inner.Length == 0) return false;

        // Parse: #id, #id.role1.role2, .role1.role2
        int pos = 0;
        if (inner[0] == '#')
        {
            pos = 1;
            var dotIndex = inner[pos..].IndexOf('.');
#if NET10_0_OR_GREATER
            ReadOnlySpan<char> idPart;
#else
            string idPart;
#endif
            if (dotIndex >= 0)
                idPart = inner[pos..(pos + dotIndex)];
            else
                idPart = inner[pos..];

            if (idPart.Length == 0) return false;
            if (!IsValidIdChars(idPart)) return false;
            id = idPart.ToString();
            pos += idPart.Length;
        }

        // Parse roles: .role1.role2...
        if (pos < inner.Length)
        {
            if (inner[pos] != '.') return false;
            var roleParts = new List<string>();
            while (pos < inner.Length && inner[pos] == '.')
            {
                pos++;
                int start = pos;
                while (pos < inner.Length && inner[pos] != '.')
                    pos++;
                var rolePart = inner[start..pos];
                if (rolePart.Length == 0) return false;
                if (!IsValidIdChars(rolePart)) return false;
                roleParts.Add(rolePart.ToString());
            }
            if (roleParts.Count == 0) return false;
            roles = roleParts;
        }

        return id is not null || roles is not null;

#if NET10_0_OR_GREATER
        static bool IsValidIdChars(ReadOnlySpan<char> s)
#else
        static bool IsValidIdChars(string s)
#endif
        {
            foreach (var ch in s)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-') return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Tries to parse a role attribute line: <c>[role="value1 value2"]</c>.
    /// </summary>
    private static bool TryParseRoleAttribute(string line, out List<string>? roles)
    {
        roles = null;
        // [role="..."] — minimum length is [role="x"] = 9
        if (line.Length < 9) return false;

        var trimmed = line.AsSpan().TrimEnd();
        if (!trimmed.StartsWith("[role=\"".AsSpan())) return false;
        if (trimmed[^1] != ']') return false;
        if (trimmed[^2] != '"') return false;

        var inner = trimmed[7..^2]; // content between [role=" and "]
        if (inner.Length == 0) return false;

        var result = new List<string>();
        int start = 0;
        for (int i = 0; i <= inner.Length; i++)
        {
            if (i == inner.Length || inner[i] == ' ')
            {
                if (i > start)
                    result.Add(inner[start..i].ToString());
                start = i + 1;
            }
        }

        if (result.Count == 0) return false;
        roles = result;
        return true;
    }

    private static bool TryParseBlockAnchor(string line, out string id, out string? reftext)
    {
        id = string.Empty;
        reftext = null;
        if (line.Length < 5 || line[0] != '[' || line[1] != '[') return false;
        if (line[^1] != ']' || line[^2] != ']') return false;
        var inner = line[2..^2];
        if (inner.Length == 0) return false;
        var commaIdx = inner.IndexOf(',');
        if (commaIdx > 0)
        {
            id = inner[..commaIdx].Trim();
            reftext = inner[(commaIdx + 1)..].Trim();
        }
        else
        {
            id = inner;
        }
        return true;
    }

    /// <summary>
    /// Generates a section ID from its title text, following the Asciidoctor convention.
    /// Respects <c>:idprefix:</c> (default <c>_</c>) and <c>:idseparator:</c> (default <c>_</c>)
    /// document attributes for customizing auto-generated IDs.
    /// </summary>
    internal static string GenerateSectionId(string title, IReadOnlyDictionary<string, string>? attributes = null)
    {
        var prefix = "_";
        var separator = "_";
        if (attributes is not null)
        {
            if (attributes.TryGetValue("idprefix", out var pfx))
                prefix = pfx;
            if (attributes.TryGetValue("idseparator", out var sep))
                separator = sep;
        }

        var sb = new System.Text.StringBuilder(title.Length + prefix.Length);
        sb.Append(prefix);
        bool lastWasSeparator = prefix.Length > 0; // collapse leading separator
        foreach (var ch in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                lastWasSeparator = false;
            }
            else if (ch is '\'' or '\u2019') // Asciidoctor strips apostrophes (and smart apostrophes)
            {
                // Apostrophe is simply dropped, not replaced with separator.
            }
            else
            {
                if (!lastWasSeparator && separator.Length > 0)
                {
                    sb.Append(separator);
                    lastWasSeparator = true;
                }
            }
        }
        // Strip trailing separator.
        if (separator.Length > 0 && sb.Length > prefix.Length && sb.ToString().EndsWith(separator))
            sb.Length -= separator.Length;
        return sb.ToString();
    }

    // ── Description list helpers ──────────────────────────────────────────

    /// <summary>
    /// Tries to parse a description list item: term:: description
    /// The marker is :: (double colon) with at least one character before it.
    /// Supports nested markers: :: = depth 1, ::: = depth 2, :::: = depth 3.
    /// </summary>
    private static bool TryParseDescriptionItem(string line, out string term, out string description, out int depth)
    {
        term = string.Empty;
        description = string.Empty;
        depth = 0;

        // Look for :: not at position 0.
        int idx = line.IndexOf("::", StringComparison.Ordinal);
        if (idx < 1) return false;

        // Count consecutive colons starting at idx
        int colonEnd = idx;
        while (colonEnd < line.Length && line[colonEnd] == ':') colonEnd++;
        int colonCount = colonEnd - idx;

        // In AsciiDoc, description list items require :: (or ::: or ::::) followed by a space or end-of-line.
        // Block macros use :: immediately followed by a target (no space): name::target[attrs].
        if (colonEnd < line.Length && line[colonEnd] != ' ')
            return false;

        var candidateTerm = line[..idx].TrimEnd();
        if (candidateTerm.Length == 0) return false;

        // Description is everything after the colons, trimmed.
        var desc = colonEnd < line.Length ? line[(colonEnd + 1)..].Trim() : string.Empty;
        term = candidateTerm;
        description = desc;
        depth = colonCount - 1; // :: = depth 1, ::: = depth 2, :::: = depth 3
        return true;
    }

    /// <summary>
    /// Returns true if the line looks like a section header (starts with "= " or "== " etc.).
    /// </summary>
    private static bool IsSectionHeader(string line)
    {
        return (line.Length >= 3 && line[0] == '='
            && (line.StartsWith("= ") || line.StartsWith("== ") || line.StartsWith("=== ")
                || line.StartsWith("==== ") || line.StartsWith("===== ")))
            || (line.Length >= 3 && line[0] == '#'
            && (line.StartsWith("# ") || line.StartsWith("## ") || line.StartsWith("### ")
                || line.StartsWith("#### ") || line.StartsWith("##### ") || line.StartsWith("###### ")));
    }

    /// <summary>
    /// Returns true if the line is a delimited block boundary (e.g. ----, ====, ****, etc.).
    /// </summary>
    private static bool IsDelimitedBlockBoundary(string line)
    {
        if (line.Length < 4) return false;
        char c = line[0];
        if (c != '-' && c != '=' && c != '.' && c != '*' && c != '_' && c != '+' && c != '/' && c != '|')
            return false;
        for (int i = 1; i < line.Length; i++)
        {
            if (line[i] != c) return false;
        }
        return true;
    }

    // ── Callout helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Strips a callout marker from the end of a source line.
    /// Recognizes: <c>// &lt;N&gt;</c>, <c># &lt;N&gt;</c>, <c>&lt;!-- &lt;N&gt; --&gt;</c>.
    /// Returns the line unchanged if no marker is found.
    /// </summary>
    private static string StripCalloutMarker(string line)
        => StripCalloutMarker(line, out _);

    /// <summary>
    /// Strips a callout marker from the end of a source line and outputs the callout numbers found.
    /// </summary>
    private static string StripCalloutMarker(string line, out List<int>? calloutNumbers)
    {
        calloutNumbers = null;

        // Try XML-style: <!-- <N> -->
        int xmlEnd = line.LastIndexOf("-->", StringComparison.Ordinal);
        if (xmlEnd >= 0)
        {
            int xmlStart = line.LastIndexOf("<!--", xmlEnd, StringComparison.Ordinal);
            if (xmlStart >= 0)
            {
                var inner = line[(xmlStart + 4)..xmlEnd].Trim();
                if (TryParseCalloutNumber(inner, out int num))
                {
                    calloutNumbers = [num];
                    return line[..xmlStart].TrimEnd();
                }
            }
        }

        // Try C-style: // <N>  — preserve the "//" comment prefix
        int cIdx = line.LastIndexOf("// <", StringComparison.Ordinal);
        if (cIdx >= 0 && line.Length > cIdx + 4)
        {
            var tail = line[(cIdx + 3)..].TrimEnd();
            if (TryParseCalloutNumber(tail, out int num))
            {
                calloutNumbers = [num];
                return line[..(cIdx + 2)].TrimEnd();  // keep "//"
            }
        }

        // Try hash-style: # <N>  — preserve the "#" comment prefix
        int hIdx = line.LastIndexOf("# <", StringComparison.Ordinal);
        if (hIdx >= 0 && line.Length > hIdx + 3)
        {
            var tail = line[(hIdx + 2)..].TrimEnd();
            if (TryParseCalloutNumber(tail, out int num))
            {
                calloutNumbers = [num];
                return line[..(hIdx + 1)].TrimEnd();  // keep "#"
            }
        }

        // Try bare callout markers at end of line: text <N> or text <.>
        var trimmed = line.TrimEnd();
        bool stripped = false;
        while (trimmed.EndsWith('>'))
        {
            int openAngle = trimmed.LastIndexOf('<');
            if (openAngle < 0) break;
            var candidate = trimmed[openAngle..];
            if (!TryParseCalloutNumber(candidate, out int num)) break;
            calloutNumbers ??= [];
            calloutNumbers.Add(num);
            trimmed = trimmed[..openAngle].TrimEnd();
            stripped = true;
        }
        if (stripped) return trimmed;

        return line;
    }

    /// <summary>
    /// Checks if a string is a callout number reference like <c>&lt;1&gt;</c>.
    /// </summary>
    private static bool IsCalloutNumber(string s) => TryParseCalloutNumber(s, out _);

    /// <summary>
    /// Tries to parse a callout number reference like <c>&lt;1&gt;</c>.
    /// Returns -1 for auto-numbering (<c>&lt;.&gt;</c>).
    /// </summary>
    private static bool TryParseCalloutNumber(string s, out int number)
    {
        number = 0;
        if (s.Length < 3 || s[0] != '<' || s[^1] != '>') return false;
        var inner = s[1..^1];
        if (inner == ".")
        {
            number = -1; // auto-numbering sentinel
            return true;
        }
        for (int i = 0; i < inner.Length; i++)
            if (!char.IsDigit(inner[i])) return false;
        number = int.Parse(inner);
        return true;
    }

    /// <summary>
    /// Tries to parse a callout list entry line like <c>&lt;1&gt; explanation text</c>.
    /// </summary>
    private static bool TryParseCalloutEntry(string line, out int number, out string text)
    {
        number = 0;
        text = string.Empty;

        if (line.Length < 3 || line[0] != '<') return false;

        int closeAngle = line.IndexOf('>');
        if (closeAngle < 2) return false;

        var numStr = line[1..closeAngle];
        if (numStr == ".")
        {
            number = 0; // auto-number: caller assigns sequential number
        }
        else if (!int.TryParse(numStr, out number))
        {
            return false;
        }

        text = closeAngle + 1 < line.Length ? line[(closeAngle + 1)..].TrimStart() : string.Empty;
        return true;
    }

    // ── Bibliography helpers ──────────────────────────────────────────────

    /// <summary>
    /// Tries to parse a bibliography entry line: <c>- [[[id]]] text</c> or <c>- [[[id,label]]] text</c>.
    /// </summary>
    private static bool TryParseBibliographyEntry(string line, out string refId, out string? label, out string text)
    {
        refId = string.Empty;
        label = null;
        text = string.Empty;

        // Must start with "- [[["
        if (!line.StartsWith("- [[["))
            return false;

        // Find closing ]]]
        int closeIdx = line.IndexOf("]]]", 5);
        if (closeIdx < 0)
            return false;

        var inner = line[5..closeIdx];
        if (inner.Length == 0)
            return false;

        // Check for comma-separated label: id,label
        int commaIdx = inner.IndexOf(',');
        if (commaIdx >= 0)
        {
            refId = inner[..commaIdx].Trim();
            label = inner[(commaIdx + 1)..].Trim();
        }
        else
        {
            refId = inner.Trim();
        }

        // Text after ]]] (with optional leading space)
        int textStart = closeIdx + 3;
        text = textStart < line.Length ? line[textStart..].TrimStart() : string.Empty;
        return true;
    }

    /// <summary>
    /// Returns a dictionary of intrinsic default attributes (backend, special chars, etc.).
    /// Used by both BlockParser and ConditionalPreprocessor to ensure consistent defaults.
    /// </summary>
    internal static Dictionary<string, string> GetDefaultAttributes()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["backend"] = "html5",
            ["doctype"] = "article",
            ["filetype"] = "html",
            ["outfilesuffix"] = ".html",
            ["empty"] = "",
            ["sp"] = " ",
            ["blank"] = "",
            ["zwsp"] = "\u200B",
            ["wj"] = "\u2060",
            ["apos"] = "'",
            ["quot"] = "\"",
            ["lsquo"] = "\u2018",
            ["rsquo"] = "\u2019",
            ["ldquo"] = "\u201C",
            ["rdquo"] = "\u201D",
            ["deg"] = "\u00B0",
            ["plus"] = "+",
            ["brvbar"] = "\u00A6",
            ["nbsp"] = "\u00A0",
            ["startsb"] = "[",
            ["endsb"] = "]",
            ["caret"] = "^",
            ["tilde"] = "~",
            ["backslash"] = "\\",
            ["backtick"] = "`",
            ["vbar"] = "|",
            ["amp"] = "&",
            ["lt"] = "<",
            ["gt"] = ">",
            ["asterisk"] = "*",
            ["two-colons"] = "::",
            ["two-semicolons"] = ";;",
            ["cpp"] = "C++",
            ["asciidoc-version"] = "", // empty — we're not Asciidoctor
            // Smart quotes are enabled by default (matching Asciidoctor).
            // Users can disable via :!smartquotes: in their document.
            ["smartquotes"] = "",
        };
    }

    private static void PopulateDefaultAttributes(DocumentNode document)
    {
        foreach (var kvp in GetDefaultAttributes())
            document.SetAttribute(kvp.Key, kvp.Value);

        // Date/time built-in attributes (Asciidoctor compatibility).
        // doc* defaults to current time but is overridden by ConvertFile / AdocParser
        // to file mtime when parsing from file. local* always reflects current time.
        // Asciidoctor uses Ruby strftime %z which formats UTC offset as "+0100"
        // (no colon). .NET's "zzz" formats as "+01:00" — strip the colon to match.
        var now = DateTime.Now;
        var tzOffsetNoColon = now.ToString("zzz").Replace(":", "");
        var time = now.ToString("HH:mm:ss") + " " + tzOffsetNoColon;
        var datetime = now.ToString("yyyy-MM-dd HH:mm:ss") + " " + tzOffsetNoColon;
        document.SetAttribute("docyear", now.Year.ToString());
        document.SetAttribute("docdate", now.ToString("yyyy-MM-dd"));
        document.SetAttribute("doctime", time);
        document.SetAttribute("docdatetime", datetime);
        document.SetAttribute("localyear", now.Year.ToString());
        document.SetAttribute("localdate", now.ToString("yyyy-MM-dd"));
        document.SetAttribute("localtime", time);
        document.SetAttribute("localdatetime", datetime);
    }

    /// <summary>
    /// Parses an AsciiDoc author line and populates intrinsic author attributes.
    /// Supports multiple authors separated by semicolons.
    /// Format: <c>Firstname [Middlename] Lastname [&lt;email&gt;]</c>
    /// </summary>
    private static void ParseAuthorLine(string line, DocumentNode document)
    {
        var authors = line.Split(';');
        for (int idx = 0; idx < authors.Length; idx++)
        {
            var raw = authors[idx].Trim();
            if (raw.Length == 0) continue;

            string? email = null;
            string namePart = raw;

            // Extract email from <email> suffix.
            int emailStart = raw.IndexOf('<');
            int emailEnd = raw.IndexOf('>');
            if (emailStart >= 0 && emailEnd > emailStart)
            {
                email = raw[(emailStart + 1)..emailEnd].Trim();
                namePart = raw[..emailStart].Trim();
            }

            if (namePart.Length == 0) continue;

            var parts = namePart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string? firstName = parts.Length > 0 ? parts[0] : null;
            string? middleName = parts.Length > 2 ? string.Join(" ", parts[1..^1]) : null;
            string? lastName = parts.Length > 1 ? parts[^1] : null;

            // Build initials.
            string initials = "";
            if (firstName is not null) initials += firstName[0];
            if (middleName is not null)
            {
                foreach (var part in middleName.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    initials += part[0];
            }
            if (lastName is not null) initials += lastName[0];
            initials = initials.ToUpperInvariant();

            // Attribute suffix: first author has no suffix, subsequent authors get _2, _3, etc.
            string suffix = idx == 0 ? "" : $"_{idx + 1}";

            document.SetAttribute($"author{suffix}", namePart);
            if (firstName is not null) document.SetAttribute($"firstname{suffix}", firstName);
            if (middleName is not null) document.SetAttribute($"middlename{suffix}", middleName);
            if (lastName is not null) document.SetAttribute($"lastname{suffix}", lastName);
            if (email is not null) document.SetAttribute($"email{suffix}", email);
            if (initials.Length > 0) document.SetAttribute($"authorinitials{suffix}", initials);
        }
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="line"/> looks like a revision line
    /// rather than an author line. A revision line starts with <c>v</c>/<c>V</c>
    /// followed by a digit, or starts with a digit (bare date such as <c>2024-01-15</c>).
    /// </summary>
    private static bool LooksLikeRevisionLine(string line)
    {
        if (line.Length == 0)
            return false;
#if NET10_0_OR_GREATER
        if (char.IsAsciiDigit(line[0]))
#else
        if (CharCompat.IsAsciiDigit(line[0]))
#endif
            return true;
#if NET10_0_OR_GREATER
        if ((line[0] == 'v' || line[0] == 'V') && line.Length > 1 && char.IsAsciiDigit(line[1]))
#else
        if ((line[0] == 'v' || line[0] == 'V') && line.Length > 1 && CharCompat.IsAsciiDigit(line[1]))
#endif
            return true;
        return false;
    }

    /// <summary>
    /// Parses an AsciiDoc revision line and populates intrinsic revision attributes.
    /// Format: <c>[vN.N][, date][: remark]</c>
    /// </summary>
    private static void ParseRevisionLine(string line, DocumentNode document)
    {
        var remaining = line.AsSpan().Trim();

        // Extract revnumber: starts with 'v' or 'V' followed by digits/dots.
        if (remaining.Length > 0 && (remaining[0] == 'v' || remaining[0] == 'V'))
        {
            int end = 1;
            while (end < remaining.Length && remaining[end] != ',' && remaining[end] != ':')
                end++;
            var revNumber = remaining[1..end].Trim();
            if (revNumber.Length > 0)
                document.SetAttribute("revnumber", revNumber.ToString());
            remaining = remaining[end..];
            // Skip comma separator if present.
            if (remaining.Length > 0 && remaining[0] == ',')
                remaining = remaining[1..].Trim();
        }

        // Extract revdate: everything up to ':' or end of line.
        int colonIdx = remaining.IndexOf(':');
        if (colonIdx >= 0)
        {
            var date = remaining[..colonIdx].Trim();
            if (date.Length > 0)
                document.SetAttribute("revdate", date.ToString());
            // Extract revremark: everything after ':'.
            var remark = remaining[(colonIdx + 1)..].Trim();
            if (remark.Length > 0)
                document.SetAttribute("revremark", remark.ToString());
        }
        else
        {
            // No colon — entire remaining is revdate.
            if (remaining.Length > 0)
                document.SetAttribute("revdate", remaining.ToString());
        }
    }
}
