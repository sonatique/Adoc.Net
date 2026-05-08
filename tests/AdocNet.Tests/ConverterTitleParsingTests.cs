using System.Text;
using AdocNet;
using AdocNet.Ast;
using AdocNet.Converters.Man;
using AdocNet.Converters.Revealjs;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class ConverterTitleParsingTests
{
    private static string RenderMan(string adoc)
    {
        var doc = BlockParser.Parse(adoc).Document;
        using var ms = new MemoryStream();
        new ManRenderer().Render(doc, ms, RenderOptions.Default);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string RenderRevealjs(string adoc)
    {
        var doc = BlockParser.Parse(adoc).Document;
        using var ms = new MemoryStream();
        new RevealjsRenderer().Render(doc, ms, RenderOptions.Default);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ── Man: block titles parse inline formatting ─────────────────────────────

    [Test]
    public void Man_example_block_title_parses_backticks_as_bold()
    {
        var output = RenderMan(".Use `MyClass`\n====\nbody.\n====");
        // groff bold open is \fB; backticks should not appear literally.
        Assert.That(output, Does.Not.Contain("`MyClass`"));
        Assert.That(output, Does.Contain("MyClass"));
    }

    [Test]
    public void Man_listing_block_title_parses_backticks()
    {
        var output = RenderMan(".Code for `Foo`\n----\nx = 1\n----");
        Assert.That(output, Does.Not.Contain("`Foo`"));
    }

    [Test]
    public void Man_link_label_parses_backticks()
    {
        var output = RenderMan("See link:http://x.com[the `Foo` page] for more.");
        Assert.That(output, Does.Not.Contain("`Foo`"));
    }

    // Regression: plain titles render unchanged
    [Test]
    public void Man_plain_block_title_unchanged()
    {
        var output = RenderMan(".Plain title\n====\nbody.\n====");
        Assert.That(output, Does.Contain("Plain title"));
    }

    // ── Reveal.js: titles parse inline formatting ─────────────────────────────

    [Test]
    public void Revealjs_section_title_parses_backticks()
    {
        var output = RenderRevealjs("= Doc\n\n== Use `MyClass`\n\nbody");
        // backticks should produce <code> in HTML output
        Assert.That(output, Does.Contain("<code"));
        Assert.That(output, Does.Not.Contain("Use `MyClass`"));
    }

    [Test]
    public void Revealjs_section_title_parses_emphasis()
    {
        var output = RenderRevealjs("= Doc\n\n== A *bold* word\n\nbody");
        Assert.That(output, Does.Contain("<strong>bold</strong>"));
    }

    [Test]
    public void Revealjs_link_label_parses_backticks()
    {
        var output = RenderRevealjs("= Doc\n\n== Slide\n\nSee link:http://x.com[the `Foo` page] for more.");
        Assert.That(output, Does.Not.Contain("`Foo`"));
    }

    // Regression: plain section title still renders
    [Test]
    public void Revealjs_plain_section_title_unchanged()
    {
        var output = RenderRevealjs("= Doc\n\n== Plain Slide Title\n\nbody");
        Assert.That(output, Does.Contain("Plain Slide Title"));
    }
}
