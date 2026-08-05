using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using AdocNet.Ast;
using AdocNet.Emitter;

namespace AdocNet.Importers.Docx;

/// <summary>
/// Reads a Word (.docx) package and produces an Adoc.Net AST — and, through
/// <see cref="ToAsciiDoc(System.IO.Stream)"/>, AsciiDoc source.
/// <para>
/// What survives: document structure (headings/sections, paragraphs, lists
/// with nesting and numbering style, tables with spans and header rows,
/// images with captions and sizes, hyperlinks, cross references, bookmarks,
/// footnotes and endnotes, admonitions, quote and code blocks, page and
/// thematic breaks, core properties) and character formatting that AsciiDoc
/// models (bold, italic, monospace, super/subscript, highlight, plus
/// underline/strikethrough/caps as roles).
/// </para>
/// <para>
/// What cannot survive: everything that is page geometry or Word-specific
/// presentation — page size and margins, headers and footers, columns, text
/// boxes and shapes, exact fonts and colours, tab stops, line spacing. These
/// are enumerated per document in <see cref="DocxImportReport"/> rather than
/// dropped silently.
/// </para>
/// </summary>
public sealed class DocxImporter
{
    private readonly DocxImportOptions _options;

    public DocxImporter(DocxImportOptions? options = null)
        => _options = options ?? DocxImportOptions.Default;

    /// <summary>Imports a .docx from a seekable stream.</summary>
    public DocxImportResult Import(Stream docx)
    {
        if (docx is null) throw new ArgumentNullException(nameof(docx));

        using var package = OpcPackage.Open(docx);

        var documentPartName = FindDocumentPart(package);
        var documentPart = package.ReadXml(documentPartName)
            ?? throw new DocxImportException($"Main document part '{documentPartName}' is missing from the package.");

        var body = documentPart.Root?.Element(Ns.W + "body")
            ?? throw new DocxImportException("Main document part has no w:body element.");

        var report = new DocxImportReport();
        var relationships = package.GetRelationships(documentPartName);

        var styles = StyleTable.Load(ReadRelatedPart(package, documentPartName, Ns.RelStyles, "word/styles.xml"));
        var numbering = NumberingTable.Load(
            ReadRelatedPart(package, documentPartName, Ns.RelNumbering, "word/numbering.xml"), styles);

        var context = new ConversionContext
        {
            Package = package,
            DocumentPartName = documentPartName,
            DocumentRelationships = relationships,
            Styles = styles,
            Numbering = numbering,
            Options = _options,
            Report = report,
        };

        LoadNotes(package, documentPartName, context);
        var coreProperties = ReadCoreProperties(package);
        context.CoreTitle = coreProperties.Title;

        var document = new DocumentNode();
        new BlockConverter(context, document).ConvertBody(body);

        ApplyDocumentHeader(document, coreProperties, context);

        return new DocxImportResult
        {
            Document = document,
            Report = report,
            Media = context.Media,
        };
    }

    /// <summary>Imports a .docx file. Media stays in memory; nothing is written.</summary>
    public DocxImportResult ImportFile(string path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        using var stream = File.OpenRead(path);
        return Import(stream);
    }

    /// <summary>Imports a .docx from a stream and emits AsciiDoc source.</summary>
    public string ToAsciiDoc(Stream docx) => new AsciidocEmitter().Emit(Import(docx).Document);

    /// <summary>Imports a .docx file and emits AsciiDoc source.</summary>
    public string ToAsciiDoc(string path)
    {
        using var stream = File.OpenRead(path);
        return ToAsciiDoc(stream);
    }

    /// <summary>
    /// Converts <paramref name="docxPath"/> to AsciiDoc at
    /// <paramref name="adocPath"/>, writing extracted images into the media
    /// directory beside the output file (unless
    /// <see cref="DocxImportOptions.ExtractMedia"/> is off).
    /// </summary>
    public DocxImportResult ConvertFile(string docxPath, string adocPath)
    {
        if (docxPath is null) throw new ArgumentNullException(nameof(docxPath));
        if (adocPath is null) throw new ArgumentNullException(nameof(adocPath));

        var result = ImportFile(docxPath);
        var source = new AsciidocEmitter().Emit(result.Document);

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(adocPath));
        if (!string.IsNullOrEmpty(outputDirectory)) Directory.CreateDirectory(outputDirectory!);

        // UTF-8 without a BOM: AsciiDoc processors read plain UTF-8, and a BOM
        // would show up as stray characters in the first line of the header.
        File.WriteAllText(adocPath, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (_options.ExtractMedia) WriteMedia(result.Media, outputDirectory ?? ".");

        return result;
    }

