using AdocNet;
using AdocNet.Parser;
using CustomRenderer;

var source = """
    = My Document

    == Introduction

    This is a *bold* and _italic_ example with `inline code`.

    == Lists

    * First item
    * Second item
    * Third item

    . Step one
    . Step two

    == Code

    [source,csharp]
    ----
    Console.WriteLine("Hello from AdocNet!");
    ----
    """;

var result = AdocParser.Parse(source);

if (result.Diagnostics.Any(d => d.IsError))
{
    Console.Error.WriteLine("Parse errors:");
    foreach (var diag in result.Diagnostics.Where(d => d.IsError))
        Console.Error.WriteLine($"  {diag}");
    return 1;
}

var markdown = new MarkdownRenderer().RenderToString(result.Document);
Console.WriteLine(markdown);

return 0;
