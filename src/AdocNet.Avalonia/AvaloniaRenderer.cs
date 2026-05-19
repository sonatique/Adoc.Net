using System.Diagnostics;
using System.Text;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using AdocNet.Layout;
using AvInline = global::Avalonia.Controls.Documents.Inline;

namespace AdocNet.Avalonia;

/// <summary>
/// Renders a <see cref="DocumentLayout"/> tree into Avalonia controls.
/// </summary>
public class AvaloniaRenderer
{
    private static readonly FontFamily MonospaceFont = new("Cascadia Mono, Consolas, Courier New, monospace");
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.FromRgb(0, 102, 204));
    private static readonly IBrush CodeBlockBackground = new SolidColorBrush(Color.FromRgb(245, 245, 245));
    private static readonly IBrush CodeLangForeground = new SolidColorBrush(Color.FromRgb(136, 136, 136));
    private static readonly IBrush TableBorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly IBrush TableHeaderBackground = new SolidColorBrush(Color.FromRgb(240, 240, 240));
    private static readonly IBrush ThematicBreakBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly IBrush DescTermForeground = new SolidColorBrush(Color.FromRgb(60, 60, 60));
    private const string BulletPrefix = "\u2022 ";

    /// <summary>
    /// When true (the default), <see cref="Render"/> wraps the produced
    /// content panel in a <see cref="ScrollViewer"/> with horizontal scrolling
    /// disabled. Set to false to receive the bare content control instead —
    /// the natural choice when the consumer already hosts the result inside
    /// its own scrolling container (e.g. an editor preview pane).
    /// </summary>
    public bool WrapInScrollViewer { get; set; } = true;

    /// <summary>
    /// Renders a document layout into an Avalonia control tree.
    /// </summary>
    /// <param name="document">The layout tree to render.</param>
    /// <returns>
    /// When <see cref="WrapInScrollViewer"/> is true, a <see cref="ScrollViewer"/>
    /// containing the rendered document. When false, the bare content panel
    /// itself, leaving scroll handling to the caller.
    /// </returns>
    public Control Render(DocumentLayout document)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };

        foreach (var block in document.Children)
        {
            var control = RenderBlock(block);
            if (control != null)
            {
                panel.Children.Add(control);
            }
        }

        if (!WrapInScrollViewer)
            return panel;

        return new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private Control? RenderBlock(BlockLayout block)
    {
        switch (block)
        {
            case ParagraphLayout paragraph:
                return RenderParagraph(paragraph);
            case HeadingLayout heading:
                return RenderHeading(heading);
            case ListLayout list:
                return RenderList(list);
            case CodeBlockLayout codeBlock:
                return RenderCodeBlock(codeBlock);
            case AdmonitionLayout admonition:
                return RenderAdmonition(admonition);
            case TableLayout table:
                return RenderTable(table);
            case DescriptionListLayout descList:
                return RenderDescriptionList(descList);
            case ThematicBreakLayout:
                return RenderThematicBreak();
            default:
                return null;
        }
    }

    // ── Block rendering ─────────────────────────────────────────────

    private TextBlock RenderParagraph(ParagraphLayout paragraph)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        AddInlines(textBlock.Inlines!, paragraph.Inlines);
        return textBlock;
    }

    private TextBlock RenderHeading(HeadingLayout heading)
    {
        double fontSize = heading.Level switch
        {
            1 => 24,
            2 => 20,
            3 => 18,
            4 => 16,
            5 => 14,
            _ => 13,
        };

        var textBlock = new TextBlock
        {
            FontSize = fontSize,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 4),
        };
        AddInlines(textBlock.Inlines!, heading.Inlines);
        return textBlock;
    }

    private StackPanel RenderList(ListLayout list)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(16, 0, 0, 8),
        };

        for (int i = 0; i < list.Items.Count; i++)
        {
            var item = list.Items[i];
            var prefix = list.Ordered ? $"{i + 1}. " : BulletPrefix;
            var itemPanel = RenderListItem(item, prefix);
            panel.Children.Add(itemPanel);
        }

        return panel;
    }

    private StackPanel RenderListItem(ListItemLayout item, string prefix)
    {
        var panel = new StackPanel();

        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2),
        };
        textBlock.Inlines!.Add(new Run(prefix));
        AddInlines(textBlock.Inlines!, item.Inlines);
        panel.Children.Add(textBlock);

        foreach (var nested in item.Blocks)
        {
            var control = RenderBlock(nested);
            if (control != null)
            {
                panel.Children.Add(control);
            }
        }

        return panel;
    }

    private Border RenderCodeBlock(CodeBlockLayout codeBlock)
    {
        var codeText = new TextBlock
        {
            Text = codeBlock.Text,
            FontFamily = MonospaceFont,
            TextWrapping = TextWrapping.NoWrap,
        };

        Control child;
        if (codeBlock.Language != null)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = codeBlock.Language,
                FontSize = 11,
                Foreground = CodeLangForeground,
                Margin = new Thickness(0, 0, 0, 4),
            });
            panel.Children.Add(codeText);
            child = panel;
        }
        else
        {
            child = codeText;
        }

        return new Border
        {
            Background = CodeBlockBackground,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(4),
            Child = child,
        };
    }

    private Border RenderAdmonition(AdmonitionLayout admonition)
    {
        var (accentColor, bgColor, labelColor) = GetAdmonitionColors(admonition.Kind);

        var panel = new StackPanel();

        var label = new TextBlock
        {
            Text = admonition.Kind.ToString().ToUpperInvariant(),
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(labelColor),
            Margin = new Thickness(0, 0, 0, 4),
        };
        panel.Children.Add(label);

        foreach (var block in admonition.Blocks)
        {
            var control = RenderBlock(block);
            if (control != null)
            {
                panel.Children.Add(control);
            }
        }

        return new Border
        {
            Background = new SolidColorBrush(bgColor),
            BorderBrush = new SolidColorBrush(accentColor),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = panel,
        };
    }

    private static (Color accent, Color background, Color label) GetAdmonitionColors(AdmonitionKind kind)
    {
        return kind switch
        {
            AdmonitionKind.Note => (Color.FromRgb(70, 130, 180), Color.FromRgb(240, 248, 255), Color.FromRgb(50, 100, 150)),
            AdmonitionKind.Tip => (Color.FromRgb(60, 150, 120), Color.FromRgb(240, 255, 245), Color.FromRgb(40, 120, 90)),
            AdmonitionKind.Warning => (Color.FromRgb(210, 160, 50), Color.FromRgb(255, 252, 240), Color.FromRgb(180, 130, 30)),
            AdmonitionKind.Important => (Color.FromRgb(210, 120, 50), Color.FromRgb(255, 248, 240), Color.FromRgb(180, 90, 30)),
            AdmonitionKind.Caution => (Color.FromRgb(200, 60, 60), Color.FromRgb(255, 242, 242), Color.FromRgb(170, 40, 40)),
            _ => (Color.FromRgb(70, 130, 180), Color.FromRgb(240, 248, 255), Color.FromRgb(50, 100, 150)),
        };
    }

    // ── Table rendering ─────────────────────────────────────────────

    private Control RenderTable(TableLayout table)
    {
        var wrapper = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

        if (table.Title != null)
        {
            wrapper.Children.Add(new TextBlock
            {
                Text = table.Title,
                FontWeight = FontWeight.Bold,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }

        if (table.Rows.Count == 0)
            return wrapper;

        // Determine column count from first row
        int colCount = 0;
        foreach (var row in table.Rows)
        {
            int rowCols = 0;
            foreach (var cell in row.Cells)
                rowCols += cell.ColSpan;
            if (rowCols > colCount)
                colCount = rowCols;
        }

        if (colCount == 0)
            return wrapper;

        var grid = new Grid();
        for (int c = 0; c < colCount; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        int gridRow = 0;
        // Track row-span occupancy: occupied[col] = how many more rows that col is spanned
        var occupied = new int[colCount];

        foreach (var row in table.Rows)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            int col = 0;
            int cellIdx = 0;

            while (col < colCount && cellIdx < row.Cells.Count)
            {
                // Skip columns occupied by row-spanning cells from prior rows
                while (col < colCount && occupied[col] > 0)
                {
                    occupied[col]--;
                    col++;
                }

                if (col >= colCount)
                    break;

                var cell = row.Cells[cellIdx];
                var cellControl = RenderTableCell(cell);

                Grid.SetRow(cellControl, gridRow);
                Grid.SetColumn(cellControl, col);
                if (cell.ColSpan > 1)
                    Grid.SetColumnSpan(cellControl, cell.ColSpan);
                if (cell.RowSpan > 1)
                    Grid.SetRowSpan(cellControl, cell.RowSpan);

                grid.Children.Add(cellControl);

                // Mark future rows as occupied for row-spanning cells
                if (cell.RowSpan > 1)
                {
                    for (int sc = col; sc < col + cell.ColSpan && sc < colCount; sc++)
                        occupied[sc] = cell.RowSpan - 1;
                }

                col += cell.ColSpan;
                cellIdx++;
            }

            // Decrement remaining occupied counts for columns we didn't visit
            while (col < colCount)
            {
                if (occupied[col] > 0)
                    occupied[col]--;
                col++;
            }

            gridRow++;
        }

        // Add extra row definitions for row-spanned cells
        while (grid.RowDefinitions.Count < gridRow)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var tableBorder = new Border
        {
            BorderBrush = TableBorderBrush,
            BorderThickness = new Thickness(1),
            Child = grid,
        };

        wrapper.Children.Add(tableBorder);
        return wrapper;
    }

    private Border RenderTableCell(TableCellLayout cell)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontWeight = cell.IsHeader ? FontWeight.Bold : FontWeight.Normal,
        };
        AddInlines(textBlock.Inlines!, cell.Inlines);

        return new Border
        {
            BorderBrush = TableBorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(6, 4),
            Background = cell.IsHeader ? TableHeaderBackground : null,
            Child = textBlock,
        };
    }

    // ── Description list rendering ──────────────────────────────────

    private StackPanel RenderDescriptionList(DescriptionListLayout descList)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        foreach (var item in descList.Items)
        {
            var termBlock = new TextBlock
            {
                FontWeight = FontWeight.Bold,
                Foreground = DescTermForeground,
                TextWrapping = TextWrapping.Wrap,
            };
            AddInlines(termBlock.Inlines!, item.Term);
            panel.Children.Add(termBlock);

            var descBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16, 0, 0, 6),
            };
            AddInlines(descBlock.Inlines!, item.Description);
            panel.Children.Add(descBlock);
        }

        return panel;
    }

    // ── Thematic break rendering ────────────────────────────────────

    private static Border RenderThematicBreak()
    {
        return new Border
        {
            Height = 1,
            Background = ThematicBreakBrush,
            Margin = new Thickness(0, 8, 0, 8),
        };
    }

    // ── Inline rendering ────────────────────────────────────────────

    private void AddInlines(InlineCollection target, IReadOnlyList<InlineLayout> inlines)
    {
        foreach (var inline in inlines)
        {
            var rendered = RenderInline(inline);
            if (rendered != null)
            {
                target.Add(rendered);
            }
        }
    }

    private AvInline? RenderInline(InlineLayout inline)
    {
        switch (inline)
        {
            case TextRun text:
                return new Run(text.Text);

            case BoldRun bold:
            {
                var span = new Bold();
                AddInlines(span.Inlines, bold.Children);
                return span;
            }

            case ItalicRun italic:
            {
                var span = new Italic();
                AddInlines(span.Inlines, italic.Children);
                return span;
            }

            case MonoRun mono:
            {
                var span = new Span { FontFamily = MonospaceFont };
                AddInlines(span.Inlines, mono.Children);
                return span;
            }

            case LinkRun link:
            {
                var linkText = new TextBlock
                {
                    Foreground = LinkBrush,
                    TextDecorations = global::Avalonia.Media.TextDecorations.Underline,
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                AddInlines(linkText.Inlines!, link.Children);
                linkText.PointerPressed += (_, _) => OpenUrl(link.Href);
                return new InlineUIContainer { Child = linkText };
            }

            case LineBreakRun:
                return new LineBreak();

            default:
                return null;
        }
    }

    /// <summary>
    /// Extracts plain text from a list of inline layout nodes.
    /// </summary>
    internal static string GetPlainText(IReadOnlyList<InlineLayout> inlines)
    {
        var sb = new StringBuilder();
        AppendPlainText(sb, inlines);
        return sb.ToString();
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
                    sb.Append('\n');
                    break;
            }
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Silently ignore failures to open URLs
        }
    }
}
