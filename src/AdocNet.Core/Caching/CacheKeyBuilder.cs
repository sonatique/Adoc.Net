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
            AppendValueHash(sb, value);
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return ComputeSha256Hex(bytes);
    }

    /// <summary>
    /// Appends a content-derived representation of <paramref name="value"/>. For collections,
    /// <c>ToString()</c> is just the type name — so two different template/colour lists would hash
    /// identically and serve stale cached output. Hash the element count and each element (by value
    /// where formattable, otherwise by identity) so different collections produce different keys.
    /// </summary>
    private static void AppendValueHash(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case string s:
                sb.Append(s);
                break;
            case IFormattable formattable:
                sb.Append(formattable.ToString(null, CultureInfo.InvariantCulture));
                break;
            case System.Collections.IEnumerable enumerable:
                sb.Append('[');
                int count = 0;
                foreach (var item in enumerable)
                {
                    if (count++ > 0) sb.Append(',');
                    if (item is null)
                        sb.Append("null");
                    else if (item is string si)
                        sb.Append(si);
                    else if (item is IFormattable f)
                        sb.Append(f.ToString(null, CultureInfo.InvariantCulture));
                    else
                        // GetHashCode distinguishes by content when overridden, by identity
                        // otherwise — either way different elements yield different keys.
                        sb.Append(item.GetHashCode().ToString(CultureInfo.InvariantCulture));
                }
                sb.Append('#').Append(count).Append(']');
                break;
            default:
                sb.Append(value.ToString() ?? "null");
                break;
        }
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
