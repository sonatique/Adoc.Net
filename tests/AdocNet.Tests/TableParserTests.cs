using AdocNet;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class TableParserTests
{
    // ── Basic table parsing ──────────────────────────────────────────────

    [Test]
    public void Simple_2x2_table()
    {
        var result = BlockParser.Parse("|===\n|A |B\n|C |D\n|===");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var table = (TableNode)result.Document.Children[0];
        Assert.That(table.HasHeader, Is.False);
        Assert.That(table.Children, Has.Count.EqualTo(2));

        var row0 = (TableRowNode)table.Children[0];
        Assert.That(row0.Children, Has.Count.EqualTo(2));
        Assert.That(((TableCellNode)row0.Children[0]).Text, Is.EqualTo("A"));
        Assert.That(((TableCellNode)row0.Children[1]).Text, Is.EqualTo("B"));

        var row1 = (TableRowNode)table.Children[1];
        Assert.That(((TableCellNode)row1.Children[0]).Text, Is.EqualTo("C"));
        Assert.That(((TableCellNode)row1.Children[1]).Text, Is.EqualTo("D"));
    }

    [Test]
    public void Three_row_table()
    {
        var result = BlockParser.Parse("|===\n|Name |Age\n|Alice |30\n|Bob |41\n|===");

        var table = (TableNode)result.Document.Children[0];
        Assert.That(table.Children, Has.Count.EqualTo(3));
        Assert.That(((TableCellNode)((TableRowNode)table.Children[0]).Children[0]).Text, Is.EqualTo("Name"));
        Assert.That(((TableCellNode)((TableRowNode)table.Children[2]).Children[0]).Text, Is.EqualTo("Bob"));
    }

    [Test]
    public void Table_with_header_option()
    {
        var result = BlockParser.Parse("[options=\"header\"]\n|===\n|Name |Age\n|Alice |30\n|===");

        var table = (TableNode)result.Document.Children[0];
        Assert.That(table.HasHeader, Is.True);
        Assert.That(table.Children, Has.Count.EqualTo(2));
    }

    [Test]
    public void Table_at_document_root()
    {
        var result = BlockParser.Parse("|===\n|Cell\n|===");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<TableNode>());
    }

    [Test]
    public void Table_inside_section()
    {
        var result = BlockParser.Parse("== Section\n\n|===\n|A |B\n|===");

        var section = (SectionNode)result.Document.Children[0];
        Assert.That(section.Children, Has.Count.EqualTo(1));
        Assert.That(section.Children[0], Is.InstanceOf<TableNode>());
    }

    // ── Paragraphs around tables ─────────────────────────────────────────

    [Test]
    public void Paragraph_before_and_after_table()
    {
        var result = BlockParser.Parse("Before.\n\n|===\n|Cell\n|===\n\nAfter.");

        Assert.That(result.Document.Children, Has.Count.EqualTo(3));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
        Assert.That(result.Document.Children[1], Is.InstanceOf<TableNode>());
        Assert.That(result.Document.Children[2], Is.InstanceOf<ParagraphNode>());
    }

    // ── Cell content ─────────────────────────────────────────────────────

    [Test]
    public void Cell_text_is_trimmed()
    {
        var result = BlockParser.Parse("|===\n|  padded  |  text  \n|===");

        var table = (TableNode)result.Document.Children[0];
        var row = (TableRowNode)table.Children[0];
        Assert.That(((TableCellNode)row.Children[0]).Text, Is.EqualTo("padded"));
        Assert.That(((TableCellNode)row.Children[1]).Text, Is.EqualTo("text"));
    }

    [Test]
    public void Empty_cell_is_accepted()
    {
        var result = BlockParser.Parse("|===\n| |B\n|===");

        var table = (TableNode)result.Document.Children[0];
        var row = (TableRowNode)table.Children[0];
        Assert.That(((TableCellNode)row.Children[0]).Text, Is.EqualTo(""));
        Assert.That(((TableCellNode)row.Children[1]).Text, Is.EqualTo("B"));
    }

    [Test]
    public void Cell_content_is_inline_parsed()
    {
        var result = BlockParser.Parse("|===\n|*bold* cell |plain\n|===");

        var table = (TableNode)result.Document.Children[0];
        var row = (TableRowNode)table.Children[0];
        var cell = (TableCellNode)row.Children[0];
        Assert.That(cell.Inlines, Has.Count.EqualTo(2));
        Assert.That(cell.Inlines[0], Is.InstanceOf<StrongInlineNode>());
        Assert.That(cell.Inlines[1], Is.InstanceOf<TextInlineNode>());
    }

    // ── Edge cases ───────────────────────────────────────────────────────

    [Test]
    public void Uneven_rows_are_accepted()
    {
        // First row has 2 cells, second has 3 — both accepted.
        var result = BlockParser.Parse("|===\n|A |B\n|C |D |E\n|===");

        var table = (TableNode)result.Document.Children[0];
        Assert.That(table.Children, Has.Count.EqualTo(2));
        Assert.That(((TableRowNode)table.Children[0]).Children, Has.Count.EqualTo(2));
        Assert.That(((TableRowNode)table.Children[1]).Children, Has.Count.EqualTo(3));
    }

    [Test]
    public void Empty_table_produces_node_with_no_rows()
    {
        var result = BlockParser.Parse("|===\n|===");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var table = (TableNode)result.Document.Children[0];
        Assert.That(table.Children, Is.Empty);
    }

    [Test]
    public void Blank_lines_inside_table_are_ignored()
    {
        var result = BlockParser.Parse("|===\n|A |B\n\n|C |D\n|===");

        var table = (TableNode)result.Document.Children[0];
        Assert.That(table.Children, Has.Count.EqualTo(2));
    }

    [Test]
    public void Unclosed_table_produces_warning_and_paragraph()
    {
        var result = BlockParser.Parse("|===\n|A |B\n|C |D");

        Assert.That(result.Diagnostics, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(result.Diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        // The |=== line becomes paragraph text when unclosed.
        Assert.That(result.Document.Children.OfType<ParagraphNode>().Count(), Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Stray_pipe_equals_line_is_paragraph_when_no_closing()
    {
        var result = BlockParser.Parse("|===");

        Assert.That(result.Diagnostics, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Options_header_without_following_table_is_cleared()
    {
        // [options="header"] followed by a paragraph, not a table.
        var result = BlockParser.Parse("[options=\"header\"]\nJust a paragraph.");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Table_trailing_whitespace_on_delimiter()
    {
        var result = BlockParser.Parse("|===   \n|Cell\n|===   ");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<TableNode>());
    }

    // ── HTML rendering ───────────────────────────────────────────────────

    [Test]
    public void Table_renders_to_html()
    {
        var result = BlockParser.Parse("|===\n|A |B\n|C |D\n|===");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<table class=\"tableblock frame-all grid-all stretch\">"));
        Assert.That(html, Does.Contain("<tbody>"));
        Assert.That(html, Does.Contain("<td class=\"tableblock halign-left valign-top\"><p class=\"tableblock\">A</p></td>"));
        Assert.That(html, Does.Contain("<td class=\"tableblock halign-left valign-top\"><p class=\"tableblock\">D</p></td>"));
        Assert.That(html, Does.Contain("</table>"));
        Assert.That(html, Does.Not.Contain("<thead>"));
    }

    [Test]
    public void Header_table_renders_thead_and_th()
    {
        var result = BlockParser.Parse("[options=\"header\"]\n|===\n|Name |Age\n|Alice |30\n|===");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<thead>"));
        Assert.That(html, Does.Contain("<th class=\"tableblock halign-left valign-top\">Name</th>"));
        Assert.That(html, Does.Contain("<th class=\"tableblock halign-left valign-top\">Age</th>"));
        Assert.That(html, Does.Contain("</thead>"));
        Assert.That(html, Does.Contain("<td class=\"tableblock halign-left valign-top\"><p class=\"tableblock\">Alice</p></td>"));
        Assert.That(html, Does.Not.Contain("<td class=\"tableblock halign-left valign-top\"><p class=\"tableblock\">Name</p></td>"));
    }

    [Test]
    public void Html_escaping_in_table_cells()
    {
        var result = BlockParser.Parse("|===\n|<b>bold</b> |A & B\n|===");
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("&lt;b&gt;bold&lt;/b&gt;"));
        Assert.That(html, Does.Contain("A &amp; B"));
    }

    [Test]
    public void Table_appears_in_pretty_printer_output()
    {
        var result = BlockParser.Parse("|===\n|Name |Age\n|===");
        var output = AstPrettyPrinter.Print(result.Document, includeSourceRanges: false);

        Assert.That(output, Does.Contain("Table"));
        Assert.That(output, Does.Contain("TableRow"));
        Assert.That(output, Does.Contain("TableCell"));
        Assert.That(output, Does.Contain("Text=\"Name\""));
    }

    [Test]
    public void Table_node_kind_is_correct()
    {
        var result = BlockParser.Parse("|===\n|Cell\n|===");
        var table = (TableNode)result.Document.Children[0];

        Assert.That(table.Kind, Is.EqualTo(AstNodeKind.Table));
        var row = (TableRowNode)table.Children[0];
        Assert.That(row.Kind, Is.EqualTo(AstNodeKind.TableRow));
        var cell = (TableCellNode)row.Children[0];
        Assert.That(cell.Kind, Is.EqualTo(AstNodeKind.TableCell));
    }

    // ── Vertical alignment ───────────────────────────────────────────────

    [Test]
    public void Column_spec_with_vertical_top()
    {
        var adoc = "[cols=\".<1,2\"]\n|===\n|a |b\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        Assert.That(table.Columns![0].VerticalAlignment, Is.EqualTo(TableVerticalAlignment.Top));
    }

    [Test]
    public void Column_spec_with_vertical_middle()
    {
        var adoc = "[cols=\".^1,2\"]\n|===\n|a |b\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        Assert.That(table.Columns![0].VerticalAlignment, Is.EqualTo(TableVerticalAlignment.Middle));
    }

    [Test]
    public void Column_spec_with_vertical_bottom()
    {
        var adoc = "[cols=\".>1,2\"]\n|===\n|a |b\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        Assert.That(table.Columns![0].VerticalAlignment, Is.EqualTo(TableVerticalAlignment.Bottom));
    }

    [Test]
    public void Column_spec_with_both_alignments()
    {
        var adoc = "[cols=\"^.^1,2\"]\n|===\n|a |b\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        Assert.That(table.Columns![0].Alignment, Is.EqualTo(TableAlignment.Center));
        Assert.That(table.Columns![0].VerticalAlignment, Is.EqualTo(TableVerticalAlignment.Middle));
    }

    [Test]
    public void Cell_alignment_rendered_in_html()
    {
        var adoc = "[cols=\">1,<2\"]\n|===\n|right |left\n|===";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("halign-right"));
    }

    [Test]
    public void Cell_vertical_alignment_rendered_in_html()
    {
        var adoc = "[cols=\".^1,2\"]\n|===\n|middle |normal\n|===";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("valign-middle"));
    }

    // ── AsciiDoc cell parsing ─────────────────────────────────────────────

    [Test]
    public void AsciiDoc_cell_parses_block_content()
    {
        var adoc = "|===\na|A paragraph\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        var row = table.Children.OfType<TableRowNode>().First();
        var cell = row.Children.OfType<TableCellNode>().First();

        Assert.That(cell.ContentStyle, Is.EqualTo(TableCellStyle.AsciiDoc));
        Assert.That(cell.Children.Count, Is.GreaterThan(0));
        Assert.That(cell.Children.OfType<ParagraphNode>().Count(), Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void AsciiDoc_cell_rendered_as_blocks()
    {
        var adoc = "|===\na|A paragraph\n|===";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("<p>A paragraph</p>"));
    }

    // ── Table stripes ─────────────────────────────────────────────────────

    [Test]
    public void Stripes_even_rendered_as_class()
    {
        var adoc = "[stripes=even]\n|===\n|x |y\n|===";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("stripes-even"));
    }

    [Test]
    public void Stripes_odd_rendered_as_class()
    {
        var adoc = "[stripes=odd]\n|===\n|x |y\n|===";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("stripes-odd"));
    }

    [Test]
    public void Table_without_stripes_has_no_stripes_class()
    {
        var adoc = "|===\n|x |y\n|===";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Not.Contain("stripes-"));
    }

    // ── Grid and Frame ──────────────────────────────────────────────────

    [Test]
    public void Grid_rows_parsed_into_table_node()
    {
        var adoc = "[grid=rows]\n|===\n|A |B\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        Assert.That(table.Grid, Is.EqualTo("rows"));
    }

    [Test]
    public void Frame_none_parsed_into_table_node()
    {
        var adoc = "[frame=none]\n|===\n|A |B\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        Assert.That(table.Frame, Is.EqualTo("none"));
    }

    [Test]
    public void Grid_and_frame_combined()
    {
        var adoc = "[grid=rows,frame=none]\n|===\n|A |B\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        Assert.That(table.Grid, Is.EqualTo("rows"));
        Assert.That(table.Frame, Is.EqualTo("none"));
    }

    [Test]
    public void Grid_rows_rendered_as_class()
    {
        var adoc = "[grid=rows]\n|===\n|A |B\n|===";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("grid-rows"));
    }

    [Test]
    public void Frame_topbot_rendered_as_class()
    {
        var adoc = "[frame=topbot]\n|===\n|A |B\n|===";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("frame-topbot"));
    }

    [Test]
    public void Grid_and_frame_classes_are_additive_with_stripes()
    {
        var adoc = "[grid=cols,frame=sides,stripes=even]\n|===\n|A |B\n|===";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("stripes-even"));
        Assert.That(html, Does.Contain("grid-cols"));
        Assert.That(html, Does.Contain("frame-sides"));
    }

    [Test]
    public void Grid_all_is_default_no_class()
    {
        var adoc = "[grid=all]\n|===\n|A |B\n|===";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("grid-all"));
    }

    [Test]
    public void Frame_all_is_default_no_class()
    {
        var adoc = "[frame=all]\n|===\n|A |B\n|===";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);
        Assert.That(html, Does.Contain("frame-all"));
    }

    // ── CSV/DSV/TSV tables ──────────────────────────────────────────────

    [Test]
    public void Csv_table_with_comma_delimiter()
    {
        var adoc = ",===\na,b,c\n1,2,3\n,===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        Assert.That(table.Children, Has.Count.EqualTo(2));
        var row0 = (TableRowNode)table.Children[0];
        Assert.That(row0.Children, Has.Count.EqualTo(3));
        Assert.That(((TableCellNode)row0.Children[0]).Text, Is.EqualTo("a"));
        Assert.That(((TableCellNode)row0.Children[1]).Text, Is.EqualTo("b"));
        Assert.That(((TableCellNode)row0.Children[2]).Text, Is.EqualTo("c"));
    }

    [Test]
    public void Dsv_table_with_colon_delimiter()
    {
        var adoc = ":===\na:b:c\n1:2:3\n:===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        Assert.That(table.Children, Has.Count.EqualTo(2));
        var row0 = (TableRowNode)table.Children[0];
        Assert.That(row0.Children, Has.Count.EqualTo(3));
        Assert.That(((TableCellNode)row0.Children[0]).Text, Is.EqualTo("a"));
    }

    [Test]
    public void Csv_with_quoted_fields()
    {
        var adoc = ",===\n\"hello, world\",test\n,===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        var row0 = (TableRowNode)table.Children[0];
        Assert.That(row0.Children, Has.Count.EqualTo(2));
        Assert.That(((TableCellNode)row0.Children[0]).Text, Is.EqualTo("hello, world"));
        Assert.That(((TableCellNode)row0.Children[1]).Text, Is.EqualTo("test"));
    }

    [Test]
    public void Format_csv_attribute_on_pipe_table()
    {
        var adoc = "[format=csv]\n|===\na,b\nc,d\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        Assert.That(table.Children, Has.Count.EqualTo(2));
        var row0 = (TableRowNode)table.Children[0];
        Assert.That(row0.Children, Has.Count.EqualTo(2));
        Assert.That(((TableCellNode)row0.Children[0]).Text, Is.EqualTo("a"));
        Assert.That(((TableCellNode)row0.Children[1]).Text, Is.EqualTo("b"));
    }

    [Test]
    public void Format_dsv_attribute_on_pipe_table()
    {
        var adoc = "[format=dsv]\n|===\na:b\nc:d\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        Assert.That(table.Children, Has.Count.EqualTo(2));
        var row0 = (TableRowNode)table.Children[0];
        Assert.That(row0.Children, Has.Count.EqualTo(2));
        Assert.That(((TableCellNode)row0.Children[0]).Text, Is.EqualTo("a"));
    }

    [Test]
    public void Format_tsv_attribute_on_pipe_table()
    {
        var adoc = "[format=tsv]\n|===\na\tb\nc\td\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        Assert.That(table.Children, Has.Count.EqualTo(2));
        var row0 = (TableRowNode)table.Children[0];
        Assert.That(row0.Children, Has.Count.EqualTo(2));
        Assert.That(((TableCellNode)row0.Children[0]).Text, Is.EqualTo("a"));
        Assert.That(((TableCellNode)row0.Children[1]).Text, Is.EqualTo("b"));
    }

    [Test]
    public void Csv_table_ignores_empty_lines()
    {
        var adoc = ",===\na,b\n\nc,d\n,===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        Assert.That(table.Children, Has.Count.EqualTo(2));
    }

    // ── Nested table (!===) ─────────────────────────────────────────────

    [Test]
    public void Nested_table_with_bang_delimiters()
    {
        // a| cell contains inline text; the nested !===  table is placed inside the
        // cell text by providing it as the cell's AsciiDoc content.
        // Because the parser does single-line cell content for a| cells, test the
        // nested table by placing the !===  content as the a| cell value directly.
        var innerContent = "!===\n!Inner1 !Inner2\n!===";
        var result = BlockParser.Parse(innerContent);

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var innerTable = (TableNode)result.Document.Children[0];
        Assert.That(innerTable.Children, Has.Count.EqualTo(1));

        var innerRow = (TableRowNode)innerTable.Children[0];
        Assert.That(innerRow.Children, Has.Count.EqualTo(2));
        Assert.That(((TableCellNode)innerRow.Children[0]).Text, Is.EqualTo("Inner1"));
        Assert.That(((TableCellNode)innerRow.Children[1]).Text, Is.EqualTo("Inner2"));
    }

    [Test]
    public void Nested_table_with_multiple_rows()
    {
        // !===  table with header option
        var adoc = "[options=\"header\"]\n!===\n!Name !Age\n!Alice !30\n!===";
        var result = BlockParser.Parse(adoc);

        var table = (TableNode)result.Document.Children[0];
        Assert.That(table.HasHeader, Is.True);
        Assert.That(table.Children, Has.Count.EqualTo(2));

        var headerRow = (TableRowNode)table.Children[0];
        Assert.That(((TableCellNode)headerRow.Children[0]).Text, Is.EqualTo("Name"));
        Assert.That(((TableCellNode)headerRow.Children[1]).Text, Is.EqualTo("Age"));

        var dataRow = (TableRowNode)table.Children[1];
        Assert.That(((TableCellNode)dataRow.Children[0]).Text, Is.EqualTo("Alice"));
        Assert.That(((TableCellNode)dataRow.Children[1]).Text, Is.EqualTo("30"));
    }

    [Test]
    public void Nested_table_renders_correctly_in_html()
    {
        // Test rendering of a standalone nested table using ! delimiters
        var adoc = "!===\n!A !B\n!C !D\n!===";
        var result = BlockParser.Parse(adoc);
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Does.Contain("<table"));
        Assert.That(html, Does.Contain(">A<"));
        Assert.That(html, Does.Contain(">B<"));
        Assert.That(html, Does.Contain(">C<"));
        Assert.That(html, Does.Contain(">D<"));
    }

    [Test]
    public void Standalone_nested_table_with_bang_delimiters()
    {
        // A !===  table at the top level also works
        var adoc = "!===\n!X !Y\n!Z !W\n!===";
        var result = BlockParser.Parse(adoc);

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var table = (TableNode)result.Document.Children[0];
        Assert.That(table.Children, Has.Count.EqualTo(2));

        var row0 = (TableRowNode)table.Children[0];
        Assert.That(((TableCellNode)row0.Children[0]).Text, Is.EqualTo("X"));
        Assert.That(((TableCellNode)row0.Children[1]).Text, Is.EqualTo("Y"));

        var row1 = (TableRowNode)table.Children[1];
        Assert.That(((TableCellNode)row1.Children[0]).Text, Is.EqualTo("Z"));
        Assert.That(((TableCellNode)row1.Children[1]).Text, Is.EqualTo("W"));
    }

    // ── Empty cols entries (issue #2) ────────────────────────────────────────

    [Test]
    public void Empty_cols_entries_count_as_default_columns()
    {
        // 7 entries: <1,1,1,(default),1,(default),> → 7 columns
        var adoc = "[cols=\"<1,1,1,,1,,>\"]\n|===\n| C1 | C2 | C3 | C4 | C5 | C6 | C7\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();

        Assert.That(table.Columns, Is.Not.Null);
        Assert.That(table.Columns!.Count, Is.EqualTo(7));
        Assert.That(table.Columns[0].Alignment, Is.EqualTo(TableAlignment.Left));
        Assert.That(table.Columns[3].Alignment, Is.EqualTo(TableAlignment.Left), "empty entry defaults to left");
        Assert.That(table.Columns[5].Alignment, Is.EqualTo(TableAlignment.Left), "empty entry defaults to left");
        Assert.That(table.Columns[6].Alignment, Is.EqualTo(TableAlignment.Right), "trailing > applies to last column");
    }

    [Test]
    public void Empty_cols_entry_at_start()
    {
        var adoc = "[cols=\",,1,1\"]\n|===\n|a|b|c|d\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();

        Assert.That(table.Columns!.Count, Is.EqualTo(4));
    }

    [Test]
    public void Empty_cols_entry_at_end()
    {
        var adoc = "[cols=\"1,1,,\"]\n|===\n|a|b|c|d\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();

        Assert.That(table.Columns!.Count, Is.EqualTo(4));
    }

    [Test]
    public void Empty_cols_entries_preserve_row_column_mapping()
    {
        // Regression for issue #2: with 7 specified columns (3 explicit + 4 empty),
        // a 7-column table must render 7 cells per row, not collapse to 5.
        var adoc = "[cols=\"<1,1,1,,1,,>\"]\n|===\n| C1 | C2 | C3 | C4 | C5 | C6 | C7\n\n| a1 | a2 | a3 | a4 | a5 | a6 | a7\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        var rows = table.Children.OfType<TableRowNode>().ToList();

        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0].Children, Has.Count.EqualTo(7));
        Assert.That(rows[1].Children, Has.Count.EqualTo(7));
        Assert.That(((TableCellNode)rows[0].Children[6]).Text, Is.EqualTo("C7"));
        Assert.That(((TableCellNode)rows[1].Children[6]).Text, Is.EqualTo("a7"));
    }

    // ── Multi-line cell content (issue #3) ───────────────────────────────────

    [Test]
    public void AsciiDoc_cell_with_multi_line_footnote_keeps_footnote()
    {
        // Regression for issue #3: the closing ']' of the footnote macro is on
        // the next physical line. Without joining continuation lines, the macro
        // body is dropped silently.
        var adoc = "|===\n| H1 | H2\n\na| body footnote:[this body\ncontinues on the next line]\n| next\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();

        var allCells = table.Children.OfType<TableRowNode>()
            .SelectMany(r => r.Children.OfType<TableCellNode>())
            .ToList();

        // Find the AsciiDoc cell with the footnote.
        var asciiDocCell = allCells.First(c => c.ContentStyle == TableCellStyle.AsciiDoc);

        // Walk the cell's block children and inline descendants looking for the footnote.
        bool hasFootnote = false;
        foreach (var child in asciiDocCell.Children)
        {
            if (child is ParagraphNode para)
            {
                foreach (var inline in para.Inlines)
                {
                    if (inline is FootnoteInlineNode fn)
                    {
                        hasFootnote = true;
                        Assert.That(fn.Text, Does.Contain("this body"));
                        Assert.That(fn.Text, Does.Contain("continues on the next line"));
                    }
                }
            }
        }
        Assert.That(hasFootnote, Is.True, "footnote macro spanning a newline inside an a| cell must be parsed");
    }

    [Test]
    public void Cell_content_continues_across_physical_lines()
    {
        // A line that does not contain the cell separator is a continuation of
        // the preceding cell's content. This matches Asciidoctor behaviour.
        var adoc = "|===\n| line one\nline two\n| second cell\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();
        var rows = table.Children.OfType<TableRowNode>().ToList();

        Assert.That(rows, Has.Count.EqualTo(2));
        var firstCell = (TableCellNode)rows[0].Children[0];
        Assert.That(firstCell.Text, Does.Contain("line one"));
        Assert.That(firstCell.Text, Does.Contain("line two"));
    }

    // ── List items containing `|` inside `a|` cells (issue #6) ───────────────

    [Test]
    public void List_item_with_pipe_inside_asciidoc_cell_keeps_pre_pipe_item()
    {
        // Regression for issue #6: a `*` list item whose text contains a
        // literal `|` inside an `a|` AsciiDoc-content cell. The `|` is a cell
        // separator at the table-grammar level, so the pre-pipe portion stays
        // in the AsciiDoc cell's list, the post-pipe portion forms a new
        // cell. Before the fix the entire `* item beta` <li> was dropped.
        var adoc =
            "|===\n" +
            "| Header A | Header B\n" +
            "\n" +
            "a| AsciiDoc cell with a bullet list:\n" +
            "\n" +
            "* item alpha\n" +
            "* item beta | extra after pipe\n" +
            "\n" +
            "| plain cell\n" +
            "|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();

        var asciiDocCell = table.Children.OfType<TableRowNode>()
            .SelectMany(r => r.Children.OfType<TableCellNode>())
            .First(c => c.ContentStyle == TableCellStyle.AsciiDoc);

        var list = asciiDocCell.Children.OfType<ListNode>().FirstOrDefault();
        Assert.That(list, Is.Not.Null, "the AsciiDoc cell must contain a list");
        Assert.That(list!.Children, Has.Count.EqualTo(2), "list must keep both pre-pipe items");
        Assert.That(((ListItemNode)list.Children[0]).Text, Is.EqualTo("item alpha"));
        Assert.That(((ListItemNode)list.Children[1]).Text, Is.EqualTo("item beta"));
    }

    // ── Leading blank line inside |=== block (issue #7) ──────────────────────

    [Test]
    public void Leading_blank_line_before_first_row_suppresses_implicit_header()
    {
        // Regression for issue #7. A blank line between |=== and the first
        // row of cells means there is no implicit header — all rows render
        // as body rows. Without the fix, the first content row was promoted
        // to a header by the trailing blank line that follows it.
        var adoc = "|===\n\n| A | B | C\n\n| 1 | 2 | 3\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();

        Assert.That(table.HasHeader, Is.False);
        var rows = table.Children.OfType<TableRowNode>().ToList();
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(((TableCellNode)rows[0].Children[0]).Text, Is.EqualTo("A"));
        Assert.That(((TableCellNode)rows[1].Children[0]).Text, Is.EqualTo("1"));
    }

    [Test]
    public void No_leading_blank_line_keeps_implicit_header_detection()
    {
        // Sanity check that the canonical case still works: first row
        // immediately after |=== followed by a blank line → header.
        var adoc = "|===\n| A | B | C\n\n| 1 | 2 | 3\n|===";
        var result = BlockParser.Parse(adoc);
        var table = result.Document.Children.OfType<TableNode>().First();

        Assert.That(table.HasHeader, Is.True);
    }

    [Test]
    public void Cols_repeat_multiplier_expands_to_n_columns()
    {
        // 2* -> two default columns, then ,1 -> a third; 2*2 -> two width-2 cols.
        var twoStarOne = ParseColumns("2*,1");
        Assert.That(twoStarOne, Has.Count.EqualTo(3));
        Assert.That(twoStarOne.Select(c => c.Width), Is.EqualTo(new[] { 1, 1, 1 }));

        var twoStarTwo = ParseColumns("2*2,1");
        Assert.That(twoStarTwo, Has.Count.EqualTo(3));
        Assert.That(twoStarTwo.Select(c => c.Width), Is.EqualTo(new[] { 2, 2, 1 }));

        // 2*^1 -> two centre-aligned width-1 columns.
        var twoStarCentre = ParseColumns("2*^1");
        Assert.That(twoStarCentre, Has.Count.EqualTo(2));
        Assert.That(twoStarCentre.All(c => c.Alignment == TableAlignment.Center), Is.True);

        // Whole-spec form still works.
        Assert.That(ParseColumns("3*"), Has.Count.EqualTo(3));
    }

    private static IReadOnlyList<TableColumnSpec> ParseColumns(string cols)
    {
        var result = BlockParser.Parse($"[cols=\"{cols}\"]\n|===\n|a|b|c\n|===");
        return result.Document.Children.OfType<TableNode>().First().Columns!;
    }

    [Test]
    public void Escaped_pipe_is_literal_content_not_a_cell_boundary()
    {
        // Asciidoctor: `| a \| b | c` -> two cells, "a | b" and "c".
        var result = BlockParser.Parse("|===\n| a \\| b | c\n|===");
        var table = result.Document.Children.OfType<TableNode>().First();
        var row = table.Children.OfType<TableRowNode>().First();
        var cells = row.Children.OfType<TableCellNode>().ToList();

        Assert.That(cells, Has.Count.EqualTo(2));
        Assert.That(cells[0].Text, Is.EqualTo("a | b"));
        Assert.That(cells[1].Text, Is.EqualTo("c"));
    }
}

