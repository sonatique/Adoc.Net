using System.Text.RegularExpressions;
using AdocNet.Converters.Pdf;

namespace AdocNet.Importers.Tests;

/// <summary>Round trips: AsciiDoc → PDF (AdocNet's own renderer) → PdfImporter → AsciiDoc.</summary>
[TestFixture]
public class PdfImporterTests
{
    private static ImportResult RoundTrip(string asciidoc, PdfRenderOptions? options = null)
    {
        var doc = AdocNet.Parser.AdocParser.Parse(asciidoc).Document;
        var pdf = new PdfRenderer().RenderToBytes(doc, options ?? new PdfRenderOptions());
        return new PdfImporter().Import(new MemoryStream(pdf));
    }

    private const string Manual = """
        = Widget Manual

        == Introduction

        This first paragraph is long enough to wrap across several lines of the page, so the importer has to join the lines back together into one paragraph without losing or doubling any of its words.

        A second paragraph with *bold words* and _italic words_ in it.

        === Details

        * First item
        * Second item
        ** Nested item

        ----
        var x = 1;
        if (x > 0) {
            run();
        }
        ----

        == Second Chapter

        Closing text with a link to https://example.com[the site].

        <<<

        == Third Chapter

        Text on the second page.
        """;

    [Test]
    public void Headings_paragraphs_lists_code_and_links_come_back()
    {
        var r = RoundTrip(Manual, new PdfRenderOptions { HeaderText = "Widget Manual", FooterText = "{page-number} of {page-count}" });
        var adoc = r.AsciiDoc;

        Assert.That(adoc, Does.StartWith("= Widget Manual\n"));
        Assert.That(Regex.Matches(adoc, @"^== ", RegexOptions.Multiline).Count, Is.EqualTo(3), adoc);
        Assert.That(adoc, Does.Contain("\n=== Details\n"));
        Assert.That(adoc, Does.Contain("This first paragraph is long enough to wrap across several lines of the page, so the importer has to join the lines back together into one paragraph without losing or doubling any of its words."));
        Assert.That(adoc, Does.Contain("A second paragraph with *bold words* and _italic words_ in it."));
        Assert.That(adoc, Does.Contain("* First item\n* Second item\n** Nested item"));
        Assert.That(adoc, Does.Contain("----\nvar x = 1;\nif (x > 0) {\n    run();\n}\n----"));
        Assert.That(adoc, Does.Contain("https://example.com[the site"));
        Assert.That(adoc, Does.Not.Contain("of 2"), "running footer dropped");
        Assert.That(Regex.Matches(adoc, "Widget Manual").Count, Is.EqualTo(1), "running header dropped");
        Assert.That(r.Report.Pages, Is.EqualTo(2));
        Assert.That(r.Report.Issues.Select(i => i.Code), Does.Contain("pdf.running-content.dropped").And.Contain("pdf.structure.inferred"));
    }

    [Test]
    public void Hyphenated_line_ends_are_joined()
    {
        var text = string.Join(" ", Enumerable.Repeat("internationalization responsibilities notwithstanding", 12));
        var r = RoundTrip($"= Doc\n\n{text}\n", new PdfRenderOptions { EnableHyphenation = true, PageWidth = 300 });
        Assert.That(r.AsciiDoc, Does.Contain(text));
    }

    [Test]
    public void Tables_come_back_with_their_cells()
    {
        var r = RoundTrip("= Doc\n\n[cols=\"1,1,2\",options=\"header\"]\n|===\n|Name |Size |Notes\n\n|alpha |12 |first entry\n\n|beta |4 |second entry\n|===\n");
        Assert.That(r.AsciiDoc, Does.Contain("|===\n|Name\n|Size\n|Notes\n\n|alpha\n|12\n|first entry\n\n|beta\n|4\n|second entry\n|==="), r.AsciiDoc);
        Assert.That(r.Report.Tables, Is.EqualTo(1));
    }

