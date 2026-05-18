using System.Linq;
using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class TableEmitter
{
    public static void Emit(TableNode table, EmitContext ctx)
    {
        if (!string.IsNullOrEmpty(table.Title))
        {
            ctx.Output.Append('.');
            ctx.Output.Append(table.Title);
            ctx.Output.Append('\n');
        }

        BlockAttributesEmitter.Emit(table, ctx);
        EmitTableAttributesLine(table, ctx);

        ctx.Output.Append("|===\n");

        var rows = table.Children.OfType<TableRowNode>().ToList();
        for (int r = 0; r < rows.Count; r++)
        {
            EmitRow(rows[r], ctx);
            // Asciidoctor signals the implicit header by a blank line after
            // the first row when [options="header"] is set.
            if (r == 0 && table.HasHeader && rows.Count > 1)
                ctx.Output.Append('\n');
        }

        ctx.Output.Append("|===\n");
    }

    private static void EmitRow(TableRowNode row, EmitContext ctx)
    {
        foreach (var child in row.Children)
        {
            if (child is not TableCellNode cell) continue;
            EmitCell(cell, ctx);
        }
        ctx.Output.Append('\n');
    }

    private static void EmitCell(TableCellNode cell, EmitContext ctx)
    {
        // Span / alignment / style prefix. Order matters for the parser:
        // colspan, [.rowspan], +, then optional alignment, then style letter,
        // then the | separator.
        if (cell.ColSpan > 1 || cell.RowSpan > 1)
        {
            if (cell.ColSpan > 1) ctx.Output.Append(cell.ColSpan);
            if (cell.RowSpan > 1)
            {
                ctx.Output.Append('.');
                ctx.Output.Append(cell.RowSpan);
            }
            ctx.Output.Append('+');
        }

        if (cell.Alignment is TableAlignment alignment)
        {
            ctx.Output.Append(alignment switch
            {
                TableAlignment.Center => '^',
                TableAlignment.Right => '>',
                _ => '<',
            });
        }

        if (cell.ContentStyle != TableCellStyle.Default)
        {
            ctx.Output.Append(cell.ContentStyle switch
            {
                TableCellStyle.AsciiDoc => 'a',
                TableCellStyle.Emphasis => 'e',
                TableCellStyle.Header => 'h',
                TableCellStyle.Literal => 'l',
                TableCellStyle.Monospace => 'm',
                TableCellStyle.Strong => 's',
                _ => ' ',
            });
        }

        ctx.Output.Append('|');

        if (cell.Inlines.Count > 0)
            InlineEmitter.EmitAll(cell.Inlines, ctx);
        else
            ctx.Output.Append(cell.Text);
    }

    private static void EmitTableAttributesLine(TableNode table, EmitContext ctx)
    {
        var parts = new List<string>();
        if (table.Columns is { Count: > 0 } cols)
        {
            var colStr = string.Join(",", cols.Select(c =>
            {
                var alignPrefix = c.Alignment switch
                {
                    TableAlignment.Center => "^",
                    TableAlignment.Right => ">",
                    _ => "",
                };
                var vAlign = c.VerticalAlignment switch
                {
                    TableVerticalAlignment.Middle => ".^",
                    TableVerticalAlignment.Bottom => ".>",
                    _ => "",
                };
                return $"{alignPrefix}{vAlign}{c.Width}";
            }));
            parts.Add($"cols=\"{colStr}\"");
        }
        if (table.HasHeader || table.HasFooter)
        {
            var opts = new List<string>();
            if (table.HasHeader) opts.Add("header");
            if (table.HasFooter) opts.Add("footer");
            parts.Add($"options=\"{string.Join(",", opts)}\"");
        }
        if (table.IsAutoWidth) parts.Add("%autowidth");
        if (!string.IsNullOrEmpty(table.Stripes)) parts.Add($"stripes={table.Stripes}");
        if (!string.IsNullOrEmpty(table.Grid)) parts.Add($"grid={table.Grid}");
        if (!string.IsNullOrEmpty(table.Frame)) parts.Add($"frame={table.Frame}");

        if (parts.Count == 0) return;

        ctx.Output.Append('[');
        ctx.Output.Append(string.Join(", ", parts));
        ctx.Output.Append("]\n");
    }
}
