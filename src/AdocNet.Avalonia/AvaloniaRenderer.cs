using System.Diagnostics;
using System.Text;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Threading;
using AdocNet;
using AdocNet.Layout;
using AvInline = global::Avalonia.Controls.Documents.Inline;

namespace AdocNet.Avalonia;

/// <summary>
/// Renders a <see cref="DocumentLayout"/> tree into Avalonia controls.
/// </summary>
public class AvaloniaRenderer
{
    private const string BulletPrefix = "\u2022 ";
    private const string CheckedPrefix = "\u2611 ";
    private const string UncheckedPrefix = "\u2610 ";

    /// <summary>
    /// Visual styling (brushes, fonts, sizes). Defaults to a fresh
    /// <see cref="AvaloniaRenderTheme"/>; assign a new one or mutate its
    /// properties to re-theme the preview (e.g. dark mode, host palette).
    /// </summary>
    public AvaloniaRenderTheme Theme { get; set; } = new();

    /// <summary>
    /// Raised when a rendered hyperlink is clicked. Handle it to intercept
    /// navigation (route <c>xref:</c> internally, sandbox external URLs, \u2026);
    /// if no handler sets <see cref="LinkClickedEventArgs.Handled"/>, the
    /// renderer opens the URL with the OS shell.
    /// </summary>
    public event EventHandler<LinkClickedEventArgs>? LinkClicked;

    /// <summary>
    /// Attached property recording the AsciiDoc <see cref="SourceRange"/> of the
    /// inline that produced each rendered <see cref="AvInline"/>. An editor can
    /// read it from the inline under the pointer (e.g. via a hit-test) to map a
    /// click to a source offset at inline granularity.
    /// </summary>
    public static readonly AttachedProperty<SourceRange> SourceRangeProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaRenderer, AvInline, SourceRange>(
            "SourceRange", SourceRange.None);

    /// <summary>Reads the <see cref="SourceRangeProperty"/> of a rendered inline.</summary>
    public static SourceRange GetSourceRange(AvInline inline) => inline.GetValue(SourceRangeProperty);

    /// <summary>Sets the <see cref="SourceRangeProperty"/> of a rendered inline.</summary>
    public static void SetSourceRange(AvInline inline, SourceRange value) => inline.SetValue(SourceRangeProperty, value);

    /// <summary>
    /// When true (the default), <see cref="Render(DocumentLayout)"/> wraps
    /// the produced content panel in a <see cref="ScrollViewer"/> with
    /// horizontal scrolling disabled. Set to false to receive the bare
    /// content control instead — the natural choice when the consumer
    /// already hosts the result inside its own scrolling container
    /// (e.g. an editor preview pane).
    /// </summary>
    public bool WrapInScrollViewer { get; set; } = true;

    /// <summary>
    /// Directory that relative image paths resolve against, usually the document's folder.
    /// When null, only absolute paths load. Images load from local files only: URLs and
    /// network (UNC) paths show their alternative text instead, so opening a document
    /// never makes a network request.
    /// </summary>
    public string? BaseDirectory { get; set; }

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

        if (!string.IsNullOrEmpty(document.Title))
            panel.Children.Add(CreateTitleBlock(document.Title!));

