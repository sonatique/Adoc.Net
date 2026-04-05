using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace AdocNet.Caching;

/// <summary>
/// Computes deterministic cache keys for parse and render caching.
/// Uses SHA-256 for collision-resistant hashing.
/// </summary>
internal static class CacheKeyBuilder
{
    /// <summary>
    /// Computes a SHA-256 hex string from the input text.
    /// Used as the parse cache key.
    /// </summary>
    internal static string ComputeInputHash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return ComputeSha256Hex(bytes);
    }

    /// <summary>
    /// Computes a composite cache key from input hash, renderer format, and render options.
    /// Used as the render cache key.
    /// </summary>
    internal static string ComputeRenderKey(string inputHash, string format, RenderOptions options)
    {
        var optionsHash = ComputeOptionsHash(options);
        var composite = $"{inputHash}|{format}|{optionsHash}";
        var bytes = Encoding.UTF8.GetBytes(composite);
        return ComputeSha256Hex(bytes);
    }

    /// <summary>
    /// Computes a deterministic hash of a RenderOptions instance by reflecting over
    /// all public instance properties and hashing their string representations.
    /// </summary>
    private static string ComputeOptionsHash(RenderOptions options)
    {
        var sb = new StringBuilder();
        sb.Append(options.GetType().FullName);

        var properties = options.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        // Sort by name for deterministic ordering across platforms
        Array.Sort(properties, (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

        foreach (var prop in properties)
        {
            var value = prop.GetValue(options);
            sb.Append('|');
            sb.Append(prop.Name);
            sb.Append('=');
            if (value is IFormattable formattable)
                sb.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
            else
                sb.Append(value?.ToString() ?? "null");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return ComputeSha256Hex(bytes);
    }

    private static string ComputeSha256Hex(byte[] data)
    {
#if NET5_0_OR_GREATER
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash);
#else
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data);
        return BitConverter.ToString(hash).Replace("-", "");
#endif
    }
}
