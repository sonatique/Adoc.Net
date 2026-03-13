// Polyfill for .NET Standard 2.0: IReadOnlySet<T> was added in .NET 5.
// This minimal polyfill preserves the public API of ParseOptions.LockedAttributes.
// On NS2.0, HashSet<T> does not implement this interface. Callers must wrap
// their set in a ReadOnlySetAdapter<T> or use a type that implements this interface.

#if NETSTANDARD2_0

namespace System.Collections.Generic
{
    /// <summary>
    /// Provides a read-only view of a set. Polyfill for .NET Standard 2.0.
    /// </summary>
    public interface IReadOnlySet<T> : IReadOnlyCollection<T>
    {
        bool Contains(T item);
        bool IsProperSubsetOf(IEnumerable<T> other);
        bool IsProperSupersetOf(IEnumerable<T> other);
        bool IsSubsetOf(IEnumerable<T> other);
        bool IsSupersetOf(IEnumerable<T> other);
        bool Overlaps(IEnumerable<T> other);
        bool SetEquals(IEnumerable<T> other);
    }
}

#endif
