using AdocNet;
using AdocNet.Parser;
using AdocNet.Converters.Html;

// ── Parse from a string ──────────────────────────────────────────────────────

var source = """
    = My Document
    :author: Jane Doe

    == Introduction

    This is a *bold* and _italic_ example with a link: https://example.com

    * First item
    * Second item with `code`

    [source,csharp]
    ----
    Console.WriteLine("Hello, world!");
    ----
    """;

var result = AdocParser.Parse(source);

// ── Check diagnostics ────────────────────────────────────────────────────────

if (result.Diagnostics.Any(d => d.IsError))
{
    Console.Error.WriteLine("Parse errors:");
    foreach (var diag in result.Diagnostics.Where(d => d.IsError))
        Console.Error.WriteLine($"  {diag}");
    return 1;
}

if (result.Diagnostics.Count > 0)
{
    Console.Error.WriteLine("Warnings:");
    foreach (var diag in result.Diagnostics)
        Console.Error.WriteLine($"  {diag}");
}

// ── Render to HTML ───────────────────────────────────────────────────────────

var html = new HtmlRenderer().RenderToString(result.Document);
Console.WriteLine(html);

// ── Parse from a file (with include expansion) ──────────────────────────────

if (args.Length > 0)
{
    var filePath = args[0];
    if (!File.Exists(filePath))
    {
        Console.Error.WriteLine($"File not found: {filePath}");
        return 1;
    }

    var fileText = File.ReadAllText(filePath);
    var fileResult = AdocParser.Parse(fileText, new ParseOptions
    {
        SourceFilePath = filePath,
    });

    foreach (var diag in fileResult.Diagnostics)
        Console.Error.WriteLine($"  {diag}");

    Console.WriteLine(new HtmlRenderer().RenderToString(fileResult.Document));
}

return 0;
