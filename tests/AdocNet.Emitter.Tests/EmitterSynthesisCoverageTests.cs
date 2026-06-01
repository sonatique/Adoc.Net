using AdocNet.Emitter;
using AdocNet.Parser;

namespace AdocNet.Emitter.Tests;

/// <summary>
/// Guards the emitter's synthesis path (used by the WYSIWYG AST-mutation
/// commands, where no original source is available) against silently dropping a
/// node to the <c>// [emitter: unhandled &lt;Kind&gt;]</c> sentinel. A sentinel
/// in the output means a mutate-and-splice would corrupt the document, so each
/// supported construct must round-trip through synthesis without one.
/// </summary>
[TestFixture]
public class EmitterSynthesisCoverageTests
{
    private static readonly AsciidocEmitter Emitter = new();

    private const string Sentinel = "[emitter: unhandled";

    [TestCase("paragraph", "Hello world")]
    [TestCase("section", "== Section\n\nbody text")]
    [TestCase("nested-section", "= Doc\n\n== A\n\n=== B\n\nbody")]
    [TestCase("unordered-list", "* alpha\n* beta")]
    [TestCase("ordered-list", ". one\n. two")]
    [TestCase("description-list", "term:: definition")]
    [TestCase("listing-block", "----\ncode line\n----")]
    [TestCase("source-block", "[source,csharp]\n----\nvar x = 1;\n----")]
    [TestCase("example-block", "====\nexample body\n====")]
    [TestCase("quote-block", "____\nquoted\n____")]
    [TestCase("sidebar-block", "****\naside\n****")]
    [TestCase("literal-block", "....\nliteral\n....")]
    [TestCase("open-block", "--\nopen\n--")]
    [TestCase("admonition-inline", "NOTE: take note")]
    [TestCase("admonition-block", "[WARNING]\n====\nbe careful\n====")]
    [TestCase("table", "|===\n| a | b\n| c | d\n|===")]
    [TestCase("block-image", "image::picture.png[Alt text]")]
    [TestCase("thematic-break", "before\n\n'''\n\nafter")]
    [TestCase("page-break", "before\n\n<<<\n\nafter")]
    [TestCase("stem-block", "[stem]\n++++\nsqrt(x)\n++++")]
    [TestCase("formatted-paragraph", "Mix of *bold*, _italic_, `mono` and a https://x.example[link].")]
    public void Synthesis_emits_no_unhandled_sentinel(string name, string source)
    {
        var doc = AdocParser.Parse(source).Document;

        // No options => synthesis path (the source-anchored fast path is off).
        var emitted = Emitter.Emit(doc);

        Assert.That(emitted, Does.Not.Contain(Sentinel),
            $"[{name}] synthesis dropped a node to the unhandled sentinel:\n{emitted}");
    }
}
