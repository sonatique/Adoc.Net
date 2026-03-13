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

        return ParseInlines(text, 0, text.Length, ActiveMarkers.None, doFormatting, doMacros, doReplacements, doPostReplacements, doExperimental);
    }

    /// <summary>
    /// Recursive inline parser. Scans from <paramref name="startIndex"/> to <paramref name="endIndex"/>
    /// within <paramref name="text"/>, respecting <paramref name="activeMarkers"/> to prevent
    /// self-nesting of formatting markers.
    /// </summary>
    private static List<InlineNode> ParseInlines(
        string text, int startIndex, int endIndex,
        ActiveMarkers activeMarkers, bool doFormatting, bool doMacros, bool doReplacements, bool doPostReplacements,
        bool doExperimental = false)
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
                if (doFormatting && (next == '*' || next == '_' || next == '`' || next == '<' || next == '+' || next == '^' || next == '~' || next == '#'))
                {
                    plain.Append(next);
                    i += 2;
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

            // ── Cross-reference: <<id>> or <<id,label>> ────────────────────
            if (doFormatting && c == '<' && i + 1 < endIndex && text[i + 1] == '<')
            {
                int closeIdx = text.IndexOf(">>", i + 2, StringComparison.Ordinal);
                if (closeIdx > i + 2 && closeIdx < endIndex)
                {
                    var inner = text[(i + 2)..closeIdx];
                    if (inner.Length > 0)
                    {
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements);
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
                        continue;
                    }
                }
            }

            // ── Inline anchor: [[id]] ─────────────────────────────────────────
            if (doFormatting && c == '[' && i + 1 < endIndex && text[i + 1] == '[')
            {
                int closeIdx = text.IndexOf("]]", i + 2, StringComparison.Ordinal);
                if (closeIdx > i + 2 && closeIdx + 2 <= endIndex)
                {
                    var id = text[(i + 2)..closeIdx];
                    if (id.Length > 0)
                    {
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                        nodes.Add(new InlineAnchorNode { Id = id });
                        i = closeIdx + 2;
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
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    nodes.Add(new PassthroughInlineNode { Content = text[(i + 3)..close] });
                    i = close + 3;
                    continue;
                }
            }

            // ── Inline passthrough: +content+ ───────────────────────────────
            if (doFormatting && c == '+')
            {
                int close = IndexOf(text, '+', i + 1, endIndex);
                if (close > i + 1)
                {
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    nodes.Add(new PassthroughInlineNode { Content = text[(i + 1)..close] });
                    i = close + 1;
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
                            var subsText = text[(i + 5)..openBracket];
                            var content = text[(openBracket + 1)..closeBracket];
                            var subs = ParseSubstitutionNames(subsText);

                            FlushPlain(nodes, plain, doReplacements, doPostReplacements);

                            if (subs != SubstitutionKind.None)
                            {
                                // Re-parse content with the requested substitutions
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
                                nodes.Add(new PassthroughInlineNode { Content = content });
                            }

                            i = closeBracket + 1;
                            continue;
                        }
                    }
                }
            }

            // ── Inline macros: link:target[label], image:target[alt] ──────────
            if (doMacros && (c == 'l' || c == 'i'))
            {
                if (TryParseInlineMacro(text, i, endIndex, out var macroNode, out var macroEnd))
                {
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    nodes.Add(macroNode);
                    i = macroEnd;
                    continue;
                }
            }

            // ── Footnote macro: footnote:[text], footnote:id[text], footnote:id[] ─
            if (doMacros && c == 'f')
            {
                if (TryParseFootnoteMacro(text, i, endIndex, doFormatting, doMacros, doReplacements, doPostReplacements, out var footnoteNode, out var fnEnd))
                {
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    nodes.Add(footnoteNode);
                    i = fnEnd;
                    continue;
                }
            }

            // ── Xref macro: xref:path[label] or xref:path#id[label] ──────────
            if (doMacros && c == 'x')
            {
                if (TryParseXrefMacro(text, i, endIndex, out var xrefNode, out var xrefEnd))
                {
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    nodes.Add(xrefNode);
                    i = xrefEnd;
                    continue;
                }
            }

            // ── Generic inline macros: kbd:[...], btn:[...], menu:target[...], icon:name[...] ──
            // kbd:, btn:, menu: require :experimental: attribute; icon: is always available
            if (doMacros && (c == 'k' || c == 'b' || c == 'm' || c == 'i'))
            {
                // Skip kbd/btn/menu when :experimental: is not set
                bool isExperimentalMacro = c != 'i'; // icon: is always available
                if (!isExperimentalMacro || doExperimental)
                {
                    if (TryParseGenericMacro(text, i, endIndex, out var genericMacro, out var gmEnd))
                    {
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                        nodes.Add(genericMacro);
                        i = gmEnd;
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

                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
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
                            // Strip trailing ^ (opens in new window indicator)
                            if (label.EndsWith('^'))
                                label = label[..^1];
                            nodes.Add(new InlineLinkMacroNode { Url = url, Label = label });
                            i = closeBracket + 1;
                        }
                        else if (closeBracket == i + 1)
                        {
                            // Empty brackets: URL[] — use URL as label
                            nodes.Add(new LinkInlineNode { Url = url });
                            i = closeBracket + 1;
                        }
                        else
                        {
                            nodes.Add(new LinkInlineNode { Url = url });
                        }
                    }
                    else
                    {
                        nodes.Add(new LinkInlineNode { Url = url });
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
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                        nodes.Add(new InlineLinkMacroNode { Url = "mailto:" + email, Label = email });
                        i = domainEnd;
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
                        var inner = text[(i + 3)..closeIdx];
                        var terms = inner.Split(',').Select(t => t.Trim()).ToArray();
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                        nodes.Add(new IndexTermHiddenNode { Terms = terms });
                        i = closeIdx + 3;
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
                            var inner = text[(i + 2)..closeIdx];
                            var terms = inner.Split(',').Select(t => t.Trim()).ToArray();
                            FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                            nodes.Add(new IndexTermNode { Terms = terms });
                            i = closeIdx + 2;
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
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    var children = ParseInlines(text, i + 2, close,
                        activeMarkers | ActiveMarkers.Strong, doFormatting, doMacros, doReplacements, doPostReplacements);
                    nodes.Add(new StrongInlineNode { Children = children });
                    i = close + 2;
                    continue;
                }
            }

            // ── Constrained strong: *content* ───────────────────────────────
            if (doFormatting && c == '*' && !activeMarkers.HasFlag(ActiveMarkers.Strong))
            {
                int close = IndexOf(text, '*', i + 1, endIndex);
                if (close > i + 1)
                {
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    var children = ParseInlines(text, i + 1, close,
                        activeMarkers | ActiveMarkers.Strong, doFormatting, doMacros, doReplacements, doPostReplacements);
                    nodes.Add(new StrongInlineNode { Children = children });
                    i = close + 1;
                    continue;
                }
            }

            // ── Unconstrained emphasis: __content__ ─────────────────────────
            if (doFormatting && c == '_' && i + 1 < endIndex && text[i + 1] == '_'
                && !activeMarkers.HasFlag(ActiveMarkers.Emphasis)
                && !activeMarkers.HasFlag(ActiveMarkers.Monospace))
            {
                int close = text.IndexOf("__", i + 2, StringComparison.Ordinal);
                if (close > i + 2 && close + 2 <= endIndex)
                {
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    var children = ParseInlines(text, i + 2, close,
                        activeMarkers | ActiveMarkers.Emphasis, doFormatting, doMacros, doReplacements, doPostReplacements);
                    nodes.Add(new EmphasisInlineNode { Children = children });
                    i = close + 2;
                    continue;
                }
            }

            // ── Constrained emphasis: _content_ ─────────────────────────────
            if (doFormatting && c == '_' && !activeMarkers.HasFlag(ActiveMarkers.Emphasis)
                && !activeMarkers.HasFlag(ActiveMarkers.Monospace))
            {
                int close = IndexOf(text, '_', i + 1, endIndex);
                if (close > i + 1)
                {
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    var children = ParseInlines(text, i + 1, close,
                        activeMarkers | ActiveMarkers.Emphasis, doFormatting, doMacros, doReplacements, doPostReplacements);
                    nodes.Add(new EmphasisInlineNode { Children = children });
                    i = close + 1;
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
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements);
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
                        FlushPlain(nodes, plain, doReplacements, doPostReplacements);
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
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    var children = ParseInlines(text, i + 2, close,
                        activeMarkers | ActiveMarkers.Monospace, doFormatting, doMacros, doReplacements: false, doPostReplacements: false);
                    nodes.Add(new MonospaceInlineNode { Children = children });
                    i = close + 2;
                    continue;
                }
            }

            // ── Monospace: `content` ──────────────────────────────────────────
            if (doFormatting && c == '`' && !activeMarkers.HasFlag(ActiveMarkers.Monospace))
            {
                int close = IndexOf(text, '`', i + 1, endIndex);
                if (close > i + 1)
                {
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    var children = ParseInlines(text, i + 1, close,
                        activeMarkers | ActiveMarkers.Monospace, doFormatting, doMacros, doReplacements: false, doPostReplacements: false);
                    nodes.Add(new MonospaceInlineNode { Children = children });
                    i = close + 1;
                    continue;
                }
            }

            // ── Superscript: ^content^ ────────────────────────────────────────
            if (doFormatting && c == '^')
            {
                int close = IndexOf(text, '^', i + 1, endIndex);
                if (close > i + 1)
                {
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    nodes.Add(new SuperscriptInlineNode { Content = text[(i + 1)..close] });
                    i = close + 1;
                    continue;
                }
            }

            // ── Subscript: ~content~ ──────────────────────────────────────────
            if (doFormatting && c == '~')
            {
                int close = IndexOf(text, '~', i + 1, endIndex);
                if (close > i + 1)
                {
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    nodes.Add(new SubscriptInlineNode { Content = text[(i + 1)..close] });
                    i = close + 1;
                    continue;
                }
            }

            // ── Custom span roles: [.role]#text#, [.role]*text*, [.role]_text_, [.role]`text` ───────
            if (doFormatting && c == '[' && i + 2 < endIndex && text[i + 1] == '.')
            {
                int closeBracket = text.IndexOf(']', i + 2);
                if (closeBracket > i + 2 && closeBracket + 1 < endIndex)
                {
                    char marker = text[closeBracket + 1];
                    if (marker == '#' && !activeMarkers.HasFlag(ActiveMarkers.Highlight))
                    {
                        // Parse roles from [.role1.role2]
                        var rolesStr = text[(i + 1)..closeBracket];
                        var roles = rolesStr.Split('.', StringSplitOptions.RemoveEmptyEntries).ToList();
                        if (roles.Count > 0)
                        {
                            int contentStart;
                            int close;
                            bool isUnconstrained = closeBracket + 2 < endIndex && text[closeBracket + 2] == '#';
                            if (isUnconstrained)
                            {
                                // [.role]##content##
                                contentStart = closeBracket + 3;
                                close = text.IndexOf("##", contentStart, StringComparison.Ordinal);
                                if (close > contentStart && close + 2 <= endIndex)
                                {
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Highlight, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    nodes.Add(new HighlightInlineNode { Children = children, Roles = roles });
                                    i = close + 2;
                                    continue;
                                }
                            }
                            else
                            {
                                // [.role]#content#
                                contentStart = closeBracket + 2;
                                close = IndexOf(text, '#', contentStart, endIndex);
                                if (close > contentStart)
                                {
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Highlight, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    nodes.Add(new HighlightInlineNode { Children = children, Roles = roles });
                                    i = close + 1;
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
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Strong, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    nodes.Add(new StrongInlineNode { Children = children, Roles = roles });
                                    i = close + 2;
                                    continue;
                                }
                            }
                            else
                            {
                                int contentStart = closeBracket + 2;
                                int close = IndexOf(text, '*', contentStart, endIndex);
                                if (close > contentStart)
                                {
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Strong, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    nodes.Add(new StrongInlineNode { Children = children, Roles = roles });
                                    i = close + 1;
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
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Emphasis, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    nodes.Add(new EmphasisInlineNode { Children = children, Roles = roles });
                                    i = close + 2;
                                    continue;
                                }
                            }
                            else
                            {
                                int contentStart = closeBracket + 2;
                                int close = IndexOf(text, '_', contentStart, endIndex);
                                if (close > contentStart)
                                {
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Emphasis, doFormatting, doMacros, doReplacements, doPostReplacements);
                                    nodes.Add(new EmphasisInlineNode { Children = children, Roles = roles });
                                    i = close + 1;
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
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Monospace, doFormatting, doMacros, doReplacements: false, doPostReplacements: false);
                                    nodes.Add(new MonospaceInlineNode { Children = children, Roles = roles });
                                    i = close + 2;
                                    continue;
                                }
                            }
                            else
                            {
                                int contentStart = closeBracket + 2;
                                int close = IndexOf(text, '`', contentStart, endIndex);
                                if (close > contentStart)
                                {
                                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                                    var children = ParseInlines(text, contentStart, close,
                                        activeMarkers | ActiveMarkers.Monospace, doFormatting, doMacros, doReplacements: false, doPostReplacements: false);
                                    nodes.Add(new MonospaceInlineNode { Children = children, Roles = roles });
                                    i = close + 1;
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
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    var children = ParseInlines(text, i + 2, close,
                        activeMarkers | ActiveMarkers.Highlight, doFormatting, doMacros, doReplacements, doPostReplacements);
                    nodes.Add(new HighlightInlineNode { Children = children });
                    i = close + 2;
                    continue;
                }
            }

            // ── Constrained highlight: #content# ───────────────────────────
            if (doFormatting && c == '#' && !activeMarkers.HasFlag(ActiveMarkers.Highlight))
            {
                int close = IndexOf(text, '#', i + 1, endIndex);
                if (close > i + 1)
                {
                    FlushPlain(nodes, plain, doReplacements, doPostReplacements);
                    var children = ParseInlines(text, i + 1, close,
                        activeMarkers | ActiveMarkers.Highlight, doFormatting, doMacros, doReplacements, doPostReplacements);
                    nodes.Add(new HighlightInlineNode { Children = children });
                    i = close + 1;
                    continue;
                }
            }

            plain.Append(c);
            i++;
            nextChar:;
        }

        FlushPlain(nodes, plain, doReplacements, doPostReplacements);
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
    /// Expands <c>{name}</c> attribute references in <paramref name="text"/>
    /// using the provided <paramref name="attributes"/> dictionary.
    /// Unknown references are left as-is.
    /// </summary>
    internal static string ExpandAttributes(string text, IReadOnlyDictionary<string, string> attributes)
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

                    // Store back via mutable dictionary
                    if (attributes is IDictionary<string, string> mutable)
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
    private static bool TryParseInlineMacro(string text, int pos, int endIndex, out InlineNode node, out int endPos)
    {
        node = null!;
        endPos = pos;

        // Try "link:" (5 chars) or "image:" (6 chars).
#if NET10_0_OR_GREATER
        ReadOnlySpan<char> span = text.AsSpan(pos, Math.Min(7, endIndex - pos));
#else
        string span = text.AsSpan(pos, Math.Min(7, endIndex - pos));
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
            var label = bracketContent.Length > 0 ? bracketContent : target;
            node = new InlineLinkMacroNode { Url = target, Label = label };
        }
        else // image
        {
            node = new InlineImageNode { Target = target, Alt = BlockParser.ParseImageAlt(bracketContent) };
        }

        endPos = closeBracket + 1;
        return true;
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
            if (!(char.IsLetterOrDigit(ch) || ch == '/' || ch == '.' || ch == '-' || ch == '_' || ch == '#'))
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

    private static readonly HashSet<string> KnownMacroNames = ["kbd", "btn", "menu", "icon"];

    /// <summary>
    /// Tries to parse a generic inline macro (kbd, btn, menu) at position <paramref name="pos"/>.
    /// Forms: <c>name:[content]</c> or <c>name:target[content]</c>.
    /// </summary>
    private static bool TryParseGenericMacro(string text, int pos, int endIndex,
        out InlineMacroNode node, out int endPos)
    {
        node = null!;
        endPos = pos;

        // Scan forward to find a colon that terminates a word
        int colonIdx = -1;
        for (int j = pos; j < endIndex; j++)
        {
            if (text[j] == ':') { colonIdx = j; break; }
            if (!char.IsLetter(text[j])) return false;
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

        node = new InlineMacroNode { Name = name, Target = target, Content = content };
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
    {
        if (sb.Length == 0) return;
        var text = sb.ToString();
        if (doReplacements)
            text = ReplacementsProcessor.Apply(text);
        if (doPostReplacements)
            text = SmartPunctuationProcessor.Apply(text);
        nodes.Add(new TextInlineNode { Value = text });
        sb.Clear();
    }
}
