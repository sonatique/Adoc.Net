using System.Text.RegularExpressions;
using AdocNet.Ast;
using AdocNet.Parser;
using Markdig;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AdocNet.Importers;

/// <summary>
/// Parses Markdown into the same <see cref="DocumentNode"/> tree that <see cref="AdocParser"/> returns,
/// so layout and every converter work on Markdown source unchanged. Every block and inline carries a
/// source range in Markdown coordinates, with <see cref="AdocParser"/>'s conventions: 1-based lines
/// and columns, inclusive ends, block ranges absolute, and inline ranges relative to the owning
/// block's content origin (<see cref="ParagraphNode"/> start, <see cref="ListItemNode.ContentStart"/>,
/// <see cref="SectionNode.TitleStart"/>, <see cref="DescriptionItemNode.DescriptionStart"/>, the table
/// cell start).
/// </summary>
/// <remarks>
/// Markdown syntax is the one <see cref="MarkdownImporter"/> reads: CommonMark plus GitHub Flavored
/// Markdown (pipe tables with alignment, task lists, strikethrough, autolinks, footnotes), grid
/// tables, definition lists and YAML front matter. Headings map to sections as the importer maps
/// them, so a single leading <c>#</c> heading becomes the document title. Text is literal: AsciiDoc
/// substitutions never apply.
/// </remarks>
public static class MarkdownParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UsePipeTables()
        .UseGridTables()
        .UseTaskLists()
        .UseFootnotes()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .UseAutoLinks()
        .UseDefinitionLists()
        .UsePreciseSourceLocation()
        .Build();

    /// <summary>Parses Markdown source text.</summary>
    public static ParseResult Parse(string markdown)
    {
        if (markdown is null) throw new ArgumentNullException(nameof(markdown));
        return new Converter(markdown).Convert();
    }

    private sealed class Converter
    {
        private readonly string _text;
        private readonly List<int> _lineStarts = [0];
        private readonly List<Diagnostic> _diagnostics = [];
        private readonly HashSet<string> _seenIds = new(StringComparer.Ordinal);
        private readonly DocumentNode _document = new();
        private HeadingBlock? _promotedHeading;
        private int _headingShift;
        private int _lastSectionLevel;
        private bool _hardBreak;
        private readonly HashSet<Footnote> _openFootnotes = [];

        public Converter(string text)
        {
            _text = text;
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\n') _lineStarts.Add(i + 1);
        }

        public ParseResult Convert()
        {
            var markdown = Markdown.Parse(_text, Pipeline);

            if (markdown.Descendants<YamlFrontMatterBlock>().FirstOrDefault() is { } frontMatter)
            {
                var range = ToRange(frontMatter.Span);
                MarkdownImporter.FrontMatter(frontMatter.Lines.ToString(),
                    (name, value) => { if (name == "title") _document.Title = value; else _document.SetAttribute(name, value); },
                    message => _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning, message, range)));
            }

            PlanHeadings(markdown);

            var sections = new Stack<SectionNode>();
            foreach (var block in markdown)
            {
                if (block is HeadingBlock heading)
                {
                    if (heading == _promotedHeading) continue;
                    var section = ConvertHeading(heading, discrete: false);
                    while (sections.Count > 0 && sections.Peek().Level >= section.Level) sections.Pop();
                    (sections.Count > 0 ? (AstNode)sections.Peek() : _document).AddChild(section);
                    sections.Push(section);
                    continue;
                }
                if (ConvertBlock(block) is { } node)
                    (sections.Count > 0 ? (AstNode)sections.Peek() : _document).AddChild(node);
            }
            return new ParseResult(_document, _diagnostics);
        }

        // ── Headings ────────────────────────────────────────────────────────

        /// <summary>
        /// As the importer: one top-level <c>#</c> heading with nothing before it becomes the document
        /// title (unless front matter set one), and the remaining levels shift so the top one is a
        /// level 1 section.
        /// </summary>
        private void PlanHeadings(MarkdownDocument markdown)
        {
            var headings = markdown.Descendants<HeadingBlock>().ToList();
            if (headings.Count == 0) return;
            int top = headings.Min(h => h.Level);
            var first = headings[0];
            if (_document.Title is null && top == 1 && first.Level == 1 && first.Parent == markdown
                && headings.Count(h => h.Level == 1) == 1 && !HasContentBefore(markdown, first))
            {
                _promotedHeading = first;
                _document.Title = PlainText(first.Inline);
                _headingShift = 1;
            }
            else
            {
                _headingShift = top - 1;
            }
        }

        private static bool HasContentBefore(MarkdownDocument markdown, Block heading)
        {
            foreach (var block in markdown)
            {
                if (block == heading) return false;
                if (block is not (YamlFrontMatterBlock or LinkReferenceDefinitionGroup or BlankLineBlock)
                    && !(block is HtmlBlock { Type: HtmlBlockType.Comment }))
                    return true;
            }
            return false;
        }

        private SectionNode ConvertHeading(HeadingBlock heading, bool discrete)
        {
            int level = Math.Max(1, heading.Level - _headingShift);
            if (level > _lastSectionLevel + 1)
            {
                _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,
                    "A heading skipped a level; it was moved up to keep sections in sequence.", ToRange(heading.Span)));
                level = _lastSectionLevel + 1;
            }
            level = Math.Min(level, 5);
            _lastSectionLevel = level;

            var title = PlainText(heading.Inline);
            var titleStart = FirstInlineStart(heading.Inline);
            var id = BlockParser.GenerateSectionId(title, _document.Attributes);
            if (!_seenIds.Add(id))
            {
                int n = 2;
                while (!_seenIds.Add($"{id}_{n}")) n++;
                id = $"{id}_{n}";
            }
            return new SectionNode
            {
                Level = level,
                Title = title,
                TitleInlines = Inlines(heading.Inline, titleStart),
                TitleStart = titleStart,
                IsDiscrete = discrete,
                Id = id,
                Source = ToRange(heading.Span),
            };
        }

        // ── Blocks ──────────────────────────────────────────────────────────

        private void AddBlocks(ContainerBlock container, AstNode parent, int skip = 0)
        {
            for (int i = skip; i < container.Count; i++)
                if (ConvertBlock(container[i]) is { } node)
                    parent.AddChild(node);
        }

        private AstNode? ConvertBlock(Block block)
        {
            switch (block)
            {
                case YamlFrontMatterBlock or LinkReferenceDefinitionGroup or FootnoteGroup or BlankLineBlock:
                    return null;
                case HeadingBlock heading:
                    return ConvertHeading(heading, discrete: true);
                case ParagraphBlock paragraph:
                    return BlockImage(paragraph) ?? (AstNode)ConvertParagraph(paragraph);
                case FencedCodeBlock fenced:
                    var info = fenced.Info?.Trim();
                    return new DelimitedBlockNode
                    {
                        BlockKind = string.IsNullOrEmpty(info) ? DelimitedBlockKind.Listing : DelimitedBlockKind.Source,
                        Language = string.IsNullOrEmpty(info) ? null : info,
                        Content = fenced.Lines.ToString(),
                        Substitutions = SubstitutionKind.Verbatim,
                        Source = ToRange(fenced.Span),
                    };
                case CodeBlock code:
                    return new DelimitedBlockNode
                    {
                        BlockKind = DelimitedBlockKind.Listing,
                        Content = code.Lines.ToString(),
                        Substitutions = SubstitutionKind.Verbatim,
                        Source = ToRange(code.Span),
                    };
                case QuoteBlock quote:
                    var quoteNode = new DelimitedBlockNode { BlockKind = DelimitedBlockKind.Quote, Source = ToRange(quote.Span) };
                    AddBlocks(quote, quoteNode);
                    return quoteNode;
                case ListBlock list:
                    return ConvertList(list);
                case Table table:
                    return ConvertTable(table);
                case DefinitionList definitions:
                    return ConvertDefinitionList(definitions);
                case ThematicBreakBlock rule:
                    return new ThematicBreakNode { Source = ToRange(rule.Span) };
                case HtmlBlock html:
                    return ConvertHtml(html);
                case ContainerBlock container:
                    // ponytail: unknown containers keep their blocks in an open block.
                    var open = new DelimitedBlockNode { BlockKind = DelimitedBlockKind.Open, Source = ToRange(container.Span) };
                    AddBlocks(container, open);
                    return open;
                default:
                    // Unknown leaf: its source as literal text, so nothing disappears.
                    var range = ToRange(block.Span);
                    var text = Slice(block.Span);
                    return new ParagraphNode
                    {
                        Text = text,
                        Inlines = [new TextInlineNode { Value = text, Source = Relative(range, range.Start) }],
                        Source = range,
                    };
            }
        }

        private ParagraphNode ConvertParagraph(ParagraphBlock paragraph)
        {
            var range = ToRange(paragraph.Span);
            _hardBreak = false;
            var inlines = Inlines(paragraph.Inline, range.Start);
            return new ParagraphNode
            {
                Text = Slice(paragraph.Span),
                Inlines = inlines,
                HasHardbreaks = _hardBreak,
                Source = range,
            };
        }

        /// <summary>A paragraph holding only an image becomes a block image.</summary>
        private BlockImageNode? BlockImage(ParagraphBlock paragraph)
        {
            LinkInline? image = null;
            for (var inline = paragraph.Inline?.FirstChild; inline is not null; inline = inline.NextSibling)
            {
                if (inline is LinkInline { IsImage: true } link && image is null) image = link;
                else if (!(inline is LiteralInline literal && literal.Content.IsEmptyOrWhitespace())) return null;
            }
            if (image is null) return null;
            return new BlockImageNode { Target = image.Url ?? "", Alt = PlainText(image), Source = ToRange(paragraph.Span) };
        }

        private ListNode ConvertList(ListBlock list)
        {
            int? start = list.IsOrdered && int.TryParse(list.OrderedStart, out var n) && n != 1 ? n : null;
            var node = new ListNode
            {
                ListKind = list.IsOrdered ? ListKind.Ordered : ListKind.Unordered,
                Start = start,
                Source = ToRange(list.Span),
            };
            foreach (var child in list)
                if (child is ListItemBlock item)
                    node.AddChild(ConvertListItem(item));
            return node;
        }

        private ListItemNode ConvertListItem(ListItemBlock item)
        {
            if (item.Count == 0 || item[0] is not ParagraphBlock paragraph)
            {
                var empty = new ListItemNode { Text = "", Source = ToRange(item.Span) };
                AddBlocks(item, empty);
                return empty;
            }

            // A task box is the paragraph's first inline; the item text starts after it.
            bool? isChecked = null;
            int textStart = paragraph.Span.Start;
            var first = paragraph.Inline?.FirstChild;
            if (first is TaskList task)
            {
                isChecked = task.Checked;
                textStart = task.Span.End + 1;
                while (textStart <= paragraph.Span.End && _text[textStart] is ' ' or '\t') textStart++;
            }
            var origin = Pos(textStart);
            _hardBreak = false;
            var node = new ListItemNode
            {
                Text = textStart <= paragraph.Span.End ? Slice(new SourceSpan(textStart, paragraph.Span.End)) : "",
                Inlines = Inlines(paragraph.Inline, origin, textStart),
                Checked = isChecked,
                ContentStart = origin,
                Source = ToRange(item.Span),
            };
            AddBlocks(item, node, skip: 1);
            return node;
        }

        private TableNode ConvertTable(Table table)
        {
            var aligns = table.ColumnDefinitions.Select(d => d.Alignment switch
            {
                TableColumnAlign.Center => TableAlignment.Center,
                TableColumnAlign.Right => TableAlignment.Right,
                _ => TableAlignment.Left,
            }).ToList();
            var node = new TableNode
            {
                HasHeader = table.Count > 0 && table[0] is TableRow { IsHeader: true },
                Columns = aligns.Select(a => new TableColumnSpec { Width = 1, Alignment = a }).ToList(),
                Source = ToRange(table.Span),
            };
            foreach (var child in table)
            {
                if (child is not TableRow row) continue;
                var rowNode = new TableRowNode { Source = ToRange(row.Span) };
                foreach (var cellBlock in row)
                    if (cellBlock is TableCell cell)
                        rowNode.AddChild(ConvertCell(cell));
                node.AddChild(rowNode);
            }
            return node;
        }

        private TableCellNode ConvertCell(TableCell cell)
        {
            if (cell.Count == 1 && cell[0] is ParagraphBlock paragraph && BlockImage(paragraph) is null)
            {
                var span = Trim(paragraph.Span);
                var range = ToRange(span);
                return new TableCellNode
                {
                    Text = Slice(span),
                    Inlines = Inlines(paragraph.Inline, range.Start),
                    ColSpan = Math.Max(1, cell.ColumnSpan),
                    RowSpan = Math.Max(1, cell.RowSpan),
                    Source = range,
                };
            }
            var node = new TableCellNode
            {
                Text = cell.Count == 0 ? "" : Slice(cell.Span),
                ColSpan = Math.Max(1, cell.ColumnSpan),
                RowSpan = Math.Max(1, cell.RowSpan),
                ContentStyle = cell.Count == 0 ? TableCellStyle.Default : TableCellStyle.AsciiDoc,
                Source = ToRange(cell.Span),
            };
            AddBlocks(cell, node);
            return node;
        }

        private DescriptionListNode ConvertDefinitionList(DefinitionList list)
        {
            var node = new DescriptionListNode { Source = ToRange(list.Span) };
            foreach (var child in list)
            {
                if (child is not DefinitionItem item) continue;
                var terms = item.OfType<DefinitionTerm>().ToList();
                int body = terms.Count;
                var origin = ToRange(item.Span).Start;
                if (terms.Count > 0) origin = ToRange(terms[0].Span).Start;

                var description = body < item.Count ? item[body] as ParagraphBlock : null;
                var descStart = description is null ? SourcePosition.None : ToRange(description.Span).Start;
                var itemNode = new DescriptionItemNode
                {
                    Terms = terms.Select(t => PlainText(t.Inline)).ToList(),
                    TermInlines = terms.Count > 0 ? Inlines(terms[0].Inline, origin) : [],
                    AllTermInlines = terms.Count > 1 ? terms.Select(t => Inlines(t.Inline, origin)).ToList() : null,
                    Description = description is null ? "" : Slice(description.Span),
                    DescriptionInlines = description is null ? [] : Inlines(description.Inline, descStart),
                    DescriptionStart = descStart,
                    Source = new SourceRange(origin, ToRange(item.Span).End),
                };
                AddBlocks(item, itemNode, skip: description is null ? body : body + 1);
                node.AddChild(itemNode);
            }
            return node;
        }

        private AstNode? ConvertHtml(HtmlBlock html)
        {
            var content = html.Lines.ToString();
            // As the importer: comments are dropped, and scripts and styles never pass through.
            if (html.Type == HtmlBlockType.Comment
                || html.Type == HtmlBlockType.ScriptPreOrStyle && Regex.IsMatch(content, @"^\s*<(script|style)\b", RegexOptions.IgnoreCase))
            {
                _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,
                    html.Type == HtmlBlockType.Comment ? "An HTML comment was dropped." : "A <script> or <style> block was dropped.",
                    ToRange(html.Span)));
                return null;
            }
            return new DelimitedBlockNode
            {
                BlockKind = DelimitedBlockKind.Passthrough,
                Content = content,
                Substitutions = SubstitutionKind.None,
                Source = ToRange(html.Span),
            };
        }

        // ── Inlines ─────────────────────────────────────────────────────────

        /// <summary>
        /// Converts inline content with ranges relative to <paramref name="origin"/>; text before
        /// <paramref name="from"/> (a task box) is skipped. Sets <see cref="_hardBreak"/> on a hard break.
        /// </summary>
        private List<InlineNode> Inlines(ContainerInline? container, SourcePosition origin, int from = 0)
        {
            var result = new List<InlineNode>();
            string? skipUntil = null;
            for (var inline = container?.FirstChild; inline is not null; inline = inline.NextSibling)
            {
                if (inline.Span.End < from) continue;
                if (inline is HtmlInline html)
                {
                    if (skipUntil is not null)
                    {
                        if (html.Tag.StartsWith(skipUntil, StringComparison.OrdinalIgnoreCase)) skipUntil = null;
                        continue;
                    }
                    // As the importer: scripts and styles never pass through.
                    var script = Regex.Match(html.Tag, @"^<(script|style)\b", RegexOptions.IgnoreCase);
                    if (script.Success)
                    {
                        _diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,
                            "A <script> or <style> element was dropped.", ToRange(html.Span)));
                        skipUntil = "</" + script.Groups[1].Value;
                        continue;
                    }
                }
                if (skipUntil is not null) continue;
                ConvertInline(inline, origin, from, result);
            }
            return MergeText(result);
        }

        private void ConvertInline(Inline inline, SourcePosition origin, int from, List<InlineNode> output)
        {
            var range = Relative(ToRange(inline.Span), origin);
            switch (inline)
            {
                case TaskList:
                    return;
                case LiteralInline literal:
                    var value = literal.Content.ToString();
                    var span = literal.Span;
                    if (span.Start < from)
                    {
                        int skip = Math.Min(value.Length, from - span.Start);
                        value = value.Substring(skip);
                        span = new SourceSpan(from, span.End);
                    }
                    if (value.Length > 0 && Slice(span) is var raw && raw != value && raw.Trim() == value)
                        span = new SourceSpan(span.Start + raw.IndexOf(value, StringComparison.Ordinal),
                            span.Start + raw.IndexOf(value, StringComparison.Ordinal) + value.Length - 1);
                    if (value.Length > 0)
                        output.Add(new TextInlineNode { Value = value, Source = Relative(ToRange(span), origin) });
                    return;
                case EmphasisInline emphasis:
                    var children = Inlines(emphasis, origin);
                    output.Add(emphasis.DelimiterChar == '~'
                        ? new HighlightInlineNode { Children = children, Roles = ["line-through"], Source = range }
                        : emphasis.DelimiterCount >= 2
                            ? new StrongInlineNode { Children = children, Source = range }
                            : new EmphasisInlineNode { Children = children, Source = range });
                    return;
                case CodeInline code:
                    output.Add(new MonospaceInlineNode
                    {
                        Children = [new TextInlineNode { Value = code.Content, Source = Relative(ToRange(CodeContentSpan(code)), origin) }],
                        Source = range,
                    });
                    return;
                case LinkInline { IsImage: true } image:
                    output.Add(new InlineImageNode { Target = image.Url ?? "", Alt = PlainText(image), Source = range });
                    return;
                case LinkInline link:
                    var url = link.Url ?? "";
                    var label = PlainText(link);
                    output.Add(link.IsAutoLink
                        ? new LinkInlineNode { Url = url, Source = range }
                        : new InlineLinkMacroNode { Url = url, Label = label.Length > 0 ? label : url, Source = range });
                    return;
                case AutolinkInline autolink:
                    output.Add(new LinkInlineNode { Url = autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url, Source = range });
                    return;
                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard) _hardBreak = true;
                    output.Add(new TextInlineNode { Value = lineBreak.IsHard ? "\n" : " ", Source = range });
                    return;
                case HtmlEntityInline entity:
                    output.Add(new TextInlineNode { Value = entity.Transcoded.ToString(), Source = range });
                    return;
                case HtmlInline html:
                    output.Add(new PassthroughInlineNode { Content = html.Tag, Source = range });
                    return;
                case FootnoteLink { IsBackLink: true }:
                    return;
                case FootnoteLink footnote when _openFootnotes.Contains(footnote.Footnote):
                    // A footnote referencing itself (directly or not) keeps its marker as text.
                    output.Add(new TextInlineNode { Value = Slice(footnote.Span), Source = range });
                    return;
                case FootnoteLink footnote:
                    output.Add(ConvertFootnote(footnote.Footnote, range));
                    return;
                case ContainerInline container:
                    for (var child = container.FirstChild; child is not null; child = child.NextSibling)
                        ConvertInline(child, origin, from, output);
                    return;
                default:
                    var text = Slice(inline.Span);
                    if (text.Length > 0)
                        output.Add(new TextInlineNode { Value = text, Source = range });
                    return;
            }
        }

        /// <summary>
        /// A footnote reference carries the definition's text. ponytail: the body's inline ranges are
        /// None, because they sit in the definition, not in the referencing block.
        /// </summary>
        private FootnoteInlineNode ConvertFootnote(Footnote footnote, SourceRange range)
        {
            bool hardBreak = _hardBreak;
            _openFootnotes.Add(footnote);
            var inlines = new List<InlineNode>();
            foreach (var block in footnote)
            {
                if (block is not ParagraphBlock paragraph) continue;
                if (inlines.Count > 0) inlines.Add(new TextInlineNode { Value = " " });
                inlines.AddRange(Inlines(paragraph.Inline, SourcePosition.None));
            }
            _hardBreak = hardBreak;
            _openFootnotes.Remove(footnote);
            return new FootnoteInlineNode
            {
                Id = footnote.Label?.TrimStart('^'),
                Text = string.Join(" ", footnote.OfType<ParagraphBlock>().Select(p => PlainText(p.Inline))),
                Inlines = MergeText(inlines),
                Source = range,
            };
        }

        /// <summary>The span between the backtick runs, less the one space CommonMark strips on each side.</summary>
        private SourceSpan CodeContentSpan(CodeInline code)
        {
            int start = code.Span.Start + code.DelimiterCount;
            int end = code.Span.End - code.DelimiterCount;
            if (end - start >= 2 && _text[start] == ' ' && _text[end] == ' ' && code.Content.Trim().Length > 0)
            {
                start++;
                end--;
            }
            return new SourceSpan(start, end);
        }

        private static List<InlineNode> MergeText(List<InlineNode> inlines)
        {
            var merged = new List<InlineNode>(inlines.Count);
            foreach (var inline in inlines)
            {
                if (inline is TextInlineNode text && text.Value != "\n"
                    && merged.Count > 0 && merged[^1] is TextInlineNode previous && previous.Value != "\n")
                {
                    merged[^1] = new TextInlineNode
                    {
                        Value = previous.Value + text.Value,
                        Source = previous.Source.IsNone || text.Source.IsNone
                            ? SourceRange.None
                            : new SourceRange(previous.Source.Start, text.Source.End),
                    };
                }
                else
                {
                    merged.Add(inline);
                }
            }
            return merged;
        }

        private static string PlainText(ContainerInline? container)
        {
            var sb = new System.Text.StringBuilder();
            void Walk(Inline inline)
            {
                switch (inline)
                {
                    case LiteralInline literal: sb.Append(literal.Content.ToString()); break;
                    case CodeInline code: sb.Append(code.Content); break;
                    case HtmlEntityInline entity: sb.Append(entity.Transcoded.ToString()); break;
                    case LineBreakInline: sb.Append(' '); break;
                    case AutolinkInline autolink: sb.Append(autolink.Url); break;
                    case ContainerInline nested:
                        for (var child = nested.FirstChild; child is not null; child = child.NextSibling) Walk(child);
                        break;
                }
            }
            for (var inline = container?.FirstChild; inline is not null; inline = inline.NextSibling) Walk(inline);
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        private SourcePosition FirstInlineStart(ContainerInline? container) =>
            container?.FirstChild is { } first && first.Span.End >= first.Span.Start ? Pos(first.Span.Start) : SourcePosition.None;

        // ── Positions ───────────────────────────────────────────────────────

        private SourcePosition Pos(int offset)
        {
            offset = Math.Max(0, Math.Min(offset, _text.Length));
            int line = _lineStarts.BinarySearch(offset);
            if (line < 0) line = ~line - 1;
            return new SourcePosition(line + 1, offset - _lineStarts[line] + 1);
        }

        /// <summary>Markdig's 0-based inclusive offsets as a 1-based inclusive range, line ends excluded.</summary>
        private SourceRange ToRange(SourceSpan span)
        {
            if (span.Start < 0 || span.End < span.Start || span.Start >= _text.Length) return SourceRange.None;
            int end = Math.Min(span.End, _text.Length - 1);
            while (end > span.Start && _text[end] is '\n' or '\r') end--;
            return new SourceRange(Pos(span.Start), Pos(end));
        }

        private SourceSpan Trim(SourceSpan span)
        {
            int start = span.Start, end = Math.Min(span.End, _text.Length - 1);
            while (start < end && char.IsWhiteSpace(_text[start])) start++;
            while (end > start && char.IsWhiteSpace(_text[end])) end--;
            return new SourceSpan(start, end);
        }

        private string Slice(SourceSpan span)
        {
            var range = ToRange(span);
            if (range.IsNone) return "";
            int start = span.Start;
            int end = Math.Min(span.End, _text.Length - 1);
            while (end > start && _text[end] is '\n' or '\r') end--;
            return _text.Substring(start, end - start + 1).Replace("\r\n", "\n");
        }

        /// <summary>
        /// The inverse of the layout's promotion: line relative to <paramref name="origin"/>, and the
        /// column relative to it only on the origin's own line.
        /// </summary>
        private static SourceRange Relative(SourceRange range, SourcePosition origin)
        {
            if (range.IsNone || origin.IsNone || range.Start.Line < origin.Line) return SourceRange.None;
            return new SourceRange(Relative(range.Start, origin), Relative(range.End, origin));
        }

        private static SourcePosition Relative(SourcePosition p, SourcePosition origin) =>
            new(p.Line - origin.Line + 1, p.Line == origin.Line ? p.Column - origin.Column + 1 : p.Column);
    }
}
