namespace AdocNet.Avalonia.Editor.Tests;

/// <summary>
/// Unit coverage for <see cref="BlockEditController.DecideCommit"/> — the pure
/// decision behind committing an in-place block edit, including the stale-offset
/// guard that protects unrelated text from corruption.
/// </summary>
[TestFixture]
public class BlockEditControllerTests
{
    [Test]
    public void Replaces_when_slice_matches_and_text_changed()
    {
        var src = "alpha\n\nbeta\n\ngamma";
        int start = src.IndexOf("beta", StringComparison.Ordinal);

        var (action, s, len) = BlockEditController.DecideCommit(src, start, 4, "beta", "BETA");

        Assert.That(action, Is.EqualTo(BlockEditController.CommitAction.Replace));
        Assert.That(s, Is.EqualTo(start));
        Assert.That(len, Is.EqualTo(4));
    }

    [Test]
    public void No_change_when_text_unedited()
    {
        var src = "alpha\n\nbeta";
        int start = src.IndexOf("beta", StringComparison.Ordinal);

        var (action, _, _) = BlockEditController.DecideCommit(src, start, 4, "beta", "beta");

        Assert.That(action, Is.EqualTo(BlockEditController.CommitAction.NoChange));
    }

    [Test]
    public void Aborts_on_stale_offsets_rather_than_corrupting_text()
    {
        // The document shifted (text was prepended): the captured offset no
        // longer holds the slice we opened for editing.
        var shifted = "PREFIX alpha\n\nbeta";
        int staleStart = "alpha\n\nbeta".IndexOf("beta", StringComparison.Ordinal);

        var (action, _, _) = BlockEditController.DecideCommit(shifted, staleStart, 4, "beta", "BETA");

        Assert.That(action, Is.EqualTo(BlockEditController.CommitAction.Abort));
    }

    [Test]
    public void Aborts_when_range_is_out_of_bounds()
    {
        var (action, _, _) = BlockEditController.DecideCommit("short", start: 100, length: 4, "x", "y");
        Assert.That(action, Is.EqualTo(BlockEditController.CommitAction.Abort));
    }

    [Test]
    public void Clamps_length_to_document_end_then_replaces()
    {
        var (action, s, len) = BlockEditController.DecideCommit(
            "abcdef", start: 4, length: 10, originalSlice: "ef", newSlice: "EF");

        Assert.That(action, Is.EqualTo(BlockEditController.CommitAction.Replace));
        Assert.That(s, Is.EqualTo(4));
        Assert.That(len, Is.EqualTo(2));
    }
}
