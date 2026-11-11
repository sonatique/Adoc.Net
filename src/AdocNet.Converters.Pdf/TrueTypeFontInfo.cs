namespace AdocNet.Converters.Pdf;

/// <summary>
/// Reads metadata from TrueType/OpenType font files, such as the font paths a
/// <see cref="PdfRenderOptions"/> loaded by <see cref="PdfThemeLoader"/> carries.
/// </summary>
public static class TrueTypeFontInfo
{
    /// <summary>
    /// Returns the font's family name (OpenType name ID 1, English when available,
    /// e.g. "Calibri" or "Calibri Light"): the name CSS <c>font-family</c> and
    /// Avalonia <c>FontFamily("file:///…/calibri.ttf#Calibri")</c> expect.
    /// For a collection (.ttc) the first font is read. Returns null when the file
    /// is missing, unreadable, not a font, or has no family name.
    /// </summary>
    public static string? ReadFamilyName(string fontPath)
    {
        if (string.IsNullOrEmpty(fontPath)) return null;
        try
        {
            return TrueTypeFont.ReadFamilyName(File.ReadAllBytes(fontPath));
        }
        // Truncated or malformed files make the parser read past the buffer.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or IndexOutOfRangeException or ArgumentException)
        {
            return null;
        }
    }
}
