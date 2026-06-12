using AdocNet;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Regression tests for description list rendering and include behavior,
/// plus feature tests for Q&amp;A/horizontal styles and include indent=.
/// </summary>
[TestFixture]
public class QandaAndIndentTests
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

    // ═════��════════════════════════════════════════════════════════════════════
    // Step 0 — Regression tests
    // ══��═════════════════════════��═════════════════════════��═══════════════════

    [Test]
    public void Regression_description_list_renders_as_dl()
    {
        var result = BlockParser.Parse("Term:: Description");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<dl>"));
        Assert.That(html, Does.Contain("<dt class=\"hdlist1\">Term</dt>"));
        Assert.That(html, Does.Contain("<dd>"));
        Assert.That(html, Does.Contain("<p>Description</p>"));
        Assert.That(html, Does.Contain("</dl>"));
    }

    [Test]
    public void Regression_include_lines_filter()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "five.adoc"), "L1\nL2\nL3\nL4\nL5");

        var text = "include::five.adoc[lines=2..4]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);
        Assert.That(result.Text, Is.EqualTo("L2\nL3\nL4"));
    }

    [Test]
    public void Regression_include_tag_filter()
    {
        var content = "before\n// tag::snippet[]\nTagged content.\n// end::snippet[]\nafter";
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "tagged.adoc"), content);

        var text = "include::tagged.adoc[tag=snippet]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);
        Assert.That(result.Text, Is.EqualTo("Tagged content."));
    }

    [Test]
    public void Regression_description_list_no_style()
    {
        var result = BlockParser.Parse("CPU:: The brain.");
        var dl = (DescriptionListNode)result.Document.Children[0];
        Assert.That(dl.Style, Is.Null);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Step 4 — Q&A and horizontal tests
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Qanda_style_set_on_ast()
    {
        var result = BlockParser.Parse("[qanda]\nWhat is CPU?:: The brain.\nWhat is RAM?:: Memory.");
        var dl = (DescriptionListNode)result.Document.Children[0];
        Assert.That(dl.Style, Is.EqualTo("qanda"));
    }

    [Test]
    public void Horizontal_style_set_on_ast()
    {
        var result = BlockParser.Parse("[horizontal]\nCPU:: The brain.\nRAM:: Memory.");
        var dl = (DescriptionListNode)result.Document.Children[0];
        Assert.That(dl.Style, Is.EqualTo("horizontal"));
    }

    [Test]
    public void Qanda_renders_as_ol()
    {
        var result = BlockParser.Parse("[qanda]\nWhat is CPU?:: The brain.\nWhat is RAM?:: Memory.");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<div class=\"qlist qanda\">"));
        Assert.That(html, Does.Contain("<ol>"));
        Assert.That(html, Does.Contain("<em>What is CPU?</em>"));
        Assert.That(html, Does.Contain("<p>The brain.</p>"));
    }

    [Test]
    public void Horizontal_renders_as_table()
    {
        var result = BlockParser.Parse("[horizontal]\nCPU:: The brain.\nRAM:: Memory.");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<div class=\"hdlist\">"));
        Assert.That(html, Does.Contain("<table>"));
        Assert.That(html, Does.Contain("<td class=\"hdlist1\">"));
        Assert.That(html, Does.Contain("<td class=\"hdlist2\">"));
    }

    [Test]
    public void Default_style_unchanged_dl()
    {
        var result = BlockParser.Parse("CPU:: The brain.");
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<dl>"));
        Assert.That(html, Does.Contain("<dt class=\"hdlist1\">"));
        Assert.That(html, Does.Not.Contain("<ol>"));
        Assert.That(html, Does.Not.Contain("<table>"));
    }

    [Test]
    public void Qanda_multiple_items()
    {
        var result = BlockParser.Parse("[qanda]\nQ1:: A1\nQ2:: A2\nQ3:: A3");
        var html = new HtmlRenderer().RenderToString(result.Document);
        // 3 list items
        Assert.That(System.Text.RegularExpressions.Regex.Matches(html, "<li>").Count, Is.EqualTo(3));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Step 6 — Include indent= tests
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Include_indent_prepends_spaces()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "code.adoc"), "line1\nline2\nline3");

        var text = "include::code.adoc[indent=4]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);
        Assert.That(result.Text, Is.EqualTo("    line1\n    line2\n    line3"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_indent_zero_removes_common_indent_preserving_relative()
    {
        // Asciidoctor indent=0 removes the COMMON leading indentation (here 2 spaces), not all
        // leading whitespace — so the relative indentation of nested lines is preserved.
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "indented.adoc"), "    line1\n      line2\n  line3");

        var text = "include::indented.adoc[indent=0]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);
        Assert.That(result.Text, Is.EqualTo("  line1\n    line2\nline3"));
        Assert.That(result.Diagnostics, Is.Empty);
    }

    [Test]
    public void Include_indent_preserves_nested_code_indentation()
    {
        // Real-world case: a uniformly-indented code region re-indented to 0 must keep its inner
        // structure (the body stays indented relative to the def), not flatten to the margin.
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "code.adoc"), "  def foo():\n      return 1");

        var text = "include::code.adoc[indent=0]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);
        Assert.That(result.Text, Is.EqualTo("def foo():\n    return 1"));
    }

    [Test]
    public void Include_indent_with_lines_filter()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "five.adoc"), "L1\nL2\nL3\nL4\nL5");

        var text = "include::five.adoc[indent=2,lines=1..3]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);
        Assert.That(result.Text, Is.EqualTo("  L1\n  L2\n  L3"));
    }

    [Test]
    public void Include_indent_with_tag_filter()
    {
        var content = "before\n// tag::main[]\nTagged line.\n// end::main[]\nafter";
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "tagged.adoc"), content);

        var text = "include::tagged.adoc[indent=4,tag=main]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);
        Assert.That(result.Text, Is.EqualTo("    Tagged line."));
    }

    [Test]
    public void Include_no_indent_unchanged()
    {
        var reader = new DictReader()
            .Add(Path.Combine(BaseDir, "plain.adoc"), "  indented\nnot indented");

        var text = "include::plain.adoc[]";
        var result = IncludeExpander.Expand(text, BaseDir, reader);
        Assert.That(result.Text, Is.EqualTo("  indented\nnot indented"));
    }
}
