using System.IO.Compression;
using AdocNet.Converters.DocBook;
using AdocNet.Converters.Epub;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Image paths under <c>:imagesdir:</c>, as Asciidoctor writes them (<c>image_uri</c>), in HTML,
/// DocBook and EPUB; and the images an EPUB carries.
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

    [Test]
    public void Epub_WithoutABaseDirectory_RefersToTheImagesUnderImagesDir()
    {
        using var zip = Open(new EpubRenderer().RenderToBytes(AdocParser.Parse("= Book\n\n" + Source).Document));

        Assert.That(Chapter(zip), Does.Contain("<img src=\"img/a.png\""));
        Assert.That(zip.GetEntry("EPUB/img/a.png"), Is.Null);
    }

    [Test]
    public void Epub_CarriesTheImagesFoundInTheBaseDirectory()
    {
        var dir = Directory.CreateTempSubdirectory("adocnet-epub-images-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "img"));
            byte[] png = [137, 80, 78, 71, 1, 2, 3];
            File.WriteAllBytes(Path.Combine(dir, "img", "a.png"), png);
            File.WriteAllBytes(Path.Combine(dir, "img", "b.png"), png);
            File.WriteAllBytes(Path.Combine(dir, "secret.png"), png); // outside imagesdir but inside the base
            var outside = Path.Combine(Path.GetDirectoryName(dir)!, Path.GetFileName(dir) + "-outside.png");
            File.WriteAllBytes(outside, png);
            var source = "= Book\n\n" + Source + $"\nimage::../{Path.GetFileName(outside)}[Out]\n\nimage::missing.png[Gone]\n";

            using var zip = Open(new EpubRenderer().RenderToBytes(AdocParser.Parse(source).Document,
                new EpubRenderOptions { BaseDirectory = dir }));

            Assert.That(Read(zip.GetEntry("EPUB/img/a.png")!), Is.EqualTo(png));
            Assert.That(zip.GetEntry("EPUB/img/b.png"), Is.Not.Null);
            var opf = new StreamReader(zip.GetEntry("EPUB/package.opf")!.Open()).ReadToEnd();
            Assert.That(opf, Does.Contain("<item href=\"img/a.png\" id=\"item_image1\" media-type=\"image/png\"/>"));
            Assert.That(opf, Does.Contain("<item href=\"img/b.png\" id=\"item_image2\" media-type=\"image/png\"/>"));
            var chapter = Chapter(zip);
            Assert.That(chapter, Does.Contain("<img src=\"img/a.png\""));
            Assert.That(chapter, Does.Contain("<img class=\"inline\" src=\"img/b.png\""));
            // Safe mode: nothing from outside the base directory; a missing image stays as written.
            Assert.That(zip.Entries.Select(e => e.FullName), Has.None.Contains("outside"));
            Assert.That(chapter, Does.Contain("<img src=\"img/missing.png\""));
            File.Delete(outside);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void Epub_AnImageNeverReplacesTheBooksOwnFiles()
    {
        var dir = Directory.CreateTempSubdirectory("adocnet-epub-images-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "avatars"));
            File.WriteAllBytes(Path.Combine(dir, "avatars", "default.jpg"), [255, 216, 255, 1]);

            using var zip = Open(new EpubRenderer().RenderToBytes(
                AdocParser.Parse("= Book\n\nimage::avatars/default.jpg[Mine]\n").Document,
                new EpubRenderOptions { BaseDirectory = dir }));

            Assert.That(zip.Entries.Count(e => e.FullName == "EPUB/avatars/default.jpg"), Is.EqualTo(1));
            Assert.That(Read(zip.GetEntry("EPUB/images/2-default.jpg")!), Is.EqualTo(new byte[] { 255, 216, 255, 1 }));
            Assert.That(Chapter(zip), Does.Contain("<img src=\"images/2-default.jpg\""));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static ZipArchive Open(byte[] epub) => new(new MemoryStream(epub), ZipArchiveMode.Read);

    private static string Chapter(ZipArchive zip) =>
        new StreamReader(zip.Entries.Single(e => e.FullName.StartsWith("EPUB/_", StringComparison.Ordinal) && e.FullName.EndsWith(".xhtml", StringComparison.Ordinal)).Open()).ReadToEnd();

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
