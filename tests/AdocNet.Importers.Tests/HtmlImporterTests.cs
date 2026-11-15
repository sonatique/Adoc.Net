namespace AdocNet.Importers.Tests;

[TestFixture]
public class HtmlImporterTests
{
    private static ImportResult Import(string html) => new HtmlImporter().ImportString(html);

    [Test]
    public void Head_title_and_meta_become_header_attributes()
    {
        var r = Import("<html><head><title>Page</title><meta name=description content='About it'><meta name=author content='Ada'>" +
                       "<script>var x;</script><style>p{}</style></head><body><h2>Part</h2><p>Text.</p></body></html>");
        Assert.That(r.AsciiDoc, Does.StartWith("= Page\n:description: About it\n:author: Ada\n"));
        Assert.That(r.AsciiDoc, Does.Contain("== Part"));
        Assert.That(r.AsciiDoc, Does.Not.Contain("var x"));
        Assert.That(r.Report.Issues.Select(i => i.Code), Does.Contain("html.script.dropped").And.Contain("html.style.dropped"));
    }

    [Test]
    public void Code_with_language_class_quotes_breaks_and_rules()
    {
        var r = Import("<pre><code class='language-rust'>fn main() {}\n</code></pre><blockquote><p>Q <em>e</em></p></blockquote><p>a<br>b</p><hr>");
        Assert.That(r.AsciiDoc, Does.Contain("[source,rust]\n----\nfn main() {}\n----"));
        Assert.That(r.AsciiDoc, Does.Contain("____\nQ _e_\n____"));
        Assert.That(r.AsciiDoc, Does.Contain("a +\nb"));
        Assert.That(r.AsciiDoc, Does.Contain("'''"));
    }

    [Test]
    public void Lists_nest_and_carry_extra_blocks()
    {
        var r = Import("<ol start=2><li>two<ul><li>inner</li></ul></li><li><p>para</p><p>more</p></li></ol><dl><dt>T</dt><dd>D</dd></dl>");
        Assert.That(r.AsciiDoc, Does.Contain("[start=2]\n. two\n** inner\n. para\n+\nmore"));
        Assert.That(r.AsciiDoc, Does.Contain("T:: D"));
    }

    [Test]
    public void Tables_keep_spans_header_and_caption()
    {
        var r = Import("<table><caption>Cap</caption><thead><tr><th>A</th><th>B</th><th>C</th></tr></thead>" +
                       "<tbody><tr><td colspan=2>wide</td><td rowspan=2>tall</td></tr><tr><td>x|y</td><td align=right>z</td></tr></tbody></table>");
        Assert.That(r.AsciiDoc, Does.Contain(".Cap\n[cols=\"1,1,1\",options=\"header\"]\n|===\n|A\n|B\n|C\n\n2+|wide\n.2+|tall\n\n|x\\|y\n>|z\n|==="));
        var table = r.Document.Children.OfType<AdocNet.Ast.TableNode>().Single();
        Assert.That(table.Children.OfType<AdocNet.Ast.TableRowNode>().Count(), Is.EqualTo(3), "the AsciiDoc re-parses as a 3-row table");
    }

    [Test]
    public void Links_anchors_and_data_uri_images()
    {
        const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
        var r = Import($"<p id=top>See <a href='#sec'>there</a>, <a href='doc.html'>doc</a>, <a href='mailto:a@b.c'>a@b.c</a>.</p>" +
                       $"<h2 id=sec>Sec</h2><p><img src='data:image/png;base64,{png}' alt='dot'></p><p><a href='#top'>up</a></p>");
        Assert.That(r.AsciiDoc, Does.Contain("[[top]]\nSee <<sec,there>>, link:doc.html[doc], mailto:a@b.c[]."));
        Assert.That(r.AsciiDoc, Does.Contain("[#sec]\n== Sec"));
        Assert.That(r.AsciiDoc, Does.Contain("image::media/image1.png[dot]"));
        Assert.That(r.Media, Has.Count.EqualTo(1));
        Assert.That(r.Media[0].Content.Take(4), Is.EqualTo(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }));
    }

    [Test]
    public void Form_controls_and_embeds_are_reported_as_lost()
    {
        var r = Import("<p>Keep</p><form><input name=q></form><iframe src='x'></iframe>");
        Assert.That(r.AsciiDoc.Trim(), Is.EqualTo("Keep"));
        Assert.That(r.Report.Issues.Where(i => i.Severity == ImportIssueSeverity.Loss).Select(i => i.Code),
            Is.EquivalentTo(new[] { "html.form.dropped", "html.iframe.dropped" }));
        Assert.That(r.Report.Fidelity, Is.LessThan(1));
    }

    [Test]
    public void Convert_file_writes_the_adoc_and_media_but_never_outside_the_output_folder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "adocnet-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var html = Path.Combine(dir, "in.html");
            File.WriteAllText(html, "<p><img src='data:image/gif;base64,R0lGODlhAQABAAAAACw='></p>");
            var r = new HtmlImporter().ConvertFile(html, Path.Combine(dir, "out", "doc.adoc"));
            Assert.That(File.ReadAllText(Path.Combine(dir, "out", "doc.adoc")), Does.Contain("image::media/image1.gif[]"));
            Assert.That(File.Exists(Path.Combine(dir, "out", "media", "image1.gif")), Is.True);

            var evil = new[] { new ImportMediaItem { Source = "x", RelativePath = "../escape.png", Content = [1] } };
            Assert.Throws<ImportException>(() => DocumentImporter.WriteMedia(evil, Path.Combine(dir, "out")));
            Assert.That(File.Exists(Path.Combine(dir, "escape.png")), Is.False);
        }
        finally { Directory.Delete(dir, true); }
    }
}
