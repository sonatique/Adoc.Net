using AdocNet;
using AdocNet.Internal.Compatibility;

namespace AdocNet.Ast;

/// <summary>
/// Base class for all AST nodes.
/// </summary>
public abstract class AstNode
{
    private readonly List<AstNode> _children = [];
    private int _structuralHash;
    private bool _structuralHashComputed;

    public SourceRange Source { get; set; }

    public IReadOnlyList<AstNode> Children => _children;

    public abstract AstNodeKind Kind { get; }

    /// <summary>
    /// A deterministic structural hash of this node's kind, properties, and children.
    /// Lazy-computed on first access and cached. Two subtrees with identical structure,
    /// properties, and children produce identical hashes. Uses a simple deterministic
    /// hash (not cryptographic) — AdocNet.Ast has zero external dependencies.
    /// </summary>
    public int StructuralHash
    {
        get
        {
            if (!_structuralHashComputed)
            {
                _structuralHash = ComputeStructuralHash();
                _structuralHashComputed = true;
            }
            return _structuralHash;
        }
    }

    /// <summary>
    /// Clears the cached structural hash, forcing recomputation on next access.
    /// Call after AST mutation (e.g., after extensions modify the tree).
    /// Does not cascade to children — callers should invalidate from the root
    /// to force top-down lazy recomputation.
    /// </summary>
    public void InvalidateStructuralHash()
    {
        _structuralHash = 0;
        _structuralHashComputed = false;
    }

    public void AddChild(AstNode child)
    {
        Guard.NotNull(child);
        _children.Add(child);
    }

    public void InsertChild(int index, AstNode child)
    {
        Guard.NotNull(child);
        _children.Insert(index, child);
    }

    /// <summary>
    /// Returns node-specific properties for pretty-printing.
    /// Override in subclasses that have meaningful properties beyond children.
    /// </summary>
    public virtual IEnumerable<KeyValuePair<string, string>> GetProperties() => [];

    /// <summary>
    /// Returns additional child nodes for structural hashing that are not in
    /// <see cref="Children"/>. Override in node types that store inline content
    /// in separate properties (e.g., ParagraphNode.Inlines, SectionNode.TitleInlines).
    /// </summary>
    protected virtual IEnumerable<AstNode> GetStructuralInlines() => [];

    /// <summary>
    /// Mixes additional state into the structural hash beyond Kind, GetProperties,
    /// and children. Override in subclasses with properties not covered by
    /// GetProperties (e.g., BlockNode.Id, BlockNode.Roles).
    /// </summary>
    protected virtual int MixAdditionalState(int hash) => hash;

    private int ComputeStructuralHash()
    {
        int hash = unchecked((int)2166136261); // FNV-1a 32-bit offset basis

        // 1. Node kind
        hash = FnvMix(hash, (int)Kind);

        // 2. Node-specific properties
        foreach (var kvp in GetProperties())
        {
            hash = FnvMixString(hash, kvp.Key);
            hash = FnvMixString(hash, kvp.Value);
        }

        // 3. Additional state (BlockNode.Id/Roles, inline Roles, etc.)
        hash = MixAdditionalState(hash);

        // 4. Side-channel inline collections
        foreach (var inline in GetStructuralInlines())
            hash = FnvMix(hash, inline.StructuralHash);

        // 5. Children (recursive)
        for (int i = 0; i < _children.Count; i++)
            hash = FnvMix(hash, _children[i].StructuralHash);

        return hash;
    }

    /// <summary>Mixes an integer value into an FNV-1a hash.</summary>
    protected static int FnvMix(int hash, int value)
    {
        unchecked
        {
            hash ^= value & 0xFF;
            hash *= 16777619; // FNV-1a 32-bit prime
            hash ^= (value >> 8) & 0xFF;
            hash *= 16777619;
            hash ^= (value >> 16) & 0xFF;
            hash *= 16777619;
            hash ^= (value >> 24) & 0xFF;
            hash *= 16777619;
            return hash;
        }
    }

    /// <summary>
    /// Mixes a string into an FNV-1a hash using a deterministic per-character approach.
    /// Does not use string.GetHashCode() which is randomized in .NET Core.
    /// </summary>
    protected static int FnvMixString(int hash, string s)
    {
        unchecked
        {
            for (int i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash *= 16777619;
            }
            return hash;
        }
    }
}