    [Test]
    public void Embedded_images_become_media()
    {
        var dir = Path.Combine(Path.GetTempPath(), "adocnet-pdfimg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "dot.png"), Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP4z8DAwMDAxMDAwMDAAAANBAEB3ZkSfgAAAABJRU5ErkJggg=="));
            var r = RoundTrip("= Doc\n\nBefore.\n\nimage::dot.png[width=50]\n\nAfter.\n", new PdfRenderOptions { BaseDirectory = dir });
            Assert.That(r.Media, Has.Count.EqualTo(1));
            Assert.That(r.Media[0].RelativePath, Is.EqualTo("media/image1.png"));
            Assert.That(r.AsciiDoc, Does.Contain("Before.\n\nimage::media/image1.png[]\n\nAfter."));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void Title_page_and_toc_become_title_and_toc_attribute()
    {
        var r = RoundTrip("= Main Title: The Subtitle\nAda Writer\n:doctype: book\n:toc: macro\n\ntoc::[]\n\n<<<\n== Alpha\n\nText.\n\n<<<\n== Beta\n\nMore.\n");
        Assert.That(r.AsciiDoc, Does.Contain(":toc:"));
        Assert.That(r.AsciiDoc, Does.Contain("== Alpha\n\nText.").And.Contain("== Beta\n\nMore."));
        Assert.That(r.AsciiDoc, Does.Not.Contain(". . ."), "no TOC leader lines");
        Assert.That(r.Document.Title, Does.StartWith("Main Title"));
    }

    [Test]
    public void Not_a_pdf_throws_import_exception()
    {
        Assert.Throws<ImportException>(() => new PdfImporter().Import(new MemoryStream("not a pdf"u8.ToArray())));
    }

    [Test]
    public void Wrapped_line_into_an_empty_table_cell_is_kept()
    {
        // Row 2 leaves column 1 empty; the close line below fills only column 1.
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        void Text(string s, double x, double y) => page.AddText(s, 10, new UglyToad.PdfPig.Core.PdfPoint(x, y), font);
        Text("Name", 72, 700); Text("Value", 300, 700);
        Text("alpha", 72, 688);
        Text("late", 300, 676);
        Text("beta", 72, 664); Text("two", 300, 664);

        var r = new PdfImporter().Import(new MemoryStream(builder.Build()));
        Assert.That(r.AsciiDoc, Does.Contain("late").And.Contain("beta"));
    }

