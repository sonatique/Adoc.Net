using AdocNet;
using AdocNet.Parser;
using AdocNet.Converters.Html;
using CustomIncludeReader;

// ── Define in-memory content for include targets ────────────────────────────

var includeFiles = new Dictionary<string, string>
{
    ["chapter1.adoc"] = """
        == Chapter 1: Getting Started

        This content was resolved from an *in-memory* include reader.

        * Step one: install the library
        * Step two: parse your first document
        """,
    ["chapter2.adoc"] = """
        == Chapter 2: Advanced Usage

        This chapter covers _advanced_ features like custom renderers.
        """,
};

// ── Resolve includes from memory via ParseOptions.IncludeReader ──────────────
//
// AdocParser.Parse expands include:: directives through the IIncludeReader supplied
// on ParseOptions. Pass a custom reader to resolve include targets from a database,
// embedded resources, or — as here — an in-memory dictionary, with no files on disk.

var reader = new InMemoryIncludeReader(includeFiles);

var mainDoc = """
    = My Book

    include::chapter1.adoc[]

    include::chapter2.adoc[]
    """;

var result = AdocParser.Parse(mainDoc, new ParseOptions
{
    // SourceFilePath gives the includes a base directory to resolve against; the content
    // itself comes from our reader, not the filesystem. The relative include targets resolve
    // within that base directory, so the default SafeMode.Safe is sufficient.
    SourceFilePath = "main.adoc",
    IncludeReader = reader,
});

if (result.Diagnostics.Count > 0)
{
    Console.WriteLine("Diagnostics:");
    foreach (var diag in result.Diagnostics)
        Console.Error.WriteLine($"  {diag}");
}

var html = new HtmlRenderer().RenderToString(result.Document);
Console.WriteLine("=== Rendered HTML (includes resolved from memory) ===");
Console.WriteLine(html);

return 0;
