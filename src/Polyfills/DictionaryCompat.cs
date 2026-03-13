// Polyfill for .NET Standard 2.0: Dictionary extension methods added in .NET Core 2.0+.
// Imported via global using; call sites use natural syntax: dict.TryAdd(k, v).

#if NETSTANDARD2_0

namespace System.Collections.Generic
{
    /// <summary>
    /// Extension methods that backport <c>Dictionary</c> APIs introduced after .NET Standard 2.0.
    /// Placed in <c>System.Collections.Generic</c> so the compiler resolves them without extra usings.
    /// </summary>
    internal static class DictionaryCompat
    {
        /// <summary>Returns the value for the given key, or <c>default</c> if not found.</summary>
        public static TValue GetValueOrDefault<TKey, TValue>(
            this Dictionary<TKey, TValue> dictionary, TKey key)
            where TKey : notnull
            => dictionary.TryGetValue(key, out var value) ? value : default!;

        /// <summary>Returns the value for the given key, or <paramref name="defaultValue"/> if not found.</summary>
        public static TValue GetValueOrDefault<TKey, TValue>(
            this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
            where TKey : notnull
            => dictionary.TryGetValue(key, out var value) ? value : defaultValue;

        /// <summary>Returns the value for the given key, or <paramref name="defaultValue"/> if not found (IReadOnlyDictionary overload).</summary>
        public static TValue GetValueOrDefault<TKey, TValue>(
            this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
            where TKey : notnull
            => dictionary.TryGetValue(key, out var value) ? value : defaultValue;

        /// <summary>Adds the key-value pair if the key doesn't already exist. Returns true if added.</summary>
        public static bool TryAdd<TKey, TValue>(
            this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
            where TKey : notnull
        {
            if (dictionary.ContainsKey(key)) return false;
            dictionary.Add(key, value);
            return true;
        }
    }
}

#endif
