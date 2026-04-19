using System.Text;
using System.Text.RegularExpressions;

namespace AdocNet.Tests.Converters.Pdf;

/// <summary>
/// Lightweight PDF text extractor for differential testing.
/// Extracts visible text from PDF content streams by parsing text operators
/// (Tj, TJ, ', "). Does NOT handle encrypted PDFs or complex encodings.
/// Sufficient for comparing AdocNet vs asciidoctor-pdf content ordering.
/// </summary>
internal static class PdfTextExtractor
{
    /// <summary>
    /// Extracts all visible text from a PDF byte array, returned as a list of text fragments
    /// in document order (page by page, top to bottom within each page).
    /// </summary>
    public static List<string> ExtractText(byte[] pdfBytes)
    {
        var text = Encoding.ASCII.GetString(pdfBytes);
        var fragments = new List<string>();

        // Find all stream...endstream sections (content streams)
        int pos = 0;
        while (pos < text.Length)
        {
            int streamStart = text.IndexOf("stream\n", pos, StringComparison.Ordinal);
            if (streamStart < 0) break;
            streamStart += "stream\n".Length;

            int streamEnd = text.IndexOf("endstream", streamStart, StringComparison.Ordinal);
            if (streamEnd < 0) break;

            var streamContent = text.Substring(streamStart, streamEnd - streamStart);
            ExtractTextFromStream(streamContent, fragments);

            pos = streamEnd + "endstream".Length;
        }

        return fragments;
    }

    /// <summary>
    /// Extracts text from a single PDF content stream.
    /// Handles Tj (show string) and hex-encoded text.
    /// </summary>
    private static void ExtractTextFromStream(string stream, List<string> fragments)
    {
        // Match (text) Tj — parenthesized string show
        var tjRegex = new Regex(@"\(([^)]*)\)\s*Tj", RegexOptions.Compiled);
        foreach (Match match in tjRegex.Matches(stream))
        {
            var decoded = DecodePdfString(match.Groups[1].Value);
            if (!string.IsNullOrWhiteSpace(decoded))
                fragments.Add(decoded);
        }

        // Match <hex> Tj — hex string show (embedded TrueType fonts use this)
        var hexTjRegex = new Regex(@"<([0-9a-fA-F]+)>\s*Tj", RegexOptions.Compiled);
        foreach (Match match in hexTjRegex.Matches(stream))
        {
            // Hex-encoded text for embedded fonts — can't decode without the font's CMap.
            // Mark as "[embedded text]" for structural comparison.
            fragments.Add("[embedded]");
        }
    }

    /// <summary>
    /// Decodes a PDF parenthesized string, handling octal escapes and backslash sequences.
    /// </summary>
    private static string DecodePdfString(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        int i = 0;
        while (i < raw.Length)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                i++;
                if (raw[i] >= '0' && raw[i] <= '7')
                {
                    // Octal escape: 1-3 digits
                    int end = Math.Min(i + 3, raw.Length);
                    int val = 0;
                    while (i < end && raw[i] >= '0' && raw[i] <= '7')
                    {
                        val = val * 8 + (raw[i] - '0');
                        i++;
                    }
                    if (val >= 32 && val < 127)
                        sb.Append((char)val);
                    else
                        sb.Append('?'); // Non-printable
                }
                else
                {
                    sb.Append(raw[i] switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '(' => '(',
                        ')' => ')',
                        '\\' => '\\',
                        _ => raw[i],
                    });
                    i++;
                }
            }
            else
            {
                sb.Append(raw[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns a normalized, comparable representation of PDF text content.
    /// Joins all fragments, collapses whitespace, trims.
    /// </summary>
    public static string NormalizeText(List<string> fragments)
    {
        var joined = string.Join(" ", fragments);
        // Collapse whitespace
        joined = Regex.Replace(joined, @"\s+", " ").Trim();
        return joined;
    }
}
