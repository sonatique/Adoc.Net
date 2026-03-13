using AdocNet.LanguageServer;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;

namespace AdocNet.Tests.Lsp;

[TestFixture]
public class DiagnosticMappingTests
{
    [Test]
    public void MapDiagnostic_maps_error_severity()
    {
        var adocDiag = new AdocNet.Diagnostic(
            AdocNet.DiagnosticSeverity.Error, "Test error",
            new AdocNet.SourceRange(new(1, 1), new(1, 10)));

        var lspDiag = DiagnosticMapper.Map(adocDiag);

        Assert.That(lspDiag.Severity, Is.EqualTo(LspDiagnosticSeverity.Error));
        Assert.That(lspDiag.Message, Is.EqualTo("Test error"));
    }

    [Test]
    public void MapDiagnostic_maps_warning_severity()
    {
        var adocDiag = new AdocNet.Diagnostic(
            AdocNet.DiagnosticSeverity.Warning, "Test warning",
            new AdocNet.SourceRange(new(5, 3), new(5, 20)));

        var lspDiag = DiagnosticMapper.Map(adocDiag);

        Assert.That(lspDiag.Severity, Is.EqualTo(LspDiagnosticSeverity.Warning));
    }

    [Test]
    public void MapDiagnostic_converts_1based_to_0based_positions()
    {
        var adocDiag = new AdocNet.Diagnostic(
            AdocNet.DiagnosticSeverity.Error, "msg",
            new AdocNet.SourceRange(new(3, 5), new(3, 10)));

        var lspDiag = DiagnosticMapper.Map(adocDiag);

        Assert.That(lspDiag.Range.Start.Line, Is.EqualTo(2));
        Assert.That(lspDiag.Range.Start.Character, Is.EqualTo(4));
        Assert.That(lspDiag.Range.End.Line, Is.EqualTo(2));
        Assert.That(lspDiag.Range.End.Character, Is.EqualTo(9));
    }

    [Test]
    public void MapDiagnostic_handles_no_range()
    {
        var adocDiag = new AdocNet.Diagnostic(
            AdocNet.DiagnosticSeverity.Warning, "no range",
            AdocNet.SourceRange.None);

        var lspDiag = DiagnosticMapper.Map(adocDiag);

        Assert.That(lspDiag.Range.Start.Line, Is.EqualTo(0));
        Assert.That(lspDiag.Range.Start.Character, Is.EqualTo(0));
    }
}
