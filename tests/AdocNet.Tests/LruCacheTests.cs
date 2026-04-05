using System.Reflection;

namespace AdocNet.Tests;

[TestFixture]
public class LruCacheTests
{
    // LruCache is internal, so we access it via reflection
    private static dynamic CreateCache(int capacity)
    {
        var asm = typeof(AdocEngine).Assembly;
        var cacheType = asm.GetType("AdocNet.Caching.LruCache`2")!
            .MakeGenericType(typeof(string), typeof(string));
        return Activator.CreateInstance(cacheType, capacity)!;
    }

    [Test]
    public void Set_And_TryGet_ReturnsValue()
    {
        var cache = CreateCache(4);
        cache.Set("key1", "value1");

        string result;
        bool found = cache.TryGet("key1", out result);

        Assert.That(found, Is.True);
        Assert.That((string)result, Is.EqualTo("value1"));
    }

    [Test]
    public void TryGet_MissingKey_ReturnsFalse()
    {
        var cache = CreateCache(4);
        string result;
        bool found = cache.TryGet("missing", out result);

        Assert.That(found, Is.False);
    }

    [Test]
    public void Eviction_RemovesLeastRecentlyUsed()
    {
        var cache = CreateCache(2);
        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.Set("c", "3"); // evicts "a"

        string result;
        Assert.That((bool)cache.TryGet("a", out result), Is.False, "a should be evicted");
        Assert.That((bool)cache.TryGet("b", out result), Is.True, "b should still exist");
        Assert.That((bool)cache.TryGet("c", out result), Is.True, "c should still exist");
    }

    [Test]
    public void TryGet_PromotesEntry_PreventingEviction()
    {
        var cache = CreateCache(2);
        cache.Set("a", "1");
        cache.Set("b", "2");

        // Access "a" to promote it
        string result;
        cache.TryGet("a", out result);

        cache.Set("c", "3"); // should evict "b" (LRU), not "a"

        Assert.That((bool)cache.TryGet("a", out result), Is.True, "a was accessed recently");
        Assert.That((bool)cache.TryGet("b", out result), Is.False, "b should be evicted");
    }

    [Test]
    public void Clear_RemovesAllEntries()
    {
        var cache = CreateCache(4);
        cache.Set("a", "1");
        cache.Set("b", "2");
        cache.Clear();

        Assert.That((int)cache.Count, Is.EqualTo(0));

        string result;
        Assert.That((bool)cache.TryGet("a", out result), Is.False);
    }

    [Test]
    public void Set_UpdatesExistingKey()
    {
        var cache = CreateCache(4);
        cache.Set("key", "old");
        cache.Set("key", "new");

        string result;
        cache.TryGet("key", out result);
        Assert.That((string)result, Is.EqualTo("new"));
        Assert.That((int)cache.Count, Is.EqualTo(1));
    }

    [Test]
    public void Capacity_ThrowsOnZero()
    {
        Assert.Throws<TargetInvocationException>(() => CreateCache(0));
    }

    [Test]
    public void Count_ReflectsEntries()
    {
        var cache = CreateCache(10);
        Assert.That((int)cache.Count, Is.EqualTo(0));

        cache.Set("a", "1");
        Assert.That((int)cache.Count, Is.EqualTo(1));

        cache.Set("b", "2");
        Assert.That((int)cache.Count, Is.EqualTo(2));
    }
}
