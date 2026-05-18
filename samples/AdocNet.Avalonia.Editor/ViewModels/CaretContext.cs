using AdocNet.Ast;

namespace AdocNet.Avalonia.Editor.ViewModels;

/// <summary>
/// Resolves the deepest AST node whose <see cref="AstNode.Source"/> range
/// covers a given caret position. Used by the editor to:
/// <list type="bullet">
///   <item><description>show the current AST context in the status bar
///     (e.g. "in §2 / paragraph / strong")</description></item>
///   <item><description>enable/disable toolbar commands based on context
///     (e.g. gray out Bold while the caret is inside a code block)</description></item>
/// </list>
/// </summary>
internal static class CaretContext
{
    /// <summary>
    /// Walks the AST top-down looking for the deepest node whose source
    /// range contains <paramref name="line"/> / <paramref name="column"/>
    /// (both 1-based, matching <see cref="SourcePosition"/> semantics).
    /// Returns null when no node contains the position.
    /// </summary>
    public static AstNode? Resolve(AstNode root, int line, int column)
    {
        var pos = new SourcePosition(line, column);
        AstNode? best = null;
        Walk(root, pos, ref best);
        return best;
    }

    private static void Walk(AstNode node, SourcePosition pos, ref AstNode? best)
    {
        if (!node.Source.IsNone && node.Source.Contains(pos))
            best = node;

        foreach (var child in node.Children)
            Walk(child, pos, ref best);

        // Side-channel inlines (paragraph.Inlines, section.TitleInlines, etc.)
        // also carry SourceRanges since Phase 2. Walk them too so the resolver
        // can land on a Strong/Em/Mono/Highlight node when the caret is
        // inside the inline's marked region.
        foreach (var inline in EnumerateInlines(node))
            Walk(inline, pos, ref best);
    }

    private static IEnumerable<InlineNode> EnumerateInlines(AstNode node) => node switch
    {
        ParagraphNode p           => p.Inlines,
        SectionNode s             => s.TitleInlines,
        ListItemNode li           => li.Inlines,
        AdmonitionNode a          => a.Inlines,
        TableCellNode tc          => tc.Inlines,
        FootnoteInlineNode fn     => fn.Inlines,
        StrongInlineNode sn       => sn.Children,
        EmphasisInlineNode en     => en.Children,
        MonospaceInlineNode mn    => mn.Children,
        HighlightInlineNode hn    => hn.Children,
        _                         => Array.Empty<InlineNode>(),
    };

    /// <summary>
    /// Renders a short human-readable label describing the AST context at
    /// the caret. Used by the status bar.
    /// </summary>
    public static string Describe(AstNode? node) => node switch
    {
        null                     => string.Empty,
        DocumentNode             => "document",
        SectionNode sn           => $"§{sn.Level} {Trim(sn.Title, 24)}",
        ParagraphNode            => "paragraph",
        ListNode                 => "list",
        ListItemNode             => "list item",
        DelimitedBlockNode db    => $"{db.BlockKind.ToString().ToLowerInvariant()} block",
        AdmonitionNode a         => $"{a.AdmonitionType.ToLowerInvariant()} admonition",
        TableNode                => "table",
        TableCellNode            => "table cell",
        StrongInlineNode         => "strong",
        EmphasisInlineNode       => "emphasis",
        MonospaceInlineNode      => "monospace",
        HighlightInlineNode      => "highlight",
        LinkInlineNode           => "link",
        InlineLinkMacroNode      => "link",
        InlineImageNode          => "image",
        FootnoteInlineNode       => "footnote",
        CrossReferenceInlineNode => "xref",
        PassthroughInlineNode    => "passthrough",
        _                        => node.Kind.ToString().ToLowerInvariant(),
    };

    private static string Trim(string s, int max)
        => s.Length <= max ? s : s[..max].TrimEnd() + "…";
}
