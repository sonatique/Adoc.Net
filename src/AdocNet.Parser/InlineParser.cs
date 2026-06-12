using System.Text;
using AdocNet;
using AdocNet.Ast;

namespace AdocNet.Parser;

/// <summary>
/// A recursive-descent inline parser supporting nested formatting.
/// Supports: plain text, emphasis (_..._), strong (*...*), monospace (`...`),
/// bare URLs, link:URL[label], image:target[alt] inline macros,
/// passthrough (+...+ and pass:[...]), and cross-references (&lt;&lt;id&gt;&gt;).
/// Formatting markers can nest arbitrarily (e.g., *_bold italic_*) but a marker
/// cannot nest inside itself.
/// <para>
/// Substitution behavior is controlled by <see cref="SubstitutionKind"/> flags:
/// <list type="bullet">
///   <item><see cref="SubstitutionKind.Normal"/> — full inline processing (default for text contexts)</item>
///   <item><see cref="SubstitutionKind.Verbatim"/> — no processing (raw content returned as-is)</item>
/// </list>
/// </para>
/// </summary>
internal static class InlineParser
{
    [Flags]
    private enum ActiveMarkers
    {
        None = 0,
        Strong = 1,
        Emphasis = 2,
        Monospace = 4,
        Highlight = 8,
    }

    /// <summary>
    /// Parses inline content with <see cref="SubstitutionKind.Normal"/> substitutions
    /// and no document attributes. Backward-compatible convenience overload.
    /// </summary>
    public static IReadOnlyList<InlineNode> Parse(string text) =>
        Parse(text, SubstitutionKind.Normal, attributes: null);

    /// <summary>
    /// Parses inline content applying the specified <paramref name="substitutions"/>.
    /// <para>
    /// When <see cref="SubstitutionKind.Attributes"/> is set, <c>{name}</c> references
    /// are expanded from <paramref name="attributes"/> before inline scanning.
    /// Unknown attribute references are left as-is.
    /// </para>
    /// </summary>
    /// <param name="text">The raw inline text to process.</param>
    /// <param name="substitutions">Which substitution types to apply.</param>
    /// <param name="attributes">
    /// Document attributes for <c>{name}</c> expansion. May be null if
    /// <see cref="SubstitutionKind.Attributes"/> is not set.
    /// </param>
    public static IReadOnlyList<InlineNode> Parse(
        string text,
        SubstitutionKind substitutions,
        IReadOnlyDictionary<string, string>? attributes)
    {
        if (string.IsNullOrEmpty(text)) return [];

        // ── Verbatim / None: no inline processing ──────────────────────────
        // Skip parsing when no inline-relevant phases are requested
        // (SpecialCharacters and Replacements are handled externally).
        const SubstitutionKind inlinePhases =
            SubstitutionKind.Quotes | SubstitutionKind.Macros |
            SubstitutionKind.Attributes | SubstitutionKind.Replacements |
            SubstitutionKind.PostReplacements;
        if ((substitutions & inlinePhases) == SubstitutionKind.None)
            return [new TextInlineNode { Value = text }];

        // ── Attribute expansion pre-pass ─────────────────────────────────────
        if (substitutions.HasFlag(SubstitutionKind.Attributes) && attributes is { Count: > 0 })
            text = ExpandAttributes(text, attributes);

        var doFormatting        = substitutions.HasFlag(SubstitutionKind.InlineFormatting);
        var doMacros            = substitutions.HasFlag(SubstitutionKind.Macros);
        var doReplacements      = substitutions.HasFlag(SubstitutionKind.Replacements);
        var doPostReplacements  = substitutions.HasFlag(SubstitutionKind.PostReplacements);
        // kbd:[], btn:[], menu:[] require :experimental: attribute to be set
        var doExperimental      = attributes is not null && attributes.ContainsKey("experimental");

        return ParseInlines(text, 0, text.Length, ActiveMarkers.None, doFormatting, doMacros, doReplacements, doPostReplacements, doExperimental, attributes);
    }

    /// <summary>
    /// Recursive inline parser. Scans from <paramref name="startIndex"/> to <paramref name="endIndex"/>
    /// within <paramref name="text"/>, respecting <paramref name="activeMarkers"/> to prevent
    /// self-nesting of formatting markers.
    /// </summary>
    private static List<InlineNode> ParseInlines(
        string text, int startIndex, int endIndex,
        ActiveMarkers activeMarkers, bool doFormatting, bool doMacros, bool doReplacements, bool doPostReplacements,
        bool doExperimental = false, IReadOnlyDictionary<string, string>? linkAttributes = null)
    {
        var nodes = new List<InlineNode>();
        var plain = new StringBuilder();
        int i = startIndex;

        while (i < endIndex)
        {
            char c = text[i];

            // ── Backslash escape: \* \_ \` \< \http etc. ────────────────────
            if (c == '\\' && i + 1 < endIndex)
            {
                char next = text[i + 1];
                if (doFormatting && (next == '*' || next == '_' || next == '`' || next == '^' || next == '~' || next == '#'))
                {
                    // Asciidoctor only consumes the backslash when the escaped
                    // character would actually OPEN a formatting span at this
                    // position. Check both: (a) the open boundary is valid, and
                    // (b) a matching closer exists. Without (b), a lone \* at
                    // closing position would incorrectly strip the backslash.
                    bool wouldOpen = (i + 2 < endIndex && text[i + 2] == next)
                        || (IsConstrainedOpenValid(text, i + 1, endIndex)
                            && FindConstrainedClose(text, next, i + 2, endIndex) >= 0);
                    if (wouldOpen)
                    {
                        plain.Append(next);
                        i += 2;
                        continue;
                    }
                }
                if (doFormatting && (next == '<' || next == '+'))
                {
                    plain.Append(next);
                    i += 2;
                    continue;
                }
                // \$$ — escape stem delimiter
                if (next == '$' && i + 2 < endIndex && text[i + 2] == '$'
                    && linkAttributes?.ContainsKey("stem") == true)
                {
                    plain.Append("$$");
                    i += 3;
                    continue;
                }
                // \http:// or \https:// — suppress URL autolink
                if (doMacros)
                {
                    var escSpan = text.AsSpan(i + 1, Math.Min(8, endIndex - i - 1));
                    if (escSpan.StartsWith("https://") || escSpan.StartsWith("http://"))
                    {
                        // Skip the backslash, let the URL pass through as plain text
                        i++; // skip '\', next iteration will see 'h' but we need to prevent autolink
                        // Emit the URL as plain text
                        int urlStart = i;
                        while (i < endIndex && !char.IsWhiteSpace(text[i]) && text[i] != '[')
                            i++;
                        var url = text[urlStart..i];
                        plain.Append(url);
                        continue;
                    }
                }
            }

            // ── $$ inline stem delimiter (only when :stem: is set) ────────
            if (c == '$' && i + 1 < endIndex && text[i + 1] == '$'
                && linkAttributes?.ContainsKey("stem") == true)
            {
                // Look for closing $$
                int stemStart = i + 2;
                int stemClose = text.IndexOf("$$", stemStart, endIndex - stemStart, StringComparison.Ordinal);
                if (stemClose > stemStart || (stemClose == stemStart)) // allow empty $$$$
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    var content = text[stemStart..stemClose];
                    i = stemClose + 2;
                    nodes.Add(new StemInlineNode { Content = content, StemType = "latexmath", Source = RangeWithin(text, nodeStart, i) });
                    continue;
                }
                // No closing $$ found — fall through to plain text
            }

            // ── Cross-reference: <<id>> or <<id,label>> ────────────────────
            if (doFormatting && c == '<' && i + 1 < endIndex && text[i + 1] == '<')
            {
                int closeIdx = text.IndexOf(">>", i + 2, StringComparison.Ordinal);
                if (closeIdx > i + 2 && closeIdx < endIndex)
                {
                    var inner = text[(i + 2)..closeIdx];
                    if (inner.Length > 0)
                    {
                        int nodeStart = i;
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                        int commaIdx = inner.IndexOf(',');
                        var target = commaIdx > 0 ? inner[..commaIdx].Trim() : inner.Trim();
                        var label = commaIdx > 0 ? inner[(commaIdx + 1)..].Trim() : null;

                        // Detect inter-document xref: target contains .adoc# or ends with .adoc
                        if (target.Contains(".adoc#", StringComparison.Ordinal) || target.EndsWith(".adoc", StringComparison.Ordinal))
                        {
                            int hashIdx = target.IndexOf('#');
                            if (hashIdx >= 0)
                            {
                                var path = target[..hashIdx];
                                var id = target[(hashIdx + 1)..];
                                nodes.Add(new InterDocumentXrefNode { Path = path, Id = id.Length > 0 ? id : null, Label = label is { Length: > 0 } ? label : null });
                            }
                            else
                            {
                                nodes.Add(new InterDocumentXrefNode { Path = target, Id = null, Label = label is { Length: > 0 } ? label : null });
                            }
                        }
                        else if (commaIdx > 0)
                        {
                            nodes.Add(new CrossReferenceInlineNode { Target = target, Label = label is { Length: > 0 } ? label : null });
                        }
                        else
                        {
                            nodes.Add(new CrossReferenceInlineNode { Target = target });
                        }
                        i = closeIdx + 2;
                        nodes[^1].Source = RangeWithin(text, nodeStart, i);
                        continue;
                    }
                }
            }

            // ── Inline anchor: [[id]] or [[id,reftext]] ────────────────────────
            if (doFormatting && c == '[' && i + 1 < endIndex && text[i + 1] == '[')
            {
                int closeIdx = text.IndexOf("]]", i + 2, StringComparison.Ordinal);
                if (closeIdx > i + 2 && closeIdx + 2 <= endIndex)
                {
                    var content = text[(i + 2)..closeIdx];
                    if (content.Length > 0)
                    {
                        int nodeStart = i;
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                        var commaIdx = content.IndexOf(',');
                        string id;
                        string? reftext = null;
                        if (commaIdx > 0)
                        {
                            id = content[..commaIdx].Trim();
                            reftext = content[(commaIdx + 1)..].Trim();
                        }
                        else
                        {
                            id = content;
                        }
                        i = closeIdx + 2;
                        nodes.Add(new InlineAnchorNode { Id = id, Reftext = reftext, Source = RangeWithin(text, nodeStart, i) });
                        continue;
                    }
                }
            }

            // ── Triple-plus passthrough: +++content+++ ────────────────────────
            if (doFormatting && c == '+' && i + 2 < endIndex && text[i + 1] == '+' && text[i + 2] == '+')
            {
                int close = text.IndexOf("+++", i + 3, StringComparison.Ordinal);
                if (close >= i + 3 && close + 3 <= endIndex)
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    var pcontent = text[(i + 3)..close];
                    i = close + 3;
                    nodes.Add(new PassthroughInlineNode { Content = pcontent, Source = RangeWithin(text, nodeStart, i) });
                    continue;
                }
            }

