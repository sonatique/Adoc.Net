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