        foreach (var block in document.Children)
        {
            var control = Render(block);
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

    /// <summary>
    /// Renders a single <see cref="BlockLayout"/> into an Avalonia control.
    /// Public entry point used by the incremental renderer to rebuild
    /// individual top-level blocks without re-rendering the whole document.
    /// Returns null when the block kind is not recognised.
    /// </summary>
    public Control? Render(BlockLayout block) => ApplyBodyStyle(RenderBlock(block));

    /// <summary>
    /// <see cref="Render(BlockLayout)"/> of a list with only the items from <paramref name="first"/> to
    /// <paramref name="last"/> built, after and before empty space as tall as the other items.
    /// </summary>
    internal Control? RenderListPart(ListLayout list, int first, int last, double heightBefore, double heightAfter) =>
        first >= 0 && last < list.Items.Count && first <= last
            ? ApplyBodyStyle(RenderList(list, (first, last, heightBefore, heightAfter)))
            : null;

    /// <summary>
    /// <see cref="Render(BlockLayout)"/> of a table with only the cells within the grid rows of
    /// <paramref name="part"/> built. Null when the grid does not have the part's rows and columns.
    /// </summary>
    internal Control? RenderTablePart(TableLayout table, TablePart part) =>
        RenderTable(table, part) is { } control ? ApplyBodyStyle(control) : null;

    /// <summary>
    /// The part of a table's grid to build: its columns, the height of every grid row, and the rows
    /// <see cref="FirstRow"/> to <see cref="LastRow"/> whose cells are built. The rows before and after
    /// become one empty row each, as tall as they are together.
    /// </summary>
    internal readonly record struct TablePart(
        IReadOnlyList<(GridLength Width, double MinWidth)> Columns, IReadOnlyList<double> RowHeights, int FirstRow, int LastRow);

    /// <summary>True when <see cref="RenderBlock"/> is not overridden, so list and table parts render as the whole blocks do.</summary>
    internal bool RendersBlocksItself => _rendersBlocksItself ??=
        GetType().GetMethod(nameof(RenderBlock), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            null, [typeof(BlockLayout)], null)?.DeclaringType == typeof(AvaloniaRenderer);

    private bool? _rendersBlocksItself;

    /// <summary>
    /// Builds the document-title block (shared by the full, paged and incremental renderers).
    /// </summary>
    internal TextBlock CreateTitleBlock(string title)
    {
        var textBlock = new TextBlock
        {
            Text = title,
            FontSize = Theme.DocumentTitleFontSize,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        ApplyBodyStyle(textBlock);
        ApplyHeadingStyle(textBlock, 0);
        return textBlock;
    }

    // Set on each top-level block (not the page/panel) so blocks measured before
    // they join the visual tree (paged rendering) already carry the body style.
    private Control? ApplyBodyStyle(Control? control)
    {
        if (control is null) return null;
        // A block that sets its own value (a heading is its own root) keeps it.
        if (Theme.BodyFont is { } font && !control.IsSet(TextElement.FontFamilyProperty))
            TextElement.SetFontFamily(control, font);
        if (Theme.BodyForeground is { } fg && !control.IsSet(TextElement.ForegroundProperty))
            TextElement.SetForeground(control, fg);
        return control;
    }

    private void ApplyHeadingStyle(TextBlock textBlock, int level)
    {
        if (Theme.HeadingFont is { } font) textBlock.FontFamily = font;
        if (Theme.HeadingForeground(level) is { } fg) textBlock.Foreground = fg;
    }

    /// <summary>
    /// Dispatches a block layout to its renderer. Override to render custom
    /// block kinds: handle your own and call <c>base.RenderBlock</c> for the
    /// rest. Returning null drops the block.
    /// </summary>
    protected virtual Control? RenderBlock(BlockLayout block)
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
            case ImageLayout image:
                return RenderImage(image);
            case QuoteLayout quote:
                return RenderQuote(quote);
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
        var textBlock = new TextBlock
        {
            FontSize = Theme.HeadingFontSize(heading.Level),
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 4),
        };
        ApplyHeadingStyle(textBlock, heading.Level);
        AddInlines(textBlock.Inlines!, heading.Inlines);
        return textBlock;
    }

