using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class HtmlThemeTests
{
    private const string SimpleAdoc = "= Test Document\n\nHello world.";

    // ── Fragment vs full document ───────────────────────────────────────

    [Test]
    public void Default_options_produces_fragment_without_DOCTYPE()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var html = new HtmlRenderer().RenderToString(doc);

        Assert.That(html, Does.Not.Contain("<!DOCTYPE html>"));
        Assert.That(html, Does.Not.Contain("<html"));
        Assert.That(html, Does.Not.Contain("<head>"));
        Assert.That(html, Does.Not.Contain("<body>"));
    }

    [Test]
    public void Theme_Default_produces_full_document_with_DOCTYPE_and_style()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = new HtmlRenderOptions { Theme = HtmlTheme.Default };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("<!DOCTYPE html>"));
        Assert.That(html, Does.Contain("<html lang=\"en\">"));
        Assert.That(html, Does.Contain("<head>"));
        Assert.That(html, Does.Contain("<body class=\"article\">"));
        Assert.That(html, Does.Contain("<style>"));
        Assert.That(html, Does.Contain("</body>"));
        Assert.That(html, Does.Contain("</html>"));
    }

    [Test]
    public void Theme_Asciidoctor_produces_full_document_with_Asciidoctor_CSS()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = new HtmlRenderOptions { Theme = HtmlTheme.Asciidoctor };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("<!DOCTYPE html>"));
        Assert.That(html, Does.Contain("<style>"));
        // Asciidoctor theme uses "Noto Serif" font
        Assert.That(html, Does.Contain("Noto Serif"));
    }

    [Test]
    public void Theme_Clean_produces_full_document_with_Clean_CSS()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = new HtmlRenderOptions { Theme = HtmlTheme.Clean };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("<!DOCTYPE html>"));
        Assert.That(html, Does.Contain("<style>"));
        // Clean theme uses Georgia font
        Assert.That(html, Does.Contain("Georgia"));
    }

    [Test]
    public void FullDocument_true_with_Theme_None_produces_document_without_style()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = new HtmlRenderOptions { FullDocument = true, Theme = HtmlTheme.None };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("<!DOCTYPE html>"));
        Assert.That(html, Does.Contain("<head>"));
        Assert.That(html, Does.Contain("<body class=\"article\">"));
        Assert.That(html, Does.Not.Contain("<style>"));
    }

    // ── CustomCss ───────────────────────────────────────────────────────

    [Test]
    public void CustomCss_is_appended_after_theme_CSS()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = new HtmlRenderOptions
        {
            Theme = HtmlTheme.Default,
            CustomCss = ".my-custom { color: red; }",
        };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("<style>"));
        Assert.That(html, Does.Contain(".my-custom { color: red; }"));
        // Custom CSS appears after theme CSS but before </style>
        var styleStart = html.IndexOf("<style>");
        var customPos = html.IndexOf(".my-custom");
        var styleEnd = html.IndexOf("</style>");
        Assert.That(customPos, Is.GreaterThan(styleStart));
        Assert.That(customPos, Is.LessThan(styleEnd));
    }

    // ── ExtraHead ───────────────────────────────────────────────────────

    [Test]
    public void ExtraHead_content_appears_in_head_section()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = new HtmlRenderOptions
        {
            Theme = HtmlTheme.Default,
            ExtraHead = "<link rel=\"stylesheet\" href=\"extra.css\">",
        };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("<link rel=\"stylesheet\" href=\"extra.css\">"));
        // ExtraHead should be in <head>, before </head>
        var extraPos = html.IndexOf("extra.css");
        var headEnd = html.IndexOf("</head>");
        Assert.That(extraPos, Is.LessThan(headEnd));
    }

    // ── Title ───────────────────────────────────────────────────────────

    [Test]
    public void Title_option_overrides_document_title()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = new HtmlRenderOptions
        {
            Theme = HtmlTheme.Default,
            Title = "Custom Title",
        };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("<title>Custom Title</title>"));
        Assert.That(html, Does.Not.Contain("<title>Test Document</title>"));
    }

    [Test]
    public void Document_title_used_when_Title_option_is_null()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = new HtmlRenderOptions { Theme = HtmlTheme.Default };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("<title>Test Document</title>"));
    }

    // ── Theme CSS class selectors ───────────────────────────────────────

    [TestCase(HtmlTheme.Default)]
    [TestCase(HtmlTheme.Asciidoctor)]
    [TestCase(HtmlTheme.Clean)]
    public void Theme_CSS_contains_expected_class_selectors(HtmlTheme theme)
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = new HtmlRenderOptions { Theme = theme };
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain(".admonitionblock"));
        Assert.That(html, Does.Contain(".listingblock"));
        Assert.That(html, Does.Contain(".exampleblock"));
        Assert.That(html, Does.Contain(".imageblock"));
        Assert.That(html, Does.Contain(".sidebarblock"));
    }

    // ── Styled convenience property ─────────────────────────────────────

    [Test]
    public void Styled_property_produces_full_document_with_Default_theme()
    {
        var doc = BlockParser.Parse(SimpleAdoc).Document;
        var options = HtmlRenderOptions.Styled;
        var html = new HtmlRenderer().RenderToString(doc, options);

        Assert.That(html, Does.Contain("<!DOCTYPE html>"));
        Assert.That(html, Does.Contain("<style>"));
        Assert.That(html, Does.Contain("<body class=\"article\">"));
        Assert.That(html, Does.Contain("Hello world."));
    }
}
