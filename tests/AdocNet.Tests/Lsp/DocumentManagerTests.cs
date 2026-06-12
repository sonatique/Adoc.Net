using AdocNet.LanguageServer;

namespace AdocNet.Tests.Lsp;

[TestFixture]
public class DocumentManagerTests
{
    [Test]
    public void Parse_returns_result_with_document()
    {
        var mgr = new DocumentManager();
        var result = mgr.Parse("file:///test.adoc", "= Title\n\nHello.\n");
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Document.Children, Has.Count.GreaterThan(0));
    }

    [Test]
    public void Get_returns_cached_result()
    {
        var mgr = new DocumentManager();
        mgr.Parse("file:///test.adoc", "= Title\n\nHello.\n");
        Assert.That(mgr.Get("file:///test.adoc"), Is.Not.Null);
    }

    [Test]
    public void Get_returns_null_for_unknown_uri()
    {
        var mgr = new DocumentManager();
        Assert.That(mgr.Get("file:///unknown.adoc"), Is.Null);
    }

    [Test]
    public void Remove_clears_cached_result()
    {
        var mgr = new DocumentManager();
        mgr.Parse("file:///test.adoc", "= Title\n\nHello.\n");
        mgr.Remove("file:///test.adoc");
        Assert.That(mgr.Get("file:///test.adoc"), Is.Null);
    }

    [Test]
    public void Parse_replaces_previous_result()
    {
        var mgr = new DocumentManager();
        mgr.Parse("file:///test.adoc", "= First\n\nV1.\n");
        mgr.Parse("file:///test.adoc", "= Second\n\nV2.\n");
        var result = mgr.Get("file:///test.adoc");
        Assert.That(result!.Document.Title, Is.EqualTo("Second"));
    }

    [Test]
    public void GetText_returns_cached_source()
    {
        var mgr = new DocumentManager();
        mgr.Parse("file:///test.adoc", "= Title\n\nHello.\n");
        Assert.That(mgr.GetText("file:///test.adoc"), Is.EqualTo("= Title\n\nHello.\n"));
    }

    [Test]
    public void GetAnchors_returns_section_ids()
    {
        var mgr = new DocumentManager();
        mgr.Parse("file:///test.adoc", "= Doc\n\n== First Section\n\n== Second Section\n");
        var anchors = mgr.GetAnchors("file:///test.adoc");
        Assert.That(anchors, Does.Contain("_first_section"));
        Assert.That(anchors, Does.Contain("_second_section"));
    }

    [Test]
    public void GetAttributes_returns_document_attributes()
    {
        var mgr = new DocumentManager();
        mgr.Parse("file:///test.adoc", "= Doc\n:author: Alice\n:version: 1.0\n\nText.\n");
        var attrs = mgr.GetAttributes("file:///test.adoc");
        Assert.That(attrs, Does.ContainKey("author"));
    }

    [Test]
    public void Parse_resolves_includes_relative_to_the_document_file_uri()
    {
        // Regression: the LSP passed the raw file:// URI as SourceFilePath, so include:: resolved
        // against a bogus path and every include reported "not found" on each keystroke.
        var dir = Path.Combine(Path.GetTempPath(), "adocnet-lsp-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "part.adoc"), "Included from part.");
            var mainPath = Path.Combine(dir, "main.adoc");
            File.WriteAllText(mainPath, "= Main\n\ninclude::part.adoc[]\n");
            var uri = new Uri(mainPath).AbsoluteUri; // file:///.../main.adoc

            var mgr = new DocumentManager();
            var result = mgr.Parse(uri, File.ReadAllText(mainPath));

            Assert.That(result.Diagnostics.Any(d => d.Message.Contains("not found")), Is.False,
                "include should resolve relative to the document's directory");
            var html = new AdocNet.Converters.Html.HtmlRenderer().RenderToString(result.Document);
            Assert.That(html, Does.Contain("Included from part"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
