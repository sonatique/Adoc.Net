using System.IO.Compression;
using AdocNet.Converters.DocBook;
using AdocNet.Converters.Epub;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>An image without alt text is written with Asciidoctor's <c>default-alt</c>, from its
/// file name; the AST keeps the alt as given.</summary>
[TestFixture]
public class ImageAltTests
{
    [TestCase("", "a.png", "a")]
    [TestCase("", "img/my_pic-1.png", "my pic 1")]
    [TestCase("", "a.tar.gz", "a.tar")]
    [TestCase("", "dir.v2/name", "name")]
    [TestCase("", ".hidden", ".hidden")]
    [TestCase("", "https://e.com/x/a%20b.svg", "a%20b")]
    [TestCase("Mine", "my_pic.png", "Mine")]
    public void ImageAlt_IsTheGivenAlt_ElseTheFileName(string alt, string target, string expected)
    {
        Assert.That(AssetPath.ImageAlt(alt, target), Is.EqualTo(expected));
    }

    [Test]
    public void HtmlDocBookAndEpub_WriteTheDefaultAlt()
    {
        var doc = AdocParser.Parse("= Book\n\nimage::my_pic.png[]\n\nText image:b-c.png[] here.\n").Document;

        var html = new HtmlRenderer().RenderToString(doc);
        Assert.That(html, Does.Contain("<img src=\"my_pic.png\" alt=\"my pic\">"));
        Assert.That(html, Does.Contain("<img src=\"b-c.png\" alt=\"b c\">"));
        var xml = new DocBookRenderer().RenderToString(doc);
        Assert.That(xml, Does.Contain("<phrase>my pic</phrase>"));
        Assert.That(xml, Does.Contain("<phrase>b c</phrase>"));
        using var zip = new ZipArchive(new MemoryStream(new EpubRenderer().RenderToBytes(doc)), ZipArchiveMode.Read);
        var chapter = new StreamReader(zip.Entries.Single(e => e.FullName.StartsWith("EPUB/_", StringComparison.Ordinal)).Open()).ReadToEnd();
        Assert.That(chapter, Does.Contain("<img src=\"my_pic.png\" alt=\"my pic\""));
        Assert.That(chapter, Does.Contain("<img class=\"inline\" src=\"b-c.png\" alt=\"b c\""));
    }
}
