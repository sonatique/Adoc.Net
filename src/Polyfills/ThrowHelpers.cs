// Polyfill: provides Guard.NotNull / Guard.NotNullOrEmpty as cross-TFM replacements for
// ArgumentNullException.ThrowIfNull (.NET 6+) and ArgumentException.ThrowIfNullOrEmpty (.NET 7+).
//
// Unlike most polyfills, this file is active on BOTH targets:
// - NS2.0: custom implementation using CallerMemberName
// - net10.0: thin wrapper delegating to the built-in methods with CallerArgumentExpression

#if NETSTANDARD2_0

using System.Runtime.CompilerServices;

namespace AdocNet.Internal.Compatibility
{
    /// <summary>
    /// Argument validation helpers that work on both netstandard2.0 and net10.0.
    /// On ns2.0, throws directly; on net10.0, delegates to the built-in static methods.
    /// </summary>
    internal static class Guard
    {
        /// <summary>Throws <see cref="System.ArgumentNullException"/> if <paramref name="argument"/> is null.</summary>
        public static void NotNull(object? argument, [CallerMemberName] string? paramName = null)
        {
            if (argument is null)
                throw new System.ArgumentNullException(paramName);
        }

        /// <summary>Throws if <paramref name="argument"/> is null or empty.</summary>
        public static void NotNullOrEmpty(string? argument, [CallerMemberName] string? paramName = null)
        {
            if (string.IsNullOrEmpty(argument))
                throw argument is null
                    ? (System.Exception)new System.ArgumentNullException(paramName)
                    : new System.ArgumentException("Value cannot be an empty string.", paramName);
        }
    }
}

#else

namespace AdocNet.Internal.Compatibility
{
    /// <summary>
    /// Argument validation helpers. On net10.0, delegates to built-in
    /// ArgumentNullException.ThrowIfNull and ArgumentException.ThrowIfNullOrEmpty.
    /// </summary>
    internal static class Guard
    {
        /// <summary>Throws <see cref="System.ArgumentNullException"/> if <paramref name="argument"/> is null.</summary>
        public static void NotNull(object? argument, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(argument))] string? paramName = null)
            => System.ArgumentNullException.ThrowIfNull(argument, paramName);

        /// <summary>Throws if <paramref name="argument"/> is null or empty.</summary>
        public static void NotNullOrEmpty(string? argument, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(argument))] string? paramName = null)
            => System.ArgumentException.ThrowIfNullOrEmpty(argument, paramName);
    }
}

#endif
