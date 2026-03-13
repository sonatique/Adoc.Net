using AdocNet;
using AdocNet.Ast;

namespace AdocNet.Tests;

[TestFixture]
public class AstPrettyPrinterTests
{
    [Test]
    public void Empty_document_prints_single_line()
    {
        var doc = new DocumentNode();
        var result = AstPrettyPrinter.Print(doc);
        Assert.That(result, Is.EqualTo("Document\n"));
    }

    [Test]
    public void Document_with_source_range_includes_range()
    {
        var doc = new DocumentNode
        {
            Source = new SourceRange(new(1, 1), new(5, 1))
        };
        var result = AstPrettyPrinter.Print(doc);
        Assert.That(result, Is.EqualTo("Document [1:1-5:1]\n"));
    }

    [Test]
    public void Paragraph_prints_text_property()
    {
        var para = new ParagraphNode { Text = "Hello world" };
        var result = AstPrettyPrinter.Print(para);
        Assert.That(result, Is.EqualTo("Paragraph Text=\"Hello world\"\n"));
    }

    [Test]
    public void Section_prints_level_and_title()
    {
        var section = new SectionNode { Level = 1, Title = "Intro" };
        var result = AstPrettyPrinter.Print(section);
        Assert.That(result, Is.EqualTo("Section Level=\"1\" Title=\"Intro\"\n"));
    }

    [Test]
    public void Nested_tree_is_indented()
    {
        var doc = new DocumentNode();
        var section = new SectionNode { Level = 1, Title = "Ch1" };
        var para = new ParagraphNode { Text = "Hello" };
        section.AddChild(para);
        doc.AddChild(section);

        var result = AstPrettyPrinter.Print(doc);

        var expected =
            "Document\n" +
            "  Section Level=\"1\" Title=\"Ch1\"\n" +
            "    Paragraph Text=\"Hello\"\n";

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Multiple_children_at_same_level()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "First" });
        doc.AddChild(new ParagraphNode { Text = "Second" });

        var result = AstPrettyPrinter.Print(doc);

        var expected =
            "Document\n" +
            "  Paragraph Text=\"First\"\n" +
            "  Paragraph Text=\"Second\"\n";

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void Text_with_quotes_is_escaped()
    {
        var para = new ParagraphNode { Text = "He said \"hi\"" };
        var result = AstPrettyPrinter.Print(para);
        Assert.That(result, Is.EqualTo("Paragraph Text=\"He said \\\"hi\\\"\"\n"));
    }

    [Test]
    public void Print_throws_on_null()
    {
        Assert.Throws<ArgumentNullException>(() => AstPrettyPrinter.Print(null!));
    }
}
