namespace AdocNet.Extensions;

/// <summary>
/// Helper methods for working with assembly strong-name public key tokens.
/// </summary>
internal static class SigningHelper
{
    /// <summary>
    /// Converts a public key token byte array to a lowercase hex string.
    /// Returns an empty string if the token is null or empty (unsigned assembly).
    /// </summary>
    internal static string ToHexString(byte[]? token)
    {
        if (token is null || token.Length == 0)
            return "";

        var chars = new char[token.Length * 2];
        for (int i = 0; i < token.Length; i++)
        {
            chars[i * 2] = GetHexChar(token[i] >> 4);
            chars[i * 2 + 1] = GetHexChar(token[i] & 0xF);
        }
        return new string(chars);
    }

    /// <summary>
    /// Validates that a string is a valid 16-character hexadecimal public key token.
    /// </summary>
    internal static bool IsValidTokenFormat(string token)
    {
        if (token is null || token.Length != 16)
            return false;

        foreach (var c in token)
        {
            if (!IsHexChar(c))
                return false;
        }
        return true;
    }

    private static char GetHexChar(int nibble) =>
        (char)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);

    private static bool IsHexChar(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
