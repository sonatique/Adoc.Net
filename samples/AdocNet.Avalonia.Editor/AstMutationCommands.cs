using AdocNet.Ast;
using AdocNet.Emitter;

namespace AdocNet.Avalonia.Editor;

/// <summary>
/// AST-mutation commands for Full WYSIWYG. Unlike the text-only
/// <c>SourceEdit</c> primitives, these commands operate on the
/// in-memory AST: mutate a node's typed properties, emit that node
/// freshly through <see cref="AsciidocEmitter"/>, and splice the
/// result back into the source at the node's <see cref="AstNode.Source"/>
/// range. The rest of the document stays byte-identical because the
/// splice only touches the mutated block's slice.
///
/// <para>This is the architectural promise from Phase 1 finally being
/// cashed: changes that don't have a clean text-splice form (e.g.
/// toggling a role on a block, where the role might require synthesising
/// an attribute line) round-trip through the emitter while the rest of
/// the source survives untouched.</para>
/// </summary>
internal static class AstMutationCommands
{
    private static readonly AsciidocEmitter Emitter = new();

    /// <summary>
    /// Toggles a role (CSS-style class) on a top-level block. Removes
    /// the role when present, adds it when absent. Returns the new
    /// source string, or the original source on no-op.
    /// </summary>
    public static string ToggleBlockRole(string source, DocumentNode document, int blockIndex, string role)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (string.IsNullOrEmpty(role)) throw new ArgumentException("Role must be non-empty.", nameof(role));
        if (blockIndex < 0 || blockIndex >= document.Children.Count) return source;
        if (document.Children[blockIndex] is not BlockNode block) return source;
        if (block.Source.IsNone) return source;

        // Resolve the block's source range, then extend it backward to
        // absorb any `[…]` attribute lines and `.Title` lines sitting
        // immediately above the block. Those lines aren't part of the
        // block's own range but they *are* what the emitter regenerates
        // when block.Roles / block.Id change, so the splice has to
        // overwrite them too.
        var (rangeStart, rangeLength) = SourceRangeOffsets.Resolve(source, block.Source);
        if (rangeLength <= 0) return source;
        int start = ExtendStartOverBlockAttributeLines(source, rangeStart);
        int length = rangeLength + (rangeStart - start);

        // Mutate the role list. Roles is List-ish but exposed as
        // IReadOnlyList<string>; copy + reassign.
        var newRoles = block.Roles.ToList();
        if (newRoles.Contains(role, StringComparer.Ordinal))
            newRoles.RemoveAll(r => string.Equals(r, role, StringComparison.Ordinal));
        else
            newRoles.Add(role);
        block.Roles = newRoles;
        block.InvalidateStructuralHash();

        // Emit ONLY this block. The emitter's BlockAttributesEmitter
        // turns the role list into the `[.role1.role2]\n` attribute
        // line; the body of the block is then emitted from the typed
        // properties (Text / Inlines / etc.). Source-anchored is OFF
        // here — we want fresh synthesis for the mutated node and we
        // splice it into the otherwise-unchanged source by hand.
        var emittedSlice = Emitter.Emit(block).TrimEnd('\n');

        // Splice the emitted slice back into the source.
        return source.Substring(0, start) + emittedSlice + source.Substring(start + length);
    }

    /// <summary>
    /// Walks backwards from <paramref name="blockStartOffset"/> over any
    /// preceding block-attribute lines (<c>[…]</c>, <c>[[…]]</c>) and
    /// block-title lines (<c>.Title</c>). Returns the offset of the
    /// first such line — or the original offset when nothing matches.
    /// Used by mutation splices so an edit to <c>block.Roles</c> or
    /// <c>block.Id</c> overwrites the existing attribute line(s)
    /// instead of leaving them duplicated above the freshly emitted
    /// version.
    /// </summary>
    private static int ExtendStartOverBlockAttributeLines(string source, int blockStartOffset)
    {
        int pos = blockStartOffset;
        while (pos > 0)
        {
            // Position immediately above must end in '\n'.
            if (source[pos - 1] != '\n') break;

            // Find the start of the previous line.
            int prevLineStart = pos - 1;
            while (prevLineStart > 0 && source[prevLineStart - 1] != '\n')
                prevLineStart--;

            int prevLineEnd = pos - 1;
            if (prevLineEnd <= prevLineStart) break;

            char first = source[prevLineStart];
            char last = source[prevLineEnd - 1];

            bool isAttributeLine = first == '[' && last == ']';
            bool isTitleLine = first == '.' && prevLineEnd - prevLineStart > 1;

            if (!isAttributeLine && !isTitleLine) break;
            pos = prevLineStart;
        }
        return pos;
    }

    /// <summary>
    /// Convenience: clones a top-level block by emitting the AST node
    /// fresh and inserting the emitted text after the original. The
    /// original block stays put; a copy appears immediately after.
    /// </summary>
    public static string DuplicateBlock(string source, DocumentNode document, int blockIndex)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (blockIndex < 0 || blockIndex >= document.Children.Count) return source;
        if (document.Children[blockIndex] is not BlockNode block) return source;
        if (block.Source.IsNone) return source;

        var (start, length) = SourceRangeOffsets.Resolve(source, block.Source);
        if (length <= 0) return source;

        var emittedSlice = Emitter.Emit(block).TrimEnd('\n');
        int insertAt = start + length;

        // Insert "\n\n<emitted>" so the new block is separated from the
        // original by a blank line, matching standard AsciiDoc spacing.
        var separator = NeedsLeadingBlankLine(source, insertAt) ? "\n\n" : "\n";
        return source.Substring(0, insertAt) + separator + emittedSlice + source.Substring(insertAt);
    }

    /// <summary>
    /// Promotes a paragraph to a section heading at the given level
    /// (1-based: 1 == "==", 2 == "===" …). Returns the original
    /// source if the target block is not a paragraph.
    /// </summary>
    public static string PromoteToHeading(string source, DocumentNode document, int blockIndex, int level)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (level < 1) throw new ArgumentOutOfRangeException(nameof(level));
        if (blockIndex < 0 || blockIndex >= document.Children.Count) return source;
        if (document.Children[blockIndex] is not BlockNode block) return source;
        if (block.Source.IsNone) return source;
        if (block is not ParagraphNode paragraph) return source;

        var (start, length) = SourceRangeOffsets.Resolve(source, paragraph.Source);
        if (length <= 0) return source;

        // Convert the paragraph into a SectionNode with the same title
        // text + roles. Emit it via the synthesis path — the emitter
        // turns this into "[id] [.role] == Title\n\n" form.
        var heading = new SectionNode
        {
            Level = level,
            Title = paragraph.Text,
            Id = paragraph.Id,
            Roles = paragraph.Roles,
        };

        var emittedSlice = Emitter.Emit(heading).TrimEnd('\n');
        return source.Substring(0, start) + emittedSlice + source.Substring(start + length);
    }

    private static bool NeedsLeadingBlankLine(string source, int offset)
    {
        // If the next chars already start a blank line, no need to add one.
        if (offset >= source.Length) return false;
        if (source[offset] != '\n') return true;
        if (offset + 1 < source.Length && source[offset + 1] == '\n') return false;
        return true;
    }
}
