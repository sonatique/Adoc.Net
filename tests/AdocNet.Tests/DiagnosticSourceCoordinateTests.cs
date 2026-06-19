using AdocNet;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Tests for issue #67: diagnostic ranges (and therefore the CLI's file:line and
/// the LSP's squiggle position) are reported in original-source coordinates, not
/// post-include-expansion (AST) coordinates. A diagnostic that follows an
/// <c>include::</c> must report the source line the author edits, and a diagnostic
/// inside an included file must name that file via <see cref="Diagnostic.FilePath"/>.
/// </summary>
[TestFixture]
public class DiagnosticSourceCoordinateTests
{
    private const string BaseDir = "/docs";

    private sealed class DictReader : IIncludeReader
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
        public DictReader Add(string path, string content)
        {
            _files[Path.GetFullPath(path)] = content;
            return this;
        }
        public bool Exists(string path) => _files.ContainsKey(Path.GetFullPath(path));
        public string Read(string path) => _files[Path.GetFullPath(path)];
    }

    private static ParseResult Parse(string main, DictReader reader, string mainName = "main.adoc") =>
        AdocParser.Parse(main, new ParseOptions
        {
            BaseDirectory = BaseDir,
            SourceFilePath = Path.Combine(BaseDir, mainName),
            IncludeReader = reader,
            IncludeMaxDepth = 10,
        });

    [Test]
    public void Diagnostic_after_include_reports_source_line_not_expanded_line()
    {
        // inc.adoc is 10 lines, included at line 3 → shifts everything after it
        // down by 9. The malformed table row is at SOURCE line 8 of main.adoc.
        var inc = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line{i}"));
        var reader = new DictReader().Add(Path.Combine(BaseDir, "inc.adoc"), inc);
        const string main =
            "= T\n" +        // 1
            "\n" +           // 2
            "include::inc.adoc[]\n" + // 3
            "\n" +           // 4
            "|===\n" +       // 5
            "| A | B | C\n" + // 6
            "\n" +           // 7
            "| 1 | 2\n" +    // 8  ← malformed row
            "|===\n";        // 9

        var result = Parse(main, reader);

        var diag = result.Diagnostics.FirstOrDefault(d => d.Message.Contains("incomplete row"));
        Assert.That(diag, Is.Not.Null, "expected the incomplete-table-row diagnostic");

        // Source line 8 — NOT the post-expansion line 17 (8 + 9).
        Assert.That(diag!.Range.Start.Line, Is.EqualTo(8),
            "diagnostic should report the original source line, not the expanded line");
        // It belongs to the main document, so file:line points at main.adoc.
        Assert.That(diag.FilePath, Does.EndWith("main.adoc"));
    }

    [Test]
    public void Diagnostic_inside_include_names_the_included_file_and_its_line()
    {
        // The malformed row lives inside the included file, at inc.adoc line 4.
        const string inc =
            "|===\n" +        // 1
            "| A | B | C\n" + // 2
            "\n" +            // 3
            "| 1 | 2\n" +     // 4  ← malformed row (inside the include)
            "|===\n";         // 5
        var reader = new DictReader().Add(Path.Combine(BaseDir, "inc.adoc"), inc);
        const string main =
            "= T\n" +                  // 1
            "\n" +                     // 2
            "include::inc.adoc[]\n";   // 3

        var result = Parse(main, reader);

        var diag = result.Diagnostics.FirstOrDefault(d => d.Message.Contains("incomplete row"));
        Assert.That(diag, Is.Not.Null, "expected the incomplete-table-row diagnostic");
        Assert.That(diag!.Range.Start.Line, Is.EqualTo(4), "line within the included file");
        Assert.That(diag.FilePath, Does.EndWith("inc.adoc"),
            "FilePath should name the included file the diagnostic originates in");
    }

    [Test]
    public void ToSourceLine_maps_expanded_line_back_to_source()
    {
        var inc = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line{i}"));
        var reader = new DictReader().Add(Path.Combine(BaseDir, "inc.adoc"), inc);
        const string main = "= T\n\ninclude::inc.adoc[]\n\nAfter the include.\n";

        var result = Parse(main, reader);

        // "After the include." is source line 5; after expansion it is line 14
        // (5 + 9). ToSourceLine must invert that.
        Assert.That(result.ToSourceLine(14), Is.EqualTo(5));
        // Out-of-range input is returned unchanged.
        Assert.That(result.ToSourceLine(99999), Is.EqualTo(99999));
    }

    [Test]
    public void Without_includes_diagnostic_line_is_unchanged()
    {
        // Regression guard: with no include, source == expanded, so translation is
        // a no-op and the malformed row keeps its line.
        const string main =
            "= T\n" +        // 1
            "\n" +           // 2
            "|===\n" +       // 3
            "| A | B | C\n" + // 4
            "\n" +           // 5
            "| 1 | 2\n" +    // 6  ← malformed row
            "|===\n";        // 7

        var result = AdocParser.Parse(main);
        var diag = result.Diagnostics.FirstOrDefault(d => d.Message.Contains("incomplete row"));
        Assert.That(diag, Is.Not.Null);
        Assert.That(diag!.Range.Start.Line, Is.EqualTo(6));
    }
}
