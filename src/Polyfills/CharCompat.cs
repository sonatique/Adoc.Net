// Polyfill for .NET Standard 2.0: char.IsAsciiDigit (added in .NET 7).
// Called as CharCompat.IsAsciiDigit(c) with #if guard at call site.

#if NETSTANDARD2_0

using System.Runtime.CompilerServices;

namespace AdocNet.Internal.Compatibility
{
    /// <summary>
    /// Backports <c>char.IsAsciiDigit</c> for .NET Standard 2.0.
    /// On net10.0, call sites use the built-in method directly via <c>#if</c> guard.
    /// </summary>
    internal static class CharCompat
    {
        /// <summary>Returns whether the character is an ASCII digit ('0'..'9').</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
    }
}

#endif
