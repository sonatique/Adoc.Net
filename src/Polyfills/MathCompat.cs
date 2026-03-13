// Polyfill for .NET Standard 2.0: Math.Clamp (added in .NET Core 2.0).
// Called as MathCompat.Clamp(val, min, max) with #if guard at call site.

#if NETSTANDARD2_0

namespace AdocNet.Internal.Compatibility
{
    /// <summary>
    /// Backports <c>Math.Clamp</c> for .NET Standard 2.0.
    /// On net10.0, call sites use the built-in method directly via <c>#if</c> guard.
    /// </summary>
    internal static class MathCompat
    {
        /// <summary>Clamps an integer value to the inclusive range [min, max].</summary>
        public static int Clamp(int value, int min, int max)
            => value < min ? min : value > max ? max : value;

        /// <summary>Clamps a double value to the inclusive range [min, max].</summary>
        public static double Clamp(double value, double min, double max)
            => value < min ? min : value > max ? max : value;
    }
}

#endif
