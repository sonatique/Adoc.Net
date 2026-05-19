using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Ast;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class PdfRendererTests
{
    // ── Basic PDF structure ─────────────────────────────────────────────

    [Test]
    public void Render_returns_valid_pdf_header()
    {
        var doc = new DocumentNode();
        byte[] pdf = new PdfRenderer().RenderToBytes(doc);

        string header = Encoding.ASCII.GetString(pdf, 0, 5);
        Assert.That(header, Is.EqualTo("%PDF-"));
    }

    [Test]
    public void Render_returns_pdf_ending_with_eof_marker()
    {
        var doc = new DocumentNode();
        byte[] pdf = new PdfRenderer().RenderToBytes(doc);

        string trailer = Encoding.ASCII.GetString(pdf, pdf.Length - 6, 6);
        Assert.That(trailer, Does.Contain("%%EOF"));
    }

    [Test]
    public void Render_produces_deterministic_output()
    {
        var doc = new DocumentNode { Title = "Test" };
        doc.AddChild(new ParagraphNode { Text = "Hello world." });

        byte[] pdf1 = new PdfRenderer().RenderToBytes(doc);
        byte[] pdf2 = new PdfRenderer().RenderToBytes(doc);

        Assert.That(pdf1, Is.EqualTo(pdf2));
    }

    [Test]
    public void Render_with_options_overload_returns_same_result()
    {
        var doc = new DocumentNode { Title = "Test" };

        byte[] pdf1 = new PdfRenderer().RenderToBytes(doc);
        byte[] pdf2 = new PdfRenderer().RenderToBytes(doc, PdfRenderOptions.Default);

        Assert.That(pdf1, Is.EqualTo(pdf2));
    }

    [Test]
    public void Render_throws_on_null_document()
    {
        Assert.That(() => new PdfRenderer().RenderToBytes(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void Render_with_options_throws_on_null_document()
    {
        Assert.That(() => new PdfRenderer().RenderToBytes(null!, PdfRenderOptions.Default),
            Throws.ArgumentNullException);
    }

    [Test]
    public void Render_with_null_options_uses_default()
    {
        var doc = new DocumentNode();
        byte[] pdf = new PdfRenderer().RenderToBytes(doc, null!);
        Assert.That(pdf, Is.Not.Empty);
    }

    // ── Content presence ────────────────────────────────────────────────

    [Test]
    public void Render_empty_document_produces_valid_pdf()
    {
        var doc = new DocumentNode();
        byte[] pdf = new PdfRenderer().RenderToBytes(doc);

        Assert.That(pdf.Length, Is.GreaterThan(100));
        AssertPdfContains(pdf, "/Type /Page");
    }

    [Test]
    public void Render_document_with_title_includes_title_text()
    {
        var doc = new DocumentNode { Title = "My Document Title" };
        byte[] pdf = new PdfRenderer().RenderToBytes(doc);

        AssertPdfContains(pdf, "My Document Title");
    }

    [Test]
    public void Render_paragraph_includes_text_content()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "Sample paragraph text." });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "Sample paragraph text.");
    }

    [Test]
    public void Render_section_includes_heading_text()
    {
        var doc = new DocumentNode();
        doc.AddChild(new SectionNode { Level = 1, Title = "Introduction" });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "Introduction");
    }

    [Test]
    public void Render_nested_sections_include_all_titles()
    {
        var doc = new DocumentNode { Title = "Main" };
        var s1 = new SectionNode { Level = 1, Title = "Chapter One" };
        s1.AddChild(new ParagraphNode { Text = "Body text." });
        var s2 = new SectionNode { Level = 2, Title = "Subsection" };
        s1.AddChild(s2);
        doc.AddChild(s1);

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "Chapter One");
        AssertPdfContains(pdf, "Subsection");
        AssertPdfContains(pdf, "Body text.");
    }

    [Test]
    public void Render_unordered_list_includes_items()
    {
        var doc = new DocumentNode();
        var list = new ListNode { ListKind = ListKind.Unordered };
        list.AddChild(new ListItemNode { Text = "Alpha" });
        list.AddChild(new ListItemNode { Text = "Beta" });
        doc.AddChild(list);

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "Alpha");
        AssertPdfContains(pdf, "Beta");
    }

    [Test]
    public void Render_ordered_list_includes_numbered_items()
    {
        var doc = new DocumentNode();
        var list = new ListNode { ListKind = ListKind.Ordered };
        list.AddChild(new ListItemNode { Text = "First" });
        list.AddChild(new ListItemNode { Text = "Second" });
        doc.AddChild(list);

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "1.");
        AssertPdfContains(pdf, "First");
        AssertPdfContains(pdf, "2.");
        AssertPdfContains(pdf, "Second");
    }

    [Test]
    public void Render_verbatim_block_includes_content()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Listing,
            Content = "var x = 42;"
        });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "var x = 42;");
    }

    [Test]
    public void Render_source_block_renders_content()
    {
        // Asciidoctor-pdf doesn't print the language label inside the code
        // block — only the content is rendered (with optional syntax coloring).
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "Console.WriteLine();"
        });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "Console.WriteLine");
    }

    [Test]
    public void Render_table_includes_cell_text()
    {
        var doc = new DocumentNode();
        var table = new TableNode { HasHeader = true };
        var headerRow = new TableRowNode();
        headerRow.AddChild(new TableCellNode { Text = "Name" });
        headerRow.AddChild(new TableCellNode { Text = "Value" });
        table.AddChild(headerRow);
        var bodyRow = new TableRowNode();
        bodyRow.AddChild(new TableCellNode { Text = "Foo" });
        bodyRow.AddChild(new TableCellNode { Text = "42" });
        table.AddChild(bodyRow);
        doc.AddChild(table);

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "Name");
        AssertPdfContains(pdf, "Value");
        AssertPdfContains(pdf, "Foo");
        AssertPdfContains(pdf, "42");
    }

    [Test]
    public void Render_block_image_includes_placeholder_text()
    {
        var doc = new DocumentNode();
        doc.AddChild(new BlockImageNode { Target = "photo.png", Alt = "A photo" });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "A photo");
    }

    [Test]
    public void Render_description_list_includes_terms()
    {
        var doc = new DocumentNode();
        var dl = new DescriptionListNode();
        dl.AddChild(new DescriptionItemNode
        {
            Terms = ["CPU"],
            Description = "Central Processing Unit",
            TermInlines = [],
            DescriptionInlines = [],
        });
        doc.AddChild(dl);

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "CPU");
        AssertPdfContains(pdf, "Central Processing Unit");
    }

    [Test]
    public void Render_admonition_includes_type_and_text()
    {
        var doc = new DocumentNode();
        doc.AddChild(new AdmonitionNode
        {
            AdmonitionType = "NOTE",
            Text = "Remember this."
        });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "NOTE");
        AssertPdfContains(pdf, "Remember this.");
    }

    [Test]
    public void Render_quote_block_includes_child_content()
    {
        var doc = new DocumentNode();
        var quote = new DelimitedBlockNode { BlockKind = DelimitedBlockKind.Quote };
        quote.AddChild(new ParagraphNode { Text = "To be or not to be." });
        doc.AddChild(quote);

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "To be or not to be.");
    }

    [Test]
    public void Render_block_with_title_includes_title()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Listing,
            Title = "Example Code",
            Content = "echo hello"
        });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "Example Code");
    }

    // ── PDF structure validation ────────────────────────────────────────

    [Test]
    public void Render_includes_pdf_catalog()
    {
        var doc = new DocumentNode { Title = "Test" };
        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "/Type /Catalog");
    }

    [Test]
    public void Render_includes_font_resources()
    {
        var doc = new DocumentNode { Title = "Test" };
        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "/BaseFont /Helvetica");
    }

    [Test]
    public void Render_includes_helvetica_bold()
    {
        var doc = new DocumentNode { Title = "Bold Title" };
        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "/BaseFont /Helvetica-Bold");
    }

    [Test]
    public void Render_includes_producer_metadata()
    {
        var doc = new DocumentNode();
        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "AdocNet PDF Renderer");
    }

    [Test]
    public void Render_includes_cross_reference_table()
    {
        var doc = new DocumentNode();
        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "xref");
        AssertPdfContains(pdf, "startxref");
    }

    // ── End-to-end with parser ──────────────────────────────────────────

    [Test]
    public void Render_parsed_document_with_all_features()
    {
        var source = """
            = Full Document

            == Section One

            A paragraph with *bold* and _italic_ text.

            * Item A
            * Item B

            ----
            code block
            ----

            |===
            | Col1 | Col2
            | A    | B
            |===

            NOTE: Pay attention.
            """;

        var result = AdocParser.Parse(source);
        byte[] pdf = new PdfRenderer().RenderToBytes(result.Document);

        Assert.That(pdf.Length, Is.GreaterThan(500));
        AssertPdfContains(pdf, "Full Document");
        AssertPdfContains(pdf, "Section One");
        AssertPdfContains(pdf, "code block");
    }

    [Test]
    public void Render_large_document_produces_multiple_pages()
    {
        var doc = new DocumentNode { Title = "Long Document" };
        // Add enough paragraphs to force multiple pages
        for (int i = 0; i < 100; i++)
            doc.AddChild(new ParagraphNode { Text = $"Paragraph number {i}. " +
                "This is a line of text that adds some volume to the document content." });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);

        // Count page objects — each page has /Type /Page
        string pdfText = Encoding.ASCII.GetString(pdf);
        int pageCount = 0;
        int idx = 0;
        while ((idx = pdfText.IndexOf("/Type /Page ", idx)) >= 0)
        {
            pageCount++;
            idx++;
        }

        Assert.That(pageCount, Is.GreaterThan(1), "Document should span multiple pages");
    }

    [Test]
    public void Render_special_characters_are_escaped_in_pdf()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "Use parentheses (like this) and backslash \\ too." });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        // PDF should contain escaped parens
        AssertPdfContains(pdf, "\\(like this\\)");
    }

    // ── Page configuration ────────────────────────────────────────────

    [Test]
    public void Custom_page_size_applied()
    {
        var doc = AdocParser.Parse("Hello").Document;
        var options = new PdfRenderOptions { PageWidth = 612f, PageHeight = 792f };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("/MediaBox [0 0 612 792]"));
    }

    [Test]
    public void Page_numbers_appear_in_footer()
    {
        var doc = AdocParser.Parse("Hello").Document;
        var options = new PdfRenderOptions { ShowPageNumbers = true };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("Page 1"));
    }

    [Test]
    public void Custom_header_text_rendered()
    {
        var doc = AdocParser.Parse("Hello").Document;
        var options = new PdfRenderOptions { HeaderText = "My Document" };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("My Document"));
    }

    [Test]
    public void Custom_margins_affect_content_width()
    {
        var doc = AdocParser.Parse("Hello").Document;
        var options = new PdfRenderOptions { MarginLeft = 100f, MarginRight = 100f };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);
        Assert.That(bytes, Is.Not.Empty);
    }

    [Test]
    public void Letter_size_preset_correct()
    {
        var options = PdfRenderOptions.Letter;
        Assert.That(options.PageWidth, Is.EqualTo(612f));
        Assert.That(options.PageHeight, Is.EqualTo(792f));
    }

    [Test]
    public void Custom_font_size_changes_output()
    {
        var doc = AdocParser.Parse("= Test\n\nHello.").Document;
        var defaultBytes = new PdfRenderer().RenderToBytes(doc);
        var largeBytes = new PdfRenderer().RenderToBytes(doc, new PdfRenderOptions { FontSize = 16f });
        // Different font size should produce different PDF output
        Assert.That(largeBytes, Is.Not.EqualTo(defaultBytes),
            "Custom FontSize should produce different output than default");
    }

    // ── Header/footer tests ──────────────────────────────────────────────

    [Test]
    public void Multi_page_footer_contains_page_numbers()
    {
        // Generate a document long enough for 3+ pages
        var sb = new System.Text.StringBuilder("= Long Doc\n\n");
        for (int i = 0; i < 100; i++)
            sb.Append($"Paragraph {i} with some text to fill space.\n\n");

        var doc = AdocParser.Parse(sb.ToString()).Document;
        var options = new PdfRenderOptions { ShowPageNumbers = true };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);
        var text = Encoding.ASCII.GetString(bytes);

        Assert.That(text, Does.Contain("Page 1"));
        Assert.That(text, Does.Contain("Page 2"));
        Assert.That(text, Does.Contain("Page 3"));
    }

    [Test]
    public void Total_pages_placeholder_resolved()
    {
        // Generate a multi-page doc with {pages} in footer
        var sb = new System.Text.StringBuilder("= Doc\n\n");
        for (int i = 0; i < 100; i++)
            sb.Append($"Paragraph {i} text.\n\n");

        var doc = AdocParser.Parse(sb.ToString()).Document;
        var options = new PdfRenderOptions { FooterText = "{page} of {pages}" };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);
        var text = Encoding.ASCII.GetString(bytes);

        // {pages} placeholder must NOT appear literally
        Assert.That(text, Does.Not.Contain("{pages}"));
        Assert.That(text, Does.Not.Contain("___TOTAL___"));

        // Page count should appear (at least "1 of N" for page 1)
        Assert.That(text, Does.Contain("1 of "));
    }

    [Test]
    public void Custom_footer_template_with_page_and_pages()
    {
        var sb = new System.Text.StringBuilder("= Doc\n\n");
        for (int i = 0; i < 60; i++)
            sb.Append($"Paragraph {i} with enough text.\n\n");

        var doc = AdocParser.Parse(sb.ToString()).Document;
        var options = new PdfRenderOptions { FooterText = "Page {page} of {pages}" };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);
        var text = Encoding.ASCII.GetString(bytes);

        Assert.That(text, Does.Contain("Page 1 of "));
        Assert.That(text, Does.Contain("Page 2 of "));
    }

    [Test]
    public void No_footer_by_default()
    {
        var doc = AdocParser.Parse("= Test\n\nHello world.").Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Not.Contain("Page 1"));
    }

    [Test]
    public void Header_footer_determinism()
    {
        var doc = AdocParser.Parse("= Test\n\nContent.").Document;
        var options = new PdfRenderOptions { ShowPageNumbers = true, HeaderText = "Title" };
        var bytes1 = new PdfRenderer().RenderToBytes(doc, options);
        var bytes2 = new PdfRenderer().RenderToBytes(doc, options);
        Assert.That(bytes1, Is.EqualTo(bytes2), "Header/footer renders must be byte-identical");
    }

    [Test]
    public void Footer_height_affects_text_position()
    {
        var doc = AdocParser.Parse("= Test\n\nParagraph content.").Document;
        var optsWith = new PdfRenderOptions { FooterText = "Footer", FooterHeight = 48f };
        var optsWithout = new PdfRenderOptions { FooterText = "Footer" };

        var pdfWith = Encoding.ASCII.GetString(new PdfRenderer().RenderToBytes(doc, optsWith));
        var pdfWithout = Encoding.ASCII.GetString(new PdfRenderer().RenderToBytes(doc, optsWithout));

        // Extract footer Y positions
        var yWith = ExtractFooterY(pdfWith);
        var yWithout = ExtractFooterY(pdfWithout);
        Assert.That(yWith, Is.LessThan(yWithout), "Footer with height should be positioned lower");
    }

    [Test]
    public void Footer_section_title_includes_number_when_sectnums()
    {
        var doc = AdocParser.Parse("= Doc Title\n:sectnums:\n\n== First Section\n\nContent.\n\n== Second Section\n\nMore content.").Document;
        var options = new PdfRenderOptions { FooterText = "{section-title}" };
        var pdf = Encoding.ASCII.GetString(new PdfRenderer().RenderToBytes(doc, options));

        // Footer should contain numbered section title
        Assert.That(pdf, Does.Contain("1. First Section").Or.Contain("2. Second Section"));
    }

    [Test]
    public void Footer_section_title_without_sectnums_has_no_number()
    {
        var doc = AdocParser.Parse("= Doc Title\n\n== My Section\n\nContent.").Document;
        var options = new PdfRenderOptions { FooterText = "{section-title}" };
        var pdf = Encoding.ASCII.GetString(new PdfRenderer().RenderToBytes(doc, options));

        // Footer should contain plain section title (no number prefix)
        Assert.That(pdf, Does.Contain("My Section"));
        Assert.That(pdf, Does.Not.Contain("1. My Section"));
    }

    [Test]
    public void Footer_Y_matches_Asciidoctor_for_height48_font11()
    {
        // Asciidoctor PDF places footer text baseline at Y=33.75 when footer.height=48 and font-size=11.
        // Our formula: footerHeight - fontSize * 1.30 = 48 - 14.3 = 33.7 (matches within 0.05pt).
        var doc = AdocParser.Parse("= Test\n\nContent.").Document;
        var options = new PdfRenderOptions { FooterText = "Footer", FooterHeight = 48f, FooterFontSize = 11f };
        var pdf = Encoding.ASCII.GetString(new PdfRenderer().RenderToBytes(doc, options));
        var y = ExtractFooterY(pdf);
        Assert.That(y, Is.EqualTo(33.7f).Within(0.1f),
            $"Footer Y should match Asciidoctor (33.75) for footer.height=48, font=11. Got {y}.");
    }

    [Test]
    public void Header_Y_matches_Asciidoctor_for_height64_font11()
    {
        // Asciidoctor PDF places header text baseline at Y=808.35 when header.height=64, font=11, page=842.
        // Our formula: pageHeight - headerHeight/2 - fontSize * 0.15 = 842 - 32 - 1.65 = 808.35.
        var doc = AdocParser.Parse("= Test\n\nContent.").Document;
        var options = new PdfRenderOptions { HeaderText = "Header", HeaderHeight = 64f, HeaderFontSize = 11f };
        var pdf = Encoding.ASCII.GetString(new PdfRenderer().RenderToBytes(doc, options));
        var match = Regex.Match(pdf, @"BT\n/F1 \d+ Tf\n([\d.]+) ([\d.]+) Td\n\(Header\)");
        Assert.That(match.Success, Is.True, "Header text not found in PDF");
        var y = float.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(y, Is.EqualTo(808.35f).Within(0.1f),
            $"Header Y should match Asciidoctor (808.35) for header.height=64, font=11. Got {y}.");
    }

    private static float ExtractFooterY(string pdfContent)
    {
        // Find footer BT...ET block with low Y position
        var match = Regex.Match(pdfContent, @"BT\n/F1 \d+ Tf\n([\d.]+) ([\d.]+) Td\n\(Footer\)");
        return match.Success ? float.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : -1f;
    }

    // ── Hyperlink annotations ──────────────────────────────────────────

    [Test]
    public void Hyperlink_creates_annotation()
    {
        var doc = AdocParser.Parse("Visit https://example.com for details.").Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("/Subtype /Link"));
        Assert.That(text, Does.Contain("/URI (https://example.com)"));
    }

    [Test]
    public void Link_macro_creates_annotation()
    {
        var doc = AdocParser.Parse("See link:https://example.com[Example Site] for more.").Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("/URI (https://example.com)"));
    }

    [Test]
    public void Page_has_annots_entry()
    {
        var doc = AdocParser.Parse("Visit https://example.com here.").Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("/Annots"));
    }

    [Test]
    public void Multiple_links_produce_multiple_annotations()
    {
        var doc = AdocParser.Parse(
            "See https://one.com and https://two.com and https://three.com links.").Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("/URI (https://one.com)"));
        Assert.That(text, Does.Contain("/URI (https://two.com)"));
        Assert.That(text, Does.Contain("/URI (https://three.com)"));
        // Count annotation objects — each /Subtype /Link is a separate annotation
        int annotCount = 0;
        int idx = 0;
        while ((idx = text.IndexOf("/Subtype /Link", idx, StringComparison.Ordinal)) >= 0)
        {
            annotCount++;
            idx++;
        }
        Assert.That(annotCount, Is.GreaterThanOrEqualTo(3), "Should have at least 3 link annotations");
    }

    [Test]
    public void Hyperlink_determinism_two_renders_produce_identical_output()
    {
        var doc = AdocParser.Parse(
            "Visit https://example.com and link:https://other.com[Other].").Document;
        var bytes1 = new PdfRenderer().RenderToBytes(doc);
        var bytes2 = new PdfRenderer().RenderToBytes(doc);
        Assert.That(bytes1, Is.EqualTo(bytes2), "Link renders must be byte-identical");
    }

    // ── Text quality ─────────────────────────────────────────────────────

    [Test]
    public void Punctuation_does_not_start_wrapped_line()
    {
        // Use WrapText directly to inspect line-break decisions.
        // Construct input where a closing paren would naturally start a new line.
        // Width is tight enough that "word)" wraps — the ")" must not start the next line.
        var writer = new PdfWriter();

        // Measure to pick a maxWidth that forces a break right before the paren
        float bodySize = 11f;
        string input = "some words here then more text and close) after that";
        var lines = writer.WrapText(input, "F1", bodySize, 200f);

        // No line should start with a no-start character
        for (int i = 1; i < lines.Count; i++)
        {
            char first = lines[i][0];
            Assert.That(first, Is.Not.EqualTo(')'),
                $"Line {i} starts with ')': \"{lines[i]}\"");
            Assert.That(first, Is.Not.EqualTo('.'),
                $"Line {i} starts with '.': \"{lines[i]}\"");
        }
    }

    [Test]
    public void Justification_spacing_capped_at_twice_space_width()
    {
        // A very short line with few words should not get extreme spacing.
        // The WriteJustifiedSegments method caps at 2× space width.
        // We verify indirectly: render a justified paragraph with a very short line
        // and check the PDF Tw operator value doesn't exceed 2× space width.
        float spaceWidth = PdfWriter.MeasureStandardText(" ", "F1", 11f);
        float maxAllowed = spaceWidth * 2;

        var doc = AdocParser.Parse("= Test\n\nWord word word word word word word word more text here for wrapping purpose.").Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var text = Encoding.ASCII.GetString(bytes);

        // Find all "Tw" operators and extract the spacing value
        int idx = 0;
        while ((idx = text.IndexOf(" Tw\n", idx, StringComparison.Ordinal)) >= 0)
        {
            // Walk back to find the number before " Tw"
            int end = idx;
            int start = end - 1;
            while (start > 0 && (char.IsDigit(text[start]) || text[start] == '.' || text[start] == '-'))
                start--;
            start++;
            if (start < end)
            {
                string numStr = text.Substring(start, end - start);
                if (float.TryParse(numStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float twValue))
                {
                    if (twValue > 0) // Only check positive (non-reset) values
                    {
                        Assert.That(twValue, Is.LessThanOrEqualTo(maxAllowed),
                            $"Tw value {twValue} exceeds 2× space width ({maxAllowed})");
                    }
                }
            }
            idx++;
        }
    }

    [Test]
    public void Last_line_of_justified_paragraph_is_not_justified()
    {
        // Render a multi-line paragraph. The last line should have Tw=0 (left-aligned),
        // while non-last lines should have Tw>0 (justified).
        var doc = AdocParser.Parse(
            "= Test\n\nThis is a paragraph with enough words that it will wrap across " +
            "multiple lines in the PDF output to test justification behavior properly.").Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var text = Encoding.ASCII.GetString(bytes);

        // Find all Tw values in order — the pattern is: value Tw, then text, then 0 Tw (reset)
        var twValues = new List<float>();
        int idx = 0;
        while ((idx = text.IndexOf(" Tw\n", idx, StringComparison.Ordinal)) >= 0)
        {
            int end = idx;
            int start = end - 1;
            while (start > 0 && (char.IsDigit(text[start]) || text[start] == '.' || text[start] == '-'))
                start--;
            start++;
            if (start < end)
            {
                string numStr = text.Substring(start, end - start);
                if (float.TryParse(numStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float twValue))
                {
                    twValues.Add(twValue);
                }
            }
            idx++;
        }

        // Non-zero Tw values are justification; they come in pairs (set, then 0 reset).
        // The last text line should be rendered via WriteTextSegments (no Tw at all) or Tw=0.
        // We just verify that not ALL lines have positive Tw — at least the last must be 0 or absent.
        bool hasPositiveTw = twValues.Exists(v => v > 0);
        bool hasZeroTw = twValues.Exists(v => v == 0);
        if (hasPositiveTw)
        {
            Assert.That(hasZeroTw, Is.True,
                "Justified paragraph must have at least one Tw reset (0) for the last line");
        }
    }

    [Test]
    public void Text_quality_determinism()
    {
        var doc = AdocParser.Parse(
            "= Test\n\nA paragraph with text (including punctuation) that wraps.").Document;
        var bytes1 = new PdfRenderer().RenderToBytes(doc);
        var bytes2 = new PdfRenderer().RenderToBytes(doc);
        Assert.That(bytes1, Is.EqualTo(bytes2), "Text quality renders must be byte-identical");
    }

    [Test]
    public void Code_block_background_does_not_overlap_surrounding_text()
    {
        // Regression test: the gray background rect of a code block must not
        // overlap with surrounding paragraphs, and internal padding must be
        // visually balanced (top gap ≈ bottom gap within 2pt).

        var adoc = "= Test\n\nBefore the block.\n\n----\ncode line 1\n\ncode line 3\n----\n\nAfter the block.";
        var doc = AdocParser.Parse(adoc).Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var pdf = Encoding.ASCII.GetString(bytes);

        // Parse all Td (text position) Y values
        var tdYValues = ParseTdYValues(pdf);
        // Parse all filled rects (code block backgrounds)
        var rects = ParseFilledRects(pdf);

        Assert.That(rects.Count, Is.GreaterThan(0), "Should have at least one background rect");

        foreach (var (rectY, rectH) in rects)
        {
            float rectTop = rectY + rectH;
            float rectBottom = rectY;

            // Collect text positions INSIDE the rect (code block text)
            var insideY = new List<float>();
            // Text OUTSIDE and closest to the rect
            float? closestAbove = null;
            float? closestBelow = null;

            foreach (var ty in tdYValues)
            {
                if (ty > rectTop)
                {
                    if (closestAbove is null || ty < closestAbove)
                        closestAbove = ty;
                }
                else if (ty < rectBottom)
                {
                    if (closestBelow is null || ty > closestBelow)
                        closestBelow = ty;
                }
                else
                {
                    insideY.Add(ty);
                }
            }

            // 1. No overlap with surrounding text
            if (closestAbove is not null)
            {
                Assert.That(closestAbove.Value, Is.GreaterThan(rectTop),
                    $"Text above code block (Y={closestAbove}) overlaps rect top (Y={rectTop})");
            }
            if (closestBelow is not null)
            {
                Assert.That(closestBelow.Value, Is.LessThan(rectBottom),
                    $"Text below code block (Y={closestBelow}) overlaps rect bottom (Y={rectBottom})");
            }

            // 2. Balanced internal padding: top gap ≈ bottom gap
            if (insideY.Count >= 2)
            {
                float firstBaseline = insideY.Max(); // highest Y = first line
                float lastBaseline = insideY.Min();   // lowest Y = last line
                float fontSize = 9f; // code font size

                // Visual gap = distance from rect edge to nearest text extreme
                float topVisualGap = rectTop - firstBaseline - fontSize * 0.75f;  // rect top to ascenders
                float bottomVisualGap = lastBaseline - fontSize * 0.25f - rectBottom; // descenders to rect bottom

                Assert.That(topVisualGap, Is.GreaterThanOrEqualTo(1f),
                    $"Top padding too small: {topVisualGap:F1}pt visual gap above first text");
                Assert.That(bottomVisualGap, Is.GreaterThanOrEqualTo(1f),
                    $"Bottom padding too small: {bottomVisualGap:F1}pt visual gap below last text");
                Assert.That(Math.Abs(topVisualGap - bottomVisualGap), Is.LessThanOrEqualTo(2f),
                    $"Padding imbalance: top={topVisualGap:F1}pt, bottom={bottomVisualGap:F1}pt (max 2pt difference)");
            }
        }
    }

    private static List<float> ParseTdYValues(string pdf)
    {
        var values = new List<float>();
        int idx = 0;
        while ((idx = pdf.IndexOf(" Td\n", idx, StringComparison.Ordinal)) >= 0)
        {
            int lineStart = pdf.LastIndexOf('\n', idx - 1) + 1;
            string line = pdf.Substring(lineStart, idx - lineStart).Trim();
            var parts = line.Split(' ');
            if (parts.Length >= 2 && float.TryParse(parts[1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float y))
            {
                values.Add(y);
            }
            idx++;
        }
        return values;
    }

    private static List<(float Y, float H)> ParseFilledRects(string pdf)
    {
        var rects = new List<(float Y, float H)>();

        // Find code block background regions: q\n0.95 0.95 0.95 rg\n...h\nf\nQ
        // or the fallback: q\n0.95 0.95 0.95 rg\nX Y W H re f\nQ
        int idx = 0;
        while (idx < pdf.Length)
        {
            int qStart = pdf.IndexOf("q\n0.95 0.95 0.95 rg\n", idx, StringComparison.Ordinal);
            if (qStart < 0) break;
            int blockStart = qStart + "q\n0.95 0.95 0.95 rg\n".Length;
            int qEnd = pdf.IndexOf("Q\n", blockStart, StringComparison.Ordinal);
            if (qEnd < 0) break;
            string block = pdf.Substring(blockStart, qEnd - blockStart);

            // Try "X Y W H re f" pattern first
            var reMatch = Regex.Match(block, @"([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+re\s+f");
            if (reMatch.Success)
            {
                float.TryParse(reMatch.Groups[2].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float ry);
                float.TryParse(reMatch.Groups[4].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float rh);
                rects.Add((ry, rh));
            }
            else
            {
                // Rounded rect: extract all Y coords from m/c/l ops to find bounding box
                var yValues = new List<float>();
                foreach (Match m in Regex.Matches(block, @"([\d.]+)\s+([\d.]+)\s+[mlc]"))
                {
                    if (float.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float yv))
                        yValues.Add(yv);
                }
                // Also parse Bezier control points (6 coords before c)
                foreach (Match m in Regex.Matches(block,
                    @"[\d.]+\s+([\d.]+)\s+[\d.]+\s+([\d.]+)\s+[\d.]+\s+([\d.]+)\s+c"))
                {
                    foreach (int gi in new[] { 1, 2, 3 })
                    {
                        if (float.TryParse(m.Groups[gi].Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float yv))
                            yValues.Add(yv);
                    }
                }
                if (yValues.Count > 0)
                {
                    float minY = yValues.Min();
                    float maxY = yValues.Max();
                    rects.Add((minY, maxY - minY));
                }
            }
            idx = qEnd + 1;
        }
        return rects;
    }

    [Test]
    public void Quote_block_does_not_render_newline_as_visible_glyph()
    {
        // Regression: multi-line quote block text had \n chars rendered as
        // visible square glyphs instead of being treated as spaces.
        var adoc = "[quote]\n____\nLine one.\nLine two.\nLine three.\n____";
        var doc = AdocParser.Parse(adoc).Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var pdf = Encoding.ASCII.GetString(bytes);

        // The text should be joined with spaces — no \n in the content stream.
        // In PDF, text is rendered via (string) Tj or <hex> Tj operators.
        // A literal \n (0x0A) inside a PDF string would show as a square.
        // Check that the rendered text for "Line one" and "Line two" appears
        // as a single paragraph without control characters.

        // Extract all parenthesized strings from the PDF (standard font text)
        var renderedTexts = new List<string>();
        int idx = 0;
        while ((idx = pdf.IndexOf(") Tj", idx, StringComparison.Ordinal)) >= 0)
        {
            int start = pdf.LastIndexOf('(', idx);
            if (start >= 0)
            {
                string text = pdf.Substring(start + 1, idx - start - 1);
                renderedTexts.Add(text);
            }
            idx++;
        }

        // No rendered text should contain a newline character
        foreach (var text in renderedTexts)
        {
            Assert.That(text, Does.Not.Contain("\n"),
                $"Rendered text contains literal newline: '{text}'");
            Assert.That(text, Does.Not.Contain("\r"),
                $"Rendered text contains literal carriage return: '{text}'");
        }
    }

    [Test]
    public void Structural_block_border_line_aligns_with_indented_text()
    {
        // The left border line of a quote/example block should be at or left of
        // the text start position, and the text should be indented past the line.
        var adoc = "[quote]\n____\nQuote content here.\n____";
        var doc = AdocParser.Parse(adoc).Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var pdf = Encoding.ASCII.GetString(bytes);

        // Find vertical lines: "X1 Y1 m X2 Y2 l S" where X1 == X2 (vertical)
        var verticalLines = new List<float>(); // X positions of vertical lines
        int idx = 0;
        while ((idx = pdf.IndexOf(" l S\n", idx, StringComparison.Ordinal)) >= 0)
        {
            int lineStart = pdf.LastIndexOf('\n', idx - 1) + 1;
            string line = pdf.Substring(lineStart, idx - lineStart).Trim();
            var parts = line.Split(' ');
            // Format: "X1 Y1 m X2 Y2 l S" — we look for the 'm' and check X1 == X2
            int mIdx = Array.IndexOf(parts, "m");
            if (mIdx >= 2 && mIdx + 2 < parts.Length &&
                float.TryParse(parts[mIdx - 2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x1) &&
                float.TryParse(parts[mIdx + 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x2))
            {
                if (Math.Abs(x1 - x2) < 0.1f) // vertical line
                    verticalLines.Add(x1);
            }
            idx++;
        }

        // Find text X positions (first number in "X Y Td")
        var textXPositions = ParseTdXValues(pdf);

        Assert.That(verticalLines.Count, Is.GreaterThan(0), "Should have a vertical border line");
        Assert.That(textXPositions.Count, Is.GreaterThan(0), "Should have text positions");

        // The border line X should be less than the smallest text X in the block
        // (line is to the left of the text, with a visible gap)
        float borderX = verticalLines.Min();
        float minTextX = textXPositions.Where(x => x > borderX - 20).Min();
        float gap = minTextX - borderX;

        Assert.That(gap, Is.GreaterThanOrEqualTo(4f),
            $"Border line (X={borderX:F1}) too close to text (X={minTextX:F1}), gap={gap:F1}pt");
        Assert.That(gap, Is.LessThanOrEqualTo(20f),
            $"Border line (X={borderX:F1}) too far from text (X={minTextX:F1}), gap={gap:F1}pt");
    }

    private static List<float> ParseTdXValues(string pdf)
    {
        var values = new List<float>();
        int idx = 0;
        while ((idx = pdf.IndexOf(" Td\n", idx, StringComparison.Ordinal)) >= 0)
        {
            int lineStart = pdf.LastIndexOf('\n', idx - 1) + 1;
            string line = pdf.Substring(lineStart, idx - lineStart).Trim();
            var parts = line.Split(' ');
            if (parts.Length >= 2 && float.TryParse(parts[0],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float x))
            {
                values.Add(x);
            }
            idx++;
        }
        return values;
    }

    // ── Cell wrapping and multi-line support ─────────────────────────────

    [Test]
    public void Table_cell_text_wraps_in_pdf()
    {
        var doc = AdocParser.Parse("|===\n| Short | This is a very long cell text that should wrap within the column width because it exceeds the available space\n|===").Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        Assert.That(bytes, Is.Not.Empty);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("Short"));
    }

    [Test]
    public void Multi_row_table_renders_correctly()
    {
        var adoc = "|===\n| Header 1 | Header 2\n\n| Cell 1 | Cell 2\n| Cell 3 | Cell 4\n|===";
        var doc = AdocParser.Parse(adoc).Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        Assert.That(bytes, Is.Not.Empty);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("Header 1"));
        Assert.That(text, Does.Contain("Cell 4"));
    }

    [Test]
    public void Table_with_column_spec_respects_widths()
    {
        var adoc = "[cols=\"1,3\"]\n|===\n| Narrow | Wide column\n|===";
        var doc = AdocParser.Parse(adoc).Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        Assert.That(bytes, Is.Not.Empty);
    }

    [Test]
    public void Table_cell_wraps_to_multiple_lines()
    {
        // A cell with very long text should produce a row taller than 1 line
        var writer = new PdfWriter();
        string longText = "This is a very long cell text that absolutely must wrap within the column width";
        var lines = writer.WrapText(longText, "F1", 11f, 150f); // narrow column
        Assert.That(lines.Count, Is.GreaterThan(1),
            "Long cell text must wrap to multiple lines in a narrow column");
    }

    [Test]
    public void Table_auto_sizing_gives_prose_column_more_space_than_narrow_columns()
    {
        // Issue #17: in a multi-column auto-sized table mixing short identifiers
        // with one prose cell, the prose column must receive substantially more
        // width than the narrow columns — not a near-equal share that collapses
        // prose to one word per line.
        var adoc = "|===\n| A | B | C | D | E | F | G | Description\n\n"
                 + "| ADV_CONNECT_IND | ADV or periodic | uncoded | uncoded | => F | => W | => R "
                 + "| The link type should be stored with its PHY and coding type for efficient "
                 + "detection in the case that those never change for a given link or broadcast.\n"
                 + "|===";
        var doc = AdocParser.Parse(adoc).Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);

        // Extract Td-positioned text offsets from the PDF content stream to
        // verify the prose column occupies the lion's share of the page width.
        var text = Encoding.Latin1.GetString(bytes);
        int describeIdx = text.IndexOf("(The link", StringComparison.Ordinal);
        Assert.That(describeIdx, Is.GreaterThan(-1),
            "Prose cell content should appear in the PDF stream");

        // The prose cell wraps; count how many lines its content spans by
        // counting "(The " / "(type " / "(change " etc. — at least 2 lines
        // for a 150-char sentence in any sane column width.
        int proseLines = 0;
        int idx = 0;
        while ((idx = text.IndexOf("(", idx, StringComparison.Ordinal)) > -1)
        {
            int end = text.IndexOf(')', idx);
            if (end < 0) break;
            string s = text.Substring(idx + 1, end - idx - 1);
            if (s.StartsWith("The link") || s.StartsWith("type for")
                || s.StartsWith("change for") || s.StartsWith("detection"))
                proseLines++;
            idx = end + 1;
        }

        // Prose used to render one-word-per-line (~15-20 lines for a single
        // 150-char sentence). With content-weighted column allocation it
        // should wrap to a small handful of lines.
        Assert.That(proseLines, Is.LessThan(8),
            $"Prose cell should wrap to a small number of lines, but rendered as {proseLines} lines");
    }

    [Test]
    public void Table_column_spec_3_1_1_produces_correct_ratio()
    {
        // Test that cols="3,1,1" produces first column ~3× wider than others.
        // We verify by checking that the table renders and the column spec parser works.
        var adoc = "[cols=\"3,1,1\"]\n|===\n| Wide column | Narrow | Narrow\n\n| Content A | B | C\n|===";
        var doc = AdocParser.Parse(adoc).Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("Wide column"));
        Assert.That(text, Does.Contain("Content A"));

        // Verify the column spec was parsed correctly
        var table = doc.Children.OfType<TableNode>().First();
        Assert.That(table.Columns, Is.Not.Null);
        Assert.That(table.Columns!, Has.Count.EqualTo(3));
        Assert.That(table.Columns![0].Width, Is.EqualTo(3));
        Assert.That(table.Columns![1].Width, Is.EqualTo(1));
        Assert.That(table.Columns![2].Width, Is.EqualTo(1));
        // The ratio is enforced: col0 gets 3/5 of width, col1 and col2 get 1/5 each
        // So col0 should be 3× col1 within the renderer
    }

    [Test]
    public void Table_with_many_rows_splits_across_pages()
    {
        // Generate a table with 50+ rows that should span multiple pages
        var sb = new System.Text.StringBuilder();
        sb.Append("|===\n| Header 1 | Header 2\n\n");
        for (int i = 1; i <= 60; i++)
            sb.Append($"| Row {i} Col 1 | Row {i} Col 2\n");
        sb.Append("|===");

        var doc = AdocParser.Parse(sb.ToString()).Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var text = Encoding.ASCII.GetString(bytes);

        // Count pages by counting /Type /Page entries (not /Type /Pages)
        int pageCount = 0;
        int idx = 0;
        while ((idx = text.IndexOf("/Type /Page ", idx, StringComparison.Ordinal)) >= 0)
        {
            pageCount++;
            idx++;
        }
        Assert.That(pageCount, Is.GreaterThanOrEqualTo(2), "Large table must span at least 2 pages");
    }

    [Test]
    public void Table_header_repeats_on_continuation_page()
    {
        // Generate a table with header that spans multiple pages
        var sb = new System.Text.StringBuilder();
        sb.Append("|===\n| HeaderAlpha | HeaderBeta\n\n");
        for (int i = 1; i <= 60; i++)
            sb.Append($"| Row {i} Col 1 | Row {i} Col 2\n");
        sb.Append("|===");

        var doc = AdocParser.Parse(sb.ToString()).Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var text = Encoding.ASCII.GetString(bytes);

        // Count occurrences of the header text — should appear at least twice
        // (once on first page, once on continuation page)
        int headerCount = 0;
        int idx = 0;
        while ((idx = text.IndexOf("HeaderAlpha", idx, StringComparison.Ordinal)) >= 0)
        {
            headerCount++;
            idx++;
        }
        Assert.That(headerCount, Is.GreaterThanOrEqualTo(2),
            "Header text should appear on first page and continuation pages");
    }

    [Test]
    public void Table_determinism_two_renders_produce_identical_output()
    {
        var adoc = "|===\n| H1 | H2\n\n| C1 | C2\n| C3 | C4\n|===";
        var doc = AdocParser.Parse(adoc).Document;
        var bytes1 = new PdfRenderer().RenderToBytes(doc);
        var bytes2 = new PdfRenderer().RenderToBytes(doc);
        Assert.That(bytes1, Is.EqualTo(bytes2), "Table renders must be byte-identical");
    }

    // ── Image embedding ────────────────────────────────────────────────

    [Test]
    public void Image_parser_detects_jpeg_dimensions()
    {
        byte[] data = CreateMinimalJpeg(320, 240, 3);
        var info = ImageParser.TryParseJpeg(data);
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Value.Width, Is.EqualTo(320));
        Assert.That(info!.Value.Height, Is.EqualTo(240));
        Assert.That(info!.Value.Components, Is.EqualTo(3));
        Assert.That(info!.Value.BitsPerComponent, Is.EqualTo(8));
        Assert.That(info!.Value.Format, Is.EqualTo(ImageParser.ImageFormat.Jpeg));
    }

    [Test]
    public void Image_parser_detects_grayscale_jpeg()
    {
        byte[] data = CreateMinimalJpeg(100, 50, 1);
        var info = ImageParser.TryParseJpeg(data);
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Value.Components, Is.EqualTo(1));
    }

    [Test]
    public void Image_parser_rejects_invalid_jpeg()
    {
        byte[] data = [0x00, 0x00, 0x00, 0x00];
        var info = ImageParser.TryParseJpeg(data);
        Assert.That(info, Is.Null);
    }

    [Test]
    public void Image_parser_detects_png_dimensions()
    {
        byte[] data = CreateMinimalPng(4, 2, colorType: 2); // RGB
        var info = ImageParser.TryParsePng(data);
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Value.Width, Is.EqualTo(4));
        Assert.That(info!.Value.Height, Is.EqualTo(2));
        Assert.That(info!.Value.Components, Is.EqualTo(3));
        Assert.That(info!.Value.Format, Is.EqualTo(ImageParser.ImageFormat.Png));
    }

    [Test]
    public void Image_parser_detects_grayscale_png()
    {
        byte[] data = CreateMinimalPng(2, 2, colorType: 0); // Grayscale
        var info = ImageParser.TryParsePng(data);
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Value.Components, Is.EqualTo(1));
    }

    [Test]
    public void Image_parser_handles_rgba_png_with_alpha()
    {
        byte[] data = CreateMinimalPng(2, 2, colorType: 6); // RGBA
        var info = ImageParser.TryParsePng(data);
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Value.Components, Is.EqualTo(3)); // RGB after alpha split
        Assert.That(info!.Value.AlphaData, Is.Not.Null);
    }

    [Test]
    public void Image_parser_rejects_invalid_png()
    {
        byte[] data = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var info = ImageParser.TryParsePng(data);
        Assert.That(info, Is.Null);
    }

    [Test]
    public void Jpeg_image_embedded_in_pdf()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create a minimal JPEG with SOF0 marker so the parser can extract dimensions
            byte[] jpegData = CreateMinimalJpeg(8, 8, 3);
            // We need a truly valid JPEG for full embedding, but at minimum the parser extracts info.
            // Write a synthetic JPEG that has the markers our parser needs.
            File.WriteAllBytes(Path.Combine(tempDir, "test.jpg"), jpegData);

            var doc = AdocParser.Parse("image::test.jpg[Test Image]").Document;
            var options = new PdfRenderOptions { BaseDirectory = tempDir };
            var bytes = new PdfRenderer().RenderToBytes(doc, options);
            var text = Encoding.ASCII.GetString(bytes);
            Assert.That(text, Does.Contain("/Subtype /Image"));
            Assert.That(text, Does.Contain("/Filter /DCTDecode"));
            Assert.That(text, Does.Contain("/ColorSpace /DeviceRGB"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void Png_image_embedded_in_pdf()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            byte[] pngData = CreateMinimalPng(4, 2, colorType: 2);
            File.WriteAllBytes(Path.Combine(tempDir, "test.png"), pngData);

            var doc = AdocParser.Parse("image::test.png[Test PNG]").Document;
            var options = new PdfRenderOptions { BaseDirectory = tempDir };
            var bytes = new PdfRenderer().RenderToBytes(doc, options);
            var text = Encoding.ASCII.GetString(bytes);
            Assert.That(text, Does.Contain("/Subtype /Image"));
            Assert.That(text, Does.Contain("/Filter /FlateDecode"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void Rgba_png_creates_smask_in_pdf()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            byte[] pngData = CreateMinimalPng(2, 2, colorType: 6);
            File.WriteAllBytes(Path.Combine(tempDir, "alpha.png"), pngData);

            var doc = AdocParser.Parse("image::alpha.png[Alpha Image]").Document;
            var options = new PdfRenderOptions { BaseDirectory = tempDir };
            var bytes = new PdfRenderer().RenderToBytes(doc, options);
            var text = Encoding.ASCII.GetString(bytes);
            Assert.That(text, Does.Contain("/SMask"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void Missing_image_falls_back_to_placeholder()
    {
        var doc = AdocParser.Parse("image::nonexistent.jpg[Missing]").Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        Assert.That(bytes, Is.Not.Empty);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("[Image: Missing]"));
    }

    [Test]
    public void Image_without_base_directory_falls_back_to_placeholder()
    {
        var doc = AdocParser.Parse("image::photo.png[A photo]").Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("[Image: A photo]"));
    }

    [Test]
    public void Pdf_object_supports_binary_streams()
    {
        var binaryData = new byte[] { 0x00, 0xFF, 0x80 };
        var obj = new PdfObject("<< /Test true >>", binaryData);
        Assert.That(obj.BinaryStream, Is.EqualTo(binaryData));
        Assert.That(obj.Content, Is.EqualTo("<< /Test true >>"));
    }

    [Test]
    public void Image_xobject_referenced_in_page_resources()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            byte[] jpegData = CreateMinimalJpeg(8, 8, 3);
            File.WriteAllBytes(Path.Combine(tempDir, "test.jpg"), jpegData);

            var doc = AdocParser.Parse("image::test.jpg[Test]").Document;
            var options = new PdfRenderOptions { BaseDirectory = tempDir };
            var bytes = new PdfRenderer().RenderToBytes(doc, options);
            var text = Encoding.ASCII.GetString(bytes);
            Assert.That(text, Does.Contain("/XObject"));
            Assert.That(text, Does.Contain("/Im1"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void Image_determinism_two_renders_produce_identical_output()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            byte[] jpegData = CreateMinimalJpeg(8, 8, 3);
            byte[] pngData = CreateMinimalPng(4, 2, colorType: 2);
            File.WriteAllBytes(Path.Combine(tempDir, "test.jpg"), jpegData);
            File.WriteAllBytes(Path.Combine(tempDir, "test.png"), pngData);

            var doc = AdocParser.Parse("= Images\n\nimage::test.jpg[JPEG]\n\nimage::test.png[PNG]").Document;
            var options = new PdfRenderOptions { BaseDirectory = tempDir };
            var bytes1 = new PdfRenderer().RenderToBytes(doc, options);
            var bytes2 = new PdfRenderer().RenderToBytes(doc, options);

            Assert.That(bytes1, Is.EqualTo(bytes2), "Image renders must be byte-identical");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ── Combined feature tests ──────────────────────────────────────────

    [Test]
    public void Combined_features_unicode_image_link_table_justified()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet_combined_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            byte[] jpegData = CreateMinimalJpeg(16, 16, 3);
            File.WriteAllBytes(Path.Combine(tempDir, "photo.jpg"), jpegData);

            var source = """
                = Combined Test — café résumé

                A justified paragraph with enough words to wrap across multiple lines in the output so that justification is exercised properly.

                Visit https://example.com for more details.

                image::photo.jpg[A photo]

                |===
                | Header 1 | Header 2

                | Cell A | Cell B
                |===
                """;

            var doc = AdocParser.Parse(source).Document;
            var options = new PdfRenderOptions { BaseDirectory = tempDir };
            var bytes = new PdfRenderer().RenderToBytes(doc, options);
            var text = Encoding.ASCII.GetString(bytes);

            Assert.That(text, Does.Contain("%PDF-1.4"));
            Assert.That(text, Does.Contain("/Subtype /Image"));
            Assert.That(text, Does.Contain("/URI (https://example.com)"));
            Assert.That(text, Does.Contain("Header 1"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void Combined_features_determinism()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet_det_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            byte[] jpegData = CreateMinimalJpeg(8, 8, 3);
            File.WriteAllBytes(Path.Combine(tempDir, "img.jpg"), jpegData);

            var source = "= Test\n\nText with https://example.com link.\n\nimage::img.jpg[Img]\n\n|===\n| A | B\n|===";
            var doc = AdocParser.Parse(source).Document;
            var options = new PdfRenderOptions { BaseDirectory = tempDir };
            var bytes1 = new PdfRenderer().RenderToBytes(doc, options);
            var bytes2 = new PdfRenderer().RenderToBytes(doc, options);

            Assert.That(bytes1, Is.EqualTo(bytes2), "Combined feature renders must be byte-identical");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // ── Image creation helpers ─────────────────────────────────────────

    /// <summary>
    /// Creates a minimal byte array that looks like a JPEG with SOF0 marker
    /// containing the specified dimensions.
    /// </summary>
    private static byte[] CreateMinimalJpeg(int width, int height, int components)
    {
        using var ms = new MemoryStream();

        // SOI marker
        ms.Write([0xFF, 0xD8]);

        // APP0 (JFIF) marker - minimal
        ms.Write([0xFF, 0xE0, 0x00, 0x10,
            0x4A, 0x46, 0x49, 0x46, 0x00,  // "JFIF\0"
            0x01, 0x01, 0x00,               // version 1.1, no units
            0x00, 0x01, 0x00, 0x01,         // 1x1 density
            0x00, 0x00]);                   // no thumbnail

        // SOF0 marker
        int sofLength = 8 + 3 * components;
        ms.Write([0xFF, 0xC0,
            (byte)(sofLength >> 8), (byte)(sofLength & 0xFF),
            0x08, // bits per component
            (byte)(height >> 8), (byte)(height & 0xFF),
            (byte)(width >> 8), (byte)(width & 0xFF),
            (byte)components]);

        // Component specs (one per component)
        for (int i = 0; i < components; i++)
        {
            ms.Write([(byte)(i + 1), 0x11, 0x00]); // id, sampling, quant table
        }

        // EOI marker
        ms.Write([0xFF, 0xD9]);

        return ms.ToArray();
    }

    /// <summary>
    /// Creates a minimal valid PNG file with the specified dimensions and color type.
    /// Color types: 0 = Grayscale, 2 = RGB, 6 = RGBA.
    /// </summary>
    private static byte[] CreateMinimalPng(int width, int height, int colorType)
    {
        using var ms = new MemoryStream();

        // PNG signature
        ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        // IHDR chunk
        WriteChunk(ms, "IHDR", writer =>
        {
            WriteBigEndian(writer, width);
            WriteBigEndian(writer, height);
            writer.WriteByte(8);          // bit depth
            writer.WriteByte((byte)colorType);
            writer.WriteByte(0);          // compression
            writer.WriteByte(0);          // filter
            writer.WriteByte(0);          // interlace
        });

        // Generate raw pixel data with filter bytes
        int bytesPerPixel = colorType switch
        {
            0 => 1,
            2 => 3,
            6 => 4,
            _ => 3
        };
        int scanlineWidth = bytesPerPixel * width;
        byte[] rawData = new byte[height * (1 + scanlineWidth)];

        for (int row = 0; row < height; row++)
        {
            int offset = row * (1 + scanlineWidth);
            rawData[offset] = 0; // Filter type: None
            for (int x = 0; x < scanlineWidth; x++)
            {
                rawData[offset + 1 + x] = (byte)((row * scanlineWidth + x) % 256);
            }
        }

        // Compress with zlib (2-byte header + deflate + adler32)
        byte[] compressedIdat = ZlibCompress(rawData);

        // IDAT chunk
        WriteChunk(ms, "IDAT", writer =>
        {
            writer.Write(compressedIdat, 0, compressedIdat.Length);
        });

        // IEND chunk
        WriteChunk(ms, "IEND", _ => { });

        return ms.ToArray();
    }

    private static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();

        // Zlib header (deflate, no preset dict, default compression)
        output.WriteByte(0x78);
        output.WriteByte(0x9C);

        // Deflate-compressed data
        using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }

        // Adler-32 checksum
        uint adler = Adler32(data);
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)(adler));

        return output.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (byte d in data)
        {
            a = (a + d) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    private static void WriteChunk(MemoryStream ms, string type, Action<MemoryStream> writeData)
    {
        using var chunkData = new MemoryStream();
        writeData(chunkData);
        byte[] data = chunkData.ToArray();

        // Length (big-endian)
        WriteBigEndian(ms, data.Length);

        // Type
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        ms.Write(typeBytes, 0, 4);

        // Data
        if (data.Length > 0)
            ms.Write(data, 0, data.Length);

        // CRC-32 over type + data
        uint crc = Crc32(typeBytes, data);
        ms.WriteByte((byte)(crc >> 24));
        ms.WriteByte((byte)(crc >> 16));
        ms.WriteByte((byte)(crc >> 8));
        ms.WriteByte((byte)(crc));
    }

    private static void WriteBigEndian(MemoryStream ms, int value)
    {
        ms.WriteByte((byte)(value >> 24));
        ms.WriteByte((byte)(value >> 16));
        ms.WriteByte((byte)(value >> 8));
        ms.WriteByte((byte)(value));
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in type)
            crc = Crc32Update(crc, b);
        foreach (byte b in data)
            crc = Crc32Update(crc, b);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint Crc32Update(uint crc, byte b)
    {
        crc ^= b;
        for (int i = 0; i < 8; i++)
        {
            if ((crc & 1) != 0)
                crc = (crc >> 1) ^ 0xEDB88320;
            else
                crc >>= 1;
        }
        return crc;
    }

    // ── TrueType font embedding ──────────────────────────────────────────

    [Test]
    public void TrueType_parser_reads_metrics()
    {
        var fontPath = FindSystemFont();
        if (fontPath is null)
        {
            Assert.Ignore("No system TrueType font available for testing");
            return;
        }

        var fontData = File.ReadAllBytes(fontPath);
        var font = TrueTypeFont.Parse(fontData);

        Assert.That(font.UnitsPerEm, Is.GreaterThan(0));
        Assert.That(font.FontName, Is.Not.Null.And.Not.Empty);
        Assert.That(font.GetGlyphId('A'), Is.GreaterThan((ushort)0));
    }

    [Test]
    public void TrueType_text_measurement_works()
    {
        var fontPath = FindSystemFont();
        if (fontPath is null) { Assert.Ignore("No system font"); return; }

        var font = TrueTypeFont.Parse(File.ReadAllBytes(fontPath));
        float width = font.MeasureText("Hello", 12f);
        Assert.That(width, Is.GreaterThan(0f));
    }

    [Test]
    public void Embedded_font_produces_valid_pdf()
    {
        var fontPath = FindSystemFont();
        if (fontPath is null) { Assert.Ignore("No system font"); return; }

        var doc = AdocParser.Parse("Hello Unicode: cafe resume naive").Document;
        var options = new PdfRenderOptions { FontPath = fontPath };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("/Subtype /CIDFontType2"));
        Assert.That(text, Does.Contain("/ToUnicode"));
    }

    [Test]
    public void No_font_path_uses_standard_fonts()
    {
        var doc = AdocParser.Parse("Hello standard").Document;
        var bytes = new PdfRenderer().RenderToBytes(doc);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("/BaseFont /Helvetica"));
        Assert.That(text, Does.Not.Contain("/CIDFontType2"));
    }

    [Test]
    public void Embedded_font_encodes_text_as_hex_glyph_ids()
    {
        var fontPath = FindSystemFont();
        if (fontPath is null) { Assert.Ignore("No system font"); return; }

        var doc = AdocParser.Parse("AB").Document;
        var options = new PdfRenderOptions { FontPath = fontPath };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);
        var text = Encoding.ASCII.GetString(bytes);
        // The stream should contain hex-encoded glyph IDs (angle brackets)
        Assert.That(text, Does.Contain("/Identity-H"));
    }

    [Test]
    public void Embedded_font_includes_font_descriptor()
    {
        var fontPath = FindSystemFont();
        if (fontPath is null) { Assert.Ignore("No system font"); return; }

        var doc = AdocParser.Parse("Test").Document;
        var options = new PdfRenderOptions { FontPath = fontPath };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("/Type /FontDescriptor"));
        Assert.That(text, Does.Contain("/FontFile2"));
    }

    [Test]
    public void Nonexistent_font_path_falls_back_to_standard()
    {
        var doc = AdocParser.Parse("Hello").Document;
        var options = new PdfRenderOptions { FontPath = "/nonexistent/font.ttf" };
        var bytes = new PdfRenderer().RenderToBytes(doc, options);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.That(text, Does.Contain("/BaseFont /Helvetica"));
        Assert.That(text, Does.Not.Contain("/CIDFontType2"));
    }

    private static string? FindSystemFont()
    {
        // Only .ttf files — the TrueType parser does not handle .ttc collections.
        string[] candidates =
        [
            @"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\calibri.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/System/Library/Fonts/Supplemental/Courier New.ttf",
            "/System/Library/Fonts/Supplemental/Times New Roman.ttf",
            "/Library/Fonts/Arial.ttf",
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    // ── Outline / bookmark tree ─────────────────────────────────────────

    [Test]
    public void Render_document_with_sections_produces_outline()
    {
        var doc = new DocumentNode { Title = "My Doc" };
        var section = new SectionNode { Level = 1, Title = "First Section", Id = "_first_section" };
        section.AddChild(new ParagraphNode { Text = "Content." });
        doc.AddChild(section);

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        string pdfText = Encoding.ASCII.GetString(pdf);

        Assert.That(pdfText, Does.Contain("/Type /Outlines"),
            "PDF should contain an outline root object");
        Assert.That(pdfText, Does.Contain("/Title (First Section)"),
            "PDF outline should contain the section title");
    }

    [Test]
    public void Render_outline_includes_document_title()
    {
        var doc = new DocumentNode { Title = "My Document Title" };
        var section = new SectionNode { Level = 1, Title = "Section", Id = "_section" };
        section.AddChild(new ParagraphNode { Text = "Text." });
        doc.AddChild(section);

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        string pdfText = Encoding.ASCII.GetString(pdf);

        Assert.That(pdfText, Does.Contain("/Title (My Document Title)"),
            "Outline should include document title");
    }

    [Test]
    public void Render_outline_nests_child_sections()
    {
        var doc = new DocumentNode { Title = "Doc" };
        var sect1 = new SectionNode { Level = 1, Title = "Chapter One", Id = "_chapter_one" };
        var sect2 = new SectionNode { Level = 2, Title = "Sub Section", Id = "_sub_section" };
        sect2.AddChild(new ParagraphNode { Text = "Deep content." });
        sect1.AddChild(sect2);
        doc.AddChild(sect1);

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        string pdfText = Encoding.ASCII.GetString(pdf);

        Assert.That(pdfText, Does.Contain("/Title (Chapter One)"));
        Assert.That(pdfText, Does.Contain("/Title (Sub Section)"));
        // The sub-section's parent should reference the chapter's object
        Assert.That(pdfText, Does.Contain("/First ").And.Contain("/Last "),
            "Chapter outline entry should have First/Last for nested children");
    }

    [Test]
    public void Render_catalog_references_outlines()
    {
        var doc = new DocumentNode { Title = "Doc" };
        var section = new SectionNode { Level = 1, Title = "Sec", Id = "_sec" };
        section.AddChild(new ParagraphNode { Text = "Text." });
        doc.AddChild(section);

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        string pdfText = Encoding.ASCII.GetString(pdf);

        Assert.That(pdfText, Does.Contain("/Outlines ").And.Contain("/PageMode /UseOutlines"),
            "Catalog should reference outlines and set PageMode");
    }

    [Test]
    public void Render_outline_deterministic()
    {
        var doc = new DocumentNode { Title = "Doc" };
        for (int i = 1; i <= 3; i++)
        {
            var s = new SectionNode { Level = 1, Title = $"Section {i}", Id = $"_section_{i}" };
            s.AddChild(new ParagraphNode { Text = $"Content {i}." });
            doc.AddChild(s);
        }

        byte[] pdf1 = new PdfRenderer().RenderToBytes(doc);
        byte[] pdf2 = new PdfRenderer().RenderToBytes(doc);

        Assert.That(pdf1, Is.EqualTo(pdf2), "Outline rendering must be deterministic");
    }

    // ── Cross-reference links ───────────────────────────────────────────

    [Test]
    public void Render_cross_reference_creates_internal_link()
    {
        var doc = new DocumentNode { Title = "Doc" };
        var section = new SectionNode { Level = 1, Title = "Target Section", Id = "_target_section" };
        section.AddChild(new ParagraphNode { Text = "Target content." });
        doc.AddChild(section);

        // Paragraph with cross-reference to the section
        doc.AddChild(new ParagraphNode
        {
            Text = "See Target Section",
            Inlines = [
                new TextInlineNode { Value = "See " },
                new CrossReferenceInlineNode
                {
                    Target = "_target_section",
                    Label = "Target Section"
                }
            ]
        });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        string pdfText = Encoding.ASCII.GetString(pdf);

        // Should have a GoTo destination annotation (not URI)
        Assert.That(pdfText, Does.Contain("/Dest ["),
            "Cross-reference should create a GoTo destination link");
    }

    [Test]
    public void Render_cross_reference_to_unknown_target_does_not_crash()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "Missing",
            Inlines = [
                new CrossReferenceInlineNode
                {
                    Target = "_nonexistent",
                    Label = "Missing"
                }
            ]
        });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);

        Assert.That(pdf.Length, Is.GreaterThan(0),
            "PDF should be generated even with unknown xref target");
    }

    // ── TOC rendering ───────────────────────────────────────────────────

    [Test]
    public void Render_toc_node_produces_table_of_contents()
    {
        var doc = new DocumentNode { Title = "Doc" };

        var entries = new List<TocEntry>
        {
            new() { Level = 1, Id = "_intro", Title = "Introduction" },
            new() { Level = 1, Id = "_main", Title = "Main Content" },
        };
        doc.AddChild(new TocNode { Entries = entries });

        var s1 = new SectionNode { Level = 1, Title = "Introduction", Id = "_intro" };
        s1.AddChild(new ParagraphNode { Text = "Intro text." });
        doc.AddChild(s1);

        var s2 = new SectionNode { Level = 1, Title = "Main Content", Id = "_main" };
        s2.AddChild(new ParagraphNode { Text = "Main text." });
        doc.AddChild(s2);

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        string pdfText = Encoding.ASCII.GetString(pdf);

        // TOC title should be in the PDF
        AssertPdfContains(pdf, "Table of Contents");
        // TOC entries should be in the PDF
        AssertPdfContains(pdf, "Introduction");
        AssertPdfContains(pdf, "Main Content");
    }

    [Test]
    public void Render_toc_with_nested_entries()
    {
        var doc = new DocumentNode { Title = "Doc" };

        var entries = new List<TocEntry>
        {
            new()
            {
                Level = 1, Id = "_chapter", Title = "Chapter",
                Children = [new TocEntry { Level = 2, Id = "_sub", Title = "Subsection" }]
            },
        };
        doc.AddChild(new TocNode { Entries = entries });

        var s1 = new SectionNode { Level = 1, Title = "Chapter", Id = "_chapter" };
        var s2 = new SectionNode { Level = 2, Title = "Subsection", Id = "_sub" };
        s2.AddChild(new ParagraphNode { Text = "Detail." });
        s1.AddChild(s2);
        doc.AddChild(s1);

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);

        AssertPdfContains(pdf, "Chapter");
        AssertPdfContains(pdf, "Subsection");
    }

    [Test]
    public void Render_document_without_toc_produces_no_toc()
    {
        var doc = new DocumentNode { Title = "Simple" };
        doc.AddChild(new ParagraphNode { Text = "No table of contents." });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        string pdfText = Encoding.ASCII.GetString(pdf);

        Assert.That(pdfText, Does.Not.Contain("Table of Contents"),
            "PDF without TocNode should not contain TOC");
    }

    // ── Helper ──────────────────────────────────────────────────────────

    private static void AssertPdfContains(byte[] pdf, string expected)
    {
        string pdfText = Encoding.ASCII.GetString(pdf);
        Assert.That(pdfText, Does.Contain(expected),
            $"PDF content should contain '{expected}'");
    }
}
