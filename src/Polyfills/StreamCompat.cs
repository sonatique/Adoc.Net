// Polyfill for .NET Standard 2.0: Stream.Write(byte[]) single-argument overload.
// In NS2.0, Stream.Write requires (byte[], int, int). This extension bridges the gap.
// Imported via global using; call sites use natural syntax: stream.Write(bytes).

#if NETSTANDARD2_0

namespace AdocNet.Internal.Compatibility
{
    /// <summary>
    /// Extension method that backports the <c>Stream.Write(byte[])</c> overload
    /// (added in .NET Core 2.1) for .NET Standard 2.0.
    /// </summary>
    internal static class StreamCompat
    {
        /// <summary>Writes the entire byte array to the stream.</summary>
        public static void Write(this System.IO.Stream stream, byte[] buffer)
            => stream.Write(buffer, 0, buffer.Length);
    }
}

#endif
