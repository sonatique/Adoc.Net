using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class CssAttributeTests
{
    private static string RenderFullDoc(string adoc, HtmlRenderOptions? options = null)
    {
        var doc = AdocParser.Parse(adoc).Document;
        options ??= new HtmlRenderOptions { Theme = HtmlTheme.Default };
        return new HtmlRenderer().RenderToString(doc, options);
    }

    // ── :stylesheet: + :linkcss: → <link> tag ────────────────────────────

    [Test]
    public void Stylesheet_and_linkcss_produces_link_tag()
    {
        var adoc = "= Test\n:stylesheet: custom.css\n:linkcss:\n\nHello.";
        var html = RenderFullDoc(adoc);
        Assert.That(html, Does.Contain("<link rel=\"stylesheet\" href=\"./custom.css\">"));
        Assert.That(html, Does.Not.Contain("<style>"));
    }

    [Test]
    public void Stylesheet_linkcss_stylesdir_resolves_path()
    {
        var adoc = "= Test\n:stylesheet: custom.css\n:linkcss:\n:stylesdir: css\n\nHello.";
        var html = RenderFullDoc(adoc);
        Assert.That(html, Does.Contain("<link rel=\"stylesheet\" href=\"css/custom.css\">"));
    }

    [Test]
    public void Linkcss_without_stylesheet_uses_default_name()
    {
        var adoc = "= Test\n:linkcss:\n\nHello.";
        var html = RenderFullDoc(adoc);
        Assert.That(html, Does.Contain("<link rel=\"stylesheet\" href=\"./asciidoctor.css\">"));
        Assert.That(html, Does.Not.Contain("<style>"));
    }

    [Test]
    public void Stylesheet_absolute_url_used_as_is()
    {
        var adoc = "= Test\n:stylesheet: https://example.com/style.css\n:linkcss:\n\nHello.";
        var html = RenderFullDoc(adoc);
        Assert.That(html, Does.Contain("<link rel=\"stylesheet\" href=\"https://example.com/style.css\">"));
    }

    // ── :stylesheet: without :linkcss: → fallback to theme ───────────────

    [Test]
    public void Stylesheet_without_linkcss_uses_theme_css()
    {
        // Without :linkcss:, we can't read the file, so fallback to theme CSS
        var adoc = "= Test\n:stylesheet: custom.css\n\nHello.";
        var html = RenderFullDoc(adoc);
        // Should still have theme CSS embedded (not link)
        Assert.That(html, Does.Contain("<style>"));
    }

    // ── API CustomCss takes precedence ───────────────────────────────────

    [Test]
    public void Api_custom_css_takes_precedence_over_stylesheet_attribute()
    {
        var adoc = "= Test\n:stylesheet: custom.css\n:linkcss:\n\nHello.";
        var options = new HtmlRenderOptions
        {
            Theme = HtmlTheme.Default,
            CustomCss = ".api { color: blue; }",
        };
        var html = RenderFullDoc(adoc, options);
        Assert.That(html, Does.Contain("<style>"));
        Assert.That(html, Does.Contain(".api { color: blue; }"));
        Assert.That(html, Does.Not.Contain("<link rel=\"stylesheet\" href=\"custom.css\">"));
    }

    // ── Empty :stylesheet: suppresses CSS ────────────────────────────────

    [Test]
    public void Empty_stylesheet_suppresses_all_css()
    {
        var adoc = "= Test\n:stylesheet:\n\nHello.";
        var html = RenderFullDoc(adoc);
        Assert.That(html, Does.Not.Contain("<style>"));
        Assert.That(html, Does.Not.Contain("<link rel=\"stylesheet\""));
    }

    // ── Regression: no attributes → theme CSS ────────────────────────────

    [Test]
    public void Regression_no_css_attributes_uses_theme()
    {
        var adoc = "= Test\n\nHello.";
        var html = RenderFullDoc(adoc);
        Assert.That(html, Does.Contain("<style>"));
    }
}
