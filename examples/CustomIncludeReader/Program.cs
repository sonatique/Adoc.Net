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

// ── Write include content to temp files so AdocParser can resolve them ──────
//
// AdocParser.Parse uses the built-in FileIncludeReader internally.
// To demonstrate custom include content, we write our in-memory files to a
// temporary directory and parse with SourceFilePath pointing there.
//
// The InMemoryIncludeReader class above shows the IIncludeReader pattern;
// it can be used directly if the include expansion API is extended in the
// future to accept a custom reader.

var reader = new InMemoryIncludeReader(includeFiles);

var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-include-demo");
Directory.CreateDirectory(tempDir);

try
{
    // Write each in-memory file to the temp directory.
    foreach (var (name, content) in includeFiles)
    {
        File.WriteAllText(Path.Combine(tempDir, name), content);
    }

    // Verify our InMemoryIncludeReader resolves the same content.
    Console.WriteLine("=== InMemoryIncludeReader verification ===");
    foreach (var name in includeFiles.Keys)
    {
        var resolvedPath = Path.Combine(tempDir, name);
        Console.WriteLine($"  {name}: Exists={reader.Exists(resolvedPath)}, Length={reader.Read(resolvedPath).Length}");
    }
    Console.WriteLine();

    // Write the main document that includes the chapters.
    var mainDoc = """
        = My Book

        include::chapter1.adoc[]

        include::chapter2.adoc[]
        """;

    var mainPath = Path.Combine(tempDir, "main.adoc");
    File.WriteAllText(mainPath, mainDoc);

    // Parse with SourceFilePath set so includes are expanded from the temp directory.
    var result = AdocParser.Parse(File.ReadAllText(mainPath), new ParseOptions
    {
        SourceFilePath = mainPath,
    });

    if (result.Diagnostics.Count > 0)
    {
        Console.WriteLine("Diagnostics:");
        foreach (var diag in result.Diagnostics)
            Console.Error.WriteLine($"  {diag}");
    }

    // Render to HTML to show that included content was resolved.
    var html = new HtmlRenderer().RenderToString(result.Document);
    Console.WriteLine("=== Rendered HTML (with resolved includes) ===");
    Console.WriteLine(html);
}
finally
{
    // Clean up temp files.
    if (Directory.Exists(tempDir))
        Directory.Delete(tempDir, recursive: true);
}

return 0;
