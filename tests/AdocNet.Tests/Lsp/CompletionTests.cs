using AdocNet.LanguageServer;
using AdocNet.Parser;

namespace AdocNet.Tests.Lsp;

[TestFixture]
public class CompletionTests
{
    [Test]
    public void Suggests_anchors_after_double_angle()
    {
        var dm = new DocumentManager();
        dm.Parse("file:///test.adoc", "= Doc\n\n== First\n\n== Second\n\nSee <<\n");

        // Line 6: "See <<", col 5 (on the second <)
        var result = CompletionResolver.Resolve(dm, "file:///test.adoc", 6, 5);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Does.Contain("_first"));
        Assert.That(result, Does.Contain("_second"));
    }

    [Test]
    public void Suggests_attributes_after_open_brace()
    {
        var dm = new DocumentManager();
        dm.Parse("file:///test.adoc", "= Doc\n:author: Jane\n:version: 1.0\n\nHello {\n");

        // Line 4: "Hello {", col 6 (on the {)
        var result = CompletionResolver.Resolve(dm, "file:///test.adoc", 4, 6);

        Assert.That(result, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(result, Does.Contain("author"));
        Assert.That(result, Does.Contain("version"));
    }

    [Test]
    public void Plain_text_returns_empty()
    {
        var dm = new DocumentManager();
        dm.Parse("file:///test.adoc", "= Doc\n\nJust plain text.\n");

        var result = CompletionResolver.Resolve(dm, "file:///test.adoc", 2, 5);

        Assert.That(result, Is.Empty);
    }
}
