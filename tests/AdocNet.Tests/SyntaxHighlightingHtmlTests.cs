using AdocNet.Ast;
using AdocNet.Converters.Html;

namespace AdocNet.Tests;

[TestFixture]
public class SyntaxHighlightingHtmlTests
{
    [Test]
    public void CSharp_source_block_emits_span_classes()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "public class Foo { }"
        });

        var options = new HtmlRenderOptions { EnableSyntaxHighlighting = true };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("class=\"hl-kw\""), "Should contain keyword span class");
        Assert.That(html, Does.Contain("public"), "Should contain keyword text");
    }

    [Test]
    public void Highlighting_disabled_produces_no_span_classes()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "public class Foo { }"
        });

        var options = new HtmlRenderOptions { EnableSyntaxHighlighting = false };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Not.Contain("hl-kw"), "Should NOT contain highlighting spans");
        Assert.That(html, Does.Contain("public"), "Should still contain source text");
    }

    [Test]
    public void Unsupported_language_falls_back_to_plain()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "rust",
            Content = "fn main() { }"
        });

        var options = new HtmlRenderOptions { EnableSyntaxHighlighting = true };
        var html = new HtmlRenderer().RenderToString(doc, options);
        Assert.That(html, Does.Not.Contain("hl-kw"), "Unsupported language should not highlight");
        Assert.That(html, Does.Contain("fn main()"), "Should contain plain source text");
    }

    [Test]
    public void HighlightJs_mode_skips_server_highlighting()
    {
        var doc = new DocumentNode();
        doc.SetAttribute("source-highlighter", "highlight.js");
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "public class Foo { }"
        });

        var html = new HtmlRenderer().RenderToString(doc);
        Assert.That(html, Does.Not.Contain("hl-kw"), "highlight.js mode should skip server highlighting");
        Assert.That(html, Does.Contain("highlightjs"), "Should contain highlightjs class");
    }

    [Test]
    public void Theme_CSS_contains_syntax_highlighting_rules()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "class X { }"
        });

        var options = new HtmlRenderOptions { Theme = HtmlTheme.Default };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain(".hl-kw"), "Default theme CSS should contain syntax highlight rules");
        Assert.That(html, Does.Contain(".hl-s"), "Default theme CSS should contain string highlighting rule");
    }

    [Test]
    public void HTML_output_is_deterministic()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "int x = 42;"
        });

        var html1 = new HtmlRenderer().RenderToString(doc);
        var html2 = new HtmlRenderer().RenderToString(doc);
        Assert.That(html1, Is.EqualTo(html2));
    }
}