    /// <summary>Writes imported media into <paramref name="baseDirectory"/>.</summary>
    public static void WriteMedia(IReadOnlyList<DocxMediaItem> media, string baseDirectory)
    {
        foreach (var item in media)
        {
            var target = Path.GetFullPath(Path.Combine(baseDirectory, item.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory!);
            File.WriteAllBytes(target, item.Content);
            item.WrittenPath = target;
        }
    }

    // ── Package plumbing ────────────────────────────────────────────────────

    private static string FindDocumentPart(OpcPackage package)
    {
        var rel = package.FindRelationship(string.Empty, Ns.RelOfficeDocument);
        if (rel?.PartName is not null && package.HasPart(rel.PartName)) return rel.PartName;

        // Some producers omit the package relationship; the conventional part
        // name is a safe fallback before giving up.
        if (package.HasPart("word/document.xml")) return "word/document.xml";

        throw new DocxImportException(
            "Package does not declare an officeDocument relationship and has no word/document.xml part.");
    }

    private static XDocument? ReadRelatedPart(OpcPackage package, string documentPartName,
        string relationshipType, string conventionalName)
    {
        var rel = package.FindRelationship(documentPartName, relationshipType);
        if (rel?.PartName is not null) return package.ReadXml(rel.PartName);
        return package.HasPart(conventionalName) ? package.ReadXml(conventionalName) : null;
    }

    private static void LoadNotes(OpcPackage package, string documentPartName, ConversionContext context)
    {
        var footnotes = ReadRelatedPart(package, documentPartName, Ns.RelFootnotes, "word/footnotes.xml");
        if (footnotes?.Root is not null)
        {
            foreach (var note in footnotes.Root.Elements(Ns.W + "footnote"))
            {
                var id = note.Attribute(Ns.W + "id")?.Value;
                var type = note.Attribute(Ns.W + "type")?.Value;
                // "separator" and "continuationSeparator" notes hold the rule
                // Word draws above footnotes, not document content.
                if (id is null || type is not null) continue;
                context.Footnotes[id] = note;
            }
        }

        var endnotes = ReadRelatedPart(package, documentPartName, Ns.RelEndnotes, "word/endnotes.xml");
        if (endnotes?.Root is not null)
        {
            foreach (var note in endnotes.Root.Elements(Ns.W + "endnote"))
            {
                var id = note.Attribute(Ns.W + "id")?.Value;
                var type = note.Attribute(Ns.W + "type")?.Value;
                if (id is null || type is not null) continue;
                context.Endnotes[id] = note;
            }
        }

        if (context.Options.Comments == CommentHandling.Ignore) return;

        var comments = package.HasPart("word/comments.xml") ? package.ReadXml("word/comments.xml") : null;
        if (comments?.Root is null) return;

        foreach (var comment in comments.Root.Elements(Ns.W + "comment"))
        {
            var id = comment.Attribute(Ns.W + "id")?.Value;
            if (id is null) continue;
            context.Comments[id] = comment;
        }
    }

    private sealed class CoreProperties
    {
        public string? Title { get; set; }
        public string? Creator { get; set; }
        public string? Description { get; set; }
        public string? Keywords { get; set; }
        public string? Revision { get; set; }
        public string? Modified { get; set; }
    }

    private static CoreProperties ReadCoreProperties(OpcPackage package)
    {
        var properties = new CoreProperties();

        var rel = package.FindRelationship(string.Empty, Ns.RelCoreProperties);
        var partName = rel?.PartName ?? "docProps/core.xml";
        var part = package.HasPart(partName) ? package.ReadXml(partName) : null;
        if (part?.Root is null) return properties;

        properties.Title = Value(part.Root.Element(Ns.Dc + "title"));
        properties.Creator = Value(part.Root.Element(Ns.Dc + "creator"));
        properties.Description = Value(part.Root.Element(Ns.Dc + "description"));
        properties.Keywords = Value(part.Root.Element(Ns.Cp + "keywords"));
        properties.Revision = Value(part.Root.Element(Ns.Cp + "revision"));
        properties.Modified = Value(part.Root.Element(Ns.DcTerms + "modified"));
        return properties;

        static string? Value(XElement? element)
        {
            var text = element?.Value.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
    }

    private void ApplyDocumentHeader(DocumentNode document, CoreProperties properties, ConversionContext context)
    {
        document.Title ??= properties.Title;

        if (_options.ImportCoreProperties)
        {
            if (properties.Creator is not null) document.SetAttribute("author", properties.Creator);
            if (properties.Description is not null) document.SetAttribute("description", properties.Description);
            if (properties.Keywords is not null) document.SetAttribute("keywords", properties.Keywords);
            if (properties.Revision is not null) document.SetAttribute("revnumber", properties.Revision);
            if (properties.Modified is not null)
            {
                // revdate is a date, not a timestamp; keep the date portion of
                // the ISO 8601 value Word writes.
                var modified = properties.Modified;
                var t = modified.IndexOf('T');
                document.SetAttribute("revdate", t > 0 ? modified.Substring(0, t) : modified);
            }
        }

        if (context.SawTableOfContents && _options.ConvertTocFieldToAttribute)
            document.SetAttribute("toc", string.Empty);
    }
}
