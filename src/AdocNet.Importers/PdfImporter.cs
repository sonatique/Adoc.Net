using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace AdocNet.Importers;

/// <summary>
/// Recovers an AsciiDoc document from a PDF's text layer, approximately: a PDF stores positioned
/// glyphs, not structure, so structure is inferred and the report says so.
/// <list type="bullet">
/// <item>paragraphs: lines joined, hyphenation at line ends undone, continued across pages;</item>
/// <item>headings from font size relative to the body text (the document information
/// title, or a lone largest heading opening the document, becomes the title);</item>
/// <item>lists from bullet glyphs and numbering, nested by indentation; code from monospaced lines;</item>
/// <item>simple tables: consecutive lines split into the same aligned columns;</item>
/// <item>bold, italic and monospaced words as inline formatting; link annotations as links;</item>
/// <item>embedded images as media files (PNG, or JPEG as stored);</item>
/// <item>running headers and footers (text repeating at the page top or bottom) and page numbers dropped.</item>
/// </list>
/// Multi-column layouts, rotated text, form fields and vector graphics are not recovered.
/// </summary>
public sealed class PdfImporter : DocumentImporter
{
    public PdfImporter(ImportOptions? options = null) : base(options) { }

    public override ImportResult Import(Stream input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            input.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        PdfDocument document;
        try { document = PdfDocument.Open(bytes); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new ImportException("The input is not a readable PDF: " + ex.Message, ex);
        }
        using (document)
        {
            var builder = new AsciiDocBuilder(Options);
            var blocks = new PdfStructure(document, builder, Options).Convert();
            return Result(builder.Build(blocks), builder.Report, builder.Media);
        }
    }
}

/// <summary>The PDF text-layer reconstruction behind <see cref="PdfImporter"/>.</summary>
internal sealed class PdfStructure
{
    private sealed class Word
    {
        public required string Text;
        public double Left, Right;
        public bool Bold, Italic, Mono;
        public string Family = "";
        public string? Link;
        /// <summary>False when the word touches the previous one (a font change mid-word, e.g. <c>code</c> then a period).</summary>
        public bool SpaceBefore = true;
    }

    private sealed class Line
    {
        public int Page;
        public double Top, Bottom, Baseline, Left, Right, Size;
        public bool Bold, Italic, Mono;
        public string Family = "";
        public required List<Word> Words;
        public List<List<Word>> Segments = [];
        public string Text => string.Join(" ", Words.Select(w => w.Text));
        public string? ImagePath; // an image placeholder line
        public string? Admonition; // NOTE, TIP, IMPORTANT, WARNING or CAUTION label found on this line
    }

    private static readonly Regex Bullet = new(@"^([•◦▪▫‣⁃●○■□➢►▸–\-*·])$", RegexOptions.CultureInvariant);
    private static readonly Regex Number = new(@"^(\d{1,3}|[a-zA-Z]|[ivxIVX]{1,5})[.)]$", RegexOptions.CultureInvariant);

    private readonly PdfDocument _pdf;
    private readonly AsciiDocBuilder _b;
    private readonly ImportOptions _options;
    private ImportReport R => _b.Report;
    private double _bodySize = 10, _leading = 12, _contentLeft, _contentRight;
    private string _bodyFamily = "";

    /// <summary>A font's family: subset tag and style suffixes removed (<c>ABCDEF+NotoSerif-Bold</c> → <c>notoserif</c>).</summary>
    private static string Family(string font) =>
        Regex.Replace(Regex.Replace(font, @"^[A-Z]{6}\+", ""), @"[-,_ ]?(Bold|Italic|Oblique|Regular|Medium|Semibold|Light|Black|Heavy|BI|MT|PS).*$", "", RegexOptions.IgnoreCase)
             .ToLowerInvariant();

    public PdfStructure(PdfDocument pdf, AsciiDocBuilder builder, ImportOptions options)
    {
        _pdf = pdf;
        _b = builder;
        _options = options;
    }

