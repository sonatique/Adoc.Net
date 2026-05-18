using System.IO;
using AdocNet.Ast;
using AdocNet.Emitter;
using AdocNet.Parser;

namespace AdocNet.Emitter.Tests;

/// <summary>
/// Diagnostic-only test that dumps every failing fixture's original vs emitted
/// AST side-by-side so the from-AST punch list can be triaged. Marked
/// [Explicit] so it doesn't fire in normal runs.
/// </summary>
[TestFixture]
public class Diagnostics
{
    private static readonly AsciidocEmitter Emitter = new();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        return dir!.FullName;
    }

    [Test, Explicit]
    public void Dump_failing_fixtures()
    {
        var root = FindRepoRoot();
        var fixtures = new[]
        {
            "spec/conformance/user-manual.adoc",
            "spec/conformance/spring-security-auth.adoc",
            "spec/conformance/quarkus-getting-started.adoc",
            "spec/conformance/api-reference.adoc",
        };
        foreach (var rel in fixtures)
        {
            var path = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) continue;
            var source = File.ReadAllText(path);
            var original = AdocParser.Parse(source).Document;
            var emitted = Emitter.Emit(original);
            var reparsed = AdocParser.Parse(emitted).Document;

            TestContext.WriteLine($"--- {rel} ---");
            var origDump = AstPrettyPrinter.Print(original, includeSourceRanges: false);
            var reDump = AstPrettyPrinter.Print(reparsed, includeSourceRanges: false);
            ShowFirstDiff(origDump, reDump);
            TestContext.WriteLine();
        }
    }

    private static void ShowFirstDiff(string a, string b)
    {
        var aLines = a.Split('\n');
        var bLines = b.Split('\n');
        for (int i = 0; i < Math.Min(aLines.Length, bLines.Length); i++)
        {
            if (aLines[i] != bLines[i])
            {
                int start = Math.Max(0, i - 2);
                int end = Math.Min(Math.Max(aLines.Length, bLines.Length), i + 6);
                TestContext.WriteLine($"First diff at AST line {i + 1}:");
                TestContext.WriteLine("--- original ---");
                for (int j = start; j < Math.Min(end, aLines.Length); j++)
                    TestContext.WriteLine((j == i ? "* " : "  ") + aLines[j]);
                TestContext.WriteLine("--- reparsed ---");
                for (int j = start; j < Math.Min(end, bLines.Length); j++)
                    TestContext.WriteLine((j == i ? "* " : "  ") + bLines[j]);
                return;
            }
        }
        if (aLines.Length != bLines.Length)
            TestContext.WriteLine($"Common prefix matches; one is longer: orig={aLines.Length}, reparsed={bLines.Length}");
    }
}
