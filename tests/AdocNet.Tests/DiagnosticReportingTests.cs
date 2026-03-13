using AdocNet;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Tests for the structured diagnostics system: diagnostic creation,
/// file/line tracking, parser continuation after warnings, and CLI formatting.
/// </summary>
[TestFixture]
public class DiagnosticReportingTests
{
    // ── Diagnostic model ────────────────────────────────────────────────

    [Test]
    public void Diagnostic_is_immutable_record()
    {
        var diag = new Diagnostic(
            DiagnosticSeverity.Warning,
            "test message",
            new SourceRange(new(1, 1), new(1, 10)));

        Assert.That(diag.Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(diag.Message, Is.EqualTo("test message"));
        Assert.That(diag.Range.Start.Line, Is.EqualTo(1));
        Assert.That(diag.FilePath, Is.Null);
    }

    [Test]
    public void Diagnostic_with_FilePath()
    {
        var diag = new Diagnostic(
            DiagnosticSeverity.Error,
            "file not found",
            new SourceRange(new(5, 1), new(5, 20)))
        {
            FilePath = "chapter.adoc"
        };

        Assert.That(diag.FilePath, Is.EqualTo("chapter.adoc"));
        Assert.That(diag.IsError, Is.True);
        Assert.That(diag.IsWarning, Is.False);
    }

    [Test]
    public void Diagnostic_with_expression_creates_copy_with_FilePath()
    {
        var original = new Diagnostic(
            DiagnosticSeverity.Warning,
            "some warning",
            new SourceRange(new(3, 1), new(3, 10)));

        var withFile = original with { FilePath = "main.adoc" };

        Assert.That(withFile.FilePath, Is.EqualTo("main.adoc"));
        Assert.That(withFile.Message, Is.EqualTo("some warning"));
        Assert.That(original.FilePath, Is.Null); // original unchanged
    }

    [Test]
    public void Diagnostic_ToString_with_FilePath()
    {
        var diag = new Diagnostic(
            DiagnosticSeverity.Warning,
            "Unknown block macro 'video'",
            new SourceRange(new(12, 1), new(12, 30)))
        {
            FilePath = "file.adoc"
        };

        Assert.That(diag.ToString(), Is.EqualTo("Warning at file.adoc 12:1-12:30: Unknown block macro 'video'"));
    }

    [Test]
    public void Diagnostic_ToString_without_FilePath()
    {
        var diag = new Diagnostic(
            DiagnosticSeverity.Error,
            "Unclosed block",
            new SourceRange(new(3, 1), new(3, 4)));

        Assert.That(diag.ToString(), Is.EqualTo("Error at 3:1-3:4: Unclosed block"));
    }

    // ── ParseResult convenience properties ──────────────────────────────

    [Test]
    public void ParseResult_HasErrors_is_true_when_errors_present()
    {
        var doc = new DocumentNode();
        var diags = new List<Diagnostic>
        {
            new(DiagnosticSeverity.Error, "bad", SourceRange.None)
        };
        var result = new ParseResult(doc, diags);

        Assert.That(result.HasErrors, Is.True);
        Assert.That(result.HasWarnings, Is.False);
    }

    [Test]
    public void ParseResult_HasWarnings_is_true_when_warnings_present()
    {
        var doc = new DocumentNode();
        var diags = new List<Diagnostic>
        {
            new(DiagnosticSeverity.Warning, "hmm", SourceRange.None)
        };
        var result = new ParseResult(doc, diags);

        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.HasWarnings, Is.True);
    }

    [Test]
    public void ParseResult_no_diagnostics()
    {
        var doc = new DocumentNode();
        var result = new ParseResult(doc, []);

        Assert.That(result.HasErrors, Is.False);
        Assert.That(result.HasWarnings, Is.False);
    }

    // ── Unknown block macro diagnostic ──────────────────────────────────

