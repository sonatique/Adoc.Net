using System.IO.Compression;
using System.Text;
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

    /// <inheritdoc />
    protected override void RenderDocument(RenderContext context, Stream output)
    {
        var doc = context.Document;

        // Render HTML content using HtmlRenderer
        var htmlRenderer = new HtmlRenderer();
        var htmlContent = htmlRenderer.RenderToString(doc);

        // Extract metadata
        var title = doc.Title ?? "Untitled";
        var author = doc.Attributes.TryGetValue("author", out var a) ? a : "Unknown";
        var language = doc.Attributes.TryGetValue("lang", out var l) ? l : "en";
        var uid = $"urn:adocnet:{Guid.Empty}";

        // Extract TOC entries from top-level sections
        var tocEntries = new List<(string Id, string Title)>();
        foreach (var child in doc.Children)
        {
            if (child is SectionNode section)
            {
                var id = section.Id ?? $"_section_{tocEntries.Count + 1}";
                tocEntries.Add((id, section.Title));
            }
        }

        // Build EPUB ZIP archive
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        WriteMimetype(archive);
        WriteContainerXml(archive);
        WriteContentOpf(archive, title, author, language, uid);
        WriteTocXhtml(archive, title, tocEntries);
        WriteStyleCss(archive);
        WriteContentXhtml(archive, title, htmlContent);
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

    private static void WriteContentOpf(ZipArchive archive, string title, string author, string language, string uid)
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="uid">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="uid">{EscapeXml(uid)}</dc:identifier>
                <dc:title>{EscapeXml(title)}</dc:title>
                <dc:creator>{EscapeXml(author)}</dc:creator>
                <dc:language>{EscapeXml(language)}</dc:language>
                <meta property="dcterms:modified">2026-01-01T00:00:00Z</meta>
              </metadata>
              <manifest>
                <item id="nav" href="toc.xhtml" media-type="application/xhtml+xml" properties="nav"/>
                <item id="content" href="content.xhtml" media-type="application/xhtml+xml"/>
                <item id="style" href="style.css" media-type="text/css"/>
              </manifest>
              <spine>
                <itemref idref="content"/>
              </spine>
            </package>
            """;
        WriteEntry(archive, "OEBPS/content.opf", xml);
    }

    private static void WriteTocXhtml(ZipArchive archive, string title, List<(string Id, string Title)> tocEntries)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>Table of Contents</title></head>
            <body>
            <nav epub:type="toc">
              <h1>Table of Contents</h1>
              <ol>
            """);
        sb.Append('\n');

        foreach (var (id, entryTitle) in tocEntries)
        {
            sb.Append($"    <li><a href=\"content.xhtml#{EscapeXml(id)}\">{EscapeXml(entryTitle)}</a></li>\n");
        }

        sb.Append("""
              </ol>
            </nav>
            </body>
            </html>
            """);
        WriteEntry(archive, "OEBPS/toc.xhtml", sb.ToString());
    }

    private static void WriteStyleCss(ZipArchive archive)
    {
        WriteEntry(archive, "OEBPS/style.css",
            """
            body { font-family: serif; margin: 1em; line-height: 1.6; }
            h1, h2, h3, h4, h5, h6 { margin-top: 1.5em; }
            pre { font-family: monospace; background: #f5f5f5; padding: 0.5em; overflow-x: auto; }
            code { font-family: monospace; }
            table { border-collapse: collapse; width: 100%; }
            td, th { border: 1px solid #ccc; padding: 0.3em; }
            .admonitionblock { border-left: 3px solid #999; padding-left: 1em; margin: 1em 0; }
            """);
    }

    private static void WriteContentXhtml(ZipArchive archive, string title, string htmlContent)
    {
        var xhtml = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head>
              <title>{EscapeXml(title)}</title>
              <link rel="stylesheet" type="text/css" href="style.css"/>
            </head>
            <body>
            {htmlContent}</body>
            </html>
            """;
        WriteEntry(archive, "OEBPS/content.xhtml", xhtml);
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
