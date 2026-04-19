using System.Globalization;
using System.Text;

namespace AdocNet.Converters.Pdf;

/// <summary>
/// Minimal SVG parser that extracts vector shapes (paths, polygons, rects) from SVG files
/// and converts them to PDF path operations. Supports the subset of SVG commonly used
/// in logos and simple graphics: path d-attribute, polygon, rect, circle, style fills.
/// </summary>
internal static class SvgParser
{
    /// <summary>
    /// Parsed SVG document with viewBox dimensions and a list of shapes.
    /// </summary>
    internal readonly record struct SvgDocument(
        float ViewBoxWidth, float ViewBoxHeight,
        float Width, float Height,
        IReadOnlyList<SvgShape> Shapes);

    /// <summary>
    /// A single SVG shape with its fill color and path data.
    /// </summary>
    internal readonly record struct SvgShape(PdfColor? Fill, string PathData);

    /// <summary>
    /// Parses an SVG file's bytes into an <see cref="SvgDocument"/>.
    /// </summary>
    internal static SvgDocument? Parse(byte[] data)
    {
        string svg = DetectAndDecode(data);
        if (string.IsNullOrEmpty(svg)) return null;

        // Extract viewBox
        float vbW = 0, vbH = 0;
        var viewBox = ExtractAttr(svg, "viewBox");
        if (viewBox is not null)
        {
            var parts = viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4)
            {
                ParseF(parts[2], out vbW);
                ParseF(parts[3], out vbH);
            }
        }

        // Extract width/height from root <svg>
        float svgW = vbW, svgH = vbH;
        var widthAttr = ExtractAttr(svg, "width");
        var heightAttr = ExtractAttr(svg, "height");
        if (widthAttr is not null) svgW = ParseDimension(widthAttr);
        if (heightAttr is not null) svgH = ParseDimension(heightAttr);
        if (vbW == 0) vbW = svgW;
        if (vbH == 0) vbH = svgH;
        if (vbW == 0 || vbH == 0) return null;

        // Parse CSS classes for fill colors
        var classColors = ParseStyleBlock(svg);

        // Parse shapes in document order (later shapes render on top of earlier ones)
        var shapes = ParseShapesInDocumentOrder(svg, classColors);

