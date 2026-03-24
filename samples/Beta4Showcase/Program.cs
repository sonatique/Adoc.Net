// Standalone C# file to generate beta.4 showcase PDFs.
// Build and run from solution root:
//   dotnet build
//   dotnet run --project samples/Beta4Showcase

using AdocNet;
using AdocNet.Converters.Pdf;

var adocFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "beta4-showcase.adoc");
if (!File.Exists(adocFile))
{
    // Try relative to working directory
    adocFile = "samples/beta4-showcase.adoc";
}

var source = File.ReadAllText(adocFile);
var doc = Adoc.Parse(source).Document;
var outDir = Path.GetDirectoryName(adocFile) ?? "samples";

// ── 1. Default (no highlighting, no hyphenation — beta.3 compatible) ────
{
    var pdf = new PdfRenderer().RenderToBytes(doc);
    var path = Path.Combine(outDir, "beta4-default.pdf");
    File.WriteAllBytes(path, pdf);
    Console.WriteLine($"[1] Default (beta.3 compat): {path} ({pdf.Length:N0} bytes)");
}

// ── 2. Syntax highlighting enabled ──────────────────────────────────────
{
    var options = new PdfRenderOptions
    {
        SyntaxColors = SyntaxColorScheme.Default,
    };
    var pdf = new PdfRenderer().RenderToBytes(doc, options);
    var path = Path.Combine(outDir, "beta4-syntax-highlighting.pdf");
    File.WriteAllBytes(path, pdf);
    Console.WriteLine($"[2] Syntax highlighting: {path} ({pdf.Length:N0} bytes)");
}

// ── 3. Hyphenation enabled ──────────────────────────────────────────────
{
    var options = new PdfRenderOptions
    {
        EnableHyphenation = true,
    };
    var pdf = new PdfRenderer().RenderToBytes(doc, options);
    var path = Path.Combine(outDir, "beta4-hyphenation.pdf");
    File.WriteAllBytes(path, pdf);
    Console.WriteLine($"[3] Hyphenation: {path} ({pdf.Length:N0} bytes)");
}

// ── 4. Full styling: highlighting + hyphenation + colors + spacing ──────
{
    var options = new PdfRenderOptions
    {
        SyntaxColors = SyntaxColorScheme.Default,
        EnableHyphenation = true,
        HeadingColor = new PdfColor(0.15f, 0.15f, 0.6f),
        LinkColor = new PdfColor(0f, 0.4f, 0.7f),
        ParagraphSpacingBefore = 2f,
        ParagraphSpacingAfter = 10f,
        SectionSpacing = 20f,
        ShowPageNumbers = true,
        FooterText = "AdocNet beta.4 Showcase — Page {page} of {pages}",
    };
    var pdf = new PdfRenderer().RenderToBytes(doc, options);
    var path = Path.Combine(outDir, "beta4-full-styling.pdf");
    File.WriteAllBytes(path, pdf);
    Console.WriteLine($"[4] Full styling: {path} ({pdf.Length:N0} bytes)");
}

// ── 5. Compact preset + highlighting ────────────────────────────────────
{
    var options = new PdfRenderOptions
    {
        FontSize = 10f,
        LineSpacing = 1.25f,
        ParagraphSpacingAfter = 6f,
        MarginTop = 54f,
        MarginBottom = 54f,
        SectionSpacing = 12f,
        SyntaxColors = SyntaxColorScheme.Default,
        EnableHyphenation = true,
        ShowPageNumbers = true,
        FooterText = "Compact — Page {page}",
    };
    var pdf = new PdfRenderer().RenderToBytes(doc, options);
    var path = Path.Combine(outDir, "beta4-compact.pdf");
    File.WriteAllBytes(path, pdf);
    Console.WriteLine($"[5] Compact preset: {path} ({pdf.Length:N0} bytes)");
}

// ── 6. Presentation preset ──────────────────────────────────────────────
{
    var options = new PdfRenderOptions
    {
        TitleFontSize = 30f,
        FontSize = 14f,
        LineSpacing = 1.5f,
        HeadingColor = new PdfColor(0f, 0f, 0.6f),
        SectionSpacing = 24f,
        SyntaxColors = SyntaxColorScheme.Default,
    };
    var pdf = new PdfRenderer().RenderToBytes(doc, options);
    var path = Path.Combine(outDir, "beta4-presentation.pdf");
    File.WriteAllBytes(path, pdf);
    Console.WriteLine($"[6] Presentation preset: {path} ({pdf.Length:N0} bytes)");
}

// ── 7. With TrueType fonts (if available on Windows) ────────────────────
{
    var arialPath = @"C:\Windows\Fonts\arial.ttf";
    var arialBoldPath = @"C:\Windows\Fonts\arialbd.ttf";
    var arialItalicPath = @"C:\Windows\Fonts\ariali.ttf";
    var consolasPath = @"C:\Windows\Fonts\consola.ttf";

    if (File.Exists(arialPath) && File.Exists(consolasPath))
    {
        var options = new PdfRenderOptions
        {
            FontPath = arialPath,
            BoldFontPath = arialBoldPath,
            ItalicFontPath = arialItalicPath,
            MonoFontPath = consolasPath,
            SyntaxColors = SyntaxColorScheme.Default,
            EnableHyphenation = true,
            HeadingColor = new PdfColor(0.1f, 0.1f, 0.5f),
            ShowPageNumbers = true,
            FooterText = "Page {page} of {pages}",
        };
        var pdf = new PdfRenderer().RenderToBytes(doc, options);
        var path = Path.Combine(outDir, "beta4-truetype-fonts.pdf");
        File.WriteAllBytes(path, pdf);
        Console.WriteLine($"[7] TrueType fonts (Arial+Consolas): {path} ({pdf.Length:N0} bytes)");
    }
    else
    {
        Console.WriteLine("[7] TrueType fonts: SKIPPED (Windows fonts not found)");
    }
}

Console.WriteLine("\nDone! All PDFs written to samples/ directory.");

// ── Quick analysis: verify background rects per page ────────────────
Console.WriteLine("\n=== Background rect analysis ===");
foreach (var pdfPath in new[] { "beta4-full-styling.pdf", "beta4-syntax-highlighting.pdf" })
{
    var fullPath = Path.Combine(outDir, pdfPath);
    if (!File.Exists(fullPath)) continue;
    var pdfText = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(fullPath));
    int pageNum = 0, pos2 = 0;
    Console.Write($"{pdfPath}: ");
    while (pos2 < pdfText.Length)
    {
        int ss = pdfText.IndexOf("stream\n", pos2, StringComparison.Ordinal);
        if (ss < 0) break;
        ss += 7;
        int se = pdfText.IndexOf("\nendstream", ss, StringComparison.Ordinal);
        if (se < 0) break;
        var content = pdfText.Substring(ss, se - ss);
        pos2 = se + 1;
        if (!content.Contains("BT") || !content.Contains("ET")) continue;
        pageNum++;
        int rects = System.Text.RegularExpressions.Regex.Matches(content, @"re\s*\n\s*f\b").Count;
        Console.Write($"p{pageNum}={rects}rects ");
    }
    Console.WriteLine();
}
