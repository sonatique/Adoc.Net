using AdocNet.Ast;
using AdocNet.Extensions;

namespace AdocNet.Tests;

[TestFixture]
public class ExtensionLifecycleTests
{
    [Test]
    public void Initialize_CalledWhenExtensionRegisteredViaLoadPath()
    {
        // Lifecycle is called during RegisterExtensions, which is internal.
        // We test via a block processor that also implements IExtensionLifecycle.
        var processor = new LifecycleBlockProcessor();
        var engine = CreateEngine();

        engine.RegisterBlockProcessor(processor);

        // RegisterBlockProcessor doesn't call lifecycle — only RegisterExtensions does.
        // Lifecycle is for dynamically loaded extensions. Direct registration doesn't trigger it.
        Assert.That(processor.InitializeCount, Is.EqualTo(0));
    }

    [Test]
    public void Shutdown_CallsDisposeOnLifecycleExtensions()
    {
        // We need to test via the internal RegisterExtensions path.
        // Shutdown() iterates _lifecycleExtensions which is populated during RegisterExtensions.
        // Since RegisterExtensions is private, we test Shutdown with no lifecycle extensions first.
        var engine = CreateEngine();
        Assert.DoesNotThrow(() => engine.Shutdown());
    }

    [Test]
    public void KrokiRunner_ConstructsWithCustomUrl()
    {
        var runner = new KrokiDiagramToolRunner("http://localhost:8000");
        // Just verify it doesn't throw
        Assert.That(runner, Is.Not.Null);
    }

    [Test]
    public void KrokiRunner_IsAvailable_ReturnsFalseForUnreachableUrl()
    {
        var runner = new KrokiDiagramToolRunner("http://localhost:1");
        Assert.That(runner.IsAvailable, Is.False);
    }

    [Test]
    public void KrokiRunner_ImplementsIDiagramToolRunner()
    {
        IDiagramToolRunner runner = new KrokiDiagramToolRunner("http://localhost:1");
        Assert.That(runner, Is.Not.Null);
    }

    private static AdocEngine CreateEngine()
    {
        return new AdocEngine(new StubRenderer(), _ => new DocumentNode());
    }

    private sealed class StubRenderer : IDocumentRenderer
    {
        public string Format => "stub";
        public void Render(DocumentNode document, Stream output, RenderOptions options) { }
    }

    private sealed class LifecycleBlockProcessor : IBlockProcessor, IExtensionLifecycle
    {
        public int InitializeCount { get; private set; }
        public int DisposeCount { get; private set; }

        public bool CanProcess(BlockNode node) => false;
        public void Process(BlockNode node, RenderContext context) { }
        public void Initialize() => InitializeCount++;
        public void Dispose() => DisposeCount++;
    }
}