    public List<string> Convert()
    {
        var info = _pdf.Information;
        if (!string.IsNullOrWhiteSpace(info.Title)) _b.Title = info.Title!.Trim();
        if (!string.IsNullOrWhiteSpace(info.Author)) _b.SetAttribute("author", info.Author!);
        if (!string.IsNullOrWhiteSpace(info.Keywords)) _b.SetAttribute("keywords", info.Keywords!);
        if (!string.IsNullOrWhiteSpace(info.Subject)) _b.SetAttribute("description", info.Subject!);

        var pages = new List<(Page Page, List<Line> Lines)>();
        for (int number = 1; number <= _pdf.NumberOfPages; number++)
        {
            R.Pages++;
            Page page;
            List<Line> lines;
            try
            {
                page = _pdf.GetPage(number);
                lines = Lines(page);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A damaged page loses its content, not the document.
                R.Lost("pdf.page.unreadable", "The page could not be read: " + ex.Message, number);
                continue;
            }
            if (lines.Count == 0 && page.NumberOfImages > 0)
                R.Lost("pdf.page.no-text", "The page has no text layer (a scan?); only its images were kept.", page.Number);
            pages.Add((page, lines));
        }
        DropRunningContent(pages);

        var all = pages.SelectMany(p => p.Lines).ToList();
        var textLines = all.Where(l => l.ImagePath is null && !l.Mono).ToList();
        if (textLines.Count > 0)
        {
            _bodySize = textLines.GroupBy(l => Math.Round(l.Size * 2) / 2).OrderByDescending(g => g.Sum(l => l.Text.Length)).First().Key;
            var gaps = new List<double>();
            for (int i = 1; i < textLines.Count; i++)
                if (textLines[i].Page == textLines[i - 1].Page && Math.Abs(textLines[i].Size - _bodySize) < 0.6 && Math.Abs(textLines[i - 1].Size - _bodySize) < 0.6)
                    gaps.Add(textLines[i].Baseline - textLines[i - 1].Baseline);
            var positive = gaps.Where(g => g > 0).OrderBy(g => g).ToList();
            _leading = positive.Count > 0 ? positive[positive.Count / 2] : _bodySize * 1.2;
            var body = textLines.Where(l => Math.Abs(l.Size - _bodySize) < 0.6).ToList();
            _bodyFamily = body.GroupBy(l => l.Family).OrderByDescending(g => g.Sum(l => l.Text.Length)).First().Key;
            _contentLeft = body.Min(l => l.Left);
            _contentRight = body.Max(l => l.Right);
        }
        R.Add(ImportIssueSeverity.Info, "pdf.structure.inferred", "Structure was inferred from fonts and positions; check headings, lists and tables.");
        TitlePage(all, pages.Count);
        Toc(all);
        return Blocks(all);
    }

    // ── Lines ───────────────────────────────────────────────────────────

    private List<Line> Lines(Page page)
    {
        double height = page.Height;
        var links = page.GetHyperlinks().Where(h => !string.IsNullOrEmpty(h.Uri)).ToList();
        var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.Letters.Count > 0).ToList();
        if (words.Any(w => w.Letters.Any(l => l.Value == "�")))
            R.Add(ImportIssueSeverity.Warning, "pdf.text.unmapped", "Some glyphs have no Unicode mapping and came out as replacement characters.", page.Number);

