using System.IO.Compression;
using System.Text;

namespace AdocNet.Importers.Docx.Tests;

/// <summary>
/// Builds a minimal but valid .docx package in memory. Tests describe the
/// WordprocessingML they care about and inherit a default style/numbering set
/// that matches what Word writes for the built-in styles.
/// </summary>
internal sealed class DocxBuilder
{
    public const string WordNamespaces =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" " +
        "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\"";

    private readonly List<(string Id, string Type, string Target, bool External)> _relationships = new();
    private readonly Dictionary<string, byte[]> _parts = new(StringComparer.Ordinal);

    private string _body = string.Empty;
    private string? _styles;
    private string? _numbering;
    private string? _footnotes;
    private string? _endnotes;
    private string? _comments;
    private string? _coreProperties;

    public DocxBuilder Body(string bodyXml)
    {
        _body = bodyXml;
        return this;
    }

    public DocxBuilder Styles(string stylesXml)
    {
        _styles = stylesXml;
        return this;
    }

    public DocxBuilder Numbering(string numberingXml)
    {
        _numbering = numberingXml;
        return this;
    }

    public DocxBuilder Footnotes(string footnotesXml)
    {
        _footnotes = footnotesXml;
        return this;
    }

    public DocxBuilder Endnotes(string endnotesXml)
    {
        _endnotes = endnotesXml;
        return this;
    }

    public DocxBuilder Comments(string commentsXml)
    {
        _comments = commentsXml;
        return this;
    }

    public DocxBuilder CoreProperties(string? title = null, string? creator = null,
        string? description = null, string? keywords = null, string? revision = null, string? modified = null)
    {
        var sb = new StringBuilder();
        sb.Append("<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" ")
          .Append("xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\">");
        if (title is not null) sb.Append("<dc:title>").Append(title).Append("</dc:title>");
        if (creator is not null) sb.Append("<dc:creator>").Append(creator).Append("</dc:creator>");
        if (description is not null) sb.Append("<dc:description>").Append(description).Append("</dc:description>");
        if (keywords is not null) sb.Append("<cp:keywords>").Append(keywords).Append("</cp:keywords>");
        if (revision is not null) sb.Append("<cp:revision>").Append(revision).Append("</cp:revision>");
        if (modified is not null) sb.Append("<dcterms:modified>").Append(modified).Append("</dcterms:modified>");
        sb.Append("</cp:coreProperties>");
        _coreProperties = sb.ToString();
        return this;
    }

    /// <summary>Adds an image part plus the relationship that points at it.</summary>
    public DocxBuilder Image(string relationshipId, string fileName, byte[] content)
    {
        _parts["word/media/" + fileName] = content;
        _relationships.Add((relationshipId,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
            "media/" + fileName, false));
        return this;
    }

    public DocxBuilder Hyperlink(string relationshipId, string url)
    {
        _relationships.Add((relationshipId,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink",
            url, true));
        return this;
    }

    public byte[] Build()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "[Content_Types].xml", ContentTypes());
            Write(archive, "_rels/.rels", PackageRelationships());
            Write(archive, "word/document.xml",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document {WordNamespaces}><w:body>{_body}</w:body></w:document>");
            Write(archive, "word/_rels/document.xml.rels", DocumentRelationships());
            Write(archive, "word/styles.xml", _styles ?? DefaultStyles);
            if (_numbering is not null) Write(archive, "word/numbering.xml", _numbering);
            if (_footnotes is not null) Write(archive, "word/footnotes.xml", _footnotes);
            if (_endnotes is not null) Write(archive, "word/endnotes.xml", _endnotes);
            if (_comments is not null) Write(archive, "word/comments.xml", _comments);
            if (_coreProperties is not null) Write(archive, "docProps/core.xml", _coreProperties);

            foreach (var part in _parts)
            {
                var entry = archive.CreateEntry(part.Key, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(part.Value, 0, part.Value.Length);
            }
        }

        return buffer.ToArray();
    }

    public MemoryStream BuildStream() => new(Build());

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private string ContentTypes()
        => "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
           "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
           "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
           "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
           "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
           "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
           "</Types>";

