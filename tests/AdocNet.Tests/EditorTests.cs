using AdocNet.Ast;
using AdocNet.Editor;

namespace AdocNet.Tests;

[TestFixture]
public class DocumentChangeTests
{
    [Test]
    public void ApplyAll_Insert_InsertsTextAtOffset()
    {
        var text = "Hello World";
        var changes = new[] { new DocumentChange(5, 0, ",") };

        var result = DocumentChange.ApplyAll(text, changes);

        Assert.That(result, Is.EqualTo("Hello, World"));
    }

    [Test]
    public void ApplyAll_Delete_RemovesCharacters()
    {
        var text = "Hello World";
        var changes = new[] { new DocumentChange(5, 6, "") };

        var result = DocumentChange.ApplyAll(text, changes);

        Assert.That(result, Is.EqualTo("Hello"));
    }

    [Test]
    public void ApplyAll_Replace_ReplacesRange()
    {
        var text = "Hello World";
        var changes = new[] { new DocumentChange(6, 5, "Earth") };

        var result = DocumentChange.ApplyAll(text, changes);

        Assert.That(result, Is.EqualTo("Hello Earth"));
    }

    [Test]
    public void ApplyAll_MultipleChanges_AppliedSequentially()
    {
        var text = "abc";
        var changes = new[]
        {
            new DocumentChange(1, 1, "B"),   // abc -> aBc
            new DocumentChange(2, 1, "CD"),  // aBc -> aBCD
        };

        var result = DocumentChange.ApplyAll(text, changes);

        Assert.That(result, Is.EqualTo("aBCD"));
    }

    [Test]
    public void ApplyAll_EmptyChanges_ReturnsOriginalText()
    {
        var result = DocumentChange.ApplyAll("test", Array.Empty<DocumentChange>());
        Assert.That(result, Is.EqualTo("test"));
    }

    [Test]
    public void ApplyAll_EmptyText_InsertWorks()
    {
        var result = DocumentChange.ApplyAll("", new[] { new DocumentChange(0, 0, "hello") });
        Assert.That(result, Is.EqualTo("hello"));
    }

    [Test]
    public void ApplyAll_OffsetExceedsLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DocumentChange.ApplyAll("abc", new[] { new DocumentChange(10, 0, "x") }));
    }

    [Test]
    public void Constructor_NegativeOffset_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentChange(-1, 0, "x"));
    }

    [Test]
    public void Constructor_NegativeLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentChange(0, -1, "x"));
    }
}

[TestFixture]
public class DocumentSnapshotTests
{
    [Test]
    public void Initial_CreatesVersion0()
    {
        var snapshot = DocumentSnapshot.Initial("hello");

        Assert.That(snapshot.Version, Is.EqualTo(0));
        Assert.That(snapshot.Text, Is.EqualTo("hello"));
        Assert.That(snapshot.Document, Is.Null);
    }

    [Test]
    public void ApplyChanges_IncrementsVersion()
    {
        var s0 = DocumentSnapshot.Initial("hello");
        var s1 = s0.ApplyChanges(new[] { new DocumentChange(5, 0, " world") });

        Assert.That(s1.Version, Is.EqualTo(1));
        Assert.That(s1.Text, Is.EqualTo("hello world"));
        Assert.That(s1.Document, Is.Null);
    }

    [Test]
    public void ApplyChanges_ChainingIncrementsVersions()
    {
        var s0 = DocumentSnapshot.Initial("a");
        var s1 = s0.ApplyChanges(new[] { new DocumentChange(1, 0, "b") });
        var s2 = s1.ApplyChanges(new[] { new DocumentChange(2, 0, "c") });

        Assert.That(s2.Version, Is.EqualTo(2));
        Assert.That(s2.Text, Is.EqualTo("abc"));
    }

    [Test]
    public void Constructor_WithDocument_RetainsDocument()
    {
        var doc = new DocumentNode();
        var snapshot = new DocumentSnapshot(1, "text", doc);

        Assert.That(snapshot.Document, Is.SameAs(doc));
    }
}

[TestFixture]
public class ParseIncrementalTests
{
    private static AdocEngine CreateEngine(bool caching = false)
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, text => new DocumentNode())
        {
            EnableCaching = caching,
        };
        return engine;
    }

    [Test]
    public void ParseIncremental_ReturnsParsedSnapshot()
    {
        var engine = CreateEngine();
        var snapshot = DocumentSnapshot.Initial("= Title");

        var result = engine.ParseIncremental(snapshot);

        Assert.That(result.Document, Is.Not.Null);
        Assert.That(result.Version, Is.EqualTo(0));
        Assert.That(result.Text, Is.EqualTo("= Title"));
    }

    [Test]
    public void ParseIncremental_WithCaching_ReturnsCachedForSameText()
    {
        int parseCount = 0;
        var doc = new DocumentNode();
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, text =>
        {
            parseCount++;
            return doc;
        })
        {
            EnableCaching = true,
        };

        var s1 = engine.ParseIncremental(DocumentSnapshot.Initial("= Title"));
        var s2 = engine.ParseIncremental(DocumentSnapshot.Initial("= Title"));

        Assert.That(parseCount, Is.EqualTo(1), "Parser should only be called once for identical text");
        Assert.That(s1.Document, Is.SameAs(s2.Document));
    }

    [Test]
    public void ParseIncremental_WithCaching_ReparsesDifferentText()
    {
        int parseCount = 0;
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, text =>
        {
            parseCount++;
            return new DocumentNode();
        })
        {
            EnableCaching = true,
        };

        engine.ParseIncremental(DocumentSnapshot.Initial("= Title A"));
        engine.ParseIncremental(DocumentSnapshot.Initial("= Title B"));

        Assert.That(parseCount, Is.EqualTo(2));
    }

    [Test]
    public void ParseIncremental_WithoutCaching_AlwaysReparses()
    {
        int parseCount = 0;
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, text =>
        {
            parseCount++;
            return new DocumentNode();
        })
        {
            EnableCaching = false,
        };

        engine.ParseIncremental(DocumentSnapshot.Initial("= Title"));
        engine.ParseIncremental(DocumentSnapshot.Initial("= Title"));

        Assert.That(parseCount, Is.EqualTo(2));
    }

    [Test]
    public void ParseIncremental_AfterApplyChanges_ParsesNewText()
    {
        string? lastParsedText = null;
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, text =>
        {
            lastParsedText = text;
            return new DocumentNode();
        });

        var s0 = DocumentSnapshot.Initial("abc");
        var s1 = s0.ApplyChanges(new[] { new DocumentChange(3, 0, "def") });
        engine.ParseIncremental(s1);

        Assert.That(lastParsedText, Is.EqualTo("abcdef"));
    }

    private sealed class StubRenderer : IDocumentRenderer
    {
        public string Format => "stub";
        public void Render(DocumentNode document, Stream output, RenderOptions options) { }
    }
}
