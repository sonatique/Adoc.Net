using System.Text;
using AdocNet;
using AdocNet.Converters.Html;
using AdocNet.Parser;
using AdocNet.Tools.DifferentialTester;

// ── Configuration ──────────────────────────────────────────────────────
var repoRoot = FindRepoRoot(AppContext.BaseDirectory)
    ?? throw new InvalidOperationException("Could not find repository root (looking for AdocNet.slnx)");

var fixturesDir = Path.Combine(repoRoot, "spec", "fixtures");
var conformanceDir = Path.Combine(repoRoot, "spec", "conformance");
var outputDir = Path.Combine(repoRoot, "tools", "DifferentialTester", "output");
var reportPath = Path.Combine(repoRoot, "docs", "V0_8_DIFF_REPORT.md");

// ── Preflight ──────────────────────────────────────────────────────────
Console.WriteLine("AdocNet Differential Tester v0.8");
Console.WriteLine("================================");
Console.WriteLine();

var asciidoctorVersion = AsciidoctorRunner.GetVersion();
if (asciidoctorVersion is null)
{
    Console.Error.WriteLine("ERROR: Asciidoctor is not installed or not on PATH.");
    Console.Error.WriteLine("Install with: gem install asciidoctor");
    return 1;
}

Console.WriteLine($"Reference: {asciidoctorVersion}");
Console.WriteLine($"Fixtures:  {fixturesDir}");
Console.WriteLine($"Output:    {outputDir}");
Console.WriteLine();

// ── Discover corpus ────────────────────────────────────────────────────
var adocFiles = new List<(string Path, string Category)>();

if (Directory.Exists(fixturesDir))
{
    foreach (var file in Directory.EnumerateFiles(fixturesDir, "*.adoc", SearchOption.AllDirectories))
    {
        // Skip include fragments (files starting with _)
        if (Path.GetFileName(file).StartsWith('_'))
            continue;

        var relativePath = Path.GetRelativePath(fixturesDir, file);
        var category = Path.GetDirectoryName(relativePath) ?? "root";
        adocFiles.Add((file, category));
    }
}

if (Directory.Exists(conformanceDir))
{
    foreach (var file in Directory.EnumerateFiles(conformanceDir, "*.adoc", SearchOption.TopDirectoryOnly))
    {
        adocFiles.Add((file, "conformance"));
    }
}

adocFiles.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

Console.WriteLine($"Discovered {adocFiles.Count} .adoc files");
Console.WriteLine();

// ── Run comparisons ────────────────────────────────────────────────────
// Clean output dir from previous runs
if (Directory.Exists(outputDir))
    Directory.Delete(outputDir, recursive: true);
Directory.CreateDirectory(outputDir);

var renderer = new HtmlRenderer();
var results = new List<ComparisonResult>();
int processed = 0;

foreach (var (filePath, category) in adocFiles)
{
    processed++;
    var fileName = Path.GetFileNameWithoutExtension(filePath);
    Console.Write($"\r[{processed}/{adocFiles.Count}] {category}/{fileName}".PadRight(80));

    // Render with AdocNet
    string adocNetHtml;
    try
    {
        var sourceText = File.ReadAllText(filePath);
        var parseOptions = new ParseOptions { SourceFilePath = filePath };
        var parseResult = AdocParser.Parse(sourceText, parseOptions);
        adocNetHtml = renderer.RenderToString(parseResult.Document);
    }
    catch (Exception ex)
    {
        results.Add(new ComparisonResult(filePath, category, ComparisonStatus.AdocNetError, 0, null, ex.Message));
        continue;
    }

    // Render with Asciidoctor
    var asciidoctorResult = AsciidoctorRunner.Render(filePath);

    if (asciidoctorResult is null || asciidoctorResult.Html is null)
    {
        var error = asciidoctorResult?.TimedOut == true ? "Timed out" : (asciidoctorResult?.Stderr ?? "Unknown error");
        results.Add(new ComparisonResult(filePath, category, ComparisonStatus.AsciidoctorError, 0, null, error));
        continue;
    }

    // Normalize and compare
    var normalizedAdocNet = HtmlNormalizer.Normalize(adocNetHtml, HtmlSource.AdocNet);
    var normalizedAsciidoctor = HtmlNormalizer.Normalize(asciidoctorResult.Html, HtmlSource.Asciidoctor);

    var diff = DiffEngine.Compare(normalizedAsciidoctor, normalizedAdocNet);

    var status = diff.Identical ? ComparisonStatus.Identical
        : diff.Similarity >= 0.95 ? ComparisonStatus.AcceptableDiff
        : diff.Similarity >= 0.70 ? ComparisonStatus.MinorDiff
        : ComparisonStatus.MajorDiff;

    results.Add(new ComparisonResult(filePath, category, status, diff.Similarity, diff, null));

    // Save diff file for non-identical results
    if (!diff.Identical)
    {
        var diffFileName = $"{category.Replace(Path.DirectorySeparatorChar, '-').Replace(Path.AltDirectorySeparatorChar, '-')}--{fileName}.diff.txt";
        var diffPath = Path.Combine(outputDir, diffFileName);
        SaveDiff(diffPath, filePath, diff, normalizedAsciidoctor, normalizedAdocNet);
    }
}

