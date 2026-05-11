using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Ast;
using AdocNet.Converters.Html;
#if NETSTANDARD2_0
using AdocNet.Internal.Compatibility;
#endif

namespace AdocNet.Converters.Epub;

/// <summary>
/// Renders a <see cref="DocumentNode"/> AST to an EPUB 3.0 archive.
/// Internally reuses <see cref="HtmlRenderer"/> for content generation.
/// </summary>
public sealed class EpubRenderer : DocumentRendererBase
{
    private static readonly DateTimeOffset DeterministicTimestamp =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override string Format => "epub";

    /// <summary>
    /// One emitted chapter file: filename inside the archive, page title, and rendered HTML body.
    /// Section TOC entries point into one of these chapters.
    /// </summary>
    private readonly record struct Chapter(string FileName, string PageTitle, string HtmlBody, string? Author = null);

    /// <summary>
    /// One TOC entry: which chapter file it belongs to, the section anchor id within that chapter,
    /// and the display title (already prefixed with "1. ", "2. ", etc. when sectnums is enabled).
    /// </summary>
    private readonly record struct TocEntry(string ChapterFile, string AnchorId, string Title);

    /// <inheritdoc />
    protected override void RenderDocument(RenderContext context, Stream output)
    {
        var doc = context.Document;

        // Extract metadata
        var title = doc.Title ?? "Untitled";
        var author = doc.Attributes.TryGetValue("author", out var a) && !string.IsNullOrWhiteSpace(a) ? a : null;
        var language = doc.Attributes.TryGetValue("lang", out var l) ? l : "en";
        // Asciidoctor uses :revdate: when set, otherwise the file mtime
        // (:docdatetime: or :docdate:) — both are populated by the parser when
        // the document has a SourceFilePath. Falls back to no date.
        string? revdate = null;
        if (doc.Attributes.TryGetValue("revdate", out var rd) && !string.IsNullOrWhiteSpace(rd))
            revdate = rd;
        else if (doc.Attributes.TryGetValue("docdatetime", out var ddt) && !string.IsNullOrWhiteSpace(ddt))
            revdate = ConvertToIso8601Z(ddt);
        else if (doc.Attributes.TryGetValue("docdate", out var dd) && !string.IsNullOrWhiteSpace(dd))
            revdate = ConvertToIso8601Z(dd);
        // Derive identifier from the document title slug, mirroring asciidoctor-epub3.
        // Falls back to a deterministic urn if the document has no title.
        var identifier = doc.Title is not null
            ? Slugify(doc.Title)
            : $"urn:adocnet:{Guid.Empty}";

        bool sectnumsEnabled = doc.Attributes.ContainsKey("sectnums");
        bool isBookDoctype = doc.Attributes.TryGetValue("doctype", out var dt) && dt == "book";

        // Build chapters and TOC entries together. For book doctype: one chapter per top-level
        // section (matches asciidoctor-epub3 behaviour). For article doctype: a single chapter
        // file named after the document title slug.
        var chapters = new List<Chapter>();
        var tocEntries = new List<TocEntry>();
        if (isBookDoctype)
        {
            BuildBookChapters(doc, sectnumsEnabled, chapters, tocEntries);
        }
        else
        {
            BuildArticleChapter(doc, identifier, sectnumsEnabled, chapters, tocEntries);
        }

        // Build EPUB ZIP archive
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        // :description: doc attribute → <dc:description> in OPF metadata.
        var description = doc.Attributes.TryGetValue("description", out var desc) && !string.IsNullOrWhiteSpace(desc)
            ? desc : null;

        WriteMimetype(archive);
        WriteContainerXml(archive);
        WriteContentOpf(archive, title, author, language, identifier, revdate, description, chapters);
        WriteNavXhtml(archive, title, chapters, tocEntries);
        WriteTocNcx(archive, title, identifier, language, chapters, tocEntries);
        // Bundled fonts/CSS/images + iBooks display options. Asset paths inside
        // the EPUB match asciidoctor-epub3 conventions so manifest item href
        // attributes resolve correctly.
        WriteAssets(archive);
        foreach (var ch in chapters)
            WriteChapterXhtml(archive, ch);
    }