    private static string PdfWords(string asciidoc)
    {
        var pdf = new PdfRenderer().RenderToBytes(AdocNet.Parser.AdocParser.Parse(asciidoc).Document, new PdfRenderOptions());
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text));
    }

    [Test]
    public void Pdf_converter_prints_monospace_passthrough_in_cells_and_keeps_bold_glued()
    {
        var words = PdfWords("[cols=\"1,1\"]\n|===\n|Lead `+Mixed mono pass+` tail |plain\n|===\n\n*Name*- x and *Bold*, y.\n");
        Assert.That(words, Does.Contain("Lead Mixed mono pass tail"));
        Assert.That(words, Does.Contain("Name- x and Bold, y."));
    }

    [Test]
    public void Pdf_converter_prints_asciidoc_cell_blocks_not_their_source()
    {
        var words = PdfWords("[cols=\"1,1a\"]\n|===\n|Outer\n|\nBefore nested.\n\n* item\n\n!===\n!In1 !In2\n!===\n|===\n");
        Assert.That(words, Does.Contain("Before nested.").And.Contain("item").And.Contain("In1 | In2"));
        Assert.That(words, Does.Not.Contain("!===").And.Not.Contain("* item"));
    }

    [Test]
    public void Admonition_label_beside_a_multi_line_paragraph_marks_it()
    {
        // The label sits in its own column, vertically centred on four lines of text.
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.HelveticaBold);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        static UglyToad.PdfPig.Core.PdfPoint P(double x, double y) => new(x, y);
        page.AddText("Some body text before the note, long enough to read as a paragraph.", 10, P(72, 760), font);
        string[] note = ["The bytes use the importer's order, which differs from the", "displayed order. Bytes 0 and 1 are the first part; byte 2 is the",
                         "middle part; bytes 3 through 5 are the last part, least", "significant first. Colons do not change this order."];
        for (int i = 0; i < note.Length; i++) page.AddText(note[i], 10, P(160, 720 - 12 * i), font);
        page.AddText("IMPORTANT", 10, P(72, 702), bold);
        page.AddText("After the note.", 10, P(72, 650), font);

        var r = new PdfImporter().Import(new MemoryStream(builder.Build()));
        Assert.That(r.AsciiDoc, Does.Contain("IMPORTANT: The bytes use the importer's order, which differs from the displayed order. Bytes 0 and 1 are the first part; byte 2 is the middle part;"), r.AsciiDoc);
        Assert.That(r.AsciiDoc, Does.Not.Contain("== IMPORTANT").And.Not.Contain("=== IMPORTANT").And.Not.Contain("==== IMPORTANT"), r.AsciiDoc);
    }

    [Test]
    public void Cover_title_keeps_the_space_a_metadata_title_lost()
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        builder.DocumentInformation.Title = "Packet FormatUser Manual";
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var cover = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        cover.AddText("Packet Format", 30, new UglyToad.PdfPig.Core.PdfPoint(72, 500), font);
        cover.AddText("User Manual", 30, new UglyToad.PdfPig.Core.PdfPoint(72, 460), font);
        var body = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        body.AddText("Body text on the second page.", 10, new UglyToad.PdfPig.Core.PdfPoint(72, 700), font);

        var r = new PdfImporter().Import(new MemoryStream(builder.Build()));
        Assert.That(r.AsciiDoc, Does.StartWith("= Packet Format User Manual\n"), r.AsciiDoc);
    }

    [Test]
    public void Cover_subtitle_and_version_line_are_kept()
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        builder.DocumentInformation.Author = "Ada Writer";
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var cover = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        static UglyToad.PdfPig.Core.PdfPoint P(double x, double y) => new(x, y);
        cover.AddText("Packet Format", 30, P(72, 500), font);
        cover.AddText("A text format for importing and exporting", 10, P(72, 440), font);
        cover.AddText("packets with timestamps.", 10, P(72, 427), font);
        cover.AddText("Ada Writer", 10, P(72, 395), font);
        cover.AddText("Version 2.1", 10, P(72, 375), font);
        var body = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        body.AddText("Body text on the second page.", 10, P(72, 700), font);

        var r = new PdfImporter().Import(new MemoryStream(builder.Build()));
        Assert.That(r.AsciiDoc, Does.StartWith("= Packet Format: A text format for importing and exporting packets with timestamps.\n"), r.AsciiDoc);
        Assert.That(r.AsciiDoc, Does.Contain(":revnumber: 2.1"), r.AsciiDoc);
        Assert.That(r.AsciiDoc, Does.Not.Contain("Ada Writer\n\n"), r.AsciiDoc);
    }

    [Test]
    public void Pdf_converter_keeps_a_wrapped_heading_on_one_page()
    {
        // Whatever the filler, a two-line heading never starts at a page bottom and ends on the next page.
        const string heading = "A deliberately long section heading that has to wrap onto a second line of the page";
        for (int filler = 40; filler <= 90; filler++)
        {
            var source = "= Doc\n\n" + string.Concat(Enumerable.Range(0, filler).Select(i => $"Filler line {i}.\n\n")) + $"== {heading}\n\nAfter.\n";
            var pdf = new PdfRenderer().RenderToBytes(AdocNet.Parser.AdocParser.Parse(source).Document, new PdfRenderOptions());
            using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
            var pages = document.GetPages().Select(p => (p.Number, Text: string.Join(" ", p.GetWords().Select(w => w.Text)))).ToList();
            var first = pages.First(p => p.Text.Contains("deliberately")).Number;
            var last = pages.First(p => p.Text.Contains("second line of the page")).Number;
            Assert.That(last, Is.EqualTo(first), $"filler {filler}");
        }
    }

    [Test]
    public void Font_family_ignores_lowercase_subset_tags()
    {
        Assert.That(PdfStructure.Family("4fd136+Calibri-Bold"), Is.EqualTo(PdfStructure.Family("c0f016+Calibri")));
        Assert.That(PdfStructure.Family("ABCDEF+NotoSerif-Bold"), Is.EqualTo("notoserif"));
    }

    private static int Tables(AdocNet.Ast.AstNode node) => (node is AdocNet.Ast.TableNode ? 1 : 0) + node.Children.Sum(Tables);
    private static int Tables(ImportResult r) => Tables(r.Document);

    [Test]
    public void Narrow_ruled_columns_come_back_cell_by_cell()
    {
        var r = RoundTrip("""
            == Registers

            [%autowidth,cols="<,,,,>",options="header"]
            |===
            | Bit | Len | Type | Reset | Description

            | 11 | 21 | ro | 0 | Reserved
            | 10 | 1 | rw | 0 | Enables the output stage when set, which takes effect after the next frame boundary.
            | 9 | 1 | rw | | Empty reset cell
            5+| Spanning note across every column
            |===

            * First point
            * Second point

            [cols="1,1"]
            |===
            | left | right
            |===
            """);
        Assert.That(Tables(r), Is.EqualTo(2), r.AsciiDoc);
        Assert.That(r.AsciiDoc, Does.Contain("|Bit\n|Len\n|Type\n|Reset\n|Description\n"));
        Assert.That(r.AsciiDoc, Does.Contain("|9\n|1\n|rw\n|\n|Empty reset cell\n"));
        Assert.That(r.AsciiDoc, Does.Contain("5+|Spanning note across every column"));
        Assert.That(r.AsciiDoc, Does.Contain("* Second point\n\n["), "the list does not swallow the table after it");
    }

    [Test]
    public void Ruled_table_across_a_page_break_stays_one_table_without_the_repeated_header()
    {
        var rows = string.Concat(Enumerable.Range(1, 80).Select(i => $"| row {i} | value {i}\n"));
        var r = RoundTrip($"== Long\n\n[cols=\"1,1\",options=\"header\"]\n|===\n| Key | Value\n\n{rows}|===\n\n== After\n\nText.\n");
        Assert.That(Tables(r), Is.EqualTo(1), r.AsciiDoc);
        Assert.That(Regex.Matches(r.AsciiDoc, @"^\|Key$", RegexOptions.Multiline), Has.Count.EqualTo(1));
        Assert.That(r.AsciiDoc, Does.Contain("|row 80\n|value 80\n|==="));
    }

    [Test]
    public void Level_five_heading_is_a_section()
    {
        var r = RoundTrip("= Doc\n\n== One\n\n=== Two\n\n==== Three\n\n===== Five\n\nBody text under the fifth level.\n\n== Last\n\nEnd.\n");
        Assert.That(r.AsciiDoc, Does.Match(@"\n=+ Five\n\nBody text under the fifth level\."), r.AsciiDoc);
    }

    [Test]
    public void Table_with_column_rules_only_splits_rows_by_spacing()
    {
        var r = RoundTrip("""
            [cols="1,3",grid=cols]
            |===
            | alpha | first row text that is long enough to wrap onto a second line inside its narrow cell of the table
            | beta | second
            | gamma | third
            |===
            """);
        Assert.That(Tables(r), Is.EqualTo(1), r.AsciiDoc);
        Assert.That(r.AsciiDoc, Does.Contain("|beta\n|second\n\n|gamma\n|third\n"), r.AsciiDoc);
    }

    [Test]
    public void Missing_row_rule_under_a_cell_is_a_row_span()
    {
        // 2×3 grid; the rule between rows 1 and 2 is drawn only under column 1, so column 0 spans both rows.
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        static UglyToad.PdfPig.Core.PdfPoint P(double x, double y) => new(x, y);
        foreach (var x in new[] { 72.0, 200, 400 }) page.DrawLine(P(x, 720), P(x, 640), 0.5);
        foreach (var y in new[] { 720.0, 640 }) page.DrawLine(P(72, y), P(400, y), 0.5);
        page.DrawLine(P(72, 693), P(400, 693), 0.5);
        page.DrawLine(P(200, 667), P(400, 667), 0.5);
        void Text(string s, double x, double y) => page.AddText(s, 10, P(x, y), font);
        Text("Key", 76, 703); Text("Value", 204, 703);
        Text("merged", 76, 676); Text("one", 204, 676);
        Text("two", 204, 650);

        var r = new PdfImporter().Import(new MemoryStream(builder.Build()));
        Assert.That(r.AsciiDoc, Does.Contain(".2+|merged\n|one\n\n|two\n"), r.AsciiDoc);
    }

    [Test]
    public void Page_border_does_not_turn_the_page_into_a_table()
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        static UglyToad.PdfPig.Core.PdfPoint P(double x, double y) => new(x, y);
        page.DrawRectangle(P(20, 20), 555, 802, 0.5); // the border
        foreach (var x in new[] { 72.0, 200, 400 }) page.DrawLine(P(x, 660), P(x, 610), 0.5);
        foreach (var y in new[] { 660.0, 635, 610 }) page.DrawLine(P(72, y), P(400, y), 0.5);
        void Text(string s, double x, double y) => page.AddText(s, 10, P(x, y), font);
        Text("Before the table.", 72, 700);
        Text("Key", 76, 643); Text("Value", 204, 643);
        Text("a", 76, 618); Text("b", 204, 618);
        Text("After the table.", 72, 580);

        var r = new PdfImporter().Import(new MemoryStream(builder.Build()));
        Assert.That(Tables(r), Is.EqualTo(1), r.AsciiDoc);
        Assert.That(r.AsciiDoc, Does.Contain("Before the table.\n\n[").And.Contain("|===\n\nAfter the table."), r.AsciiDoc);
    }
}

