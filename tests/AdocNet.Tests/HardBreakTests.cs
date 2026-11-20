using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Ast;
using AdocNet.Converters.DocBook;
using AdocNet.Converters.Epub;
using AdocNet.Converters.Man;
using AdocNet.Converters.Pdf;
using AdocNet.Converters.Revealjs;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// A line ending in " +" is a <see cref="HardBreakInlineNode"/> wherever inline text is parsed,
/// and every converter breaks the line there, as Asciidoctor does.
/// </summary>
[TestFixture]
public class HardBreakTests
{
    private static DocumentNode Parse(string source) => AdocParser.Parse(source).Document;

    [Test]
    public void Trailing_plus_is_a_hard_break_node_and_other_line_feeds_stay_soft()
    {
        var para = Parse("one +\ntwo\nthree").Children.OfType<ParagraphNode>().Single();

        Assert.That(para.HasHardbreaks, Is.False, "only the marked line breaks");
        Assert.That(para.Text, Is.EqualTo("one +\ntwo\nthree"), "Text keeps the source");
        Assert.That(para.Inlines.Select(i => i.GetType().Name),
            Is.EqualTo(new[] { "TextInlineNode", "HardBreakInlineNode", "TextInlineNode" }));
        var hardBreak = (HardBreakInlineNode)para.Inlines[1];
        Assert.That(hardBreak.Value, Is.EqualTo("\n"));
        Assert.That(hardBreak.Source, Is.EqualTo(new SourceRange(new SourcePosition(1, 4), new SourcePosition(1, 5))), "the \" +\" marker");
        Assert.That(((TextInlineNode)para.Inlines[2]).Value, Is.EqualTo("two\nthree"));
    }

    [Test]
    public void Trailing_plus_breaks_in_list_items_cells_and_descriptions()
    {
        var doc = Parse("* a +\nb\n\n|===\n|c +\nd\n|===\n\nterm:: e +\nf\n");

        var item = (ListItemNode)doc.Children.OfType<ListNode>().Single().Children[0];
        var cell = doc.Children.OfType<TableNode>().Single().Children.OfType<TableRowNode>().Single().Children.OfType<TableCellNode>().Single();
        var description = doc.Children.OfType<DescriptionListNode>().Single().Children.OfType<DescriptionItemNode>().Single();

        foreach (var inlines in new[] { item.Inlines, cell.Inlines, description.DescriptionInlines })
            Assert.That(inlines.OfType<HardBreakInlineNode>().Count(), Is.EqualTo(1));
        Assert.That(description.Description, Is.EqualTo("e +\nf"));
        Assert.That(description.Source.End.Line, Is.EqualTo(10), "the description's continuation line is part of the item");
    }

    [Test]
    public void Hardbreaks_option_breaks_every_line_but_keeps_a_last_line_marker()
    {
        var para = Parse("[%hardbreaks]\none\ntwo +").Children.OfType<ParagraphNode>().Single();

        Assert.That(para.HasHardbreaks, Is.True);
        Assert.That(para.Inlines.Select(i => ((TextInlineNode)i).Value), Is.EqualTo(new[] { "one", "\n", "two +" }));
        Assert.That(para.Inlines[1], Is.TypeOf<HardBreakInlineNode>());
    }

    [Test]
    public void Trailing_plus_at_the_end_breaks_without_a_line_feed()
    {
        var para = Parse("end +").Children.OfType<ParagraphNode>().Single();
        Assert.That(para.Inlines.Last(), Is.TypeOf<HardBreakInlineNode>());
        Assert.That(((TextInlineNode)para.Inlines.Last()).Value, Is.Empty);
    }

    [Test]
    public void Verbatim_text_keeps_its_marker()
    {
        var block = Parse("----\none +\ntwo\n----").Children.OfType<DelimitedBlockNode>().Single();
        Assert.That(block.Content, Is.EqualTo("one +\ntwo"));
    }

    [Test]
    public void DocBook_marks_the_break_like_asciidoctor()
    {
        var xml = new DocBookRenderer().RenderToString(Parse("* one +\ntwo"));
        Assert.That(xml, Does.Contain("one<?asciidoc-br?>\ntwo"));
    }

    [Test]
    public void Man_page_breaks_with_br_like_asciidoctor()
    {
        using var ms = new MemoryStream();
        new ManRenderer().Render(Parse("= cmd(1)\n\n== NAME\n\ncmd - x\n\n== DESCRIPTION\n\na +\nb\nc\n"), ms, RenderOptions.Default);
        Assert.That(Encoding.UTF8.GetString(ms.ToArray()), Does.Contain("a\n.br\nb\nc"));
    }

    [Test]
    public void Epub_and_revealjs_render_br()
    {
        var bytes = new EpubRenderer().RenderToBytes(Parse("* one +\ntwo"));
        using (var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes)))
        {
            var chapter = zip.Entries.First(e => e.FullName.EndsWith(".xhtml", StringComparison.Ordinal) && !e.FullName.Contains("nav.xhtml"));
            using var reader = new StreamReader(chapter.Open());
            Assert.That(reader.ReadToEnd(), Does.Contain("one<br/>\ntwo"));
        }

        var html = new RevealjsRenderer().RenderToString(Parse("== Slide\n\n* one +\ntwo"));
        Assert.That(html, Does.Contain("one<br>\ntwo"));
    }

    [Test]
    public void Pdf_breaks_list_items_and_keeps_soft_line_feeds_as_spaces()
    {
        var text = Encoding.ASCII.GetString(new PdfRenderer().RenderToBytes(Parse("* Intro words +\nStation one\nStation two\n")));

        // One text object per line: its y and the strings it shows (the bullet is a string of its own).
        var lines = Regex.Matches(text, @"BT\s+[\d.]+ ([\d.]+) Td(.*?)ET", RegexOptions.Singleline)
            .Select(m => (
                Y: float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                Text: string.Concat(Regex.Matches(m.Groups[2].Value, @"\(([^)]*)\) Tj").Select(s => s.Groups[1].Value))))
            .ToList();
        var all = string.Join(" | ", lines.Select(t => $"{t.Y}:{t.Text}"));
        var intro = lines.Single(t => t.Text.EndsWith("Intro words", StringComparison.Ordinal));
        var rest = lines.Single(t => t.Text.Contains("Station"));
        Assert.That(rest.Text, Is.EqualTo("Station one Station two"), "a soft line feed is a space; " + all);
        Assert.That(intro.Y, Is.GreaterThan(rest.Y), all);
        Assert.That(text, Does.Not.Contain("+) Tj"));
    }
}
