using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Importers.Docx.Tests;

/// <summary>Shared helpers: import a synthesised package and inspect the result.</summary>
internal static class ImportHarness
{
    public static DocxImportResult Import(DocxBuilder builder, DocxImportOptions? options = null)
    {
        using var stream = builder.BuildStream();
        return new DocxImporter(options).Import(stream);
    }

    public static string ToAsciiDoc(DocxBuilder builder, DocxImportOptions? options = null)
    {
        using var stream = builder.BuildStream();
        return new DocxImporter(options).ToAsciiDoc(stream);
    }

    /// <summary>
    /// Renders imported AsciiDoc through the real parser and HTML renderer,
    /// then strips markup — the text a reader ends up seeing. This is what the
    /// round-trip assertions compare against the Word document's text.
    /// </summary>
    public static string RenderedText(string asciidoc)
    {
        var document = AdocParser.Parse(asciidoc).Document;
        var html = new HtmlRenderer().RenderToString(document);
        return HtmlToText(html);
    }

    private static readonly Regex InlineTag = new(
        @"</?(?:a|abbr|b|big|cite|code|del|em|i|ins|kbd|mark|q|s|samp|small|span|strong|sub|sup|tt|u|var)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string HtmlToText(string html)
    {
        // Inline tags vanish without a trace: Word splits words across runs, so
        // "1<sup>er</sup>" has to read back as "1er", not "1 er". Block-level
        // tags become spaces so words from adjacent blocks do not run together.
        var text = InlineTag.Replace(html, string.Empty);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        return CollapseWhitespace(text);
    }

    public static string CollapseWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