/// <summary>Parity check on the themed fixture: structure recovered from the themed PDF matches its source.</summary>
[TestFixture]
public class PdfImporterThemedFixtureTests
{
    [Test]
    public void Themed_manual_comes_back_with_its_sections_tables_and_code()
    {
        var repo = TestContext.CurrentContext.TestDirectory;
        while (!Directory.Exists(Path.Combine(repo, "spec", "pdf-parity"))) repo = Path.GetDirectoryName(repo)!;
        var fixture = Path.Combine(repo, "spec", "pdf-parity", "themed-manual");
        var docPath = Path.Combine(fixture, "guide", "manual.adoc");
        var options = PdfThemeLoader.Load(Path.Combine(fixture, "theme", "parity-theme.yml"),
            Path.Combine(repo, "src", "AdocNet.Converters.Epub", "Resources"), Path.GetDirectoryName(docPath));
        var source = AdocNet.Parser.AdocParser.Parse(File.ReadAllText(docPath),
            new AdocNet.ParseOptions { SourceFilePath = docPath, SafeMode = AdocNet.SafeMode.Unsafe }).Document;
        var pdf = new PdfRenderer().RenderToBytes(source, options);

        var r = new PdfImporter().Import(new MemoryStream(pdf));

        int Count(AdocNet.Ast.AstNode node, Func<AdocNet.Ast.AstNode, bool> match) =>
            (match(node) ? 1 : 0) + node.Children.Sum(c => Count(c, match));
        Assert.That(Count(r.Document, n => n is AdocNet.Ast.SectionNode), Is.EqualTo(Count(source, n => n is AdocNet.Ast.SectionNode)), r.AsciiDoc);
        Assert.That(Count(r.Document, n => n is AdocNet.Ast.TableNode), Is.EqualTo(Count(source, n => n is AdocNet.Ast.TableNode)), r.AsciiDoc);
        Assert.That(r.AsciiDoc, Does.Contain(":toc:").And.Contain(":sectnums:"));
        Assert.That(r.AsciiDoc, Does.Contain("IMPORTANT: Use matched double quotes"));
        Assert.That(r.AsciiDoc, Does.Contain("----\nFormat:WidgetExchange version=2.0\n"));
        Assert.That(r.AsciiDoc, Does.Not.Contain(" of 7"), "running footers dropped");
        Assert.That(r.Document.Title, Does.StartWith("Widget Exchange Format"));
    }
}
