using AdocNet;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class SourceBlockRoleTests
{
    // Regression: the existing standalone case must keep working.
    [Test]
    public void Standalone_source_block_keeps_role()
    {
        var src = "[source,java,role=\"primary\"]\n----\nint x = 1;\n----";
        var doc = BlockParser.Parse(src).Document;

        var block = (DelimitedBlockNode)doc.Children[0];
        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
            Assert.That(block.Language, Is.EqualTo("java"));
            Assert.That(block.Roles, Contains.Item("primary"));
        });
    }

    [Test]
    public void Standalone_source_block_keeps_id()
    {
        var src = "[source,java#snippet1]\n----\nint x = 1;\n----";
        var doc = BlockParser.Parse(src).Document;

        var block = (DelimitedBlockNode)doc.Children[0];
        Assert.That(block.Id, Is.EqualTo("snippet1"));
    }

    // Bug: source block inside a description-list continuation drops role.
    [Test]
    public void Source_block_in_description_list_continuation_keeps_role()
    {
        var src =
            "Java::\n" +
            "+\n" +
            "[source,java,role=\"primary\"]\n" +
            "----\n" +
            "int x = 1;\n" +
            "----\n";
        var doc = BlockParser.Parse(src).Document;

        var dl = (DescriptionListNode)doc.Children[0];
        var item = (DescriptionItemNode)dl.Children[0];
        var block = item.Children.OfType<DelimitedBlockNode>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
            Assert.That(block.Language, Is.EqualTo("java"));
            Assert.That(block.Roles, Contains.Item("primary"));
        });
    }

    [Test]
    public void Source_block_in_description_list_continuation_keeps_id()
    {
        var src =
            "Java::\n" +
            "+\n" +
            "[source,java#snippet1]\n" +
            "----\n" +
            "int x = 1;\n" +
            "----\n";
        var doc = BlockParser.Parse(src).Document;

        var dl = (DescriptionListNode)doc.Children[0];
        var item = (DescriptionItemNode)dl.Children[0];
        var block = item.Children.OfType<DelimitedBlockNode>().Single();

        Assert.That(block.Id, Is.EqualTo("snippet1"));
    }

    [Test]
    public void Source_block_in_unordered_list_continuation_keeps_role()
    {
        var src =
            "* item one\n" +
            "+\n" +
            "[source,java,role=\"primary\"]\n" +
            "----\n" +
            "int x = 1;\n" +
            "----\n";
        var doc = BlockParser.Parse(src).Document;

        var list = (ListNode)doc.Children[0];
        var item = (ListItemNode)list.Children[0];
        var block = item.Children.OfType<DelimitedBlockNode>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(block.BlockKind, Is.EqualTo(DelimitedBlockKind.Source));
            Assert.That(block.Language, Is.EqualTo("java"));
            Assert.That(block.Roles, Contains.Item("primary"));
        });
    }

    [Test]
    public void Source_block_in_unordered_list_continuation_keeps_id()
    {
        var src =
            "* item one\n" +
            "+\n" +
            "[source,java#snippet1]\n" +
            "----\n" +
            "int x = 1;\n" +
            "----\n";
        var doc = BlockParser.Parse(src).Document;

        var list = (ListNode)doc.Children[0];
        var item = (ListItemNode)list.Children[0];
        var block = item.Children.OfType<DelimitedBlockNode>().Single();

        Assert.That(block.Id, Is.EqualTo("snippet1"));
    }
}