    /// <summary>
    /// Article doctype path: render the whole document into one chapter file named after the
    /// document title slug. TOC anchors point at section IDs within that one chapter.
    /// </summary>
    /// <summary>
    /// HTML void elements that must be self-closed in XHTML. EPUB readers
    /// strictly parse chapter HTML as XHTML, so unclosed &lt;col&gt;, &lt;br&gt;,
    /// etc. trigger reader errors ("Opening and ending tag mismatch").
    /// </summary>
    private static readonly Regex VoidElementPattern = new(
        @"<(br|col|hr|img|meta|link|input|area|source|track|wbr|param|embed)\b(?<attrs>[^>]*?)(?<!/)>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Converts the HtmlRenderer's HTML5 output into XHTML by self-closing
    /// the void elements EPUB readers require closed. Idempotent.
    /// </summary>
    private static string ToXhtml(string html) =>
        VoidElementPattern.Replace(html, "<${1}${attrs} />");

    /// <summary>
    /// Inline SVG avatar silhouette used in the chapter byline. Mimics
    /// asciidoctor-epub3's default-avatar.jpg without bundling a binary asset.
    /// 24×24 px, dark grey on transparent background.
    /// </summary>
    private const string InlineAvatarSvg =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" "
        + "width=\"24\" height=\"24\" class=\"avatar\" "
        + "style=\"vertical-align:middle;margin-right:0.5em;fill:#777;\">"
        + "<path d=\"M12 12a4 4 0 1 0-4-4 4 4 0 0 0 4 4zm0 2c-2.7 0-8 1.3-8 4v2h16v-2c0-2.7-5.3-4-8-4z\"/>"
        + "</svg>";

    /// <summary>
    /// Splits a chapter title at the first ": " separator into title + subtitle
    /// (asciidoctor-epub3's title-subtitle convention). Returns (title, null)
    /// when there's no separator.
    /// </summary>
    private static (string Title, string? Subtitle) SplitTitleSubtitle(string fullTitle)
    {
        var idx = fullTitle.IndexOf(": ", StringComparison.Ordinal);
        if (idx < 0) return (fullTitle, null);
        return (fullTitle.Substring(0, idx), fullTitle.Substring(idx + 2));
    }

    private static void BuildArticleChapter(DocumentNode doc, string identifier, bool sectnumsEnabled,
        List<Chapter> chapters, List<TocEntry> tocEntries)
    {
        var htmlRenderer = new HtmlRenderer();
        var htmlOptions = new HtmlRenderOptions { SuppressInlineToc = true };
        var htmlContent = ToXhtml(htmlRenderer.RenderToString(doc, htmlOptions));
        // Use the slug-based identifier as the chapter filename when a title exists; fall back
        // to a stable "_content.xhtml" otherwise (urn-based names produce illegal filenames).
        var chapterFile = doc.Title is not null
            ? $"{identifier}.xhtml"
            : "_content.xhtml";
        var chapterAuthor = doc.Attributes.TryGetValue("author", out var auth) && !string.IsNullOrWhiteSpace(auth) ? auth : null;
        chapters.Add(new Chapter(chapterFile, doc.Title ?? "Untitled", htmlContent, chapterAuthor));

        int counter = 0;
        foreach (var child in doc.Children)
        {
            if (child is not SectionNode section) continue;
            counter++;
            var id = section.Id ?? $"_section_{counter}";
            var entryTitle = sectnumsEnabled && !section.IsDiscrete
                ? $"{counter}. {section.Title}"
                : section.Title;
            tocEntries.Add(new TocEntry(chapterFile, id, entryTitle));
        }
    }

    /// <summary>
    /// Book doctype path: each top-level section becomes its own chapter file. The doc-level
    /// attributes are copied onto a synthetic single-section DocumentNode so the chapter renders
    /// with the same context (sectnums, etc.) as the original.
    /// </summary>
    private static void BuildBookChapters(DocumentNode doc, bool sectnumsEnabled,
        List<Chapter> chapters, List<TocEntry> tocEntries)
    {
        var htmlRenderer = new HtmlRenderer();
        var htmlOptions = new HtmlRenderOptions { SuppressInlineToc = true };
        int counter = 0;
        foreach (var child in doc.Children)
        {
            if (child is not SectionNode section) continue;
            counter++;
            var id = section.Id ?? $"_section_{counter}";
            var entryTitle = sectnumsEnabled && !section.IsDiscrete
                ? $"{counter}. {section.Title}"
                : section.Title;

            // Synthesize a single-section DocumentNode preserving doc-level attributes
            var synth = new DocumentNode { Title = section.Title };
            foreach (var (k, v) in doc.Attributes) synth.SetAttribute(k, v);
            synth.AddChild(section);

            var chapterFile = $"{Slugify(section.Title)}.xhtml";
            var html = ToXhtml(htmlRenderer.RenderToString(synth, htmlOptions));
            var chapterAuthor = synth.Attributes.TryGetValue("author", out var bookAuth) && !string.IsNullOrWhiteSpace(bookAuth) ? bookAuth : null;
            chapters.Add(new Chapter(chapterFile, section.Title, html, chapterAuthor));
            tocEntries.Add(new TocEntry(chapterFile, id, entryTitle));
        }

        // Edge case: no top-level sections → emit a stub chapter so the EPUB has a spine entry.
        if (chapters.Count == 0)
        {
            var html = ToXhtml(htmlRenderer.RenderToString(doc, htmlOptions));
            var stubName = $"{Slugify(doc.Title ?? "content")}.xhtml";
            var chapterAuthor = doc.Attributes.TryGetValue("author", out var stubAuth) && !string.IsNullOrWhiteSpace(stubAuth) ? stubAuth : null;
            chapters.Add(new Chapter(stubName, doc.Title ?? "Content", html, chapterAuthor));
        }
    }

    /// <summary>
    /// Slugifies a string for use as an EPUB identifier (lowercase, ASCII letters/digits only,
    /// non-alphanumeric runs collapsed to "_", with a leading underscore — matches
    /// asciidoctor-epub3's identifier-from-title convention).
    /// </summary>
    private static string Slugify(string input)
    {
        var sb = new StringBuilder(input.Length + 1);
        sb.Append('_');
        bool lastWasUnderscore = true; // suppress consecutive underscores
        foreach (var ch in input)
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                sb.Append(ch);
                lastWasUnderscore = false;
            }
            else if (ch >= 'A' && ch <= 'Z')
            {
                sb.Append((char)(ch + 32));
                lastWasUnderscore = false;
            }
            else if (!lastWasUnderscore)
            {
                sb.Append('_');
                lastWasUnderscore = true;
            }
        }
        // Strip any trailing underscore for cleaner output
        while (sb.Length > 1 && sb[sb.Length - 1] == '_') sb.Length--;
        return sb.ToString();
    }

    private static void WriteMimetype(ZipArchive archive)
    {
        var entry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        entry.LastWriteTime = DeterministicTimestamp;
        using var stream = entry.Open();
#if NET10_0_OR_GREATER
        stream.Write("application/epub+zip"u8);
#else
        var mimeBytes = Encoding.ASCII.GetBytes("application/epub+zip");
        stream.Write(mimeBytes, 0, mimeBytes.Length);
#endif
    }

    private static void WriteContainerXml(ZipArchive archive)
    {
        WriteEntry(archive, "META-INF/container.xml",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="EPUB/package.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);
    }

    private static void WriteContentOpf(ZipArchive archive, string title, string? author, string language,
        string identifier, string? revdate, string? description, List<Chapter> chapters)
    {
        // Metadata field order matches asciidoctor-epub3 exactly (per the
        // canonicalized reference output):
        //   language, identifier, identifier-type, title, (creator), date,
        //   modified, (description). Only the parenthesised fields are conditional.
        var meta = new StringBuilder();
        meta.Append($"    <dc:language id=\"pub-language\">{EscapeXml(language)}</dc:language>\n");
        meta.Append($"    <dc:identifier id=\"pub-identifier\">{EscapeXml(identifier)}</dc:identifier>\n");
        meta.Append("    <meta property=\"identifier-type\" refines=\"#pub-identifier\">uuid</meta>\n");
        meta.Append($"    <dc:title id=\"pub-title\">{EscapeXml(title)}</dc:title>\n");
        if (author is not null)
            meta.Append($"    <dc:creator>{EscapeXml(author)}</dc:creator>\n");
        if (revdate is not null)
            meta.Append($"    <dc:date>{EscapeXml(revdate)}</dc:date>\n");
        // dcterms:modified should be the document's last-modified timestamp.
        // Use the source-file mtime when available (deterministic per file)
        // and fall back to a fixed instant for in-memory documents.
        var modified = revdate ?? "2026-01-01T00:00:00Z";
        meta.Append($"    <meta property=\"dcterms:modified\">{EscapeXml(modified)}</meta>\n");
        if (description is not null)
            meta.Append($"    <dc:description>{EscapeXml(description)}</dc:description>\n");

        // Manifest order (asciidoctor-epub3 parity):
        //   1. nav.xhtml (EPUB 3 navigation document, properties="nav")
        //   2. each chapter (properties="scripted" — Calibre-style detection JS)
        //   3. toc.ncx (EPUB 2 fallback)
        //   4. styles (epub3, epub3-css3-only, epub3-fonts)
        //   5. fonts (Noto Serif x4, M+ 1p x3, M+ 1mn x4, FA solid, assorted)
        //   6. avatars/headshots default JPEGs
        var manifest = new StringBuilder();
        manifest.Append("    <item href=\"nav.xhtml\" id=\"nav\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n");
        var spine = new StringBuilder();
        foreach (var ch in chapters)
        {
            // Item id = "item_" + filename without ".xhtml" extension.
            var stem = ch.FileName.EndsWith(".xhtml", StringComparison.Ordinal)
                ? ch.FileName.Substring(0, ch.FileName.Length - 6)
                : ch.FileName;
            var itemId = $"item_{stem}";
            manifest.Append($"    <item href=\"{EscapeXml(ch.FileName)}\" id=\"{EscapeXml(itemId)}\" media-type=\"application/xhtml+xml\" properties=\"scripted\"/>\n");
            spine.Append($"    <itemref idref=\"{EscapeXml(itemId)}\"/>\n");
        }
        manifest.Append("    <item href=\"toc.ncx\" id=\"ncx\" media-type=\"application/x-dtbncx+xml\"/>\n");
        manifest.Append("    <item href=\"styles/epub3.css\" id=\"item_epub3\" media-type=\"text/css\"/>\n");
        manifest.Append("    <item href=\"styles/epub3-css3-only.css\" id=\"item_epub3-css3-only\" media-type=\"text/css\"/>\n");
        manifest.Append("    <item href=\"styles/epub3-fonts.css\" id=\"item_epub3-fonts\" media-type=\"text/css\"/>\n");
        manifest.Append("    <item href=\"fonts/notoserif-regular-latin.ttf\" id=\"item_notoserif-regular-latin\" media-type=\"application/vnd.ms-opentype\"/>\n");
        manifest.Append("    <item href=\"fonts/notoserif-italic-latin.ttf\" id=\"item_notoserif-italic-latin\" media-type=\"application/vnd.ms-opentype\"/>\n");
        manifest.Append("    <item href=\"fonts/notoserif-bold-latin.ttf\" id=\"item_notoserif-bold-latin\" media-type=\"application/vnd.ms-opentype\"/>\n");
        manifest.Append("    <item href=\"fonts/notoserif-bolditalic-latin.ttf\" id=\"item_notoserif-bolditalic-latin\" media-type=\"application/vnd.ms-opentype\"/>\n");
        manifest.Append("    <item href=\"fonts/mplus1p-regular-latin.ttf\" id=\"item_mplus1p-regular-latin\" media-type=\"application/vnd.ms-opentype\"/>\n");
        manifest.Append("    <item href=\"fonts/mplus1p-light-latin.ttf\" id=\"item_mplus1p-light-latin\" media-type=\"application/vnd.ms-opentype\"/>\n");
        manifest.Append("    <item href=\"fonts/mplus1p-bold-latin.ttf\" id=\"item_mplus1p-bold-latin\" media-type=\"application/vnd.ms-opentype\"/>\n");
        manifest.Append("    <item href=\"fonts/mplus1mn-regular-ascii-conums.ttf\" id=\"item_mplus1mn-regular-ascii-conums\" media-type=\"application/vnd.ms-opentype\"/>\n");
        manifest.Append("    <item href=\"fonts/mplus1mn-italic-ascii.ttf\" id=\"item_mplus1mn-italic-ascii\" media-type=\"application/vnd.ms-opentype\"/>\n");
        manifest.Append("    <item href=\"fonts/mplus1mn-bold-ascii.ttf\" id=\"item_mplus1mn-bold-ascii\" media-type=\"application/vnd.ms-opentype\"/>\n");
        manifest.Append("    <item href=\"fonts/mplus1mn-bolditalic-ascii.ttf\" id=\"item_mplus1mn-bolditalic-ascii\" media-type=\"application/vnd.ms-opentype\"/>\n");
        manifest.Append("    <item href=\"fonts/awesome/fa-solid-900.ttf\" id=\"item_fa-solid-900\" media-type=\"application/vnd.ms-opentype\"/>\n");
        manifest.Append("    <item href=\"fonts/assorted-icons.ttf\" id=\"item_assorted-icons\" media-type=\"application/vnd.ms-opentype\"/>\n");
        manifest.Append("    <item href=\"avatars/default.jpg\" id=\"item_default\" media-type=\"image/jpeg\"/>\n");
        manifest.Append("    <item href=\"headshots/default.jpg\" id=\"item_default1\" media-type=\"image/jpeg\"/>\n");

        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="pub-identifier">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            {meta.ToString().TrimEnd()}
              </metadata>
              <manifest>
            {manifest.ToString().TrimEnd()}
              </manifest>
              <spine toc="ncx">
            {spine.ToString().TrimEnd()}
              </spine>
            </package>
            """;
        WriteEntry(archive, "EPUB/package.opf", xml);
    }

    private static void WriteNavXhtml(ZipArchive archive, string title, List<Chapter> chapters,
        List<TocEntry> tocEntries)
    {
        // EPUB 3 navigation document. Structure mirrors asciidoctor-epub3:
        //   <section class="chapter"> wrapping
        //     <header><h1 class="chapter-title"><small class="subtitle">Table of Contents</small></h1></header>
        //     <nav epub:type="toc"><ol>…</ol></nav>
        //     <nav epub:type="landmarks" hidden><ol>…</ol></nav>
        // Top-level <li><a> points at the first chapter; per-chapter section
        // entries nest under it.
        string firstChapterFile = chapters.Count > 0 ? chapters[0].FileName : "content.xhtml";
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" xml:lang=\"en\" lang=\"en\">\n");
        sb.Append("<head>\n");
        sb.Append($"<title>{EscapeXml(title)}</title>\n");
        sb.Append("<link rel=\"stylesheet\" type=\"text/css\" href=\"styles/epub3.css\"/>\n");
        sb.Append("<link rel=\"stylesheet\" type=\"text/css\" href=\"styles/epub3-css3-only.css\" media=\"(min-device-width: 0px)\"/>\n");
        sb.Append("</head>\n");
        sb.Append("<body>\n");
        sb.Append("<section class=\"chapter\">\n");
        sb.Append("<header class=\"chapter-header\">\n");
        sb.Append("<h1 class=\"chapter-title\"><small class=\"subtitle\">Table of Contents</small></h1>\n");
        sb.Append("</header>\n");
        sb.Append("<nav epub:type=\"toc\" id=\"toc\">\n");
        sb.Append("<ol>\n");
        sb.Append($"<li><a href=\"{EscapeXml(firstChapterFile)}\">{EscapeXml(title)}</a>");
        if (tocEntries.Count > 0)
        {
            sb.Append("\n<ol>\n");
            foreach (var entry in tocEntries)
                sb.Append($"<li><a href=\"{EscapeXml(entry.ChapterFile)}#{EscapeXml(entry.AnchorId)}\">{EscapeXml(entry.Title)}</a></li>\n");
            sb.Append("</ol>\n");
        }
        sb.Append("</li>\n");
        sb.Append("</ol>\n");
        sb.Append("</nav>\n\n");
        sb.Append("<nav epub:type=\"landmarks\" id=\"landmarks\" hidden=\"hidden\">\n");
        sb.Append("<ol>\n");
        sb.Append($"<li><a epub:type=\"bodymatter\" href=\"{EscapeXml(firstChapterFile)}\">Start of Content</a></li>\n\n");
        sb.Append("</ol>\n");
        sb.Append("</nav>\n");
        sb.Append("</section>\n");
        sb.Append("</body>\n");
        sb.Append("</html>\n");
        WriteEntry(archive, "EPUB/nav.xhtml", sb.ToString());
    }

    /// <summary>
    /// Writes the legacy NCX (Navigation Control file for XML) TOC for EPUB2 backward
    /// compatibility. Many older readers (notably older Kindle devices) require this even
    /// in EPUB3 documents. Mirrors asciidoctor-epub3's nav structure: outer navPoint for
    /// the document title with one nested navPoint per top-level section.
    /// </summary>
    private static void WriteTocNcx(ZipArchive archive, string title, string identifier, string language,
        List<Chapter> chapters, List<TocEntry> tocEntries)
    {
        string firstChapterFile = chapters.Count > 0 ? chapters[0].FileName : "content.xhtml";
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append($"<ncx xmlns=\"http://www.daisy.org/z3986/2005/ncx/\" version=\"2005-1\" xml:lang=\"{EscapeXml(language)}\">\n");
        sb.Append("<head>\n");
        sb.Append($"<meta name=\"dtb:uid\" content=\"{EscapeXml(identifier)}\"/>\n");
        sb.Append($"<meta name=\"dtb:depth\" content=\"{(tocEntries.Count > 0 ? 2 : 1)}\"/>\n");
        sb.Append("<meta name=\"dtb:totalPageCount\" content=\"0\"/>\n");
        sb.Append("<meta name=\"dtb:maxPageNumber\" content=\"0\"/>\n");
        sb.Append("</head>\n");
        sb.Append($"<docTitle><text>{EscapeXml(title)}</text></docTitle>\n");
        sb.Append("<navMap>\n");
        sb.Append("<navPoint id=\"nav_1\" playOrder=\"1\">\n");
        sb.Append($"<navLabel><text>{EscapeXml(title)}</text></navLabel>\n");
        sb.Append($"<content src=\"{EscapeXml(firstChapterFile)}\"/>\n");
        int order = 2;
        foreach (var entry in tocEntries)
        {
            sb.Append($"<navPoint id=\"nav_{order}\" playOrder=\"{order}\">\n");
            sb.Append($"<navLabel><text>{EscapeXml(entry.Title)}</text></navLabel>\n");
            sb.Append($"<content src=\"{EscapeXml(entry.ChapterFile)}#{EscapeXml(entry.AnchorId)}\"/>\n");
            sb.Append("</navPoint>\n");
            order++;
        }
        sb.Append("</navPoint>\n");
        sb.Append("</navMap>\n");
        sb.Append("</ncx>\n");
        WriteEntry(archive, "EPUB/toc.ncx", sb.ToString());
    }


    private static void WriteChapterXhtml(ZipArchive archive, Chapter chapter)
    {
        // Wrap chapter body in <section class="chapter"> with a <header> containing
        // the chapter title. Matches asciidoctor-epub3 structure so reader-side CSS
        // (and external stylesheets) can target the chapter-title hook consistently.
        var chapterId = Slugify(chapter.PageTitle);
        var titleHtml = EscapeXml(chapter.PageTitle);
        // Asciidoctor-epub3 wraps the chapter title text in <small class="subtitle">
        // unconditionally (even when there's no explicit ': ' split — the small element
        // gets a CSS-driven larger size). We follow the same convention.
        var titleMarkup = $"<small class=\"subtitle\">{EscapeXml(chapter.PageTitle)}</small>";
        // Asciidoctor-epub3 always emits the byline header — uses the bundled
        // default avatar JPEG and an empty <b class="author"> when no :author:
        // is set. Provides a consistent layout hook for reader-side CSS.
        var authorName = chapter.Author is not null ? EscapeXml(chapter.Author) : "";
        var bylineMarkup =
            $"<p class=\"byline\"><img src=\"avatars/default.jpg\"/><b class=\"author\">{authorName}</b></p>\n";
        // Calibre/reader detection script — matches asciidoctor-epub3's chapter
        // template. Sets the body class to the reading-system name so per-reader
        // CSS hooks can target Kindle, Calibre, etc. The 'scripted' manifest
        // property points at this script.
        const string CalibreScript = """
            <script type="text/javascript">
            document.addEventListener('DOMContentLoaded', function(event, reader) {
              if (!(reader = navigator.epubReadingSystem)) {
                if (navigator.userAgent.indexOf(' calibre/') >= 0) reader = { name: 'calibre-desktop' };
                else if (window.parent == window || !(reader = window.parent.navigator.epubReadingSystem)) return;
              }
              document.body.setAttribute('class', reader.name.toLowerCase().replace(/ /g, '-'));
            });
            </script>
            """;
        var xhtml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="en" lang="en">
            <head>
              <title>{titleHtml}</title>
              <link rel="stylesheet" type="text/css" href="styles/epub3.css"/>
              <link rel="stylesheet" type="text/css" href="styles/epub3-css3-only.css" media="(min-device-width: 0px)"/>
              {CalibreScript}
            </head>
            <body>
            <section class="chapter" id="{chapterId}" title="{titleHtml}">
            <header class="chapter-header">
            {bylineMarkup}<h1 class="chapter-title">{titleMarkup}</h1>
            </header>
            {chapter.HtmlBody}</section>
            </body>
            </html>
            """;
        WriteEntry(archive, $"EPUB/{chapter.FileName}", xhtml);
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = DeterministicTimestamp;
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    /// <summary>
    /// Writes all embedded asset resources (fonts, CSS, default avatar/headshot,
    /// iBooks display options) into the EPUB archive at the asciidoctor-epub3
    /// canonical paths. The resources are bundled with the assembly via
    /// &lt;EmbeddedResource&gt; in the csproj.
    /// </summary>
    private static void WriteAssets(ZipArchive archive)
    {
        // (assembly resource name, archive path) tuples. Resource names follow
        // the .NET convention: {RootNamespace}.{folder}.{file}.
        var assets = new (string ResourceName, string ArchivePath)[]
        {
            // Stylesheets — chapter XHTML references epub3.css; epub3-css3-only.css
            // is loaded conditionally by readers that support media-query gates;
            // epub3-fonts.css declares the @font-face rules for the embedded TTFs.
            ("AdocNet.Converters.Epub.Resources.epub3.css",                       "EPUB/styles/epub3.css"),
            ("AdocNet.Converters.Epub.Resources.epub3-css3-only.css",             "EPUB/styles/epub3-css3-only.css"),
            ("AdocNet.Converters.Epub.Resources.epub3-fonts.css",                 "EPUB/styles/epub3-fonts.css"),

            // Noto Serif body-text fonts (Latin subset).
            ("AdocNet.Converters.Epub.Resources.notoserif-regular-latin.ttf",     "EPUB/fonts/notoserif-regular-latin.ttf"),
            ("AdocNet.Converters.Epub.Resources.notoserif-italic-latin.ttf",      "EPUB/fonts/notoserif-italic-latin.ttf"),
            ("AdocNet.Converters.Epub.Resources.notoserif-bold-latin.ttf",        "EPUB/fonts/notoserif-bold-latin.ttf"),
            ("AdocNet.Converters.Epub.Resources.notoserif-bolditalic-latin.ttf",  "EPUB/fonts/notoserif-bolditalic-latin.ttf"),

            // M+ 1p heading fonts (Latin subset).
            ("AdocNet.Converters.Epub.Resources.mplus1p-regular-latin.ttf",       "EPUB/fonts/mplus1p-regular-latin.ttf"),
            ("AdocNet.Converters.Epub.Resources.mplus1p-light-latin.ttf",         "EPUB/fonts/mplus1p-light-latin.ttf"),
            ("AdocNet.Converters.Epub.Resources.mplus1p-bold-latin.ttf",          "EPUB/fonts/mplus1p-bold-latin.ttf"),

            // M+ 1mn monospace + ASCII conums fallback.
            ("AdocNet.Converters.Epub.Resources.mplus1mn-regular-ascii-conums.ttf","EPUB/fonts/mplus1mn-regular-ascii-conums.ttf"),
            ("AdocNet.Converters.Epub.Resources.mplus1mn-italic-ascii.ttf",       "EPUB/fonts/mplus1mn-italic-ascii.ttf"),
            ("AdocNet.Converters.Epub.Resources.mplus1mn-bold-ascii.ttf",         "EPUB/fonts/mplus1mn-bold-ascii.ttf"),
            ("AdocNet.Converters.Epub.Resources.mplus1mn-bolditalic-ascii.ttf",   "EPUB/fonts/mplus1mn-bolditalic-ascii.ttf"),

            // FontAwesome 5 Solid + assorted-icons supplementary glyphs.
            ("AdocNet.Converters.Epub.Resources.awesome.fa-solid-900.ttf",        "EPUB/fonts/awesome/fa-solid-900.ttf"),
            ("AdocNet.Converters.Epub.Resources.assorted-icons.ttf",              "EPUB/fonts/assorted-icons.ttf"),

            // Default author avatar / chapter headshot (used when :author: has
            // no explicit avatar override).
            ("AdocNet.Converters.Epub.Resources.avatar.jpg",                      "EPUB/avatars/default.jpg"),
            ("AdocNet.Converters.Epub.Resources.headshot.jpg",                    "EPUB/headshots/default.jpg"),

            // iBooks-specific metadata: keeps the reader from substituting fonts.
            ("AdocNet.Converters.Epub.Resources.com.apple.ibooks.display-options.xml",
             "META-INF/com.apple.ibooks.display-options.xml"),
        };

        var asm = typeof(EpubRenderer).Assembly;
        foreach (var (resourceName, archivePath) in assets)
        {
            using var resource = asm.GetManifestResourceStream(resourceName);
            if (resource is null) continue; // resource missing — skip silently
            var entry = archive.CreateEntry(archivePath, CompressionLevel.Optimal);
            entry.LastWriteTime = DeterministicTimestamp;
            using var stream = entry.Open();
            resource.CopyTo(stream);
        }
    }

    /// <summary>
    /// Converts a parser-emitted date attribute (e.g. "2026-04-15" or
    /// "2026-04-15 13:10:19 +0000") into an ISO 8601 UTC instant
    /// ("2026-04-15T13:10:19Z") for &lt;dc:date&gt;. Returns the input
    /// unchanged if it doesn't look like one of the known formats.
    /// </summary>
    private static string ConvertToIso8601Z(string raw)
    {
        if (DateTimeOffset.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dto))
        {
            return dto.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
        }
        return raw;
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
