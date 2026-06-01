using NUnit.Framework;
using AdocNet.Layout;
using AdocNet.Layout.Builders;
using AdocNet.Parser;

namespace AdocNet.Layout.Tests;

[TestFixture]
public class LayoutBuilderTests
{
    private readonly LayoutBuilder _builder = new();

    private DocumentLayout Build(string asciidoc)
    {
        var result = AdocParser.Parse(asciidoc);
        return _builder.Build(result.Document);
    }

    // ── Empty document ──────────────────────────────────────────────

    [Test]
    public void Empty_document_produces_empty_layout()
    {
        var layout = Build("");
        Assert.That(layout.Children, Is.Empty);
    }

    // ── Paragraphs ──────────────────────────────────────────────────

    [Test]
    public void Single_paragraph_produces_ParagraphLayout()
    {
        var layout = Build("Hello world.");
        Assert.That(layout.Children, Has.Count.EqualTo(1));
        Assert.That(layout.Children[0], Is.InstanceOf<ParagraphLayout>());

        var para = (ParagraphLayout)layout.Children[0];
        Assert.That(para.Inlines, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(para.Inlines[0], Is.InstanceOf<TextRun>());
        Assert.That(((TextRun)para.Inlines[0]).Text, Does.Contain("Hello world"));
    }

    [Test]
    public void Paragraph_with_bold_contains_BoldRun()
    {
        var layout = Build("Hello *bold* world.");
        var para = (ParagraphLayout)layout.Children[0];

        Assert.That(para.Inlines.Any(i => i is BoldRun), Is.True);
        var bold = (BoldRun)para.Inlines.First(i => i is BoldRun);
        Assert.That(bold.Children, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(bold.Children[0], Is.InstanceOf<TextRun>());
        Assert.That(((TextRun)bold.Children[0]).Text, Does.Contain("bold"));
    }

    [Test]
    public void Paragraph_with_italic_contains_ItalicRun()
    {
        var layout = Build("Hello _italic_ world.");
        var para = (ParagraphLayout)layout.Children[0];

        Assert.That(para.Inlines.Any(i => i is ItalicRun), Is.True);
        var italic = (ItalicRun)para.Inlines.First(i => i is ItalicRun);
        Assert.That(italic.Children, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(((TextRun)italic.Children[0]).Text, Does.Contain("italic"));
    }

    [Test]
    public void Paragraph_with_monospace_contains_MonoRun()
    {
        var layout = Build("Hello `mono` world.");
        var para = (ParagraphLayout)layout.Children[0];

        Assert.That(para.Inlines.Any(i => i is MonoRun), Is.True);
        var mono = (MonoRun)para.Inlines.First(i => i is MonoRun);
        Assert.That(mono.Children, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(((TextRun)mono.Children[0]).Text, Does.Contain("mono"));
    }

    [Test]
    public void Paragraph_with_link_contains_LinkRun()
    {
        var layout = Build("Visit https://example.com today.");
        var para = (ParagraphLayout)layout.Children[0];

        Assert.That(para.Inlines.Any(i => i is LinkRun), Is.True);
        var link = (LinkRun)para.Inlines.First(i => i is LinkRun);
        Assert.That(link.Href, Does.Contain("example.com"));
    }

    // ── Headings ────────────────────────────────────────────────────

    [Test]
    public void Heading_produces_HeadingLayout_with_correct_level()
    {
        var layout = Build("== My Heading");
        Assert.That(layout.Children, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(layout.Children[0], Is.InstanceOf<HeadingLayout>());

        var heading = (HeadingLayout)layout.Children[0];
        Assert.That(heading.Level, Is.EqualTo(1));
        Assert.That(heading.Inlines, Has.Count.GreaterThanOrEqualTo(1));
    }

    // ── Lists ───────────────────────────────────────────────────────

    [Test]
    public void Unordered_list_produces_ListLayout_ordered_false()
    {
        var layout = Build("* Item one\n* Item two\n* Item three");

        Assert.That(layout.Children.Any(c => c is ListLayout), Is.True);
        var list = (ListLayout)layout.Children.First(c => c is ListLayout);
        Assert.That(list.Ordered, Is.False);
        Assert.That(list.Items, Has.Count.EqualTo(3));

        var firstItem = list.Items[0];
        Assert.That(firstItem.Inlines, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(((TextRun)firstItem.Inlines[0]).Text, Does.Contain("Item one"));
    }

    [Test]
    public void Ordered_list_produces_ListLayout_ordered_true()
    {
        var layout = Build(". First\n. Second");

        Assert.That(layout.Children.Any(c => c is ListLayout), Is.True);
        var list = (ListLayout)layout.Children.First(c => c is ListLayout);
        Assert.That(list.Ordered, Is.True);
        Assert.That(list.Items, Has.Count.EqualTo(2));
    }

    // ── Code blocks ─────────────────────────────────────────────────

    [Test]
    public void Source_block_produces_CodeBlockLayout()
    {
        var layout = Build("[source,csharp]\n----\nint x = 42;\n----");

        Assert.That(layout.Children.Any(c => c is CodeBlockLayout), Is.True);
        var code = (CodeBlockLayout)layout.Children.First(c => c is CodeBlockLayout);
        Assert.That(code.Text, Does.Contain("int x = 42"));
        Assert.That(code.Language, Is.EqualTo("csharp"));
    }

    [Test]
    public void Listing_block_without_language_has_null_language()
    {
        var layout = Build("----\nsome code\n----");

        Assert.That(layout.Children.Any(c => c is CodeBlockLayout), Is.True);
        var code = (CodeBlockLayout)layout.Children.First(c => c is CodeBlockLayout);
        Assert.That(code.Text, Does.Contain("some code"));
        Assert.That(code.Language, Is.Null);
    }

    // ── Admonitions ─────────────────────────────────────────────────

    [Test]
    public void Inline_admonition_produces_AdmonitionLayout()
    {
        var layout = Build("NOTE: Remember this.");

        Assert.That(layout.Children.Any(c => c is AdmonitionLayout), Is.True);
        var admonition = (AdmonitionLayout)layout.Children.First(c => c is AdmonitionLayout);
        Assert.That(admonition.Kind, Is.EqualTo(AdmonitionKind.Note));
        Assert.That(admonition.Blocks, Has.Count.EqualTo(1));
        Assert.That(admonition.Blocks[0], Is.InstanceOf<ParagraphLayout>());
    }

    [Test]
    public void Block_admonition_produces_AdmonitionLayout()
    {
        var layout = Build("[WARNING]\n====\nBe careful.\n====");

        Assert.That(layout.Children.Any(c => c is AdmonitionLayout), Is.True);
        var admonition = (AdmonitionLayout)layout.Children.First(c => c is AdmonitionLayout);
        Assert.That(admonition.Kind, Is.EqualTo(AdmonitionKind.Warning));
        Assert.That(admonition.Blocks, Has.Count.GreaterThanOrEqualTo(1));
    }

    // ── Mixed content ───────────────────────────────────────────────

    [Test]
    public void Mixed_content_produces_correct_sequence()
    {
        var layout = Build("== Introduction\n\nSome text.\n\n* Item A\n* Item B");

        Assert.That(layout.Children, Has.Count.GreaterThanOrEqualTo(3));
        Assert.That(layout.Children[0], Is.InstanceOf<HeadingLayout>());
        Assert.That(layout.Children[1], Is.InstanceOf<ParagraphLayout>());
        Assert.That(layout.Children[2], Is.InstanceOf<ListLayout>());
    }

    // ── Document title ──────────────────────────────────────────────

    [Test]
    public void Document_with_title_preserves_title()
    {
        var layout = Build("= My Document\n\nContent here.");
        Assert.That(layout.Title, Is.EqualTo("My Document"));
    }

    // ── Tables ──────────────────────────────────────────────────────

    [Test]
    public void Table_produces_TableLayout()
    {
        var layout = Build("|===\n| A | B\n| C | D\n|===");

        Assert.That(layout.Children.Any(c => c is TableLayout), Is.True);
        var table = (TableLayout)layout.Children.First(c => c is TableLayout);
        Assert.That(table.Rows, Has.Count.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Table_with_header_marks_first_row()
    {
        var layout = Build("[options=\"header\"]\n|===\n| H1 | H2\n| A | B\n|===");

        var table = (TableLayout)layout.Children.First(c => c is TableLayout);
        Assert.That(table.HasHeader, Is.True);
        Assert.That(table.Rows[0].Cells[0].IsHeader, Is.True);
    }

    // ── Description lists ───────────────────────────────────────────

    [Test]
    public void Description_list_produces_DescriptionListLayout()
    {
        var layout = Build("Term A:: Description A\nTerm B:: Description B");

        Assert.That(layout.Children.Any(c => c is DescriptionListLayout), Is.True);
        var descList = (DescriptionListLayout)layout.Children.First(c => c is DescriptionListLayout);
        Assert.That(descList.Items, Has.Count.EqualTo(2));
    }

    [Test]
    public void Description_item_has_term_and_description()
    {
        var layout = Build("Foo:: Bar baz");

        var descList = (DescriptionListLayout)layout.Children.First(c => c is DescriptionListLayout);
        var item = descList.Items[0];
        Assert.That(item.Term, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(item.Description, Has.Count.GreaterThanOrEqualTo(1));
    }

    // ── Cross-references ────────────────────────────────────────────

    [Test]
    public void Cross_reference_renders_as_text()
    {
        var layout = Build("See <<my-target,My Label>>.");

        var para = (ParagraphLayout)layout.Children.First(c => c is ParagraphLayout);
        Assert.That(para.Inlines.Any(i => i is TextRun t && t.Text.Contains("My Label")), Is.True);
    }

    // ── Footnotes ───────────────────────────────────────────────────

    [Test]
    public void Footnote_renders_as_bracketed_text()
    {
        var layout = Build("Some text.footnote:[This is a footnote.]");

        var para = (ParagraphLayout)layout.Children.First(c => c is ParagraphLayout);
        Assert.That(para.Inlines.Any(i => i is TextRun t && t.Text.Contains("This is a footnote")), Is.True);
    }

    // ── Passthrough ─────────────────────────────────────────────────

    [Test]
    public void Passthrough_renders_content_as_text()
    {
        var layout = Build("Hello pass:[<b>world</b>] end.");

        var para = (ParagraphLayout)layout.Children.First(c => c is ParagraphLayout);
        Assert.That(para.Inlines.Any(i => i is TextRun t && t.Text.Contains("<b>world</b>")), Is.True);
    }

    // ── Source positions (issue #19) ────────────────────────────────

    [Test]
    public void Block_layouts_carry_source_lines_from_originating_ast_nodes()
    {
        // The source has well-known line numbers for each block; check that
        // the matching layout block exposes a non-empty source range that
        // starts on the expected line.
        var source = """
            = Title

            == Section one

            First paragraph.

            == Section two

            Second paragraph.

            |===
            | Cell
            |===

            ----
            code
            ----
            """;
        var layout = Build(source);

        // Locate blocks by kind in document order.
        var headings = layout.Children.OfType<HeadingLayout>().ToList();
        var paragraphs = layout.Children.OfType<ParagraphLayout>().ToList();
        var tables = layout.Children.OfType<TableLayout>().ToList();
        var codeBlocks = layout.Children.OfType<CodeBlockLayout>().ToList();

        Assert.That(headings, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(paragraphs, Has.Count.GreaterThanOrEqualTo(2));
        Assert.That(tables, Has.Count.EqualTo(1));
        Assert.That(codeBlocks, Has.Count.EqualTo(1));

        // Every emitted block should carry a non-empty source range.
        foreach (var b in headings)
            Assert.That(b.Source.IsNone, Is.False, $"HeadingLayout at level {b.Level} should have source set");
        foreach (var b in paragraphs)
            Assert.That(b.Source.IsNone, Is.False, "ParagraphLayout should have source set");
        Assert.That(tables[0].Source.IsNone, Is.False, "TableLayout should have source set");
        Assert.That(codeBlocks[0].Source.IsNone, Is.False, "CodeBlockLayout should have source set");

        // Sanity: blocks appear in document order, source line monotone non-decreasing.
        int prevLine = 0;
        foreach (var child in layout.Children)
        {
            if (child.Source.IsNone) continue;
            int line = child.Source.Start.Line;
            Assert.That(line, Is.GreaterThanOrEqualTo(prevLine),
                $"Layout child at line {line} must not precede prior child at line {prevLine}");
            prevLine = line;
        }
    }

    [Test]
    public void BlockLayout_Source_defaults_to_None_when_built_directly()
    {
        // Layouts constructed without going through LayoutBuilder (e.g. by
        // tests or third-party code) should have Source = SourceRange.None.
        var p = new ParagraphLayout(System.Array.Empty<InlineLayout>());
        Assert.That(p.Source.IsNone, Is.True);
    }

    [Test]
    public void Inline_layouts_carry_source_ranges_from_originating_ast_nodes()
    {
        // Each rendered inline run must expose a non-None source range so an
        // editor can map a run back to (and hit-test a click into) its source
        // span at inline — not just block — granularity.
        var layout = Build("Hello *bold* and _italic_ world");
        var para = layout.Children.OfType<ParagraphLayout>().First();

        Assert.That(para.Inlines, Is.Not.Empty);
        foreach (var inline in para.Inlines)
            Assert.That(inline.Source.IsNone, Is.False,
                $"{inline.GetType().Name} should carry a source range");

        // The bold run, and its nested children, must also be mapped.
        var bold = para.Inlines.OfType<BoldRun>().First();
        Assert.That(bold.Source.IsNone, Is.False);
        foreach (var child in bold.Children)
            Assert.That(child.Source.IsNone, Is.False, "nested inline run should carry a source range");
    }

    [Test]
    public void InlineLayout_Source_defaults_to_None_when_built_directly()
    {
        var run = new TextRun("x");
        Assert.That(run.Source.IsNone, Is.True);
    }

    // ── Table column weights (issue #26) ────────────────────────────

    [Test]
    public void TableColumnWeights_caps_outlier_column_at_three_times_median()
    {
        // Issue #26 repro: 8-column table with one cell of long prose.
        // Without the cap, the prose column's raw weight (~150 chars)
        // dwarfs the other columns (~4–15 chars), takes ~half the
        // viewport, and squeezes everything else to one-letter-per-line.
        var source = """
            |===
            | LL Packet | Link Type | Content | Result | FPGA | Writer | Reader | Description

            | EXAMPLE_LONG_IDENTIFIER
            | alpha or beta
            | enabled
            | enabled
            | => X
            | => Y
            | => Z
            | One column holds prose long enough to overflow the available row width, and must wrap across several lines instead of collapsing to one word per line.
            |===
            """;
        var layout = Build(source);
        var table = layout.Children.OfType<TableLayout>().Single();

        int colCount = table.Rows[0].Cells.Sum(c => c.ColSpan);
        var weights = TableColumnWeights.Compute(table, colCount);

        Assert.That(weights, Has.Length.EqualTo(8));

        double max = weights.Max();
        double median = weights.OrderBy(w => w).ElementAt(weights.Length / 2);
        Assert.That(max, Is.LessThanOrEqualTo(median * 3 + 0.01),
            "The longest column's weight must not exceed 3× the median.");

        // The prose column (last column) must not exceed ~⅓ of total
        // weight — otherwise the rest of the table loses too much room.
        double total = weights.Sum();
        double proseShare = weights[7] / total;
        Assert.That(proseShare, Is.LessThan(0.35),
            $"Prose column took {proseShare:P0} of total weight; expected < 35%.");
    }

    [Test]
    public void TableColumnWeights_leaves_uniform_tables_unchanged()
    {
        // A table where every column has similar content should keep its
        // raw weights — the cap only fires for outliers.
        var source = """
            |===
            | aaaa | bbbb | cccc | dddd
            | eeee | ffff | gggg | hhhh
            |===
            """;
        var layout = Build(source);
        var table = layout.Children.OfType<TableLayout>().Single();
        int colCount = table.Rows[0].Cells.Sum(c => c.ColSpan);

        var weights = TableColumnWeights.Compute(table, colCount);

        // Every cell is 4 chars long; median is 4 → cap is 12. No weight
        // exceeds the cap, so all four weights stay at the raw value of 4
        // and the table renders with equal column shares.
        Assert.That(weights, Is.EquivalentTo(new[] { 4.0, 4.0, 4.0, 4.0 }));
    }

    [Test]
    public void TableColumnWeights_empty_table_returns_empty()
    {
        // Pathological: zero columns → empty result.
        var table = new TableLayout(title: null, hasHeader: false, hasFooter: false,
            rows: System.Array.Empty<TableRowLayout>());
        var weights = TableColumnWeights.Compute(table, columnCount: 0);
        Assert.That(weights, Is.Empty);
    }

    [Test]
    public void TableColumnWeights_caps_at_one_when_median_is_zero()
    {
        // All-zero raw weights (empty cells) must not produce Star(0) —
        // weights are floored at 1 so the Grid sizes columns equally.
        var raw = new double[] { 0, 0, 0, 0 };
        var capped = TableColumnWeights.CapAtMedianMultiple(raw, multiple: 3);
        foreach (var w in capped)
            Assert.That(w, Is.EqualTo(1));
    }

    [Test]
    public void TableColumnWeights_tracks_rowspans_when_assigning_to_columns()
    {
        // Issue #26 re-opened: a continuation row whose cells follow a
        // row-spanned cell from the prior row was being placed at the
        // wrong column index. Here the long cell is the 3rd in row 2 of
        // the AST, but it must land in column 3 of the visual grid
        // because column 0 is held by the row-spanned cell from row 1.
        //
        //   row 1:  [A rowspan=2] [B] [C] [D]
        //   row 2:               [E] [F] [LONG]
        //
        // Visual grid columns: 0=A, 1=B/E, 2=C/F, 3=D/LONG.
        // Therefore column 3 must carry the LONG weight, not column 2.
        var source = """
            |===
            | A | B | C | D

            .2+| A
            | B
            | C
            | D

            | E
            | F
            | This is a long prose cell that the algorithm must attribute to column 3 because column 0 is held by the row-spanned cell A.
            |===
            """;
        var layout = Build(source);
        var table = layout.Children.OfType<TableLayout>().Single();
        int colCount = table.Rows.Max(r => r.Cells.Sum(c => c.ColSpan));

        var weights = TableColumnWeights.Compute(table, colCount);

        // Column 3 should have the largest weight — that's where the
        // long prose lands once row-spans are honoured. With the v1.0.4
        // algorithm (no row-span tracking) the long cell was attributed
        // to column 2 instead.
        int widestCol = -1;
        double widest = -1;
        for (int c = 0; c < colCount; c++)
        {
            if (weights[c] > widest) { widest = weights[c]; widestCol = c; }
        }
        Assert.That(widestCol, Is.EqualTo(3),
            $"Long prose cell must be attributed to column 3 (visual grid), not column {widestCol}. Weights: [{string.Join(", ", weights.Select(w => w.ToString("F1")))}]");
    }

    [Test]
    public void TableColumnWeights_ComputeMinWidthsPixels_floors_at_longest_word()
    {
        // Each column's MinWidth in pixels must accommodate its longest
        // single (unbreakable) word — otherwise the cell renders with
        // one letter per line on a narrow star allocation.
        var source = """
            |===
            | Short | Description

            | Identifier_With_Long_Name
            | normal prose with several words
            |===
            """;
        var layout = Build(source);
        var table = layout.Children.OfType<TableLayout>().Single();
        int colCount = 2;

        var minWidths = TableColumnWeights.ComputeMinWidthsPixels(table, colCount);

        Assert.That(minWidths, Has.Length.EqualTo(2));
        // Column 0's longest word is "Identifier_With_Long_Name" (25 chars).
        // 25 × 7.5 + 16 = 203.5
        Assert.That(minWidths[0], Is.GreaterThan(150),
            "Col 0 min must fit 'Identifier_With_Long_Name' (25 chars)");
        // Column 1's longest word is "Description" (11) or "several" (7);
        // "Description" wins: 11 × 7.5 + 16 = 98.5
        Assert.That(minWidths[1], Is.GreaterThan(70).And.LessThan(150),
            "Col 1 min must fit 'Description' but not the whole sentence");
    }

    [Test]
    public void TableColumnWeights_ComputeMinWidthsPixels_assigns_one_char_floor_to_empty_cols()
    {
        // Edge case: a column whose every cell is empty must still get a
        // small minimum so the Grid doesn't collapse it to zero width.
        var source = """
            |===
            | First | | Third

            | a |  | b
            |===
            """;
        var layout = Build(source);
        var table = layout.Children.OfType<TableLayout>().Single();

        var minWidths = TableColumnWeights.ComputeMinWidthsPixels(table, columnCount: 3);
        Assert.That(minWidths[1], Is.GreaterThan(0),
            "Empty column must have a non-zero minimum width");
    }

    // ── Per-row source positions (issue #31) ────────────────────────

    [Test]
    public void TableRowLayout_carries_source_lines_from_originating_row_nodes()
    {
        // Each row's Source must be populated from the originating
        // TableRowNode, and the start lines must advance row-by-row in
        // the source — otherwise sync-scroll consumers can't map editor
        // scroll position to preview row.
        var source = """
            = Doc

            |===
            | H1 | H2

            | a1 | a2
            | b1 | b2
            | c1 | c2
            |===
            """;
        var layout = Build(source);
        var table = layout.Children.OfType<TableLayout>().Single();

        Assert.That(table.Rows, Has.Count.EqualTo(4));
        foreach (var row in table.Rows)
            Assert.That(row.Source.IsNone, Is.False,
                "Every TableRowLayout must have a non-None Source");

        // Each row's source line is strictly later than the previous
        // (there are no rowspans here that would compress multiple rows
        // into one source span).
        int prevLine = 0;
        foreach (var row in table.Rows)
        {
            Assert.That(row.Source.Start.Line, Is.GreaterThan(prevLine),
                $"Row source line {row.Source.Start.Line} must be after prior row at line {prevLine}");
            prevLine = row.Source.Start.Line;
        }
    }

    [Test]
    public void TableCellLayout_carries_source_lines_from_originating_cell_nodes()
    {
        // Cell-level source ranges power hover-to-source and per-cell
        // diagnostics. Each cell must end up tagged with the line it
        // was parsed from.
        var source = """
            |===
            | Cell A
            | Cell B
            | Cell C
            |===
            """;
        var layout = Build(source);
        var table = layout.Children.OfType<TableLayout>().Single();

        // Flatten all cells (this table is 1 column × 3 rows).
        var cells = table.Rows.SelectMany(r => r.Cells).ToList();
        Assert.That(cells, Has.Count.EqualTo(3));
        foreach (var cell in cells)
            Assert.That(cell.Source.IsNone, Is.False, "Every cell must have a non-None Source");

        // Cells should be in source-line order.
        int prev = 0;
        foreach (var cell in cells)
        {
            Assert.That(cell.Source.Start.Line, Is.GreaterThan(prev));
            prev = cell.Source.Start.Line;
        }
    }

    [Test]
    public void TableRowLayout_source_spans_multi_line_cells()
    {
        // When a cell's content spans multiple source lines (because the
        // continuation-joining folded subsequent lines into it), the row's
        // Source range must cover the full span — Start at the first line,
        // End at the last.
        var source = """
            [cols="1,1"]
            |===
            | first cell
              continuation line for first cell
            | second cell

            | other row first
            | other row second
            |===
            """;
        var layout = Build(source);
        var table = layout.Children.OfType<TableLayout>().Single();

        Assert.That(table.Rows.Count, Is.GreaterThanOrEqualTo(1));
        var firstRow = table.Rows[0];
        Assert.That(firstRow.Source.IsNone, Is.False);
        // First cell starts on line 3, but its content runs through line 4
        // (the continuation). Row's End line must be ≥ Start line + 1.
        Assert.That(firstRow.Source.End.Line, Is.GreaterThan(firstRow.Source.Start.Line),
            "Multi-line cell content must extend the row's Source.End past Source.Start");
    }
}
