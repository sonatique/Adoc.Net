using AdocNet.Importers.Html;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace AdocNet.Importers;

/// <summary>
/// Imports HTML: the semantic subset (headings, paragraphs, inline formatting, code with its
/// <c>language-xx</c> class, block quotes, lists, description lists, tables with spans, links,
/// images, footnotes) becomes AsciiDoc. <c>&lt;title&gt;</c> and <c>&lt;meta&gt;</c> description,
/// keywords and author become header attributes; inline data-URI images become media files.
/// Scripts, styles and form controls are dropped and reported.
/// </summary>
public sealed class HtmlImporter : DocumentImporter
{
    public HtmlImporter(ImportOptions? options = null) : base(options) { }

    /// <summary>Imports HTML from a stream (encoding from the BOM or <c>&lt;meta charset&gt;</c>, else UTF-8).</summary>
    public override ImportResult Import(Stream input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        using var document = new HtmlParser().ParseDocument(input);
        return Convert(document);
    }

    /// <summary>Imports HTML source text.</summary>
    public ImportResult ImportString(string html)
    {
        if (html is null) throw new ArgumentNullException(nameof(html));
        using var document = new HtmlParser().ParseDocument(html);
        return Convert(document);
    }

    private ImportResult Convert(IDocument document)
    {
        var builder = new AsciiDocBuilder(Options);
        var blocks = ConvertDocument(document, builder, Options, readHead: true);
        return Result(builder.Build(blocks), builder.Report, builder.Media);
    }

    internal static List<string> ConvertDocument(IDocument document, AsciiDocBuilder builder, ImportOptions options, bool readHead)
    {
        string? headTitle = null;
        if (readHead && document.Head is { } head)
        {
            headTitle = head.QuerySelector("title")?.TextContent.Trim();
            foreach (var name in head.QuerySelectorAll("script, style").Select(e => e.LocalName).Distinct())
                builder.Report.Add(ImportIssueSeverity.Info, $"html.{name}.dropped", $"<{name}> content is not document content and was dropped.");
            foreach (var meta in head.QuerySelectorAll("meta[name][content]"))
            {
                var name = meta.GetAttribute("name")!.ToLowerInvariant();
                var content = meta.GetAttribute("content")!;
                if (name is "description" or "keywords" or "author") builder.SetAttribute(name, content);
            }
        }
        var body = document.Body ?? document.DocumentElement;
        var blocks = HtmlConverter.Convert(body, builder, options);
        if (builder.Title is null && !string.IsNullOrEmpty(headTitle)) builder.Title = headTitle;
        return blocks;
    }
}
