using AdocNet;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Tests for the public API surface: AdocParser, HtmlRenderer, ParseOptions,
/// RenderOptions, Diagnostic helpers, and the canonical entry points.
/// </summary>
[TestFixture]
public class PublicApiTests
{
    // ── AdocParser.Parse(string) ──────────────────────────────────────────────

    [Test]
    public void Parse_simple_string_returns_document()
    {
        var result = AdocParser.Parse("= Title\n\nHello world");

        Assert.That(result.Document, Is.Not.Null);
        Assert.That(result.Document.Title, Is.EqualTo("Title"));
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Parse_empty_string_returns_empty_document()
    {
        var result = AdocParser.Parse("");

        Assert.That(result.Document, Is.Not.Null);
        Assert.That(result.Document.Title, Is.Null);
        Assert.That(result.Document.Children, Is.Empty);
    }

    [Test]
    public void Parse_null_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AdocParser.Parse(null!));
    }

    [Test]
    public void Parse_with_null_options_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AdocParser.Parse("text", null!));
    }

    // ── AdocParser.Parse(string, ParseOptions) ───────────────────────────────

    [Test]
    public void Parse_with_default_options_works()
    {
        var result = AdocParser.Parse("Hello", ParseOptions.Default);

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_with_source_file_path_enables_include_base_dir()
    {
        // When SourceFilePath is set, includes would resolve relative to it.
        // No actual file to include here, so just confirm it doesn't throw.
        var options = new ParseOptions { SourceFilePath = "test.adoc" };
        var result = AdocParser.Parse("Some text", options);

        Assert.That(result.Document, Is.Not.Null);
    }

    [Test]
    public void Parse_with_explicit_base_directory()
    {
        var options = new ParseOptions { BaseDirectory = "." };
        var result = AdocParser.Parse("Some text", options);

        Assert.That(result.Document, Is.Not.Null);
    }

    [Test]
    public void Parse_with_expand_includes_false_skips_expansion()
    {
        var options = new ParseOptions
        {
            BaseDirectory = ".",
            ExpandIncludes = false,
        };
        var result = AdocParser.Parse("include::nonexistent.adoc[]", options);

        // When includes are disabled, the directive becomes paragraph text.
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
    }

    // ── ParseOptions defaults ─────────────────────────────────────────────────

    [Test]
    public void ParseOptions_default_has_max_depth_10()
    {
        Assert.That(ParseOptions.Default.IncludeMaxDepth, Is.EqualTo(10));
    }

    [Test]
    public void ParseOptions_default_does_not_expand_includes()
    {
        // Without SourceFilePath or BaseDirectory, includes are not expanded.
        var result = AdocParser.Parse("include::missing.adoc[]");
        Assert.That(result.Diagnostics, Is.Empty);
    }

    // ── HtmlRenderer ──────────────────────────────────────────────────────────

    [Test]
    public void Render_document_produces_html()
    {
        var result = AdocParser.Parse("= Title\n\n*bold* text");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<h1>Title</h1>"));
        Assert.That(html, Does.Contain("<strong>bold</strong>"));
    }

    [Test]
    public void Render_with_options_produces_same_html()
    {
        var result = AdocParser.Parse("Hello");
        var html1 = new HtmlRenderer().RenderToString(result.Document);
        var html2 = new HtmlRenderer().RenderToString(result.Document, RenderOptions.Default);

        Assert.That(html1, Is.EqualTo(html2));
    }

    [Test]
    public void Render_null_document_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HtmlRenderer().RenderToString(null!));
    }

    [Test]
    public void Render_null_options_uses_default()
    {
        var doc = new DocumentNode();
        var html = new HtmlRenderer().RenderToString(doc, null!);
        Assert.That(html, Is.Not.Null);
    }

    // ── Diagnostic helpers ────────────────────────────────────────────────────

    [Test]
    public void Diagnostic_IsError_returns_true_for_error()
    {
        var diag = new Diagnostic(DiagnosticSeverity.Error, "test", SourceRange.None);
        Assert.That(diag.IsError, Is.True);
        Assert.That(diag.IsWarning, Is.False);
    }

    [Test]
    public void Diagnostic_IsWarning_returns_true_for_warning()
    {
        var diag = new Diagnostic(DiagnosticSeverity.Warning, "test", SourceRange.None);
        Assert.That(diag.IsWarning, Is.True);
        Assert.That(diag.IsError, Is.False);
    }

    [Test]
    public void Diagnostic_FilePath_included_in_ToString()
    {
        var diag = new Diagnostic(DiagnosticSeverity.Error, "not found", SourceRange.None)
        {
            FilePath = "chapter.adoc",
        };
        Assert.That(diag.ToString(), Does.Contain("chapter.adoc"));
    }

    [Test]
    public void Diagnostic_without_FilePath_ToString_has_no_path()
    {
        var diag = new Diagnostic(DiagnosticSeverity.Info, "note", SourceRange.None);
        Assert.That(diag.FilePath, Is.Null);
        Assert.That(diag.ToString(), Does.Not.Contain("null"));
    }

    // ── ParseResult structure ─────────────────────────────────────────────────

    [Test]
    public void ParseResult_exposes_Document_and_Diagnostics()
    {
        var result = AdocParser.Parse("= Title\n\nParagraph");

        Assert.That(result.Document, Is.InstanceOf<DocumentNode>());
        Assert.That(result.Diagnostics, Is.InstanceOf<IReadOnlyList<Diagnostic>>());
    }

    // ── Unclosed block produces diagnostic ────────────────────────────────────

    [Test]
    public void Unclosed_block_produces_warning_diagnostic()
    {
        var result = AdocParser.Parse("----\ncode\n");

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].IsWarning, Is.True);
    }

    // ── RenderOptions placeholder ─────────────────────────────────────────────

    [Test]
    public void RenderOptions_Default_is_not_null()
    {
        Assert.That(RenderOptions.Default, Is.Not.Null);
    }

    // ── End-to-end parse + render ─────────────────────────────────────────────

    [Test]
    public void End_to_end_parse_and_render()
    {
        var source = """
            = Sample
            :version: 1.0

            Version is {version}.

            == Features

            * *Bold* items
            * _Italic_ items
            * `Code` items

            [source,csharp]
            ----
            var x = 42;
            ----

            |===
            | Name | Value
            | Alpha | One
            |===
            """;

        var result = AdocParser.Parse(source);
        Assert.That(result.Diagnostics.Any(d => d.IsError), Is.False);

        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<h1>Sample</h1>"));
        Assert.That(html, Does.Contain("Version is 1.0."));
        Assert.That(html, Does.Contain("<strong>Bold</strong>"));
        Assert.That(html, Does.Contain("<em>Italic</em>"));
        Assert.That(html, Does.Contain("<code>Code</code>"));
        Assert.That(html, Does.Contain("language-csharp"));
        Assert.That(html, Does.Contain("<table class=\"frame-all grid-all stretch tableblock\">"));
    }

    // ── SubstitutionKind is public ────────────────────────────────────────────

    [Test]
    public void SubstitutionKind_Normal_includes_all_flags()
    {
        Assert.That(SubstitutionKind.Normal,
            Is.EqualTo(SubstitutionKind.SpecialCharacters | SubstitutionKind.Quotes | SubstitutionKind.Attributes
                | SubstitutionKind.Replacements | SubstitutionKind.Macros | SubstitutionKind.PostReplacements));
    }

    [Test]
    public void SubstitutionKind_Verbatim_is_SpecialCharacters()
    {
        Assert.That(SubstitutionKind.Verbatim, Is.EqualTo(SubstitutionKind.SpecialCharacters));
    }
}
