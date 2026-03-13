using AdocNet.LanguageServer;
using AdocNet.Parser;

namespace AdocNet.Tests.Lsp;

[TestFixture]
public class HoverTests
{
    [Test]
    public void Xref_hover_returns_section_title()
    {
        var dm = new DocumentManager();
        dm.Parse("file:///test.adoc", "= Doc\n\n== My Section\n\nSee <<_my_section>>.\n");

        var result = HoverResolver.Resolve(dm, "file:///test.adoc", 4, 10);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("My Section"));
    }

    [Test]
    public void Attribute_hover_returns_value()
    {
        var dm = new DocumentManager();
        dm.Parse("file:///test.adoc", "= Doc\n:version: 2.0\n\nVersion is {version}.\n");

        var result = HoverResolver.Resolve(dm, "file:///test.adoc", 3, 16);

        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("2.0"));
    }

    [Test]
    public void Plain_text_returns_null()
    {
        var dm = new DocumentManager();
        dm.Parse("file:///test.adoc", "= Doc\n\nJust plain text.\n");

        var result = HoverResolver.Resolve(dm, "file:///test.adoc", 2, 5);

        Assert.That(result, Is.Null);
    }
}
