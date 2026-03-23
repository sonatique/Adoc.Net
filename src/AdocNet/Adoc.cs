using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Converters.Pdf;
using AdocNet.Parser;

namespace AdocNet;

/// <summary>
/// Zero-config entry point for AsciiDoc conversion.
/// Use this when you want the simplest possible API with sensible defaults.
/// </summary>
/// <example>
/// <code>
/// string html = Adoc.ToHtml("= Title\n\nHello *world*.");
/// byte[] pdf  = Adoc.ToPdf("= Title\n\nHello *world*.");
/// </code>
/// </example>
public static class Adoc
{
    /// <summary>
    /// Converts AsciiDoc source text to an HTML fragment.
    /// Returns semantic HTML5 without a document wrapper.
    /// </summary>
    /// <param name="input">AsciiDoc source text.</param>
    /// <returns>HTML string.</returns>
    public static string ToHtml(string input)
    {
        var doc = AdocParser.Parse(input).Document;
        return new HtmlRenderer().RenderToString(doc);
    }

    /// <summary>
    /// Converts AsciiDoc source text to a full HTML document with embedded CSS.
    /// </summary>
    /// <param name="input">AsciiDoc source text.</param>
    /// <param name="theme">CSS theme to apply. Defaults to <see cref="HtmlTheme.Default"/>.</param>
    /// <returns>Complete HTML document string.</returns>
    public static string ToStyledHtml(string input, HtmlTheme theme = HtmlTheme.Default)
    {
        var doc = AdocParser.Parse(input).Document;
        return new HtmlRenderer().RenderToString(doc, new HtmlRenderOptions { Theme = theme });
    }

    /// <summary>
    /// Converts AsciiDoc source text to a PDF byte array.
    /// Uses A4 page size with default margins.
    /// </summary>
    /// <param name="input">AsciiDoc source text.</param>
    /// <returns>PDF file contents as a byte array.</returns>
    public static byte[] ToPdf(string input)
    {
        var doc = AdocParser.Parse(input).Document;
        return new PdfRenderer().RenderToBytes(doc);
    }

    /// <summary>
    /// Converts AsciiDoc source text to HTML and writes it to a stream.
    /// </summary>
    /// <param name="input">AsciiDoc source text.</param>
    /// <param name="output">Stream to write the HTML output to.</param>
    public static void ToHtml(string input, Stream output)
    {
        var doc = AdocParser.Parse(input).Document;
        new HtmlRenderer().Render(doc, output, RenderOptions.Default);
    }

    /// <summary>
    /// Converts AsciiDoc source text to PDF and writes it to a stream.
    /// </summary>
    /// <param name="input">AsciiDoc source text.</param>
    /// <param name="output">Stream to write the PDF output to.</param>
    public static void ToPdf(string input, Stream output)
    {
        var doc = AdocParser.Parse(input).Document;
        new PdfRenderer().Render(doc, output, RenderOptions.Default);
    }

    /// <summary>
    /// Converts an AsciiDoc file to HTML and writes it to a stream.
    /// Resolves include directives relative to the file path.
    /// </summary>
    /// <param name="filePath">Path to the .adoc file.</param>
    /// <param name="output">Stream to write the HTML output to.</param>
    public static void ConvertFile(string filePath, Stream output)
    {
        var text = File.ReadAllText(filePath);
        var doc = AdocParser.Parse(text, new ParseOptions { SourceFilePath = filePath }).Document;
        new HtmlRenderer().Render(doc, output, RenderOptions.Default);
    }

    /// <summary>
    /// Parses AsciiDoc source text and returns the result with AST and diagnostics.
    /// Use this when you need to check for errors or inspect the document structure.
    /// </summary>
    /// <param name="input">AsciiDoc source text.</param>
    /// <returns>Parse result containing the document AST and any diagnostics.</returns>
    public static ParseResult Parse(string input)
        => AdocParser.Parse(input);
}
