// C# script to render the showcase with TrueType fonts and page numbers
// Run with: dotnet script samples/render-showcase.csx

#r "../src/AdocNet.Core/bin/Debug/net10.0/AdocNet.Core.dll"
#r "../src/AdocNet.Ast/bin/Debug/net10.0/AdocNet.Ast.dll"
#r "../src/AdocNet.Parser/bin/Debug/net10.0/AdocNet.Parser.dll"
#r "../src/AdocNet.Converters.Pdf/bin/Debug/net10.0/AdocNet.Converters.Pdf.dll"

using AdocNet;
using AdocNet.Converters.Pdf;

var source = File.ReadAllText("samples/beta3-showcase.adoc");
var doc = AdocParser.Parse(source).Document;

var options = new PdfRenderOptions
{
    FontPath = @"C:\Windows\Fonts\arial.ttf",
    BoldFontPath = @"C:\Windows\Fonts\arialbd.ttf",
    ItalicFontPath = @"C:\Windows\Fonts\ariali.ttf",
    MonoFontPath = @"C:\Windows\Fonts\consola.ttf",
    ShowPageNumbers = true,
    FooterText = "Page {page} of {pages}",
};

var pdf = new PdfRenderer().RenderToBytes(doc, options);
File.WriteAllBytes("samples/beta3-showcase-arial.pdf", pdf);
Console.WriteLine($"Written: samples/beta3-showcase-arial.pdf ({pdf.Length:N0} bytes)");
