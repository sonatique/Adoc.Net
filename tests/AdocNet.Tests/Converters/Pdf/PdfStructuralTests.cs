using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests.Converters.Pdf;

/// <summary>
/// Structural PDF tests: verify that AdocNet PDF output contains the expected
/// text content in the correct order. Uses <see cref="PdfTextExtractor"/> to
/// extract text from generated PDFs and compare against expected content.
/// </summary>
[TestFixture]
public class PdfStructuralTests
{
    private static byte[] RenderToPdf(string adoc, PdfRenderOptions? options = null)
    {
        var parseResult = AdocParser.Parse(adoc);
        var renderer = new PdfRenderer();
        return renderer.RenderToBytes(parseResult.Document, options);
    }

    // ── Content ordering ────────────────────────────────────────────────

    [Test]
    public void Sections_appear_in_document_order()
    {
        var adoc = """
            = My Document

            == First Section

            First content.

            == Second Section

            Second content.

            == Third Section

            Third content.
            """;

        var pdf = RenderToPdf(adoc);
        var text = PdfTextExtractor.NormalizeText(PdfTextExtractor.ExtractText(pdf));

        // Verify ordering: title before first, first before second, second before third
        int titlePos = text.IndexOf("My Document");
        int firstPos = text.IndexOf("First Section");
        int secondPos = text.IndexOf("Second Section");
        int thirdPos = text.IndexOf("Third Section");

        Assert.That(titlePos, Is.GreaterThanOrEqualTo(0), "Title should be in PDF");
        Assert.That(firstPos, Is.GreaterThan(titlePos), "First section after title");
        Assert.That(secondPos, Is.GreaterThan(firstPos), "Second section after first");
        Assert.That(thirdPos, Is.GreaterThan(secondPos), "Third section after second");
    }

    [Test]
    public void Paragraphs_render_complete_text()
    {
        var adoc = """
            = Doc

            This is a paragraph with multiple words that should all appear in the output.
            """;

        var pdf = RenderToPdf(adoc);
        var text = PdfTextExtractor.NormalizeText(PdfTextExtractor.ExtractText(pdf));

        Assert.That(text, Does.Contain("paragraph with multiple words"));
    }

    [Test]
    public void Lists_render_all_items()
    {
        var adoc = """
            = Doc

            * Apple
            * Banana
            * Cherry
            """;

        var pdf = RenderToPdf(adoc);
        var text = PdfTextExtractor.NormalizeText(PdfTextExtractor.ExtractText(pdf));

        Assert.That(text, Does.Contain("Apple"));
        Assert.That(text, Does.Contain("Banana"));
        Assert.That(text, Does.Contain("Cherry"));
    }

    [Test]
    public void Ordered_list_renders_numbers()
    {
        var adoc = """
            = Doc

            . First
            . Second
            . Third
            """;

        var pdf = RenderToPdf(adoc);
        var text = PdfTextExtractor.NormalizeText(PdfTextExtractor.ExtractText(pdf));

        Assert.That(text, Does.Contain("1."));
        Assert.That(text, Does.Contain("2."));
        Assert.That(text, Does.Contain("3."));
    }

    [Test]
    public void Code_blocks_preserve_content()
    {
        var adoc = """
            = Doc

            [source,java]
            ----
            public class Main {
                public static void main(String[] args) {
                    System.out.println("Hello");
                }
            }
            ----
            """;

        var pdf = RenderToPdf(adoc);
        var text = PdfTextExtractor.NormalizeText(PdfTextExtractor.ExtractText(pdf));

        // Code blocks use embedded TrueType font — only non-indented lines extract as ASCII.
        // Indented lines are hex-encoded and appear as [embedded] markers.
        Assert.That(text, Does.Contain("public class Main"));
    }

    [Test]
    public void Tables_render_all_cells()
    {
        var adoc = """
            = Doc

            |===
            |Header 1 |Header 2

            |Cell A |Cell B
            |Cell C |Cell D
            |===
            """;

        var pdf = RenderToPdf(adoc);
        var text = PdfTextExtractor.NormalizeText(PdfTextExtractor.ExtractText(pdf));

        Assert.That(text, Does.Contain("Header 1"));
        Assert.That(text, Does.Contain("Header 2"));
        Assert.That(text, Does.Contain("Cell A"));
        Assert.That(text, Does.Contain("Cell D"));
    }

    [Test]
    public void Admonitions_render_type_and_content()
    {
        var adoc = """
            = Doc

            WARNING: Do not run with scissors.
            """;

        var pdf = RenderToPdf(adoc);
        var text = PdfTextExtractor.NormalizeText(PdfTextExtractor.ExtractText(pdf));

        Assert.That(text, Does.Contain("WARNING"));
        Assert.That(text, Does.Contain("scissors"));
    }

    // ── Outline structure ───────────────────────────────────────────────

    [Test]
    public void Outline_contains_all_section_titles()
    {
        var adoc = """
            = My Document
            :sectnums:

            == Introduction

            Intro text.

            == Methods

            Methods text.

            === Data Collection

            Data text.

            == Results

            Results text.
            """;

        var pdf = RenderToPdf(adoc);
        var pdfText = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.That(pdfText, Does.Contain("/Type /Outlines"));
        // With :sectnums: enabled, outline titles include the section number prefix
        // (matches Asciidoctor PDF behavior — bookmark text mirrors what's rendered).
        Assert.That(pdfText, Does.Contain("/Title (1. Introduction)"));
        Assert.That(pdfText, Does.Contain("/Title (2. Methods)"));
        Assert.That(pdfText, Does.Contain("/Title (2.1. Data Collection)"));
        Assert.That(pdfText, Does.Contain("/Title (3. Results)"));
    }

    [Test]
    public void Section_numbering_appears_in_text()
    {
        var adoc = """
            = Doc
            :sectnums:

            == First

            Text.

            == Second

            Text.
            """;

        var pdf = RenderToPdf(adoc);
        var text = PdfTextExtractor.NormalizeText(PdfTextExtractor.ExtractText(pdf));

        Assert.That(text, Does.Contain("1."));
        Assert.That(text, Does.Contain("2."));
    }

    // ── End-to-end with real parser ─────────────────────────────────────

    [Test]
    public void Full_document_with_mixed_content()
    {
        var adoc = """
            = Complete Document
            Author Name
            :sectnums:

            == Introduction

            This document demonstrates all major features.

            == Lists

            * Unordered item one
            * Unordered item two

            . Ordered item one
            . Ordered item two

            == Code

            [source,csharp]
            ----
            Console.WriteLine("Hello, World!");
            ----

            == Table

            |===
            |Name |Value

            |Alpha |100
            |Beta |200
            |===

            == Conclusion

            The end.
            """;

        var pdf = RenderToPdf(adoc);
        var text = PdfTextExtractor.NormalizeText(PdfTextExtractor.ExtractText(pdf));
        var pdfRaw = System.Text.Encoding.ASCII.GetString(pdf);

        // Content present
        Assert.That(text, Does.Contain("Complete Document"));
        Assert.That(text, Does.Contain("Introduction"));
        Assert.That(text, Does.Contain("Unordered item one"));
        Assert.That(text, Does.Contain("Alpha"));
        Assert.That(text, Does.Contain("Conclusion"));

        // Structural features
        Assert.That(pdfRaw, Does.Contain("/Type /Outlines"), "Should have bookmark outline");
        Assert.That(pdfRaw, Does.Contain("/PageMode /UseOutlines"), "Should open with outlines");
    }
}
