using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Converters.Pdf;
using AdocNet.Layout;
using AdocNet.Layout.Builders;
using UglyToad.PdfPig;

namespace AdocNet.Importers.Tests;

[TestFixture]
public class MarkdownParserTests
{
    private static DocumentNode Doc(string markdown) => MarkdownParser.Parse(markdown).Document;

    private static SourceRange R(int line, int startCol, int endLine, int endCol) =>
        new(new SourcePosition(line, startCol), new SourcePosition(endLine, endCol));

    private static SourceRange R(int line, int startCol, int endCol) => R(line, startCol, line, endCol);

    private static T Single<T>(AstNode parent) where T : AstNode => parent.Children.OfType<T>().Single();

    private static IEnumerable<AstNode> Blocks(AstNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var nested in Blocks(child)) yield return nested;
        }
    }

    private static DocumentLayout Layout(string markdown) => new LayoutBuilder().Build(Doc(markdown));

    // ── Paragraphs and inline formatting ────────────────────────────────

    [Test]
    public void Paragraph_and_inline_ranges_follow_the_parser_conventions()
    {
        var paragraph = Doc("Intro.\n\nPara **b** _i_ `m` end\n").Children.OfType<ParagraphNode>().Last();
        Assert.That(paragraph.Source, Is.EqualTo(R(3, 1, 22)));

        // Inline ranges are relative to the paragraph start, delimiters included.
        var strong = paragraph.Inlines.OfType<StrongInlineNode>().Single();
        Assert.That(strong.Source, Is.EqualTo(R(1, 6, 10)));
        Assert.That(strong.Children.Single().Source, Is.EqualTo(R(1, 8, 8)));
        var emphasis = paragraph.Inlines.OfType<EmphasisInlineNode>().Single();
        Assert.That(emphasis.Source, Is.EqualTo(R(1, 12, 14)));
        var code = paragraph.Inlines.OfType<MonospaceInlineNode>().Single();
        Assert.That(code.Source, Is.EqualTo(R(1, 16, 18)));
        Assert.That(((TextInlineNode)code.Children.Single()).Value, Is.EqualTo("m"));
        Assert.That(code.Children.Single().Source, Is.EqualTo(R(1, 17, 17)));
    }

    [Test]
    public void Layout_promotes_inline_ranges_to_markdown_coordinates()
    {
        var paragraph = Layout("Intro.\n\nPara **bold** end\n").Children.OfType<ParagraphLayout>().Last();
        var bold = paragraph.Inlines.OfType<BoldRun>().Single();
        Assert.That(bold.Source, Is.EqualTo(R(3, 6, 13)));
        Assert.That(bold.Children.OfType<TextRun>().Single().Source, Is.EqualTo(R(3, 8, 11)));
    }

    [Test]
    public void Soft_break_is_a_space_and_hard_break_a_line_break()
    {
        var soft = Doc("one\ntwo\n").Children.OfType<ParagraphNode>().Single();
        Assert.That(((TextInlineNode)soft.Inlines.Single()).Value, Is.EqualTo("one two"));
        Assert.That(soft.Inlines.Single().Source, Is.EqualTo(R(1, 1, 2, 3)));
        Assert.That(soft.HasHardbreaks, Is.False);

        var hard = Doc("one  \ntwo\\\nthree\n").Children.OfType<ParagraphNode>().Single();
        Assert.That(hard.HasHardbreaks, Is.True);
        Assert.That(((TextInlineNode)hard.Inlines.Single()).Value, Is.EqualTo("one\ntwo\nthree"));
    }

    [Test]
    public void Crlf_line_endings_give_the_same_ranges()
    {
        var lf = Doc("Intro.\n\nPara\nnext **b**\n").Children.OfType<ParagraphNode>().Last();
        var crlf = Doc("Intro.\r\n\r\nPara\r\nnext **b**\r\n").Children.OfType<ParagraphNode>().Last();
        Assert.That(crlf.Source, Is.EqualTo(lf.Source));
        Assert.That(crlf.Inlines.Select(i => i.Source), Is.EqualTo(lf.Inlines.Select(i => i.Source)));
        Assert.That(crlf.Text, Is.EqualTo("Para\nnext **b**"));
    }

    [Test]
    public void Text_stays_literal()
    {
        var md = "Keep {name} -- (C) and a * b _ c &lt;&lt;x&gt;&gt; ... 'q'\n";
        var paragraph = Doc(md).Children.OfType<ParagraphNode>().Single();
        Assert.That(((TextInlineNode)paragraph.Inlines.Single()).Value,
            Is.EqualTo("Keep {name} -- (C) and a * b _ c <<x>> ... 'q'"));

        var html = new HtmlRenderer().RenderToString(Doc(md));
        Assert.That(html, Does.Contain("Keep {name} -- (C) and a * b _ c &lt;&lt;x&gt;&gt; ... 'q'"));
    }

    [Test]
    public void Strikethrough_is_a_line_through_highlight()
    {
        var highlight = Doc("a ~~gone~~ b\n").Children.OfType<ParagraphNode>().Single()
            .Inlines.OfType<HighlightInlineNode>().Single();
        Assert.That(highlight.Roles, Is.EqualTo(new[] { "line-through" }));
        Assert.That(highlight.Content, Is.EqualTo("gone"));
        Assert.That(highlight.Source, Is.EqualTo(R(1, 3, 10)));
    }

    // ── Headings ────────────────────────────────────────────────────────

    [Test]
    public void A_lone_first_h1_becomes_the_title_and_sections_nest()
    {
        var doc = Doc("# Guide\n\nIntro.\n\n## Part\n\nText.\n\n### Sub\n\nMore.\n\n## Next\n");
        Assert.That(doc.Title, Is.EqualTo("Guide"));
        var sections = doc.Children.OfType<SectionNode>().ToList();
        Assert.That(sections.Select(s => (s.Level, s.Title)), Is.EqualTo(new[] { (1, "Part"), (1, "Next") }));

        var part = sections[0];
        Assert.That(part.Source, Is.EqualTo(R(5, 1, 7)));
        Assert.That(part.TitleStart, Is.EqualTo(new SourcePosition(5, 4)));
        Assert.That(part.Id, Is.EqualTo("_part"));
        Assert.That(part.TitleInlines.Single().Source, Is.EqualTo(R(1, 1, 4)));
        Assert.That(Single<ParagraphNode>(part).Source, Is.EqualTo(R(7, 1, 5)));
        var sub = Single<SectionNode>(part);
        Assert.That((sub.Level, sub.Title), Is.EqualTo((2, "Sub")));

        var heading = new LayoutBuilder().Build(doc).Children.OfType<HeadingLayout>().First();
        Assert.That(heading.Inlines.OfType<TextRun>().Single().Source, Is.EqualTo(R(5, 4, 7)));
    }

    [Test]
    public void Several_h1_stay_sections()
    {
        var doc = Doc("# One\n\nA.\n\n# Two\n");
        Assert.That(doc.Title, Is.Null);
        Assert.That(doc.Children.OfType<SectionNode>().Select(s => s.Level), Is.EqualTo(new[] { 1, 1 }));
    }

    [Test]
    public void Setext_heading_range_covers_its_underline()
    {
        var section = Doc("Intro.\n\nPart *one*\n----------\n\nText.\n").Children.OfType<SectionNode>().Single();
        Assert.That(section.Title, Is.EqualTo("Part one"));
        Assert.That(section.Source, Is.EqualTo(R(3, 1, 4, 10)));
        Assert.That(section.TitleStart, Is.EqualTo(new SourcePosition(3, 1)));
        Assert.That(section.TitleInlines.OfType<EmphasisInlineNode>().Single().Source, Is.EqualTo(R(1, 6, 10)));
    }

    [Test]
    public void Skipped_heading_level_is_moved_up_with_a_diagnostic()
    {
        var result = MarkdownParser.Parse("## A\n\n#### C\n");
        var a = result.Document.Children.OfType<SectionNode>().Single();
        Assert.That(Single<SectionNode>(a).Level, Is.EqualTo(2));
        Assert.That(result.Diagnostics.Single().Severity, Is.EqualTo(DiagnosticSeverity.Info));
        Assert.That(result.Diagnostics.Single().Range, Is.EqualTo(R(3, 1, 6)));
    }

    [Test]
    public void Duplicate_titles_get_numbered_ids()
    {
        var ids = Doc("## A\n\n## A\n").Children.OfType<SectionNode>().Select(s => s.Id);
        Assert.That(ids, Is.EqualTo(new[] { "_a", "_a_2" }));
    }

    [Test]
    public void Front_matter_sets_the_title_and_attributes()
    {
        var result = MarkdownParser.Parse("---\ntitle: Book\nauthor: Ann\ndate: 2026-01-02\n---\n# Chapter\n\nText.\n");
        var doc = result.Document;
        Assert.That(doc.Title, Is.EqualTo("Book"));
        Assert.That(doc.Attributes["author"], Is.EqualTo("Ann"));
        Assert.That(doc.Attributes["revdate"], Is.EqualTo("2026-01-02"));
        // With a front matter title, the h1 stays a section.
        var chapter = doc.Children.OfType<SectionNode>().Single();
        Assert.That((chapter.Level, chapter.Source), Is.EqualTo((1, R(6, 1, 9))));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    // ── Code, quotes, rules, HTML ───────────────────────────────────────

    [Test]
    public void Fenced_code_with_a_language_is_a_source_block()
    {
        var block = Doc("Intro.\n\n```cs\nvar x = 1;\n<1> {a}\n```\n").Children.OfType<DelimitedBlockNode>().Single();
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
        Assert.That(block.Language, Is.EqualTo("cs"));
        Assert.That(block.Content, Is.EqualTo("var x = 1;\n<1> {a}"));
        Assert.That(block.Source, Is.EqualTo(R(3, 1, 6, 3)));
    }

    [Test]
    public void Fenced_code_without_language_and_indented_code_are_listings()
    {
        var blocks = Doc("```\nplain\n```\n\n    indented\n    code\n").Children.OfType<DelimitedBlockNode>().ToList();
        Assert.That(blocks.Select(b => (b.BlockKind, b.Language, b.Content)), Is.EqualTo(new[]
        {
            (DelimitedBlockKind.Listing, (string?)null, "plain"),
            (DelimitedBlockKind.Listing, (string?)null, "indented\ncode"),
        }));
        Assert.That(blocks[1].Source, Is.EqualTo(R(5, 1, 6, 8)));
    }

    [Test]
    public void Blockquote_content_has_absolute_ranges()
    {
        var quote = Doc("Intro.\n\n> quoted **q**\n> second\n").Children.OfType<DelimitedBlockNode>().Single();
        Assert.That(quote.BlockKind, Is.EqualTo(DelimitedBlockKind.Quote));
        Assert.That(quote.Source, Is.EqualTo(R(3, 1, 4, 8)));
        var paragraph = Single<ParagraphNode>(quote);
        Assert.That(paragraph.Source, Is.EqualTo(R(3, 3, 4, 8)));
        Assert.That(paragraph.Inlines.OfType<StrongInlineNode>().Single().Source, Is.EqualTo(R(1, 8, 12)));

        var layoutParagraph = Layout("Intro.\n\n> quoted **q**\n> second\n").Children.OfType<ParagraphLayout>().Last();
        Assert.That(layoutParagraph.Inlines.OfType<BoldRun>().Single().Source, Is.EqualTo(R(3, 10, 14)));
        Assert.That(layoutParagraph.Inlines.OfType<TextRun>().Last().Source, Is.EqualTo(R(3, 15, 4, 8)));
    }

    [Test]
    public void Thematic_break()
    {
        Assert.That(Doc("a\n\n---\n").Children.OfType<ThematicBreakNode>().Single().Source, Is.EqualTo(R(3, 1, 3)));
    }

    [Test]
    public void Raw_html_passes_through_and_comments_are_dropped()
    {
        var result = MarkdownParser.Parse("<div class=\"x\">\nhi\n</div>\n\n<!-- note -->\n\nText <b>bold</b>.\n");
        var block = result.Document.Children.OfType<DelimitedBlockNode>().Single();
        Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Passthrough));
        Assert.That(block.Content, Is.EqualTo("<div class=\"x\">\nhi\n</div>"));
        Assert.That(block.Source, Is.EqualTo(R(1, 1, 3, 6)));
        Assert.That(result.Diagnostics.Single().Range, Is.EqualTo(R(5, 1, 13)));

        var inlines = result.Document.Children.OfType<ParagraphNode>().Single().Inlines;
        Assert.That(inlines.OfType<PassthroughInlineNode>().Select(p => p.Content), Is.EqualTo(new[] { "<b>", "</b>" }));
        Assert.That(new HtmlRenderer().RenderToString(result.Document), Does.Contain("Text <b>bold</b>."));
    }

    [Test]
    public void Scripts_and_styles_never_pass_through()
    {
        var result = MarkdownParser.Parse("<script>\nalert(1)\n</script>\n\nText <script>alert(2)</script> <style>p{}</style> end <i>ok</i>\n");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Not.Contain("<script").And.Not.Contain("alert").And.Not.Contain("<style"));
        Assert.That(html, Does.Contain("<i>ok</i>"));
        Assert.That(result.Diagnostics, Has.Count.EqualTo(3));
    }

    [Test]
    public void Code_content_renders_literally()
    {
        var html = new HtmlRenderer().RenderToString(Doc("```\nx <1> {a} -- (C)\n```\n"));
        Assert.That(html, Does.Contain("x &lt;1&gt; {a} -- (C)"));
    }

    // ── Lists ───────────────────────────────────────────────────────────

    [Test]
    public void Nested_lists_with_tasks_and_start_numbers()
    {
        var md = "Intro.\n\n1. one\n2. two\n   - [x] done\n   - [ ] todo\n\n7) seven\n";
        var lists = Doc(md).Children.OfType<ListNode>().ToList();
        Assert.That(lists.Select(l => (l.ListKind, l.Start)), Is.EqualTo(new[] { (ListKind.Ordered, (int?)null), (ListKind.Ordered, (int?)7) }));

        var two = (ListItemNode)lists[0].Children[1];
        Assert.That(two.Text, Is.EqualTo("two"));
        Assert.That(two.ContentStart, Is.EqualTo(new SourcePosition(4, 4)));
        Assert.That(two.Source, Is.EqualTo(R(4, 1, 6, 13)));

        var nested = Single<ListNode>(two);
        Assert.That(nested.ListKind, Is.EqualTo(ListKind.Unordered));
        Assert.That(nested.Source, Is.EqualTo(R(5, 4, 6, 13)));
        var done = (ListItemNode)nested.Children[0];
        Assert.That((done.Checked, done.Text), Is.EqualTo(((bool?)true, "done")));
        Assert.That(done.ContentStart, Is.EqualTo(new SourcePosition(5, 10)));
        Assert.That(done.Inlines.Single().Source, Is.EqualTo(R(1, 1, 4)));
        Assert.That(((ListItemNode)nested.Children[1]).Checked, Is.False);

        var layoutItem = new LayoutBuilder().Build(Doc(md)).Children.OfType<ListLayout>().First().Items[1];
        var layoutDone = ((ListLayout)layoutItem.Blocks.Single()).Items[0];
        Assert.That(layoutDone.Inlines.OfType<TextRun>().Single().Source, Is.EqualTo(R(5, 10, 13)));
    }

    [Test]
    public void List_item_paragraphs_after_the_first_are_absolute_blocks()
    {
        var item = (ListItemNode)Doc("- item\n\n  more *para*\n").Children.OfType<ListNode>().Single().Children.Single();
        var paragraph = Single<ParagraphNode>(item);
        Assert.That(paragraph.Source, Is.EqualTo(R(3, 3, 13)));

        var layoutItem = Layout("- item\n\n  more *para*\n").Children.OfType<ListLayout>().Single().Items.Single();
        var italic = ((ParagraphLayout)layoutItem.Blocks.Single()).Inlines.OfType<ItalicRun>().Single();
        Assert.That(italic.Source, Is.EqualTo(R(3, 8, 13)));
    }

    [Test]
    public void Definition_list_terms_and_descriptions()
    {
        var item = (DescriptionItemNode)Doc("Intro.\n\nTerm *t*\n:   Definition *d*\n").Children
            .OfType<DescriptionListNode>().Single().Children.Single();
        Assert.That((item.Terms.Single(), item.Description), Is.EqualTo(("Term t", "Definition *d*")));
        Assert.That(item.Source.Start, Is.EqualTo(new SourcePosition(3, 1)));
        Assert.That(item.DescriptionStart, Is.EqualTo(new SourcePosition(4, 5)));
        Assert.That(item.DescriptionInlines.OfType<EmphasisInlineNode>().Single().Source, Is.EqualTo(R(1, 12, 14)));
    }

    // ── Tables ──────────────────────────────────────────────────────────

    [Test]
    public void Table_with_header_alignment_and_cell_ranges()
    {
        var md = "Intro.\n\n| H1 | H2 |\n|:---|---:|\n| c1 | *c2* |\n";
        var table = Doc(md).Children.OfType<TableNode>().Single();
        Assert.That(table.HasHeader, Is.True);
        Assert.That(table.Columns!.Select(c => c.Alignment), Is.EqualTo(new[] { TableAlignment.Left, TableAlignment.Right }));
        Assert.That(table.Source, Is.EqualTo(R(3, 1, 5, 13)));

        var rows = table.Children.OfType<TableRowNode>().ToList();
        Assert.That(rows, Has.Count.EqualTo(2));
        var cells = rows[1].Children.OfType<TableCellNode>().ToList();
        Assert.That(cells.Select(c => c.Text), Is.EqualTo(new[] { "c1", "*c2*" }));
        Assert.That(cells[1].Source, Is.EqualTo(R(5, 8, 11)));
        Assert.That(cells[1].Inlines.Single().Source, Is.EqualTo(R(1, 1, 4)));

        var layoutCell = new LayoutBuilder().Build(Doc(md)).Children.OfType<TableLayout>().Single().Rows[1].Cells[1];
        Assert.That(layoutCell.Inlines.OfType<ItalicRun>().Single().Source, Is.EqualTo(R(5, 8, 11)));
    }

    // ── Links, images, footnotes ────────────────────────────────────────

    [Test]
    public void Links_autolinks_and_reference_links()
    {
        var inlines = Doc("See [the *site*](http://a.example) <http://b.example> https://c.example [ref][r] <me@d.example>\n\n[r]: http://r.example\n")
            .Children.OfType<ParagraphNode>().Single().Inlines;
        var macros = inlines.OfType<InlineLinkMacroNode>().ToList();
        Assert.That(macros.Select(m => (m.Url, m.Label)), Is.EqualTo(new[] { ("http://a.example", "the site"), ("http://r.example", "ref") }));
        Assert.That(macros[0].Source, Is.EqualTo(R(1, 5, 34)));
        Assert.That(inlines.OfType<LinkInlineNode>().Select(l => l.Url),
            Is.EqualTo(new[] { "http://b.example", "https://c.example", "mailto:me@d.example" }));
    }

    [Test]
    public void Images_inline_and_block()
    {
        var doc = Doc("![Alt text](img/a.png)\n\nText ![i](b.png) here.\n");
        var block = doc.Children.OfType<BlockImageNode>().Single();
        Assert.That((block.Target, block.Alt, block.Source), Is.EqualTo(("img/a.png", "Alt text", R(1, 1, 22))));
        var inline = doc.Children.OfType<ParagraphNode>().Single().Inlines.OfType<InlineImageNode>().Single();
        Assert.That((inline.Target, inline.Alt, inline.Source), Is.EqualTo(("b.png", "i", R(1, 6, 16))));
    }

    [Test]
    public void Footnotes_carry_their_text()
    {
        var doc = Doc("Text[^n] and again[^n].\n\n[^n]: The *note*.\n");
        var footnotes = doc.Children.OfType<ParagraphNode>().Single().Inlines.OfType<FootnoteInlineNode>().ToList();
        Assert.That(footnotes.Select(f => (f.Id, f.Text)), Is.EqualTo(new[] { ("n", "The note."), ("n", "The note.") }));
        Assert.That(footnotes[0].Source, Is.EqualTo(R(1, 5, 8)));
        Assert.That(footnotes[0].Inlines.OfType<EmphasisInlineNode>().Single().Source.IsNone, Is.True);
        Assert.That(doc.Children, Has.Count.EqualTo(1), "the definition is not a block");
    }

    [Test]
    public void Self_referencing_footnote_does_not_recurse()
    {
        var footnote = Doc("Text[^a].\n\n[^a]: See[^a].\n").Children.OfType<ParagraphNode>().Single()
            .Inlines.OfType<FootnoteInlineNode>().Single();
        Assert.That(((TextInlineNode)footnote.Inlines.Single()).Value, Is.EqualTo("See[^a]."));
    }

    // ── Whole documents ─────────────────────────────────────────────────

    private const string Mixed = """
        ---
        author: Ann
        ---
        # Guide

        Intro with **bold**, `code`, [a link](http://x.example) and {name} -- (C).

        ## Lists

        1. First
        2. Second
           - [x] nested *task*

        > A quote.

        | Name | Value |
        |:-----|------:|
        | a    | 1     |

        ```cs
        var x = 1;
        ```

        ---

        Done[^1].

        [^1]: Footnote.
        """;

    [Test]
    public void Every_layout_range_points_at_its_markdown_text()
    {
        var md = Mixed.Replace("\r\n", "\n");
        var lines = md.Split('\n');
        var runs = new List<TextRun>();
        void Collect(IEnumerable<InlineLayout> inlines)
        {
            foreach (var inline in inlines)
            {
                if (inline is TextRun run) runs.Add(run);
                var children = inline switch
                {
                    BoldRun b => b.Children,
                    ItalicRun i => i.Children,
                    MonoRun m => m.Children,
                    _ => null,
                };
                if (children is not null) Collect(children);
            }
        }
        void Walk(IEnumerable<BlockLayout> blocks)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case ParagraphLayout p: Collect(p.Inlines); break;
                    case HeadingLayout h: Collect(h.Inlines); break;
                    case ListLayout l:
                        foreach (var item in l.Items) { Collect(item.Inlines); Walk(item.Blocks); }
                        break;
                    case TableLayout t:
                        foreach (var row in t.Rows) foreach (var cell in row.Cells) Collect(cell.Inlines);
                        break;
                }
            }
        }
        Walk(new LayoutBuilder().Build(Doc(md)).Children);

        var single = runs.Where(r => !r.Source.IsNone && r.Source.Start.Line == r.Source.End.Line).ToList();
        Assert.That(single, Has.Count.GreaterThan(10));
        foreach (var run in single)
        {
            var line = lines[run.Source.Start.Line - 1];
            var text = line.Substring(run.Source.Start.Column - 1, run.Source.End.Column - run.Source.Start.Column + 1);
            Assert.That(text, Is.EqualTo(run.Text), $"run at {run.Source}");
        }
    }

    [Test]
    public void Mixed_document_renders_to_pdf_and_html()
    {
        var doc = Doc(Mixed);
        Assert.That(Blocks(doc).Any(b => b is TableNode), Is.True);

        var pdf = new PdfRenderer().RenderToBytes(doc, new PdfRenderOptions());
        using var pdfDocument = PdfDocument.Open(pdf);
        var text = string.Join("\n", pdfDocument.GetPages().Select(p => p.Text));
        Assert.That(text, Does.Contain("Guide"));
        Assert.That(text, Does.Contain("{name} -- (C)"));
        Assert.That(text, Does.Contain("nested task"));

        var html = new HtmlRenderer().RenderToString(doc);
        Assert.That(html, Does.Contain("<h2 id=\"_lists\">Lists</h2>"));
        Assert.That(html, Does.Contain("{name} -- (C)"));
        Assert.That(html, Does.Contain("Footnote."));
    }
}
