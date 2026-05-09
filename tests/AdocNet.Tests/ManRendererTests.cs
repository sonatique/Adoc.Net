using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.Man;

namespace AdocNet.Tests;

[TestFixture]
public class ManRendererTests
{
    private static string Render(DocumentNode doc)
    {
        var renderer = new ManRenderer();
        using var ms = new MemoryStream();
        renderer.Render(doc, ms, RenderOptions.Default);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ── .TH header tests ────────────────────────────────────────────────

    [Test]
    public void Document_with_title_renders_TH_header()
    {
        var doc = new DocumentNode { Title = "mycommand" };

        var output = Render(doc);

        Assert.That(output, Does.Contain(".TH \"MYCOMMAND\" \"1\""));
    }

    [Test]
    public void Document_with_manpage_title_format_extracts_section()
    {
        var doc = new DocumentNode { Title = "git-status(1)" };

        var output = Render(doc);

        // Hyphens in the .TH name field are escaped as \- (Asciidoctor convention).
        Assert.That(output, Does.Contain(".TH \"GIT\\-STATUS\" \"1\""));
    }

    [Test]
    public void Document_attributes_appear_in_TH()
    {
        var doc = new DocumentNode { Title = "myapp(3)" };
        doc.SetAttribute("mansource", "MyApp 2.0");
        doc.SetAttribute("manmanual", "MyApp Manual");
        doc.SetAttribute("revdate", "2026-04-01");

        var output = Render(doc);

        Assert.That(output, Does.Contain("\"2026-04-01\""));
        Assert.That(output, Does.Contain("\"MyApp 2.0\""));
        Assert.That(output, Does.Contain("\"MyApp Manual\""));
    }

    [Test]
    public void Document_without_title_uses_UNTITLED()
    {
        var doc = new DocumentNode();

        var output = Render(doc);

        Assert.That(output, Does.Contain(".TH \"UNTITLED\""));
    }

    // ── Section tests ───────────────────────────────────────────────────

    [Test]
    public void Section_level1_renders_as_SH()
    {
        var doc = new DocumentNode { Title = "test" };
        doc.AddChild(new SectionNode { Level = 1, Title = "Description" });

        var output = Render(doc);

        Assert.That(output, Does.Contain(".SH \"DESCRIPTION\""));
    }

    [Test]
    public void Section_level2_renders_as_SS()
    {
        var doc = new DocumentNode { Title = "test" };
        doc.AddChild(new SectionNode { Level = 2, Title = "Details" });

        var output = Render(doc);

        Assert.That(output, Does.Contain(".SS \"Details\""));
    }

    [Test]
    public void Section_level3_renders_as_bold_paragraph()
    {
        var doc = new DocumentNode { Title = "test" };
        doc.AddChild(new SectionNode { Level = 3, Title = "SubDetail" });

        var output = Render(doc);

        Assert.That(output, Does.Contain(".PP\n\\fBSubDetail\\fP"));
    }

    // ── Paragraph tests ─────────────────────────────────────────────────

    [Test]
    public void Paragraph_renders_with_sp()
    {
        var doc = new DocumentNode { Title = "test" };
        doc.AddChild(new ParagraphNode { Text = "Hello world" });

        var output = Render(doc);

        // Asciidoctor's manpage converter emits .sp (single vertical space) between
        // paragraphs, not .PP (which would reset indentation).
        Assert.That(output, Does.Contain(".sp\nHello world"));
    }

    // ── Bold/Italic/Mono inline tests ───────────────────────────────────

    [Test]
    public void Bold_text_renders_as_fB()
    {
        var doc = new DocumentNode { Title = "test" };
        doc.AddChild(new ParagraphNode
        {
            Text = "test",
            Inlines = new InlineNode[]
            {
                new StrongInlineNode
                {
                    Children = new[] { new TextInlineNode { Value = "important" } }
                }
            }
        });

        var output = Render(doc);

        Assert.That(output, Does.Contain("\\fBimportant\\fP"));
    }

    [Test]
    public void Italic_text_renders_as_fI()
    {
        var doc = new DocumentNode { Title = "test" };
        doc.AddChild(new ParagraphNode
        {
            Text = "test",
            Inlines = new InlineNode[]
            {
                new EmphasisInlineNode
                {
                    Children = new[] { new TextInlineNode { Value = "emphasis" } }
                }
            }
        });

        var output = Render(doc);

        Assert.That(output, Does.Contain("\\fIemphasis\\fP"));
    }

    [Test]
    public void Monospace_text_renders_as_fB()
    {
        var doc = new DocumentNode { Title = "test" };
        doc.AddChild(new ParagraphNode
        {
            Text = "test",
            Inlines = new InlineNode[]
            {
                new MonospaceInlineNode
                {
                    Children = new[] { new TextInlineNode { Value = "code" } }
                }
            }
        });

        var output = Render(doc);

        Assert.That(output, Does.Contain("\\fBcode\\fP"));
    }

    // ── Code block tests ────────────────────────────────────────────────

    [Test]
    public void Source_block_renders_with_nf_fi()
    {
        var doc = new DocumentNode { Title = "test" };
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Content = "echo hello",
            Language = "bash"
        });

        var output = Render(doc);

