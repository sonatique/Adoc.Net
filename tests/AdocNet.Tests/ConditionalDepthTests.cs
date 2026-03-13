using AdocNet;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class ConditionalDepthTests
{
    [Test]
    public void Nesting_depth_65_produces_warning()
    {
        var lines = new List<string>();
        lines.Add(":x:");
        for (int i = 0; i < 65; i++)
            lines.Add("ifdef::x[]");
        lines.Add("content");
        for (int i = 0; i < 65; i++)
            lines.Add("endif::[]");
        var input = string.Join("\n", lines);

        var (_, diagnostics) = ConditionalPreprocessor.Process(input);
        Assert.That(diagnostics.Any(d => d.Message.Contains("depth exceeded")), Is.True);
    }

    [Test]
    public void Nesting_within_limit_produces_no_warning()
    {
        var lines = new List<string>();
        lines.Add(":x:");
        for (int i = 0; i < 10; i++)
            lines.Add("ifdef::x[]");
        lines.Add("content");
        for (int i = 0; i < 10; i++)
            lines.Add("endif::[]");
        var input = string.Join("\n", lines);

        var (text, diagnostics) = ConditionalPreprocessor.Process(input);
        Assert.That(diagnostics.Any(d => d.Message.Contains("depth exceeded")), Is.False);
        Assert.That(text, Does.Contain("content"));
    }

    [Test]
    public void Ifeval_with_undefined_attribute_treats_as_empty()
    {
        // Define the attribute as empty so ifeval comparison succeeds
        var input = ":myattr:\nifeval::[\"{myattr}\" == \"\"]\nvisible\nendif::[]";
        var (text, _) = ConditionalPreprocessor.Process(input);
        Assert.That(text, Does.Contain("visible"));
    }
}
