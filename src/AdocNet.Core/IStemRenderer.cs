using System.Globalization;
using System.Text;

namespace AdocNet;

/// <summary>
/// Typesets STEM content (<c>stem:[]</c>, <c>latexmath:[]</c>, <c>asciimath:[]</c> and the
/// <c>[stem]</c> blocks) into outlines every output can draw without a script or a network
/// connection. Set it on <see cref="RenderOptions.StemRenderer"/> (HTML, EPUB, PDF) or on the
/// Avalonia renderer; when none is set, each converter keeps its own handling of STEM content.
/// </summary>
/// <remarks>Implementations must be safe to call from several threads at once.</remarks>
public interface IStemRenderer
{
    /// <summary>Typesets one formula.</summary>
    /// <param name="content">The formula source, without delimiters.</param>
    /// <param name="stemType"><c>latexmath</c> or <c>asciimath</c>, as on the STEM nodes.</param>
    /// <param name="display">True for a block (display style), false for inline text style.</param>
    /// <returns>The outlines, or null when the content cannot be typeset; callers then show the source.</returns>
    StemGraphic? Render(string content, string stemType, bool display);
}

/// <summary>
/// A typeset formula as one filled outline. Lengths are in em of the surrounding text, so the same
/// graphic serves any font size: x grows to the right, y grows down, the box runs from (0, 0) to
/// (<see cref="Width"/>, <see cref="Height"/>) and the text baseline is at y = <see cref="Ascent"/>.
/// </summary>
public sealed class StemGraphic
{
    /// <summary>Creates a graphic.</summary>
    /// <param name="pathData">SVG path data of the whole formula, filled with the text colour.</param>
    /// <param name="width">Width in em.</param>
    /// <param name="ascent">Height above the baseline in em.</param>
    /// <param name="descent">Depth below the baseline in em.</param>
    /// <exception cref="ArgumentException">The path holds anything but M, L, C, Z, numbers and spaces,
    /// or a length is not a finite number. Every output writes the path as is.</exception>
    public StemGraphic(string pathData, double width, double ascent, double descent)
    {
        PathData = pathData ?? throw new ArgumentNullException(nameof(pathData));
        foreach (var c in pathData)
        {
            if (c is not ('M' or 'L' or 'C' or 'Z' or ' ' or '.' or '-' or (>= '0' and <= '9')))
                throw new ArgumentException($"Path data may hold only M, L, C, Z, numbers and spaces, not '{c}'.", nameof(pathData));
        }
        if (!IsFinite(width) || !IsFinite(ascent) || !IsFinite(descent) || width < 0 || ascent + descent < 0)
            throw new ArgumentException("The width and height must be finite and not negative.");
        Width = width;
        Ascent = ascent;
        Descent = descent;
    }

    private static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);

    /// <summary>
    /// SVG path data, filled with the text colour (non-zero rule). Absolute M, L, C and Z commands only,
    /// space-separated numbers: the PDF converter's path reader takes no other.
    /// </summary>
    public string PathData { get; }

    /// <summary>Width in em.</summary>
    public double Width { get; }

    /// <summary>Height above the baseline in em.</summary>
    public double Ascent { get; }

    /// <summary>Depth below the baseline in em.</summary>
    public double Descent { get; }

    /// <summary>Total height in em.</summary>
    public double Height => Ascent + Descent;

    /// <summary>
    /// An inline <c>&lt;svg&gt;</c> element (HTML and XHTML) sized in em, its baseline on the text
    /// baseline, filled with the current text colour. <paramref name="label"/> becomes its accessible
    /// name (typically the formula source).
    /// </summary>
    public string ToSvg(string label, bool display = false)
    {
        var sb = new StringBuilder();
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"stem\" overflow=\"visible\" role=\"img\" aria-label=\"");
        AppendEscaped(sb, label ?? "");
        sb.Append(string.Format(CultureInfo.InvariantCulture,
            "\" width=\"{0:0.####}em\" height=\"{1:0.####}em\" viewBox=\"0 0 {0:0.####} {1:0.####}\" style=\"",
            Width, Height));
        sb.Append(display
            ? "display:block;margin:0 auto"
            : string.Format(CultureInfo.InvariantCulture, "vertical-align:{0:0.####}em", -Descent));
        sb.Append("\"><path fill=\"currentColor\" d=\"").Append(PathData).Append("\"/></svg>");
        return sb.ToString();
    }

    private static void AppendEscaped(StringBuilder sb, string text)
    {
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\n': sb.Append(' '); break;
                default:
                    if (c >= ' ' || c == '\t') sb.Append(c); // XML 1.0 refuses other control characters
                    break;
            }
        }
    }
}
