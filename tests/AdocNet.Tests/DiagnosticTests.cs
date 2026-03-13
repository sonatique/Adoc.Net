using AdocNet;

namespace AdocNet.Tests;

[TestFixture]
public class DiagnosticTests
{
    [Test]
    public void ToString_includes_severity_range_and_message()
    {
        var diag = new Diagnostic(
            DiagnosticSeverity.Error,
            "Unexpected token",
            new SourceRange(new(3, 1), new(3, 5)));

        Assert.That(diag.ToString(), Is.EqualTo("Error at 3:1-3:5: Unexpected token"));
    }

    [Test]
    public void Diagnostic_with_none_range()
    {
        var diag = new Diagnostic(DiagnosticSeverity.Warning, "Something odd", SourceRange.None);
        Assert.That(diag.ToString(), Is.EqualTo("Warning at (none): Something odd"));
    }
}
