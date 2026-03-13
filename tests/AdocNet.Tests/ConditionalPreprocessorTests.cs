using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Unit tests for <see cref="ConditionalPreprocessor"/> covering ifdef/ifndef/ifeval
/// directives, external attributes, and edge cases.
/// </summary>
[TestFixture]
public class ConditionalPreprocessorTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // ifeval with float comparisons
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Ifeval_float_greater_than()
    {
        var input = ":version: 2.5\n\nifeval::[\"{version}\" > \"1.0\"]\nIncluded.\nendif::[]";
        var (filtered, diagnostics) = ConditionalPreprocessor.Process(input);
        Assert.That(filtered, Does.Contain("Included."));
        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public void Ifeval_float_less_than()
    {
        var input = ":version: 0.5\n\nifeval::[\"{version}\" < \"1.0\"]\nIncluded.\nendif::[]";
        var (filtered, _) = ConditionalPreprocessor.Process(input);
        Assert.That(filtered, Does.Contain("Included."));
    }

    [Test]
    public void Ifeval_float_equality()
    {
        var input = ":pi: 3.14\n\nifeval::[\"{pi}\" == \"3.14\"]\nMatched.\nendif::[]";
        var (filtered, _) = ConditionalPreprocessor.Process(input);
        Assert.That(filtered, Does.Contain("Matched."));
    }

    [Test]
    public void Ifeval_float_inequality_excludes()
    {
        var input = ":val: 2.0\n\nifeval::[\"{val}\" > \"3.0\"]\nExcluded.\nendif::[]";
        var (filtered, _) = ConditionalPreprocessor.Process(input);
        Assert.That(filtered, Does.Not.Contain("Excluded."));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // External attributes
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void External_attributes_available_for_ifdef()
    {
        var input = "ifdef::backend[Backend content.]\n\nBody.";
        var external = new Dictionary<string, string> { ["backend"] = "html5" };
        var (filtered, _) = ConditionalPreprocessor.Process(input, external);
        Assert.That(filtered, Does.Contain("Backend content."));
    }

    [Test]
    public void External_attributes_locked_against_header_override()
    {
        var input = ":env: production\n\nifeval::[\"{env}\" == \"production\"]\nProd.\nendif::[]";
        var external = new Dictionary<string, string> { ["env"] = "development" };
        var (filtered, _) = ConditionalPreprocessor.Process(input, external);
        // External (API-provided) attributes are locked — header :env: production cannot override.
        // Matches Asciidoctor behavior where CLI/API attributes take precedence.
        Assert.That(filtered, Does.Not.Contain("Prod."));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Edge cases
    // ══════════════════════════════════════════════════════════════════════════

    [Test]
    public void Nested_conditionals()
    {
        var input = ":a:\n:b:\n\nifdef::a[]\nifdef::b[]\nBoth set.\nendif::[]\nendif::[]";
        var (filtered, _) = ConditionalPreprocessor.Process(input);
        Assert.That(filtered, Does.Contain("Both set."));
    }

    [Test]
    public void Unclosed_conditional_emits_warning()
    {
        var input = "ifdef::missing[]\nContent.";
        var (_, diagnostics) = ConditionalPreprocessor.Process(input);
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Message, Does.Contain("Unclosed"));
    }

    [Test]
    public void Orphan_endif_emits_warning()
    {
        var input = "endif::[]";
        var (_, diagnostics) = ConditionalPreprocessor.Process(input);
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].Message, Does.Contain("Unexpected endif"));
    }
}
