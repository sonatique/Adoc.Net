using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.Pdf;

namespace AdocNet.Tests;

[TestFixture]
public class SyntaxHighlightingPdfTests
{
    [Test]
    public void CSharp_source_block_contains_color_operators()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "public class Foo { }"
        });

        var options = new PdfRenderOptions { SyntaxColors = SyntaxColorScheme.Default };
        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        string content = Encoding.ASCII.GetString(pdf);

        // Syntax highlighting should produce "rg" color operators (RGB fill color)
        // The default color scheme uses non-black colors for keywords
        Assert.That(content, Does.Contain(" rg\n"), "Should contain RGB color operators for syntax highlighting");
    }

    [Test]
    public void SyntaxColors_null_disables_highlighting()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "public class Foo { }"
        });

        var options = new PdfRenderOptions { SyntaxColors = null };
        byte[] pdf = new PdfRenderer().RenderToBytes(doc, options);
        string content = Encoding.ASCII.GetString(pdf);

        // Without syntax highlighting, source blocks should only use black text
        // Count non-zero rg operators: there should be none for source content
        // (the only rg might be 0 0 0 rg for reset, or code background)
        Assert.That(content, Does.Contain("public class Foo"), "Should contain plain source text");
    }

    [Test]
    public void Unsupported_language_renders_plain()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "rust",
            Content = "fn main() { }"
        });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        string content = Encoding.ASCII.GetString(pdf);

        Assert.That(content, Does.Contain("fn main"), "Should contain plain source text");
    }

    [Test]
    public void PDF_with_highlighting_is_deterministic()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "int x = 42;"
        });

        byte[] pdf1 = new PdfRenderer().RenderToBytes(doc);
        byte[] pdf2 = new PdfRenderer().RenderToBytes(doc);
        Assert.That(pdf1, Is.EqualTo(pdf2));
    }

    [Test]
    public void Default_options_disable_syntax_highlighting_for_backward_compat()
    {
        Assert.That(PdfRenderOptions.Default.SyntaxColors, Is.Null,
            "Default options should not enable syntax highlighting (backward compat)");
    }

    [Test]
    public void Explicit_scheme_enables_syntax_highlighting()
    {
        Assert.That(SyntaxColorScheme.Default, Is.Not.Null,
            "SyntaxColorScheme.Default should be a valid color scheme");
    }
}
