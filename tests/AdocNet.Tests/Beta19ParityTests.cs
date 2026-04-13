using AdocNet;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class Beta19ParityTests
{
    // ── Step 0: Regression tests — lock existing behavior ─────────────────

    [Test]
    public void Regression_dash_delimiter_produces_listing_block()
    {
        var doc = BlockParser.Parse("----\nsome code\n----").Document;
        var block = doc.Children.OfType<DelimitedBlockNode>().First();
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Listing));
            Assert.That(block.Content, Is.EqualTo("some code"));
        });
    }

    [Test]
    public void Regression_source_attribute_with_dash_delimiter_produces_source_block()
    {
        var doc = BlockParser.Parse("[source,java]\n----\nclass Foo {}\n----").Document;
        var block = doc.Children.OfType<DelimitedBlockNode>().First();
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
            Assert.That(block.Language, Is.EqualTo("java"));
            Assert.That(block.Content, Is.EqualTo("class Foo {}"));
        });
    }

    [Test]
    public void Regression_toc_generated_at_document_start()
    {
        var doc = BlockParser.Parse(":toc:\n\n== Section One\n\nText\n\n== Section Two\n\nMore text").Document;
        // TocNode should be first child
        Assert.That(doc.Children[0], Is.InstanceOf<TocNode>());
        var toc = (TocNode)doc.Children[0];
        Assert.That(toc.Entries, Has.Count.EqualTo(2));
    }

    [Test]
    public void Regression_image_block_macro_still_works()
    {
        var doc = BlockParser.Parse("image::photo.png[My Photo]").Document;
        var img = doc.Children.OfType<BlockImageNode>().First();
        Assert.Multiple(() =>
        {
            Assert.That(img.Target, Is.EqualTo("photo.png"));
            Assert.That(img.Alt, Is.EqualTo("My Photo"));
        });
    }

    // ── Step 2: Fenced code block tests ─────────────────────────────────

    [Test]
    public void Fenced_code_block_no_language()
    {
        var doc = BlockParser.Parse("```\nsome code\n```").Document;
        var block = doc.Children.OfType<DelimitedBlockNode>().First();
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
            Assert.That(block.Content, Is.EqualTo("some code"));
            Assert.That(block.Language, Is.Null);
        });
    }

    [Test]
    public void Fenced_code_block_with_language()
    {
        var doc = BlockParser.Parse("```java\nclass Foo {}\n```").Document;
        var block = doc.Children.OfType<DelimitedBlockNode>().First();
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
            Assert.That(block.Language, Is.EqualTo("java"));
            Assert.That(block.Content, Is.EqualTo("class Foo {}"));
        });
    }

    [Test]
    public void Fenced_code_block_with_csharp_language()
    {
        var doc = BlockParser.Parse("```csharp\nint x = 1;\n```").Document;
        var block = doc.Children.OfType<DelimitedBlockNode>().First();
        Assert.Multiple(() =>
        {
            Assert.That(block.Language, Is.EqualTo("csharp"));
            Assert.That(block.Content, Is.EqualTo("int x = 1;"));
        });
    }

    [Test]
    public void Fenced_code_block_unclosed_consumes_to_eof()
    {
        var result = BlockParser.Parse("```\nunclosed code\nmore code");
        var block = result.Document.Children.OfType<DelimitedBlockNode>().First();
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
            Assert.That(block.Content, Does.Contain("unclosed code"));
            Assert.That(result.Diagnostics, Has.Count.GreaterThan(0));
            Assert.That(result.Diagnostics[0].Message, Does.Contain("Unclosed"));
        });
    }

    [Test]
    public void Fenced_and_dash_blocks_mixed_in_same_document()
    {
        var input = "```java\nfenced code\n```\n\n----\nlisting code\n----";
        var doc = BlockParser.Parse(input).Document;
        var blocks = doc.Children.OfType<DelimitedBlockNode>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(blocks, Has.Count.EqualTo(2));
            Assert.That(blocks[0].BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
            Assert.That(blocks[0].Language, Is.EqualTo("java"));
            Assert.That(blocks[1].BlockKind, Is.EqualTo(DelimitedBlockKind.Listing));
        });
    }

    [Test]
    public void Backticks_inside_listing_block_not_interpreted_as_fenced()
    {
        var input = "----\n```\nnot a fenced block\n```\n----";
        var doc = BlockParser.Parse(input).Document;
        var blocks = doc.Children.OfType<DelimitedBlockNode>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(blocks, Has.Count.EqualTo(1));
            Assert.That(blocks[0].BlockKind, Is.EqualTo(DelimitedBlockKind.Listing));
            Assert.That(blocks[0].Content, Does.Contain("```"));
        });
    }

    [Test]
    public void Fenced_code_with_source_attribute_uses_attribute_language()
    {
        var doc = BlockParser.Parse("[source,ruby]\n```\nputs 'hi'\n```").Document;
        var block = doc.Children.OfType<DelimitedBlockNode>().First();
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
            Assert.That(block.Language, Is.EqualTo("ruby"));
        });
    }

    [Test]
    public void Dash_blocks_still_work_after_fenced_support_added()
    {
        var doc = BlockParser.Parse("----\nexisting behavior\n----").Document;
        var block = doc.Children.OfType<DelimitedBlockNode>().First();
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Listing));
            Assert.That(block.Content, Is.EqualTo("existing behavior"));
        });
    }

    // ── Step 6: Book doctype / section style tests ────────────────────────

    [Test]
    public void Appendix_style_sets_section_style()
    {
        var doc = BlockParser.Parse("[appendix]\n== Appendix Title").Document;
        var section = doc.Children.OfType<SectionNode>().First();
        Assert.That(section.Style, Is.EqualTo("appendix"));
    }

    [Test]
    public void Glossary_style_sets_section_style()
    {
        var doc = BlockParser.Parse("[glossary]\n== Glossary").Document;
        var section = doc.Children.OfType<SectionNode>().First();
        Assert.That(section.Style, Is.EqualTo("glossary"));
    }

    [Test]
    public void Colophon_style_sets_section_style()
    {
        var doc = BlockParser.Parse("[colophon]\n== Colophon").Document;
        var section = doc.Children.OfType<SectionNode>().First();
        Assert.That(section.Style, Is.EqualTo("colophon"));
    }

    [Test]
    public void Dedication_style_sets_section_style()
    {
        var doc = BlockParser.Parse("[dedication]\n== Dedication").Document;
        var section = doc.Children.OfType<SectionNode>().First();
        Assert.That(section.Style, Is.EqualTo("dedication"));
    }

    [Test]
    public void Preface_style_sets_section_style()
    {
        var doc = BlockParser.Parse("[preface]\n== Preface").Document;
        var section = doc.Children.OfType<SectionNode>().First();
        Assert.That(section.Style, Is.EqualTo("preface"));
    }

    [Test]
    public void Normal_section_has_no_style()
    {
        var doc = BlockParser.Parse("== Normal Section").Document;
        var section = doc.Children.OfType<SectionNode>().First();
        Assert.That(section.Style, Is.Null);
    }

    [Test]
    public void Appendix_renders_with_prefix()
    {
        var input = "[appendix]\n== Resources\n\nSome text";
        var html = RenderHtml(input);
        Assert.That(html, Does.Contain("Appendix A: Resources"));
    }

    [Test]
    public void Multiple_appendixes_get_sequential_letters()
    {
        var input = "[appendix]\n== First\n\nText\n\n[appendix]\n== Second\n\nText";
        var html = RenderHtml(input);
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("Appendix A: First"));
            Assert.That(html, Does.Contain("Appendix B: Second"));
        });
    }

    [Test]
    public void Default_doctype_article_has_no_part_behavior()
    {
        var doc = BlockParser.Parse("== Normal Section\n\nText").Document;
        var section = doc.Children.OfType<SectionNode>().First();
        Assert.Multiple(() =>
        {
            Assert.That(section.Style, Is.Null);
            Assert.That(section.Level, Is.EqualTo(1));
        });
    }

    // ── Step 8: toc::[] macro tests ─────────────────────────────────────

    [Test]
    public void Toc_macro_places_toc_at_macro_position()
    {
        var input = ":toc: macro\n\n== Section One\n\nSome text\n\ntoc::[]\n\n== Section Two\n\nMore text";
        var doc = BlockParser.Parse(input).Document;
        // TocNode should NOT be at position 0 — it should be after Section One content.
        Assert.That(doc.Children[0], Is.Not.InstanceOf<TocNode>());
        var tocIndex = -1;
        for (int i = 0; i < doc.Children.Count; i++)
        {
            if (doc.Children[i] is TocNode)
            {
                tocIndex = i;
                break;
            }
        }
        Assert.That(tocIndex, Is.GreaterThan(0), "TOC should not be at position 0");
        var toc = (TocNode)doc.Children[tocIndex];
        Assert.That(toc.Entries, Has.Count.EqualTo(2));
    }

    [Test]
    public void Toc_without_macro_still_at_document_start()
    {
        var input = ":toc:\n\n== Section One\n\nText\n\n== Section Two\n\nMore text";
        var doc = BlockParser.Parse(input).Document;
        Assert.That(doc.Children[0], Is.InstanceOf<TocNode>());
    }

    [Test]
    public void Toc_macro_without_toc_attribute_is_ignored()
    {
        // No :toc: attribute, so toc::[] should just be recognized but no TOC generated.
        var input = "== Section\n\ntoc::[]\n\nText";
        var doc = BlockParser.Parse(input).Document;
        // The toc::[] creates a TocNode placeholder but since :toc: isn't set,
        // the post-parse step won't populate it. The placeholder remains.
        var tocNodes = doc.Children.OfType<TocNode>().ToList();
        // With no :toc: attribute, the empty placeholder stays but has no entries.
        Assert.That(tocNodes.All(t => t.Entries.Count == 0), Is.True);
    }

    [Test]
    public void Toc_macro_fallback_when_no_placeholder()
    {
        // :toc: macro but no toc::[] in the document — falls back to position 0.
        var input = ":toc: macro\n\n== Section\n\nText";
        var doc = BlockParser.Parse(input).Document;
        Assert.That(doc.Children[0], Is.InstanceOf<TocNode>());
    }

    // ── P03 Step 0: Regression tests — rendering attributes ─────────────

    [Test]
    public void Regression_full_doc_mode_always_shows_title()
    {
        var html = RenderHtmlFull("= My Title\n\nContent");
        Assert.That(html, Does.Contain("<h1>My Title</h1>"));
    }

    [Test]
    public void Regression_footnotes_rendered_when_present()
    {
        var html = RenderHtml("Text with a footnote.footnote:[This is a footnote.]");
        Assert.That(html, Does.Contain("<div id=\"footnotes\">"));
    }

    [Test]
    public void Regression_source_block_without_language_has_no_class()
    {
        var html = RenderHtml("[source]\n----\ncode\n----");
        Assert.That(html, Does.Contain("<code>"));
        Assert.That(html, Does.Not.Contain("language-"));
    }

    [Test]
    public void Regression_link_macro_label_is_entire_bracket_content()
    {
        var html = RenderHtml("link:https://example.com[Click here, window=_blank]");
        // Without :linkattrs:, entire bracket content is the label.
        Assert.That(html, Does.Contain(">Click here, window=_blank</a>"));
    }

    [Test]
    public void Regression_full_doc_epilogue_has_body_close()
    {
        var html = RenderHtmlFull("= Title\n\nContent");
        Assert.That(html, Does.Contain("</body>"));
        Assert.That(html, Does.Contain("</html>"));
    }

    // ── P03 Step 6: Feature tests — rendering attributes I ────────────

    [Test]
    public void Showtitle_in_embedded_mode_renders_title()
    {
        var html = RenderHtml(":showtitle:\n\n= My Title\n\nContent");
        Assert.That(html, Does.Contain("<h1>My Title</h1>"));
    }

    [Test]
    public void Notitle_suppresses_title()
    {
        var html = RenderHtml(":notitle:\n\n= My Title\n\nContent");
        Assert.That(html, Does.Not.Contain("<h1>"));
    }

    [Test]
    public void Default_embedded_mode_shows_title()
    {
        var html = RenderHtml("= My Title\n\nContent");
        Assert.That(html, Does.Contain("<h1>My Title</h1>"));
    }

    [Test]
    public void Nofooter_suppresses_footer_in_full_document()
    {
        var html = RenderHtmlFull(":nofooter:\n\n= Title\n\nContent");
        Assert.That(html, Does.Not.Contain("<div id=\"footer\">"));
    }

    [Test]
    public void No_nofooter_renders_footer_in_full_document()
    {
        var html = RenderHtmlFull("= Title\n\nContent");
        Assert.That(html, Does.Contain("<div id=\"footer\">"));
    }

    [Test]
    public void Nofootnotes_suppresses_footnote_section()
    {
        var html = RenderHtml(":nofootnotes:\n\nText with a footnote.footnote:[Hidden footnote.]");
        Assert.That(html, Does.Not.Contain("<div id=\"footnotes\">"));
        // But inline reference should still be present.
        Assert.That(html, Does.Contain("footnote"));
    }

    [Test]
    public void Source_language_sets_default_language()
    {
        var html = RenderHtml(":source-language: python\n\n[source]\n----\nprint('hello')\n----");
        Assert.That(html, Does.Contain("language-python"));
    }

    [Test]
    public void Explicit_language_overrides_source_language()
    {
        var html = RenderHtml(":source-language: python\n\n[source,java]\n----\nSystem.out.println();\n----");
        Assert.That(html, Does.Contain("language-java"));
        Assert.That(html, Does.Not.Contain("language-python"));
    }

    [Test]
    public void Linkattrs_enables_window_attribute()
    {
        var html = RenderHtml(":linkattrs:\n\nlink:https://example.com[Click, window=_blank]");
        Assert.That(html, Does.Contain("target=\"_blank\""));
        Assert.That(html, Does.Contain(">Click</a>"));
    }

    [Test]
    public void No_linkattrs_treats_bracket_as_plain_label()
    {
        var html = RenderHtml("link:https://example.com[Click, window=_blank]");
        Assert.That(html, Does.Not.Contain("target="));
        Assert.That(html, Does.Contain(">Click, window=_blank</a>"));
    }

    // ── P04 Step 0: Regression tests — rendering attributes II ──────────

    [Test]
    public void Regression_section_heading_plain_no_anchor()
    {
        var html = RenderHtml("== My Section\n\nContent");
        Assert.That(html, Does.Contain("<h2"));
        Assert.That(html, Does.Not.Contain("class=\"anchor\""));
        Assert.That(html, Does.Not.Contain("class=\"link\""));
    }

    [Test]
    public void Regression_bare_url_shows_full_scheme()
    {
        var html = RenderHtml("https://example.com");
        Assert.That(html, Does.Contain(">https://example.com</a>"));
    }

    [Test]
    public void Regression_no_google_fonts_link()
    {
        var html = RenderHtmlFull("= Title\n\nContent");
        Assert.That(html, Does.Not.Contain("fonts.googleapis.com"));
    }

    // ── P04 Step 6: Feature tests — rendering attributes II ───────────

    [Test]
    public void Sectanchors_adds_anchor_before_heading()
    {
        var html = RenderHtml(":sectanchors:\n\n== My Section\n\nContent");
        Assert.That(html, Does.Contain("<a class=\"anchor\" href=\"#_my_section\"></a>"));
    }

    [Test]
    public void No_sectanchors_no_anchor()
    {
        var html = RenderHtml("== My Section\n\nContent");
        Assert.That(html, Does.Not.Contain("class=\"anchor\""));
    }

    [Test]
    public void Sectlinks_wraps_heading_in_link()
    {
        var html = RenderHtml(":sectlinks:\n\n== My Section\n\nContent");
        Assert.That(html, Does.Contain("<a class=\"link\" href=\"#_my_section\">My Section</a>"));
    }

    [Test]
    public void Sectanchors_and_sectlinks_both_present()
    {
        var html = RenderHtml(":sectanchors:\n:sectlinks:\n\n== My Section\n\nContent");
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<a class=\"anchor\""));
            Assert.That(html, Does.Contain("<a class=\"link\""));
        });
    }

    [Test]
    public void Hide_uri_scheme_strips_https()
    {
        var html = RenderHtml(":hide-uri-scheme:\n\nhttps://example.com");
        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("href=\"https://example.com\""));
            Assert.That(html, Does.Contain(">example.com</a>"));
        });
    }

    [Test]
    public void No_hide_uri_scheme_shows_full_url()
    {
        var html = RenderHtml("https://example.com");
        Assert.That(html, Does.Contain(">https://example.com</a>"));
    }

    [Test]
    public void Webfonts_injects_google_fonts_link()
    {
        var html = RenderHtmlFull(":webfonts:\n\n= Title\n\nContent");
        Assert.That(html, Does.Contain("fonts.googleapis.com"));
        Assert.That(html, Does.Contain("<link rel=\"stylesheet\""));
    }

    [Test]
    public void Webfonts_custom_url()
    {
        var html = RenderHtmlFull(":webfonts: https://fonts.example.com/myfont.css\n\n= Title\n\nContent");
        Assert.That(html, Does.Contain("fonts.example.com/myfont.css"));
    }

    [Test]
    public void Last_update_label_customizes_footer()
    {
        var html = RenderHtmlFull(":last-update-label: Dernière mise à jour\n\n= Title\n\nContent");
        Assert.That(html, Does.Contain("Dernière mise à jour"));
        Assert.That(html, Does.Not.Contain("Last updated"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string RenderHtml(string input)
    {
        var renderer = new HtmlRenderer();
        var doc = BlockParser.Parse(input);
        using var ms = new MemoryStream();
        renderer.Render(doc.Document, ms, HtmlRenderOptions.Default);
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string RenderHtmlFull(string input)
    {
        var renderer = new HtmlRenderer();
        var doc = BlockParser.Parse(input);
        using var ms = new MemoryStream();
        renderer.Render(doc.Document, ms, new HtmlRenderOptions { FullDocument = true });
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