    private string PackageRelationships()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
          .Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">")
          .Append("<Relationship Id=\"rIdDoc\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>");
        if (_coreProperties is not null)
        {
            sb.Append("<Relationship Id=\"rIdCore\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>");
        }

        sb.Append("</Relationships>");
        return sb.ToString();
    }

    private string DocumentRelationships()
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
          .Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">")
          .Append("<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");

        if (_numbering is not null)
            sb.Append("<Relationship Id=\"rIdNum\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/>");
        if (_footnotes is not null)
            sb.Append("<Relationship Id=\"rIdFootnotes\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes\" Target=\"footnotes.xml\"/>");
        if (_endnotes is not null)
            sb.Append("<Relationship Id=\"rIdEndnotes\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/endnotes\" Target=\"endnotes.xml\"/>");

        foreach (var (id, type, target, external) in _relationships)
        {
            sb.Append("<Relationship Id=\"").Append(id)
              .Append("\" Type=\"").Append(type)
              .Append("\" Target=\"").Append(target.Replace("&", "&amp;"))
              .Append('"');
            if (external) sb.Append(" TargetMode=\"External\"");
            sb.Append("/>");
        }

        sb.Append("</Relationships>");
        return sb.ToString();
    }

    /// <summary>The built-in styles Word writes into a document that uses them.</summary>
    public const string DefaultStyles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Title\"><w:name w:val=\"Title\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Subtitle\"><w:name w:val=\"Subtitle\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:pPr><w:outlineLvl w:val=\"1\"/></w:pPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading3\"><w:name w:val=\"heading 3\"/><w:pPr><w:outlineLvl w:val=\"2\"/></w:pPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading4\"><w:name w:val=\"heading 4\"/><w:pPr><w:outlineLvl w:val=\"3\"/></w:pPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading5\"><w:name w:val=\"heading 5\"/><w:pPr><w:outlineLvl w:val=\"4\"/></w:pPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading6\"><w:name w:val=\"heading 6\"/><w:pPr><w:outlineLvl w:val=\"5\"/></w:pPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"ListParagraph\"><w:name w:val=\"List Paragraph\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Quote\"><w:name w:val=\"Quote\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"IntenseQuote\"><w:name w:val=\"Intense Quote\"/><w:basedOn w:val=\"Quote\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Caption\"><w:name w:val=\"caption\"/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"HTMLPreformatted\"><w:name w:val=\"HTML Preformatted\"/></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"HTMLCode\"><w:name w:val=\"HTML Code\"/></w:style>" +
        "</w:styles>";

    /// <summary>A bullet list at levels 0-2 (numId 1) and a decimal list (numId 2).</summary>
    public const string DefaultNumbering =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<w:numbering xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
        "<w:abstractNum w:abstractNumId=\"0\">" +
        "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"\"/></w:lvl>" +
        "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"o\"/></w:lvl>" +
        "<w:lvl w:ilvl=\"2\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"\"/></w:lvl>" +
        "</w:abstractNum>" +
        "<w:abstractNum w:abstractNumId=\"1\">" +
        "<w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl>" +
        "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"lowerLetter\"/><w:lvlText w:val=\"%2.\"/></w:lvl>" +
        "<w:lvl w:ilvl=\"2\"><w:start w:val=\"1\"/><w:numFmt w:val=\"lowerRoman\"/><w:lvlText w:val=\"%3.\"/></w:lvl>" +
        "</w:abstractNum>" +
        "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>" +
        "<w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num>" +
        "</w:numbering>";

    // ── Body-fragment helpers ───────────────────────────────────────────────

    public static string Paragraph(string text, string? styleId = null)
        => $"<w:p>{ParagraphProperties(styleId)}<w:r><w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r></w:p>";

    public static string Heading(int level, string text)
        => Paragraph(text, "Heading" + level);

    public static string ListItem(string text, string numId, int level = 0)
        => $"<w:p><w:pPr><w:pStyle w:val=\"ListParagraph\"/><w:numPr><w:ilvl w:val=\"{level}\"/>" +
           $"<w:numId w:val=\"{numId}\"/></w:numPr></w:pPr><w:r><w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r></w:p>";

    public static string Run(string text, string? properties = null)
        => $"<w:r>{(properties is null ? string.Empty : $"<w:rPr>{properties}</w:rPr>")}" +
           $"<w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r>";

    public static string ParagraphOf(params string[] runs)
        => "<w:p>" + string.Concat(runs) + "</w:p>";

    public static string Drawing(string relationshipId, long widthEmu = 1905000, long heightEmu = 952500,
        string? description = null)
        => "<w:r><w:drawing><wp:inline>" +
           $"<wp:extent cx=\"{widthEmu}\" cy=\"{heightEmu}\"/>" +
           $"<wp:docPr id=\"1\" name=\"Picture 1\"{(description is null ? string.Empty : $" descr=\"{Escape(description)}\"")}/>" +
           "<a:graphic><a:graphicData><pic:pic><pic:blipFill>" +
           $"<a:blip r:embed=\"{relationshipId}\"/>" +
           "</pic:blipFill></pic:pic></a:graphicData></a:graphic>" +
           "</wp:inline></w:drawing></w:r>";

    private static string ParagraphProperties(string? styleId)
        => styleId is null ? string.Empty : $"<w:pPr><w:pStyle w:val=\"{styleId}\"/></w:pPr>";

    public static string Escape(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>A 1×1 transparent PNG, enough to exercise media extraction.</summary>
    public static byte[] SamplePng { get; } = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
}
