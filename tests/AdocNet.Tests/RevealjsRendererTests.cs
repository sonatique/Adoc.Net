using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.Revealjs;

namespace AdocNet.Tests;

[TestFixture]
public class RevealjsRendererTests
{
    private static string Render(DocumentNode doc)
    {
        var renderer = new RevealjsRenderer();
        using var ms = new MemoryStream();
        renderer.Render(doc, ms, RenderOptions.Default);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ── Format ──────────────────────────────────────────────────────────

    [Test]
    public void Format_is_revealjs()
    {
        var renderer = new RevealjsRenderer();
        Assert.That(renderer.Format, Is.EqualTo("revealjs"));
    }

    // ── Document structure ──────────────────────────────────────────────

    [Test]
    public void Output_contains_reveal_js_structure()
    {
        var doc = new DocumentNode { Title = "My Presentation" };
        var output = Render(doc);

        Assert.That(output, Does.Contain("<!DOCTYPE html>"));
        Assert.That(output, Does.Contain("<div class=\"reveal\">"));
        Assert.That(output, Does.Contain("<div class=\"slides\">"));
        Assert.That(output, Does.Contain("reveal.js"));
        Assert.That(output, Does.Contain("Reveal.initialize("));
    }

    [Test]
    public void Title_generates_title_slide()
    {
        var doc = new DocumentNode { Title = "Hello World" };
        var output = Render(doc);

        Assert.That(output, Does.Contain("<section class=\"title\" data-state=\"title\">\n<h1>Hello World</h1>\n</section>"));
    }

    [Test]
    public void Output_includes_reveal_js_scripts()
    {
        var doc = new DocumentNode { Title = "Test" };
        var output = Render(doc);

        Assert.That(output, Does.Contain("reveal.js\"></script>"));
        Assert.That(output, Does.Contain("Reveal.initialize("));
    }

    // ── Section-to-slide mapping ────────────────────────────────────────

    [Test]
    public void Level1_section_renders_as_horizontal_slide()
    {
        var doc = new DocumentNode { Title = "Pres" };
        var section = new SectionNode { Level = 1, Title = "Introduction" };
        section.AddChild(new ParagraphNode { Text = "Hello" });
        doc.AddChild(section);

        var output = Render(doc);

        Assert.That(output, Does.Contain("<h2>Introduction</h2>"));
        // Slide body content is wrapped in <div class="slide-content"> (asciidoctor parity).
        Assert.That(output, Does.Contain("<div class=\"slide-content\">"));
        Assert.That(output, Does.Contain("<div class=\"paragraph\">\n<p>Hello</p>"));
    }

    [Test]
    public void Level2_sections_render_as_vertical_slides()
    {
        var doc = new DocumentNode { Title = "Pres" };
        var section = new SectionNode { Level = 1, Title = "Chapter" };
        var sub1 = new SectionNode { Level = 2, Title = "Part A" };
        sub1.AddChild(new ParagraphNode { Text = "Content A" });
        section.AddChild(sub1);
        var sub2 = new SectionNode { Level = 2, Title = "Part B" };
        sub2.AddChild(new ParagraphNode { Text = "Content B" });
        section.AddChild(sub2);
        doc.AddChild(section);

        var output = Render(doc);

        // Dedicated title slide first (asciidoctor-revealjs layout), then the
        // outer section wrapping the vertical group with the section heading.
        Assert.That(output, Does.Contain("<section class=\"title\" data-state=\"title\">\n<h1>Pres</h1>"));
        Assert.That(output, Does.Contain("<section>\n<section>\n<h2>Chapter</h2>"));
        // Vertical slides — Asciidoctor uses <h2> for vertical slides too.
        Assert.That(output, Does.Contain("<h2>Part A</h2>"));
        Assert.That(output, Does.Contain("<div class=\"paragraph\">\n<p>Content A</p>"));
        Assert.That(output, Does.Contain("<h2>Part B</h2>"));
        Assert.That(output, Does.Contain("<div class=\"paragraph\">\n<p>Content B</p>"));
    }

    [Test]
    public void Multiple_level1_sections_create_multiple_slides()
    {
        var doc = new DocumentNode { Title = "Pres" };
        var s1 = new SectionNode { Level = 1, Title = "Slide One" };
        s1.AddChild(new ParagraphNode { Text = "First" });
        doc.AddChild(s1);
        var s2 = new SectionNode { Level = 1, Title = "Slide Two" };
        s2.AddChild(new ParagraphNode { Text = "Second" });
        doc.AddChild(s2);

        var output = Render(doc);

        Assert.That(output, Does.Contain("<h2>Slide One</h2>"));
        Assert.That(output, Does.Contain("<h2>Slide Two</h2>"));
    }

    // ── Theme and attributes ────────────────────────────────────────────

    [Test]
    public void Default_theme_is_black()
    {
        var doc = new DocumentNode { Title = "Test" };
        var output = Render(doc);

        Assert.That(output, Does.Contain("/theme/black.css"));
    }

    [Test]
    public void Custom_theme_attribute_changes_CSS_link()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.SetAttribute("revealjs_theme", "moon");

        var output = Render(doc);

        Assert.That(output, Does.Contain("/theme/moon.css"));
        Assert.That(output, Does.Not.Contain("/theme/black.css"));
    }