            // ── Inline passthrough: +content+ (constrained) ─────────────────
            // The single-plus passthrough is a *constrained* mark: the opening
            // `+` must sit on a word boundary and not be followed by space, and
            // the closing `+` must not be preceded by space. This keeps ordinary
            // prose/maths like `a + b + c`, `1+1`, and `C++` literal instead of
            // silently swallowing the `+` markers.
            if (doFormatting && c == '+' && IsConstrainedOpenValid(text, i, endIndex))
            {
                int close = FindConstrainedClose(text, '+', i + 1, endIndex);
                if (close > i + 1)
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    var pcontent = text[(i + 1)..close];
                    i = close + 1;
                    nodes.Add(new PassthroughInlineNode { Content = pcontent, Source = RangeWithin(text, nodeStart, i) });
                    continue;
                }
            }

            // ── Pass macro: pass:[content] or pass:subs[content] ────────────
            if (doMacros && c == 'p')
            {
                var remaining = text.AsSpan(i, Math.Min(6, endIndex - i));
                if (remaining.StartsWith("pass:"))
                {
                    // Find the opening bracket to determine if there are substitution names
                    int openBracket = IndexOf(text, '[', i + 5, endIndex);
                    if (openBracket >= 0)
                    {
                        int closeBracket = IndexOf(text, ']', openBracket + 1, endIndex);
                        if (closeBracket >= 0)
                        {
                            int nodeStart = i;
                            var subsText = text[(i + 5)..openBracket];
                            var content = text[(openBracket + 1)..closeBracket];
                            var subs = ParseSubstitutionNames(subsText);

                            FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);

                            i = closeBracket + 1;

                            if (subs != SubstitutionKind.None)
                            {
                                // Re-parse content with the requested substitutions.
                                // Inline children take Source ranges from their own
                                // (slice-relative) coordinates here; their positions
                                // are relative to the content slice, not the outer
                                // text, but they remain non-None.
                                var doSubFormatting = subs.HasFlag(SubstitutionKind.InlineFormatting);
                                var doSubMacros = subs.HasFlag(SubstitutionKind.Macros);
                                var doSubReplacements = subs.HasFlag(SubstitutionKind.Replacements);
                                var doSubPostReplacements = subs.HasFlag(SubstitutionKind.PostReplacements);
                                var children = ParseInlines(content, 0, content.Length,
                                    ActiveMarkers.None, doSubFormatting, doSubMacros, doSubReplacements, doSubPostReplacements);
                                nodes.AddRange(children);
                            }
                            else
                            {
                                nodes.Add(new PassthroughInlineNode { Content = content, Source = RangeWithin(text, nodeStart, i) });
                            }

                            continue;
                        }
                    }
                }
            }

            // ── Inline macros: link:target[label], image:target[alt], anchor:id[reftext] ──
            if (doMacros && (c == 'l' || c == 'i' || c == 'a'))
            {
                if (TryParseInlineMacro(text, i, endIndex, linkAttributes, out var macroNode, out var macroEnd))
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    i = macroEnd;
                    macroNode.Source = RangeWithin(text, nodeStart, i);
                    nodes.Add(macroNode);
                    continue;
                }
            }

            // ── Footnote macro: footnote:[text], footnote:id[text], footnote:id[] ─
            if (doMacros && c == 'f')
            {
                if (TryParseFootnoteMacro(text, i, endIndex, doFormatting, doMacros, doReplacements, doPostReplacements, out var footnoteNode, out var fnEnd))
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    i = fnEnd;
                    footnoteNode.Source = RangeWithin(text, nodeStart, i);
                    nodes.Add(footnoteNode);
                    continue;
                }
            }

            // ── Xref macro: xref:path[label] or xref:path#id[label] ──────────
            if (doMacros && c == 'x')
            {
                if (TryParseXrefMacro(text, i, endIndex, out var xrefNode, out var xrefEnd))
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    i = xrefEnd;
                    xrefNode.Source = RangeWithin(text, nodeStart, i);
                    nodes.Add(xrefNode);
                    continue;
                }
            }

            // ── Generic inline macros: kbd:[...], btn:[...], menu:target[...], icon:name[...] ──
            // kbd:, btn:, menu: require :experimental: attribute; icon: is always available
            if (doMacros && (c == 'k' || c == 'b' || c == 'm' || c == 'i' || c == 's' || c == 'l' || c == 'a'))
            {
                // Skip kbd/btn/menu when :experimental: is not set
                // icon:, stem:, latexmath:, asciimath: are always available
                bool isExperimentalMacro = c is 'k' or 'b' or 'm';
                if (!isExperimentalMacro || doExperimental)
                {
                    if (TryParseGenericMacro(text, i, endIndex, linkAttributes, out var genericMacro, out var gmEnd))
                    {
                        int nodeStart = i;
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                        i = gmEnd;
                        genericMacro.Source = RangeWithin(text, nodeStart, i);
                        nodes.Add(genericMacro);
                        continue;
                    }
                }
            }

            // ── Bare URL: http:// or https:// ─────────────────────────────────
            // Also handles URL[label] shorthand (e.g., https://example.com[Example])
            if (doMacros && c == 'h')
            {
                var remaining = text.AsSpan(i, Math.Min(8, endIndex - i));
                if (remaining.StartsWith("https://") || remaining.StartsWith("http://"))
                {
                    // Determine scheme length to check for host component
                    int schemeEnd = i + (remaining.StartsWith("https://") ? 8 : 7);
                    // Don't auto-link bare scheme-only URLs (e.g., "https://" with no host)
                    if (schemeEnd >= endIndex || char.IsWhiteSpace(text[schemeEnd]) || text[schemeEnd] == '_' || text[schemeEnd] == '*' || text[schemeEnd] == '[')
                    {
                        plain.Append(c);
                        i++;
                        continue;
                    }

                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    int start = i;
                    while (i < endIndex && !char.IsWhiteSpace(text[i]) && text[i] != '[') i++;
                    var url = text[start..i].TrimEnd('.', ',', ';', ':', '!', ')', '?');
                    // Rewind so stripped trailing punctuation re-enters the loop as plain text.
                    i = start + url.Length;

                    // Check for [label] suffix
                    if (i < endIndex && text[i] == '[')
                    {
                        int closeBracket = IndexOf(text, ']', i + 1, endIndex);
                        if (closeBracket > i + 1)
                        {
                            var label = text[(i + 1)..closeBracket];
                            string? window = null;
                            // Strip trailing ^ (opens in new window indicator)
                            if (label.EndsWith('^'))
                            {
                                label = label[..^1];
                                window = "_blank";
                            }
                            i = closeBracket + 1;
                            nodes.Add(new InlineLinkMacroNode { Url = url, Label = label, Window = window, Source = RangeWithin(text, nodeStart, i) });
                        }
                        else if (closeBracket == i + 1)
                        {
                            // Empty brackets: URL[] — use URL as label
                            i = closeBracket + 1;
                            nodes.Add(new LinkInlineNode { Url = url, Source = RangeWithin(text, nodeStart, i) });
                        }
                        else
                        {
                            nodes.Add(new LinkInlineNode { Url = url, Source = RangeWithin(text, nodeStart, i) });
                        }
                    }
                    else
                    {
                        nodes.Add(new LinkInlineNode { Url = url, Source = RangeWithin(text, nodeStart, i) });
                    }
                    continue;
                }
            }

            // ── Email auto-link: user@domain.tld ─────────────────────────────
            if (doMacros && c == '@' && i > startIndex && i + 1 < endIndex)
            {
                // Walk backward to find start of local part
                int localStart = i - 1;
                while (localStart >= startIndex && (char.IsLetterOrDigit(text[localStart]) || text[localStart] == '.' || text[localStart] == '_' || text[localStart] == '-' || text[localStart] == '+'))
                    localStart--;
                localStart++; // back to first valid char

                if (localStart < i)
                {
                    // Walk forward to find end of domain part
                    int domainEnd = i + 1;
                    bool hasDot = false;
                    while (domainEnd < endIndex && (char.IsLetterOrDigit(text[domainEnd]) || text[domainEnd] == '.' || text[domainEnd] == '-'))
                    {
                        if (text[domainEnd] == '.') hasDot = true;
                        domainEnd++;
                    }

                    // Trim trailing dots/punctuation from domain
                    while (domainEnd > i + 1 && (text[domainEnd - 1] == '.' || text[domainEnd - 1] == ',' || text[domainEnd - 1] == ';' || text[domainEnd - 1] == ':'))
                        domainEnd--;

                    if (hasDot && domainEnd > i + 1)
                    {
                        var email = text[localStart..domainEnd];
                        // Remove the local part we already added to plain text buffer
                        var localPart = text[localStart..i];
                        if (plain.Length >= localPart.Length)
                            plain.Length -= localPart.Length;
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, localStart);
                        i = domainEnd;
                        nodes.Add(new InlineLinkMacroNode { Url = "mailto:" + email, Label = email, Source = RangeWithin(text, localStart, i) });
                        continue;
                    }
                }
            }

            // ── Index terms: (((hidden))) and ((visible)) ─────────────────────
            if (doMacros && c == '(' && i + 1 < endIndex && text[i + 1] == '(')
            {
                // Check for hidden index term (((term))) first
                if (i + 2 < endIndex && text[i + 2] == '(')
                {
                    int closeIdx = text.IndexOf(")))", i + 3, StringComparison.Ordinal);
                    if (closeIdx > i + 3 && closeIdx + 3 <= endIndex)
                    {
                        int nodeStart = i;
                        var inner = text[(i + 3)..closeIdx];
                        var terms = inner.Split(',').Select(t => t.Trim()).ToArray();
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                        i = closeIdx + 3;
                        nodes.Add(new IndexTermHiddenNode { Terms = terms, Source = RangeWithin(text, nodeStart, i) });
                        continue;
                    }
                }
                // Check for visible index term ((term))
                // Make sure we don't match ((( which was handled above
                if (!(i + 2 < endIndex && text[i + 2] == '('))
                {
                    int closeIdx = text.IndexOf("))", i + 2, StringComparison.Ordinal);
                    if (closeIdx > i + 2 && closeIdx + 2 <= endIndex)
                    {
                        // Ensure we don't match ))) — the close should not be followed by )
                        if (closeIdx + 2 >= endIndex || text[closeIdx + 2] != ')')
                        {
                            int nodeStart = i;
                            var inner = text[(i + 2)..closeIdx];
                            var terms = inner.Split(',').Select(t => t.Trim()).ToArray();
                            FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                            i = closeIdx + 2;
                            nodes.Add(new IndexTermNode { Terms = terms, Source = RangeWithin(text, nodeStart, i) });
                            continue;
                        }
                    }
                }
            }

            // ── Unconstrained strong: **content** ────────────────────────────
            if (doFormatting && c == '*' && i + 1 < endIndex && text[i + 1] == '*'
                && !activeMarkers.HasFlag(ActiveMarkers.Strong))
            {
                int close = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (close > i + 2 && close + 2 <= endIndex)
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    var children = ParseInlines(text, i + 2, close,
                        activeMarkers | ActiveMarkers.Strong, doFormatting, doMacros, doReplacements, doPostReplacements);
                    i = close + 2;
                    nodes.Add(new StrongInlineNode { Children = children, Source = RangeWithin(text, nodeStart, i) });
                    continue;
                }
            }

            // ── Constrained strong: *content* ───────────────────────────────
            if (doFormatting && c == '*' && !activeMarkers.HasFlag(ActiveMarkers.Strong)
                && IsConstrainedOpenValid(text, i, endIndex))
            {
                int close = FindConstrainedClose(text, '*', i + 1, endIndex);
                if (close > i + 1)
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    var children = ParseInlines(text, i + 1, close,
                        activeMarkers | ActiveMarkers.Strong, doFormatting, doMacros, doReplacements, doPostReplacements);
                    i = close + 1;
                    nodes.Add(new StrongInlineNode { Children = children, Source = RangeWithin(text, nodeStart, i) });
                    continue;
                }
            }

            // ── Unconstrained emphasis: __content__ ─────────────────────────
            // Emphasis nests legitimately inside monospace (`` `_x_` `` →
            // <code><em>x</em></code> in Asciidoctor); snake_case inside code
            // is already protected by the constrained word-boundary rule, so
            // no Monospace guard is needed here.
            if (doFormatting && c == '_' && i + 1 < endIndex && text[i + 1] == '_'
                && !activeMarkers.HasFlag(ActiveMarkers.Emphasis))
            {
                int close = text.IndexOf("__", i + 2, StringComparison.Ordinal);
                if (close > i + 2 && close + 2 <= endIndex)
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    var children = ParseInlines(text, i + 2, close,
                        activeMarkers | ActiveMarkers.Emphasis, doFormatting, doMacros, doReplacements, doPostReplacements);
                    i = close + 2;
                    nodes.Add(new EmphasisInlineNode { Children = children, Source = RangeWithin(text, nodeStart, i) });
                    continue;
                }
            }

            // ── Constrained emphasis: _content_ ─────────────────────────────
            // (No Monospace guard — see the unconstrained branch above.)
            if (doFormatting && c == '_' && !activeMarkers.HasFlag(ActiveMarkers.Emphasis)
                && IsConstrainedOpenValid(text, i, endIndex))
            {
                int close = FindConstrainedClose(text, '_', i + 1, endIndex);
                if (close > i + 1)
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    var children = ParseInlines(text, i + 1, close,
                        activeMarkers | ActiveMarkers.Emphasis, doFormatting, doMacros, doReplacements, doPostReplacements);
                    i = close + 1;
                    nodes.Add(new EmphasisInlineNode { Children = children, Source = RangeWithin(text, nodeStart, i) });
                    continue;
                }
            }

            // ── Curly double quotes: "`text`" ──────────────────────────────
            if (doFormatting && c == '"' && i + 1 < endIndex && text[i + 1] == '`')
            {
                // Look for closing `"
                int scan = i + 2;
                while (scan + 1 < endIndex)
                {
                    if (text[scan] == '`' && text[scan + 1] == '"')
                    {
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                        plain.Append('\u201c');
                        var inner = text[(i + 2)..scan];
                        plain.Append(inner);
                        plain.Append('\u201d');
                        i = scan + 2;
                        goto nextChar;
                    }
                    scan++;
                }
            }

            // ── Curly single quotes: '`text`' ──────────────────────────────
            if (doFormatting && c == '\'' && i + 1 < endIndex && text[i + 1] == '`')
            {
                int scan = i + 2;
                while (scan + 1 < endIndex)
                {
                    if (text[scan] == '`' && text[scan + 1] == '\'')
                    {
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                        plain.Append('\u2018');
                        var inner = text[(i + 2)..scan];
                        plain.Append(inner);
                        plain.Append('\u2019');
                        i = scan + 2;
                        goto nextChar;
                    }
                    scan++;
                }
            }

            // ── Unconstrained monospace: ``content`` ──────────────────────────
            if (doFormatting && c == '`' && i + 1 < endIndex && text[i + 1] == '`'
                && !activeMarkers.HasFlag(ActiveMarkers.Monospace))
            {
                int close = text.IndexOf("``", i + 2, StringComparison.Ordinal);
                if (close > i + 2 && close + 2 <= endIndex)
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    var children = ParseInlines(text, i + 2, close,
                        activeMarkers | ActiveMarkers.Monospace, doFormatting, doMacros, doReplacements, doPostReplacements);
                    i = close + 2;
                    nodes.Add(new MonospaceInlineNode { Children = children, Source = RangeWithin(text, nodeStart, i) });
                    continue;
                }
            }

            // ── Monospace: `content` ──────────────────────────────────────────
            if (doFormatting && c == '`' && !activeMarkers.HasFlag(ActiveMarkers.Monospace)
                && IsConstrainedOpenValid(text, i, endIndex))
            {
                int close = FindConstrainedClose(text, '`', i + 1, endIndex);
                if (close > i + 1)
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    var children = ParseInlines(text, i + 1, close,
                        activeMarkers | ActiveMarkers.Monospace, doFormatting, doMacros, doReplacements, doPostReplacements);
                    i = close + 1;
                    nodes.Add(new MonospaceInlineNode { Children = children, Source = RangeWithin(text, nodeStart, i) });
                    continue;
                }
            }

            // ── Superscript: ^content^ ────────────────────────────────────────
            if (doFormatting && c == '^')
            {
                int close = IndexOf(text, '^', i + 1, endIndex);
                if (close > i + 1)
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    var content = text[(i + 1)..close];
                    i = close + 1;
                    nodes.Add(new SuperscriptInlineNode { Content = content, Source = RangeWithin(text, nodeStart, i) });
                    continue;
                }
            }

            // ── Subscript: ~content~ ──────────────────────────────────────────
            if (doFormatting && c == '~')
            {
                int close = IndexOf(text, '~', i + 1, endIndex);
                if (close > i + 1)
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    var content = text[(i + 1)..close];
                    i = close + 1;
                    nodes.Add(new SubscriptInlineNode { Content = content, Source = RangeWithin(text, nodeStart, i) });
                    continue;
                }
            }

            // ── Custom span roles/id: [.role]#text#, [#id]#text#, [#id.role]#text#, [.role]*text*, etc. ───
            if (doFormatting && c == '[' && i + 2 < endIndex && (text[i + 1] == '.' || text[i + 1] == '#'))
            {
                int closeBracket = text.IndexOf(']', i + 2);
                if (closeBracket > i + 2 && closeBracket + 1 < endIndex)
                {
                    char marker = text[closeBracket + 1];
                    if (marker == '#' && !activeMarkers.HasFlag(ActiveMarkers.Highlight))
                    {
                        // Parse id and roles from [#id.role1.role2] or [.role1.role2]
                        var attrStr = text[(i + 1)..closeBracket];
                        string? spanId = null;
                        List<string> roles;
                        if (attrStr.StartsWith("#"))
                        {
                            // Extract id: everything from # to the first . or end
                            int dotPos = attrStr.IndexOf('.', 1);
                            if (dotPos > 0)
                            {
                                spanId = attrStr[1..dotPos];
                                roles = attrStr[dotPos..].Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
                            }
                            else
                            {
                                spanId = attrStr[1..];
                                roles = new List<string>();
                            }
                        }
                        else
                        {
                            roles = attrStr.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
                        }
                        if (roles.Count > 0 || spanId is not null)
                        {
                            int contentStart;
                            int close;
                            bool isUnconstrained = closeBracket + 2 < endIndex && text[closeBracket + 2] == '#';
                            if (isUnconstrained)
                            {
                                // [#id.role]##content##
                                contentStart = closeBracket + 3;
                                close = text.IndexOf("##", contentStart, StringComparison.Ordinal);
                                if (close > contentStart && close + 2 <= endIndex)
                                {
                                    int nodeStart = i;
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Highlight, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    i = close + 2;
                                    nodes.Add(new HighlightInlineNode { Children = children, Roles = roles, Id = spanId, Source = RangeWithin(text, nodeStart, i) });
                                    continue;
                                }
                            }
                            else
                            {
                                // [#id.role]#content#
                                contentStart = closeBracket + 2;
                                close = IndexOf(text, '#', contentStart, endIndex);
                                if (close > contentStart)
                                {
                                    int nodeStart = i;
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Highlight, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    i = close + 1;
                                    nodes.Add(new HighlightInlineNode { Children = children, Roles = roles, Id = spanId, Source = RangeWithin(text, nodeStart, i) });
                                    continue;
                                }
                            }
                        }
                    }
                    else if (marker == '*' && !activeMarkers.HasFlag(ActiveMarkers.Strong))
                    {
                        var rolesStr = text[(i + 1)..closeBracket];
                        var roles = rolesStr.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
                        if (roles.Count > 0)
                        {
                            bool isUnconstrained = closeBracket + 2 < endIndex && text[closeBracket + 2] == '*';
                            if (isUnconstrained)
                            {
                                int contentStart = closeBracket + 3;
                                int close = text.IndexOf("**", contentStart, StringComparison.Ordinal);
                                if (close > contentStart && close + 2 <= endIndex)
                                {
                                    int nodeStart = i;
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Strong, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    i = close + 2;
                                    nodes.Add(new StrongInlineNode { Children = children, Roles = roles, Source = RangeWithin(text, nodeStart, i) });
                                    continue;
                                }
                            }
                            else
                            {
                                int contentStart = closeBracket + 2;
                                int close = IndexOf(text, '*', contentStart, endIndex);
                                if (close > contentStart)
                                {
                                    int nodeStart = i;
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Strong, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    i = close + 1;
                                    nodes.Add(new StrongInlineNode { Children = children, Roles = roles, Source = RangeWithin(text, nodeStart, i) });
                                    continue;
                                }
                            }
                        }
                    }
                    else if (marker == '_' && !activeMarkers.HasFlag(ActiveMarkers.Emphasis)
                             && !activeMarkers.HasFlag(ActiveMarkers.Monospace))
                    {
                        var rolesStr = text[(i + 1)..closeBracket];
                        var roles = rolesStr.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
                        if (roles.Count > 0)
                        {
                            bool isUnconstrained = closeBracket + 2 < endIndex && text[closeBracket + 2] == '_';
                            if (isUnconstrained)
                            {
                                int contentStart = closeBracket + 3;
                                int close = text.IndexOf("__", contentStart, StringComparison.Ordinal);
                                if (close > contentStart && close + 2 <= endIndex)
                                {
                                    int nodeStart = i;
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Emphasis, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    i = close + 2;
                                    nodes.Add(new EmphasisInlineNode { Children = children, Roles = roles, Source = RangeWithin(text, nodeStart, i) });
                                    continue;
                                }
                            }
                            else
                            {
                                int contentStart = closeBracket + 2;
                                int close = IndexOf(text, '_', contentStart, endIndex);
                                if (close > contentStart)
                                {
                                    int nodeStart = i;
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Emphasis, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    i = close + 1;
                                    nodes.Add(new EmphasisInlineNode { Children = children, Roles = roles, Source = RangeWithin(text, nodeStart, i) });
                                    continue;
                                }
                            }
                        }
                    }
                    else if (marker == '`' && !activeMarkers.HasFlag(ActiveMarkers.Monospace))
                    {
                        var rolesStr = text[(i + 1)..closeBracket];
                        var roles = rolesStr.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
                        if (roles.Count > 0)
                        {
                            bool isUnconstrained = closeBracket + 2 < endIndex && text[closeBracket + 2] == '`';
                            if (isUnconstrained)
                            {
                                int contentStart = closeBracket + 3;
                                int close = text.IndexOf("``", contentStart, StringComparison.Ordinal);
                                if (close > contentStart && close + 2 <= endIndex)
                                {
                                    int nodeStart = i;
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Monospace, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    i = close + 2;
                                    nodes.Add(new MonospaceInlineNode { Children = children, Roles = roles, Source = RangeWithin(text, nodeStart, i) });
                                    continue;
                                }
                            }
                            else
                            {
                                int contentStart = closeBracket + 2;
                                int close = IndexOf(text, '`', contentStart, endIndex);
                                if (close > contentStart)
                                {
                                    int nodeStart = i;
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Monospace, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    i = close + 1;
                                    nodes.Add(new MonospaceInlineNode { Children = children, Roles = roles, Source = RangeWithin(text, nodeStart, i) });
                                    continue;
                                }
                            }
                        }
                    }
                }
            }

            // ── Unconstrained highlight: ##content## ────────────────────────
            if (doFormatting && c == '#' && i + 1 < endIndex && text[i + 1] == '#'
                && !activeMarkers.HasFlag(ActiveMarkers.Highlight))
            {
                int close = text.IndexOf("##", i + 2, StringComparison.Ordinal);
                if (close > i + 2 && close + 2 <= endIndex)
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    var children = ParseInlines(text, i + 2, close,
                        activeMarkers | ActiveMarkers.Highlight, doFormatting, doMacros, doReplacements, doPostReplacements);
                    i = close + 2;
                    nodes.Add(new HighlightInlineNode { Children = children, Source = RangeWithin(text, nodeStart, i) });
                    continue;
                }
            }

            // ── Constrained highlight: #content# ───────────────────────────
            // Boundary-checked like *, _, `: opening # must not be preceded by a word char
            // and must not be followed by whitespace; closing # must not be preceded by whitespace
            // and must not be followed by a word char. This prevents matching across e.g.
            // javadoc:foo[Bar#baz] or User#name where # appears mid-word.
            if (doFormatting && c == '#' && !activeMarkers.HasFlag(ActiveMarkers.Highlight)
                && IsConstrainedOpenValid(text, i, endIndex))
            {
                int close = FindConstrainedClose(text, '#', i + 1, endIndex);
                if (close > i + 1)
                {
                    int nodeStart = i;
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
                    var children = ParseInlines(text, i + 1, close,
                        activeMarkers | ActiveMarkers.Highlight, doFormatting, doMacros, doReplacements, doPostReplacements);
                    i = close + 1;
                    nodes.Add(new HighlightInlineNode { Children = children, Source = RangeWithin(text, nodeStart, i) });
                    continue;
                }
            }

            plain.Append(c);
            i++;
            nextChar:;
        }

        FlushPlain(nodes, plain, doReplacements, doPostReplacements, text, i);
        return nodes;
    }

    /// <summary>
    /// Finds the first occurrence of <paramref name="ch"/> in <paramref name="text"/>
    /// between <paramref name="startIndex"/> (inclusive) and <paramref name="endIndex"/> (exclusive).
    /// Returns -1 if not found.
    /// </summary>
    private static int IndexOf(string text, char ch, int startIndex, int endIndex)
    {
        for (int i = startIndex; i < endIndex; i++)
        {
            if (text[i] == ch) return i;
        }
        return -1;
    }

    /// <summary>
    /// Returns true if <paramref name="c"/> is a word character (letter, digit, or underscore).
    /// Constrained formatting markers cannot open when preceded by a word char,
    /// and cannot close when followed by a word char.
    /// </summary>
    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Checks whether position <paramref name="pos"/> is a valid location for a
    /// constrained opening marker. The marker must not be preceded by a word character,
    /// and the character immediately after must not be whitespace.
    /// </summary>
    private static bool IsConstrainedOpenValid(string text, int pos, int endIndex)
    {
        if (pos > 0 && IsWordChar(text[pos - 1]))
            return false;
        if (pos + 1 >= endIndex || char.IsWhiteSpace(text[pos + 1]))
            return false;
        return true;
    }

    /// <summary>
    /// Checks whether position <paramref name="pos"/> is a valid location for a
    /// constrained closing marker. The character before the marker must not be whitespace,
    /// and the marker must not be followed by a word character.
    /// </summary>
    private static bool IsConstrainedCloseValid(string text, int pos, int endIndex)
    {
        if (pos > 0 && char.IsWhiteSpace(text[pos - 1]))
            return false;
        if (pos + 1 < endIndex && IsWordChar(text[pos + 1]))
            return false;
        return true;
    }

    /// <summary>
    /// Finds the first valid constrained closing marker for <paramref name="ch"/>
    /// in the range [<paramref name="startIndex"/>, <paramref name="endIndex"/>).
    /// A valid closing position must satisfy <see cref="IsConstrainedCloseValid"/>.
    /// Returns -1 if no valid closing marker is found.
    /// </summary>
    private static int FindConstrainedClose(string text, char ch, int startIndex, int endIndex)
    {
        for (int i = startIndex; i < endIndex; i++)
        {
            if (text[i] == ch && IsConstrainedCloseValid(text, i, endIndex))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Expands <c>{name}</c> attribute references in <paramref name="text"/>
    /// using the provided <paramref name="attributes"/> dictionary.
    /// Unknown references are left as-is.
    /// </summary>
    // When incrementCounters is false, {counter:name} references expand to their next value but
    // the counter state is NOT mutated. Used by callers that expand a line speculatively (e.g. to
    // test whether it is a block macro) and would otherwise double-increment counters.
    internal static string ExpandAttributes(string text, IReadOnlyDictionary<string, string> attributes, bool incrementCounters = true)
    {
        // Fast path: if no '{' exists, nothing to expand.
        if (!text.Contains('{')) return text;

        var sb = new StringBuilder(text.Length);
        int segmentStart = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') continue;

            // Backslash-escaped brace: \{ → literal { (skip expansion, consume backslash)
            if (i > 0 && text[i - 1] == '\\')
            {
                // Flush text before the backslash (not including it)
                if (i - 1 > segmentStart)
                    sb.Append(text.AsSpan(segmentStart, i - 1 - segmentStart));
                sb.Append('{');
                segmentStart = i + 1; // skip past the '{'
                continue;
            }

            int close = text.IndexOf('}', i + 1);
            if (close > i + 1)
            {
                var name = text[(i + 1)..close];

                // ── Inline counter expansion: {counter:name} / {counter2:name} ──
                if (name.StartsWith("counter:", StringComparison.Ordinal) || name.StartsWith("counter2:", StringComparison.Ordinal))
                {
                    bool silent = name.StartsWith("counter2:", StringComparison.Ordinal);
                    var counterSpec = name[(silent ? "counter2:".Length : "counter:".Length)..];
                    var parts = counterSpec.Split(':', 2);
                    var counterName = parts[0];
                    var seed = parts.Length > 1 ? parts[1] : null;

                    // Get current value
                    attributes.TryGetValue(counterName, out var currentVal);
                    string newVal;
                    if (currentVal == null)
                    {
                        newVal = seed ?? "1";
                    }
                    else if (int.TryParse(currentVal, out var num))
                    {
                        newVal = (num + 1).ToString();
                    }
                    else if (currentVal.Length == 1 && char.IsLetter(currentVal[0]))
                    {
                        newVal = ((char)(currentVal[0] + 1)).ToString();
                    }
                    else
                    {
                        newVal = currentVal;
                    }

                    // Store back via mutable dictionary (unless this is a non-mutating probe).
                    if (incrementCounters && attributes is IDictionary<string, string> mutable)
                        mutable[counterName] = newVal;

                    // Flush preceding plain text
                    if (i > segmentStart)
                        sb.Append(text.AsSpan(segmentStart, i - segmentStart));

                    if (!silent)
                        sb.Append(newVal);

                    i = close;
                    segmentStart = close + 1;
                    continue;
                }

                // ── Conditional attribute substitution: {foo?yes} / {foo!no} ──
                int qIdx = name.IndexOf('?');
                int bIdx = qIdx < 0 ? name.IndexOf('!') : -1; // ? takes precedence over !
                if (qIdx > 0 || bIdx > 0)
                {
                    bool isIfSet = qIdx > 0;
                    int opIdx = isIfSet ? qIdx : bIdx;
                    var attrName = name[..opIdx];
                    var condValue = name[(opIdx + 1)..];
                    if (IsValidAttributeName(attrName))
                    {
                        bool defined = attributes.ContainsKey(attrName);
                        // Flush preceding plain text
                        if (i > segmentStart)
                            sb.Append(text.AsSpan(segmentStart, i - segmentStart));
                        // {foo?yes}: emit condValue when defined; {foo!no}: emit condValue when NOT defined
                        if (isIfSet ? defined : !defined)
                            sb.Append(condValue);
                        i = close;
                        segmentStart = close + 1;
                        continue;
                    }
                }

                if (IsValidAttributeName(name) && attributes.TryGetValue(name, out var value))
                {
                    // Flush preceding plain text as a bulk copy.
                    if (i > segmentStart)
                        sb.Append(text.AsSpan(segmentStart, i - segmentStart));
                    sb.Append(value);
                    i = close; // loop will increment to close + 1
                    segmentStart = close + 1;
                    continue;
                }
            }
            // Not a valid/known attribute reference — leave '{' in place, continue scanning.
        }

        // If no substitutions occurred, return the original string (no allocation).
        if (segmentStart == 0) return text;

        // Flush any remaining text after the last substitution.
        if (segmentStart < text.Length)
            sb.Append(text.AsSpan(segmentStart));

        return sb.ToString();
    }

    /// <summary>
    /// Validates that a string is a legal attribute name: starts with a letter or underscore,
    /// followed by letters, digits, underscores, or hyphens.
    /// </summary>
    private static bool IsValidAttributeName(string name)
    {
        if (name.Length == 0) return false;

        char first = name[0];
        if (!char.IsLetter(first) && first != '_') return false;

        for (int j = 1; j < name.Length; j++)
        {
            char c = name[j];
            if (!char.IsLetterOrDigit(c) && c != '_' && c != '-') return false;
        }

        return true;
    }

    /// <summary>
    /// Tries to parse an inline macro at position <paramref name="pos"/> in <paramref name="text"/>.
    /// Supported forms: <c>link:target[label]</c> and <c>image:target[alt]</c>.
    /// Single colon only (double-colon is a block macro, handled by BlockParser).
    /// </summary>
    private static bool TryParseInlineMacro(string text, int pos, int endIndex, IReadOnlyDictionary<string, string>? attributes, out InlineNode node, out int endPos)
    {
        node = null!;
        endPos = pos;

        // Try "link:" (5), "image:" (6), or "anchor:" (7).
#if !NETSTANDARD2_0
        ReadOnlySpan<char> span = text.AsSpan(pos, Math.Min(8, endIndex - pos));
#else
        string span = text.AsSpan(pos, Math.Min(8, endIndex - pos));
#endif

        string? macroName = null;
        int colonOffset = 0;

        if (span.StartsWith("link:") && (span.Length < 6 || span[5] != ':'))
        {
            macroName = "link";
            colonOffset = 5; // position after "link:"
        }
        else if (span.StartsWith("image:") && (span.Length < 7 || span[6] != ':'))
        {
            macroName = "image";
            colonOffset = 6; // position after "image:"
        }
        else if (span.StartsWith("anchor:") && (span.Length < 8 || span[7] != ':'))
        {
            macroName = "anchor";
            colonOffset = 7; // position after "anchor:"
        }

        if (macroName is null) return false;

        int targetStart = pos + colonOffset;

        // Find the opening bracket within bounds.
        int openBracket = -1;
        for (int j = targetStart; j < endIndex; j++)
        {
            if (text[j] == '[') { openBracket = j; break; }
        }
        if (openBracket < 0) return false;

        // Target is the text between the colon and the opening bracket.
        var target = text[targetStart..openBracket];
        if (target.Length == 0 && macroName == "link") return false; // link: requires a target

        // Find the closing bracket within bounds. No nesting support.
        int closeBracket = -1;
        for (int j = openBracket + 1; j < endIndex; j++)
        {
            if (text[j] == ']') { closeBracket = j; break; }
        }
        if (closeBracket < 0) return false;

        var bracketContent = text[(openBracket + 1)..closeBracket];

        if (macroName == "link")
        {
            bool linkattrs = attributes is not null && attributes.ContainsKey("linkattrs");
            if (linkattrs && bracketContent.Contains(','))
            {
                ParseLinkAttributes(bracketContent, target, out node);
            }
            else
            {
                var label = bracketContent.Length > 0 ? bracketContent : target;
                string? window = null;
                if (label.EndsWith('^'))
                {
                    label = label[..^1];
                    window = "_blank";
                }
                node = new InlineLinkMacroNode { Url = target, Label = label, Window = window };
            }
        }
        else if (macroName == "anchor")
        {
            if (target.Length == 0) return false; // anchor: requires an id
            var reftext = bracketContent.Length > 0 ? bracketContent : null;
            node = new InlineAnchorNode { Id = target, Reftext = reftext };
        }
        else // image
        {
            var imgAttrs = BlockParser.ParseImageAttributes(bracketContent);
            node = new InlineImageNode
            {
                Target = target,
                Alt = imgAttrs.Alt,
                Width = imgAttrs.Width,
                Height = imgAttrs.Height,
            };
        }

        endPos = closeBracket + 1;
        return true;
    }

    /// <summary>
    /// Parses link bracket content with named attributes (when :linkattrs: is enabled).
    /// Format: "label, window=_blank, role=external"
    /// </summary>
    private static void ParseLinkAttributes(string bracketContent, string target, out InlineNode node)
    {
        string label = "";
        string? window = null;
        string? role = null;

        var parts = bracketContent.Split(',');
        for (int pi = 0; pi < parts.Length; pi++)
        {
            var part = parts[pi].Trim();
            var eqIdx = part.IndexOf('=');
            if (eqIdx > 0)
            {
                var key = part[..eqIdx].Trim();
                var value = part[(eqIdx + 1)..].Trim();
                // Strip surrounding quotes
                if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
                    value = value[1..^1];
                if (string.Equals(key, "window", StringComparison.OrdinalIgnoreCase))
                    window = value;
                else if (string.Equals(key, "role", StringComparison.OrdinalIgnoreCase))
                    role = value;
            }
            else if (pi == 0)
            {
                label = part;
            }
        }

        if (label.Length == 0)
            label = target;

        node = new InlineLinkMacroNode { Url = target, Label = label, Window = window, Role = role };
    }

    /// <summary>
    /// Tries to parse a footnote macro at position <paramref name="pos"/> in <paramref name="text"/>.
    /// Supported forms: <c>footnote:[text]</c>, <c>footnote:id[text]</c>, <c>footnote:id[]</c>.
    /// </summary>
    private static bool TryParseFootnoteMacro(
        string text, int pos, int endIndex, bool doFormatting, bool doMacros, bool doReplacements, bool doPostReplacements,
        out FootnoteInlineNode node, out int endPos)
    {
        node = null!;
        endPos = pos;

        var span = text.AsSpan(pos, Math.Min(10, endIndex - pos));
        if (!span.StartsWith("footnote:")) return false;

        // Ensure it's not a block macro (footnote::)
        int afterColon = pos + 9; // position after "footnote:"
        if (afterColon < endIndex && text[afterColon] == ':') return false;

        // Find the opening bracket
        int openBracket = -1;
        for (int j = afterColon; j < endIndex; j++)
        {
            if (text[j] == '[') { openBracket = j; break; }
            // Only allow word characters in the ID
            if (!char.IsLetterOrDigit(text[j]) && text[j] != '_' && text[j] != '-') return false;
        }
        if (openBracket < 0) return false;

        // Find the closing bracket (handle nested brackets for inline content)
        int closeBracket = FindMatchingCloseBracket(text, openBracket, endIndex);
        if (closeBracket < 0) return false;

        // Extract ID (between colon and bracket) — empty string means anonymous
        var idText = text[afterColon..openBracket];
        string? id = idText.Length > 0 ? idText : null;

        // Extract bracket content
        var bracketContent = text[(openBracket + 1)..closeBracket];

        if (bracketContent.Length == 0 && id is not null)
        {
            // Back-reference: footnote:id[]
            node = new FootnoteInlineNode { Id = id, Text = null, Inlines = [] };
        }
        else
        {
            // Anonymous or named with text
            var inlines = ParseInlines(bracketContent, 0, bracketContent.Length,
                ActiveMarkers.None, doFormatting, doMacros, doReplacements, doPostReplacements);
            node = new FootnoteInlineNode { Id = id, Text = bracketContent, Inlines = inlines };
        }

        endPos = closeBracket + 1;
        return true;
    }

    /// <summary>
    /// Tries to parse an xref macro at position <paramref name="pos"/> in <paramref name="text"/>.
    /// Supported form: <c>xref:path#id[label]</c> or <c>xref:path[label]</c>.
    /// </summary>
    private static bool TryParseXrefMacro(string text, int pos, int endIndex, out InterDocumentXrefNode node, out int endPos)
    {
        node = null!;
        endPos = pos;

        var span = text.AsSpan(pos, Math.Min(5, endIndex - pos));
        if (!span.StartsWith("xref:")) return false;

        // Ensure it's not a block macro (xref::)
        int afterColon = pos + 5;
        if (afterColon < endIndex && text[afterColon] == ':') return false;

        // Scan path characters until '['
        int openBracket = -1;
        for (int j = afterColon; j < endIndex; j++)
        {
            if (text[j] == '[') { openBracket = j; break; }
            char ch = text[j];
            if (!(char.IsLetterOrDigit(ch) || ch == '/' || ch == '.' || ch == '-' || ch == '_' || ch == '#' || ch == ':'))
                return false;
        }
        if (openBracket < 0 || openBracket == afterColon) return false;

        // Find closing bracket
        int closeBracket = -1;
        for (int j = openBracket + 1; j < endIndex; j++)
        {
            if (text[j] == ']') { closeBracket = j; break; }
        }
        if (closeBracket < 0) return false;

        var target = text[afterColon..openBracket];
        var label = text[(openBracket + 1)..closeBracket];

        // Split target on '#' for optional fragment
        int hashIdx = target.IndexOf('#');
        string path;
        string? id;
        if (hashIdx >= 0)
        {
            path = target[..hashIdx];
            var fragment = target[(hashIdx + 1)..];
            id = fragment.Length > 0 ? fragment : null;
        }
        else
        {
            path = target;
            id = null;
        }

        node = new InterDocumentXrefNode
        {
            Path = path,
            Id = id,
            Label = label.Length > 0 ? label : null,
        };
        endPos = closeBracket + 1;
        return true;
    }

    private static readonly HashSet<string> KnownMacroNames = ["kbd", "btn", "menu", "icon", "stem", "latexmath", "asciimath", "indexterm", "indexterm2"];
    private static readonly HashSet<string> StemMacroNames = ["stem", "latexmath", "asciimath"];

    /// <summary>
    /// Tries to parse a generic inline macro (kbd, btn, menu) at position <paramref name="pos"/>.
    /// Forms: <c>name:[content]</c> or <c>name:target[content]</c>.
    /// </summary>
    private static bool TryParseGenericMacro(string text, int pos, int endIndex,
        IReadOnlyDictionary<string, string>? attributes,
        out InlineNode node, out int endPos)
    {
        node = null!;
        endPos = pos;

        // Scan forward to find a colon that terminates a word
        int colonIdx = -1;
        for (int j = pos; j < endIndex; j++)
        {
            if (text[j] == ':') { colonIdx = j; break; }
            if (!char.IsLetterOrDigit(text[j])) return false;
        }
        if (colonIdx < 0 || colonIdx == pos) return false;

        var name = text[pos..colonIdx];
        if (!KnownMacroNames.Contains(name)) return false;

        // Don't match block macros (::)
        int afterColon = colonIdx + 1;
        if (afterColon < endIndex && text[afterColon] == ':') return false;

        // Find opening bracket
        int openBracket = -1;
        for (int j = afterColon; j < endIndex; j++)
        {
            if (text[j] == '[') { openBracket = j; break; }
            // Target chars: allow letters, digits, spaces, and common punctuation but not whitespace-only
            if (char.IsWhiteSpace(text[j]) && text[j] != ' ') return false;
        }
        if (openBracket < 0) return false;

        var target = text[afterColon..openBracket];

        // Find closing bracket
        int closeBracket = -1;
        for (int j = openBracket + 1; j < endIndex; j++)
        {
            if (text[j] == ']') { closeBracket = j; break; }
        }
        if (closeBracket < 0) return false;

        var content = text[(openBracket + 1)..closeBracket];

        if (StemMacroNames.Contains(name))
        {
            // stem:[...] resolves to the document's :stem: interpreter (default asciimath, matching
            // Asciidoctor); latexmath:[...] / asciimath:[...] are explicit.
            var stemType = name == "stem"
                ? (attributes is not null && attributes.TryGetValue("stem", out var s) && s.Length > 0
                    ? s.ToLowerInvariant() : "asciimath")
                : name;
            node = new StemInlineNode { Content = content, StemType = stemType };
        }
        else if (name == "indexterm")
        {
            // indexterm:[primary, secondary, tertiary] — hidden index term (same as (((term))))
            var terms = content.Split(',').Select(t => t.Trim()).ToArray();
            node = new IndexTermHiddenNode { Terms = terms };
        }
        else if (name == "indexterm2")
        {
            // indexterm2:[primary, secondary, tertiary] — visible index term (same as ((term)))
            var terms = content.Split(',').Select(t => t.Trim()).ToArray();
            node = new IndexTermNode { Terms = terms };
        }
        else
        {
            node = new InlineMacroNode { Name = name, Target = target, Content = content };
        }
        endPos = closeBracket + 1;
        return true;
    }

    /// <summary>
    /// Finds the matching closing bracket ']' for an opening bracket at <paramref name="openBracket"/>,
    /// handling nested bracket pairs.
    /// </summary>
    private static int FindMatchingCloseBracket(string text, int openBracket, int endIndex)
    {
        int depth = 1;
        for (int j = openBracket + 1; j < endIndex; j++)
        {
            if (text[j] == '[') depth++;
            else if (text[j] == ']')
            {
                depth--;
                if (depth == 0) return j;
            }
        }
        return -1;
    }

    /// <summary>
    /// Parses a comma-separated list of substitution names (e.g., "quotes,attributes")
    /// into a <see cref="SubstitutionKind"/> flags value.
    /// An empty string maps to <see cref="SubstitutionKind.None"/>.
    /// </summary>
    private static SubstitutionKind ParseSubstitutionNames(string names)
    {
        if (names.Length == 0)
            return SubstitutionKind.None;

        var result = SubstitutionKind.None;
        foreach (var part in names.Split(','))
        {
            var name = part.Trim();
            result |= name switch
            {
                "quotes" => SubstitutionKind.Quotes,
                "attributes" => SubstitutionKind.Attributes,
                "macros" => SubstitutionKind.Macros,
                "post_replacements" => SubstitutionKind.PostReplacements,
                "specialcharacters" or "specialchars" => SubstitutionKind.SpecialCharacters,
                "replacements" => SubstitutionKind.Replacements,
                "normal" => SubstitutionKind.Normal,
                "verbatim" => SubstitutionKind.Verbatim,
                "none" => SubstitutionKind.None,
                _ => SubstitutionKind.None,
            };
        }
        return result;
    }

    private static void FlushPlain(List<InlineNode> nodes, StringBuilder sb, bool doReplacements, bool doPostReplacements)
        => FlushPlain(nodes, sb, doReplacements, doPostReplacements, SourceRange.None);

    private static void FlushPlain(
        List<InlineNode> nodes, StringBuilder sb, bool doReplacements, bool doPostReplacements,
        string sourceText, int currentIndex)
    {
        // `sb.Length` is the length of the run of plain text accumulated so
        // far. The run started at `currentIndex - sb.Length` in `sourceText`.
        // This is approximate when an escape or attribute substitution made
        // the buffer length diverge from the input length, but for typical
        // paragraph content it is correct.
        int startIdx = currentIndex - sb.Length;
        if (startIdx < 0) startIdx = 0;
        var range = RangeWithin(sourceText, startIdx, currentIndex);
        FlushPlain(nodes, sb, doReplacements, doPostReplacements, range);
    }

    private static void FlushPlain(List<InlineNode> nodes, StringBuilder sb, bool doReplacements, bool doPostReplacements, SourceRange range)
    {
        if (sb.Length == 0) return;
        var text = sb.ToString();
        if (doReplacements)
            text = ReplacementsProcessor.Apply(text);
        if (doPostReplacements)
            text = SmartPunctuationProcessor.Apply(text);
        nodes.Add(new TextInlineNode { Value = text, Source = range });
        sb.Clear();
    }

    // Memoized newline-offset index for the most recently queried text. Every
    // inline node calls PositionWithin twice (range start + end), and the whole
    // block shares one `text` instance, so caching the newline offsets turns the
    // per-node O(offset) rescan into an O(log n) binary search — i.e. the inline
    // source-range pass drops from O(n·k) to O(n + k·log n). ThreadStatic keeps
    // concurrent parses isolated; the cache is pure (read-only) memoization.
    [ThreadStatic] private static string? _lineIndexText;
    [ThreadStatic] private static int[]? _lineIndexNewlines;

    /// <summary>
    /// Converts a 0-based character offset into <paramref name="text"/> into a
    /// 1-based <see cref="SourcePosition"/>. The returned position is RELATIVE
    /// to <paramref name="text"/> (line 1, col 1 at the start of the buffer);
    /// the block parser that owns the surrounding context can shift these
    /// into document coordinates if it wants document-absolute ranges.
    /// </summary>
    internal static SourcePosition PositionWithin(string text, int charOffset)
    {
        int limit = Math.Min(charOffset, text.Length);

        if (!ReferenceEquals(_lineIndexText, text))
        {
            _lineIndexText = text;
            _lineIndexNewlines = BuildNewlineIndex(text);
        }
        var newlines = _lineIndexNewlines!;

        // lower_bound: number of newline offsets strictly less than `limit` is
        // both the count of line breaks before the offset and the 0-based line.
        int lo = 0, hi = newlines.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (newlines[mid] < limit) lo = mid + 1;
            else hi = mid;
        }
        int line = lo + 1;
        int lineStart = lo == 0 ? 0 : newlines[lo - 1] + 1;
        int col = limit - lineStart + 1;
        return new SourcePosition(line, col);
    }

    private static int[] BuildNewlineIndex(string text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') count++;
        if (count == 0) return Array.Empty<int>();

        var offsets = new int[count];
        int k = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') offsets[k++] = i;
        return offsets;
    }

    /// <summary>
    /// Builds a slice-relative <see cref="SourceRange"/> covering character
    /// offsets <c>[startIdx, endIdxExclusive)</c> in <paramref name="text"/>.
    /// Returns <see cref="SourceRange.None"/> for empty or invalid spans.
    /// </summary>
    internal static SourceRange RangeWithin(string text, int startIdx, int endIdxExclusive)
    {
        if (string.IsNullOrEmpty(text) || endIdxExclusive <= startIdx) return SourceRange.None;
        var start = PositionWithin(text, startIdx);
        // SourceRange end is INCLUSIVE in the AdocNet model, so we point at the
        // last character of the range, not one past it.
        var end = PositionWithin(text, endIdxExclusive - 1);
        return new SourceRange(start, end);
    }
}
