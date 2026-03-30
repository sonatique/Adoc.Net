using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class SimpleJsonParserRegistryTests
{
    [Test]
    public void ParseObjectWithArray_ValidRegistry_ParsesMetadataAndItems()
    {
        var json = """
            {
              "version": "1",
              "extensions": [
                {
                  "name": "alpha",
                  "version": "1.0.0",
                  "description": "Alpha extension",
                  "path": "/home/user/.adocnet/extensions/alpha",
                  "dependencies": ""
                },
                {
                  "name": "beta",
                  "version": "2.0.0",
                  "description": "Beta extension",
                  "path": "/home/user/.adocnet/extensions/beta",
                  "dependencies": "alpha >= 1.0.0"
                }
              ]
            }
            """;

        var (metadata, items) = SimpleJsonParser.ParseObjectWithArray(json, "extensions");

        Assert.That(metadata["version"], Is.EqualTo("1"));
        Assert.That(items, Has.Count.EqualTo(2));
        Assert.That(items[0]["name"], Is.EqualTo("alpha"));
        Assert.That(items[0]["path"], Is.EqualTo("/home/user/.adocnet/extensions/alpha"));
        Assert.That(items[1]["name"], Is.EqualTo("beta"));
        Assert.That(items[1]["dependencies"], Is.EqualTo("alpha >= 1.0.0"));
    }

    [Test]
    public void ParseObjectWithArray_EmptyArray_ReturnsNoItems()
    {
        var json = """{"version": "1", "extensions": []}""";

        var (metadata, items) = SimpleJsonParser.ParseObjectWithArray(json, "extensions");

        Assert.That(metadata["version"], Is.EqualTo("1"));
        Assert.That(items, Has.Count.EqualTo(0));
    }

    [Test]
    public void ParseObjectWithArray_NoArrayKey_ReturnsEmptyItems()
    {
        var json = """{"version": "1", "other": "value"}""";

        var (metadata, items) = SimpleJsonParser.ParseObjectWithArray(json, "extensions");

        Assert.That(metadata["version"], Is.EqualTo("1"));
        Assert.That(metadata["other"], Is.EqualTo("value"));
        Assert.That(items, Has.Count.EqualTo(0));
    }

    [Test]
    public void ParseObjectWithArray_MalformedJson_ThrowsFormatException()
    {
        var json = "{ not valid }";

        Assert.That(() => SimpleJsonParser.ParseObjectWithArray(json, "extensions"),
            Throws.TypeOf<FormatException>());
    }

    [Test]
    public void SerializeRegistry_RoundTripsWithParseObjectWithArray()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["version"] = "1"
        };
        var items = new List<Dictionary<string, string>>
        {
            new(StringComparer.Ordinal)
            {
                ["name"] = "ext-a",
                ["version"] = "1.0.0",
                ["description"] = "Extension A",
                ["path"] = "/path/to/ext-a",
                ["dependencies"] = ""
            }
        };
        var fieldOrder = new[] { "name", "version", "description", "path", "dependencies" };

        var json = SimpleJsonWriter.SerializeRegistry(metadata, "extensions", items, fieldOrder);
        var (parsedMeta, parsedItems) = SimpleJsonParser.ParseObjectWithArray(json, "extensions");

        Assert.That(parsedMeta["version"], Is.EqualTo("1"));
        Assert.That(parsedItems, Has.Count.EqualTo(1));
        Assert.That(parsedItems[0]["name"], Is.EqualTo("ext-a"));
        Assert.That(parsedItems[0]["path"], Is.EqualTo("/path/to/ext-a"));
    }

    [Test]
    public void SerializeRegistry_EscapesSpecialCharacters()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["version"] = "1"
        };
        var items = new List<Dictionary<string, string>>
        {
            new(StringComparer.Ordinal)
            {
                ["name"] = "ext",
                ["description"] = "Has \"quotes\" and \\backslashes"
            }
        };
        var fieldOrder = new[] { "name", "description" };

        var json = SimpleJsonWriter.SerializeRegistry(metadata, "items", items, fieldOrder);
        var (_, parsedItems) = SimpleJsonParser.ParseObjectWithArray(json, "items");

        Assert.That(parsedItems[0]["description"], Is.EqualTo("Has \"quotes\" and \\backslashes"));
    }

    [Test]
    public void ParseStringArray_ValidArray_ReturnsStrings()
    {
        var json = """["one", "two", "three"]""";

        var result = SimpleJsonParser.ParseStringArray(json, 0);

        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result[0], Is.EqualTo("one"));
        Assert.That(result[1], Is.EqualTo("two"));
        Assert.That(result[2], Is.EqualTo("three"));
    }

    [Test]
    public void ParseStringArray_EmptyArray_ReturnsEmpty()
    {
        var json = """[]""";

        var result = SimpleJsonParser.ParseStringArray(json, 0);

        Assert.That(result, Has.Count.EqualTo(0));
    }

    [Test]
    public void ParseStringArray_NotAnArray_ReturnsEmpty()
    {
        var json = """42""";

        var result = SimpleJsonParser.ParseStringArray(json, 0);

        Assert.That(result, Has.Count.EqualTo(0));
    }
}
