using AdocNet.Extensions;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class DependencyResolverTests
{
    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == value) return i;
        return -1;
    }

    [Test]
    public void Resolve_EmptyInput_ReturnsEmpty()
    {
        var result = DependencyResolver.Resolve(
            Array.Empty<(string, IReadOnlyList<string>)>());

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Resolve_SingleExtension_NoDeps_ReturnsSingle()
    {
        var input = new (string, IReadOnlyList<string>)[]
        {
            ("alpha", Array.Empty<string>())
        };

        var result = DependencyResolver.Resolve(input);

        Assert.That(result, Is.EqualTo(new[] { "alpha" }));
    }

    [Test]
    public void Resolve_NoDependencies_PreservesInputOrder()
    {
        var input = new (string, IReadOnlyList<string>)[]
        {
            ("charlie", Array.Empty<string>()),
            ("alpha", Array.Empty<string>()),
            ("bravo", Array.Empty<string>())
        };

        var result = DependencyResolver.Resolve(input);

        // With no deps, all have in-degree 0, seeded in input order
        Assert.That(result, Is.EqualTo(new[] { "charlie", "alpha", "bravo" }));
    }

    [Test]
    public void Resolve_ADependsOnB_BLoadedFirst()
    {
        var input = new (string, IReadOnlyList<string>)[]
        {
            ("A", new[] { "B" }),
            ("B", Array.Empty<string>())
        };

        var result = DependencyResolver.Resolve(input);

        Assert.That(IndexOf(result,"B"), Is.LessThan(IndexOf(result,"A")));
    }

    [Test]
    public void Resolve_LinearChain_CorrectOrder()
    {
        // C depends on B, B depends on A
        var input = new (string, IReadOnlyList<string>)[]
        {
            ("C", new[] { "B" }),
            ("A", Array.Empty<string>()),
            ("B", new[] { "A" })
        };

        var result = DependencyResolver.Resolve(input);

        Assert.That(IndexOf(result,"A"), Is.LessThan(IndexOf(result,"B")));
        Assert.That(IndexOf(result,"B"), Is.LessThan(IndexOf(result,"C")));
    }

    [Test]
    public void Resolve_DiamondDependency_DependenciesBeforeDependents()
    {
        // A depends on B and C; B depends on D; C depends on D
        var input = new (string, IReadOnlyList<string>)[]
        {
            ("A", new[] { "B", "C" }),
            ("B", new[] { "D" }),
            ("C", new[] { "D" }),
            ("D", Array.Empty<string>())
        };

        var result = DependencyResolver.Resolve(input);

        // D must be first; A must be last
        Assert.That(result[0], Is.EqualTo("D"));
        Assert.That(result[^1], Is.EqualTo("A"));
        Assert.That(IndexOf(result,"B"), Is.LessThan(IndexOf(result,"A")));
        Assert.That(IndexOf(result,"C"), Is.LessThan(IndexOf(result,"A")));
    }

    [Test]
    public void Resolve_CycleDetected_ThrowsWithDescription()
    {
        var input = new (string, IReadOnlyList<string>)[]
        {
            ("A", new[] { "B" }),
            ("B", new[] { "A" })
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DependencyResolver.Resolve(input));

        Assert.That(ex!.Message, Does.Contain("cycle"));
        Assert.That(ex.Message, Does.Contain("A"));
        Assert.That(ex.Message, Does.Contain("B"));
    }

    [Test]
    public void Resolve_SelfDependency_ThrowsCycleException()
    {
        var input = new (string, IReadOnlyList<string>)[]
        {
            ("A", new[] { "A" })
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DependencyResolver.Resolve(input));

        Assert.That(ex!.Message, Does.Contain("cycle"));
    }

    [Test]
    public void Resolve_MissingDependency_IgnoresAndLoadsAnyway()
    {
        // A depends on "unknown" which is not in the input list
        var input = new (string, IReadOnlyList<string>)[]
        {
            ("A", new[] { "unknown" }),
            ("B", Array.Empty<string>())
        };

        var result = DependencyResolver.Resolve(input);

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result, Does.Contain("A"));
        Assert.That(result, Does.Contain("B"));
    }

    [Test]
    public void Resolve_DependencyWithVersion_ExtractsNameCorrectly()
    {
        // Dependency string with version: "B >= 1.0.0"
        var input = new (string, IReadOnlyList<string>)[]
        {
            ("A", new[] { "B >= 1.0.0" }),
            ("B", Array.Empty<string>())
        };

        var result = DependencyResolver.Resolve(input);

        Assert.That(IndexOf(result,"B"), Is.LessThan(IndexOf(result,"A")));
    }

    [Test]
    public void Resolve_ThreeNodeCycle_ThrowsWithAllNodes()
    {
        var input = new (string, IReadOnlyList<string>)[]
        {
            ("A", new[] { "B" }),
            ("B", new[] { "C" }),
            ("C", new[] { "A" })
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DependencyResolver.Resolve(input));

        Assert.That(ex!.Message, Does.Contain("A"));
        Assert.That(ex.Message, Does.Contain("B"));
        Assert.That(ex.Message, Does.Contain("C"));
    }

    [Test]
    public void Resolve_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DependencyResolver.Resolve(null!));
    }
}
