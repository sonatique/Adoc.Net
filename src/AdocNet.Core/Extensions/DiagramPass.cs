using System.Collections.Concurrent;
using AdocNet.Ast;

namespace AdocNet.Extensions;

/// <summary>
/// Turns the diagram blocks of a parsed document (<c>[mermaid]</c>, <c>[plantuml,name,svg]</c>,
/// <c>[source,graphviz]</c>, a <c>```mermaid</c> fence...) into image blocks, for applications that
/// call the parser and the converters directly rather than through <see cref="AdocEngine"/>.
/// </summary>
/// <remarks>
/// <para>Nothing leaves the machine and no program starts unless the application passes a runner:
/// <see cref="KrokiDiagramToolRunner"/> sends the diagram source to a Kroki server, and
/// <see cref="ProcessDiagramToolRunner"/> starts a local program.</para>
/// <para>Typical preview loop: after each parse, <see cref="Apply"/> swaps in the images already made
/// (no I/O beyond a file check, so it is cheap on any thread). When it reports diagrams still pending,
/// start <see cref="GenerateAsync"/> and render again once it completes. Images are cached by diagram
/// language and source for the life of the instance, failures included, so a diagram is generated
/// once. Generation runs on a worker thread, one diagram at a time per instance.</para>
/// <para>The image format is the runner's (PNG for the Kroki and process runners as configured by
/// default); the target name and format given in the block's attribute list are not used. Apply changes
/// the document for rendering: do not write it back to AsciiDoc, which would keep the image paths.</para>
/// </remarks>
public sealed class DiagramPass
{
    // Diagram types Kroki renders, plus "dot", the usual alias of graphviz.
    private static readonly HashSet<string> KnownLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "actdiag", "blockdiag", "bpmn", "bytefield", "c4plantuml", "d2", "dbml", "ditaa", "dot", "erd",
        "excalidraw", "graphviz", "mermaid", "nomnoml", "nwdiag", "packetdiag", "pikchr", "plantuml",
        "rackdiag", "seqdiag", "structurizr", "svgbob", "symbolator", "tikz", "umlet", "vega",
        "vegalite", "wavedrom", "wireviz",
    };

    private readonly Func<string, IDiagramToolRunner?> _runnerFor;
    private readonly string _outputDirectory;
    private readonly ConcurrentDictionary<string, string?> _images = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Renders every known diagram type with <paramref name="runner"/> (typically Kroki).</summary>
    /// <param name="runner">Generates the images.</param>
    /// <param name="outputDirectory">Directory the images are written to; created when needed.</param>
    public DiagramPass(IDiagramToolRunner runner, string outputDirectory)
    {
        if (runner is null) throw new ArgumentNullException(nameof(runner));
        _runnerFor = language => KnownLanguages.Contains(language) ? runner : null;
        _outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
    }

    /// <summary>
    /// Renders only the diagram types named in <paramref name="runners"/>, each with its own runner
    /// (for example a local PlantUML for <c>plantuml</c> and Graphviz for <c>graphviz</c>).
    /// Other diagram blocks stay as source blocks.
    /// </summary>
    /// <param name="runners">Runner per diagram language; the language is matched without regard to case.</param>
    /// <param name="outputDirectory">Directory the images are written to; created when needed.</param>
    public DiagramPass(IReadOnlyDictionary<string, IDiagramToolRunner> runners, string outputDirectory)
    {
        if (runners is null) throw new ArgumentNullException(nameof(runners));
        var map = new Dictionary<string, IDiagramToolRunner>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in runners)
            map[pair.Key] = pair.Value ?? throw new ArgumentException("A runner is null.", nameof(runners));
        _runnerFor = language => map.TryGetValue(language, out var r) ? r : null;
        _outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
    }

    /// <summary>
    /// Replaces each diagram block whose image is already generated with a <see cref="BlockImageNode"/>
    /// (same source range, id, roles and title). Diagrams that failed stay as source blocks.
    /// Do not use the document from another thread while this runs.
    /// </summary>
    /// <param name="document">The parsed document, changed in place.</param>
    /// <param name="embed">
    /// False: the image target is the generated file's full path (Avalonia preview, PDF, DocBook).
    /// True: the target is the image itself as a <c>data:</c> URI, so an HTML or EPUB export needs no
    /// image file next to it.
    /// </param>
    /// <returns>The number of diagram blocks whose image is not generated yet.</returns>
    public int Apply(DocumentNode document, bool embed = false)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        int pending = 0;
        Replace(document, []);
        return pending;

        void Replace(AstNode parent, List<AstNode> ancestors)
        {
            for (int i = 0; i < parent.Children.Count; i++)
            {
                var child = parent.Children[i];
                if (child is DelimitedBlockNode block && Find(block) is { } diagram)
                {
                    if (!_images.TryGetValue(diagram.Key, out var path) || (path is not null && !File.Exists(path)))
                    {
                        _images.TryRemove(diagram.Key, out _); // an image deleted since is made again
                        pending++;
                    }
                    else if (path is not null && (embed ? DataUri(path) : path) is { } target)
                    {
                        parent.RemoveChildAt(i);
                        parent.InsertChild(i, ImageFor(block, target));
                        parent.InvalidateStructuralHash();
                        foreach (var ancestor in ancestors)
                            ancestor.InvalidateStructuralHash();
                    }
                    continue;
                }
                ancestors.Add(parent);
                Replace(child, ancestors);
                ancestors.RemoveAt(ancestors.Count - 1);
            }
        }
    }

    /// <summary>
    /// Generates, on a worker thread, the images of the document's diagram blocks not generated yet.
    /// The diagram sources are read before this method returns; the document is not touched afterwards.
    /// Cancelling stops before the next diagram.
    /// </summary>
    public Task GenerateAsync(DocumentNode document, CancellationToken cancellationToken = default)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var jobs = new List<Diagram>();
        Collect(document);
        if (jobs.Count == 0)
            return Task.CompletedTask;
        return Task.Run(async () =>
        {
            foreach (var job in jobs)
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!_images.ContainsKey(job.Key))
                        _images[job.Key] = Generate(job);
                }
                finally
                {
                    _gate.Release();
                }
            }
        }, cancellationToken);

        void Collect(AstNode node)
        {
            foreach (var child in node.Children)
            {
                if (child is DelimitedBlockNode block && Find(block) is { } diagram)
                {
                    if (!_images.ContainsKey(diagram.Key))
                        jobs.Add(diagram);
                }
                else
                {
                    Collect(child);
                }
            }
        }
    }

    private string? Generate(Diagram diagram)
    {
        try
        {
            return diagram.Runner.IsAvailable
                ? diagram.Runner.Generate(diagram.Language, diagram.Source, _outputDirectory)
                : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null; // the block stays as source
        }
    }

    private Diagram? Find(DelimitedBlockNode block)
    {
        var language = StyleOrLanguage(block);
        if (language is null || _runnerFor(language) is not { } runner)
            return null;
        if (language.Equals("dot", StringComparison.OrdinalIgnoreCase))
            language = "graphviz";
        language = language.ToLowerInvariant();
        var source = block.Content ?? "";
        return new Diagram(language + "\n" + source, language, source, runner);
    }

    /// <summary>True for a listing or literal block styled <c>[mermaid]</c> and the like, or a source block in a diagram language.</summary>
    internal static bool IsKnownDiagram(DelimitedBlockNode block) =>
        StyleOrLanguage(block) is { } language && KnownLanguages.Contains(language);

    private static string? StyleOrLanguage(DelimitedBlockNode block) => block.BlockKind switch
    {
        DelimitedBlockKind.Source => block.Language,
        DelimitedBlockKind.Listing or DelimitedBlockKind.Literal => block.Style,
        _ => null,
    };

    private static string? DataUri(string path)
    {
        var mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".svg" => "image/svg+xml",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            _ => null,
        };
        if (mime is null)
            return null;
        try
        {
            return "data:" + mime + ";base64," + Convert.ToBase64String(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // deleted or locked meanwhile: the block stays as source
        }
    }

    private static BlockImageNode ImageFor(DelimitedBlockNode block, string target) => new()
    {
        Target = target,
        Alt = block.Title ?? "Diagram",
        Title = block.Title,
        Id = block.Id,
        Reftext = block.Reftext,
        Roles = block.Roles,
        Source = block.Source,
    };

    private sealed record Diagram(string Key, string Language, string Source, IDiagramToolRunner Runner);
}
