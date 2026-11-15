using System.Text.RegularExpressions;
using AdocNet.Converters.Html;
using AngleSharp.Html.Parser;
using Markdig;

namespace AdocNet.Importers.Tests;

[TestFixture]
public class MarkdownImporterTests
{
    private static string Adoc(string markdown) => new MarkdownImporter().ImportString(markdown).AsciiDoc;

    [Test]
    public void A_lone_first_h1_becomes_the_title()
    {
        var adoc = Adoc("# Guide\n\nIntro.\n\n## Part\n\nText.\n");
        Assert.That(adoc, Does.StartWith("= Guide\n"));
        Assert.That(adoc, Does.Contain("\n== Part\n"));
    }

    [Test]
    public void Several_h1_stay_sections()
    {
        var adoc = Adoc("# One\n\nA.\n\n# Two\n\nB.\n");
        Assert.That(adoc, Does.Not.Contain("= One\n").Or.Contain("== One"));
        Assert.That(adoc, Does.Contain("== One\n").And.Contain("== Two\n"));
    }

    [Test]
    public void Front_matter_sets_title_and_attributes()
    {
        var adoc = Adoc("---\ntitle: \"Widgets\"\nauthor: Ada\ntags: [a, b]\ndate: 2026-01-02\nversion: 1.2\n---\n\n# Heading\n\nBody.\n");
        Assert.That(adoc, Does.StartWith("= Widgets\n:author: Ada\n:keywords: a, b\n:revdate: 2026-01-02\n:revnumber: 1.2\n"));
        Assert.That(adoc, Does.Contain("== Heading"), "the H1 is a section when the title comes from front matter");
    }

    [Test]
    public void Inline_formatting_links_and_breaks()
    {
        var adoc = Adoc("A **b** *i* ~~s~~ `c_d` [l](https://x.org) <https://y.org> end  \nnext\n");
        Assert.That(adoc, Does.Contain("A *b* _i_ [.line-through]#s# `+c_d+` https://x.org[l] https://y.org[] end +\nnext"));
    }

    [Test]
    public void Fenced_code_keeps_its_language_and_text()
    {
        var adoc = Adoc("```python\nprint('x') # *no*\n----\n```\n");
        Assert.That(adoc, Does.Contain("[source,python]\n-----\nprint('x') # *no*\n----\n-----"));
    }

    [Test]
    public void Lists_nest_and_task_items_become_checklists()
    {
        var adoc = Adoc("- a\n  - b\n    1. c\n- [x] done\n- [ ] todo\n\n3. three\n4. four\n");
        Assert.That(adoc, Does.Contain("* a\n** b\n... c\n* [x] done\n* [ ] todo"));
        Assert.That(adoc, Does.Contain("[start=3]\n. three\n. four"));
    }

    [Test]
    public void Gfm_tables_keep_alignment_and_header()
    {
        var adoc = Adoc("| L | C | R |\n|:--|:-:|--:|\n| a | b \\| c | `d` |\n");
        Assert.That(adoc, Does.Contain("[cols=\"1,^1,>1\",options=\"header\"]\n|===\n|L\n|C\n|R\n\n|a\n|b \\| c\n|`d`\n|==="));
    }

    [Test]
    public void Footnotes_html_blocks_and_images()
    {
        var result = new MarkdownImporter().ImportString(
            "Text[^n] ![a](img/p.png) ![r](https://x.org/r.png)\n\n<div>\n<b>raw</b>\n</div>\n\n[^n]: The *note*.\n");
        Assert.That(result.AsciiDoc, Does.Contain("Textfootnote:[The _note_.] image:img/p.png[a] image:https://x.org/r.png[r]"));
        Assert.That(result.AsciiDoc, Does.Contain("++++\n<div>\n<b>raw</b>\n</div>\n++++"));
        Assert.That(result.Report.Footnotes, Is.EqualTo(1));
        Assert.That(result.Report.Images, Is.EqualTo(2));
        Assert.That(result.Report.Issues.Select(i => i.Code), Does.Contain("html.block.passthrough"));
    }

    [Test]
    public void Script_and_style_blocks_are_dropped_not_passed_through()
    {
        var r = new MarkdownImporter().ImportString("Text.\n\n<script>alert(1)</script>\n\n<style>p{}</style>\n");
        Assert.That(r.AsciiDoc.Trim(), Is.EqualTo("Text."));
        Assert.That(r.Report.Issues.Count(i => i.Code == "markdown.script.dropped"), Is.EqualTo(2));
    }

    [Test]
    public void Literal_markup_characters_stay_literal()
    {
        var adoc = Adoc("Price 2 \\* 3, \\*not bold\\*, a_b_c, {attr}, C++ and 1 -> 2.\n");
        var html = new HtmlRenderer().RenderToString(AdocNet.Parser.AdocParser.Parse(adoc).Document);
        Assert.That(Text(html), Does.Contain("Price 2 * 3, *not bold*, a_b_c, {attr}, C++ and 1 -> 2."));
    }

    // ── Parity: Markdown → AsciiDoc → HTML has the text and structure of Markdown → HTML ──

    private static readonly MarkdownPipeline Gfm = new MarkdownPipelineBuilder()
        .UsePipeTables().UseTaskLists().UseEmphasisExtras().UseAutoLinks().Build();

    private static readonly string[] ParitySamples =
    [
        "# Title\n\nIntro with **bold**, *em*, `code` and a [link](https://example.com).\n\n## Section\n\nSecond paragraph.\n",
        "## A\n\n- one\n- two\n  - nested\n\n1. first\n2. second\n\n> quoted *text*\n",
        "| a | b |\n|---|:-:|\n| 1 | 2 |\n| 3 | 4 |\n\n```js\nlet x = 1;\n```\n",
        "Line one  \nline two\n\n---\n\n- [x] done\n- [ ] open\n\nText with ~~strike~~ and 2 * 3 = 6.\n",
    ];

    [TestCaseSource(nameof(ParitySamples))]
    public void Rendered_text_and_structure_match_markdig(string markdown)
    {
        var expected = new HtmlParser().ParseDocument(Markdown.ToHtml(markdown, Gfm));
        var adoc = Adoc(markdown);
        var actual = new HtmlParser().ParseDocument(new HtmlRenderer().RenderToString(AdocNet.Parser.AdocParser.Parse(adoc).Document,
            new HtmlRenderOptions { FullDocument = true }));

        Assert.That(Text(actual.Body!.InnerHtml), Is.EqualTo(Text(expected.Body!.InnerHtml)), adoc);
        foreach (var selector in new[] { "h1,h2,h3", "li", "td,th", "pre", "blockquote", "strong", "em", "a[href]" })
            Assert.That(actual.QuerySelectorAll(selector).Length, Is.EqualTo(expected.QuerySelectorAll(selector).Length), $"{selector}\n{adoc}");
    }

    /// <summary>Visible text, whitespace collapsed (checkbox glyphs and the AsciiDoc footer aside).</summary>
    private static string Text(string html)
    {
        var doc = new HtmlParser().ParseDocument(html);
        foreach (var e in doc.QuerySelectorAll("#footer, #footnotes, script, style, head").ToList()) e.Remove();
        var text = doc.Body!.TextContent.Replace("✓", "").Replace("❏", "").Replace("☐", "").Replace("☑", "").Replace("✗", "");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
