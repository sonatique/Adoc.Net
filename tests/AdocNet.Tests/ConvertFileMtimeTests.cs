using AdocNet;
using AdocNet.Converters.DocBook;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class ConvertFileMtimeTests
{
    private static AdocEngine NewEngine() =>
        new(new DocBookRenderer(), src => AdocParser.Parse(src).Document);

    [Test]
    public void ConvertFile_injects_docdate_from_file_mtime()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "= Title\n\nText\n");
            File.SetLastWriteTime(path, new DateTime(2026, 3, 9, 12, 0, 0));

            using var ms = new MemoryStream();
            NewEngine().ConvertFile(path, ms);
            var xml = System.Text.Encoding.UTF8.GetString(ms.ToArray());

            Assert.That(xml, Does.Contain("<date>2026-03-09</date>"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ConvertFile_does_not_override_explicit_revdate()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "= Title\n:revdate: 2026-04-01\n\nText\n");
            File.SetLastWriteTime(path, new DateTime(2026, 3, 9, 12, 0, 0));

            using var ms = new MemoryStream();
            NewEngine().ConvertFile(path, ms);
            var xml = System.Text.Encoding.UTF8.GetString(ms.ToArray());

            Assert.That(xml, Does.Contain("<date>2026-04-01</date>"));
            Assert.That(xml, Does.Not.Contain("<date>2026-03-09</date>"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ConvertFile_does_not_override_explicit_docdate()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "= Title\n:docdate: 2026-04-01\n\nText\n");
            File.SetLastWriteTime(path, new DateTime(2026, 3, 9, 12, 0, 0));

            using var ms = new MemoryStream();
            NewEngine().ConvertFile(path, ms);
            var xml = System.Text.Encoding.UTF8.GetString(ms.ToArray());

            Assert.That(xml, Does.Contain("<date>2026-04-01</date>"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ConvertFile_skips_mtime_injection_when_reproducible_set()
    {
        // :reproducible: opts out of file-mtime injection AND of date emission entirely.
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "= Title\n:reproducible:\n\nText\n");
            File.SetLastWriteTime(path, new DateTime(2026, 3, 9, 12, 0, 0));

            using var ms = new MemoryStream();
            NewEngine().ConvertFile(path, ms);
            var xml = System.Text.Encoding.UTF8.GetString(ms.ToArray());

            Assert.That(xml, Does.Not.Contain("<date>"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void Convert_string_does_not_inject_mtime()
    {
        // The string-path Convert() has no file context, so docdate falls back to
        // the parser's default (DateTime.Now) — which will not equal a fixed mtime.
        // This test locks the contract: ConvertFile is the only entry point that
        // performs file-mtime injection.
        using var ms = new MemoryStream();
        NewEngine().Convert("= Title\n\nText\n", ms);
        var xml = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // <date> is emitted (from parser's docdate default = today), but it must NOT
        // be the fixed test mtime — proving that string Convert doesn't inject.
        Assert.That(xml, Does.Not.Contain("<date>2026-03-09</date>"));
    }
}
