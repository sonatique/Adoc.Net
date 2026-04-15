using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.Html;

namespace AdocNet.Tests;

/// <summary>
/// Golden-output regression tests that lock in the exact HTML produced by each
/// extracted partial-class helper file. Two tests per helper file ensure that
/// refactors and extractions do not silently change the output.
/// </summary>
[TestFixture]
public class HtmlRendererRegressionTests
{
    private static string Render(DocumentNode doc, HtmlRenderOptions? options = null)
    {
        var renderer = new HtmlRenderer();
        using var ms = new MemoryStream();
        renderer.Render(doc, ms, options ?? HtmlRenderOptions.Default);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ── HtmlDocumentRenderer ────────────────────────────────────────────

    [Test]
    public void HtmlDocumentRenderer_FullDocWithTheme_GeneratesDoctype()
    {
        var doc = new DocumentNode { Title = "Test Doc" };
        var options = new HtmlRenderOptions { Theme = HtmlTheme.Default };
        var html = Render(doc, options);

        Assert.That(html, Does.StartWith("<!DOCTYPE html>\n"));
        Assert.That(html, Does.Contain("<title>Test Doc</title>"));
        Assert.That(html, Does.Contain("<style>"));
        Assert.That(html, Does.EndWith("</body>\n</html>\n"));
    }

    [Test]
    public void HtmlDocumentRenderer_TitleWithSpecialChars_EscapesCorrectly()
    {
        var doc = new DocumentNode { Title = "A & B <C> \"D\"" };
        var options = new HtmlRenderOptions { FullDocument = true };
        var html = Render(doc, options);

        Assert.That(html, Does.Contain("<title>A &amp; B &lt;C&gt; &quot;D&quot;</title>"));
        Assert.That(html, Does.Contain("<h1>A &amp; B &lt;C&gt; &quot;D&quot;</h1>"));
    }

    // ── HtmlSectionRenderer ─────────────────────────────────────────────

    [Test]
    public void HtmlSectionRenderer_Level1_GeneratesH2()
    {
        var doc = new DocumentNode();
        doc.AddChild(new SectionNode { Level = 1, Title = "Introduction" });
        var html = Render(doc);

        Assert.That(html, Is.EqualTo(
            "<div class=\"sect1\">\n" +
            "<h2>Introduction</h2>\n" +
            "<div class=\"sectionbody\">\n" +
            "</div>\n" +
            "</div>\n"));
    }

    [Test]
    public void HtmlSectionRenderer_NumberedSections_IncludesPrefix()
    {
        var doc = new DocumentNode();
        doc.SetAttribute("sectnums", "");
        doc.AddChild(new SectionNode { Level = 1, Title = "First" });
        doc.AddChild(new SectionNode { Level = 1, Title = "Second" });
        var html = Render(doc);

        Assert.That(html, Does.Contain("<h2>1. First</h2>"));
        Assert.That(html, Does.Contain("<h2>2. Second</h2>"));
    }

    [Test]
    public void HtmlSectionRenderer_Level2_GeneratesH3()
    {
        var doc = new DocumentNode();
        var s1 = new SectionNode { Level = 1, Title = "Parent" };
        s1.AddChild(new SectionNode { Level = 2, Title = "Child" });
        doc.AddChild(s1);
        var html = Render(doc);

        Assert.That(html, Does.Contain("<h3>Child</h3>"));
    }

    [Test]
    public void HtmlSectionRenderer_Appendix_GeneratesPrefix()
    {
        var doc = new DocumentNode();
        doc.AddChild(new SectionNode { Level = 1, Title = "Changelog", Style = "appendix" });
        var html = Render(doc);

        Assert.That(html, Does.Contain("Appendix A: Changelog"));
    }

    [Test]
    public void HtmlSectionRenderer_Sectanchors_GeneratesAnchorLink()
    {
        var doc = new DocumentNode();
        doc.SetAttribute("sectanchors", "");
        doc.AddChild(new SectionNode { Level = 1, Title = "Intro", Id = "_intro" });
        var html = Render(doc);

        Assert.That(html, Does.Contain("<a class=\"anchor\" href=\"#_intro\"></a>"));
    }

    [Test]
    public void HtmlSectionRenderer_Level0_BookDoctype_GeneratesH1WithPartPrefix()
    {
        var doc = new DocumentNode();
        doc.SetAttribute("doctype", "book");
        doc.AddChild(new SectionNode { Level = 0, Title = "Getting Started" });
        var html = Render(doc);

        Assert.That(html, Does.Contain("<h1>Part I. Getting Started</h1>"));
    }

    [Test]
    public void HtmlSectionRenderer_Level0_BookDoctype_TwoParts()
    {
        var doc = new DocumentNode();
        doc.SetAttribute("doctype", "book");
        doc.AddChild(new SectionNode { Level = 0, Title = "Basics" });
        doc.AddChild(new SectionNode { Level = 0, Title = "Advanced" });
        var html = Render(doc);

        Assert.That(html, Does.Contain("<h1>Part I. Basics</h1>"));
        Assert.That(html, Does.Contain("<h1>Part II. Advanced</h1>"));
    }

    [Test]
    public void HtmlSectionRenderer_Level1_BookDoctype_StillH2()
    {
        var doc = new DocumentNode();
        doc.SetAttribute("doctype", "book");
        doc.AddChild(new SectionNode { Level = 1, Title = "Chapter One" });
        var html = Render(doc);

        Assert.That(html, Does.Contain("<h2>Chapter One</h2>"));
    }

    [Test]
    public void HtmlSectionRenderer_Level1_ArticleDoctype_StillH2()
    {
        var doc = new DocumentNode();
        doc.SetAttribute("doctype", "article");
        doc.AddChild(new SectionNode { Level = 1, Title = "Introduction" });
        var html = Render(doc);

        Assert.That(html, Does.Contain("<h2>Introduction</h2>"));
    }

    [Test]
    public void HtmlSectionRenderer_Level0_NoBookDoctype_H1WithoutPartPrefix()
    {
        var doc = new DocumentNode();
        doc.AddChild(new SectionNode { Level = 0, Title = "Solo" });
        var html = Render(doc);

        Assert.That(html, Does.Contain("<h1>Solo</h1>"));
        Assert.That(html, Does.Not.Contain("Part"));
    }

    [Test]
    public void HtmlSectionRenderer_Appendix_StillWorks_WithBookDoctype()
    {
        var doc = new DocumentNode();
        doc.SetAttribute("doctype", "book");
        doc.AddChild(new SectionNode { Level = 1, Title = "References", Style = "appendix" });
        var html = Render(doc);

        Assert.That(html, Does.Contain("Appendix A: References"));
    }

    // ── HtmlBlockRenderer ───────────────────────────────────────────────

    [Test]
    public void HtmlBlockRenderer_ParagraphWithText_RendersPTag()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "Hello world" });
        var html = Render(doc);

        Assert.That(html, Is.EqualTo(
            "<div class=\"paragraph\">\n" +
            "<p>Hello world</p>\n" +
            "</div>\n"));
    }

    [Test]
    public void HtmlBlockRenderer_SourceBlockWithLanguage_RendersCodeWithClass()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "var x = 1;"
        });
        var html = Render(doc);

        Assert.That(html, Is.EqualTo(
            "<div class=\"listingblock\">\n" +
            "<div class=\"content\">\n" +
            "<pre class=\"highlight\"><code class=\"language-csharp\" data-lang=\"csharp\">var x = 1;</code></pre>\n" +
            "</div>\n" +
            "</div>\n"));
    }

    // ── HtmlListRenderer ────────────────────────────────────────────────

    [Test]
    public void HtmlListRenderer_UnorderedListWithItems_RendersUl()
    {
        var doc = new DocumentNode();
        var list = new ListNode { ListKind = ListKind.Unordered };
        list.AddChild(new ListItemNode { Text = "Item A" });
        list.AddChild(new ListItemNode { Text = "Item B" });
        doc.AddChild(list);
        var html = Render(doc);

        Assert.That(html, Is.EqualTo(
            "<div class=\"ulist\">\n" +
            "<ul>\n" +
            "<li>\n<p>Item A</p>\n</li>\n" +
            "<li>\n<p>Item B</p>\n</li>\n" +
            "</ul>\n" +
            "</div>\n"));
    }

    [Test]
    public void HtmlListRenderer_OrderedListWithStart_RendersOlWithStart()
    {
        var doc = new DocumentNode();
        var list = new ListNode { ListKind = ListKind.Ordered, Start = 5 };
        list.AddChild(new ListItemNode { Text = "Fifth" });
        doc.AddChild(list);
        var html = Render(doc);

        Assert.That(html, Does.Contain("<ol class=\"arabic\" start=\"5\">"));
        Assert.That(html, Does.Contain("<p>Fifth</p>"));
    }

    // ── HtmlTableRenderer ───────────────────────────────────────────────

    [Test]
    public void HtmlTableRenderer_Simple2x2_RendersTableWithCells()
    {
        var doc = new DocumentNode();
        var table = new TableNode();
        var row1 = new TableRowNode();
        row1.AddChild(new TableCellNode { Text = "A1" });
        row1.AddChild(new TableCellNode { Text = "A2" });
        table.AddChild(row1);
        var row2 = new TableRowNode();
        row2.AddChild(new TableCellNode { Text = "B1" });
        row2.AddChild(new TableCellNode { Text = "B2" });
        table.AddChild(row2);
        doc.AddChild(table);
        var html = Render(doc);

        Assert.That(html, Does.Contain("<table"));
        Assert.That(html, Does.Contain("<td class=\"halign-left tableblock valign-top\"><p class=\"tableblock\">A1</p></td>"));
        Assert.That(html, Does.Contain("<td class=\"halign-left tableblock valign-top\"><p class=\"tableblock\">B2</p></td>"));
        Assert.That(html, Does.Contain("</table>"));
    }

    [Test]
    public void HtmlTableRenderer_WithHeader_RendersThead()
    {
        var doc = new DocumentNode();
        var table = new TableNode { HasHeader = true };
        var headerRow = new TableRowNode();
        headerRow.AddChild(new TableCellNode { Text = "Name" });
        headerRow.AddChild(new TableCellNode { Text = "Value" });
        table.AddChild(headerRow);
        var dataRow = new TableRowNode();
        dataRow.AddChild(new TableCellNode { Text = "x" });
        dataRow.AddChild(new TableCellNode { Text = "1" });
        table.AddChild(dataRow);
        doc.AddChild(table);
        var html = Render(doc);

        Assert.That(html, Does.Contain("<thead>"));
        Assert.That(html, Does.Contain("<th class=\"halign-left tableblock valign-top\">Name</th>"));
        Assert.That(html, Does.Contain("</thead>"));
        Assert.That(html, Does.Contain("<tbody>"));
    }

    // ── HtmlInlineRenderer ──────────────────────────────────────────────

    [Test]
    public void HtmlInlineRenderer_BoldInline_RendersStrong()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "bold",
            Inlines = new InlineNode[]
            {
                new StrongInlineNode
                {
                    Children = new InlineNode[] { new TextInlineNode { Value = "bold" } }
                }
            }
        });
        var html = Render(doc);

        Assert.That(html, Is.EqualTo(
            "<div class=\"paragraph\">\n<p><strong>bold</strong></p>\n</div>\n"));
    }

    [Test]
    public void HtmlInlineRenderer_Link_RendersAnchor()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "https://example.com",
            Inlines = new InlineNode[]
            {
                new LinkInlineNode { Url = "https://example.com" }
            }
        });
        var html = Render(doc);

        Assert.That(html, Is.EqualTo(
            "<div class=\"paragraph\">\n" +
            "<p><a class=\"bare\" href=\"https://example.com\">https://example.com</a></p>\n" +
            "</div>\n"));
    }

    // ── HtmlImageRenderer ───────────────────────────────────────────────

    [Test]
    public void HtmlImageRenderer_BlockImage_RendersImgWithAlt()
    {
        var doc = new DocumentNode();
        doc.AddChild(new BlockImageNode { Target = "photo.png", Alt = "A photo" });
        var html = Render(doc);

        Assert.That(html, Does.Contain("<div class=\"imageblock\">"));
        Assert.That(html, Does.Contain("<img src=\"photo.png\" alt=\"A photo\">"));
        Assert.That(html, Does.Contain("</div>"));
    }

    [Test]
    public void HtmlImageRenderer_BlockImageWithTitle_RendersFigureCaption()
    {
        var doc = new DocumentNode();
        doc.AddChild(new BlockImageNode { Target = "diagram.svg", Alt = "Diagram", Title = "Architecture" });
        var html = Render(doc);

        Assert.That(html, Does.Contain("<div class=\"title\">Figure 1. Architecture</div>"));
    }

    // ── HtmlStemRenderer ────────────────────────────────────────────────

    [Test]
    public void HtmlStemRenderer_LatexmathBlock_RendersWithMathJaxDelimiters()
    {
        var doc = new DocumentNode();
        doc.AddChild(new StemBlockNode { Content = "E = mc^2", StemType = "latexmath" });
        var html = Render(doc);

        Assert.That(html, Is.EqualTo(
            "<div class=\"stemblock\">\n" +
            "<div class=\"content\">\n" +
            "\\[E = mc^2\\]\n" +
            "</div>\n" +
            "</div>\n"));
    }

    [Test]
    public void HtmlStemRenderer_AsciimathBlock_RendersWithDollarDelimiters()
    {
        var doc = new DocumentNode();
        doc.AddChild(new StemBlockNode { Content = "sum_(i=1)^n i", StemType = "asciimath" });
        var html = Render(doc);

        Assert.That(html, Is.EqualTo(
            "<div class=\"stemblock\">\n" +
            "<div class=\"content\">\n" +
            "\\$sum_(i=1)^n i\\$\n" +
            "</div>\n" +
            "</div>\n"));
    }
}
