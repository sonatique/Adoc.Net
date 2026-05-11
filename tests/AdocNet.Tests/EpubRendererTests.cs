using System.IO.Compression;
using AdocNet.Ast;
using AdocNet.Converters.Epub;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class EpubRendererTests
{
    [Test]
    public void Produces_valid_zip()
    {
        var doc = BlockParser.Parse("= Title\n\nContent").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.That(zip.Entries.Count, Is.GreaterThan(0));
    }

    [Test]
    public void Contains_mimetype_entry()
    {
        var doc = BlockParser.Parse("Hello").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var mimetype = zip.GetEntry("mimetype");
        Assert.That(mimetype, Is.Not.Null);
        using var reader = new StreamReader(mimetype!.Open());
        Assert.That(reader.ReadToEnd(), Is.EqualTo("application/epub+zip"));
    }

    [Test]
    public void Contains_container_xml()
    {
        var doc = BlockParser.Parse("Hello").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.That(zip.GetEntry("META-INF/container.xml"), Is.Not.Null);
    }

    [Test]
    public void Contains_content_opf()
    {
        var doc = BlockParser.Parse("= My Book\n\nText").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var opf = zip.GetEntry("EPUB/package.opf");
        Assert.That(opf, Is.Not.Null);
        using var reader = new StreamReader(opf!.Open());
        var content = reader.ReadToEnd();
        Assert.That(content, Does.Contain("My Book"));
        Assert.That(content, Does.Contain("version=\"3.0\""));
    }

    [Test]
    public void Contains_navigation_document()
    {
        var doc = BlockParser.Parse("= Doc\n\n== Chapter 1\n\nText\n\n== Chapter 2\n\nMore").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var nav = zip.GetEntry("EPUB/nav.xhtml");
        Assert.That(nav, Is.Not.Null, "Expected EPUB 3 nav.xhtml (Asciidoctor convention)");
        using var reader = new StreamReader(nav!.Open());
        var content = reader.ReadToEnd();
        Assert.That(content, Does.Contain("Chapter 1"));
        Assert.That(content, Does.Contain("Chapter 2"));
    }

    [Test]
    public void Contains_content_xhtml()
    {
        // For article-doctype documents the chapter is named after the title slug;
        // documents with no title fall back to "_content.xhtml".
        var doc = BlockParser.Parse("Hello *bold* world").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var content = zip.GetEntry("EPUB/_content.xhtml");
        Assert.That(content, Is.Not.Null,
            "Expected fallback chapter name '_content.xhtml' for untitled document");
        using var reader = new StreamReader(content!.Open());
        var html = reader.ReadToEnd();
        Assert.That(html, Does.Contain("<strong>bold</strong>"));
    }

    [Test]
    public void Contains_stylesheet()
    {
        var doc = BlockParser.Parse("Hello").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        // Asciidoctor parity: ships full asciidoctor-epub3 stylesheet trio.
        Assert.That(zip.GetEntry("EPUB/styles/epub3.css"), Is.Not.Null);
        Assert.That(zip.GetEntry("EPUB/styles/epub3-css3-only.css"), Is.Not.Null);
        Assert.That(zip.GetEntry("EPUB/styles/epub3-fonts.css"), Is.Not.Null);
    }

    [Test]
    public void Output_is_deterministic()
    {
        var doc = BlockParser.Parse("= Title\n\n== Section\n\nContent").Document;
        var bytes1 = new EpubRenderer().RenderToBytes(doc);
        var bytes2 = new EpubRenderer().RenderToBytes(doc);
        Assert.That(bytes1, Is.EqualTo(bytes2));
    }

    [Test]
    public void Format_is_epub()
    {
        Assert.That(new EpubRenderer().Format, Is.EqualTo("epub"));
    }

    [Test]
    public void Metadata_includes_author()
    {
        var doc = BlockParser.Parse("= Title\nJohn Doe <john@example.com>\n\nContent").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var opf = zip.GetEntry("EPUB/package.opf");
        using var reader = new StreamReader(opf!.Open());
        var content = reader.ReadToEnd();
        Assert.That(content, Does.Contain("John Doe"));
    }

    [Test]
    public void Metadata_omits_creator_when_no_author_set()
    {
        // Asciidoctor-epub3 omits dc:creator entirely when no author is provided.
        // AdocNet previously emitted "Unknown" as a placeholder — drop that.
        var doc = BlockParser.Parse("= Title\n\nContent").Document;
        var content = ReadOpf(new EpubRenderer().RenderToBytes(doc));
        Assert.That(content, Does.Not.Contain("dc:creator"));
        Assert.That(content, Does.Not.Contain("Unknown"));
    }

    [Test]
    public void Metadata_identifier_derived_from_title_slug()
    {
        // Asciidoctor-epub3 uses the doc-title slug as the EPUB identifier
        // (e.g. "How to generate PDF from ADOC" -> "_how_to_generate_pdf_from_adoc").
        var doc = BlockParser.Parse("= How to generate PDF from ADOC\n\nContent").Document;
        var content = ReadOpf(new EpubRenderer().RenderToBytes(doc));
        Assert.That(content, Does.Contain("<dc:identifier id=\"pub-identifier\">_how_to_generate_pdf_from_adoc</dc:identifier>"));
        Assert.That(content, Does.Contain("<meta property=\"identifier-type\" refines=\"#pub-identifier\">uuid</meta>"));
    }

    [Test]
    public void Metadata_emits_id_attributes_on_title_and_language()
    {
        var doc = BlockParser.Parse("= My Book\n\nContent").Document;
        var content = ReadOpf(new EpubRenderer().RenderToBytes(doc));
        Assert.That(content, Does.Contain("<dc:title id=\"pub-title\">My Book</dc:title>"));
        Assert.That(content, Does.Contain("<dc:language id=\"pub-language\">en</dc:language>"));
    }

    [Test]
    public void Metadata_emits_dc_date_when_revdate_set()
    {
        var doc = BlockParser.Parse("= Title\nJohn Doe\nv1.0, 2025-06-01\n\nContent").Document;
        var content = ReadOpf(new EpubRenderer().RenderToBytes(doc));
        Assert.That(content, Does.Contain("<dc:date>2025-06-01</dc:date>"));
    }

    [Test]
    public void Toc_includes_section_number_prefix_when_sectnums_enabled()
    {
        // Same fix as PDF outline (beta.25): the rendered TOC entry must mirror
        // the rendered section title, including the :sectnums: prefix.
        var doc = BlockParser.Parse("= Doc\n:sectnums:\n\n== First\n\nA\n\n== Second\n\nB").Document;
        var toc = ReadToc(new EpubRenderer().RenderToBytes(doc));
        Assert.That(toc, Does.Contain(">1. First<"));
        Assert.That(toc, Does.Contain(">2. Second<"));
    }

    [Test]
    public void Toc_omits_section_number_prefix_when_sectnums_not_enabled()
    {
        var doc = BlockParser.Parse("= Doc\n\n== First\n\nA\n\n== Second\n\nB").Document;
        var toc = ReadToc(new EpubRenderer().RenderToBytes(doc));
        Assert.That(toc, Does.Contain(">First<"));
        Assert.That(toc, Does.Contain(">Second<"));
        Assert.That(toc, Does.Not.Contain(">1. First<"));
    }

    [Test]
    public void Toc_nests_sections_under_document_title()
    {
        // Asciidoctor-epub3 nests section entries under the document title in
        // nav.xhtml (matches the flat-shape PDF outline fix in beta.25).
        var doc = BlockParser.Parse("= My Book\n\n== Chapter\n\nText").Document;
        var toc = ReadToc(new EpubRenderer().RenderToBytes(doc));
        // Doc title appears as a top-level <li>, with the section nested in an inner <ol>.
        var titleIdx = toc.IndexOf("My Book", StringComparison.Ordinal);
        var chapterIdx = toc.IndexOf("Chapter", StringComparison.Ordinal);
        Assert.That(titleIdx, Is.GreaterThan(0).And.LessThan(chapterIdx),
            "Document title should appear in TOC before the first section");
    }

    [Test]
    public void Toc_emits_landmarks_nav()
    {
        // Required by spec for some readers; reference asciidoctor-epub3 always emits it.
        var doc = BlockParser.Parse("= Title\n\nContent").Document;
        var toc = ReadToc(new EpubRenderer().RenderToBytes(doc));
        Assert.That(toc, Does.Contain("epub:type=\"landmarks\""));
        Assert.That(toc, Does.Contain("Start of Content"));
    }

    [Test]
    public void Contains_ncx_for_epub2_compat()
    {
        // EPUB2 readers (older Kindles, etc.) expect toc.ncx. asciidoctor-epub3
        // always emits it alongside the EPUB3 nav.xhtml.
        var doc = BlockParser.Parse("= Doc\n\n== Section\n\nText").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.That(zip.GetEntry("EPUB/toc.ncx"), Is.Not.Null,
            "toc.ncx missing — EPUB2 readers will not see the nav structure");
    }

    [Test]
    public void Ncx_navmap_nests_sections_under_doc_title()
    {
        var doc = BlockParser.Parse("= My Book\n:sectnums:\n\n== First\n\nA\n\n== Second\n\nB").Document;
        var ncx = ReadNcx(new EpubRenderer().RenderToBytes(doc));
        // Outer navPoint = doc title, inner navPoints = sections (with prefix)
        Assert.That(ncx, Does.Contain("<text>My Book</text>"));
        Assert.That(ncx, Does.Contain("<text>1. First</text>"));
        Assert.That(ncx, Does.Contain("<text>2. Second</text>"));
        // Identifier echoed in dtb:uid
        Assert.That(ncx, Does.Contain("name=\"dtb:uid\" content=\"_my_book\""));
    }

    [Test]
    public void Ncx_referenced_from_manifest_and_spine()
    {
        var doc = BlockParser.Parse("= Doc\n\nText").Document;
        var content = ReadOpf(new EpubRenderer().RenderToBytes(doc));
        // Asciidoctor parity: attribute order is href, id, media-type.
        Assert.That(content, Does.Contain("<item href=\"toc.ncx\" id=\"ncx\" media-type=\"application/x-dtbncx+xml\"/>"));
        Assert.That(content, Does.Contain("<spine toc=\"ncx\">"));
    }

    [Test]
    public void Article_doctype_uses_doctitle_slug_as_chapter_filename()
    {
        // asciidoctor-epub3 names the single chapter file after the doc title slug
        // (e.g. "How to generate PDF from ADOC" -> "_how_to_generate_pdf_from_adoc.xhtml").
        var doc = BlockParser.Parse("= How to generate PDF from ADOC\n\nContent").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.That(zip.GetEntry("EPUB/_how_to_generate_pdf_from_adoc.xhtml"), Is.Not.Null,
            "Article-doctype chapter should be named after the doc title slug");
        // Old fixed name should NOT exist
        Assert.That(zip.GetEntry("EPUB/content.xhtml"), Is.Null);
    }

    [Test]
    public void Article_chapter_referenced_in_manifest_and_spine()
    {
        var doc = BlockParser.Parse("= My Book\n\nText").Document;
        var content = ReadOpf(new EpubRenderer().RenderToBytes(doc));
        Assert.That(content, Does.Contain("href=\"_my_book.xhtml\""));
        // Asciidoctor parity: chapter manifest item id = "item_<basename>".
        Assert.That(content, Does.Contain("<itemref idref=\"item__my_book\"/>"));
    }

    [Test]
    public void Toc_anchors_point_to_named_chapter_file()
    {
        // After the chapter-naming change, TOC anchors must use the slugged filename
        // instead of the previous fixed "content.xhtml".
        var doc = BlockParser.Parse("= My Book\n:sectnums:\n\n== First\n\nA").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        var toc = ReadToc(bytes);
        Assert.That(toc, Does.Contain("href=\"_my_book.xhtml#"),
            $"Expected TOC anchor to point at _my_book.xhtml. TOC was:\n{toc}");
        Assert.That(toc, Does.Not.Contain("content.xhtml"));
    }

    [Test]
    public void Book_doctype_splits_into_one_chapter_per_top_level_section()
    {
        // asciidoctor-epub3 in :doctype: book mode emits one xhtml per top-level section,
        // each named after the section title slug.
        var doc = BlockParser.Parse("= My Book\n:doctype: book\n\n== First Chapter\n\nA\n\n== Second Chapter\n\nB").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.That(zip.GetEntry("EPUB/_first_chapter.xhtml"), Is.Not.Null,
            "Expected per-chapter file for the first top-level section");
        Assert.That(zip.GetEntry("EPUB/_second_chapter.xhtml"), Is.Not.Null,
            "Expected per-chapter file for the second top-level section");
        // No fallback single-content file should exist
        Assert.That(zip.GetEntry("EPUB/_my_book.xhtml"), Is.Null);
        Assert.That(zip.GetEntry("EPUB/content.xhtml"), Is.Null);
    }

    [Test]
    public void Book_doctype_spine_lists_chapters_in_order()
    {
        var doc = BlockParser.Parse("= Book\n:doctype: book\n\n== Alpha\n\nA\n\n== Bravo\n\nB\n\n== Charlie\n\nC").Document;
        var content = ReadOpf(new EpubRenderer().RenderToBytes(doc));
        // Spine references appear in chapter order
        var alphaIdx = content.IndexOf("href=\"_alpha.xhtml\"", StringComparison.Ordinal);
        var bravoIdx = content.IndexOf("href=\"_bravo.xhtml\"", StringComparison.Ordinal);
        var charlieIdx = content.IndexOf("href=\"_charlie.xhtml\"", StringComparison.Ordinal);
        Assert.That(alphaIdx, Is.GreaterThan(0));
        Assert.That(bravoIdx, Is.GreaterThan(alphaIdx));
        Assert.That(charlieIdx, Is.GreaterThan(bravoIdx));
    }

    [Test]
    public void Book_doctype_toc_anchors_point_to_correct_per_chapter_files()
    {
        var doc = BlockParser.Parse("= Book\n:doctype: book\n:sectnums:\n\n== First\n\nA\n\n== Second\n\nB").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        var toc = ReadToc(bytes);
        Assert.That(toc, Does.Contain("href=\"_first.xhtml#"));
        Assert.That(toc, Does.Contain("href=\"_second.xhtml#"));
        Assert.That(toc, Does.Contain(">1. First<"));
        Assert.That(toc, Does.Contain(">2. Second<"));
    }

    [Test]
    public void Stylesheet_includes_admonition_and_code_styling()
    {
        // Asciidoctor parity: ships the asciidoctor-epub3 epub3.css which covers
        // structural classes (sect1, paragraph, listingblock, admonitionblock,
        // sidebarblock, etc.) so EPUB readers without their own stylesheet show
        // themed output.
        var doc = BlockParser.Parse("Hello").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("EPUB/styles/epub3.css")!.Open());
        var css = reader.ReadToEnd();
        // The asciidoctor-epub3 stylesheet uses semantic HTML5 elements with
        // role-like classes (aside.admonition, figure.listing, table.table)
        // rather than the `*block`-suffixed wrappers HtmlRenderer emits.
        Assert.That(css, Does.Contain("aside.admonition"), "admonition styling missing");
        Assert.That(css, Does.Contain("aside.sidebar"), "sidebar styling missing");
        Assert.That(css, Does.Contain("pre.source"), "source-block styling missing");
        Assert.That(css, Does.Contain("table.table"), "table styling missing");
    }

    [Test]
    public void Chapter_xhtml_wraps_body_with_chapter_section_and_title()
    {
        // Asciidoctor-epub3 wraps each chapter body in <section class="chapter">
        // with a <header class="chapter-header"> containing <h1 class="chapter-title">.
        // Reader CSS hooks (and external stylesheets) target these classes.
        var doc = BlockParser.Parse("= My Title\n\n== Section\n\nText").Document;
        var bytes = new EpubRenderer().RenderToBytes(doc);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.GetEntry("EPUB/_my_title.xhtml")
                    ?? throw new InvalidOperationException("chapter file missing");
        using var reader = new StreamReader(entry.Open());
        var xhtml = reader.ReadToEnd();

        Assert.That(xhtml, Does.Contain("<section class=\"chapter\""));
        Assert.That(xhtml, Does.Contain("<header class=\"chapter-header\">"));
        // Asciidoctor-epub3 parity: chapter title text is wrapped in
        // <small class="subtitle"> inside <h1 class="chapter-title">.
        Assert.That(xhtml, Does.Contain("<h1 class=\"chapter-title\"><small class=\"subtitle\">My Title</small></h1>"));
        Assert.That(xhtml, Does.Contain("xmlns:epub=\"http://www.idpf.org/2007/ops\""));
    }

    private static string ReadNcx(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("EPUB/toc.ncx")!.Open());
        return reader.ReadToEnd();
    }

    private static string ReadOpf(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("EPUB/package.opf")!.Open());
        return reader.ReadToEnd();
    }

    private static string ReadToc(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("EPUB/nav.xhtml")!.Open());
        return reader.ReadToEnd();
    }
}