Console.WriteLine();
Console.WriteLine();

// ── Generate report ────────────────────────────────────────────────────
var report = GenerateReport(results, asciidoctorVersion);
File.WriteAllText(reportPath, report);
Console.WriteLine($"Report written to: {reportPath}");

// ── Summary ────────────────────────────────────────────────────────────
var identical = results.Count(r => r.Status == ComparisonStatus.Identical);
var acceptable = results.Count(r => r.Status == ComparisonStatus.AcceptableDiff);
var minor = results.Count(r => r.Status == ComparisonStatus.MinorDiff);
var major = results.Count(r => r.Status == ComparisonStatus.MajorDiff);
var errors = results.Count(r => r.Status is ComparisonStatus.AdocNetError or ComparisonStatus.AsciidoctorError);

Console.WriteLine();
Console.WriteLine("Summary");
Console.WriteLine("───────");
Console.WriteLine($"  Identical:       {identical,4}  ({100.0 * identical / results.Count:F1}%)");
Console.WriteLine($"  Acceptable (≥95%): {acceptable,3}  ({100.0 * acceptable / results.Count:F1}%)");
Console.WriteLine($"  Minor diff (≥70%): {minor,3}  ({100.0 * minor / results.Count:F1}%)");
Console.WriteLine($"  Major diff (<70%): {major,3}  ({100.0 * major / results.Count:F1}%)");
Console.WriteLine($"  Errors:          {errors,4}  ({100.0 * errors / results.Count:F1}%)");
Console.WriteLine($"  Total:           {results.Count,4}");
Console.WriteLine();

return major > 0 || errors > 0 ? 1 : 0;

// ── Helper methods ─────────────────────────────────────────────────────

static string? FindRepoRoot(string startDir)
{
    var dir = startDir;
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir, "AdocNet.slnx")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return null;
}

static void SaveDiff(string path, string sourcePath, DiffResult diff, string expected, string actual)
{
    var sb = new StringBuilder();
    sb.AppendLine($"# Diff: {Path.GetFileName(sourcePath)}");
    sb.AppendLine($"# Similarity: {diff.Similarity:P1}");
    sb.AppendLine($"# --- Asciidoctor (expected)");
    sb.AppendLine($"# +++ AdocNet (actual)");
    sb.AppendLine();

    foreach (var line in diff.Lines)
    {
        var prefix = line.Op switch
        {
            DiffOp.Add => "+ ",
            DiffOp.Remove => "- ",
            DiffOp.Separator => "  ",
            _ => "  ",
        };
        sb.AppendLine($"{prefix}{line.Content}");
    }

    File.WriteAllText(path, sb.ToString());
}

