using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class GithubThemeTests
{
    private const string SimpleAdoc = "= Test Document\n\nHello world.";

    [Test]
    public void Github_theme_produces_full_document_with_Github_CSS()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = new HtmlRenderOptions { Theme = HtmlTheme.Github };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("<!DOCTYPE html>"));
        Assert.That(html, Does.Contain("<style>"));
        // Github theme uses Noto Sans font and specific border color
        Assert.That(html, Does.Contain("Noto Sans"), "Should contain Github font stack");
        Assert.That(html, Does.Contain("#d1d9e0"), "Should contain Github border color");
    }

    [Test]
    public void Github_theme_differs_from_Default_theme()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;

        var defaultHtml = new HtmlRenderer().RenderToString(doc,
            new HtmlRenderOptions { Theme = HtmlTheme.Default });
        var githubHtml = new HtmlRenderer().RenderToString(doc,
            new HtmlRenderOptions { Theme = HtmlTheme.Github });

        Assert.That(githubHtml, Is.Not.EqualTo(defaultHtml),
            "Github and Default themes should produce different output");
    }

    [Test]
    public void Github_theme_contains_syntax_highlighting_rules()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = new HtmlRenderOptions { Theme = HtmlTheme.Github };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain(".hl-kw"), "Should contain keyword highlighting rule");
        Assert.That(html, Does.Contain("#cf222e"), "Should contain Github keyword color");
    }

    [Test]
    public void Github_theme_output_is_deterministic()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = new HtmlRenderOptions { Theme = HtmlTheme.Github };

        var html1 = new HtmlRenderer().RenderToString(doc, options);
        var html2 = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html1, Is.EqualTo(html2));
    }

    [Test]
    public void CustomCss_with_Github_theme_overrides_styles()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = new HtmlRenderOptions
        {
            Theme = HtmlTheme.Github,
            CustomCss = "body { color: purple; }"
        };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("body { color: purple; }"),
            "Custom CSS should appear after theme CSS");
        Assert.That(html, Does.Contain("#d1d9e0"),
            "Theme CSS should still be present");
    }
}
