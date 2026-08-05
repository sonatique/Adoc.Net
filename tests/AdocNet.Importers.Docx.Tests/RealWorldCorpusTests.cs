using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AdocNet.Importers.Docx.Tests;

/// <summary>
/// End-to-end check against real Word documents: every word in the document
/// must still be there after import → emit → parse → render. Synthetic
/// fixtures cannot cover what actual Word producers emit (rsid-split runs,
/// content controls, floating shapes, mixed numbering), so this fixture runs
/// over a directory of .docx files supplied through the
/// <c>ADOCNET_DOCX_CORPUS</c> environment variable and is skipped when it is
/// not set.
/// </summary>
[TestFixture]
public class RealWorldCorpusTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static IEnumerable<TestCaseData> CorpusFiles()
    {
        var directory = Environment.GetEnvironmentVariable("ADOCNET_DOCX_CORPUS");
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            yield return new TestCaseData((string?)null).SetName("corpus not configured");
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.docx", SearchOption.AllDirectories))
        {
            // "~$name.docx" is Word's owner-lock file, not a document.
            if (Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal)) continue;
            yield return new TestCaseData(file).SetName(Path.GetFileName(file));
        }
    }

    [TestCaseSource(nameof(CorpusFiles))]
    public void EveryWordSurvivesTheRoundTrip(string? path)
    {
        if (path is null)
        {
            Assert.Ignore("Set ADOCNET_DOCX_CORPUS to a directory of .docx files to run this fixture.");
            return;
        }

        var result = new DocxImporter().ImportFile(path);
        var adoc = new AdocNet.Emitter.AsciidocEmitter().Emit(result.Document);

        // The HTML fragment renderer omits the document header, so the title
        // is compared from the AST.
        var rendered = (result.Document.Title ?? string.Empty) + " " + ImportHarness.RenderedText(adoc);

        var expected = WordBag(ExtractDocumentText(path));
        var actual = WordBag(rendered);

        // Word splits words across runs at will, and both sides then differ in
        // where a word boundary lands (a footnote marker or an image between
        // two halves of a word, say). A word that is present in the rendered
        // text once whitespace is ignored is not lost content, so it is only
        // counted when it is missing from that form too.
        var compact = new string(rendered.Where(c => !char.IsWhiteSpace(c)).ToArray());

        var missing = new List<string>();
        foreach (var pair in expected)
        {
            actual.TryGetValue(pair.Key, out var seen);
            if (seen == 0 && compact.Contains(pair.Key)) continue;
            for (var i = seen; i < pair.Value; i++) missing.Add(pair.Key);
        }

        Assert.That(missing, Is.Empty,
            $"{Path.GetFileName(path)}: {missing.Count} word occurrence(s) lost, first few: "
            + string.Join(", ", missing.Take(15)));

        // Markup must not leak into the rendered text either: a passthrough or
        // table delimiter showing up means an escape did not take.
        Assert.That(rendered, Does.Not.Contain("+++"), $"{Path.GetFileName(path)}: passthrough markup leaked");
        Assert.That(rendered, Does.Not.Contain("|==="), $"{Path.GetFileName(path)}: table delimiter leaked");
    }

    /// <summary>
    /// Words of the document as Word stores them: the text of every run in the
    /// main part and in the footnote/endnote parts, which is exactly the text
    /// a reader sees.
    /// </summary>
    private static string ExtractDocumentText(string path)
    {
        using var stream = File.OpenRead(path);
        using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);

        var sb = new StringBuilder();
        foreach (var partName in new[] { "word/document.xml", "word/footnotes.xml", "word/endnotes.xml" })
        {
            var entry = archive.GetEntry(partName);
            if (entry is null) continue;

            using var part = entry.Open();
            var document = XDocument.Load(part);
            if (document.Root is not null) Walk(document.Root, sb);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Appends an element's text, inserting a separator only where Word itself
    /// has one. Runs inside a paragraph are contiguous — Word splits them
    /// mid-word constantly — while paragraphs, cells and floating shapes are
    /// separate pieces of text and get a space on both sides.
    /// </summary>
    private static void Walk(XElement element, StringBuilder sb)
    {
        var name = element.Name;

        if (name == W + "t")
        {
            sb.Append(element.Value);
            return;
        }

        if (name == W + "tab" || name == W + "br" || name == W + "cr")
        {
            sb.Append(' ');
            return;
        }

        // A non-breaking hyphen is a character of the word, exactly as the
        // importer treats it.
        if (name == W + "noBreakHyphen")
        {
            sb.Append('-');
            return;
        }

        // Word caches the generated table of contents as ordinary paragraphs;
        // the importer drops that snapshot because a backend regenerates it.
        if (name == W + "p" && IsTableOfContentsEntry(element)) return;

        // mc:Fallback is the legacy copy of the mc:Choice content beside it;
        // the importer reads Choice, so counting both would double up.
        if (name == Mc + "Fallback") return;

        var separates = name == W + "p" || name == W + "tc" || name == W + "txbxContent"
                        || name == W + "pict" || name == W + "drawing";

        if (separates) sb.Append(' ');
        foreach (var child in element.Elements()) Walk(child, sb);
        if (separates) sb.Append(' ');
    }

    /// <summary>
    /// Word caches the generated table of contents as ordinary paragraphs
    /// styled TOC1..TOC9. The importer drops that snapshot because an AsciiDoc
    /// backend regenerates it, so it must not count as lost text.
    /// </summary>
    private static bool IsTableOfContentsEntry(XElement? paragraph)
    {
        var style = paragraph?.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value;
        return style is not null
               && style.StartsWith("TOC", StringComparison.OrdinalIgnoreCase)
               && style.Length > 3
               && char.IsDigit(style[style.Length - 1]);
    }

    /// <summary>
    /// Splits on whitespace and drops punctuation-only tokens, so differences
    /// in how a renderer spaces markup do not register as content loss.
    /// </summary>
    private static Dictionary<string, int> WordBag(string text)
    {
        var bag = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(text, @"[\p{L}\p{N}][\p{L}\p{N}'’\-_.]*"))
        {
            var word = match.Value.TrimEnd('.', '-', '_');
            if (word.Length == 0) continue;
            bag.TryGetValue(word, out var count);
            bag[word] = count + 1;
        }

        return bag;
    }
}