static string GenerateReport(List<ComparisonResult> results, string asciidoctorVersion)
{
    var sb = new StringBuilder();
    sb.AppendLine("# v0.8 Differential Test Report");
    sb.AppendLine();
    sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
    sb.AppendLine($"Reference: {asciidoctorVersion}");
    sb.AppendLine();

    // Overall stats
    var identical = results.Count(r => r.Status == ComparisonStatus.Identical);
    var acceptable = results.Count(r => r.Status == ComparisonStatus.AcceptableDiff);
    var minor = results.Count(r => r.Status == ComparisonStatus.MinorDiff);
    var major = results.Count(r => r.Status == ComparisonStatus.MajorDiff);
    var errors = results.Count(r => r.Status is ComparisonStatus.AdocNetError or ComparisonStatus.AsciidoctorError);

    sb.AppendLine("## Overall");
    sb.AppendLine();
    sb.AppendLine("| Metric | Count | % |");
    sb.AppendLine("|---|---|---|");
    sb.AppendLine($"| Identical | {identical} | {100.0 * identical / results.Count:F1}% |");
    sb.AppendLine($"| Acceptable (≥95%) | {acceptable} | {100.0 * acceptable / results.Count:F1}% |");
    sb.AppendLine($"| Minor diff (≥70%) | {minor} | {100.0 * minor / results.Count:F1}% |");
    sb.AppendLine($"| Major diff (<70%) | {major} | {100.0 * major / results.Count:F1}% |");
    sb.AppendLine($"| Errors | {errors} | {100.0 * errors / results.Count:F1}% |");
    sb.AppendLine($"| **Total** | **{results.Count}** | |");
    sb.AppendLine();

    // Per-category breakdown
    var byCategory = results.GroupBy(r => r.Category).OrderBy(g => g.Key);

    sb.AppendLine("## By Category");
    sb.AppendLine();
    sb.AppendLine("| Category | Files | Identical | Acceptable | Minor | Major | Errors |");
    sb.AppendLine("|---|---|---|---|---|---|---|");

    foreach (var group in byCategory)
    {
        var cat = group.Key;
        var total = group.Count();
        var catIdentical = group.Count(r => r.Status == ComparisonStatus.Identical);
        var catAcceptable = group.Count(r => r.Status == ComparisonStatus.AcceptableDiff);
        var catMinor = group.Count(r => r.Status == ComparisonStatus.MinorDiff);
        var catMajor = group.Count(r => r.Status == ComparisonStatus.MajorDiff);
        var catErrors = group.Count(r => r.Status is ComparisonStatus.AdocNetError or ComparisonStatus.AsciidoctorError);
        sb.AppendLine($"| {cat} | {total} | {catIdentical} | {catAcceptable} | {catMinor} | {catMajor} | {catErrors} |");
    }

    sb.AppendLine();

    // List non-identical files
    var nonIdentical = results.Where(r => r.Status != ComparisonStatus.Identical).OrderBy(r => r.Similarity).ToList();

    if (nonIdentical.Count > 0)
    {
        sb.AppendLine("## Non-Identical Files");
        sb.AppendLine();
        sb.AppendLine("| File | Category | Status | Similarity | Notes |");
        sb.AppendLine("|---|---|---|---|---|");

        foreach (var r in nonIdentical)
        {
            var fileName = Path.GetFileNameWithoutExtension(r.FilePath);
            var status = r.Status switch
            {
                ComparisonStatus.AcceptableDiff => "Acceptable",
                ComparisonStatus.MinorDiff => "Minor",
                ComparisonStatus.MajorDiff => "**Major**",
                ComparisonStatus.AdocNetError => "AdocNet Error",
                ComparisonStatus.AsciidoctorError => "Asciidoctor Error",
                _ => r.Status.ToString(),
            };
            var sim = r.Status is ComparisonStatus.AdocNetError or ComparisonStatus.AsciidoctorError
                ? "N/A"
                : $"{r.Similarity:P1}";
            var notes = r.ErrorMessage ?? "";
            sb.AppendLine($"| {fileName} | {r.Category} | {status} | {sim} | {notes} |");
        }
    }

    return sb.ToString();
}

// ── Types ──────────────────────────────────────────────────────────────

record ComparisonResult(
    string FilePath,
    string Category,
    ComparisonStatus Status,
    double Similarity,
    DiffResult? Diff,
    string? ErrorMessage);

enum ComparisonStatus
{
    Identical,
    AcceptableDiff,
    MinorDiff,
    MajorDiff,
    AdocNetError,
    AsciidoctorError,
}
