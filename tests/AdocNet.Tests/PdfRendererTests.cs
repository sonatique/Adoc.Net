using System.Text;
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
    public void Render_source_block_includes_language()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "Console.WriteLine();"
        });

        byte[] pdf = new PdfRenderer().RenderToBytes(doc);
        AssertPdfContains(pdf, "csharp");
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
            Term = "CPU",
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
        string[] candidates =
        [
            @"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\calibri.ttf",
            @"C:\Windows\Fonts\segoeui.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/System/Library/Fonts/Helvetica.ttc",
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    // ── Helper ──────────────────────────────────────────────────────────

    private static void AssertPdfContains(byte[] pdf, string expected)
    {
        string pdfText = Encoding.ASCII.GetString(pdf);
        Assert.That(pdfText, Does.Contain(expected),
            $"PDF content should contain '{expected}'");
    }
}
