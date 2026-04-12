using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.Html;

namespace AdocNet.Tests;

[TestFixture]
public class NodeTemplateTests
{
    private static string Render(DocumentNode doc, HtmlRenderOptions? options = null)
    {
        var renderer = new HtmlRenderer();
        using var ms = new MemoryStream();
        renderer.Render(doc, ms, options ?? HtmlRenderOptions.Default);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// A test template that matches paragraph nodes and renders custom output.
    /// </summary>
    private sealed class ParagraphTemplate : INodeTemplate
    {
        public bool CanRender(AstNode node) => node is ParagraphNode;

        public string Render(AstNode node, RenderContext context)
        {
            var para = (ParagraphNode)node;
            return $"<div class=\"custom-para\">{para.Text}</div>\n";
        }
    }

    /// <summary>
    /// A test template that matches admonition nodes and renders custom output.
    /// </summary>
    private sealed class AdmonitionTemplate : INodeTemplate
    {
        public bool CanRender(AstNode node) => node is AdmonitionNode;

        public string Render(AstNode node, RenderContext context)
        {
            var admon = (AdmonitionNode)node;
            return $"<div class=\"custom-admonition custom-{admon.AdmonitionType.ToLowerInvariant()}\">{admon.Text}</div>\n";
        }
    }

    /// <summary>
    /// A template that matches section nodes at level 1 only.
    /// </summary>
    private sealed class Level1SectionTemplate : INodeTemplate
    {
        public bool CanRender(AstNode node) => node is SectionNode s && s.Level == 1;

        public string Render(AstNode node, RenderContext context)
        {
            var section = (SectionNode)node;
            return $"<h2 class=\"custom\">{section.Title}</h2>\n";
        }
    }

    /// <summary>
    /// A template that matches strong inline nodes and renders custom output.
    /// </summary>
    private sealed class StrongTemplate : INodeTemplate
    {
        public bool CanRender(AstNode node) => node is StrongInlineNode;

        public string Render(AstNode node, RenderContext context)
        {
            var strong = (StrongInlineNode)node;
            return $"<b class=\"custom-bold\">{strong.Content}</b>";
        }
    }

    [Test]
    public void Template_MatchingNode_UsesTemplateOutput()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "Hello" });

        var options = new HtmlRenderOptions
        {
            Templates = new INodeTemplate[] { new ParagraphTemplate() }
        };
        var html = Render(doc, options);

        Assert.That(html, Is.EqualTo("<div class=\"custom-para\">Hello</div>\n"));
    }

    [Test]
    public void Template_CustomAdmonitionRendering()
    {
        var doc = new DocumentNode();
        doc.AddChild(new AdmonitionNode { AdmonitionType = "NOTE", Text = "Important info" });

        var options = new HtmlRenderOptions
        {
            Templates = new INodeTemplate[] { new AdmonitionTemplate() }
        };
        var html = Render(doc, options);

        Assert.That(html, Is.EqualTo("<div class=\"custom-admonition custom-note\">Important info</div>\n"));
    }

    [Test]
    public void Template_NonMatchingNodes_UseDefaultRendering()
    {
        var doc = new DocumentNode();
        doc.AddChild(new SectionNode { Level = 1, Title = "Heading" });
        doc.AddChild(new ParagraphNode { Text = "Normal text" });

        // Template only matches paragraphs, sections should use default
        var options = new HtmlRenderOptions
        {
            Templates = new INodeTemplate[] { new ParagraphTemplate() }
        };
        var html = Render(doc, options);

        Assert.That(html, Does.Contain("<h2>Heading</h2>"));
        Assert.That(html, Does.Contain("<div class=\"custom-para\">Normal text</div>"));
        Assert.That(html, Does.Not.Contain("<p>Normal text</p>"));
    }

    [Test]
    public void Template_MultipleTemplates_FirstMatchWins()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "Test" });

        var firstTemplate = new ParagraphTemplate();
        // Second template also matches paragraphs but should never be called
        var secondTemplate = new AdmonitionTemplate(); // doesn't match paragraph anyway

        var options = new HtmlRenderOptions
        {
            Templates = new INodeTemplate[] { firstTemplate, secondTemplate }
        };
        var html = Render(doc, options);

        Assert.That(html, Is.EqualTo("<div class=\"custom-para\">Test</div>\n"));
    }

    [Test]
    public void Template_NullTemplates_DefaultRenderingUnchanged()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "Plain" });

        var options = new HtmlRenderOptions { Templates = null };
        var html = Render(doc, options);

        Assert.That(html, Is.EqualTo("<p>Plain</p>\n"));
    }

    [Test]
    public void Template_InlineTemplate_InterceptsInlineRendering()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "bold text",
            Inlines = new InlineNode[]
            {
                new TextInlineNode { Value = "before " },
                new StrongInlineNode
                {
                    Children = new InlineNode[] { new TextInlineNode { Value = "bold text" } }
                },
                new TextInlineNode { Value = " after" }
            }
        });

        var options = new HtmlRenderOptions
        {
            Templates = new INodeTemplate[] { new StrongTemplate() }
        };
        var html = Render(doc, options);

        Assert.That(html, Is.EqualTo("<p>before <b class=\"custom-bold\">bold text</b> after</p>\n"));
    }
}
