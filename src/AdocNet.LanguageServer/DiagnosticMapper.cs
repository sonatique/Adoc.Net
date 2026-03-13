using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using LspDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspPosition = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace AdocNet.LanguageServer;

internal static class DiagnosticMapper
{
    public static LspDiagnostic Map(Diagnostic diagnostic)
    {
        var range = MapRange(diagnostic.Range);
        return new LspDiagnostic
        {
            Range = range,
            Severity = MapSeverity(diagnostic.Severity),
            Message = diagnostic.Message,
            Source = "adocnet",
        };
    }

    private static LspRange MapRange(SourceRange source)
    {
        if (source.IsNone)
        {
            var zero = new LspPosition(0, 0);
            return new LspRange(zero, zero);
        }

        var start = new LspPosition(source.Start.Line - 1, source.Start.Column - 1);
        var end = new LspPosition(source.End.Line - 1, source.End.Column - 1);
        return new LspRange(start, end);
    }

    private static LspDiagnosticSeverity MapSeverity(DiagnosticSeverity severity) =>
        severity switch
        {
            DiagnosticSeverity.Error => LspDiagnosticSeverity.Error,
            DiagnosticSeverity.Warning => LspDiagnosticSeverity.Warning,
            _ => LspDiagnosticSeverity.Information,
        };
}
