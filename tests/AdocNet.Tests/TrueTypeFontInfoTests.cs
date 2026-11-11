using AdocNet.Converters.Pdf;

namespace AdocNet.Tests;

[TestFixture]
public class TrueTypeFontInfoTests
{
    private static string RepoFont(string fileName)
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AdocNet.slnx")))
            dir = Path.GetDirectoryName(dir);
        Assert.That(dir, Is.Not.Null, "repository root not found");
        return Path.Combine(dir!, "src", "AdocNet.Converters.Epub", "Resources", fileName);
    }

    [Test]
    public void Reads_family_name_not_postscript_name()
    {
        // Bold face: the PostScript name is "NotoSerif-Bold", the family is "Noto Serif".
        Assert.That(TrueTypeFontInfo.ReadFamilyName(RepoFont("notoserif-bold-latin.ttf")), Is.EqualTo("Noto Serif"));
    }

    [Test]
    public void Missing_file_returns_null()
    {
        Assert.That(TrueTypeFontInfo.ReadFamilyName(Path.Combine(Path.GetTempPath(), "no-such-font-7d1.ttf")), Is.Null);
    }

    [Test]
    public void Non_font_file_returns_null()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[] { 0, 1, 0, 0, 0xFF, 0xFF, 1, 2, 3 });
            Assert.That(TrueTypeFontInfo.ReadFamilyName(path), Is.Null);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
