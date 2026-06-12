using AdocNet;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class AttributeScopingTests
{
    [TestCase("backend", "html5")]
    [TestCase("doctype", "article")]
    [TestCase("empty", "")]
    [TestCase("sp", " ")]
    [TestCase("apos", "'")]
    [TestCase("quot", "\"")]
    [TestCase("startsb", "[")]
    [TestCase("endsb", "]")]
    [TestCase("caret", "^")]
    [TestCase("tilde", "~")]
    [TestCase("backslash", "\\")]
    [TestCase("plus", "+")]
    public void Default_attribute_is_available(string name, string expectedValue)
    {
        var result = BlockParser.Parse("");
        Assert.That(result.Document.Attributes.ContainsKey(name), Is.True);
        Assert.That(result.Document.Attributes[name], Is.EqualTo(expectedValue));
    }

    [Test]
    public void Nbsp_attribute_resolves_to_non_breaking_space()
    {
        var result = BlockParser.Parse("a{nbsp}b");
        var para = result.Document.Children.OfType<ParagraphNode>().First();
        var text = string.Join("", para.Inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Does.Contain("\u00A0"));
    }

    [Test]
    public void Zwsp_attribute_resolves_to_zero_width_space()
    {
        var result = BlockParser.Parse("a{zwsp}b");
        var para = result.Document.Children.OfType<ParagraphNode>().First();
        var text = string.Join("", para.Inlines.OfType<TextInlineNode>().Select(t => t.Value));
        Assert.That(text, Does.Contain("\u200B"));
    }

    [Test]
    public void Locked_attribute_not_overridden_by_header()
    {
        var options = new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["backend"] = "html5" },
            LockedAttributes = new HashSet<string> { "backend" },
        };
        var result = BlockParser.Parse("= Title\n:backend: pdf\n\nContent", options);
        Assert.That(result.Document.Attributes["backend"], Is.EqualTo("html5"));
    }

    [Test]
    public void Locked_attribute_honored_through_AdocParser_entry_point()
    {
        // Regression: AdocParser.Parse (the primary public API) previously called the
        // BlockParser overload that dropped LockedAttributes, making the option a no-op.
        var options = new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["backend"] = "html5" },
            LockedAttributes = new HashSet<string> { "backend" },
        };
        var result = AdocParser.Parse("= Title\n:backend: pdf\n\nContent", options);
        Assert.That(result.Document.Attributes["backend"], Is.EqualTo("html5"));
    }

    [Test]
    public void Locked_attribute_not_overridden_by_body()
    {
        var options = new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["myattr"] = "locked-value" },
            LockedAttributes = new HashSet<string> { "myattr" },
        };
        var result = BlockParser.Parse(":myattr: new-value\n\nContent", options);
        Assert.That(result.Document.Attributes["myattr"], Is.EqualTo("locked-value"));
    }

    [Test]
    public void Non_locked_attribute_can_be_overridden()
    {
        var options = new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["backend"] = "html5" },
            LockedAttributes = new HashSet<string> { "other" },
        };
        var result = BlockParser.Parse("= Title\n:backend: pdf\n\nContent", options);
        Assert.That(result.Document.Attributes["backend"], Is.EqualTo("pdf"));
    }

    [Test]
    public void No_locked_attributes_allows_all_overrides()
    {
        var options = new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["backend"] = "html5" },
        };
        var result = BlockParser.Parse("= Title\n:backend: pdf\n\nContent", options);
        Assert.That(result.Document.Attributes["backend"], Is.EqualTo("pdf"));
    }

    [Test]
    public void Header_attribute_overrides_api_attribute()
    {
        var options = new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["lang"] = "en" },
        };
        var result = BlockParser.Parse("= Title\n:lang: fr\n\nContent", options);
        Assert.That(result.Document.Attributes["lang"], Is.EqualTo("fr"));
    }

    [Test]
    public void Body_attribute_overrides_header_attribute()
    {
        var result = BlockParser.Parse("= Title\n:version: 1.0\n\n:version: 2.0\n\nContent");
        Assert.That(result.Document.Attributes["version"], Is.EqualTo("2.0"));
    }

    [Test]
    public void Locked_attribute_prevents_unset_in_header()
    {
        var options = new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["backend"] = "html5" },
            LockedAttributes = new HashSet<string> { "backend" },
        };
        var result = BlockParser.Parse("= Title\n:!backend:\n\nContent", options);
        Assert.That(result.Document.Attributes["backend"], Is.EqualTo("html5"));
    }

    [Test]
    public void Locked_attribute_prevents_unset_in_body()
    {
        var options = new ParseOptions
        {
            Attributes = new Dictionary<string, string> { ["myattr"] = "locked-value" },
            LockedAttributes = new HashSet<string> { "myattr" },
        };
        var result = BlockParser.Parse(":!myattr:\n\nContent", options);
        Assert.That(result.Document.Attributes["myattr"], Is.EqualTo("locked-value"));
    }
}
