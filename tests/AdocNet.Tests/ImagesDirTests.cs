using AdocNet.Converters.DocBook;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Image paths under <c>:imagesdir:</c>, as Asciidoctor writes them (<c>image_uri</c>), in HTML
/// and DocBook.
/// </summary>
[TestFixture]
public class ImagesDirTests
{
    private const string Source = ":imagesdir: img\n\nimage::a.png[A]\n\nText image:b.png[B] here.\n";

    [TestCase("a.png", null, "a.png")]
    [TestCase("a.png", "", "a.png")]
    [TestCase("a.png", "img", "img/a.png")]
    [TestCase("a.png", "img/", "img/a.png")]
    [TestCase("./a.png", null, "./a.png")]
    [TestCase("a/./b/../c.png", null, "a/c.png")]
    [TestCase("../a.png", "img", "a.png")]
    [TestCase("../../a.png", "img", "../a.png")]
    [TestCase("/abs/a.png", "img", "/abs/a.png")]
    [TestCase("/../a.png", null, "/a.png")]
    [TestCase("my pic.png", "my img", "my%20img/my%20pic.png")]
    [TestCase("a.png", "./img", "./img/a.png")]
    [TestCase("a.png", "https://cdn.example.com/img", "https://cdn.example.com/img/a.png")]
    [TestCase("https://example.com/my a.png", "img", "https://example.com/my%20a.png")]
    [TestCase("data:image/png;base64,AAAA", "img", "data:image/png;base64,AAAA")]
    [TestCase("C:/pics/a.png", "img", "img/C:/pics/a.png")] // a drive is no URI scheme, as in Asciidoctor
    public void Image_FollowsAsciidoctorsImageUri(string target, string? imagesDir, string expected)
    {
        Assert.That(AssetPath.Image(target, imagesDir), Is.EqualTo(expected));
    }

    [Test]
    public void Html_ImagesAreUnderImagesDir()
    {
        var html = new HtmlRenderer().RenderToString(AdocParser.Parse(Source).Document);

        Assert.That(html, Does.Contain("<img src=\"img/a.png\" alt=\"A\">"));
        Assert.That(html, Does.Contain("<img src=\"img/b.png\" alt=\"B\">"));
    }

    [Test]
    public void DocBook_ImagesAreUnderImagesDir()
    {
        var xml = new DocBookRenderer().RenderToString(AdocParser.Parse(Source).Document);

        Assert.That(xml, Does.Contain("fileref=\"img/a.png\""));
        Assert.That(xml, Does.Contain("fileref=\"img/b.png\""));
        Assert.That(xml, Does.Not.Contain("fileref=\"a.png\""));
    }
}