        var lines = new List<Line>();
        foreach (var w in words.OrderByDescending(w => w.Letters[0].StartBaseLine.Y).ThenBy(w => w.BoundingBox.Left))
        {
            double baseline = height - w.Letters[0].StartBaseLine.Y;
            double size = w.Letters.Average(l => l.PointSize);
            double top = baseline - 0.8 * size, bottom = baseline + 0.2 * size;
            var line = lines.LastOrDefault(l => Math.Min(l.Baseline + 0.2 * l.Size, bottom) - Math.Max(l.Baseline - 0.8 * l.Size, top) >= 0.5 * Math.Min(size, l.Size));
            if (line is null)
            {
                line = new Line { Page = page.Number, Baseline = baseline, Words = [] };
                lines.Add(line);
            }
            else if (size > line.Size + 0.1) line.Baseline = baseline; // the line's baseline is its largest text's
            var font = w.Letters[0].FontName ?? "";
            var details = w.Letters[0].FontDetails;
            var word = new Word
            {
                Text = w.Text,
                Family = Family(font),
                Left = w.BoundingBox.Left,
                Right = w.BoundingBox.Right,
                Bold = details?.IsBold == true || Regex.IsMatch(font, "Bold|Black|Heavy|Semibold|Demi", RegexOptions.IgnoreCase),
                Italic = details?.IsItalic == true || Regex.IsMatch(font, "Italic|Oblique", RegexOptions.IgnoreCase),
                Mono = IsMonospaced(font, words),
            };
            var center = w.BoundingBox.Centroid;
            var link = links.FirstOrDefault(h => h.Bounds.Left <= center.X && center.X <= h.Bounds.Right && h.Bounds.Bottom <= center.Y && center.Y <= h.Bounds.Top);
            word.Link = link?.Uri;
            line.Words.Add(word);
            line.Size = line.Words.Count == 1 ? size : Math.Max(line.Size, size);
            line.Top = line.Words.Count == 1 ? height - w.BoundingBox.Top : Math.Min(line.Top, height - w.BoundingBox.Top);
            line.Bottom = Math.Max(line.Bottom, height - w.BoundingBox.Bottom);
        }
        foreach (var line in lines)
        {
            line.Words.Sort((a, b) => a.Left.CompareTo(b.Left));
            for (int i = 1; i < line.Words.Count; i++)
                line.Words[i].SpaceBefore = line.Words[i].Left - line.Words[i - 1].Right > 0.12 * line.Size;
            line.Left = line.Words[0].Left;
            line.Right = line.Words[^1].Right;
            line.Bold = line.Words.All(w => w.Bold);
            line.Italic = line.Words.All(w => w.Italic);
            line.Mono = line.Words.All(w => w.Mono);
            line.Family = line.Words.GroupBy(w => w.Family).OrderByDescending(g => g.Sum(w => w.Text.Length)).First().Key;
            // Column segments: words separated by a gap much wider than a space.
            var segment = new List<Word> { line.Words[0] };
            line.Segments.Add(segment);
            for (int i = 1; i < line.Words.Count; i++)
            {
                if (line.Words[i].Left - line.Words[i - 1].Right > Math.Max(1.6 * line.Size, 12))
                    line.Segments.Add(segment = []);
                segment.Add(line.Words[i]);
            }
            if (line.Segments.Count >= 2 && line.Segments[0].Count == 1
                && Regex.IsMatch(line.Segments[0][0].Text, "^(NOTE|TIP|IMPORTANT|WARNING|CAUTION)$"))
            {
                line.Admonition = line.Segments[0][0].Text;
                line.Words.RemoveAt(0);
                line.Segments.RemoveAt(0);
                line.Left = line.Words[0].Left;
                line.Bold = line.Words.All(w => w.Bold);
                line.Italic = line.Words.All(w => w.Italic);
                line.Mono = line.Words.All(w => w.Mono);
            }
        }

