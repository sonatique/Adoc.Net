using AdocNet.Extensions;

namespace AdocNet.Tests;

[TestFixture]
public class MaxEngineVersionTests
{
    [Test]
    public void MaxAdocNetVersion_Compatible_ParsesField()
    {
        var json = """
            {
                "name": "test-ext",
                "version": "1.0.0",
                "entry": "Test.dll",
                "maxAdocNetVersion": "2.0.0"
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/tmp/test-ext", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.MaxAdocNetVersion, Is.EqualTo("2.0.0"));
    }

    [Test]
    public void MaxAdocNetVersion_Absent_NullProperty()
    {
        var json = """
            {
                "name": "test-ext",
                "version": "1.0.0",
                "entry": "Test.dll"
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/tmp/test-ext", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.MaxAdocNetVersion, Is.Null);
    }

    [Test]
    public void MaxAdocNetVersion_Exceeded_ExtensionSkipped()
    {
        // IsVersionCompatible(max, current) returns true if max >= current
        // When current > max, it returns false → extension should be skipped
        var result = ExtensionDirectoryLoader.IsVersionCompatible("0.9.0", "1.0.0");
        Assert.That(result, Is.False,
            "max=0.9.0 should NOT be compatible with current=1.0.0");
    }

    [Test]
    public void MaxAdocNetVersion_NotExceeded_ExtensionLoads()
    {
        // When current <= max, IsVersionCompatible(max, current) returns true
        var result = ExtensionDirectoryLoader.IsVersionCompatible("2.0.0", "1.0.0");
        Assert.That(result, Is.True,
            "max=2.0.0 should be compatible with current=1.0.0");
    }

    [Test]
    public void MaxAdocNetVersion_Equal_ExtensionLoads()
    {
        var result = ExtensionDirectoryLoader.IsVersionCompatible("1.0.0", "1.0.0");
        Assert.That(result, Is.True,
            "max=current should be compatible (current <= max)");
    }
}
