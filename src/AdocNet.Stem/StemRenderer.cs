using System.Collections.Concurrent;
using System.Drawing;
using System.Globalization;
using System.Text;
using CSharpMath.Rendering.FrontEnd;
using MathPath = CSharpMath.Rendering.FrontEnd.Path;

namespace AdocNet.Stem;

/// <summary>
/// Typesets LaTeX and AsciiMath with CSharpMath and its bundled Latin Modern Math font: managed code
/// only, no network, no native library, the same output on every platform. Pass one instance to
/// <see cref="RenderOptions.StemRenderer"/> and to the Avalonia renderer; it is safe to share between
/// threads and caches what it typesets.
/// </summary>
/// <remarks>
/// The first formula loads the fonts (a few hundred milliseconds). To keep that off a UI thread, typeset
/// one formula on a worker thread at startup, e.g. <c>Task.Run(() =&gt; renderer.Render("x", "latexmath", false))</c>.
/// </remarks>
public sealed class StemRenderer : IStemRenderer
{
    private const float UnitsPerEm = 100; // typeset at 100 units per em, written out in em

    // CSharpMath's font size is in points and its glyphs come out at 96/72 units per point.
    private const float PointSize = UnitsPerEm * 72 / 96;
    private const int MaxCached = 4096;

    // CSharpMath's typesetter and fonts are shared statics; one formula at a time.
    private static readonly object Typesetting = new();

    private readonly ConcurrentDictionary<(string Content, string Type, bool Display), StemGraphic?> _cache = new();

    /// <inheritdoc />
    public StemGraphic? Render(string content, string stemType, bool display)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));
        var key = (content, stemType ?? "", display);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        StemGraphic? graphic = null;
        // The content comes from the document: both parsers recurse per bracket, so a deeply nested
        // formula could overflow the stack, which no catch survives.
        if (content.Length <= MaxLength && Nesting(content) <= MaxNesting)
        {
            try
            {
                var latex = string.Equals(stemType, "asciimath", StringComparison.OrdinalIgnoreCase)
                    ? AsciiMath.ToLatex(content)
                    : content;
                lock (Typesetting)
                    graphic = Typeset(latex, display);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                graphic = null; // the typesetter failed on this input: the caller shows the source
            }
        }

        if (_cache.Count >= MaxCached)
            _cache.Clear(); // ponytail: dropped whole when full; enough for an editing session's formulas
        _cache[key] = graphic;
        return graphic;
    }

    private const int MaxLength = 20_000;
    private const int MaxNesting = 64;

    /// <summary>The deepest bracket nesting in <paramref name="text"/>.</summary>
    private static int Nesting(string text)
    {
        int depth = 0, max = 0;
        foreach (var c in text)
        {
            if (c is '(' or '[' or '{') max = Math.Max(max, ++depth);
            else if (c is ')' or ']' or '}' && depth > 0) depth--;
        }
        return max;
    }

    private static StemGraphic? Typeset(string latex, bool display)
    {
        if (string.IsNullOrWhiteSpace(latex))
            return null;
        var painter = new OutlinePainter
        {
            FontSize = PointSize,
            LineStyle = display ? CSharpMath.Atom.LineStyle.Display : CSharpMath.Atom.LineStyle.Text,
            DisplayErrorInline = false,
            LaTeX = latex,
        };
        if (painter.ErrorMessage is not null)
            return null;
        painter.Measure(float.NaN);
        if (painter.Display is not { } box)
            return null;

        var canvas = new OutlineCanvas(box.Width, box.Ascent + box.Descent);
        painter.Draw(canvas, 0, box.Ascent); // baseline at y = ascent, y growing down
        var path = canvas.PathData;
        return path.Length == 0
            ? null
            : new StemGraphic(path, box.Width / UnitsPerEm, box.Ascent / UnitsPerEm, box.Descent / UnitsPerEm);
    }

    private sealed class OutlinePainter : MathPainter<OutlineCanvas, Color>
    {
        public override Color UnwrapColor(Color color) => color;
        public override Color WrapColor(Color color) => color;
        public override ICanvas WrapCanvas(OutlineCanvas canvas) => canvas;
    }

    /// <summary>
    /// Collects every glyph and rule as one SVG path in em (absolute M, L, C and Z only: quadratic
    /// curves become cubic ones), ignoring colours and highlight boxes.
    /// </summary>
    private sealed class OutlineCanvas(float width, float height) : ICanvas
    {
        private readonly StringBuilder _path = new();
        private readonly Stack<(float Sx, float Sy, float Tx, float Ty)> _saved = new();
        private (float Sx, float Sy, float Tx, float Ty) _m = (1, 1, 0, 0);

        public string PathData => _path.ToString();
        public float Width => width;
        public float Height => height;
        public Color DefaultColor { get; set; }
        public Color? CurrentColor { get; set; }
        public PaintStyle CurrentStyle { get; set; }

        public void Save() => _saved.Push(_m);
        public void Restore() => _m = _saved.Pop();
        public void Translate(float dx, float dy) => _m = (_m.Sx, _m.Sy, _m.Tx + _m.Sx * dx, _m.Ty + _m.Sy * dy);
        public void Scale(float sx, float sy) => _m = (_m.Sx * sx, _m.Sy * sy, _m.Tx, _m.Ty);
        public MathPath StartNewPath() => new OutlinePath(this);
        public void DrawLine(float x1, float y1, float x2, float y2, float lineThickness) =>
            this.StrokeLineOutline(x1, y1, x2, y2, lineThickness);
        public void StrokeRect(float left, float top, float width, float height) { } // glyph boxes (debugging aid)
        // ponytail: highlight and \colorbox fills are dropped; drawn in the text colour they would hide the formula.
        public void FillRect(float left, float top, float width, float height) { }

        private void Point(char command, float x, float y)
        {
            if (command != ' ') _path.Append(command);
            else _path.Append(' ');
            _path.Append(((_m.Tx + _m.Sx * x) / UnitsPerEm).ToString("0.####", CultureInfo.InvariantCulture)).Append(' ')
                 .Append(((_m.Ty + _m.Sy * y) / UnitsPerEm).ToString("0.####", CultureInfo.InvariantCulture));
        }

        private sealed class OutlinePath(OutlineCanvas canvas) : MathPath
        {
            private float _x, _y;
            private bool _open;

            public override Color? Foreground { get; set; }

            public override void MoveTo(float x0, float y0)
            {
                canvas.Point('M', x0, y0);
                (_x, _y, _open) = (x0, y0, true);
            }

            public override void LineTo(float x1, float y1)
            {
                canvas.Point('L', x1, y1);
                (_x, _y) = (x1, y1);
            }

            public override void Curve3(float x1, float y1, float x2, float y2) =>
                Curve4(_x + 2f / 3 * (x1 - _x), _y + 2f / 3 * (y1 - _y), x2 + 2f / 3 * (x1 - x2), y2 + 2f / 3 * (y1 - y2), x2, y2);

            public override void Curve4(float x1, float y1, float x2, float y2, float x3, float y3)
            {
                canvas.Point('C', x1, y1);
                canvas.Point(' ', x2, y2);
                canvas.Point(' ', x3, y3);
                (_x, _y) = (x3, y3);
            }

            public override void CloseContour()
            {
                // A CFF glyph closes before its first contour: only close an open one.
                if (!_open) return;
                canvas._path.Append('Z');
                _open = false;
            }

            public override void Dispose() => CloseContour();
        }
    }
}
