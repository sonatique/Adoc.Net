using AdocNet.Converters.Pdf;

namespace AdocNet.Tests;

[TestFixture]
public class PdfThemeLoaderTests
{
    private static PdfRenderOptions BuildFromYaml(params string[] lines)
    {
        var props = PdfThemeLoader.ParseYaml(lines);
        return PdfThemeLoader.BuildOptions(props, ".");
    }

    // ── YAML parsing ──────────────────────────────────────────────────

    [Test]
    public void ParseYaml_flat_key_value()
    {
        var props = PdfThemeLoader.ParseYaml(["base:", "  font-size: 14"]);
        Assert.That(props["base.font-size"], Is.EqualTo("14"));
    }

    [Test]
    public void ParseYaml_strips_quotes()
    {
        var props = PdfThemeLoader.ParseYaml(["key: 'hello'"]);
        Assert.That(props["key"], Is.EqualTo("hello"));
    }

    [Test]
    public void ParseYaml_ignores_comments()
    {
        var props = PdfThemeLoader.ParseYaml(["# a comment", "key: value"]);
        Assert.That(props, Has.Count.EqualTo(1));
        Assert.That(props["key"], Is.EqualTo("value"));
    }

    // ── Heading sizes ─────────────────────────────────────────────────

    [Test]
    public void Heading_font_sizes_loaded_from_theme()
    {
        var opts = BuildFromYaml(
            "heading-h2:", "  font-size: 20",
            "heading-h3:", "  font-size: 16",
            "heading-h4:", "  font-size: 13",
            "heading-h5:", "  font-size: 11");

        Assert.That(opts.Heading2FontSize, Is.EqualTo(20f));
        Assert.That(opts.Heading3FontSize, Is.EqualTo(16f));
        Assert.That(opts.Heading4FontSize, Is.EqualTo(13f));
        Assert.That(opts.Heading5FontSize, Is.EqualTo(11f));
    }

    // ── Heading margins ───────────────────────────────────────────────

    [Test]
    public void Heading_margin_bottom_loaded_per_level()
    {
        var opts = BuildFromYaml(
            "heading-h2:", "  margin-bottom: 8",
            "heading-h3:", "  margin-bottom: 4");

        Assert.That(opts.Heading2MarginBottom, Is.EqualTo(8f));
        Assert.That(opts.Heading3MarginBottom, Is.EqualTo(4f));
        Assert.That(opts.Heading4MarginBottom, Is.Null);
    }

    [Test]
    public void Section_spacing_from_heading_h2_margin_top()
    {
        var opts = BuildFromYaml("heading-h2:", "  margin-top: 14");
        Assert.That(opts.SectionSpacing, Is.EqualTo(14f));
    }

    [Test]
    public void Title_margin_bottom_from_heading_h1()
    {
        var opts = BuildFromYaml("heading-h1:", "  margin-bottom: 0");
        Assert.That(opts.TitleMarginBottom, Is.EqualTo(0f));
    }

    [Test]
    public void Title_margin_bottom_defaults_to_16()
    {
        var opts = BuildFromYaml("base:", "  font-size: 11");
        Assert.That(opts.TitleMarginBottom, Is.EqualTo(16f));
    }

    // ── Heading colors ────────────────────────────────────────────────

    [Test]
    public void Heading_colors_loaded_per_level()
    {
        var opts = BuildFromYaml(
            "heading-h2:", "  font-color: #365f91",
            "heading-h4:", "  font-color: #ff0000");

        Assert.That(opts.Heading2Color, Is.Not.Null);
        Assert.That(opts.Heading3Color, Is.Null);
        Assert.That(opts.Heading4Color, Is.Not.Null);
    }

    [Test]
    public void Heading_color_falls_back_to_h1()
    {
        var opts = BuildFromYaml("heading-h1:", "  font-color: #365f91");
        Assert.That(opts.HeadingColor, Is.Not.Null);
    }

    // ── Title line height ─────────────────────────────────────────────

    [Test]
    public void Title_line_height_loaded_from_heading_h1()
    {
        var opts = BuildFromYaml("heading-h1:", "  line-height: 1");
        Assert.That(opts.TitleLineHeight, Is.EqualTo(1f));
    }

    [Test]
    public void Title_line_height_null_when_not_specified()
    {
        var opts = BuildFromYaml("base:", "  font-size: 11");
        Assert.That(opts.TitleLineHeight, Is.Null);
    }

    // ── Page margins ──────────────────────────────────────────────────

    [Test]
    public void Page_margins_parsed_from_array()
    {
        var opts = BuildFromYaml("page:", "  margin: [64, 48, 68, 48]");
        Assert.That(opts.MarginTop, Is.EqualTo(64f));
        Assert.That(opts.MarginRight, Is.EqualTo(48f));
        Assert.That(opts.MarginBottom, Is.EqualTo(68f));
        Assert.That(opts.MarginLeft, Is.EqualTo(48f));
    }

    // ── Color parsing ─────────────────────────────────────────────────

    [Test]
    public void ParseColor_hex_with_hash()
    {
        var c = PdfThemeLoader.ParseColor("#ff0000");
        Assert.That(c, Is.Not.Null);
        Assert.That(c!.Value.R, Is.EqualTo(1f));
        Assert.That(c.Value.G, Is.EqualTo(0f));
    }

    [Test]
    public void ParseColor_hex_without_hash()
    {
        var c = PdfThemeLoader.ParseColor("00ff00");
        Assert.That(c, Is.Not.Null);
        Assert.That(c!.Value.G, Is.EqualTo(1f));
    }

    [Test]
    public void ParseColor_null_returns_null()
    {
        Assert.That(PdfThemeLoader.ParseColor(null), Is.Null);
    }

    // ── Code block default border ─────────────────────────────────────

    [Test]
    public void Code_border_color_default_is_set()
    {
        var opts = PdfRenderOptions.Default;
        Assert.That(opts.CodeBorderColor, Is.Not.Null);
    }

    // ── Header/footer template ────────────────────────────────────────

    [Test]
    public void Header_template_translates_placeholders()
    {
        var opts = BuildFromYaml(
            "header:", "  recto:", "    right:",
            "      content: '{page-number} of {page-count}'");

        Assert.That(opts.HeaderText, Is.EqualTo("{page} of {pages}"));
    }

    [Test]
    public void Footer_template_translates_section_title()
    {
        var opts = BuildFromYaml(
            "footer:", "  recto:", "    right:",
            "      content: '{section-or-chapter-title} | {page-number}'");

        Assert.That(opts.FooterText, Is.EqualTo("{section-title} | {page}"));
    }

    // ── Footer/header height ──────────────────────────────────────────

    [Test]
    public void Footer_height_loaded_from_theme()
    {
        var opts = BuildFromYaml(
            "footer:", "  height: 48",
            "  recto:", "    right:",
            "      content: '{page-number}'");

        Assert.That(opts.FooterHeight, Is.EqualTo(48f));
    }

    [Test]
    public void Header_height_loaded_from_theme()
    {
        var opts = BuildFromYaml(
            "header:", "  height: 64",
            "  recto:", "    right:",
            "      content: '{page-number}'");

        Assert.That(opts.HeaderHeight, Is.EqualTo(64f));
    }

    [Test]
    public void Footer_height_defaults_to_zero()
    {
        var opts = BuildFromYaml("base:", "  font-size: 11");
        Assert.That(opts.FooterHeight, Is.EqualTo(0f));
    }
}
