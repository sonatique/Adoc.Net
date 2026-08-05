using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AdocNet.Ast;

namespace AdocNet.Importers.Docx;

/// <summary>
/// Walks <c>w:body</c> and builds the block structure: sections from heading
/// styles, lists from numbering, listing blocks from code styles, quotes,
/// admonitions, tables, images and page breaks.
/// </summary>
internal sealed class BlockConverter
{
    private static readonly Regex AdmonitionPrefix = new(
        @"^(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\s*:\s+", RegexOptions.CultureInvariant);

    private static readonly string[] CodeStyleNames =
    {
        "HTMLPreformatted", "PlainText", "Code", "SourceCode", "Preformatted", "CodeBlock",
    };

    private static readonly string[] QuoteStyleNames =
    {
        "Quote", "IntenseQuote", "BlockQuote", "BlockText",
    };

    private static readonly Dictionary<string, string> AdmonitionStyleNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Note"] = "NOTE",
            ["Tip"] = "TIP",
            ["Important"] = "IMPORTANT",
            ["Warning"] = "WARNING",
            ["Caution"] = "CAUTION",
        };

    private readonly ConversionContext _ctx;
    private readonly DocumentNode _root;
    private readonly bool _nested;
    private readonly List<SectionNode> _sections = new();
    private readonly List<ListLevel> _lists = new();

    private List<string>? _codeLines;
    private List<BlockNode>? _quoteBlocks;
    private string? _pendingCaption;
    private string? _pendingBlockId;
    private bool _sawFirstBlock;

    public BlockConverter(ConversionContext ctx, DocumentNode root, bool nested = false)
    {
        _ctx = ctx;
        _root = root;
        _nested = nested;
    }

    private sealed class ListLevel
    {
        public required ListNode List { get; init; }
        public required int Indent { get; init; }
        public required string NumId { get; init; }
        public ListItemNode? LastItem { get; set; }
    }

    private AstNode Container => _sections.Count > 0 ? _sections[_sections.Count - 1] : (AstNode)_root;

    public void ConvertBody(XElement body)
    {
        foreach (var element in body.Elements()) ConvertBlockElement(element);
        FlushAll();
    }

    private void ConvertBlockElement(XElement element)
    {
        var name = element.Name;

        if (name == Ns.W + "p") { ConvertParagraph(element); return; }
        if (name == Ns.W + "tbl") { ConvertTable(element); return; }

        if (name == Ns.W + "sdt")
        {
            var content = element.Element(Ns.W + "sdtContent");
            if (content is not null)
            {
                foreach (var child in content.Elements()) ConvertBlockElement(child);
            }

            return;
        }

        if (name == Ns.W + "bookmarkStart")
        {
            var bookmarkName = element.Attribute(Ns.W + "name")?.Value;
            if (bookmarkName is not null && bookmarkName != "_GoBack")
            {
                _ctx.Report.Bookmarks++;
                _ctx.Report.Count(mapped: true);
                _pendingBlockId ??= _ctx.ReserveId(AsciidocText.ToId(bookmarkName));
            }

            return;
        }

        if (name == Ns.W + "sectPr")
        {
            // Page geometry, headers/footers, columns: AsciiDoc has no model
            // for any of it, and it is presentation rather than content, so it
            // is reported once rather than counted per property.
            _ctx.Report.Add(DocxIssueSeverity.Info, "section-properties.dropped",
                "Page setup (size, margins, headers/footers, columns) is not represented in AsciiDoc.");
            return;
        }

        if (name == Ns.Mc + "AlternateContent")
        {
            var choice = element.Element(Ns.Mc + "Choice") ?? element.Element(Ns.Mc + "Fallback");
            if (choice is not null)
            {
                foreach (var child in choice.Elements()) ConvertBlockElement(child);
            }
        }
    }

    // ── Paragraphs ──────────────────────────────────────────────────────────

    private void ConvertParagraph(XElement paragraph)
    {
        _ctx.ParagraphIndex++;
        _ctx.Report.Paragraphs++;

        var pPr = paragraph.Element(Ns.W + "pPr");
        var styleId = pPr?.Element(Ns.W + "pStyle").WVal();

        if (pPr?.Element(Ns.W + "pageBreakBefore").IsToggleOn() == true)
        {
            FlushAll();
            AddBlock(new PageBreakNode());
            _ctx.Report.Count(mapped: true);
        }

        // Code paragraphs accumulate verbatim; they must not go through inline
        // conversion, which would escape their content.
        if (IsCodeParagraph(paragraph, styleId))
        {
            FlushLists();
            FlushQuote();
            (_codeLines ??= new List<string>()).Add(RawText(paragraph));
            _ctx.Report.Count(mapped: true);
            return;
        }

        FlushCode();

        var numbering = ResolveNumbering(pPr, styleId);
        var converter = new InlineConverter(_ctx, inTableCell: _nested);
        var inlines = converter.ConvertParagraph(paragraph, styleId);
        EmitPendingComments(converter);

        var headingLevel = numbering is null ? _ctx.Styles.HeadingLevel(styleId) : null;
        var blockId = HoistAnchor(inlines.Inlines) ?? TakePendingBlockId();

        if (inlines.IsEmpty && inlines.Inlines.Count == 0)
        {
            if (IsThematicBreak(pPr))
            {
                FlushAll();
                AddBlock(new ThematicBreakNode());
                _ctx.Report.Count(mapped: true);
            }
            else if (inlines.HasPageBreak)
            {
                FlushAll();
                AddBlock(new PageBreakNode());
            }

            // An empty paragraph is Word's way of adding vertical space; block
            // separation in AsciiDoc is implicit, so nothing is lost.
            return;
        }

        if (headingLevel is int level)
        {
            FlushAll();
            AddHeading(level, inlines, blockId);
            AppendTrailingPageBreak(inlines);
            return;
        }

        if (_ctx.Styles.IsOrDerivesFrom(styleId, "Title") && !_nested)
        {
            FlushAll();
            _root.Title = InlineMarkupWriter.Write(inlines.Inlines).Trim();
            _ctx.Report.Count(mapped: true);
            _sawFirstBlock = true;
            return;
        }

        if (_ctx.Styles.IsOrDerivesFrom(styleId, "Subtitle") && !_nested && _root.Title is not null)
        {
            // AsciiDoc spells a subtitle as "Title: Subtitle".
            _root.Title += ": " + InlineMarkupWriter.Write(inlines.Inlines).Trim();
            _ctx.Report.Count(mapped: true);
            return;
        }

        if (_ctx.Styles.IsOrDerivesFrom(styleId, "Caption"))
        {
            FlushLists();
            HandleCaption(InlineMarkupWriter.Write(inlines.Inlines).Trim());
            return;
        }

        if (numbering is not null)
        {
            FlushQuote();
            AddListItem(numbering, inlines, blockId);
            AppendTrailingPageBreak(inlines);
            return;
        }

        if (IsQuoteParagraph(styleId))
        {
            FlushLists();
            (_quoteBlocks ??= new List<BlockNode>()).Add(MakeParagraph(inlines, blockId));
            _ctx.Report.Count(mapped: true);
            return;
        }

        FlushQuote();

        // A paragraph holding nothing but an image becomes a block image, so
        // it can carry a caption and stand on its own.
        if (TryMakeBlockImage(inlines, blockId, out var image))
        {
            FlushLists();
            AddBlock(image!);
            AppendTrailingPageBreak(inlines);
            return;
        }

        if (TryMakeAdmonition(styleId, inlines, blockId, out var admonition))
        {
            FlushLists();
            AddBlock(admonition!);
            AppendTrailingPageBreak(inlines);
            return;
        }

        // A non-numbered paragraph styled as a list item continues the item it
        // follows (Word's way of putting a second paragraph under a bullet).
        if (_lists.Count > 0 && _ctx.Styles.IsOrDerivesFrom(styleId, "ListParagraph"))
        {
            var item = _lists[_lists.Count - 1].LastItem;
            if (item is not null)
            {
                item.AddChild(MakeParagraph(inlines, blockId));
                _ctx.Report.Count(mapped: true);
                AppendTrailingPageBreak(inlines);
                return;
            }
        }

        FlushLists();
        AddBlock(MakeParagraph(inlines, blockId));
        AppendTrailingPageBreak(inlines);
    }

    private void AppendTrailingPageBreak(InlineResult inlines)
    {
        if (!inlines.HasPageBreak) return;
        FlushLists();
        AddBlock(new PageBreakNode());
    }

    private ParagraphNode MakeParagraph(InlineResult inlines, string? id)
    {
        var node = new ParagraphNode
        {
            Text = string.Empty,
            Inlines = TrimOuterWhitespace(inlines.Inlines),
            HasHardbreaks = inlines.HasHardBreak,
        };

        if (id is not null) node.Id = id;
        _ctx.Report.Count(mapped: true);
        return node;
    }

    private void AddHeading(int level, InlineResult inlines, string? id)
    {
        var title = InlineMarkupWriter.Write(inlines.Inlines).Trim();

        if (!_nested && _root.Title is null && !_sawFirstBlock
            && level == 1 && _ctx.CoreTitle is null
            && _ctx.Options.PromoteFirstHeadingToTitle)
        {
            _root.Title = title;
            _ctx.Report.Count(mapped: true);
            _sawFirstBlock = true;
            return;
        }

        // AsciiDoc has five section levels and forbids skipping one; clamp to
        // the deepest legal level relative to the enclosing section.
        var parentLevel = _sections.Count > 0 ? _sections[_sections.Count - 1].Level : 0;
        var effective = level;
        if (effective > 5)
        {
            effective = 5;
            _ctx.Report.Approximated("heading.level-clamped",
                $"Heading level {level} clamped to 5 (AsciiDoc's deepest section level).", _ctx.ParagraphIndex);
        }

        while (_sections.Count > 0 && _sections[_sections.Count - 1].Level >= effective)
            _sections.RemoveAt(_sections.Count - 1);

        parentLevel = _sections.Count > 0 ? _sections[_sections.Count - 1].Level : 0;
        if (effective > parentLevel + 1)
        {
            effective = parentLevel + 1;
            _ctx.Report.Approximated("heading.level-normalised",
                $"Heading level {level} raised to {effective}: AsciiDoc does not allow skipping a level.",
                _ctx.ParagraphIndex);
        }

        var section = new SectionNode
        {
            Level = effective,
            Title = title,
            TitleInlines = TrimOuterWhitespace(inlines.Inlines),
        };

        if (id is not null) section.Id = id;

        Container.AddChild(section);
        _sections.Add(section);
        _sawFirstBlock = true;
        _ctx.Report.Sections++;
        _ctx.Report.Count(mapped: true);
    }

    // ── Lists ───────────────────────────────────────────────────────────────

    private sealed class NumberingRef
    {
        public required string NumId { get; init; }
        public required int Indent { get; init; }
        public required NumberingLevel? Level { get; init; }
    }

    private NumberingRef? ResolveNumbering(XElement? pPr, string? styleId)
    {
        var numPr = pPr?.Element(Ns.W + "numPr");
        var numId = numPr?.Element(Ns.W + "numId").WVal();
        var ilvlText = numPr?.Element(Ns.W + "ilvl").WVal();

        // A paragraph style can attach numbering too; direct properties win.
        numId ??= _ctx.Styles.StyleNumId(styleId);

        // numId 0 explicitly detaches the paragraph from its style's list.
        if (numId is null || numId == "0") return null;

        var indent = 0;
        if (ilvlText is not null)
            int.TryParse(ilvlText, NumberStyles.Integer, CultureInfo.InvariantCulture, out indent);

        return new NumberingRef
        {
            NumId = numId,
            Indent = indent,
            Level = _ctx.Numbering.Resolve(numId, indent),
        };
    }

    private void AddListItem(NumberingRef numbering, InlineResult inlines, string? id)
    {
        var kind = numbering.Level?.Kind ?? ListKind.Unordered;
        var listStyle = numbering.Level?.ListStyle;
        var start = numbering.Level?.Start;

        if (numbering.Level is null)
        {
            _ctx.Report.Approximated("list.numbering-missing",
                $"Numbering definition {numbering.NumId} not found; imported as an unordered list.", _ctx.ParagraphIndex);
        }
        else if (!NumberingTable.IsExactFormat(numbering.Level.NumberFormat))
        {
            _ctx.Report.Approximated("list.number-format-approximated",
                $"Number format '{numbering.Level.NumberFormat}' has no AsciiDoc equivalent; imported as a plain ordered list.",
                _ctx.ParagraphIndex);
        }

        // Close deeper levels, then decide whether the current level continues
        // the open list or starts a new one.
        while (_lists.Count > 0 && _lists[_lists.Count - 1].Indent > numbering.Indent)
            _lists.RemoveAt(_lists.Count - 1);

        ListLevel? top = _lists.Count > 0 ? _lists[_lists.Count - 1] : null;

        if (top is not null && top.Indent == numbering.Indent
            && (top.List.ListKind != kind || !string.Equals(top.NumId, numbering.NumId, StringComparison.Ordinal)))
        {
            _lists.RemoveAt(_lists.Count - 1);
            top = _lists.Count > 0 ? _lists[_lists.Count - 1] : null;
        }

        if (top is null || top.Indent < numbering.Indent)
        {
            var list = new ListNode
            {
                ListKind = kind,
                ListStyle = listStyle,
                Start = start,
            };

            if (top is null)
            {
                FlushQuote();
                Container.AddChild(list);
            }
            else if (top.LastItem is not null)
            {
                top.LastItem.AddChild(list);
            }
            else
            {
                Container.AddChild(list);
            }

            top = new ListLevel { List = list, Indent = numbering.Indent, NumId = numbering.NumId };
            _lists.Add(top);
        }

        var item = new ListItemNode
        {
            Text = string.Empty,
            Inlines = TrimOuterWhitespace(inlines.Inlines),
        };

        if (id is not null) item.Id = id;

        top.List.AddChild(item);
        top.LastItem = item;
        _sawFirstBlock = true;
        _ctx.Report.ListItems++;
        _ctx.Report.Count(mapped: true);
    }

    private void FlushLists() => _lists.Clear();

    // ── Tables ──────────────────────────────────────────────────────────────

    private void ConvertTable(XElement tbl)
    {
        FlushCode();
        FlushQuote();
        FlushLists();

        var converter = new TableConverter(_ctx);

        if (_ctx.Options.DetectAdmonitions && converter.TryConvertAdmonition(tbl, out var admonition))
        {
            AddBlock(admonition!);
            return;
        }

        var table = converter.Convert(tbl);
        if (table is null) return;

        if (_pendingCaption is not null)
        {
            table.Title = _pendingCaption;
            _pendingCaption = null;
        }

        var id = TakePendingBlockId();
        if (id is not null) table.Id = id;

        AddBlock(table);
    }

    // ── Captions, admonitions, images ───────────────────────────────────────

    private void HandleCaption(string caption)
    {
        if (caption.Length == 0) return;

        // Word puts figure captions after the image and table captions before
        // the table; attach backwards when the previous block can take a title,
        // otherwise hold it for the next one.
        var container = Container;
        if (container.Children.Count > 0
            && container.Children[container.Children.Count - 1] is BlockImageNode image
            && image.Title is null)
        {
            var replacement = new BlockImageNode
            {
                Target = image.Target,
                Alt = image.Alt,
                Title = caption,
                Width = image.Width,
                Height = image.Height,
                Link = image.Link,
                Id = image.Id,
            };

            container.RemoveChildAt(container.Children.Count - 1);
            container.AddChild(replacement);
            _ctx.Report.Count(mapped: true);
            return;
        }

        _pendingCaption = caption;
        _ctx.Report.Count(mapped: true);
    }

    private bool TryMakeBlockImage(InlineResult inlines, string? id, out BlockImageNode? image)
    {
        image = null;
        InlineImageNode? found = null;

        foreach (var node in inlines.Inlines)
        {
            if (node is InlineImageNode inlineImage)
            {
                if (found is not null) return false; // two images: keep them inline
                found = inlineImage;
                continue;
            }

            if (node is TextInlineNode text && string.IsNullOrWhiteSpace(text.Value)) continue;
            return false;
        }

        if (found is null) return false;

        image = new BlockImageNode
        {
            Target = found.Target,
            Alt = found.Alt,
            Width = found.Width,
            Height = found.Height,
            Title = _pendingCaption,
            Id = id,
        };

        _pendingCaption = null;
        return true;
    }

    private bool TryMakeAdmonition(string? styleId, InlineResult inlines, string? id, out AdmonitionNode? admonition)
    {
        admonition = null;
        if (!_ctx.Options.DetectAdmonitions) return false;

        var styleName = _ctx.Styles.CanonicalName(styleId);
        if (styleName is not null && AdmonitionStyleNames.TryGetValue(StyleTable.Squash(styleName), out var styleType))
        {
            admonition = new AdmonitionNode
            {
                AdmonitionType = styleType,
                Inlines = TrimOuterWhitespace(inlines.Inlines),
                Id = id,
            };

            _ctx.Report.Count(mapped: true);
            return true;
        }

        // Prefix form: the label must be plain text at the very start of the
        // paragraph, otherwise stripping it would drop formatting with it.
        if (inlines.Inlines.Count == 0 || inlines.Inlines[0] is not TextInlineNode first) return false;

        var match = AdmonitionPrefix.Match(first.Value);
        if (!match.Success) return false;

        var remainder = first.Value.Substring(match.Length);
        var body = new List<InlineNode>(inlines.Inlines.Count);
        if (remainder.Length > 0) body.Add(new TextInlineNode { Value = remainder });
        for (var i = 1; i < inlines.Inlines.Count; i++) body.Add(inlines.Inlines[i]);

        admonition = new AdmonitionNode
        {
            AdmonitionType = match.Groups[1].Value.ToUpperInvariant(),
            Inlines = TrimOuterWhitespace(body),
            Id = id,
        };

        _ctx.Report.Count(mapped: true);
        return true;
    }

    // ── Verbatim and quote accumulation ─────────────────────────────────────

    private bool IsCodeParagraph(XElement paragraph, string? styleId)
    {
        if (!_ctx.Options.DetectCodeBlocks) return false;

        foreach (var name in CodeStyleNames)
        {
            if (_ctx.Styles.IsOrDerivesFrom(styleId, name)) return true;
        }

        // Fall back to "every run in the paragraph is monospaced", which is how
        // pasted code usually arrives when the author never applied a style.
        var sawText = false;
        foreach (var run in paragraph.Elements(Ns.W + "r"))
        {
            var hasText = false;
            foreach (var t in run.Elements(Ns.W + "t"))
            {
                if (t.Value.Length > 0) { hasText = true; break; }
            }

            if (!hasText) continue;
            sawText = true;

            var rPr = run.Element(Ns.W + "rPr");
            var format = RunFormat.Resolve(rPr, rPr?.Element(Ns.W + "rStyle").WVal(), styleId, _ctx.Styles);
            if (!format.Monospace) return false;
        }

        return sawText;
    }

    private bool IsQuoteParagraph(string? styleId)
    {
        foreach (var name in QuoteStyleNames)
        {
            if (_ctx.Styles.IsOrDerivesFrom(styleId, name)) return true;
        }

        return false;
    }

    /// <summary>Verbatim text of a paragraph, preserving tabs and line breaks.</summary>
    private static string RawText(XElement paragraph)
    {
        var sb = new StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            var name = element.Name;
            if (name == Ns.W + "t" || name == Ns.W + "delText") sb.Append(element.Value);
            else if (name == Ns.W + "tab") sb.Append('\t');
            else if (name == Ns.W + "br" || name == Ns.W + "cr") sb.Append('\n');
            else if (name == Ns.W + "noBreakHyphen") sb.Append('-');
        }

        return sb.ToString();
    }

    private void FlushCode()
    {
        if (_codeLines is null) return;

        var content = string.Join("\n", _codeLines.ToArray());
        _codeLines = null;

        // Trailing blank lines inside a pasted block are Word spacing, not code.
        content = content.TrimEnd('\n');

        AddBlock(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Listing,
            Content = content,
            Title = TakeCaption(),
            Id = TakePendingBlockId(),
        });
    }

    private void FlushQuote()
    {
        if (_quoteBlocks is null) return;

        var blocks = _quoteBlocks;
        _quoteBlocks = null;

        var quote = new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Quote,
            Title = TakeCaption(),
            Id = TakePendingBlockId(),
        };

        foreach (var block in blocks) quote.AddChild(block);
        AddBlock(quote);
    }

    private void FlushAll()
    {
        FlushCode();
        FlushQuote();
        FlushLists();
    }

    private string? TakeCaption()
    {
        var caption = _pendingCaption;
        _pendingCaption = null;
        return caption;
    }

    private string? TakePendingBlockId()
    {
        var id = _pendingBlockId;
        _pendingBlockId = null;
        return id;
    }

    private void EmitPendingComments(InlineConverter converter)
    {
        if (converter.PendingComments.Count == 0) return;

        foreach (var comment in converter.PendingComments)
        {
            // Line comments are not AST nodes; a paragraph with the comment
            // role keeps the text visible in the output instead of losing it.
            AddBlock(new ParagraphNode
            {
                Text = string.Empty,
                Inlines = new List<InlineNode> { new TextInlineNode { Value = AsciidocText.EscapeInline(comment) } },
                Roles = new List<string> { "comment" },
            });
        }

        converter.PendingComments.Clear();
    }

    private void AddBlock(BlockNode block)
    {
        Container.AddChild(block);
        _sawFirstBlock = true;
    }

    /// <summary>
    /// Moves a leading inline anchor onto the block itself, which is both more
    /// idiomatic and lets sections carry the bookmark as their id.
    /// </summary>
    private static string? HoistAnchor(List<InlineNode> inlines)
    {
        string? id = null;
        var index = 0;
        while (index < inlines.Count)
        {
            if (inlines[index] is InlineAnchorNode anchor)
            {
                id ??= anchor.Id;
                inlines.RemoveAt(index);
                continue;
            }

            if (inlines[index] is TextInlineNode text && text.Value.Length == 0)
            {
                inlines.RemoveAt(index);
                continue;
            }

            break;
        }

        return id;
    }

    private static bool IsThematicBreak(XElement? pPr)
    {
        var bottom = pPr?.Element(Ns.W + "pBdr")?.Element(Ns.W + "bottom");
        if (bottom is null) return false;
        var val = bottom.Attribute(Ns.W + "val")?.Value;
        return val is not null && val != "none" && val != "nil";
    }

    /// <summary>
    /// Trims whitespace at the outer edges of an inline list. Word paragraphs
    /// frequently end with a trailing space that would become trailing
    /// whitespace in the emitted source.
    /// </summary>
    internal static List<InlineNode> TrimOuterWhitespace(List<InlineNode> inlines)
    {
        while (inlines.Count > 0 && inlines[0] is TextInlineNode first && string.IsNullOrWhiteSpace(first.Value))
            inlines.RemoveAt(0);

        while (inlines.Count > 0 && inlines[inlines.Count - 1] is TextInlineNode last && string.IsNullOrWhiteSpace(last.Value))
            inlines.RemoveAt(inlines.Count - 1);

        if (inlines.Count > 0 && inlines[0] is TextInlineNode lead)
        {
            var trimmed = lead.Value.TrimStart();
            if (trimmed.Length != lead.Value.Length) inlines[0] = new TextInlineNode { Value = trimmed };
        }

        if (inlines.Count > 0 && inlines[inlines.Count - 1] is TextInlineNode tail)
        {
            var trimmed = tail.Value.TrimEnd();
            if (trimmed.Length != tail.Value.Length)
                inlines[inlines.Count - 1] = new TextInlineNode { Value = trimmed };
        }

        return inlines;
    }
}
