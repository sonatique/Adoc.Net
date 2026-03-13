using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class ListParserTests
{
    // ── Single-item lists ───────────────────────────────────────────────────────

    [Test]
    public void Single_unordered_item()
    {
        var result = BlockParser.Parse("* Alpha");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var list = (ListNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(list.ListKind, Is.EqualTo(ListKind.Unordered));
            Assert.That(list.Children, Has.Count.EqualTo(1));
            Assert.That(((ListItemNode)list.Children[0]).Text, Is.EqualTo("Alpha"));
        });
    }

    [Test]
    public void Single_ordered_item()
    {
        var result = BlockParser.Parse(". First");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var list = (ListNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(list.ListKind, Is.EqualTo(ListKind.Ordered));
            Assert.That(list.Children, Has.Count.EqualTo(1));
            Assert.That(((ListItemNode)list.Children[0]).Text, Is.EqualTo("First"));
        });
    }

    // ── Multiple items ──────────────────────────────────────────────────────────

    [Test]
    public void Unordered_list_multiple_items_share_one_list_node()
    {
        var result = BlockParser.Parse("* Alpha\n* Beta\n* Gamma");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var list = (ListNode)result.Document.Children[0];
        Assert.That(list.Children, Has.Count.EqualTo(3));
        Assert.That(((ListItemNode)list.Children[0]).Text, Is.EqualTo("Alpha"));
        Assert.That(((ListItemNode)list.Children[1]).Text, Is.EqualTo("Beta"));
        Assert.That(((ListItemNode)list.Children[2]).Text, Is.EqualTo("Gamma"));
    }

    [Test]
    public void Ordered_list_multiple_items_share_one_list_node()
    {
        var result = BlockParser.Parse(". First\n. Second\n. Third");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var list = (ListNode)result.Document.Children[0];
        Assert.That(list.ListKind, Is.EqualTo(ListKind.Ordered));
        Assert.That(list.Children, Has.Count.EqualTo(3));
        Assert.That(((ListItemNode)list.Children[2]).Text, Is.EqualTo("Third"));
    }

    // ── Nesting ─────────────────────────────────────────────────────────────────

    [Test]
    public void Nested_unordered_list()
    {
        var result = BlockParser.Parse("* Outer\n** Inner");

        var outerList = (ListNode)result.Document.Children[0];
        Assert.That(outerList.ListKind, Is.EqualTo(ListKind.Unordered));
        Assert.That(outerList.Children, Has.Count.EqualTo(1));

        var outerItem = (ListItemNode)outerList.Children[0];
        Assert.That(outerItem.Text, Is.EqualTo("Outer"));
        Assert.That(outerItem.Children, Has.Count.EqualTo(1));

        var innerList = (ListNode)outerItem.Children[0];
        Assert.That(innerList.ListKind, Is.EqualTo(ListKind.Unordered));
        Assert.That(((ListItemNode)innerList.Children[0]).Text, Is.EqualTo("Inner"));
    }

    [Test]
    public void Nested_ordered_list()
    {
        var result = BlockParser.Parse(". First\n.. Sub");

        var outerList = (ListNode)result.Document.Children[0];
        Assert.That(outerList.ListKind, Is.EqualTo(ListKind.Ordered));

        var outerItem = (ListItemNode)outerList.Children[0];
        Assert.That(outerItem.Text, Is.EqualTo("First"));

        var innerList = (ListNode)outerItem.Children[0];
        Assert.That(innerList.ListKind, Is.EqualTo(ListKind.Ordered));
        Assert.That(((ListItemNode)innerList.Children[0]).Text, Is.EqualTo("Sub"));
    }

    [Test]
    public void Nested_item_can_return_to_outer_list()
    {
        // Alpha, then nested Nested, then back to Beta at depth 1
        var result = BlockParser.Parse("* Alpha\n** Nested\n* Beta");

        var outerList = (ListNode)result.Document.Children[0];
        Assert.That(outerList.Children, Has.Count.EqualTo(2));

        var alpha = (ListItemNode)outerList.Children[0];
        Assert.That(alpha.Text, Is.EqualTo("Alpha"));
        Assert.That(alpha.Children, Has.Count.EqualTo(1)); // has nested list

        var beta = (ListItemNode)outerList.Children[1];
        Assert.That(beta.Text, Is.EqualTo("Beta"));
        Assert.That(beta.Children, Is.Empty);
    }

    // ── Mixed kinds ──────────────────────────────────────────────────────────────

    [Test]
    public void Mixed_ordered_and_unordered_lists_nest_ordered_inside_unordered()
    {
        var result = BlockParser.Parse("* Alpha\n. One");

        // Without a blank line, a different list kind nests inside the last item
        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var ul = (ListNode)result.Document.Children[0];
        Assert.That(ul.ListKind, Is.EqualTo(ListKind.Unordered));
        var item = (ListItemNode)ul.Children[0];
        Assert.That(item.Text, Is.EqualTo("Alpha"));
        var nestedOl = (ListNode)item.Children[^1];
        Assert.That(nestedOl.ListKind, Is.EqualTo(ListKind.Ordered));
        Assert.That(((ListItemNode)nestedOl.Children[0]).Text, Is.EqualTo("One"));
    }

    // ── Lists inside sections ────────────────────────────────────────────────────

    [Test]
    public void List_inside_a_section()
    {
        var input = "= Doc\n\n== Section\n\n* item";
        var result = BlockParser.Parse(input);

        Assert.That(result.Document.Title, Is.EqualTo("Doc"));
        var section = (SectionNode)result.Document.Children[0];
        Assert.That(section.Children, Has.Count.EqualTo(1));

        var list = (ListNode)section.Children[0];
        Assert.That(list.ListKind, Is.EqualTo(ListKind.Unordered));
        Assert.That(((ListItemNode)list.Children[0]).Text, Is.EqualTo("item"));
    }

    // ── Lists and paragraphs ─────────────────────────────────────────────────────

    [Test]
    public void Paragraph_before_and_after_list()
    {
        var input = "Before.\n\n* item\n\nAfter.";
        var result = BlockParser.Parse(input);

        Assert.That(result.Document.Children, Has.Count.EqualTo(3));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
        Assert.That(result.Document.Children[1], Is.InstanceOf<ListNode>());
        Assert.That(result.Document.Children[2], Is.InstanceOf<ParagraphNode>());

        Assert.That(((ParagraphNode)result.Document.Children[0]).Text, Is.EqualTo("Before."));
        Assert.That(((ParagraphNode)result.Document.Children[2]).Text, Is.EqualTo("After."));
    }

    // ── Malformed / edge cases ───────────────────────────────────────────────────

    [Test]
    public void Lines_missing_space_after_marker_are_paragraphs_not_list_items()
    {
        // *no-space and .no-space should both become paragraph text
        var result = BlockParser.Parse("*no-space\n.also-not-a-list");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
        Assert.That(((ParagraphNode)result.Document.Children[0]).Text,
            Is.EqualTo("*no-space\n.also-not-a-list"));
    }

    [Test]
    public void Bare_markers_with_no_text_are_not_list_items()
    {
        // "*" alone (no space) must not crash and must not produce a list
        var result = BlockParser.Parse("*\n.");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        Assert.That(result.Document.Children[0], Is.InstanceOf<ParagraphNode>());
    }

    [Test]
    public void Empty_list_item_text_is_accepted()
    {
        // "* " — marker + space, nothing after
        var result = BlockParser.Parse("* ");

        Assert.That(result.Document.Children, Has.Count.EqualTo(1));
        var list = (ListNode)result.Document.Children[0];
        Assert.That(((ListItemNode)list.Children[0]).Text, Is.EqualTo(string.Empty));
    }

    // ── Ordered list start and style ──────────────────────────────────────────────

    [Test]
    public void Ordered_list_with_start_attribute()
    {
        var doc = BlockParser.Parse("[start=5]\n. First\n. Second");
        var list = doc.Document.Children.OfType<ListNode>().First();
        Assert.That(list.Start, Is.EqualTo(5));
    }

    [Test]
    public void Ordered_list_with_loweralpha_style()
    {
        var doc = BlockParser.Parse("[loweralpha]\n. Alpha\n. Beta");
        var list = doc.Document.Children.OfType<ListNode>().First();
        Assert.That(list.ListStyle, Is.EqualTo("loweralpha"));
    }

    // ── Source ranges ────────────────────────────────────────────────────────────

    [Test]
    public void List_node_source_range_spans_first_to_last_direct_item()
    {
        var result = BlockParser.Parse("* Alpha\n* Beta");

        var list = (ListNode)result.Document.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(list.Source.Start.Line, Is.EqualTo(1));
            Assert.That(list.Source.End.Line, Is.EqualTo(2));
        });
    }

    [Test]
    public void List_item_source_range_is_set_to_its_line()
    {
        var result = BlockParser.Parse("* Alpha\n* Beta");

        var list = (ListNode)result.Document.Children[0];
        var item0 = (ListItemNode)list.Children[0];
        var item1 = (ListItemNode)list.Children[1];
        Assert.Multiple(() =>
        {
            Assert.That(item0.Source.Start.Line, Is.EqualTo(1));
            Assert.That(item0.Source.End.Line, Is.EqualTo(1));
            Assert.That(item1.Source.Start.Line, Is.EqualTo(2));
            Assert.That(item1.Source.End.Line, Is.EqualTo(2));
        });
    }
}
