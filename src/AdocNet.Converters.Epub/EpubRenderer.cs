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
        var revdate = doc.Attributes.TryGetValue("revdate", out var rd) && !string.IsNullOrWhiteSpace(rd) ? rd : null;
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

        WriteMimetype(archive);
        WriteContainerXml(archive);
        WriteContentOpf(archive, title, author, language, identifier, revdate, chapters);
        WriteTocXhtml(archive, title, chapters, tocEntries);
        WriteTocNcx(archive, title, identifier, language, chapters, tocEntries);
        WriteStyleCss(archive);
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
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);
    }

    private static void WriteContentOpf(ZipArchive archive, string title, string? author, string language,
        string identifier, string? revdate, List<Chapter> chapters)
    {
        var meta = new StringBuilder();
        meta.Append($"    <dc:identifier id=\"pub-identifier\">{EscapeXml(identifier)}</dc:identifier>\n");
        meta.Append("    <meta property=\"identifier-type\" refines=\"#pub-identifier\">uuid</meta>\n");
        meta.Append($"    <dc:title id=\"pub-title\">{EscapeXml(title)}</dc:title>\n");
        meta.Append($"    <dc:language id=\"pub-language\">{EscapeXml(language)}</dc:language>\n");
        if (author is not null)
            meta.Append($"    <dc:creator>{EscapeXml(author)}</dc:creator>\n");
        if (revdate is not null)
            meta.Append($"    <dc:date>{EscapeXml(revdate)}</dc:date>\n");
        meta.Append("    <meta property=\"dcterms:modified\">2026-01-01T00:00:00Z</meta>\n");

        var manifest = new StringBuilder();
        manifest.Append("    <item id=\"nav\" href=\"toc.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n");
        manifest.Append("    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>\n");
        manifest.Append("    <item id=\"style\" href=\"style.css\" media-type=\"text/css\"/>\n");
        var spine = new StringBuilder();
        for (int i = 0; i < chapters.Count; i++)
        {
            var chapterId = $"chapter_{i + 1}";
            manifest.Append($"    <item id=\"{chapterId}\" href=\"{EscapeXml(chapters[i].FileName)}\" media-type=\"application/xhtml+xml\"/>\n");
            spine.Append($"    <itemref idref=\"{chapterId}\"/>\n");
        }

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
        WriteEntry(archive, "OEBPS/content.opf", xml);
    }

    private static void WriteTocXhtml(ZipArchive archive, string title, List<Chapter> chapters,
        List<TocEntry> tocEntries)
    {
        // Nest section entries under the document title to match asciidoctor-epub3's
        // nav.xhtml shape — same hierarchy fix as PDF outline in beta.25.
        // Top-level <a> points at the first chapter (article doctype) or the doc-title pseudo-page
        // (book doctype lacks a title page, so use the first chapter file).
        string firstChapterFile = chapters.Count > 0 ? chapters[0].FileName : "content.xhtml";
        var sb = new StringBuilder();
        sb.Append("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>Table of Contents</title><link rel="stylesheet" type="text/css" href="style.css"/></head>
            <body>
            <nav epub:type="toc" id="toc">
              <h1>Table of Contents</h1>
              <ol>
                <li><a href="
            """);
        sb.Append(EscapeXml(firstChapterFile));
        sb.Append("\">").Append(EscapeXml(title)).Append("</a>");

        if (tocEntries.Count > 0)
        {
            sb.Append("\n      <ol>\n");
            foreach (var entry in tocEntries)
            {
                sb.Append($"        <li><a href=\"{EscapeXml(entry.ChapterFile)}#{EscapeXml(entry.AnchorId)}\">{EscapeXml(entry.Title)}</a></li>\n");
            }
            sb.Append("      </ol>\n    ");
        }

        sb.Append("</li>\n  </ol>\n</nav>\n");
        sb.Append("<nav epub:type=\"landmarks\" id=\"landmarks\" hidden=\"hidden\">\n");
        sb.Append("  <ol>\n");
        sb.Append($"    <li><a epub:type=\"bodymatter\" href=\"{EscapeXml(firstChapterFile)}\">Start of Content</a></li>\n");
        sb.Append("  </ol>\n");
        sb.Append("</nav>\n");
        sb.Append("</body>\n");
        sb.Append("</html>\n");
        WriteEntry(archive, "OEBPS/toc.xhtml", sb.ToString());
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
        WriteEntry(archive, "OEBPS/toc.ncx", sb.ToString());
    }

    private static void WriteStyleCss(ZipArchive archive)
    {
        // Bundled stylesheet covers the structural classes the HtmlRenderer emits
        // (sect1..sect5, paragraph, listingblock, exampleblock, sidebarblock, admonitionblock,
        // quoteblock, tableblock, hdlist, qanda, etc.). Uses generic font-family stacks so
        // readers fall back to their own embedded fonts — no need to bundle TTFs.
        WriteEntry(archive, "OEBPS/style.css",
            """
            /* Reset + box-sizing (asciidoctor-epub3 parity) */
            html, body { margin: 0; padding: 0; }
            *, *:before, *:after { box-sizing: border-box; }

            body {
                font-family: "Noto Serif", Georgia, "Times New Roman", serif;
                margin: 1em;
                color: #1a1a1a;
            }

            /* Body paragraphs (asciidoctor parity: margin-top only, justified) */
            body p {
                font-family: "Noto Serif", Georgia, "Times New Roman", serif;
                line-height: 1.6;
                margin: 1em 0 0 0;
                text-align: justify;
                widows: 2;
                orphans: 2;
            }

            /* Headings — sans-serif, kerning-friendly weights matching asciidoctor */
            h1, h2, h3, h4, h5, h6 {
                font-family: "M+ 1p", "Helvetica Neue", Helvetica, Arial, sans-serif;
                font-weight: 400;
                letter-spacing: -0.01em;
                line-height: 1.4;
                page-break-after: avoid;
                page-break-inside: avoid;
            }
            h1, h2 {
                font-size: 1.5em;
                word-spacing: -0.075em;
                margin-top: 1em;
                margin-bottom: 0.3em;
            }
            h3 { font-size: 1.25em; margin-top: 0.84em; margin-bottom: 0.3em; }
            h4 { font-size: 1.2em; font-weight: 200; color: #202020; margin-top: 0.92em; margin-bottom: 0.3em; }
            h5 { font-size: 0.9em; font-weight: 700; text-transform: uppercase; color: #333332; margin-top: 1.1em; margin-bottom: 0.3em; }
            h6 { font-size: 0.85em; font-weight: 700; margin-top: 1em; margin-bottom: 0.3em; }

            a { color: #2156a5; text-decoration: none; border-bottom: 1px dashed #333332; }
            a:hover { text-decoration: underline; }

            /* Code & monospace (asciidoctor uses #E0E0E0 bg, top+right borders) */
            code, kbd, pre, samp {
                font-family: "M+ 1mn", "Courier New", Consolas, monospace;
                color: #101010;
            }
            code { background: #E0E0E0; padding: 0.1em 0.3em; font-size: 0.9em; }
            pre {
                background: #E0E0E0;
                padding: 8px 12px;
                font-size: 0.85em;
                line-height: 1.4;
                border-top: 1px solid #C8C8C8;
                border-right: 1px solid #C8C8C8;
                white-space: pre-wrap;
                overflow-wrap: break-word;
                page-break-inside: avoid;
            }
            pre code { background: none; padding: 0; }

            /* Chapter wrapper (asciidoctor-epub3 parity) */
            .chapter { display: block; }
            .chapter-header {
                padding: 0.25em 0;
                margin-bottom: 2.5em;
                border-bottom: 1px solid #333332;
            }
            .chapter-title {
                /* Asciidoctor-epub3 chapter title rules (epub3.css). When the title
                   includes <small class="subtitle">…</small>, the small element is
                   visually larger via .subtitle scale below. */
                font-weight: 200;
                font-size: 1.2em;
                margin-top: 3.5em;
                margin-bottom: 0;
                padding-bottom: 0.5em;
                color: #333332;
                text-transform: uppercase;
                word-spacing: -0.075em;
                letter-spacing: -0.01em;
            }
            /* Asciidoctor renders the subtitle larger than the title text via
               this 1.5em multiplier (net effective ≈1.8em on top of .chapter-title
               1.2em). Display:block stacks it under the title text. */
            .chapter-title .subtitle {
                display: block;
                font-size: 1.5em;
                font-weight: 300;
                margin-top: 0.25em;
                color: #555;
            }
            /* Author byline above the chapter title. */
            .byline {
                margin: 0;
                color: #555;
                font-size: 0.95em;
            }
            .byline .author { font-weight: bold; }

            /* Sections */
            .sect1, .sect2, .sect3 { margin-top: 1em; }
            .sectionbody { margin-top: 0.5em; }

            /* Block wrappers */
            .paragraph, .listingblock, .literalblock, .exampleblock,
            .sidebarblock, .quoteblock, .verseblock, .imageblock,
            .videoblock, .audioblock, .openblock, .ulist, .olist,
            .dlist, .hdlist, .qlist, .colist { margin: 1em 0; }
            .listingblock .title, .imageblock .title, .tableblock caption,
            .exampleblock .title { font-style: italic; color: #555; margin-bottom: 0.3em; }

            /* Admonitions */
            .admonitionblock {
                margin: 1em 0;
                padding: 0.75em 1em;
                border-left: 4px solid #888;
                background: #f8f8f8;
                page-break-inside: avoid;
            }
            .admonitionblock.note { border-color: #4a90d9; background: #eaf4fb; }
            .admonitionblock.tip { border-color: #57ad68; background: #ecf7ef; }
            .admonitionblock.warning { border-color: #d97a2c; background: #fdf2e9; }
            .admonitionblock.caution { border-color: #d97a2c; background: #fdf2e9; }
            .admonitionblock.important { border-color: #c83737; background: #fbe9e9; }
            .admonitionblock .title { font-weight: bold; margin-bottom: 0.25em; }

            /* Quotes */
            .quoteblock { padding-left: 1em; border-left: 3px solid #ccc; color: #444; }
            .quoteblock .attribution { font-size: 0.9em; text-align: right; color: #666; }

            /* Sidebars */
            .sidebarblock {
                background: #f6f6f6;
                border: 1px solid #ddd;
                padding: 0.75em 1em;
                page-break-inside: avoid;
            }

            /* Tables */
            table.tableblock, table {
                border-collapse: collapse;
                width: 100%;
                margin: 1em 0;
            }
            table.tableblock th, table.tableblock td, table th, table td {
                border: 1px solid #c8c8c8;
                padding: 0.4em 0.6em;
                vertical-align: top;
            }
            table.tableblock th, table th { background: #f0f0f0; font-weight: bold; text-align: left; }

            /* Lists (asciidoctor uses ::before pseudo-elements for custom bullets) */
            ul, ol { padding-left: 1em; margin-left: 1em; }
            ul { list-style: none; }
            ul > li::before {
                float: left;
                margin-left: -1em;
                padding-left: 0.25em;
                width: 0;
                content: "▪";
                color: #333332;
            }
            ul ul > li::before { content: "◦"; color: #57AD68; }
            ul ul ul > li::before { content: "•"; color: #333332; }
            ul ul ul ul > li::before { content: "▫"; color: #57AD68; }
            ol { list-style-type: decimal; padding-left: 1.75em; margin-left: 0; }
            ul li, ol li { margin-top: 0.4em; }
            dl dt { font-weight: bold; margin-top: 0.5em; }
            dl dd { margin-left: 1.5em; }
            .hdlist > table > tbody > tr > td.hdlist1 { font-weight: bold; padding-right: 1em; vertical-align: top; }

            /* Inline elements */
            mark { background: #fff8a8; padding: 0 0.15em; }
            sub, sup { font-size: 0.75em; line-height: 0; }
            kbd {
                display: inline-block;
                background: #f0f0f0;
                border: 1px solid #c8c8c8;
                border-radius: 3px;
                padding: 0.05em 0.4em;
                font-size: 0.9em;
                box-shadow: 0 1px 0 rgba(0,0,0,0.15);
            }

            /* Images */
            img { max-width: 100%; height: auto; }
            figure { margin: 1em 0; text-align: center; }
            figcaption { font-style: italic; color: #555; margin-top: 0.3em; }
            """);
    }

    private static void WriteChapterXhtml(ZipArchive archive, Chapter chapter)
    {
        // Wrap chapter body in <section class="chapter"> with a <header> containing
        // the chapter title. Matches asciidoctor-epub3 structure so reader-side CSS
        // (and external stylesheets) can target the chapter-title hook consistently.
        var chapterId = Slugify(chapter.PageTitle);
        var titleHtml = EscapeXml(chapter.PageTitle);
        // Split title at first ": " into title + subtitle (asciidoctor-epub3
        // convention: <h1 class="chapter-title">Title <small class="subtitle">…</small></h1>).
        var (titlePart, subtitlePart) = SplitTitleSubtitle(chapter.PageTitle);
        var titleMarkup = subtitlePart is not null
            ? $"{EscapeXml(titlePart)} <small class=\"subtitle\">{EscapeXml(subtitlePart)}</small>"
            : EscapeXml(titlePart);
        // Optional byline above the title (when :author: was set on the doc).
        // Asciidoctor-epub3 prefixes the author name with a small avatar icon
        // (default-avatar.jpg). We use an inline SVG silhouette so the EPUB
        // doesn't need a bundled binary asset.
        var bylineMarkup = chapter.Author is not null
            ? "<p class=\"byline\">" + InlineAvatarSvg + "<b class=\"author\">"
                + EscapeXml(chapter.Author) + "</b></p>\n"
            : "";
        var xhtml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="en" lang="en">
            <head>
              <title>{titleHtml}</title>
              <link rel="stylesheet" type="text/css" href="style.css"/>
            </head>
            <body>
            <section class="chapter" id="{chapterId}">
            <header class="chapter-header">
            {bylineMarkup}<h1 class="chapter-title">{titleMarkup}</h1>
            </header>
            {chapter.HtmlBody}</section>
            </body>
            </html>
            """;
        WriteEntry(archive, $"OEBPS/{chapter.FileName}", xhtml);
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = DeterministicTimestamp;
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
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
