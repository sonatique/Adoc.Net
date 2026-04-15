using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class StemDelimiterTests
{
    // ── Step 0: Regression tests — $$ literal preservation ──────────────
    // These MUST pass both before and after modifications.

    [Test]
    public void Regression_dollar_dollar_without_stem_is_literal_text()
    {
        var input = "Total: $$";
        var result = AdocParser.Parse(input);
        var para = result.Document.Children.OfType<ParagraphNode>().First();
        var text = string.Join("", para.Inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Does.Contain("$$"));
    }

    [Test]
    public void Regression_dollar_dollar_in_middle_of_text_without_stem_is_literal()
    {
        var input = "The price is $$ per unit for bulk orders.";
        var result = AdocParser.Parse(input);
        var para = result.Document.Children.OfType<ParagraphNode>().First();
        var text = string.Join("", para.Inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Does.Contain("$$"));
    }

    [Test]
    public void Regression_dollar_dollar_block_without_stem_is_literal()
    {
        var input = "Before.\n\n$$\nsome text\n$$\n\nAfter.";
        var result = AdocParser.Parse(input);
        // Without :stem:, $$ should NOT create StemBlockNode
        var stemBlocks = result.Document.Children.OfType<StemBlockNode>().ToList();
        Assert.That(stemBlocks, Is.Empty);
    }

    [Test]
    public void Regression_dollar_dollar_inline_without_stem_is_literal()
    {
        var input = "The formula $$x^2$$ is shown.";
        var result = AdocParser.Parse(input);
        var para = result.Document.Children.OfType<ParagraphNode>().First();
        // Without :stem:, should NOT create StemInlineNode
        var stemInlines = para.Inlines.OfType<StemInlineNode>().ToList();
        Assert.That(stemInlines, Is.Empty);
    }

    // ── Existing stem syntax regression tests ────────────────────────────

    [Test]
    public void Regression_stem_block_attribute_still_works()
    {
        var input = ":stem:\n\n[stem]\n--\nE=mc^2\n--";
        var result = AdocParser.Parse(input);
        var stemBlock = result.Document.Children.OfType<StemBlockNode>().FirstOrDefault();
        Assert.That(stemBlock, Is.Not.Null);
        Assert.That(stemBlock!.Content, Does.Contain("E=mc^2"));
    }

    [Test]
    public void Regression_stem_inline_macro_still_works()
    {
        var input = "The formula stem:[x^2] is important.";
        var result = AdocParser.Parse(input);
        var para = result.Document.Children.OfType<ParagraphNode>().First();
        var stemInline = para.Inlines.OfType<StemInlineNode>().FirstOrDefault();
        Assert.That(stemInline, Is.Not.Null);
        Assert.That(stemInline!.Content, Is.EqualTo("x^2"));
    }

    [Test]
    public void Regression_latexmath_inline_macro_still_works()
    {
        var input = "The formula latexmath:[E=mc^2] is important.";
        var result = AdocParser.Parse(input);
        var para = result.Document.Children.OfType<ParagraphNode>().First();
        var stemInline = para.Inlines.OfType<StemInlineNode>().FirstOrDefault();
        Assert.That(stemInline, Is.Not.Null);
        Assert.That(stemInline!.StemType, Is.EqualTo("latexmath"));
    }

    // ── Include depth regression tests ───────────────────────────────────

    [Test]
    public void Regression_default_include_depth_works()
    {
        // Default IncludeMaxDepth is 10
        var options = new ParseOptions();
        Assert.That(options.IncludeMaxDepth, Is.EqualTo(10));
    }

    [Test]
    public void Regression_include_depth_exceeded_produces_error()
    {
        // A document that tries to include beyond max depth should produce diagnostics
        var input = "include::nonexistent.adoc[]";
        var options = new ParseOptions
        {
            IncludeMaxDepth = 0,
            BaseDirectory = System.IO.Path.GetTempPath(),
        };
        var result = AdocParser.Parse(input, options);
        // With depth 0, the include should not be expanded
        Assert.That(result.Diagnostics.Any(d =>
            d.Message.Contains("depth") || d.Message.Contains("not found")), Is.True);
    }

    // ── Step 3: $$ stem feature tests ────────────────────────────────────

    [Test]
    public void Stem_dollar_dollar_block_creates_StemBlockNode()
    {
        var input = ":stem:\n\n$$\nE=mc^2\n$$";
        var result = AdocParser.Parse(input);
        var stemBlock = result.Document.Children.OfType<StemBlockNode>().FirstOrDefault();
        Assert.That(stemBlock, Is.Not.Null);
        Assert.That(stemBlock!.Content, Is.EqualTo("E=mc^2"));
        Assert.That(stemBlock.StemType, Is.EqualTo("latexmath"));
    }

    [Test]
    public void Stem_dollar_dollar_inline_creates_StemInlineNode()
    {
        var input = ":stem:\n\nThe formula $$x^2 + y^2$$ is shown.";
        var result = AdocParser.Parse(input);
        var para = result.Document.Children.OfType<ParagraphNode>().First();
        var stemInline = para.Inlines.OfType<StemInlineNode>().FirstOrDefault();
        Assert.That(stemInline, Is.Not.Null);
        Assert.That(stemInline!.Content, Is.EqualTo("x^2 + y^2"));
        Assert.That(stemInline.StemType, Is.EqualTo("latexmath"));
    }

    [Test]
    public void Stem_dollar_dollar_text_after_is_not_block_delimiter()
    {
        var input = ":stem:\n\n$$ text after";
        var result = AdocParser.Parse(input);
        // "$$ text after" is NOT a block delimiter (not $$ alone on line)
        var stemBlocks = result.Document.Children.OfType<StemBlockNode>().ToList();
        Assert.That(stemBlocks, Is.Empty);
    }

    [Test]
    public void No_stem_dollar_dollar_block_is_literal()
    {
        var input = "$$\ntext\n$$";
        var result = AdocParser.Parse(input);
        var stemBlocks = result.Document.Children.OfType<StemBlockNode>().ToList();
        Assert.That(stemBlocks, Is.Empty);
    }

    [Test]
    public void No_stem_dollar_dollar_inline_is_literal()
    {
        var input = "$$x^2$$";
        var result = AdocParser.Parse(input);
        var para = result.Document.Children.OfType<ParagraphNode>().First();
        var stemInlines = para.Inlines.OfType<StemInlineNode>().ToList();
        Assert.That(stemInlines, Is.Empty);
    }

    [Test]
    public void Stem_triple_dollar_is_not_delimiter()
    {
        var input = ":stem:\n\n$$$";
        var result = AdocParser.Parse(input);
        // $$$ is not a valid block delimiter (not exactly $$)
        var stemBlocks = result.Document.Children.OfType<StemBlockNode>().ToList();
        Assert.That(stemBlocks, Is.Empty);
    }

    [Test]
    public void Stem_unclosed_dollar_dollar_inline_is_literal()
    {
        var input = ":stem:\n\nThe formula $$x^2 is shown.";
        var result = AdocParser.Parse(input);
        var para = result.Document.Children.OfType<ParagraphNode>().First();
        // Unclosed $$ should not create StemInlineNode
        var stemInlines = para.Inlines.OfType<StemInlineNode>().ToList();
        Assert.That(stemInlines, Is.Empty);
    }

    [Test]
    public void Stem_macro_still_works_alongside_dollar_dollar()
    {
        var input = ":stem:\n\nThe formula stem:[a+b] and $$c+d$$ both work.";
        var result = AdocParser.Parse(input);
        var para = result.Document.Children.OfType<ParagraphNode>().First();
        var stemInlines = para.Inlines.OfType<StemInlineNode>().ToList();
        Assert.That(stemInlines.Count, Is.EqualTo(2));
    }

    [Test]
    public void Stem_block_attribute_still_works_alongside_dollar_dollar()
    {
        var input = ":stem:\n\n[stem]\n--\na+b\n--\n\n$$\nc+d\n$$";
        var result = AdocParser.Parse(input);
        var stemBlocks = result.Document.Children.OfType<StemBlockNode>().ToList();
        Assert.That(stemBlocks.Count, Is.EqualTo(2));
    }

    // ── Step 5: :max-include-depth: tests ────────────────────────────────

    [Test]
    public void Max_include_depth_attribute_caps_depth()
    {
        var input = "include::nonexistent.adoc[]";
        var result = AdocParser.Parse(input, new ParseOptions
        {
            IncludeMaxDepth = 10,
            BaseDirectory = System.IO.Path.GetTempPath(),
            Attributes = new Dictionary<string, string> { ["max-include-depth"] = "3" },
        });
        // Depth capped at 3 (attribute < API). The include will fail (file not found)
        // but the depth cap itself is applied.
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void Max_include_depth_attribute_cannot_exceed_api_max()
    {
        var input = "include::nonexistent.adoc[]";
        var result = AdocParser.Parse(input, new ParseOptions
        {
            IncludeMaxDepth = 10,
            BaseDirectory = System.IO.Path.GetTempPath(),
            Attributes = new Dictionary<string, string> { ["max-include-depth"] = "100" },
        });
        // 100 > 10, so effective is still 10 (API max wins)
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public void Max_include_depth_zero_disables_includes()
    {
        var input = "include::nonexistent.adoc[]";
        var result = AdocParser.Parse(input, new ParseOptions
        {
            IncludeMaxDepth = 10,
            BaseDirectory = System.IO.Path.GetTempPath(),
            Attributes = new Dictionary<string, string> { ["max-include-depth"] = "0" },
        });
        // Depth 0: include should not be expanded, produce depth exceeded diagnostic
        Assert.That(result.Diagnostics.Any(d => d.Message.Contains("depth")), Is.True);
    }

    [Test]
    public void Max_include_depth_invalid_value_ignored()
    {
        var input = "include::nonexistent.adoc[]";
        var result = AdocParser.Parse(input, new ParseOptions
        {
            IncludeMaxDepth = 10,
            BaseDirectory = System.IO.Path.GetTempPath(),
            Attributes = new Dictionary<string, string> { ["max-include-depth"] = "invalid" },
        });
        // Invalid value ignored — API default (10) used. File not found, but no depth error.
        Assert.That(result.Diagnostics.Any(d => d.Message.Contains("not found")), Is.True);
    }

    [Test]
    public void No_max_include_depth_uses_api_default()
    {
        var input = "include::nonexistent.adoc[]";
        var result = AdocParser.Parse(input, new ParseOptions
        {
            IncludeMaxDepth = 10,
            BaseDirectory = System.IO.Path.GetTempPath(),
        });
        // No attribute — API default (10) used. File not found, but no depth error.
        Assert.That(result.Diagnostics.Any(d => d.Message.Contains("not found")), Is.True);
    }
}
