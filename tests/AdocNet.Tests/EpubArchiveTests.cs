using System.IO.Compression;
using AdocNet.Converters.Epub;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// The EPUB file is assembled from cached per-entry pieces when the output stream is seekable:
/// it must be exactly the archive ZipArchive writes for the same entries.
/// </summary>
[TestFixture]
public class EpubArchiveTests
{
    private const string Book = "= Book Title\nAda Writer\n:doctype: book\n\n== One\n\nFirst *chapter*.\n\n== Two\n\nSecond chapter.\n";

    private static IEnumerable<TestCaseData> Cases()
    {
        yield return new TestCaseData("= Article\n\nJust a paragraph.\n", null, 0).SetName("Article");
        yield return new TestCaseData(Book, null, 0).SetName("Book");
        yield return new TestCaseData(Book, "h1 { color: red; }", 0).SetName("BookWithCustomCss");
        yield return new TestCaseData(Book, null, 37).SetName("BookAfterExistingBytes");
    }

    [TestCaseSource(nameof(Cases))]
    public void Seekable_output_is_the_archive_ZipArchive_writes_for_its_entries(string adoc, string? css, int prefix)
    {
        var doc = AdocParser.Parse(adoc).Document;
        using var output = new MemoryStream();
        output.Write(new byte[prefix], 0, prefix);
        new EpubRenderer().Render(doc, output, new EpubRenderOptions { CustomCss = css });
        var actual = output.ToArray();

        // Rewrite the same entries, in the same order, with ZipArchive after the same prefix.
        var entries = ReadEntries(actual);
        using var expected = new MemoryStream();
        expected.Write(new byte[prefix], 0, prefix);
        using (var zip = new ZipArchive(expected, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data, time) in entries)
            {
                var entry = zip.CreateEntry(name, name == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                entry.LastWriteTime = time;
                using var stream = entry.Open();
                new MemoryStream(data).CopyTo(stream);
            }
        }

        Assert.That(entries.Select(e => e.Name), Does.Contain("EPUB/fonts/awesome/fa-solid-900.ttf").And.Contain("EPUB/package.opf"));
        Assert.That(actual, Is.EqualTo(expected.ToArray()));
    }

    [Test]
    public void Non_seekable_output_has_the_same_entries()
    {
        var doc = AdocParser.Parse(Book).Document;
        var seekable = new EpubRenderer().RenderToBytes(doc);
        using var sink = new MemoryStream();
        new EpubRenderer().Render(doc, new WriteOnlyStream(sink), RenderOptions.Default);

        var a = ReadEntries(seekable);
        var b = ReadEntries(sink.ToArray());
        Assert.That(b.Select(e => e.Name), Is.EqualTo(a.Select(e => e.Name)));
        for (int i = 0; i < a.Count; i++)
            Assert.That(b[i].Data, Is.EqualTo(a[i].Data), a[i].Name);
    }

    [Test]
    public void Mimetype_is_the_first_entry_and_stored()
    {
        var bytes = new EpubRenderer().RenderToBytes(AdocParser.Parse(Book).Document);
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var first = zip.Entries[0];
        Assert.That(first.FullName, Is.EqualTo("mimetype"));
        Assert.That(first.CompressedLength, Is.EqualTo(first.Length), "stored, not deflated");
    }

    private static List<(string Name, byte[] Data, DateTimeOffset Time)> ReadEntries(byte[] epub)
    {
        using var zip = new ZipArchive(new MemoryStream(epub), ZipArchiveMode.Read);
        var list = new List<(string, byte[], DateTimeOffset)>();
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            using var data = new MemoryStream();
            stream.CopyTo(data);
            list.Add((entry.FullName, data.ToArray(), entry.LastWriteTime));
        }
        return list;
    }

    private sealed class WriteOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}
