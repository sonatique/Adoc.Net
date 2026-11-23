using AdocNet.Ast;
using AdocNet.Extensions;
using AdocNet.Parser;
using NUnit.Framework;

namespace AdocNet.Tests;

[TestFixture]
public class DiagramPassTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "adocnet-diagram-pass-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    private const string Doc = "= Doc\n\n== Flow\n\n.Login\n[mermaid#login.wide]\n----\ngraph TD; A-->B\n----\n\n====\n[source,plantuml]\n----\na -> b\n----\n====\n";

    [Test]
    public void Apply_before_generation_changes_nothing_and_counts_pending_diagrams()
    {
        var runner = new FileRunner(_dir);
        var doc = AdocParser.Parse(Doc).Document;
        var hash = doc.StructuralHash;

        Assert.That(new DiagramPass(runner, _dir).Apply(doc), Is.EqualTo(2));
        Assert.That(doc.StructuralHash, Is.EqualTo(hash));
        Assert.That(runner.Calls, Is.Empty, "Apply never runs the tool");
    }

    [Test]
    public async Task Generated_diagrams_become_images_with_the_block_metadata()
    {
        var pass = new DiagramPass(new FileRunner(_dir), _dir);
        var doc = AdocParser.Parse(Doc).Document;
        var section = doc.Children.OfType<SectionNode>().Single();
        var block = section.Children.OfType<DelimitedBlockNode>().First();
        var hashBefore = doc.StructuralHash;

        await pass.GenerateAsync(doc);
        Assert.That(pass.Apply(doc), Is.Zero);

        var image = section.Children.OfType<BlockImageNode>().Single(i => i.Id == "login");
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(image.Target), Is.True);
            Assert.That(image.Source, Is.EqualTo(block.Source));
            Assert.That(image.Title, Is.EqualTo("Login"));
            Assert.That(image.Roles, Is.EqualTo(new[] { "wide" }));
            Assert.That(doc.StructuralHash, Is.Not.EqualTo(hashBefore), "ancestors' cached hashes are invalidated");
        });
        var example = section.Children.OfType<DelimitedBlockNode>().Single(d => d.BlockKind == DelimitedBlockKind.Example);
        Assert.That(example.Children.Single(), Is.InstanceOf<BlockImageNode>(), "nested diagrams are replaced too");
    }

    [Test]
    public async Task Embedded_images_are_data_uris_that_html_writes_as_is()
    {
        var pass = new DiagramPass(new FileRunner(_dir), _dir);
        var doc = AdocParser.Parse("[mermaid]\n----\nA-->B\n----").Document;
        await pass.GenerateAsync(doc);

        pass.Apply(doc, embed: true);

        var image = (BlockImageNode)doc.Children.Single();
        Assert.That(image.Target, Is.EqualTo("data:image/png;base64,AQ=="));
        var html = new AdocNet.Converters.Html.HtmlRenderer().RenderToString(doc);
        Assert.That(html, Does.Contain("src=\"data:image/png;base64,AQ==\""));
    }

    [Test]
    public async Task Pdf_embeds_a_generated_diagram_kept_outside_the_document_folder()
    {
        // A 1×1 PNG: the runner's output, in a cache folder the document's folder does not contain.
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        var pass = new DiagramPass(new FileRunner(_dir) { Bytes = png }, _dir);
        var doc = AdocParser.Parse("[mermaid]\n----\nA-->B\n----").Document;
        await pass.GenerateAsync(doc);
        pass.Apply(doc);

        var documentFolder = Path.Combine(_dir, "doc");
        var pdf = System.Text.Encoding.Latin1.GetString(new AdocNet.Converters.Pdf.PdfRenderer().RenderToBytes(doc,
            new AdocNet.Converters.Pdf.PdfRenderOptions { BaseDirectory = documentFolder }));

        Assert.That(pdf, Does.Contain("/Subtype /Image"));
    }

    [Test]
    public async Task Each_diagram_is_generated_once_per_language_and_source()
    {
        var runner = new FileRunner(_dir);
        var pass = new DiagramPass(runner, _dir);

        await pass.GenerateAsync(AdocParser.Parse(Doc).Document);
        await pass.GenerateAsync(AdocParser.Parse(Doc).Document);
        // Same source in another language is another diagram.
        await pass.GenerateAsync(AdocParser.Parse("[graphviz]\n----\na -> b\n----").Document);

        Assert.That(runner.Calls, Is.EquivalentTo(new[] { "mermaid", "plantuml", "graphviz" }));
    }

    [Test]
    public async Task Generation_runs_off_the_calling_thread()
    {
        var runner = new FileRunner(_dir);
        int caller = Environment.CurrentManagedThreadId;
        await new DiagramPass(runner, _dir).GenerateAsync(AdocParser.Parse(Doc).Document);

        Assert.That(runner.Threads, Has.None.EqualTo(caller));
    }

    [Test]
    public async Task A_failed_diagram_stays_source_and_is_not_retried()
    {
        var runner = new FileRunner(_dir) { Fail = true };
        var pass = new DiagramPass(runner, _dir);
        var doc = AdocParser.Parse("[mermaid]\n----\nA-->\n----").Document;

        await pass.GenerateAsync(doc);
        await pass.GenerateAsync(doc);

        Assert.Multiple(() =>
        {
            Assert.That(pass.Apply(doc), Is.Zero);
            Assert.That(doc.Children.Single(), Is.InstanceOf<DelimitedBlockNode>());
            Assert.That(runner.Calls, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task An_image_deleted_since_is_generated_again()
    {
        var runner = new FileRunner(_dir);
        var pass = new DiagramPass(runner, _dir);
        var doc = AdocParser.Parse("[mermaid]\n----\nA-->B\n----").Document;
        await pass.GenerateAsync(doc);
        foreach (var file in Directory.GetFiles(_dir)) File.Delete(file);

        Assert.That(pass.Apply(doc), Is.EqualTo(1));
        await pass.GenerateAsync(doc);
        Assert.That(pass.Apply(doc), Is.Zero);
        Assert.That(doc.Children.Single(), Is.InstanceOf<BlockImageNode>());
        Assert.That(runner.Calls, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task A_runner_that_throws_leaves_the_block_as_source()
    {
        var pass = new DiagramPass(new ThrowingRunner(), _dir);
        var doc = AdocParser.Parse("[mermaid]\n----\nA-->B\n----").Document;

        await pass.GenerateAsync(doc);

        Assert.That(pass.Apply(doc), Is.Zero);
        Assert.That(doc.Children.Single(), Is.InstanceOf<DelimitedBlockNode>());
    }

    [Test]
    public async Task Per_language_runners_render_only_their_languages()
    {
        var runner = new FileRunner(_dir);
        var pass = new DiagramPass(new Dictionary<string, IDiagramToolRunner> { ["PlantUML"] = runner }, _dir);
        var doc = AdocParser.Parse(Doc).Document;

        Assert.That(pass.Apply(doc), Is.EqualTo(1), "the mermaid block is not a diagram for this pass");
        await pass.GenerateAsync(doc);
        pass.Apply(doc);

        Assert.That(runner.Calls, Is.EqualTo(new[] { "plantuml" }));
        Assert.That(doc.Descendants().OfType<BlockImageNode>().Count(), Is.EqualTo(1));
    }

    [Test]
    public void An_unavailable_runner_is_not_asked_to_generate()
    {
        var runner = new FileRunner(_dir) { Available = false };
        var pass = new DiagramPass(runner, _dir);
        var doc = AdocParser.Parse(Doc).Document;

        pass.GenerateAsync(doc).Wait();

        Assert.That(runner.Calls, Is.Empty);
        Assert.That(pass.Apply(doc), Is.Zero);
    }

    [Test]
    public void A_cancelled_generation_stops_before_the_next_diagram()
    {
        var runner = new FileRunner(_dir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(async () => await new DiagramPass(runner, _dir).GenerateAsync(AdocParser.Parse(Doc).Document, cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(runner.Calls, Is.Empty);
    }

    private sealed class FileRunner(string dir) : IDiagramToolRunner
    {
        public List<string> Calls { get; } = [];
        public List<int> Threads { get; } = [];
        public bool Fail { get; init; }
        public bool Available { get; init; } = true;
        public byte[] Bytes { get; init; } = [1];
        public bool IsAvailable => Available;

        public string? Generate(string language, string source, string outputDirectory)
        {
            lock (Calls)
            {
                Calls.Add(language);
                Threads.Add(Environment.CurrentManagedThreadId);
            }
            if (Fail) return null;
            var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(path, Bytes);
            return path;
        }
    }

    private sealed class ThrowingRunner : IDiagramToolRunner
    {
        public bool IsAvailable => true;
        public string? Generate(string language, string source, string outputDirectory) => throw new IOException("tool crashed");
    }
}

internal static class AstTestExtensions
{
    public static IEnumerable<AstNode> Descendants(this AstNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var d in child.Descendants())
                yield return d;
        }
    }
}
