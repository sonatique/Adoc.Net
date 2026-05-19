using System;
using System.Collections.Generic;
using System.Text;

namespace AdocNet.Layout;

/// <summary>
/// Helpers for computing per-column proportional weights for a
/// <see cref="TableLayout"/>, suitable for driving a star-share layout
/// such as Avalonia's <c>GridLength.Star</c>.
/// </summary>
public static class TableColumnWeights
{
    /// <summary>
    /// Computes per-column star-share weights for the given table.
    /// Each column's raw weight is the longest plain-text cell length in
    /// that column (spanning cells contribute their length evenly across
    /// the columns they cover). Cells are placed into columns honouring
    /// row-spans from prior rows. Each weight is then capped at
    /// <c>max(1, 3 × median)</c> so one cell of long prose cannot dominate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Issue #16 introduced content-weighted star columns so wide tables
    /// fit their host viewport. Issue #26 reported two compounding bugs:
    /// the cap could be defeated by tables with multiple prose columns,
    /// and — more critically — cells from rows following a row-spanned
    /// cell were attributed to the wrong column index because the
    /// algorithm did not skip occupied columns. With proper row-span
    /// tracking the weights actually match each column's content.
    /// </para>
    /// </remarks>
    public static double[] Compute(TableLayout table, int columnCount)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (columnCount <= 0) return Array.Empty<double>();

        var weights = new double[columnCount];
        ForEachPlacedCell(table, columnCount, (col, span, _, inlines) =>
        {
            double cellLen = PlainTextLength(inlines);
            double perCol = cellLen / span;
            for (int s = 0; s < span && col + s < columnCount; s++)
            {
                if (perCol > weights[col + s])
                    weights[col + s] = perCol;
            }
        });

