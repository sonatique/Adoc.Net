using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.Man;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class ManCrossCuttingTests
{
    private static string Render(string adoc, string? sourcePath = null)
    {
        var doc = sourcePath is not null
            ? AdocParser.Parse(adoc, new ParseOptions { SourceFilePath = sourcePath }).Document
            : BlockParser.Parse(adoc).Document;
        using var ms = new MemoryStream();
        new ManRenderer().Render(doc, ms, RenderOptions.Default);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ── 1. '\" t preprocessor directive at file head ─────────────────────────

    [Test]
    public void Output_starts_with_tbl_preprocessor_directive()
    {
        var output = Render("= Test\n\nbody.");
        Assert.That(output, Does.StartWith("'\\\" t\n"));
    }

    // ── 2. .TH name from docname (file basename) ─────────────────────────────

    [Test]
    public void Th_name_from_docname_when_available()
    {
        var output = Render("= Long Document Title\n\nbody.", sourcePath: "/path/my-doc.adoc");
        // docname = my-doc → uppercased + escaped hyphens → MY\-DOC
        Assert.That(output, Does.Contain(".TH \"MY\\-DOC\""));
    }

    [Test]
    public void Th_name_falls_back_to_doctitle_without_source_path()
    {
        var output = Render("= My Title\n\nbody.");
        Assert.That(output, Does.Contain(".TH \"MY TITLE\""));
    }

    // ── 3. .TH source/manual default to "\ \&" (no-break-space + zero-width) ──

    [Test]
    public void Th_source_and_manual_default_to_nbsp_zwsp_idiom()
    {
        var output = Render("= Test\n\nbody.");
        // The "\ \&" idiom keeps roff layout consistent. Asciidoctor always emits this
        // for empty source and manual fields rather than empty quoted strings.
        Assert.That(output, Does.Contain("\"\\ \\&\" \"\\ \\&\""));
    }

    // ── 4. Paragraph macro: .sp not .PP ──────────────────────────────────────

    [Test]
    public void Paragraph_emits_sp_not_PP()
    {
        var output = Render("= Test\n\nA paragraph.");
        // After .TH preamble, paragraph break should be .sp
        Assert.That(output, Does.Contain(".sp\nA paragraph."));
        Assert.That(output, Does.Not.Contain(".PP\nA paragraph."));
    }

    // ── 5. Smart quotes: typographic apostrophe → \(cq ───────────────────────

    [Test]
    public void Right_single_quote_becomes_cq_escape()
    {
        var output = Render("= Test\n\nIt's working.");
        // Asciidoctor smart-quotes the apostrophe in "It's" then renders it in roff as \(cq
        Assert.That(output, Does.Contain("It\\(cqs working"));
    }
}
