// Polyfill for .NET Standard 2.0: string methods added in .NET Core 2.1+.
// Each method provides the ns2.0 fallback; on net10.0, the built-in methods are used.
// These are extension methods, so call sites use natural syntax: s.Contains(c).

#if NETSTANDARD2_0

using System.Runtime.CompilerServices;

namespace AdocNet.Internal.Compatibility
{
    /// <summary>
    /// Extension methods that backport string APIs introduced after .NET Standard 2.0.
    /// On net10.0, the compiler uses the built-in methods instead (this class is excluded).
    /// </summary>
    internal static class StringCompat
    {
        /// <summary>Returns whether the string contains the specified character.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(this string s, char c)
            => s.IndexOf(c) >= 0;

        /// <summary>Returns whether the string contains the specified value using the given comparison.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(this string s, string value, System.StringComparison comparison)
            => s.IndexOf(value, comparison) >= 0;

        /// <summary>Returns whether the string starts with the specified character.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool StartsWith(this string s, char c)
            => s.Length > 0 && s[0] == c;

        /// <summary>Returns whether the string ends with the specified character.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EndsWith(this string s, char c)
            => s.Length > 0 && s[s.Length - 1] == c;

        /// <summary>Splits the string by a single character with the specified options.</summary>
        public static string[] Split(this string s, char separator, System.StringSplitOptions options)
            => s.Split(new[] { separator }, options);

        /// <summary>Splits the string by a single character with a maximum count and options.</summary>
        public static string[] Split(this string s, char separator, int count, System.StringSplitOptions options = System.StringSplitOptions.None)
            => s.Split(new[] { separator }, count, options);

        /// <summary>Concatenates strings using a character separator. Delegates to string.Join with a string separator.</summary>
        public static string Join(char separator, System.Collections.Generic.IEnumerable<string> values)
            => string.Join(separator.ToString(), values);

        /// <summary>Concatenates strings using a character separator. Delegates to string.Join with a string separator.</summary>
        public static string Join(char separator, string[] values)
            => string.Join(separator.ToString(), values);

        /// <summary>
        /// Returns the string itself as a stand-in for <c>string.AsSpan()</c>.
        /// On net10.0, the built-in <c>AsSpan()</c> returns <c>ReadOnlySpan&lt;char&gt;</c>;
        /// on ns2.0, this returns <c>string</c> to avoid depending on <c>System.Memory</c>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string AsSpan(this string s) => s;

        /// <summary>Returns a substring starting at the specified index (stand-in for <c>string.AsSpan(int)</c>).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string AsSpan(this string s, int start) => s.Substring(start);

        /// <summary>Returns a substring of the specified length starting at the specified index (stand-in for <c>string.AsSpan(int, int)</c>).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string AsSpan(this string s, int start, int length) => s.Substring(start, length);
    }
}

#endif
