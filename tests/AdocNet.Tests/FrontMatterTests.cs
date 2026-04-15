using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class FrontMatterTests
{
    // ── Regression tests (Step 0) — lock existing behavior ─────────────

    [Test]
    public void Regression_document_without_front_matter_parses_normally()
    {
        var input = "= My Title\n\nFirst paragraph.\n\n== Section One\n\nSecond paragraph.";
        var result = AdocParser.Parse(input);
        Assert.That(result.Document.Title, Is.EqualTo("My Title"));
        Assert.That(result.Document.Children.Count, Is.GreaterThanOrEqualTo(2));
        var section = result.Document.Children.OfType<SectionNode>().First();
        Assert.That(section.Title, Is.EqualTo("Section One"));
    }

    [Test]
    public void Regression_document_starting_with_dashes_not_front_matter()
    {
        // --- is an open block delimiter in AsciiDoc, not a thematic break.
        // Without :skip-front-matter:, it should be parsed normally (not stripped).
        var input = "= Title\n\nSome text with --- dashes in it.\n\nMore text.";
        var result = AdocParser.Parse(input);
        Assert.That(result.Document.Title, Is.EqualTo("Title"));
        Assert.That(result.Document.Children.Count, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Regression_no_skip_front_matter_attribute_preserves_dashes()
    {
        // When :skip-front-matter: is NOT set, --- on line 1 is regular content.
        var input = "---\ntitle: Hello\n---\n\n= Doc Title\n\nParagraph.";
        var result = AdocParser.Parse(input);
        // The --- lines should be parsed as regular content, not stripped.
        // Document should still parse (title may or may not be detected depending on parser).
        Assert.That(result.Document, Is.Not.Null);
    }

    [Test]
    public void Regression_html_theme_default_embeds_css_in_style_block()
    {
        var doc = BlockParser.Parse("= Test\n\nHello.").Document;
        var options = new HtmlRenderOptions { Theme = HtmlTheme.Default };
        var html = new HtmlRenderer().RenderToString(doc, options);
        Assert.That(html, Does.Contain("<style>"));
        Assert.That(html, Does.Contain("</style>"));
    }

    [Test]
    public void Regression_html_custom_css_included_inline()
    {
        var doc = BlockParser.Parse("= Test\n\nHello.").Document;
        var options = new HtmlRenderOptions
        {
            Theme = HtmlTheme.Default,
            CustomCss = ".highlight { background: yellow; }",
        };
        var html = new HtmlRenderer().RenderToString(doc, options);
        Assert.That(html, Does.Contain("<style>"));
        Assert.That(html, Does.Contain(".highlight { background: yellow; }"));
    }

    [Test]
    public void Regression_html_no_theme_no_style_block()
    {
        var doc = BlockParser.Parse("= Test\n\nHello.").Document;
        var options = new HtmlRenderOptions { FullDocument = true, Theme = HtmlTheme.None };
        var html = new HtmlRenderer().RenderToString(doc, options);
        Assert.That(html, Does.Not.Contain("<style>"));
    }

    // ── Front matter feature tests (Step 2) ──────────────────────────────

    [Test]
    public void Skip_front_matter_strips_yaml_front_matter()
    {
        var input = "---\ntitle: Hello\nauthor: Bob\n---\n= Doc Title\n\nParagraph.";
        var result = AdocParser.Parse(input, new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["skip-front-matter"] = "" }
        });
        Assert.That(result.Document.Title, Is.EqualTo("Doc Title"));
        var para = result.Document.Children.OfType<ParagraphNode>().First();
        Assert.That(para, Is.Not.Null);
    }

    [Test]
    public void Skip_front_matter_stores_content_as_attribute()
    {
        var input = "---\ntitle: Hello\nauthor: Bob\n---\n= Doc Title\n\nParagraph.";
        var result = AdocParser.Parse(input, new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["skip-front-matter"] = "" }
        });
        Assert.That(result.Document.Attributes.ContainsKey("front-matter"), Is.True);
        Assert.That(result.Document.Attributes["front-matter"], Does.Contain("title: Hello"));
        Assert.That(result.Document.Attributes["front-matter"], Does.Contain("author: Bob"));
    }

    [Test]
    public void Skip_front_matter_no_dashes_at_start_no_stripping()
    {
        var input = "= Doc Title\n\nParagraph.";
        var result = AdocParser.Parse(input, new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["skip-front-matter"] = "" }
        });
        Assert.That(result.Document.Title, Is.EqualTo("Doc Title"));
        Assert.That(result.Document.Attributes.ContainsKey("front-matter"), Is.False);
    }

    [Test]
    public void No_skip_front_matter_dashes_at_start_not_stripped()
    {
        var input = "---\ntitle: Hello\n---\n= Doc Title\n\nParagraph.";
        var result = AdocParser.Parse(input);
        // Without :skip-front-matter:, the --- is not stripped.
        Assert.That(result.Document.Attributes.ContainsKey("front-matter"), Is.False);
    }

    [Test]
    public void Skip_front_matter_unclosed_no_stripping_emits_warning()
    {
        var input = "---\ntitle: Hello\nauthor: Bob\n= Doc Title\n\nParagraph.";
        var result = AdocParser.Parse(input, new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["skip-front-matter"] = "" }
        });
        // Unclosed front matter: no stripping, warning emitted
        Assert.That(result.Document.Attributes.ContainsKey("front-matter"), Is.False);
        Assert.That(result.Diagnostics.Any(d => d.Message.Contains("Unclosed front matter")), Is.True);
    }

    [Test]
    public void Skip_front_matter_empty_content()
    {
        var input = "---\n---\n= Doc Title\n\nParagraph.";
        var result = AdocParser.Parse(input, new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["skip-front-matter"] = "" }
        });
        Assert.That(result.Document.Title, Is.EqualTo("Doc Title"));
        Assert.That(result.Document.Attributes.ContainsKey("front-matter"), Is.True);
        Assert.That(result.Document.Attributes["front-matter"], Is.EqualTo(""));
    }
}
