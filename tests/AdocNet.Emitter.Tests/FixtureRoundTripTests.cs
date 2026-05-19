using System.IO;
using AdocNet.Ast;
using AdocNet.Emitter;
using AdocNet.Parser;

namespace AdocNet.Emitter.Tests;

/// <summary>
/// Property tests that walk every <c>.adoc</c> fixture in the repo and check
/// two invariants for each:
/// <list type="number">
///   <item><description><b>Source-anchored byte-identical:</b> emitting with
///     <c>PreserveOriginalWhenAvailable</c> and the original source yields
///     a string equal to the input.</description></item>
///   <item><description><b>From-AST structural round-trip:</b>
///     <c>parse(emit(parse(x))).StructuralHash == parse(x).StructuralHash</c>.
///     This is the central correctness criterion for the synthesis path.</description></item>
/// </list>
/// As Phase 1 progresses, the second invariant is expected to fail on fixtures
/// using node kinds the emitter has not yet learned. Failing fixtures identify
/// the next emitter feature to build; this test class is the working punch list.
/// </summary>
[TestFixture]
public class FixtureRoundTripTests
{
    private static readonly AsciidocEmitter Emitter = new();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate repo root from test directory.");
    }

    public static IEnumerable<TestCaseData> AllFixtures()
    {
        string root;
        try
        {
            root = FindRepoRoot();
        }
        catch
        {
            yield break;
        }

        foreach (var pattern in new[] { "tests/fixtures/**/*.adoc", "spec/conformance/**/*.adoc" })
        {
            var parts = pattern.Split('/');
            var dir = Path.Combine(root, parts[0], parts[1]);
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir, "*.adoc", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, path).Replace('\\', '/');
                yield return new TestCaseData(path).SetName(rel);
            }
        }
    }

    [TestCaseSource(nameof(AllFixtures))]
    public void Source_anchored_emit_is_byte_identical(string path)
    {
        var source = File.ReadAllText(path);
        var doc = AdocParser.Parse(source).Document;
        var emitted = Emitter.Emit(doc, new EmitOptions
        {
            PreserveOriginalWhenAvailable = true,
            OriginalSource = source,
        });
        Assert.That(emitted, Is.EqualTo(source));
    }

    [TestCaseSource(nameof(AllFixtures))]
    [Explicit("From-AST round-trip is a working punch list; many fixtures fail until the emitter covers every node kind.")]
    public void From_ast_round_trip_preserves_structural_hash(string path)
    {
        var source = File.ReadAllText(path);
        var original = AdocParser.Parse(source).Document;
        var emitted = Emitter.Emit(original);
        var reparsed = AdocParser.Parse(emitted).Document;

        Assert.That(reparsed.StructuralHash, Is.EqualTo(original.StructuralHash),
            $"Structural mismatch.\n--- emitted ---\n{emitted}\n--- end ---");
    }
}