    [Test]
    public void Unknown_block_macro_produces_warning()
    {
        var result = BlockParser.Parse("chart::sales-data.csv[Chart title]");

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("Unknown block macro"));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("chart"));
    }

    [Test]
    public void Unknown_block_macro_line_becomes_paragraph()
    {
        var result = BlockParser.Parse("chart::sales-data.csv[Chart title]");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Unknown_macro_has_correct_line_number()
    {
        var result = BlockParser.Parse("Some text.\n\nchart::sales-data.csv[alt]");

        var diag = result.Diagnostics.Single();
        Assert.That(diag.Range.Start.Line, Is.EqualTo(3));
    }

    [Test]
    public void Known_block_macro_produces_no_diagnostic()
    {
        var result = BlockParser.Parse("image::photo.png[Alt text]");

        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.Document.Children[0], Is.InstanceOf<BlockImageNode>());
    }

    // ── Malformed attribute diagnostic ──────────────────────────────────

    [Test]
    public void Malformed_attribute_produces_warning()
    {
        // :bad attr contains a space → truly malformed even with flag-style support
        var result = BlockParser.Parse("= Title\n:bad attr");

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("Malformed attribute"));
    }

    [Test]
    public void Malformed_attribute_has_correct_line_number()
    {
        // :no close contains a space → truly malformed even with flag-style support
        var result = BlockParser.Parse("= Title\n:no close");

        Assert.That(result.Diagnostics[0].Range.Start.Line, Is.EqualTo(2));
    }

    // ── Unclosed block diagnostics ──────────────────────────────────────

    [Test]
    public void Unclosed_listing_block_produces_warning_with_line()
    {
        var result = BlockParser.Parse("----\nsome code");

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("Unclosed delimited block"));
        Assert.That(result.Diagnostics[0].Range.Start.Line, Is.EqualTo(1));
    }

    [Test]
    public void Unclosed_quote_block_produces_warning()
    {
        var result = BlockParser.Parse("____\nquote text");

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("Unclosed delimited block"));
    }

    [Test]
    public void Unclosed_table_produces_warning()
    {
        var result = BlockParser.Parse("|===\n| cell");

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("Unclosed table"));
    }

    // ── Parser continues after warnings ─────────────────────────────────

    [Test]
    public void Unclosed_block_consumes_remaining_content_to_eof()
    {
        var result = BlockParser.Parse("----\nunclosed\n\nA normal paragraph.");

        Assert.That(result.Diagnostics, Has.Count.GreaterThanOrEqualTo(1));
        // Unclosed blocks now consume all remaining content to EOF (matching Asciidoctor).
        var blocks = result.Document.Children.OfType<DelimitedBlockNode>().ToList();
        Assert.That(blocks, Has.Count.EqualTo(1));
        Assert.That(blocks[0].Content, Does.Contain("unclosed"));
        Assert.That(blocks[0].Content, Does.Contain("A normal paragraph."));
    }

    [Test]
    public void Parser_continues_after_unknown_macro()
    {
        var result = BlockParser.Parse("chart::test[alt]\n\nA paragraph.");

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children, Has.Count.EqualTo(2));
        Assert.That(result.Document.Children[1], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Parser_continues_after_malformed_attribute()
    {
        // :bad attr contains a space → truly malformed even with flag-style support
        var result = BlockParser.Parse("= Title\n:bad attr\n\nContent.");

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        // :bad attr falls through to body as paragraph, then Content. is a second paragraph.
        Assert.That(result.Document.Children, Has.Count.EqualTo(2));
        Assert.That(result.Document.Children.All(c => c is ParagraphNode), Is.True);
    }

    // ── FilePath propagation via AdocParser ──────────────────────────────

    [Test]
    public void AdocParser_stamps_FilePath_on_diagnostics()
    {
        var options = new ParseOptions { SourceFilePath = "docs/main.adoc" };
        var result = AdocParser.Parse("----\nunclosed", options);

        Assert.That(result.Diagnostics, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(result.Diagnostics.All(d => d.FilePath == "docs/main.adoc"), Is.True);
    }

    [Test]
    public void AdocParser_without_options_leaves_FilePath_null()
    {
        var result = AdocParser.Parse("----\nunclosed");

        Assert.That(result.Diagnostics, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(result.Diagnostics.All(d => d.FilePath is null), Is.True);
    }

    // ── Include diagnostics via AdocParser ───────────────────────────────

    [Test]
    public void Include_file_not_found_produces_error()
    {
        var options = new ParseOptions
        {
            SourceFilePath = "test.adoc",
            BaseDirectory = Path.GetTempPath(),
        };
        var result = AdocParser.Parse("include::nonexistent.adoc[]", options);

        Assert.That(result.HasErrors, Is.True);
        Assert.That(result.Diagnostics.Any(d =>
            d.IsError && d.Message.Contains("not found")), Is.True);
    }

    // ── Deterministic output ────────────────────────────────────────────

    [Test]
    public void Diagnostics_are_ordered_by_occurrence()
    {
        // Use two distinct diagnostics: duplicate anchor IDs produce warnings in order.
        var result = BlockParser.Parse("[[dup]]\nFirst.\n\n[[dup]]\nSecond.\n\n[[dup]]\nThird.");

        Assert.That(result.Diagnostics, Has.Count.EqualTo(2));
        Assert.That(result.Diagnostics[0].Range.Start.Line,
            Is.LessThan(result.Diagnostics[1].Range.Start.Line));
    }

    // ── Duplicate anchor detection ──────────────────────────────────────

    [Test]
    public void Duplicate_anchor_id_produces_warning()
    {
        var result = BlockParser.Parse("[[dup-id]]\nFirst paragraph.\n\n[[dup-id]]\nSecond paragraph.");

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("Duplicate anchor ID"));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("dup-id"));
    }

    [Test]
    public void Duplicate_anchor_id_still_assigns_id_to_both_blocks()
    {
        var result = BlockParser.Parse("[[dup-id]]\nFirst paragraph.\n\n[[dup-id]]\nSecond paragraph.");

        var paragraphs = result.Document.Children.OfType<ParagraphNode>().ToList();
        Assert.That(paragraphs, Has.Count.EqualTo(2));
        Assert.That(paragraphs[0].Id, Is.EqualTo("dup-id"));
        Assert.That(paragraphs[1].Id, Is.EqualTo("dup-id"));
    }

    [Test]
    public void Unique_anchor_ids_produce_no_diagnostic()
    {
        var result = BlockParser.Parse("[[id-one]]\nFirst.\n\n[[id-two]]\nSecond.");

        Assert.That(result.Diagnostics, Is.Empty);
    }

    // ── Unknown inline macro → plain text ───────────────────────────────

    [Test]
    public void Unknown_macro_treated_as_plain_text()
    {
        var result = AdocParser.Parse("Some custom:[value] text.");
        var paragraph = result.Document.Children.OfType<ParagraphNode>().Single();
        // Should be plain text, not a macro node
        Assert.That(paragraph.Inlines.OfType<InlineMacroNode>(), Is.Empty);
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty);
    }

    // ── Table robustness ────────────────────────────────────────────────

    [Test]
    public void Table_with_invalid_content_does_not_crash()
    {
        var result = AdocParser.Parse("|===\n|normal\n|===");
        Assert.That(result.Document.Children, Has.Count.GreaterThan(0));
    }

    // ── Unclosed inline formatting → no crash ───────────────────────────

    [Test]
    public void Unclosed_inline_formatting_does_not_crash()
    {
        var result = AdocParser.Parse("This has *unclosed bold and _unclosed italic.");
        var paragraph = result.Document.Children.OfType<ParagraphNode>().Single();
        Assert.That(paragraph.Text, Is.Not.Empty);
        Assert.That(result.Diagnostics.Where(d => d.IsError), Is.Empty);
    }

    // ── Empty and whitespace documents ──────────────────────────────────

    [Test]
    public void Empty_document_produces_no_diagnostics()
    {
        var result = AdocParser.Parse("");
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Whitespace_only_document_produces_no_diagnostics()
    {
        var result = AdocParser.Parse("   \n  \n   ");
        Assert.That(result.Diagnostics, Is.Empty);
    }
}
