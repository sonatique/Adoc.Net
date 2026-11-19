namespace AdocNet.Converters.Pdf;

/// <summary>
/// Adobe Font Metrics for the standard PDF Helvetica and Helvetica-Bold fonts.
/// Character widths are in units where 1.0 = 1000 font design units.
/// Multiply by fontSize to get the width in points.
/// </summary>
internal static class HelveticaMetrics
{
    internal const float DefaultWidth = 0.556f;
    internal const float BoldDefaultWidth = 0.611f;
    internal const float CourierWidth = 0.6f;

    internal static readonly Dictionary<char, float> Regular = new()
    {
        [' '] = 0.278f, ['!'] = 0.278f, ['"'] = 0.355f, ['#'] = 0.556f, ['$'] = 0.556f,
        ['%'] = 0.889f, ['&'] = 0.667f, ['\''] = 0.191f, ['('] = 0.333f, [')'] = 0.333f,
        ['*'] = 0.389f, ['+'] = 0.584f, [','] = 0.278f, ['-'] = 0.333f, ['.'] = 0.278f,
        ['/'] = 0.278f, ['0'] = 0.556f, ['1'] = 0.556f, ['2'] = 0.556f, ['3'] = 0.556f,
        ['4'] = 0.556f, ['5'] = 0.556f, ['6'] = 0.556f, ['7'] = 0.556f, ['8'] = 0.556f,
        ['9'] = 0.556f, [':'] = 0.278f, [';'] = 0.278f, ['<'] = 0.584f, ['='] = 0.584f,
        ['>'] = 0.584f, ['?'] = 0.556f, ['@'] = 1.015f, ['A'] = 0.667f, ['B'] = 0.667f,
        ['C'] = 0.722f, ['D'] = 0.722f, ['E'] = 0.667f, ['F'] = 0.611f, ['G'] = 0.778f,
        ['H'] = 0.722f, ['I'] = 0.278f, ['J'] = 0.500f, ['K'] = 0.667f, ['L'] = 0.556f,
        ['M'] = 0.833f, ['N'] = 0.722f, ['O'] = 0.778f, ['P'] = 0.667f, ['Q'] = 0.778f,
        ['R'] = 0.722f, ['S'] = 0.667f, ['T'] = 0.611f, ['U'] = 0.722f, ['V'] = 0.667f,
        ['W'] = 0.944f, ['X'] = 0.667f, ['Y'] = 0.667f, ['Z'] = 0.611f, ['['] = 0.278f,
        ['\\'] = 0.278f, [']'] = 0.278f, ['^'] = 0.469f, ['_'] = 0.556f, ['`'] = 0.333f,
        ['a'] = 0.556f, ['b'] = 0.556f, ['c'] = 0.500f, ['d'] = 0.556f, ['e'] = 0.556f,
        ['f'] = 0.278f, ['g'] = 0.556f, ['h'] = 0.556f, ['i'] = 0.222f, ['j'] = 0.222f,
        ['k'] = 0.500f, ['l'] = 0.222f, ['m'] = 0.833f, ['n'] = 0.556f, ['o'] = 0.556f,
        ['p'] = 0.556f, ['q'] = 0.556f, ['r'] = 0.333f, ['s'] = 0.500f, ['t'] = 0.278f,
        ['u'] = 0.556f, ['v'] = 0.500f, ['w'] = 0.722f, ['x'] = 0.500f, ['y'] = 0.500f,
        ['z'] = 0.500f, ['{'] = 0.334f, ['|'] = 0.260f, ['}'] = 0.334f, ['~'] = 0.584f,
    };

    internal static readonly Dictionary<char, float> Bold = new()
    {
        [' '] = 0.278f, ['!'] = 0.333f, ['"'] = 0.474f, ['#'] = 0.556f, ['$'] = 0.556f,
        ['%'] = 0.889f, ['&'] = 0.722f, ['\''] = 0.238f, ['('] = 0.333f, [')'] = 0.333f,
        ['*'] = 0.389f, ['+'] = 0.584f, [','] = 0.278f, ['-'] = 0.333f, ['.'] = 0.278f,
        ['/'] = 0.278f, ['0'] = 0.556f, ['1'] = 0.556f, ['2'] = 0.556f, ['3'] = 0.556f,
        ['4'] = 0.556f, ['5'] = 0.556f, ['6'] = 0.556f, ['7'] = 0.556f, ['8'] = 0.556f,
        ['9'] = 0.556f, [':'] = 0.333f, [';'] = 0.333f, ['<'] = 0.584f, ['='] = 0.584f,
        ['>'] = 0.584f, ['?'] = 0.611f, ['@'] = 0.975f, ['A'] = 0.722f, ['B'] = 0.722f,
        ['C'] = 0.722f, ['D'] = 0.722f, ['E'] = 0.667f, ['F'] = 0.611f, ['G'] = 0.778f,
        ['H'] = 0.722f, ['I'] = 0.278f, ['J'] = 0.556f, ['K'] = 0.722f, ['L'] = 0.611f,
        ['M'] = 0.833f, ['N'] = 0.722f, ['O'] = 0.778f, ['P'] = 0.667f, ['Q'] = 0.778f,
        ['R'] = 0.722f, ['S'] = 0.667f, ['T'] = 0.611f, ['U'] = 0.722f, ['V'] = 0.667f,
        ['W'] = 0.944f, ['X'] = 0.667f, ['Y'] = 0.667f, ['Z'] = 0.611f, ['['] = 0.333f,
        ['\\'] = 0.278f, [']'] = 0.333f, ['^'] = 0.584f, ['_'] = 0.556f, ['`'] = 0.333f,
        ['a'] = 0.556f, ['b'] = 0.611f, ['c'] = 0.556f, ['d'] = 0.611f, ['e'] = 0.556f,
        ['f'] = 0.333f, ['g'] = 0.611f, ['h'] = 0.611f, ['i'] = 0.278f, ['j'] = 0.278f,
        ['k'] = 0.556f, ['l'] = 0.278f, ['m'] = 0.889f, ['n'] = 0.611f, ['o'] = 0.611f,
        ['p'] = 0.611f, ['q'] = 0.611f, ['r'] = 0.389f, ['s'] = 0.556f, ['t'] = 0.333f,
        ['u'] = 0.611f, ['v'] = 0.556f, ['w'] = 0.778f, ['x'] = 0.556f, ['y'] = 0.556f,
        ['z'] = 0.500f, ['{'] = 0.389f, ['|'] = 0.280f, ['}'] = 0.389f, ['~'] = 0.584f,
    };

    /// <summary>Widths of the ASCII characters, by code; characters past the table use the default width.</summary>
    private static readonly float[] RegularAscii = AsciiTable(Regular, DefaultWidth);
    private static readonly float[] BoldAscii = AsciiTable(Bold, BoldDefaultWidth);

    private static float[] AsciiTable(Dictionary<char, float> widths, float defaultWidth)
    {
        var table = new float[128];
        for (int c = 0; c < table.Length; c++)
            table[c] = widths.GetValueOrDefault((char)c, defaultWidth);
        return table;
    }

    /// <summary>Width of <paramref name="text"/> in a Helvetica face (F1 regular, F2 bold, F3 oblique).</summary>
    internal static float MeasureHelvetica(string text, string fontKey, float fontSize)
    {
        bool bold = fontKey == "F2";
        var table = bold ? BoldAscii : RegularAscii;
        float fallback = bold ? BoldDefaultWidth : DefaultWidth;
        float total = 0;
        foreach (var ch in text)
            total += (ch < 128 ? table[ch] : fallback) * fontSize;
        return total;
    }
}