        Assert.That(output, Does.Contain(".nf\necho hello\n.fi"));
    }

    [Test]
    public void Literal_block_renders_with_nf_fi()
    {
        var doc = new DocumentNode { Title = "test" };
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Literal,
            Content = "raw text"
        });

        var output = Render(doc);

        Assert.That(output, Does.Contain(".nf\nraw text\n.fi"));
    }

    // ── List tests ──────────────────────────────────────────────────────

    [Test]
    public void Unordered_list_renders_with_IP_bullet()
    {
        var doc = new DocumentNode { Title = "test" };
        var list = new ListNode { ListKind = ListKind.Unordered };
        list.AddChild(new ListItemNode { Text = "first item" });
        list.AddChild(new ListItemNode { Text = "second item" });
        doc.AddChild(list);

        var output = Render(doc);

        Assert.That(output, Does.Contain(".IP \"\\(bu\" 2\nfirst item"));
        Assert.That(output, Does.Contain(".IP \"\\(bu\" 2\nsecond item"));
    }

    [Test]
    public void Ordered_list_renders_with_numbered_IP()
    {
        var doc = new DocumentNode { Title = "test" };
        var list = new ListNode { ListKind = ListKind.Ordered };
        list.AddChild(new ListItemNode { Text = "step one" });
        list.AddChild(new ListItemNode { Text = "step two" });
        doc.AddChild(list);

        var output = Render(doc);

        Assert.That(output, Does.Contain(".IP \"1.\" 3\nstep one"));
        Assert.That(output, Does.Contain(".IP \"2.\" 3\nstep two"));
    }

    [Test]
    public void Description_list_renders_with_TP()
    {
        var doc = new DocumentNode { Title = "test" };
        var dlist = new DescriptionListNode();
        dlist.AddChild(new DescriptionItemNode
        {
            Terms = ["flag"],
            Description = "enables feature",
            TermInlines = [],
            DescriptionInlines = []
        });
        doc.AddChild(dlist);

        var output = Render(doc);

        Assert.That(output, Does.Contain(".TP\n\\fBflag\\fP\nenables feature"));
    }

    // ── Admonition test ─────────────────────────────────────────────────

    [Test]
    public void Admonition_renders_with_bold_type()
    {
        var doc = new DocumentNode { Title = "test" };
        doc.AddChild(new AdmonitionNode
        {
            AdmonitionType = "NOTE",
            Text = "Be careful here"
        });

        var output = Render(doc);

        Assert.That(output, Does.Contain(".PP\n\\fBNOTE:\\fP Be careful here"));
    }

    // ── Escaping tests ──────────────────────────────────────────────────

    [Test]
    public void Leading_dot_is_escaped()
    {
        var escaped = ManRenderer.EscapeBodyText(".TH ATTACK");
        Assert.That(escaped, Is.EqualTo("\\&.TH ATTACK"));
    }

    [Test]
    public void Backslash_is_escaped_in_body()
    {
        var escaped = ManRenderer.EscapeBodyText("path\\to\\file");
        Assert.That(escaped, Is.EqualTo("path\\\\to\\\\file"));
    }

    [Test]
    public void Quote_is_escaped_in_roff()
    {
        var escaped = ManRenderer.EscapeRoff("say \"hello\"");
        Assert.That(escaped, Is.EqualTo("say \\(dqhello\\(dq"));
    }

    // ── Format property ─────────────────────────────────────────────────

    [Test]
    public void Format_is_man()
    {
        var renderer = new ManRenderer();
        Assert.That(renderer.Format, Is.EqualTo("man"));
    }

    // ── Integration: full document ──────────────────────────────────────

    [Test]
    public void Full_document_round_trip()
    {
        var doc = new DocumentNode { Title = "myapp(1)" };
        doc.SetAttribute("mansource", "MyApp 1.0");
        doc.SetAttribute("manmanual", "User Commands");

        var nameSection = new SectionNode { Level = 1, Title = "Name" };
        nameSection.AddChild(new ParagraphNode { Text = "myapp - does things" });
        doc.AddChild(nameSection);

        var descSection = new SectionNode { Level = 1, Title = "Description" };
        descSection.AddChild(new ParagraphNode
        {
            Text = "desc",
            Inlines = new InlineNode[]
            {
                new TextInlineNode { Value = "This is " },
                new StrongInlineNode
                {
                    Children = new[] { new TextInlineNode { Value = "myapp" } }
                },
                new TextInlineNode { Value = "." }
            }
        });
        doc.AddChild(descSection);

        var output = Render(doc);

        Assert.That(output, Does.Contain(".TH \"MYAPP\" \"1\""));
        Assert.That(output, Does.Contain(".SH \"NAME\""));
        Assert.That(output, Does.Contain("myapp - does things"));
        Assert.That(output, Does.Contain(".SH \"DESCRIPTION\""));
        Assert.That(output, Does.Contain("\\fBmyapp\\fP"));
    }

    [Test]
    public void Standard_preamble_emitted_after_TH()
    {
        // Asciidoctor emits a standard preamble (apostrophe glyph, sentence
        // spacing, no hyphenation, left-align, URL/MTO macros) after .TH.
        var doc = new DocumentNode { Title = "myapp(1)" };
        var output = Render(doc);

        Assert.That(output, Does.Contain(".ie \\n(.g .ds Aq \\(aq"));
        Assert.That(output, Does.Contain(".el       .ds Aq '"));
        Assert.That(output, Does.Contain(".ss \\n[.ss] 0"));
        Assert.That(output, Does.Contain(".nh"));
        Assert.That(output, Does.Contain(".ad l"));
        Assert.That(output, Does.Contain(".de URL"));
        Assert.That(output, Does.Contain(".als MTO URL"));
    }
}