        return CapAtMedianMultiple(weights, multiple: 3);
    }

    /// <summary>
    /// Computes per-column minimum widths in pixels so that the longest
    /// single (unbreakable) word in each column fits without forcing the
    /// host viewport to scroll horizontally. Designed to be set as
    /// <c>ColumnDefinition.MinWidth</c> alongside the star weights from
    /// <see cref="Compute"/>.
    /// </summary>
    /// <remarks>
    /// Star sharing alone proportions columns by content volume; without
    /// a minimum, columns whose star share is small still need enough
    /// width to keep their longest word on one line. Combining a star
    /// weight with this min-width floor lets narrow columns render their
    /// header / identifier readably while wider columns get the leftover
    /// space.
    /// </remarks>
    /// <param name="table">The table whose columns to measure.</param>
    /// <param name="columnCount">Total column count.</param>
    /// <param name="pixelsPerChar">
    /// Conservative estimate of average glyph width in pixels at the
    /// renderer's body font size. Default 7.5 ≈ Segoe UI 13pt.
    /// </param>
    /// <param name="horizontalPaddingPixels">
    /// Total horizontal padding applied to each cell (left + right + any
    /// border). Default 16.
    /// </param>
    public static double[] ComputeMinWidthsPixels(TableLayout table, int columnCount,
        double pixelsPerChar = 7.5, double horizontalPaddingPixels = 16.0)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (columnCount <= 0) return Array.Empty<double>();

        var minChars = new int[columnCount];
        ForEachPlacedCell(table, columnCount, (col, span, _, inlines) =>
        {
            // The longest single word is what can't be broken; that's the
            // floor below which the cell content visibly overflows or
            // collapses to one letter per line. Spans contribute only to
            // the cell's starting column (a spanning cell with one giant
            // word still pins only the first column of its span).
            int longestWord = LongestWordLength(inlines);
            if (longestWord > minChars[col])
                minChars[col] = longestWord;
        });

        var result = new double[columnCount];
        for (int c = 0; c < columnCount; c++)
        {
            // Even an empty column needs a small minimum so the Grid
            // doesn't collapse it to zero width.
            int chars = minChars[c] > 0 ? minChars[c] : 1;
            result[c] = chars * pixelsPerChar + horizontalPaddingPixels;
        }
        return result;
    }

    /// <summary>
    /// Caps each weight at <c>max(1, multiple × median)</c>. Weights of
    /// 0 are floored at 1 so star layouts never see Star(0). Pure function;
    /// the input array is not mutated.
    /// </summary>
    internal static double[] CapAtMedianMultiple(double[] rawWeights, double multiple)
    {
        if (rawWeights.Length == 0) return rawWeights;

        var sorted = (double[])rawWeights.Clone();
        Array.Sort(sorted);
        double median = sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0;
        double cap = median * multiple;
        if (cap < 1) cap = 1;

        var result = new double[rawWeights.Length];
        for (int c = 0; c < rawWeights.Length; c++)
        {
            double w = rawWeights[c];
            if (w < 1) w = 1;
            if (w > cap) w = cap;
            result[c] = w;
        }
        return result;
    }

    /// <summary>
    /// Walks the table's cells in their actual grid positions, honouring
    /// row-spans from prior rows. Calls <paramref name="visit"/> with the
    /// resolved column index, column span, row span, and the cell's
    /// inlines for each placed cell. Mirrors the placement logic in
    /// <c>AvaloniaRenderer.RenderTable</c>, so weight computation lines
    /// up with what the renderer actually shows.
    /// </summary>
    private static void ForEachPlacedCell(TableLayout table, int columnCount,
        Action<int, int, int, IReadOnlyList<InlineLayout>> visit)
    {
        // occupied[c] = how many more rows column c is occupied by an
        // earlier row-spanning cell.
        var occupied = new int[columnCount];

        foreach (var row in table.Rows)
        {
            int col = 0;
            int cellIdx = 0;

            while (col < columnCount && cellIdx < row.Cells.Count)
            {
                // Skip columns held by a row-span from a prior row.
                while (col < columnCount && occupied[col] > 0)
                {
                    occupied[col]--;
                    col++;
                }
                if (col >= columnCount) break;

                var cell = row.Cells[cellIdx];
                int span = cell.ColSpan > 0 ? cell.ColSpan : 1;
                int rowSpan = cell.RowSpan > 0 ? cell.RowSpan : 1;

                visit(col, span, rowSpan, cell.Inlines);

                if (rowSpan > 1)
                {
                    for (int sc = col; sc < col + span && sc < columnCount; sc++)
                        occupied[sc] = rowSpan - 1;
                }

                col += span;
                cellIdx++;
            }

            // Decrement any occupied counters we didn't visit in this row
            // (because we ran out of cells before reaching them).
            while (col < columnCount)
            {
                if (occupied[col] > 0) occupied[col]--;
                col++;
            }
        }
    }

    private static int PlainTextLength(IReadOnlyList<InlineLayout> inlines)
    {
        var sb = new StringBuilder();
        AppendPlainText(sb, inlines);
        return sb.Length;
    }

    private static int LongestWordLength(IReadOnlyList<InlineLayout> inlines)
    {
        var sb = new StringBuilder();
        AppendPlainText(sb, inlines);
        int longest = 0;
        int current = 0;
        for (int i = 0; i < sb.Length; i++)
        {
            char ch = sb[i];
            if (char.IsWhiteSpace(ch))
            {
                if (current > longest) longest = current;
                current = 0;
            }
            else
            {
                current++;
            }
        }
        if (current > longest) longest = current;
        return longest;
    }

    private static void AppendPlainText(StringBuilder sb, IReadOnlyList<InlineLayout> inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextRun text:
                    sb.Append(text.Text);
                    break;
                case BoldRun bold:
                    AppendPlainText(sb, bold.Children);
                    break;
                case ItalicRun italic:
                    AppendPlainText(sb, italic.Children);
                    break;
                case MonoRun mono:
                    AppendPlainText(sb, mono.Children);
                    break;
                case LinkRun link:
                    AppendPlainText(sb, link.Children);
                    break;
                case LineBreakRun:
                    sb.Append(' ');
                    break;
            }
        }
    }
}
