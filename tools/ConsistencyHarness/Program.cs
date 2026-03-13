// Consistency harness: targets net6.0, which resolves to the netstandard2.0 build of AdocNet.
// Parses .adoc files and outputs HTML + AST + PDF byte count for cross-TFM comparison.

using System;
using System.IO;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet;
using AdocNet.Parser;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: ConsistencyHarness <fixtures-dir> <output-dir>");
    return 1;
}

var fixturesDir = args[0];
var outputDir = args[1];
Directory.CreateDirectory(outputDir);

var htmlRenderer = new HtmlRenderer();

foreach (var file in Directory.GetFiles(fixturesDir, "*.adoc"))
{
    var name = Path.GetFileNameWithoutExtension(file);
    var text = File.ReadAllText(file);

    // Parse
    var result = AdocParser.Parse(text);

    // AST pretty-print
    var ast = AstPrettyPrinter.Print(result.Document);
    File.WriteAllText(Path.Combine(outputDir, name + ".ast.txt"), ast);

    // HTML render
    var html = htmlRenderer.RenderToString(result.Document);
    File.WriteAllText(Path.Combine(outputDir, name + ".html"), html);

    // PDF render (byte count only — structural check)
    try
    {
        var pdfRenderer = new AdocNet.Converters.Pdf.PdfRenderer();
        using var pdfStream = new MemoryStream();
        pdfRenderer.Render(result.Document, pdfStream, AdocNet.RenderOptions.Default);
        File.WriteAllText(Path.Combine(outputDir, name + ".pdf-info.txt"),
            $"ByteCount={pdfStream.Length}");
    }
    catch (Exception ex)
    {
        File.WriteAllText(Path.Combine(outputDir, name + ".pdf-info.txt"),
            $"Error={ex.GetType().Name}: {ex.Message}");
    }

    Console.WriteLine($"OK: {name}");
}

return 0;