    [Test]
    public void Transition_attribute_appears_in_initialize()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.SetAttribute("revealjs_transition", "fade");

        var output = Render(doc);

        Assert.That(output, Does.Contain("transition: 'fade'"));
    }

    [Test]
    public void Default_transition_is_slide()
    {
        var doc = new DocumentNode { Title = "Test" };
        var output = Render(doc);

        Assert.That(output, Does.Contain("transition: 'slide'"));
    }

    [Test]
    public void Controls_attribute_appears_in_initialize()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.SetAttribute("revealjs_controls", "false");

        var output = Render(doc);

        Assert.That(output, Does.Contain("controls: false"));
    }

    [Test]
    public void SlideNumber_attribute_appears_in_initialize()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.SetAttribute("revealjs_slideNumber", "true");

        var output = Render(doc);

        Assert.That(output, Does.Contain("slideNumber: true"));
    }

    // ── Content rendering ───────────────────────────────────────────────

    [Test]
    public void Paragraph_renders_as_p_in_slide()
    {
        var doc = new DocumentNode { Title = "Test" };
        var section = new SectionNode { Level = 1, Title = "Slide" };
        section.AddChild(new ParagraphNode { Text = "Some text here" });
        doc.AddChild(section);

        var output = Render(doc);

        Assert.That(output, Does.Contain("<p>Some text here</p>"));
    }

    [Test]
    public void Bold_inline_renders_as_strong()
    {
        var doc = new DocumentNode { Title = "Test" };
        var section = new SectionNode { Level = 1, Title = "Slide" };
        section.AddChild(new ParagraphNode
        {
            Text = "test",
            Inlines = new InlineNode[]
            {
                new StrongInlineNode
                {
                    Children = new[] { new TextInlineNode { Value = "bold" } }
                }
            }
        });
        doc.AddChild(section);

        var output = Render(doc);

        Assert.That(output, Does.Contain("<strong>bold</strong>"));
    }

    [Test]
    public void Source_block_renders_as_pre_code()
    {
        var doc = new DocumentNode { Title = "Test" };
        var section = new SectionNode { Level = 1, Title = "Code" };
        section.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Content = "console.log('hi');",
            Language = "javascript"
        });
        doc.AddChild(section);

        var output = Render(doc);

        // Asciidoctor parity: source blocks wrap in <div class="listingblock"> and
        // <pre class="highlight"><code class="language-X" data-lang="X">.
        Assert.That(output, Does.Contain("<div class=\"listingblock\">"));
        Assert.That(output, Does.Contain("class=\"language-javascript\" data-lang=\"javascript\""));
        Assert.That(output, Does.Contain("console.log(&#39;hi&#39;);"));
    }

    [Test]
    public void List_renders_in_slide()
    {
        var doc = new DocumentNode { Title = "Test" };
        var section = new SectionNode { Level = 1, Title = "Items" };
        var list = new ListNode { ListKind = ListKind.Unordered };
        list.AddChild(new ListItemNode { Text = "Item one" });
        list.AddChild(new ListItemNode { Text = "Item two" });
        section.AddChild(list);
        doc.AddChild(section);

        var output = Render(doc);

        // List items wrap text in <p> for Asciidoctor parity.
        Assert.That(output, Does.Contain("<ul>\n<li>\n<p>Item one</p>\n</li>\n<li>\n<p>Item two</p>\n</li>\n</ul>"));
    }

    // ── Speaker notes ───────────────────────────────────────────────────

    [Test]
    public void Notes_role_renders_as_aside()
    {
        var doc = new DocumentNode { Title = "Test" };
        var section = new SectionNode { Level = 1, Title = "Slide" };
        section.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Open,
            Content = "Speaker notes here"
        });

        // Set the notes role — need to check how roles are set
        var notesBlock = new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Sidebar,
            Content = "These are my notes"
        };
        notesBlock.Roles = new[] { "notes" };
        section.AddChild(notesBlock);
        doc.AddChild(section);

        var output = Render(doc);

        Assert.That(output, Does.Contain("<aside class=\"notes\">"));
    }

    // ── Special characters ──────────────────────────────────────────────

    [Test]
    public void Special_characters_are_escaped()
    {
        var doc = new DocumentNode { Title = "A & B" };
        var output = Render(doc);

        Assert.That(output, Does.Contain("<title>A &amp; B</title>"));
        Assert.That(output, Does.Contain("<h1>A &amp; B</h1>"));
    }

    // ── Integration ─────────────────────────────────────────────────────

    [Test]
    public void Full_presentation_round_trip()
    {
        var doc = new DocumentNode { Title = "My Talk" };
        doc.SetAttribute("revealjs_theme", "white");
        doc.SetAttribute("revealjs_transition", "none");

        var intro = new SectionNode { Level = 1, Title = "Introduction" };
        intro.AddChild(new ParagraphNode { Text = "Welcome!" });
        doc.AddChild(intro);

        var details = new SectionNode { Level = 1, Title = "Details" };
        var d1 = new SectionNode { Level = 2, Title = "Part 1" };
        d1.AddChild(new ParagraphNode { Text = "Detail one" });
        details.AddChild(d1);
        doc.AddChild(details);

        var output = Render(doc);

        // Structure checks
        Assert.That(output, Does.Contain("/theme/white.css"));
        Assert.That(output, Does.Contain("transition: 'none'"));
        Assert.That(output, Does.Contain("<h1>My Talk</h1>"));
        Assert.That(output, Does.Contain("<h2>Introduction</h2>"));
        // Vertical slides use <h2> (Asciidoctor parity).
        Assert.That(output, Does.Contain("<h2>Part 1</h2>"));
        Assert.That(output, Does.Contain("Detail one"));
    }

    // ── Asciidoctor structural parity ───────────────────────────────────

    [Test]
    public void Section_id_emitted_on_horizontal_slide()
    {
        var doc = new DocumentNode { Title = "Pres" };
        doc.AddChild(new SectionNode { Level = 1, Title = "Intro", Id = "_intro" });

        var output = Render(doc);

        Assert.That(output, Does.Contain("<section id=\"_intro\">"));
    }

    [Test]
    public void Section_id_emitted_on_vertical_slide()
    {
        var doc = new DocumentNode { Title = "Pres" };
        var parent = new SectionNode { Level = 1, Title = "Group", Id = "_group" };
        parent.AddChild(new SectionNode { Level = 2, Title = "Sub", Id = "_sub" });
        doc.AddChild(parent);

        var output = Render(doc);

        Assert.That(output, Does.Contain("<section id=\"_group\">"));
        Assert.That(output, Does.Contain("<section id=\"_sub\">"));
    }

    [Test]
    public void Title_slide_has_title_class_and_data_state()
    {
        var doc = new DocumentNode { Title = "Talk" };
        var output = Render(doc);

        Assert.That(output, Does.Contain("<section class=\"title\" data-state=\"title\">"));
    }

    [Test]
    public void Slide_content_wrapper_emitted_around_body_blocks()
    {
        var doc = new DocumentNode { Title = "Pres" };
        var s = new SectionNode { Level = 1, Title = "Intro" };
        s.AddChild(new ParagraphNode { Text = "Body" });
        doc.AddChild(s);

        var output = Render(doc);

        Assert.That(output, Does.Contain("<div class=\"slide-content\">"));
    }

    [Test]
    public void Slide_content_wrapper_omitted_when_slide_has_no_body()
    {
        // Heading-only slide: no body content -> no slide-content wrapper.
        var doc = new DocumentNode { Title = "Pres" };
        doc.AddChild(new SectionNode { Level = 1, Title = "Empty" });

        var output = Render(doc);

        // The title slide and the section both render. Title slide is heading-only.
        // The "Empty" section also has no body content. Verify no slide-content
        // wrapper exists anywhere in the output.
        Assert.That(output, Does.Not.Contain("slide-content"));
    }

    [Test]
    public void Paragraph_wrapped_in_paragraph_div()
    {
        var doc = new DocumentNode { Title = "P" };
        var s = new SectionNode { Level = 1, Title = "S" };
        s.AddChild(new ParagraphNode { Text = "Hi" });
        doc.AddChild(s);

        var output = Render(doc);

        Assert.That(output, Does.Contain("<div class=\"paragraph\">\n<p>Hi</p>\n</div>"));
    }

    [Test]
    public void Unordered_list_wrapped_in_ulist_div()
    {
        var doc = new DocumentNode { Title = "P" };
        var s = new SectionNode { Level = 1, Title = "S" };
        var list = new ListNode { ListKind = ListKind.Unordered };
        list.AddChild(new ListItemNode { Text = "one" });
        s.AddChild(list);
        doc.AddChild(s);

        var output = Render(doc);

        Assert.That(output, Does.Contain("<div class=\"ulist\">\n<ul>"));
    }

    [Test]
    public void Ordered_list_wrapped_in_olist_div()
    {
        var doc = new DocumentNode { Title = "P" };
        var s = new SectionNode { Level = 1, Title = "S" };
        var list = new ListNode { ListKind = ListKind.Ordered };
        list.AddChild(new ListItemNode { Text = "one" });
        s.AddChild(list);
        doc.AddChild(s);

        var output = Render(doc);

        Assert.That(output, Does.Contain("<div class=\"olist\">\n<ol>"));
    }
}