    private StackPanel RenderList(ListLayout list, (int First, int Last, double Before, double After)? part = null)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(16, 0, 0, 8),
        };

        var (from, to) = part is { } p ? (p.First, p.Last) : (0, list.Items.Count - 1);
        if (part is { Before: > 0 } before)
            panel.Children.Add(new Border { Height = before.Before });
        for (int i = from; i <= to; i++)
        {
            var item = list.Items[i];
            var prefix = list.Ordered ? $"{i + 1}. " : BulletPrefix;
            if (item.Checked is { } isChecked)
                prefix = (list.Ordered ? prefix : "") + (isChecked ? CheckedPrefix : UncheckedPrefix);
            var itemPanel = RenderListItem(item, prefix);
            panel.Children.Add(itemPanel);
        }
        if (part is { After: > 0 } after)
            panel.Children.Add(new Border { Height = after.After });

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
            FontFamily = Theme.MonospaceFont,
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
                Foreground = Theme.CodeLanguageForeground,
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
            Background = Theme.CodeBlockBackground,
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

    private Control? RenderTable(TableLayout table, TablePart? part = null)
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
        if (part is { } columns)
        {
            if (columns.Columns.Count != colCount || columns.RowHeights.Count != table.Rows.Count
                || columns.FirstRow < 0 || columns.LastRow >= table.Rows.Count || columns.FirstRow > columns.LastRow)
                return null;
            foreach (var (width, minWidth) in columns.Columns)
                grid.ColumnDefinitions.Add(new ColumnDefinition(width) { MinWidth = minWidth });
        }
        else
        {
            // Star weights are content-proportional (longest plain-text cell length
            // per column, honouring row-spans) and capped at 3× the median so a
            // single prose cell can't squeeze the rest of the table to one-letter-
            // per-line. Each column also gets a MinWidth equal to its longest
            // single word, so narrow columns never collapse below their content.
            // See issues #16, #26.
            var columnWeights = TableColumnWeights.Compute(table, colCount);
            var columnMinWidths = TableColumnWeights.ComputeMinWidthsPixels(table, colCount);
            for (int c = 0; c < colCount; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(columnWeights[c], GridUnitType.Star))
                {
                    MinWidth = columnMinWidths[c],
                });
            }
        }
        // In a part, the rows before FirstRow share the first grid row.
        int rowShift = part is { } shifted ? (shifted.FirstRow > 0 ? 1 : 0) - shifted.FirstRow : 0;

        int gridRow = 0;
        // Track row-span occupancy: occupied[col] = how many more rows that col is spanned
        var occupied = new int[colCount];

        foreach (var row in table.Rows)
        {
            if (part is null)
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
                // A span past the last row is clamped by the grid, as it is here.
                if (part is not { } p || gridRow >= p.FirstRow && Math.Min(gridRow + Math.Max(1, cell.RowSpan), table.Rows.Count) - 1 <= p.LastRow)
                {
                    var cellControl = RenderTableCell(cell);

                    Grid.SetRow(cellControl, gridRow + rowShift);
                    Grid.SetColumn(cellControl, col);
                    if (cell.ColSpan > 1)
                        Grid.SetColumnSpan(cellControl, cell.ColSpan);
                    if (cell.RowSpan > 1)
                        Grid.SetRowSpan(cellControl, cell.RowSpan);

                    grid.Children.Add(cellControl);
                }

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
        while (part is null && grid.RowDefinitions.Count < gridRow)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        if (part is { } rows)
        {
            double before = 0, after = 0;
            for (int r = 0; r < rows.FirstRow; r++) before += rows.RowHeights[r];
            for (int r = rows.LastRow + 1; r < rows.RowHeights.Count; r++) after += rows.RowHeights[r];
            if (rows.FirstRow > 0)
                grid.RowDefinitions.Add(new RowDefinition(new GridLength(before, GridUnitType.Pixel)));
            for (int r = rows.FirstRow; r <= rows.LastRow; r++)
                grid.RowDefinitions.Add(new RowDefinition(new GridLength(rows.RowHeights[r], GridUnitType.Pixel)));
            if (rows.LastRow < rows.RowHeights.Count - 1)
                grid.RowDefinitions.Add(new RowDefinition(new GridLength(after, GridUnitType.Pixel)));
        }

        var tableBorder = new Border
        {
            BorderBrush = Theme.TableBorderBrush,
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
        if (cell.IsHeader && Theme.TableHeaderForeground is { } headerFg)
            textBlock.Foreground = headerFg;
        AddInlines(textBlock.Inlines!, cell.Inlines);

        return new Border
        {
            BorderBrush = Theme.TableBorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(6, 4),
            Background = cell.IsHeader ? Theme.TableHeaderBackground : null,
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
                Foreground = Theme.DescriptionTermForeground,
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

    // ── Image and quote rendering ───────────────────────────────────

    private StackPanel RenderImage(ImageLayout image)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var host = new Decorator();
        panel.Children.Add(host);
        ShowImage(LoadImage(image.Target), host,
            bitmap =>
            {
                var control = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                if (PixelSize(image.Width) is { } width) control.MaxWidth = width;
                if (PixelSize(image.Height) is { } height) control.MaxHeight = height;
                return control;
            },
            () => new Border
            {
                Background = Theme.CodeBlockBackground,
                Padding = new Thickness(8),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = string.IsNullOrEmpty(image.Alt) ? image.Target : image.Alt,
                    FontStyle = FontStyle.Italic,
                    TextWrapping = TextWrapping.Wrap,
                },
            });
        if (!string.IsNullOrEmpty(image.Title))
        {
            panel.Children.Add(new TextBlock
            {
                Text = image.Title,
                FontStyle = FontStyle.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }
        return panel;
    }

    /// <summary>A plain number of pixels (<c>300</c>, <c>300px</c>); null for percentages and other units.</summary>
    private static double? PixelSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value!.Trim();
        if (text.EndsWith("px", StringComparison.OrdinalIgnoreCase)) text = text.Substring(0, text.Length - 2);
        return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var size) && size > 0
            ? size
            : null;
    }

    /// <summary>Widest decoded image, in pixels: wider files are scaled down to it once, after decoding.</summary>
    internal const int MaxImageWidth = 2048;

    // ponytail: cleared whole when full rather than least-recently-used; enough for one document's images.
    private const int MaxCachedImages = 64;

    /// <summary>Image loads by full path, reused while the file's write time is unchanged.</summary>
    private readonly Dictionary<string, (DateTime Stamp, Task<Bitmap?> Load)> _images = new(StringComparer.Ordinal);

    /// <summary>
    /// Starts (or reuses) decoding a local raster image on a worker thread; null when the target
    /// may not or cannot be loaded. The task's result is null when the file does not decode.
    /// </summary>
    private Task<Bitmap?>? LoadImage(string target)
    {
        var path = ResolveImagePath(target, BaseDirectory);
        if (path is null) return null;
        DateTime stamp;
        try
        {
            stamp = System.IO.File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return null;
        }
        if (_images.TryGetValue(path, out var cached) && cached.Stamp == stamp) return cached.Load;
        if (_images.Count >= MaxCachedImages) _images.Clear();
        var load = Task.Run(() => DecodeImage(path));
        _images[path] = (stamp, load);
        return load;
    }

    private static Bitmap? DecodeImage(string path)
    {
        try
        {
            var bitmap = new Bitmap(path);
            var size = bitmap.PixelSize;
            if (size.Width <= MaxImageWidth) return bitmap;
            using (bitmap)
                return bitmap.CreateScaledBitmap(new PixelSize(MaxImageWidth, Math.Max(1, (int)((long)size.Height * MaxImageWidth / size.Width))));
        }
        catch
        {
            return null; // undecodable: show the alternative text
        }
    }

    /// <summary>
    /// Puts the image into <paramref name="host"/> once decoded (at once when already cached),
    /// or the fallback when it cannot be loaded.
    /// </summary>
    private static void ShowImage(Task<Bitmap?>? load, Decorator host, Func<Bitmap, Control> image, Func<Control> fallback)
    {
        if (load is null)
            host.Child = fallback();
        else if (load.IsCompleted)
            host.Child = load.Result is { } bitmap ? image(bitmap) : fallback();
        else
            load.ContinueWith(
                done => Dispatcher.UIThread.Post(() => host.Child = done.Result is { } bitmap ? image(bitmap) : fallback()),
                TaskScheduler.Default);
    }

    /// <summary>
    /// The local file an image target names, or null. URLs, protocol-relative and UNC paths are
    /// refused (no network access from a preview), as are SVG files (not decodable here).
    /// </summary>
    internal static string? ResolveImagePath(string? target, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var t = target!.Trim();
        if (t.Contains("://") || t.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("\\\\", StringComparison.Ordinal))
            return null;
        if (t.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            string path;
            if (System.IO.Path.IsPathRooted(t)) path = t;
            else if (baseDirectory is null) return null;
            else path = System.IO.Path.Combine(baseDirectory, t);
            var full = System.IO.Path.GetFullPath(path);
            if (full.StartsWith("\\\\", StringComparison.Ordinal) || full.StartsWith("//", StringComparison.Ordinal))
                return null;
            return System.IO.File.Exists(full) ? full : null;
        }
        catch
        {
            return null; // invalid path characters
        }
    }

    private Border RenderQuote(QuoteLayout quote)
    {
        var panel = new StackPanel();
        foreach (var block in quote.Blocks)
        {
            var control = RenderBlock(block);
            if (control != null)
                panel.Children.Add(control);
        }

        var credit = string.Join(", ", new[] { quote.Attribution, quote.Citation }.Where(c => !string.IsNullOrEmpty(c)));
        if (credit.Length > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "\u2014 " + credit,
                FontStyle = FontStyle.Italic,
                Foreground = Theme.QuoteAttributionForeground,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Right,
            });
        }

        return new Border
        {
            BorderBrush = Theme.QuoteBorderBrush,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 4, 0, 4),
            Margin = new Thickness(0, 0, 0, 8),
            Child = panel,
        };
    }

    // ── Thematic break rendering ────────────────────────────────────

    private Border RenderThematicBreak()
    {
        return new Border
        {
            Height = 1,
            Background = Theme.ThematicBreakBrush,
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

    /// <summary>
    /// Renders an inline layout node, stamping it with its
    /// <see cref="SourceRangeProperty"/>. Override <see cref="RenderInlineCore"/>
    /// to handle custom inline kinds.
    /// </summary>
    protected AvInline? RenderInline(InlineLayout inline)
    {
        // Stamp every rendered inline with its source range so a click in the
        // preview can be hit-tested back to a source offset (see E3).
        var rendered = RenderInlineCore(inline);
        if (rendered is not null && !inline.Source.IsNone)
            rendered.SetValue(SourceRangeProperty, inline.Source);
        return rendered;
    }

    /// <summary>
    /// Dispatches an inline layout to its renderer. Override to render custom
    /// inline kinds: handle your own and call <c>base.RenderInlineCore</c> for
    /// the rest.
    /// </summary>
    protected virtual AvInline? RenderInlineCore(InlineLayout inline)
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
                var span = new Span { FontFamily = Theme.MonospaceFont };
                AddInlines(span.Inlines, mono.Children);
                return span;
            }

            case SuperscriptRun superscript:
            {
                var span = new Span
                {
                    FontSize = Theme.SubSuperscriptFontSize,
                    BaselineAlignment = BaselineAlignment.Superscript,
                };
                AddInlines(span.Inlines, superscript.Children);
                return span;
            }

            case SubscriptRun subscript:
            {
                var span = new Span
                {
                    FontSize = Theme.SubSuperscriptFontSize,
                    BaselineAlignment = BaselineAlignment.Subscript,
                };
                AddInlines(span.Inlines, subscript.Children);
                return span;
            }

            case LinkRun link:
            {
                var linkText = new TextBlock
                {
                    Foreground = Theme.LinkBrush,
                    TextDecorations = global::Avalonia.Media.TextDecorations.Underline,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    TextWrapping = TextWrapping.Wrap,
                };
                AddInlines(linkText.Inlines!, link.Children);
                var href = link.Href;
                linkText.PointerPressed += (_, _) => OnLinkClicked(href);
                return new InlineUIContainer { Child = linkText };
            }

            case LineBreakRun:
                return new LineBreak();

            case StrikethroughRun strikethrough:
            {
                var span = new Span { TextDecorations = global::Avalonia.Media.TextDecorations.Strikethrough };
                AddInlines(span.Inlines, strikethrough.Children);
                return span;
            }

            case HighlightRun highlight:
            {
                var span = new Span { Background = Theme.HighlightBackground, Foreground = Theme.HighlightForeground };
                AddInlines(span.Inlines, highlight.Children);
                return span;
            }

            case ImageRun image:
            {
                var fallback = "[image: " + image.Alt + "]";
                if (LoadImage(image.Target) is not { } load)
                    return new Run(fallback);
                var host = new Decorator();
                ShowImage(load, host,
                    bitmap => new Image { Source = bitmap, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly },
                    () => new TextBlock { Text = fallback });
                return new InlineUIContainer { Child = host };
            }

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
                case SuperscriptRun superscript:
                    AppendPlainText(sb, superscript.Children);
                    break;
                case SubscriptRun subscript:
                    AppendPlainText(sb, subscript.Children);
                    break;
                case LinkRun link:
                    AppendPlainText(sb, link.Children);
                    break;
                case LineBreakRun:
                    sb.Append('\n');
                    break;
                case StrikethroughRun strikethrough:
                    AppendPlainText(sb, strikethrough.Children);
                    break;
                case HighlightRun highlight:
                    AppendPlainText(sb, highlight.Children);
                    break;
                case ImageRun image:
                    sb.Append(image.Alt);
                    break;
            }
        }
    }

    /// <summary>
    /// Raises <see cref="LinkClicked"/> and, unless a handler marks it handled,
    /// opens the URL with the OS shell.
    /// </summary>
    private void OnLinkClicked(string url)
    {
        var args = new LinkClickedEventArgs(url);
        LinkClicked?.Invoke(this, args);
        if (!args.Handled)
            OpenUrl(url);
    }

    private static void OpenUrl(string url)
    {
        // Only auto-open links with a safe, well-known scheme. A document-controlled link must not
        // be able to shell-execute a local executable (e.g. link:file:///C:/Windows/System32/calc.exe[])
        // or leak NTLM credentials over SMB (UNC path) on a single click. Other schemes are left to
        // the host, which can subscribe to LinkClicked and decide how to handle them.
        if (!IsSafeAutoOpenUrl(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Silently ignore failures to open URLs
        }
    }

    /// <summary>
    /// True only for absolute URLs whose scheme is safe to hand to the OS shell without
    /// confirmation (<c>http</c>, <c>https</c>, <c>mailto</c>). Relative links, <c>file:</c>,
    /// UNC paths, <c>javascript:</c>, and custom protocols are rejected.
    /// </summary>
    internal static bool IsSafeAutoOpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        return uri.Scheme is "http" or "https" or "mailto";
    }
}
