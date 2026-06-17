using AdocNet;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Tests for the <see cref="IncludeExpander"/> preprocessing step.
/// Uses a dictionary-backed <see cref="IIncludeReader"/> for isolation from the filesystem.
/// </summary>
[TestFixture]
public class IncludeExpanderTests
{
    private const string BaseDir = "/docs";

    /// <summary>Simple in-memory reader for test isolation.</summary>
    private sealed class DictReader : IIncludeReader
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public DictReader Add(string path, string content)
        {
            // Normalize to full path the same way the expander does.
            _files[Path.GetFullPath(path)] = content;
            return this;
        }

        public bool Exists(string path) => _files.ContainsKey(Path.GetFullPath(path));
        public string Read(string path) => _files[Path.GetFullPath(path)];
    }

    // ── Basic expansion ──────────────────────────────────────────────────────

    [Test]
    public void No_includes_returns_text_unchanged()
    {
        var text = "= Title\n\nHello world.";
        var result = IncludeExpander.Expand(text, BaseDir, new DictReader());

        Assert.That(result.Text, Is.EqualTo(text));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Simple_include_expands_file_content()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "chapter.adoc"), "== Chapter 1\n\nSome content.");

        var text = "= Book\n\ninclude::chapter.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("= Book\n\n== Chapter 1\n\nSome content."));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_in_middle_of_document()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "snippet.adoc"), "Included text.");

        var text = "Before.\n\ninclude::snippet.adoc[]\n\nAfter.";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("Before.\n\nIncluded text.\n\nAfter."));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Multiple_includes_in_one_document()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "a.adoc"), "Alpha.")
            .Add(Path.Combine(BaseDir, "b.adoc"), "Bravo.");

        var text = "include::a.adoc[]\n\ninclude::b.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("Alpha.\n\nBravo."));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_empty_file()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "empty.adoc"), "");

        var text = "Before.\n\ninclude::empty.adoc[]\n\nAfter.";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("Before.\n\n\n\nAfter."));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    // ── Relative path resolution ─────────────────────────────────────────────

    [Test]
    public void Include_resolves_relative_to_base_directory()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "sub", "part.adoc"), "Sub-content.");

        var text = "include::sub/part.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("Sub-content."));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    // ── Nested includes ──────────────────────────────────────────────────────

    [Test]
    public void Nested_includes_expand_recursively()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "outer.adoc"), "Outer start.\n\ninclude::inner.adoc[]\n\nOuter end.")
            .Add(Path.Combine(BaseDir, "inner.adoc"), "Inner content.");

        var text = "include::outer.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("Outer start.\n\nInner content.\n\nOuter end."));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Diamond_include_is_allowed()
    {
        // A includes B and C; both B and C include D. This is NOT circular.
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "b.adoc"), "B start.\n\ninclude::d.adoc[]\n\nB end.")
            .Add(Path.Combine(BaseDir, "c.adoc"), "C start.\n\ninclude::d.adoc[]\n\nC end.")
            .Add(Path.Combine(BaseDir, "d.adoc"), "Shared.");

        var text = "include::b.adoc[]\n\ninclude::c.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("B start.\n\nShared.\n\nB end.\n\nC start.\n\nShared.\n\nC end."));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    // ── Error cases ──────────────────────────────────────────────────────────

    [Test]
    public void Missing_file_produces_error_diagnostic()
    {
        var text = "include::missing.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, new DictReader());

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("not found"));
        // Directive left as-is in output.
        Assert.That(result.Text, Is.EqualTo("include::missing.adoc[]"));
    }

    [Test]
    public void Circular_include_produces_error_diagnostic()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "a.adoc"), "include::b.adoc[]")
            .Add(Path.Combine(BaseDir, "b.adoc"), "include::a.adoc[]");

        var text = "include::a.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("Circular")),
            Is.True);
    }

    [Test]
    public void Max_depth_exceeded_produces_error_diagnostic()
    {
        // Create a chain: depth0 includes depth1 includes depth2 ...
        var reader = new DictReader();
        for (int d = 0; d < 15; d++)
        {
            var content = d < 14
                ? $"include::depth{d + 1}.adoc[]"
                : "Leaf.";
            reader.Add(Path.Combine(BaseDir, $"depth{d}.adoc"), content);
        }

        var text = "include::depth0.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, reader, maxDepth: 5);

        Assert.That(result.Diagnostics.Any(d =>
            d.Severity == DiagnosticSeverity.Error && d.Message.Contains("depth")),
            Is.True);
    }

    // ── Unsupported forms ────────────────────────────────────────────────────

    [Test]
    public void Url_include_produces_warning_diagnostic()
    {
        var text = "include::https://example.com/doc.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, new DictReader());

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("URL"));
        // Directive left as-is.
        Assert.That(result.Text, Is.EqualTo("include::https://example.com/doc.adoc[]"));
    }

    [Test]
    public void Unsupported_attributes_in_brackets_produce_warning_but_still_include()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "tagged.adoc"), "Tagged content.");

        var text = "include::tagged.adoc[encoding=utf-8]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        // Content still included (unsupported attributes ignored).
        Assert.That(result.Text, Is.EqualTo("Tagged content."));
        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("attributes"));
    }

    [Test]
    public void Undefined_attribute_reference_in_path_produces_warning()
    {
        var text = "include::{snippets}/example.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, new DictReader());

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("Undefined attribute"));
        // Include line passed through as-is
        Assert.That(result.Text, Does.Contain("include::{snippets}/example.adoc[]"));
    }

    // ── Partial includes: lines= ──────────────────────────────────────────────

    [Test]
    public void Include_with_lines_single()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "five.adoc"), "L1\nL2\nL3\nL4\nL5");

        var text = "include::five.adoc[lines=3]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("L3"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_with_lines_range()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "five.adoc"), "L1\nL2\nL3\nL4\nL5");

        var text = "include::five.adoc[lines=2..4]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("L2\nL3\nL4"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_with_lines_multiple_ranges()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "five.adoc"), "L1\nL2\nL3\nL4\nL5");

        var text = "include::five.adoc[lines=\"1..2;4..5\"]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("L1\nL2\nL4\nL5"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_with_lines_out_of_range_ignored()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "two.adoc"), "L1\nL2");

        var text = "include::two.adoc[lines=5..10]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo(""));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    // ── Partial includes: tag=/tags= ──────────────────────────────────────────

    [Test]
    public void Include_with_tag_extracts_region()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "tagged.adoc"),
                "before\n// tag::snippet[]\nincluded line 1\nincluded line 2\n// end::snippet[]\nafter");

        var text = "include::tagged.adoc[tag=snippet]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("included line 1\nincluded line 2"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_with_tag_hash_comment_style()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "script.py"),
                "# tag::snippet[]\nprint('hello')\n# end::snippet[]");

        var text = "include::script.py[tag=snippet]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("print('hello')"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_with_multiple_tags()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "multi.adoc"),
                "// tag::a[]\nAAA\n// end::a[]\nmiddle\n// tag::b[]\nBBB\n// end::b[]");

        var text = "include::multi.adoc[tags=a;b]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("AAA\nBBB"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_with_negated_tag()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "neg.adoc"),
                "keep1\n// tag::remove[]\nremoved\n// end::remove[]\nkeep2");

        var text = "include::neg.adoc[tag=!remove]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("keep1\nkeep2"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_with_unknown_tag_warns_and_includes_all()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "plain.adoc"), "all content");

        var text = "include::plain.adoc[tag=nonexistent]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("all content"));
        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
    }

    [Test]
    public void Include_with_tag_and_lines_prefers_tag()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "both.adoc"),
                "// tag::snippet[]\ntagged\n// end::snippet[]\nafter");

        var text = "include::both.adoc[tag=snippet,lines=1]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("tagged"));
        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("lines"));
    }

    // ── Level offset ────────────────────────────────────────────────────────

    [Test]
    public void Include_with_leveloffset_plus_one()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "chapter.adoc"), "= Chapter Title\n\n== Section");

        var text = "include::chapter.adoc[leveloffset=+1]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("== Chapter Title\n\n=== Section"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_with_leveloffset_minus_one()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "deep.adoc"), "=== Deep Section");

        var text = "include::deep.adoc[leveloffset=-1]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("== Deep Section"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_with_leveloffset_clamps_at_minimum()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "title.adoc"), "= Title");

        var text = "include::title.adoc[leveloffset=-5]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("= Title"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_with_leveloffset_clamps_at_maximum()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "sect.adoc"), "== Section");

        var text = "include::sect.adoc[leveloffset=+10]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("====== Section"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Leveloffset_does_not_affect_non_heading_lines()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "mixed.adoc"), "== Heading\n\nParagraph text\n\n=== Sub");

        var text = "include::mixed.adoc[leveloffset=+1]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("=== Heading\n\nParagraph text\n\n==== Sub"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [Test]
    public void Include_directive_must_be_whole_line()
    {
        // Inline text containing include:: should NOT be expanded.
        var text = "See include::file.adoc[] for details.";
        var result = IncludeExpander.Expand(text, BaseDir, new DictReader());

        // The regex requires ^include:: so this should be a passthrough.
        Assert.That(result.Text, Is.EqualTo(text));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_with_trailing_whitespace()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "file.adoc"), "Content.");

        var text = "include::file.adoc[]   ";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Is.EqualTo("Content."));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Null_text_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            IncludeExpander.Expand(null!, BaseDir, new DictReader()));
    }

    // ── Attribute references in include paths ───────────────────────────────

    [Test]
    public void Include_expands_attribute_in_path()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "snippets", "code.adoc"), "included content");

        var text = ":snippetsdir: snippets\n\ninclude::{snippetsdir}/code.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);

        Assert.That(result.Text, Does.Contain("included content"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_uses_api_attributes_for_path_expansion()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "custom", "file.adoc"), "api content");

        var attrs = new Dictionary<string, string> { ["basedir"] = "custom" };
        var text = "include::{basedir}/file.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, reader, attributes: attrs);

        Assert.That(result.Text, Does.Contain("api content"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_with_undefined_attribute_emits_warning()
    {
        var text = "include::{undefined}/file.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, new DictReader());

        Assert.That(result.Diagnostics, Has.Count.GreaterThan(0));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        // The include line should be passed through as-is
        Assert.That(result.Text, Does.Contain("include::{undefined}/file.adoc[]"));
    }

    [Test]
    public void Api_attributes_take_precedence_over_document_attributes()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "api-path", "file.adoc"), "api wins");

        var attrs = new Dictionary<string, string> { ["mydir"] = "api-path" };
        var text = ":mydir: doc-path\n\ninclude::{mydir}/file.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, reader, attributes: attrs);

        Assert.That(result.Text, Does.Contain("api wins"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    // ── URL includes ────────────────────────────────────────────────────────

    [Test]
    public void Url_include_skipped_when_allow_uri_read_is_false()
    {
        var text = "include::https://example.com/doc.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, new DictReader(), IncludeExpander.DefaultMaxDepth, attributes: null, allowUriRead: false);

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("AllowUriRead"));
        // Directive left as-is.
        Assert.That(result.Text, Is.EqualTo("include::https://example.com/doc.adoc[]"));
    }

    [Test]
    public void Url_include_default_is_not_allowed()
    {
        var text = "include::https://example.com/doc.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, new DictReader());

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        // Directive left as-is.
        Assert.That(result.Text, Is.EqualTo("include::https://example.com/doc.adoc[]"));
    }

    [Test]
    public void Http_url_include_skipped_when_allow_uri_read_is_false()
    {
        var text = "include::http://example.com/doc.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, new DictReader(), IncludeExpander.DefaultMaxDepth, attributes: null, allowUriRead: false);

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(result.Text, Is.EqualTo("include::http://example.com/doc.adoc[]"));
    }

    [Test]
    public void IO_error_produces_diagnostic_instead_of_crash()
    {
        var reader = new ThrowingReader();
        var text = "include::explode.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, reader, IncludeExpander.DefaultMaxDepth, attributes: null, allowUriRead: false);

        Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(result.Diagnostics[0].Message, Does.Contain("Failed to read include file"));
        // Original directive preserved in output
        Assert.That(result.Text, Does.Contain("include::explode.adoc[]"));
    }

    [Test]
    public void ParseOptions_IncludeReader_wires_through_AdocParser()
    {
        var reader = new DictReader()
            .Add("/docs/main.adoc", "= Title\n\ninclude::part.adoc[]")
            .Add("/docs/part.adoc", "Included content.");

        var result = AdocParser.Parse("= Title\n\ninclude::part.adoc[]", new ParseOptions
        {
            SourceFilePath = "/docs/main.adoc",
            IncludeReader = reader,
        });

        var html = new AdocNet.Converters.Html.HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("Included content."));
    }

    [Test]
    public void ParseLineRanges_accepts_comma_and_semicolon_separators()
    {
        // A quoted lines= value may use ',' or ';' to separate ranges.
        Assert.That(IncludeExpander.ParseLineRanges("1..2,5..6"),
            Is.EqualTo(new[] { (1, 2), (5, 6) }));
        Assert.That(IncludeExpander.ParseLineRanges("1..2;5..6"),
            Is.EqualTo(new[] { (1, 2), (5, 6) }));
        Assert.That(IncludeExpander.ParseLineRanges("1,3..4,8"),
            Is.EqualTo(new[] { (1, 1), (3, 4), (8, 8) }));
    }

    // ── Line-origin provenance (issue #46) ───────────────────────────────

    private static ParseResult ParseWithIncludes(string main, DictReader reader, string mainPath = "/docs/main.adoc")
        => AdocParser.Parse(main, new ParseOptions
        {
            SourceFilePath = mainPath,
            IncludeReader = reader,
            SafeMode = SafeMode.Unsafe,
        });

    [Test]
    public void LineOrigins_maps_expanded_lines_back_to_origin_file_and_line()
    {
        // The issue's minimal repro: a single include::part.adoc[] (3 lines of
        // content) pushes "Last paragraph." from editor line 7 to AST line 9.
        var main =
            "= Title\n\nFirst paragraph.\n\ninclude::part.adoc[]\n\nLast paragraph.";
        var part = "Included line one.\n\nIncluded line two.";
        var reader = new DictReader()
            .Add("/docs/main.adoc", main)
            .Add("/docs/part.adoc", part);

        var result = ParseWithIncludes(main, reader);
        var partPath = Path.GetFullPath("/docs/part.adoc");

        // Expanded layout:
        //   1 "= Title"            -> main:1
        //   2 ""                   -> main:2
        //   3 "First paragraph."   -> main:3
        //   4 ""                   -> main:4
        //   5 "Included line one." -> part:1  (synthetic)
        //   6 ""                   -> part:2  (synthetic)
        //   7 "Included line two." -> part:3  (synthetic)
        //   8 ""                   -> main:6
        //   9 "Last paragraph."    -> main:7
        Assert.That(result.LineOrigins, Has.Count.EqualTo(9));

        Assert.That(result.TryGetLineOrigin(3, out var l3), Is.True);
        Assert.That(l3.SourceLine, Is.EqualTo(3));
        Assert.That(l3.IsSynthetic, Is.False);

        // AST line 9 -> editor line 7 of the primary document.
        Assert.That(result.TryGetLineOrigin(9, out var l9), Is.True);
        Assert.That(l9.SourceLine, Is.EqualTo(7));
        Assert.That(l9.IsSynthetic, Is.False);

        // AST lines 5-7 came from part.adoc.
        foreach (var (expandedLine, partLine) in new[] { (5, 1), (6, 2), (7, 3) })
        {
            Assert.That(result.TryGetLineOrigin(expandedLine, out var o), Is.True);
            Assert.That(o.IsSynthetic, Is.True, $"expanded line {expandedLine} should be from the include");
            Assert.That(o.SourceLine, Is.EqualTo(partLine));
            Assert.That(o.SourceFile, Is.EqualTo(partPath));
        }
    }

    [Test]
    public void LineOrigins_handles_lines_filtered_include()
    {
        // lines=2..3 pulls a non-contiguous slice; the origins must report the
        // ORIGINAL include line numbers (2 and 3), not 1 and 2.
        var main = "Intro.\n\ninclude::snip.adoc[lines=2..3]";
        var snip = "one\ntwo\nthree\nfour";
        var reader = new DictReader()
            .Add("/docs/main.adoc", main)
            .Add("/docs/snip.adoc", snip);

        var result = ParseWithIncludes(main, reader);

        // Expanded: 1 "Intro." (main:1), 2 "" (main:2), 3 "two" (snip:2), 4 "three" (snip:3)
        Assert.That(result.TryGetLineOrigin(3, out var o3), Is.True);
        Assert.That(o3.IsSynthetic, Is.True);
        Assert.That(o3.SourceLine, Is.EqualTo(2), "first included line is original line 2");

        Assert.That(result.TryGetLineOrigin(4, out var o4), Is.True);
        Assert.That(o4.SourceLine, Is.EqualTo(3), "second included line is original line 3");
    }

    [Test]
    public void LineOrigins_tracks_lines_through_tag_filtered_include()
    {
        // tag=middle selects only the tagged region; origins must point at the
        // tagged lines' real positions in the include file.
        var main = "include::tagged.adoc[tag=middle]";
        var snip =
            "before\n" +              // 1
            "// tag::middle[]\n" +     // 2
            "kept one\n" +            // 3
            "kept two\n" +            // 4
            "// end::middle[]\n" +     // 5
            "after";                   // 6
        var reader = new DictReader()
            .Add("/docs/main.adoc", main)
            .Add("/docs/tagged.adoc", snip);

        var result = ParseWithIncludes(main, reader);

        Assert.That(result.TryGetLineOrigin(1, out var o1), Is.True);
        Assert.That(o1.SourceLine, Is.EqualTo(3), "first kept line is original line 3");
        Assert.That(result.TryGetLineOrigin(2, out var o2), Is.True);
        Assert.That(o2.SourceLine, Is.EqualTo(4), "second kept line is original line 4");
    }

    [Test]
    public void LineOrigins_are_present_for_documents_without_includes()
    {
        // With no expansion, every line maps to itself in the primary source.
        var result = AdocParser.Parse("= Title\n\nA paragraph.", new ParseOptions
        {
            SourceFilePath = "/docs/solo.adoc",
        });

        Assert.That(result.LineOrigins, Has.Count.EqualTo(3));
        Assert.That(result.TryGetLineOrigin(3, out var o), Is.True);
        Assert.That(o.SourceLine, Is.EqualTo(3));
        Assert.That(o.IsSynthetic, Is.False);
        Assert.That(o.SourceFile, Is.EqualTo("/docs/solo.adoc"));
    }

    [Test]
    public void LineOrigins_account_for_conditional_filtering()
    {
        // The ifdef block and its directives are removed; surviving lines must
        // still report their ORIGINAL line numbers.
        //   1 "Start."
        //   2 ""
        //   3 "ifdef::flag[]"   (removed)
        //   4 "hidden"          (removed; flag not set)
        //   5 "endif::[]"       (removed)
        //   6 ""
        //   7 "End."
        var src = "Start.\n\nifdef::flag[]\nhidden\nendif::[]\n\nEnd.";
        var result = AdocParser.Parse(src, new ParseOptions { SourceFilePath = "/docs/c.adoc" });

        // "End." is the last surviving line; it must map back to original line 7.
        var last = result.LineOrigins[result.LineOrigins.Count - 1];
        Assert.That(last.SourceLine, Is.EqualTo(7));
        Assert.That(last.IsSynthetic, Is.False);
    }

    /// <summary>Reader that throws IOException on Read to simulate I/O failures.</summary>
    private sealed class ThrowingReader : IIncludeReader
    {
        public bool Exists(string path) => true;
        public string Read(string path) => throw new IOException("Simulated disk error");
    }
}
