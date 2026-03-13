// Polyfill for .NET Standard 2.0: KeyValuePair deconstruction (added in .NET Core 2.0).
// Enables `foreach (var (key, value) in dictionary)` syntax.

#if NETSTANDARD2_0

namespace System.Collections.Generic
{
    /// <summary>
    /// Provides <c>Deconstruct</c> for <see cref="KeyValuePair{TKey, TValue}"/>
    /// to enable tuple-style deconstruction in foreach loops.
    /// </summary>
    internal static class KeyValuePairExtensions
    {
        /// <summary>Deconstructs a key-value pair into separate key and value variables.</summary>
        public static void Deconstruct<TKey, TValue>(
            this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
        {
            key = pair.Key;
            value = pair.Value;
        }
    }
}

#endif
