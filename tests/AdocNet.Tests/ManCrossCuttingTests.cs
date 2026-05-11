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

    // ── Backtick monospace renders as bold-monospace (\f(CB) ────────────────

    [Test]
    public void Backtick_monospace_renders_as_bold_courier()
    {
        var output = Render("= Test\n\nThe `name` is set.");
        // Bold-monospace combo: \f(CB...\fP — Courier Bold (the 'best of both
        // worlds' — semantically correct monospace + readable bold weight).
        Assert.That(output, Does.Contain("\\f(CB"));
        Assert.That(output, Does.Contain("name"));
    }

    // ── Tab expansion in source/literal blocks ────────────────────────────────

    [Test]
    public void Listing_block_expands_tabs_to_spaces()
    {
        // Asciidoctor parity: tabs in verbatim content expand to 8 spaces by default.
        var output = Render("= Test\n\n----\nfunc foo() {\n\treturn 42\n}\n----");
        Assert.That(output, Does.Not.Contain("\treturn"),
            "tab in verbatim block should be expanded to spaces");
        Assert.That(output, Does.Contain("        return"),
            "8-space expansion expected (default tabsize)");
    }

    // ── ASCII '-' escaped as \- in body content ───────────────────────────────

    [Test]
    public void Hyphen_minus_in_body_text_escaped_as_backslash_dash()
    {
        // Asciidoctor escapes ASCII '-' as \- so groff renders it as a literal
        // hyphen-minus rather than reflowing it.
        var output = Render("= Test\n\nUse the user-friendly tool.");
        Assert.That(output, Does.Contain("user\\-friendly"));
    }

    // ── Example block title gets numbered prefix ──────────────────────────────

    [Test]
    public void Example_block_title_gets_numbered_prefix()
    {
        var output = Render("= Test\n\n.My Example\n====\nbody.\n====");
        // Asciidoctor: ".B Example 1. My Example" (with .br + .RS).
        // We want a numbered prefix — 'Example N. <title>'.
        Assert.That(output, Does.Contain("Example 1. My Example"));
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
