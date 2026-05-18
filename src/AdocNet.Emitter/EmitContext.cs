using System.Text;
using AdocNet.Ast;

namespace AdocNet.Emitter;

/// <summary>
/// Internal state passed through the emit dispatch: the writer being built up,
/// the active options, and the lazy source-offset table that powers the
/// source-anchored fast path.
/// </summary>
internal sealed class EmitContext
{
    private SourceOffsetTable? _sourceTable;

    public EmitContext(StringBuilder output, EmitOptions options)
    {
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public StringBuilder Output { get; }

    public EmitOptions Options { get; }

    /// <summary>
    /// Lazy: built only when the source-anchored fast path is enabled AND
    /// at least one node actually has a populated <see cref="SourceRange"/>.
    /// </summary>
    public SourceOffsetTable? SourceTable
    {
        get
        {
            if (_sourceTable is not null) return _sourceTable;
            if (!Options.PreserveOriginalWhenAvailable) return null;
            if (Options.OriginalSource is null) return null;
            _sourceTable = new SourceOffsetTable(Options.OriginalSource);
            return _sourceTable;
        }
    }

    /// <summary>
    /// Attempts the source-anchored fast path: if the source-offset table is
    /// available, the node has a non-empty <see cref="AstNode.Source"/>, and
    /// the slice is non-null, appends the original source slice to the output
    /// verbatim and returns true.
    /// </summary>
    public bool TryEmitOriginal(AstNode node)
    {
        if (node is null) return false;
        var table = SourceTable;
        if (table is null) return false;
        if (node.Source.IsNone) return false;

        var slice = table.Slice(node.Source);
        if (slice is null) return false;

        Output.Append(slice);
        return true;
    }
}
