using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using AdocNet.Importers.Html;

namespace AdocNet.Importers;

/// <summary>
/// Imports Markdown (CommonMark plus GitHub Flavored Markdown: pipe tables with alignment, task
/// lists, strikethrough, autolinks, footnotes) and YAML front matter. Markdig parses and renders
/// the Markdown; the HTML importer turns that into AsciiDoc, so both formats map the same way.
/// Raw HTML blocks become <c>++++</c> passthrough blocks; front matter keys become header
/// attributes (<c>title</c> the document title, <c>date</c> <c>:revdate:</c>, <c>version</c> <c>:revnumber:</c>).
/// </summary>
public sealed class MarkdownImporter : DocumentImporter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UsePipeTables()
        .UseGridTables()
        .UseTaskLists()
        .UseFootnotes()
        .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Strikethrough)
        .UseAutoLinks()
        .UseDefinitionLists()
        .Build();

    public MarkdownImporter(ImportOptions? options = null) : base(options) { }

    /// <summary>Imports Markdown from a stream (UTF-8 unless a BOM says otherwise).</summary>
    public override ImportResult Import(Stream input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return ImportString(reader.ReadToEnd());
    }

    /// <summary>Imports Markdown source text.</summary>
    public ImportResult ImportString(string markdown)
    {
        if (markdown is null) throw new ArgumentNullException(nameof(markdown));
        var builder = new AsciiDocBuilder(Options);
        var parsed = Markdown.Parse(markdown, Pipeline);

        if (parsed.Descendants<YamlFrontMatterBlock>().FirstOrDefault() is { } frontMatter)
            FrontMatter(frontMatter.Lines.ToString(), builder);

        var html = new StringWriter();
        var renderer = new HtmlRenderer(html);
        Pipeline.Setup(renderer);
        var htmlBlocks = renderer.ObjectRenderers.FindExact<HtmlBlockRenderer>();
        if (htmlBlocks is not null) renderer.ObjectRenderers.Remove(htmlBlocks);
        renderer.ObjectRenderers.Add(new PassthroughBlockRenderer(builder.Report));
        renderer.Render(parsed);
        html.Flush();

        using var document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><body>" + html + "</body></html>");
        var blocks = HtmlImporter.ConvertDocument(document, builder, Options, readHead: false);
        return Result(builder.Build(blocks), builder.Report, builder.Media);
    }

    /// <summary>Raw HTML blocks become marked &lt;pre&gt; elements the HTML converter turns into passthrough blocks.</summary>
    private sealed class PassthroughBlockRenderer(ImportReport report) : HtmlObjectRenderer<HtmlBlock>
    {
        protected override void Write(HtmlRenderer renderer, HtmlBlock block)
        {
            if (block.Type == HtmlBlockType.Comment)
            {
                report.Add(ImportIssueSeverity.Info, "markdown.html-comment.dropped", "An HTML comment was dropped.");
                return;
            }
            // Scripts and styles are never document content; passing them through would run them in HTML output.
            if (block.Type == HtmlBlockType.ScriptPreOrStyle
                && Regex.IsMatch(block.Lines.ToString(), @"^\s*<(script|style)\b", RegexOptions.IgnoreCase))
            {
                report.Add(ImportIssueSeverity.Info, "markdown.script.dropped", "A <script> or <style> block was dropped.");
                return;
            }
            renderer.Write($"<pre {HtmlConverter.PassthroughAttribute}=\"\">");
            renderer.WriteLeafRawLines(block, true, true);
            renderer.Write("</pre>");
        }
    }

    /// <summary>
    /// Front matter: scalar <c>key: value</c> pairs and <c>- item</c> lists (joined with commas).
    /// ponytail: a line-based reader, not a YAML parser; nested maps and block scalars are reported and skipped.
    /// </summary>
    internal static void FrontMatter(string yaml, AsciiDocBuilder builder) =>
        FrontMatter(yaml,
            (name, value) => { if (name == "title") builder.Title = value; else builder.SetAttribute(name, value); },
            message => builder.Report.Add(ImportIssueSeverity.Warning, "front-matter.unsupported", message));

    /// <summary>
    /// Reads front matter into AsciiDoc attribute names: <paramref name="set"/> receives <c>title</c>
    /// (the document title, only when not empty), <c>author</c>, <c>keywords</c>, <c>revdate</c>,
    /// <c>revnumber</c> or the key itself; <paramref name="skipped"/> receives a message per skipped line.
    /// </summary>
    internal static void FrontMatter(string yaml, Action<string, string> set, Action<string> skipped)
    {
        string? key = null;
        var values = new List<string>();
        void Flush()
        {
            if (key is null) return;
            var value = string.Join(", ", values.Where(v => v.Length > 0));
            switch (key.ToLowerInvariant())
            {
                case "title": if (value.Length > 0) set("title", value); break;
                case "authors": case "author": set("author", value); break;
                case "tags": case "keywords": set("keywords", value); break;
                case "date": set("revdate", value); break;
                case "version": set("revnumber", value); break;
                default: if (value.Length > 0) set(key, value); break;
            }
            key = null;
            values.Clear();
        }

        foreach (var rawLine in yaml.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal) || line is "---" or "...") continue;
            var item = Regex.Match(line, @"^\s+-\s+(.*)$");
            if (item.Success && key is not null) { values.Add(Unquote(item.Groups[1].Value)); continue; }
            var pair = Regex.Match(line, @"^([A-Za-z0-9_][\w.\-]*)\s*:\s*(.*)$");
            if (pair.Success)
            {
                Flush();
                key = pair.Groups[1].Value;
                var value = pair.Groups[2].Value.Trim();
                if (value is "|" or ">" or "|-" or ">-")
                {
                    skipped($"Front matter key '{key}' uses a block scalar and was skipped.");
                    key = null;
                }
                else if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
                    values.AddRange(value.Substring(1, value.Length - 2).Split(',').Select(v => Unquote(v.Trim())));
                else if (value.Length > 0) values.Add(Unquote(value));
                continue;
            }
            skipped($"Front matter line '{line.Trim()}' was skipped.");
            Flush();
        }
        Flush();
    }

    private static string Unquote(string value) =>
        value.Length >= 2 && (value[0] == '"' && value[^1] == '"' || value[0] == '\'' && value[^1] == '\'') ? value.Substring(1, value.Length - 2) : value;
}