        int imageIndex = 0;
        foreach (var image in page.GetImages())
        {
            imageIndex++;
            var path = Image(image, page.Number, imageIndex);
            if (path is null) continue;
            lines.Add(new Line
            {
                Page = page.Number, Top = height - image.BoundingBox.Top, Bottom = height - image.BoundingBox.Bottom,
                Baseline = height - image.BoundingBox.Bottom, Left = image.BoundingBox.Left, Right = image.BoundingBox.Right,
                Words = [], ImagePath = path,
            });
        }
        return lines.OrderBy(l => l.Top).ThenBy(l => l.Left).ToList();
    }

    private readonly Dictionary<string, bool> _monoFonts = new(StringComparer.Ordinal);

    /// <summary>
    /// A font is monospaced when its name says so or when its words of mixed letters have
    /// letters of one width (embedded fonts often have names like <c>mplus1mn</c>).
    /// </summary>
    private bool IsMonospaced(string font, List<UglyToad.PdfPig.Content.Word> words)
    {
        if (_monoFonts.TryGetValue(font, out var mono)) return mono;
        if (Regex.IsMatch(font, "Mono|Courier|Consol|Code|Menlo", RegexOptions.IgnoreCase)) return _monoFonts[font] = true;
        int uniform = 0, varied = 0;
        foreach (var w in words.Where(w => (w.Letters[0].FontName ?? "") == font && w.Letters.Count >= 3 && w.Letters.Select(l => l.Value).Distinct().Count() >= 3))
        {
            var widths = w.Letters.Select(l => l.Width).ToList();
            if (widths.Max() - widths.Min() < 0.03 * w.Letters[0].PointSize) uniform++; else varied++;
        }
        if (uniform + varied < 2) return false; // undecided on this page
        return _monoFonts[font] = uniform >= 4 * varied;
    }

    private readonly Dictionary<string, string> _imagesByHash = new(StringComparer.Ordinal);

    private string? Image(IPdfImage image, int page, int index)
    {
        byte[]? bytes = null;
        string ext = "png";
        if (image.TryGetPng(out var png)) bytes = png;
        else if (image.ImageDictionary.ContainsKey(UglyToad.PdfPig.Tokens.NameToken.Filter)
                 && image.ImageDictionary.Data[UglyToad.PdfPig.Tokens.NameToken.Filter.Data].ToString()!.Contains("DCTDecode"))
        {
            bytes = image.RawBytes.ToArray();
            ext = "jpg";
        }
        if (bytes is null)
        {
            R.Lost("pdf.image.unsupported", "An embedded image uses an encoding that could not be extracted.", page);
            return null;
        }
        string hash;
        using (var sha = System.Security.Cryptography.SHA256.Create()) hash = System.Convert.ToBase64String(sha.ComputeHash(bytes));
        if (_imagesByHash.TryGetValue(hash, out var existing)) return existing;
        var path = _b.AddMedia($"page {page.ToString(CultureInfo.InvariantCulture)} image {index.ToString(CultureInfo.InvariantCulture)}", bytes, ext);
        _imagesByHash[hash] = path;
        return path;
    }

    /// <summary>Drops lines (and images) in the top or bottom margin that repeat across pages, and lone page numbers there.</summary>
    private void DropRunningContent(List<(Page Page, List<Line> Lines)> pages)
    {
        bool InMargin(Line l, Page p) => l.Bottom < p.Height * 0.12 || l.Top > p.Height * 0.88;
        string Key(Line l) => l.ImagePath ?? Regex.Replace(l.Text, @"\d+", "#").ToLowerInvariant();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (page, lines) in pages)
            foreach (var key in lines.Where(l => InMargin(l, page)).Select(Key).Distinct())
                counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;

        int dropped = 0;
        int threshold = Math.Max(2, (int)Math.Ceiling(pages.Count * 0.5));
        foreach (var (page, lines) in pages)
        {
            dropped += lines.RemoveAll(l => InMargin(l, page)
                && (counts[Key(l)] >= threshold && pages.Count > 1
                    || l.ImagePath is null && Regex.IsMatch(l.Text, @"^(page\s+)?([0-9]+|[ivxlc]+)(\s*(of|/)\s*[0-9]+)?$|^[-–—]\s*[0-9]+\s*[-–—]$|(^|[\s|])[0-9]+\s*(of|/)\s*[0-9]+$", RegexOptions.IgnoreCase)));
        }
        if (dropped > 0)
            R.Add(ImportIssueSeverity.Info, "pdf.running-content.dropped", $"{dropped} running header/footer line(s) and page numbers were dropped.");
    }

    /// <summary>
    /// A first page holding little but the document's largest text is a title page: its largest
    /// text is the title (unless the PDF information has one) and the rest is reported, not kept.
    /// </summary>
    private void TitlePage(List<Line> lines, int pageCount)
    {
        var page1 = lines.Where(l => l.Page == 1 && l.ImagePath is null).ToList();
        if (pageCount < 2 || page1.Count == 0 || page1.Count > 12) return;
        double max = lines.Where(l => l.ImagePath is null).Max(l => l.Size);
        if (page1.Max(l => l.Size) < max - 0.1 || max < _bodySize * 1.5) return;
        var titleLines = page1.Where(l => l.Size >= max - 0.1).ToList();
        _b.Title ??= string.Join(" ", titleLines.Select(l => l.Text));
        var rest = page1.Except(titleLines).Select(l => l.Text).ToList();
        lines.RemoveAll(l => l.Page == 1);
        R.Add(ImportIssueSeverity.Warning, "pdf.title-page", rest.Count == 0
            ? "The title page became the document title."
            : "The title page became the document title; its other text was not kept: " + string.Join(" / ", rest));
    }

    private static readonly Regex Leader = new(@"(\.\s?){5,}\s*\S+$", RegexOptions.CultureInvariant);

    /// <summary>Table-of-contents lines (dot leaders and page numbers) are dropped and become <c>:toc:</c>.</summary>
    private void Toc(List<Line> lines)
    {
        var entries = lines.Where(l => l.ImagePath is null && Leader.IsMatch(l.Text)).ToList();
        if (entries.Count < 2) return;
        var pages = entries.Select(l => l.Page).Distinct().ToList();
        lines.RemoveAll(l => entries.Contains(l)
            || pages.Contains(l.Page) && Regex.IsMatch(Squash(l.Text), "^(tableofcontents|contents)$"));
        _b.SetAttribute("toc", "");
        R.Approximated("pdf.toc", "The table of contents was replaced by a generated one (:toc:).");
    }

    // ── Blocks ──────────────────────────────────────────────────────────

    // Headings are larger than the body text, or bold in a font other than the body's (a theme's
    // heading font at body size). A bold line in the body font stays a bold paragraph: in a PDF it
    // is as often emphasis as a run-in heading, and keeping it inline loses nothing.
    private bool IsHeading(Line l) =>
        l.ImagePath is null && !l.Mono && l.Text.Length < 160 && l.Segments.Count == 1
        && (l.Size >= _bodySize * 1.15
            || l.Bold && l.Size >= _bodySize * 0.95 && !Regex.IsMatch(l.Text, "[.:;,]$")
               && (l.Family != _bodyFamily || Regex.IsMatch(l.Text, @"^\d+(\.\d+)+\.?\s")));

    private static bool StartsItem(Line l) =>
        l.ImagePath is null && l.Words.Count > 1 && (Bullet.IsMatch(l.Words[0].Text) || Number.IsMatch(l.Words[0].Text));

    private static string StyleKey(Line l) => $"{Math.Round(l.Size * 2) / 2:0.0}{(l.Bold ? "b" : "")}";

    private List<string> Blocks(List<Line> lines)
    {
        // Heading levels: distinct heading styles, largest first.
        var headingStyles = lines.Where(IsHeading).GroupBy(StyleKey)
            .OrderByDescending(g => g.First().Size).ThenByDescending(g => g.First().Bold).Select(g => g.Key).ToList();
        int titleSkip = 0;
        var first = lines.FirstOrDefault(l => l.ImagePath is null);
        if (_b.Title is { } infoTitle)
        {
            // The information title usually repeats as the first heading: skip it there.
            int at = first is null ? -1 : lines.IndexOf(first);
            if (first is not null && IsHeading(first) && Squash(HeadingRun(lines, at).Text) == Squash(infoTitle))
            {
                titleSkip = at + HeadingRun(lines, at).Count;
                if (lines.Count(l => IsHeading(l) && StyleKey(l) == StyleKey(first)) == titleSkip - at) headingStyles.Remove(StyleKey(first));
            }
        }
        else if (_options.PromoteFirstHeadingToTitle && first is not null && IsHeading(first) && headingStyles.Count > 0
                 && StyleKey(first) == headingStyles[0] && lines.Count(l => IsHeading(l) && StyleKey(l) == headingStyles[0]) == HeadingRun(lines, lines.IndexOf(first)).Count)
        {
            var run = HeadingRun(lines, lines.IndexOf(first));
            _b.Title = run.Text;
            titleSkip = lines.IndexOf(first) + run.Count;
            headingStyles.RemoveAt(0);
        }

        var headingTexts = lines.Skip(titleSkip).Where(IsHeading).Select(l => l.Text).ToList();
        bool numbered = headingTexts.Count > 0 && headingTexts.Count(t => SectionNumber.IsMatch(t)) >= 0.6 * headingTexts.Count;
        if (numbered) _b.SetAttribute("sectnums", "");

        var blocks = new List<string>();
        int lastLevel = 0;
        for (int i = titleSkip; i < lines.Count;)
        {
            var line = lines[i];
            if (line.ImagePath is not null)
            {
                R.Images++;
                R.Mapped();
                blocks.Add($"image::{line.ImagePath}[]");
                i++;
                continue;
            }
            if (IsHeading(line))
            {
                var run = HeadingRun(lines, i);
                int level = Math.Min(5, Math.Min(headingStyles.IndexOf(StyleKey(line)) + 1, lastLevel + 1));
                lastLevel = Math.Max(1, level);
                R.Headings++;
                R.Mapped();
                var headingText = numbered ? SectionNumber.Replace(run.Text, "") : run.Text;
                blocks.Add($"{new string('=', lastLevel + 1)} {AsciiDocBuilder.Inline(headingText)}");
                i += run.Count;
                continue;
            }
            if (line.Mono)
            {
                int j = i;
                var code = new StringBuilder();
                double charWidth = line.Size * 0.6;
                double left = lines.Skip(i).TakeWhile(l => l.Mono && l.ImagePath is null).Min(l => l.Left);
                while (j < lines.Count && lines[j].Mono && lines[j].ImagePath is null && (j == i || Gap(lines[j - 1], lines[j]) < 2.5))
                {
                    if (j > i && Gap(lines[j - 1], lines[j]) > 1.4) code.Append('\n');
                    code.Append(new string(' ', Math.Max(0, (int)Math.Round((lines[j].Left - left) / charWidth))))
                        .Append(MonoText(lines[j])).Append('\n');
                    j++;
                }
                var text = code.ToString().TrimEnd('\n');
                var d = AsciiDocBuilder.Delimiter('-', text);
                R.CodeBlocks++;
                R.Mapped();
                blocks.Add($"{d}\n{text}\n{d}");
                i = j;
                continue;
            }
            if (Table(lines, i) is { } table)
            {
                blocks.Add(table.Text);
                i += table.Count;
                continue;
            }
            if (StartsItem(line))
            {
                var (list, count) = List(lines, i);
                blocks.Add(list);
                i += count;
                continue;
            }
            var (paragraph, used) = Paragraph(lines, i);
            R.Paragraphs++;
            R.Mapped();
            blocks.Add(AsciiDocBuilder.BlockStart(paragraph));
            i += used;
        }
        return blocks;
    }

    private static readonly Regex SectionNumber = new(@"^(\d+(\.\d+)*|[A-Z](\.\d+)*)\.?\s+(?=\S)", RegexOptions.CultureInvariant);

    private static string Squash(string s) => Regex.Replace(s, @"\W+", "").ToLowerInvariant();

    private (string Text, int Count) HeadingRun(List<Line> lines, int start)
    {
        var key = StyleKey(lines[start]);
        int n = 1;
        while (start + n < lines.Count && IsHeading(lines[start + n]) && StyleKey(lines[start + n]) == key
               && lines[start + n].Page == lines[start].Page && Gap(lines[start + n - 1], lines[start + n]) < 1.3 * lines[start].Size / _leading + 0.6)
            n++;
        return (string.Join(" ", lines.Skip(start).Take(n).Select(l => l.Text)), n);
    }

    /// <summary>Distance between consecutive baselines, in body leadings (large across pages).</summary>
    private double Gap(Line a, Line b) => a.Page != b.Page ? 100 : (b.Baseline - a.Baseline) / _leading;

    private static string MonoText(Line l)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < l.Words.Count; i++)
        {
            if (i > 0) sb.Append(' ', Math.Max(1, (int)Math.Round((l.Words[i].Left - l.Words[i - 1].Right) / (l.Size * 0.6))));
            sb.Append(l.Words[i].Text);
        }
        return sb.ToString();
    }

    private bool Continues(Line prev, Line next)
    {
        if (next.ImagePath is not null || IsHeading(next) || next.Mono || StartsItem(next)) return false;
        if (Math.Abs(next.Size - prev.Size) > 0.6) return false;
        if (prev.Page != next.Page)
            return !Regex.IsMatch(prev.Text, @"[.!?:]$") || prev.Right > _contentRight - 2 * prev.Size;
        double gap = Gap(prev, next);
        if (gap > 1.35 || gap <= 0) return false;
        // A short line ending a sentence closes the paragraph.
        return !(prev.Right < _contentRight - 4 * prev.Size && Regex.IsMatch(prev.Text, @"[.!?:]$"));
    }

    private (string Text, int Count) Paragraph(List<Line> lines, int start)
    {
        var words = new List<Word>(lines[start].Words);
        int n = 1;
        while (start + n < lines.Count && Continues(lines[start + n - 1], lines[start + n]))
        {
            Join(words, lines[start + n].Words);
            n++;
        }
        var label = lines.Skip(start).Take(n).Select(l => l.Admonition).FirstOrDefault(a => a is not null);
        if (label is not null) R.Approximated("pdf.admonition", "An admonition was recognised from its label.");
        return ((label is null ? "" : label + ": ") + Render(words), n);
    }

    private int _dehyphenated;

    private void Join(List<Word> words, List<Word> next)
    {
        var last = words[^1];
        if (last.Text.Length > 1 && last.Text.EndsWith("-", StringComparison.Ordinal) && char.IsLetter(last.Text[^2])
            && next.Count > 0 && next[0].Text.Length > 0 && char.IsLower(next[0].Text[0]))
        {
            words[^1] = new Word { Text = last.Text.Substring(0, last.Text.Length - 1) + next[0].Text, Bold = last.Bold, Italic = last.Italic, Mono = last.Mono, Link = last.Link };
            words.AddRange(next.Skip(1));
            if (_dehyphenated++ == 0)
                R.Add(ImportIssueSeverity.Info, "pdf.dehyphenated", "Words hyphenated at line ends were joined.");
            return;
        }
        words.AddRange(next);
    }

    /// <summary>Words to AsciiDoc: runs of bold / italic / monospaced words marked, links as macros.</summary>
    private string Render(List<Word> words)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < words.Count)
        {
            var w = words[i];
            if (sb.Length > 0 && w.SpaceBefore) sb.Append(' ');
            if (w.Link is { } link)
            {
                int j = i;
                while (j < words.Count && words[j].Link == link) j++;
                var label = Join(words, i, j);
                R.Links++;
                R.Mapped();
                sb.Append(Regex.IsMatch(link, @"^(https?|ftp|mailto):", RegexOptions.IgnoreCase) ? "" : "link:")
                  .Append(AsciiDocBuilder.Target(link)).Append('[').Append(label == link ? "" : AsciiDocBuilder.MacroText(label)).Append(']');
                i = j;
                continue;
            }
            int k = i + 1;
            while (k < words.Count && words[k].Link is null && words[k].Bold == w.Bold && words[k].Italic == w.Italic && words[k].Mono == w.Mono) k++;
            var run = Join(words, i, k);
            // Constrained marks need a non-word character (or a space) on both sides.
            bool tight = !w.SpaceBefore && sb.Length > 0 && char.IsLetterOrDigit(sb[sb.Length - 1])
                         || k < words.Count && !words[k].SpaceBefore && char.IsLetterOrDigit(words[k].Text[0]);
            string mark = w.Mono ? "`" : w.Bold && w.Italic ? "*_" : w.Bold ? "*" : w.Italic ? "_" : "";
            if (tight) mark = string.Concat(mark.Select(c => new string(c, 2)));
            if (w.Mono)
            {
                var body = run.Contains('+') ? run : "+" + run + "+";
                sb.Append(mark).Append(body).Append(new string(mark.Reverse().ToArray()));
            }
            else
                sb.Append(mark).Append(AsciiDocBuilder.Inline(run)).Append(new string(mark.Reverse().ToArray()));
            i = k;
        }
        return sb.ToString();
    }

    private static string Join(List<Word> words, int from, int to)
    {
        var sb = new StringBuilder(words[from].Text);
        for (int x = from + 1; x < to; x++)
        {
            if (words[x].SpaceBefore) sb.Append(' ');
            sb.Append(words[x].Text);
        }
        return sb.ToString();
    }

    private (string Text, int Count) List(List<Line> lines, int start)
    {
        var items = new List<(double Indent, bool Ordered, List<Word> Words)>();
        int i = start;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (i > start && IsHeading(line)) break;
            if (StartsItem(line) && (i == start || Gap(lines[i - 1], line) < 2.2 || lines[i - 1].Page != line.Page))
            {
                bool ordered = Number.IsMatch(line.Words[0].Text);
                // A list of the other kind after a paragraph-sized gap is the next list, not a nested one.
                if (items.Count > 0 && ordered != items[0].Ordered && Gap(lines[i - 1], line) > 1.3) break;
                items.Add((line.Words[0].Left, ordered, line.Words.Skip(1).ToList()));
                i++;
                continue;
            }
            // Continuation: indented past the marker, close below the previous line.
            var prev = lines[i - 1];
            if (items.Count > 0 && line.ImagePath is null && !IsHeading(line) && !line.Mono
                && line.Left > items[^1].Indent - 1 && (Gap(prev, line) < 1.35 && Gap(prev, line) > 0 || prev.Page != line.Page && line.Left > _contentLeft + 4))
            {
                Join(items[^1].Words, line.Words);
                i++;
                continue;
            }
            break;
        }

        var indents = items.Select(it => Math.Round(it.Indent / 4) * 4).Distinct().OrderBy(x => x).ToList();
        var sb = new StringBuilder();
        foreach (var (indent, ordered, words) in items)
        {
            int depth = Math.Min(5, indents.IndexOf(Math.Round(indent / 4) * 4) + 1);
            R.ListItems++;
            R.Mapped();
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(new string(ordered ? '.' : '*', depth)).Append(' ').Append(words.Count == 0 ? "{empty}" : Render(words));
        }
        return (sb.ToString(), i - start);
    }

    /// <summary>
    /// Rows of two or more aligned columns. A line close below a row whose segments all sit in
    /// that row's columns continues its cells (wrapped cell text); a new row starts after a larger gap.
    /// </summary>
    private (string Text, int Count)? Table(List<Line> lines, int start)
    {
        var first = lines[start];
        if (first.ImagePath is not null || first.Segments.Count < 2 || StartsItem(first)) return null;
        var columns = first.Segments.Select(seg => seg[0].Left).ToList();
        int Column(List<Word> seg, double size) => columns.FindIndex(c => Math.Abs(c - seg[0].Left) < 2 * size);

        var rows = new List<List<Word>[]> { Row(first) };
        int n = 1;
        while (start + n < lines.Count)
        {
            var l = lines[start + n];
            var prev = lines[start + n - 1];
            if (l.ImagePath is not null || IsHeading(l) || l.Page != prev.Page) break;
            var cols = l.Segments.Select(seg => Column(seg, l.Size)).ToList();
            if (cols.Any(c => c < 0)) break;
            double gap = Gap(prev, l);
            if (gap > 3.5) break;
            if (cols[0] == 0)
                rows.Add(Row(l));
            else if (gap <= 1.4)
                for (int k = 0; k < cols.Count; k++) Join(rows[^1][cols[k]], l.Segments[k]);
            else break;
            n++;
        }
        if (rows.Count < 2) return null;

        List<Word>[] Row(Line l)
        {
            var row = new List<Word>[columns.Count];
            for (int k = 0; k < row.Length; k++) row[k] = [];
            foreach (var seg in l.Segments) row[Math.Max(0, Column(seg, l.Size))].AddRange(seg);
            return row;
        }

        R.Tables++;
        R.Approximated("pdf.table.inferred", "A table was inferred from aligned columns; check merged cells.");
        var sb = new StringBuilder();
        bool header = rows[0].All(c => c.All(w => w.Bold)) && !rows[1].All(c => c.All(w => w.Bold));
        sb.Append("[cols=\"").Append(string.Join(",", Enumerable.Repeat("1", columns.Count))).Append('"');
        if (header) sb.Append(",options=\"header\"");
        sb.Append("]\n|===\n");
        for (int r = 0; r < rows.Count; r++)
        {
            foreach (var cell in rows[r])
            {
                R.TableCells++;
                R.Mapped();
                var text = cell.Count == 0 ? "" : Render(header && r == 0 ? cell.Select(w => new Word { Text = w.Text, Mono = w.Mono, Link = w.Link }).ToList() : cell);
                sb.Append('|').Append(text.Replace("|", "\\|")).Append('\n');
            }
            if (r < rows.Count - 1) sb.Append('\n');
        }
        sb.Append("|===");
        return (sb.ToString(), n);
    }
}
