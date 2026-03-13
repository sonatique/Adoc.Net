using AdocNet.LanguageServer;
using AdocNet.Parser;

namespace AdocNet.Tests.Lsp;

[TestFixture]
public class DefinitionTests
{
    [Test]
    public void Xref_resolves_to_section_location()
    {
        var dm = new DocumentManager();
        dm.Parse("file:///test.adoc", "= Doc\n\n== My Section\n\nSee <<_my_section>>.\n");

        var result = DefinitionResolver.Resolve(dm, "file:///test.adoc", 4, 10);

        Assert.That(result, Is.Not.Null);
        // Section "== My Section" is on line index 2 (0-based)
        Assert.That(result!.Value.Line, Is.EqualTo(2));
    }

    [Test]
    public void Plain_text_returns_null()
    {
        var dm = new DocumentManager();
        dm.Parse("file:///test.adoc", "= Doc\n\nJust plain text.\n");

        var result = DefinitionResolver.Resolve(dm, "file:///test.adoc", 2, 5);

        Assert.That(result, Is.Null);
    }
}
