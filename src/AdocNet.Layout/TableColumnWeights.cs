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
    /// the columns they cover). Each weight is then capped at
    /// <c>max(1, 3 × median)</c> so one cell of long prose cannot
    /// dominate the table's width.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Issue #16 introduced content-weighted star columns so wide tables
    /// fit their host viewport. Issue #26 reported the consequence: in a
    /// table with one cell of long prose, that column's raw weight
    /// (≈150 chars) was an order of magnitude greater than the other
    /// columns' (≈4–15 chars), so it took ~half the viewport and every
    /// other column collapsed to one letter per line.
    /// </para>
    /// <para>
    /// Capping each weight at 3× the median tames the outlier without
    /// affecting tables where columns are similarly sized — in that case
    /// no column exceeds the cap and weights are returned unchanged.
    /// </para>
    /// </remarks>
    public static double[] Compute(TableLayout table, int columnCount)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        if (columnCount <= 0) return Array.Empty<double>();

        var weights = new double[columnCount];
        foreach (var row in table.Rows)
        {
            int col = 0;
            foreach (var cell in row.Cells)
            {
                if (col >= columnCount) break;
                int span = cell.ColSpan > 0 ? cell.ColSpan : 1;
                double cellLen = PlainTextLength(cell.Inlines);
                double perCol = cellLen / span;
                for (int s = 0; s < span && col + s < columnCount; s++)
                {
                    if (perCol > weights[col + s])
                        weights[col + s] = perCol;
                }
                col += span;
            }
        }

        return CapAtMedianMultiple(weights, multiple: 3);
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

    private static int PlainTextLength(IReadOnlyList<InlineLayout> inlines)
    {
        var sb = new StringBuilder();
        AppendPlainText(sb, inlines);
        return sb.Length;
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
