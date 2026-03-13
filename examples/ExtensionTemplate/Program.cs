using AdocNet;
using AdocNet.Parser;
using ExtensionTemplate;

var source = """
    = Sample Document

    == First Section

    A simple paragraph with some text.

    == Second Section

    Another paragraph here.
    """;

var result = AdocParser.Parse(source);

if (result.Diagnostics.Any(d => d.IsError))
{
    Console.Error.WriteLine("Parse errors:");
    foreach (var diag in result.Diagnostics.Where(d => d.IsError))
        Console.Error.WriteLine($"  {diag}");
    return 1;
}

var output = new MyRenderer().RenderToString(result.Document);
Console.WriteLine(output);

return 0;