        return new SvgDocument(vbW, vbH, svgW, svgH, shapes);
    }

    /// <summary>
    /// Converts an SVG shape's path data to PDF path operators.
    /// The output must be placed in a PDF content stream between q/Q (save/restore).
    /// </summary>
    internal static string ToPdfPathOps(string pathData, float scaleX, float scaleY,
        float offsetX, float offsetY, float viewBoxHeight)
    {
        var sb = new StringBuilder();
        float curX = 0, curY = 0;
        float startX = 0, startY = 0;

        int i = 0;
        while (i < pathData.Length)
        {
            SkipWhitespaceAndCommas(pathData, ref i);
            if (i >= pathData.Length) break;

            char cmd = pathData[i];
            if (char.IsLetter(cmd))
            {
                i++;
            }
            else
            {
                // Implicit repeat of previous command (treat as lineto for M->L)
                cmd = 'L'; // fallback
            }

            switch (cmd)
            {
                case 'M': // Absolute moveto
                {
                    bool first = true;
                    while (TryReadNumber(pathData, ref i, out float x) &&
                           TryReadNumber(pathData, ref i, out float y))
                    {
                        curX = x; curY = y;
                        float px = curX * scaleX + offsetX;
                        float py = (viewBoxHeight - curY) * scaleY + offsetY;
                        sb.Append(first ? $"{Fmt(px)} {Fmt(py)} m\n"
                                       : $"{Fmt(px)} {Fmt(py)} l\n");
                        if (first) { startX = curX; startY = curY; first = false; }
                    }
                    break;
                }
                case 'm': // Relative moveto
                {
                    bool first = true;
                    while (TryReadNumber(pathData, ref i, out float dx) &&
                           TryReadNumber(pathData, ref i, out float dy))
                    {
                        curX += dx; curY += dy;
                        float px = curX * scaleX + offsetX;
                        float py = (viewBoxHeight - curY) * scaleY + offsetY;
                        sb.Append(first ? $"{Fmt(px)} {Fmt(py)} m\n"
                                       : $"{Fmt(px)} {Fmt(py)} l\n");
                        if (first) { startX = curX; startY = curY; first = false; }
                    }
                    break;
                }
                case 'L': // Absolute lineto
                    while (TryReadNumber(pathData, ref i, out float lx) &&
                           TryReadNumber(pathData, ref i, out float ly))
                    {
                        curX = lx; curY = ly;
                        float px = curX * scaleX + offsetX;
                        float py = (viewBoxHeight - curY) * scaleY + offsetY;
                        sb.Append($"{Fmt(px)} {Fmt(py)} l\n");
                    }
                    break;
                case 'l': // Relative lineto
                    while (TryReadNumber(pathData, ref i, out float ldx) &&
                           TryReadNumber(pathData, ref i, out float ldy))
                    {
                        curX += ldx; curY += ldy;
                        float px = curX * scaleX + offsetX;
                        float py = (viewBoxHeight - curY) * scaleY + offsetY;
                        sb.Append($"{Fmt(px)} {Fmt(py)} l\n");
                    }
                    break;
                case 'H': // Absolute horizontal lineto
                    while (TryReadNumber(pathData, ref i, out float hx))
                    {
                        curX = hx;
                        float px = curX * scaleX + offsetX;
                        float py = (viewBoxHeight - curY) * scaleY + offsetY;
                        sb.Append($"{Fmt(px)} {Fmt(py)} l\n");
                    }
                    break;
                case 'h': // Relative horizontal lineto
                    while (TryReadNumber(pathData, ref i, out float hdx))
                    {
                        curX += hdx;
                        float px = curX * scaleX + offsetX;
                        float py = (viewBoxHeight - curY) * scaleY + offsetY;
                        sb.Append($"{Fmt(px)} {Fmt(py)} l\n");
                    }
                    break;
                case 'V': // Absolute vertical lineto
                    while (TryReadNumber(pathData, ref i, out float vy))
                    {
                        curY = vy;
                        float px = curX * scaleX + offsetX;
                        float py = (viewBoxHeight - curY) * scaleY + offsetY;
                        sb.Append($"{Fmt(px)} {Fmt(py)} l\n");
                    }
                    break;
                case 'v': // Relative vertical lineto
                    while (TryReadNumber(pathData, ref i, out float vdy))
                    {
                        curY += vdy;
                        float px = curX * scaleX + offsetX;
                        float py = (viewBoxHeight - curY) * scaleY + offsetY;
                        sb.Append($"{Fmt(px)} {Fmt(py)} l\n");
                    }
                    break;
                case 'C': // Absolute cubic Bezier
                    while (TryReadNumber(pathData, ref i, out float cx1) &&
                           TryReadNumber(pathData, ref i, out float cy1) &&
                           TryReadNumber(pathData, ref i, out float cx2) &&
                           TryReadNumber(pathData, ref i, out float cy2) &&
                           TryReadNumber(pathData, ref i, out float cx) &&
                           TryReadNumber(pathData, ref i, out float cy))
                    {
                        float p1x = cx1 * scaleX + offsetX, p1y = (viewBoxHeight - cy1) * scaleY + offsetY;
                        float p2x = cx2 * scaleX + offsetX, p2y = (viewBoxHeight - cy2) * scaleY + offsetY;
                        curX = cx; curY = cy;
                        float px = curX * scaleX + offsetX, py = (viewBoxHeight - curY) * scaleY + offsetY;
                        sb.Append($"{Fmt(p1x)} {Fmt(p1y)} {Fmt(p2x)} {Fmt(p2y)} {Fmt(px)} {Fmt(py)} c\n");
                    }
                    break;
                case 'c': // Relative cubic Bezier
                    while (TryReadNumber(pathData, ref i, out float rcx1) &&
                           TryReadNumber(pathData, ref i, out float rcy1) &&
                           TryReadNumber(pathData, ref i, out float rcx2) &&
                           TryReadNumber(pathData, ref i, out float rcy2) &&
                           TryReadNumber(pathData, ref i, out float rcx) &&
                           TryReadNumber(pathData, ref i, out float rcy))
                    {
                        float ax1 = curX + rcx1, ay1 = curY + rcy1;
                        float ax2 = curX + rcx2, ay2 = curY + rcy2;
                        curX += rcx; curY += rcy;
                        float p1x = ax1 * scaleX + offsetX, p1y = (viewBoxHeight - ay1) * scaleY + offsetY;
                        float p2x = ax2 * scaleX + offsetX, p2y = (viewBoxHeight - ay2) * scaleY + offsetY;
                        float px = curX * scaleX + offsetX, py = (viewBoxHeight - curY) * scaleY + offsetY;
                        sb.Append($"{Fmt(p1x)} {Fmt(p1y)} {Fmt(p2x)} {Fmt(p2y)} {Fmt(px)} {Fmt(py)} c\n");
                    }
                    break;
                case 'Z':
                case 'z': // Close path
                    curX = startX; curY = startY;
                    sb.Append("h\n");
                    break;
                default:
                    // Skip unsupported commands (S, Q, T, A, etc.)
                    SkipUntilNextCommand(pathData, ref i);
                    break;
            }
        }

        return sb.ToString();
    }

    // ── SVG parsing helpers ─────────────────────────────────────────────

    private static string DetectAndDecode(byte[] data)
    {
        // Check for UTF-16 BOM (LE or BE)
        if (data.Length >= 2)
        {
            if (data[0] == 0xFF && data[1] == 0xFE)
                return Encoding.Unicode.GetString(data);
            if (data[0] == 0xFE && data[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(data);
        }
        // Check for UTF-8 BOM
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8.GetString(data, 3, data.Length - 3);

        // Sniff: if every other byte is 0x00, likely UTF-16
        if (data.Length >= 4 && data[1] == 0 && data[3] == 0)
            return Encoding.Unicode.GetString(data);
        if (data.Length >= 4 && data[0] == 0 && data[2] == 0)
            return Encoding.BigEndianUnicode.GetString(data);

        return Encoding.UTF8.GetString(data);
    }

    private static string? ExtractAttr(string xml, string attrName)
    {
        // Find attrName="value" or attrName='value' — simple non-greedy
        int searchIdx = 0;
        while (searchIdx < xml.Length)
        {
            int attrIdx = xml.IndexOf(attrName, searchIdx, StringComparison.Ordinal);
            if (attrIdx < 0) return null;

            int eqIdx = attrIdx + attrName.Length;
            while (eqIdx < xml.Length && xml[eqIdx] == ' ') eqIdx++;
            if (eqIdx >= xml.Length || xml[eqIdx] != '=')
            {
                searchIdx = eqIdx;
                continue;
            }
            eqIdx++;
            while (eqIdx < xml.Length && xml[eqIdx] == ' ') eqIdx++;
            if (eqIdx >= xml.Length) return null;

            char quote = xml[eqIdx];
            if (quote != '"' && quote != '\'')
            {
                searchIdx = eqIdx;
                continue;
            }
            int endIdx = xml.IndexOf(quote, eqIdx + 1);
            if (endIdx < 0) return null;
            return xml[(eqIdx + 1)..endIdx];
        }
        return null;
    }

    private static float ParseDimension(string s)
    {
        // Strip px, pt, etc.
        var trimmed = s.TrimEnd();
        int end = trimmed.Length;
        while (end > 0 && !char.IsDigit(trimmed[end - 1]) && trimmed[end - 1] != '.')
            end--;
        if (end <= 0) return 0;
        ParseF(trimmed[..end], out float v);
        return v;
    }

    private static Dictionary<string, PdfColor> ParseStyleBlock(string svg)
    {
        var result = new Dictionary<string, PdfColor>(StringComparer.OrdinalIgnoreCase);
        int styleStart = svg.IndexOf("<style", StringComparison.OrdinalIgnoreCase);
        if (styleStart < 0) return result;
        int contentStart = svg.IndexOf('>', styleStart);
        if (contentStart < 0) return result;
        contentStart++;

        // Skip CDATA wrapper
        int cdataStart = svg.IndexOf("<![CDATA[", contentStart, StringComparison.Ordinal);
        if (cdataStart >= 0) contentStart = cdataStart + 9;

        int styleEnd = svg.IndexOf("</style>", contentStart, StringComparison.OrdinalIgnoreCase);
        if (styleEnd < 0) return result;

        int cdataEnd = svg.IndexOf("]]>", contentStart, StringComparison.Ordinal);
        if (cdataEnd >= 0 && cdataEnd < styleEnd) styleEnd = cdataEnd;

        var css = svg[contentStart..styleEnd];
        // Parse rules like: .fil0 {fill:#373435;fill-rule:nonzero}
        int pos = 0;
        while (pos < css.Length)
        {
            int dot = css.IndexOf('.', pos);
            if (dot < 0) break;
            int brace = css.IndexOf('{', dot);
            if (brace < 0) break;
            string className = css[(dot + 1)..brace].Trim();
            int endBrace = css.IndexOf('}', brace);
            if (endBrace < 0) break;
            string body = css[(brace + 1)..endBrace];

            // Extract fill color
            int fillIdx = body.IndexOf("fill:", StringComparison.OrdinalIgnoreCase);
            if (fillIdx >= 0)
            {
                int valStart = fillIdx + 5;
                while (valStart < body.Length && body[valStart] == ' ') valStart++;
                int valEnd = valStart;
                while (valEnd < body.Length && body[valEnd] != ';' && body[valEnd] != '}') valEnd++;
                string colorStr = body[valStart..valEnd].Trim();
                var color = PdfThemeLoader.ParseColor(colorStr);
                if (color is not null)
                    result[className] = color.Value;
            }
            pos = endBrace + 1;
        }
        return result;
    }

    private static PdfColor? ResolveFill(string elementXml, Dictionary<string, PdfColor> classColors)
    {
        // Check class="filN" attribute
        var classAttr = ExtractAttrInElement(elementXml, "class");
        if (classAttr is not null && classColors.TryGetValue(classAttr.Trim(), out var classColor))
            return classColor;

        // Check inline fill="..."
        var fillAttr = ExtractAttrInElement(elementXml, "fill");
        if (fillAttr is not null)
            return PdfThemeLoader.ParseColor(fillAttr);

        // Check style="fill:..."
        var styleAttr = ExtractAttrInElement(elementXml, "style");
        if (styleAttr is not null)
        {
            int fillIdx = styleAttr.IndexOf("fill:", StringComparison.OrdinalIgnoreCase);
            if (fillIdx >= 0)
            {
                int start = fillIdx + 5;
                int end = start;
                while (end < styleAttr.Length && styleAttr[end] != ';') end++;
                return PdfThemeLoader.ParseColor(styleAttr[start..end].Trim());
            }
        }

        return null;
    }

    private static string? ExtractAttrInElement(string element, string attrName)
    {
        // Within a single element tag string, find attr="value"
        string search = attrName + "=";
        int idx = element.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        int valStart = idx + search.Length;
        if (valStart >= element.Length) return null;
        char quote = element[valStart];
        if (quote != '"' && quote != '\'') return null;
        int valEnd = element.IndexOf(quote, valStart + 1);
        if (valEnd < 0) return null;
        return element[(valStart + 1)..valEnd];
    }

    private static List<SvgShape> ParseShapesInDocumentOrder(string svg, Dictionary<string, PdfColor> classColors)
    {
        var shapes = new List<SvgShape>();
        int pos = 0;
        while (pos < svg.Length)
        {
            // Find the next shape tag of any supported type
            int nextPath = svg.IndexOf("<path", pos, StringComparison.OrdinalIgnoreCase);
            int nextPolygon = svg.IndexOf("<polygon", pos, StringComparison.OrdinalIgnoreCase);
            int nextRect = svg.IndexOf("<rect", pos, StringComparison.OrdinalIgnoreCase);
            int nextCircle = svg.IndexOf("<circle", pos, StringComparison.OrdinalIgnoreCase);

            int nextTag = int.MaxValue;
            string? tagType = null;
            if (nextPath >= 0 && nextPath < nextTag) { nextTag = nextPath; tagType = "path"; }
            if (nextPolygon >= 0 && nextPolygon < nextTag) { nextTag = nextPolygon; tagType = "polygon"; }
            if (nextRect >= 0 && nextRect < nextTag) { nextTag = nextRect; tagType = "rect"; }
            if (nextCircle >= 0 && nextCircle < nextTag) { nextTag = nextCircle; tagType = "circle"; }

            if (tagType is null) break;

            int tagEnd = svg.IndexOf("/>", nextTag);
            if (tagEnd < 0) tagEnd = svg.IndexOf(">", nextTag);
            if (tagEnd < 0) break;
            tagEnd += (svg[tagEnd] == '/' ? 2 : 1);

            string element = svg[nextTag..tagEnd];
            ParseElementToShape(element, tagType, classColors, shapes);
            pos = tagEnd;
        }
        return shapes;
    }

    private static void ParseElementToShape(string element, string tagType, Dictionary<string, PdfColor> classColors, List<SvgShape> shapes)
    {
        switch (tagType)
        {
            case "path":
            {
                var d = ExtractAttrInElement(element, "d");
                if (d is not null)
                {
                    var fill = ResolveFill(element, classColors);
                    shapes.Add(new SvgShape(fill, d));
                }
                break;
            }
            case "polygon":
            {
                var points = ExtractAttrInElement(element, "points");
                if (points is not null)
                {
                    var pairs = points.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    var sb = new StringBuilder();
                    for (int i = 0; i < pairs.Length; i++)
                    {
                        sb.Append(i == 0 ? 'M' : 'L');
                        sb.Append(pairs[i]);
                        sb.Append(' ');
                    }
                    sb.Append('Z');
                    var fill = ResolveFill(element, classColors);
                    shapes.Add(new SvgShape(fill, sb.ToString()));
                }
                break;
            }
            case "rect":
            {
                ParseF(ExtractAttrInElement(element, "x") ?? "0", out float x);
                ParseF(ExtractAttrInElement(element, "y") ?? "0", out float y);
                ParseF(ExtractAttrInElement(element, "width") ?? "0", out float w);
                ParseF(ExtractAttrInElement(element, "height") ?? "0", out float h);
                if (w > 0 && h > 0)
                {
                    string pathData = $"M{Fmt(x)} {Fmt(y)} L{Fmt(x + w)} {Fmt(y)} L{Fmt(x + w)} {Fmt(y + h)} L{Fmt(x)} {Fmt(y + h)} Z";
                    var fill = ResolveFill(element, classColors);
                    shapes.Add(new SvgShape(fill, pathData));
                }
                break;
            }
            case "circle":
            {
                ParseF(ExtractAttrInElement(element, "cx") ?? "0", out float cx);
                ParseF(ExtractAttrInElement(element, "cy") ?? "0", out float cy);
                ParseF(ExtractAttrInElement(element, "r") ?? "0", out float r);
                if (r > 0)
                {
                    float k = r * 0.5522847f;
                    var sb = new StringBuilder();
                    sb.Append($"M{Fmt(cx + r)} {Fmt(cy)} ");
                    sb.Append($"C{Fmt(cx + r)} {Fmt(cy + k)} {Fmt(cx + k)} {Fmt(cy + r)} {Fmt(cx)} {Fmt(cy + r)} ");
                    sb.Append($"C{Fmt(cx - k)} {Fmt(cy + r)} {Fmt(cx - r)} {Fmt(cy + k)} {Fmt(cx - r)} {Fmt(cy)} ");
                    sb.Append($"C{Fmt(cx - r)} {Fmt(cy - k)} {Fmt(cx - k)} {Fmt(cy - r)} {Fmt(cx)} {Fmt(cy - r)} ");
                    sb.Append($"C{Fmt(cx + k)} {Fmt(cy - r)} {Fmt(cx + r)} {Fmt(cy - k)} {Fmt(cx + r)} {Fmt(cy)} Z");
                    var fill = ResolveFill(element, classColors);
                    shapes.Add(new SvgShape(fill, sb.ToString()));
                }
                break;
            }
        }
    }

    private static void ParsePaths(string svg, Dictionary<string, PdfColor> classColors, List<SvgShape> shapes)
    {
        int pos = 0;
        while (pos < svg.Length)
        {
            int tagStart = svg.IndexOf("<path", pos, StringComparison.OrdinalIgnoreCase);
            if (tagStart < 0) break;
            int tagEnd = svg.IndexOf("/>", tagStart);
            if (tagEnd < 0) tagEnd = svg.IndexOf(">", tagStart);
            if (tagEnd < 0) break;
            tagEnd += (svg[tagEnd] == '/' ? 2 : 1);

            string element = svg[tagStart..tagEnd];
            var d = ExtractAttrInElement(element, "d");
            if (d is not null)
            {
                var fill = ResolveFill(element, classColors);
                shapes.Add(new SvgShape(fill, d));
            }
            pos = tagEnd;
        }
    }

    private static void ParsePolygons(string svg, Dictionary<string, PdfColor> classColors, List<SvgShape> shapes)
    {
        int pos = 0;
        while (pos < svg.Length)
        {
            int tagStart = svg.IndexOf("<polygon", pos, StringComparison.OrdinalIgnoreCase);
            if (tagStart < 0) break;
            int tagEnd = svg.IndexOf("/>", tagStart);
            if (tagEnd < 0) tagEnd = svg.IndexOf(">", tagStart);
            if (tagEnd < 0) break;
            tagEnd += (svg[tagEnd] == '/' ? 2 : 1);

            string element = svg[tagStart..tagEnd];
            var points = ExtractAttrInElement(element, "points");
            if (points is not null)
            {
                // Convert polygon points to path data: M x1,y1 L x2,y2 ... Z
                var pairs = points.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var sb = new StringBuilder();
                for (int i = 0; i < pairs.Length; i++)
                {
                    sb.Append(i == 0 ? 'M' : 'L');
                    sb.Append(pairs[i].Replace(',', ' '));
                    sb.Append(' ');
                }
                sb.Append('Z');
                var fill = ResolveFill(element, classColors);
                shapes.Add(new SvgShape(fill, sb.ToString()));
            }
            pos = tagEnd;
        }
    }

    private static void ParseRects(string svg, Dictionary<string, PdfColor> classColors, List<SvgShape> shapes)
    {
        int pos = 0;
        while (pos < svg.Length)
        {
            int tagStart = svg.IndexOf("<rect", pos, StringComparison.OrdinalIgnoreCase);
            if (tagStart < 0) break;
            int tagEnd = svg.IndexOf("/>", tagStart);
            if (tagEnd < 0) break;
            tagEnd += 2;

            string element = svg[tagStart..tagEnd];
            ParseF(ExtractAttrInElement(element, "x") ?? "0", out float x);
            ParseF(ExtractAttrInElement(element, "y") ?? "0", out float y);
            ParseF(ExtractAttrInElement(element, "width") ?? "0", out float w);
            ParseF(ExtractAttrInElement(element, "height") ?? "0", out float h);
            if (w > 0 && h > 0)
            {
                string pathData = $"M{Fmt(x)} {Fmt(y)} L{Fmt(x + w)} {Fmt(y)} L{Fmt(x + w)} {Fmt(y + h)} L{Fmt(x)} {Fmt(y + h)} Z";
                var fill = ResolveFill(element, classColors);
                shapes.Add(new SvgShape(fill, pathData));
            }
            pos = tagEnd;
        }
    }

    private static void ParseCircles(string svg, Dictionary<string, PdfColor> classColors, List<SvgShape> shapes)
    {
        int pos = 0;
        while (pos < svg.Length)
        {
            int tagStart = svg.IndexOf("<circle", pos, StringComparison.OrdinalIgnoreCase);
            if (tagStart < 0) break;
            int tagEnd = svg.IndexOf("/>", tagStart);
            if (tagEnd < 0) break;
            tagEnd += 2;

            string element = svg[tagStart..tagEnd];
            ParseF(ExtractAttrInElement(element, "cx") ?? "0", out float cx);
            ParseF(ExtractAttrInElement(element, "cy") ?? "0", out float cy);
            ParseF(ExtractAttrInElement(element, "r") ?? "0", out float r);
            if (r > 0)
            {
                // Approximate circle with 4 cubic Bezier curves (κ ≈ 0.5523)
                float k = r * 0.5523f;
                var sb = new StringBuilder();
                sb.Append($"M{Fmt(cx + r)} {Fmt(cy)} ");
                sb.Append($"C{Fmt(cx + r)} {Fmt(cy + k)} {Fmt(cx + k)} {Fmt(cy + r)} {Fmt(cx)} {Fmt(cy + r)} ");
                sb.Append($"C{Fmt(cx - k)} {Fmt(cy + r)} {Fmt(cx - r)} {Fmt(cy + k)} {Fmt(cx - r)} {Fmt(cy)} ");
                sb.Append($"C{Fmt(cx - r)} {Fmt(cy - k)} {Fmt(cx - k)} {Fmt(cy - r)} {Fmt(cx)} {Fmt(cy - r)} ");
                sb.Append($"C{Fmt(cx + k)} {Fmt(cy - r)} {Fmt(cx + r)} {Fmt(cy - k)} {Fmt(cx + r)} {Fmt(cy)} Z");
                var fill = ResolveFill(element, classColors);
                shapes.Add(new SvgShape(fill, sb.ToString()));
            }
            pos = tagEnd;
        }
    }

    // ── Path data number parsing ────────────────────────────────────────

    private static void SkipWhitespaceAndCommas(string s, ref int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == ',' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r'))
            i++;
    }

    private static bool TryReadNumber(string s, ref int i, out float value)
    {
        SkipWhitespaceAndCommas(s, ref i);
        value = 0;
        if (i >= s.Length) return false;

        // Numbers can start with -, +, ., or digit
        char c = s[i];
        if (!char.IsDigit(c) && c != '-' && c != '+' && c != '.') return false;

        int start = i;
        if (c == '-' || c == '+') i++;
        bool hasDot = false;
        while (i < s.Length)
        {
            c = s[i];
            if (char.IsDigit(c)) { i++; continue; }
            if (c == '.' && !hasDot) { hasDot = true; i++; continue; }
            // Scientific notation (e.g., 1.5e-3)
            if ((c == 'e' || c == 'E') && i > start)
            {
                i++;
                if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
                break;
            }
            break;
        }

        if (i == start) return false;
        return ParseF(s[start..i], out value);
    }

    private static void SkipUntilNextCommand(string s, ref int i)
    {
        while (i < s.Length && !char.IsLetter(s[i])) i++;
    }

    private static bool ParseF(string s, out float value)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Fmt(float v)
        => v.ToString("0.##", CultureInfo.InvariantCulture);
}
