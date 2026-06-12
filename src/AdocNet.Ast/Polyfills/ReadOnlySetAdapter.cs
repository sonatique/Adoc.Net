// Adapter for .NET Standard 2.0, where HashSet<T> does not implement the IReadOnlySet<T>
// polyfill. Wrap a set you already have so it can be assigned to APIs typed as
// IReadOnlySet<T> (e.g. ParseOptions.LockedAttributes). On .NET 5+ HashSet<T> implements
// IReadOnlySet<T> directly, so this adapter is only compiled — and only needed — for ns2.0.

#if NETSTANDARD2_0

using System.Collections;
using System.Collections.Generic;

namespace AdocNet.Ast;

/// <summary>
/// Wraps an <see cref="ISet{T}"/> (such as a <see cref="HashSet{T}"/>) so it can be used where an
/// <see cref="IReadOnlySet{T}"/> is expected on .NET Standard 2.0.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class ReadOnlySetAdapter<T> : IReadOnlySet<T>
{
    private readonly ISet<T> _set;

    /// <summary>Creates an adapter over <paramref name="set"/>. The set is not copied.</summary>
    public ReadOnlySetAdapter(ISet<T> set)
        => _set = set ?? throw new System.ArgumentNullException(nameof(set));

    /// <inheritdoc />
    public int Count => _set.Count;

    /// <inheritdoc />
    public bool Contains(T item) => _set.Contains(item);

    /// <inheritdoc />
    public bool IsProperSubsetOf(IEnumerable<T> other) => _set.IsProperSubsetOf(other);

    /// <inheritdoc />
    public bool IsProperSupersetOf(IEnumerable<T> other) => _set.IsProperSupersetOf(other);

    /// <inheritdoc />
    public bool IsSubsetOf(IEnumerable<T> other) => _set.IsSubsetOf(other);

    /// <inheritdoc />
    public bool IsSupersetOf(IEnumerable<T> other) => _set.IsSupersetOf(other);

    /// <inheritdoc />
    public bool Overlaps(IEnumerable<T> other) => _set.Overlaps(other);

    /// <inheritdoc />
    public bool SetEquals(IEnumerable<T> other) => _set.SetEquals(other);

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => _set.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _set.GetEnumerator();
}

#endif
