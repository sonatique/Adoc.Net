# AdocNet Layout Model Design

> Phase P02 — design document, no code.
> Reference: `docs/CONTEXT-UI.md` for AST type names.

---

## 1. Design Principles

- **Pure data**: All layout types are simple POCOs — no behavior, no methods beyond constructors.
- **UI-agnostic**: Zero references to Avalonia, System.Drawing, or any UI framework.
- **No styling**: No font size, color, margin, padding, or visual properties.
- **No positioning**: No x, y, width, height. Vertical flow is implicit.
- **Tree structure**: Every node has exactly one parent (except `DocumentLayout`).
- **netstandard2.0 compatible**: No `Span<T>`, no `init`-only setters (use `get; set;` or constructor params).
- **Sealed classes**: All concrete types are sealed, matching AST conventions.
- **Immutable collections**: `IReadOnlyList<T>` for all child/inline collections.

---

## 2. Namespace

All types live in `AdocNet.Layout` namespace.

---

## 3. Type Definitions

### 3.1 Root

#### `DocumentLayout`

The root of the layout tree. Not a `BlockLayout` (mirrors how `DocumentNode` is not a `BlockNode`).

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| Title | `string?` | No | Document title |
| Children | `IReadOnlyList<BlockLayout>` | Yes | Top-level blocks |

**Constructor**: `DocumentLayout(string? title, IReadOnlyList<BlockLayout> children)`

---

### 3.2 Abstract Base

#### `BlockLayout` (abstract)

Base class for all block-level layout nodes.

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| *(none)* | | | Marker base class only |

No properties on the base. Concrete subclasses add their own.

#### `InlineLayout` (abstract)

Base class for all inline-level layout nodes.

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| *(none)* | | | Marker base class only |

---

### 3.3 Block Types

#### `ParagraphLayout` : `BlockLayout`

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| Inlines | `IReadOnlyList<InlineLayout>` | Yes | Inline content |

**Constructor**: `ParagraphLayout(IReadOnlyList<InlineLayout> inlines)`

#### `HeadingLayout` : `BlockLayout`

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| Level | `int` | Yes | 1–6, maps from `SectionNode.Level` |
| Inlines | `IReadOnlyList<InlineLayout>` | Yes | Title inline content |

**Constructor**: `HeadingLayout(int level, IReadOnlyList<InlineLayout> inlines)`

#### `ListLayout` : `BlockLayout`

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| Ordered | `bool` | Yes | `true` = ordered, `false` = unordered |
| Items | `IReadOnlyList<ListItemLayout>` | Yes | List items |

**Constructor**: `ListLayout(bool ordered, IReadOnlyList<ListItemLayout> items)`

#### `ListItemLayout` : `BlockLayout`

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| Inlines | `IReadOnlyList<InlineLayout>` | Yes | Item text content |
| Blocks | `IReadOnlyList<BlockLayout>` | Yes | Nested blocks (e.g. nested lists). Empty if none. |

**Constructor**: `ListItemLayout(IReadOnlyList<InlineLayout> inlines, IReadOnlyList<BlockLayout> blocks)`

#### `CodeBlockLayout` : `BlockLayout`

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| Text | `string` | Yes | Raw code text |
| Language | `string?` | No | Source language identifier (e.g. `"csharp"`) |

**Constructor**: `CodeBlockLayout(string text, string? language)`

#### `AdmonitionLayout` : `BlockLayout`

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| Kind | `AdmonitionKind` | Yes | Enum value |
| Blocks | `IReadOnlyList<BlockLayout>` | Yes | Body content as blocks |

**Constructor**: `AdmonitionLayout(AdmonitionKind kind, IReadOnlyList<BlockLayout> blocks)`

---

### 3.4 Inline Types

#### `TextRun` : `InlineLayout`

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| Text | `string` | Yes | Plain text content |

**Constructor**: `TextRun(string text)`

#### `BoldRun` : `InlineLayout`

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| Children | `IReadOnlyList<InlineLayout>` | Yes | Nested inline content |

**Constructor**: `BoldRun(IReadOnlyList<InlineLayout> children)`

#### `ItalicRun` : `InlineLayout`

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| Children | `IReadOnlyList<InlineLayout>` | Yes | Nested inline content |

**Constructor**: `ItalicRun(IReadOnlyList<InlineLayout> children)`

#### `MonoRun` : `InlineLayout`

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| Children | `IReadOnlyList<InlineLayout>` | Yes | Nested inline content |

**Constructor**: `MonoRun(IReadOnlyList<InlineLayout> children)`

#### `LinkRun` : `InlineLayout`

| Property | Type | Required | Notes |
|----------|------|----------|-------|
| Href | `string` | Yes | URL or target |
| Children | `IReadOnlyList<InlineLayout>` | Yes | Display content |

**Constructor**: `LinkRun(string href, IReadOnlyList<InlineLayout> children)`

#### `LineBreakRun` : `InlineLayout`

No properties — marker node for hard line breaks.

**Constructor**: `LineBreakRun()`

---

### 3.5 Enum

#### `AdmonitionKind`

```
Note
Tip
Warning
Important
Caution
```

---

## 4. AST Mapping Table

| Layout Type | AST Source Type(s) | Notes |
|---|---|---|
| `DocumentLayout` | `DocumentNode` | Root node. Title from `DocumentNode.Title`. Children from block-typed children. |
| `ParagraphLayout` | `ParagraphNode` | Inlines from `ParagraphNode.Inlines`. Also used for inline `AdmonitionNode` (when `Text` is set). |
| `HeadingLayout` | `SectionNode` | Level from `SectionNode.Level`. Inlines from `SectionNode.TitleInlines`. Section *body* children become siblings after the heading, not children of it. |
| `ListLayout` | `ListNode` | `Ordered` = `ListNode.ListKind == ListKind.Ordered`. Items from `ListItemNode` children. |
| `ListItemLayout` | `ListItemNode` | Inlines from `ListItemNode.Inlines`. Nested blocks from `ListItemNode.Children` (e.g. nested `ListNode`). |
| `CodeBlockLayout` | `DelimitedBlockNode` (when `BlockKind` is `Literal`, `Listing`, or `Source`) | Text from `DelimitedBlockNode.Content`. Language from `DelimitedBlockNode.Language`. |
| `AdmonitionLayout` | `AdmonitionNode` | Kind mapped from `AdmonitionNode.AdmonitionType` string to `AdmonitionKind` enum. Block admonitions: body from children. Inline admonitions: wrap `Inlines` in a `ParagraphLayout`. |
| `TextRun` | `TextInlineNode` | Text from `TextInlineNode.Value`. |
| `BoldRun` | `StrongInlineNode` | Children from `StrongInlineNode.Children` (the `new` shadowed property). |
| `ItalicRun` | `EmphasisInlineNode` | Children from `EmphasisInlineNode.Children` (the `new` shadowed property). |
| `MonoRun` | `MonospaceInlineNode` | Children from `MonospaceInlineNode.Children` (the `new` shadowed property). |
| `LinkRun` | `LinkInlineNode`, `InlineLinkMacroNode` | `LinkInlineNode`: Href = `Url`, display = URL text. `InlineLinkMacroNode`: Href = `Url`, display = `Label`. |
| `LineBreakRun` | *(synthetic)* | Emitted between source lines when `ParagraphNode.HasHardbreaks` is true. |

### AST Types NOT Mapped (deferred to later phases or out of scope)

| AST Type | Reason |
|----------|--------|
| `TableNode`, `TableRowNode`, `TableCellNode` | Deferred — complex table layout |
| `DescriptionListNode`, `DescriptionItemNode` | Deferred — can be added as a block type later |
| `BlockImageNode`, `InlineImageNode` | Deferred — image rendering |
| `VideoNode`, `AudioNode` | Deferred — media rendering |
| `TocNode` | Deferred — TOC rendering |
| `BibliographyEntryNode` | Deferred — niche feature |
| `IndexNode`, `IndexTermNode`, `IndexTermHiddenNode` | Deferred — index rendering |
| `ThematicBreakNode`, `PageBreakNode` | Deferred — simple additions later |
| `CrossReferenceInlineNode`, `InterDocumentXrefNode` | Deferred — internal links |
| `FootnoteInlineNode` | Deferred — footnote rendering |
| `PassthroughInlineNode` | Deferred — passthrough handling |
| `SuperscriptInlineNode`, `SubscriptInlineNode` | Deferred — can be added as inline types later |
| `HighlightInlineNode` | Deferred — highlight rendering |
| `InlineAnchorNode` | Deferred — anchor rendering |
| `InlineMacroNode` | Deferred — macro rendering |
| `DelimitedBlockNode` (Example, Quote, Sidebar, Open, Verse, Passthrough) | Deferred — structural block rendering |

---

## 5. Key Design Decisions

### 5.1 Section flattening

`SectionNode` in the AST is hierarchical — it contains its child blocks *and* subsections as children. The layout model **flattens** this: a `SectionNode` produces a `HeadingLayout` followed by its body blocks at the same level. This is simpler for vertical-flow rendering and avoids deep nesting in the layout tree.

### 5.2 Admonition normalization

`AdmonitionNode` has two modes in the AST: inline (with `Text`/`Inlines`) and block (with `Children`). The layout model normalizes both to `AdmonitionLayout` with a `Blocks` list. For inline admonitions, the builder wraps the inlines in a `ParagraphLayout`.

### 5.3 Constructor-based initialization

All properties are set via constructors, making instances effectively immutable after creation. Properties use `get;` only (set in constructor). This is netstandard2.0 compatible without requiring `init` polyfills.

### 5.4 No `BlockLayout` base properties

Unlike the AST's `BlockNode` which carries `Id`, `Roles`, etc., the layout `BlockLayout` has no properties. IDs and roles are AST-level concerns. The layout model carries only what the renderer needs to produce visual output.

---

## 6. Constraints Checklist

- [x] No styling properties (no font size, color, margin)
- [x] No position/measurement properties (no x, y, width, height)
- [x] No UI-specific types anywhere
- [x] Vertical flow is implicit
- [x] Tree structure (single parent)
- [x] netstandard2.0 compatible (no Span, no init-only)
- [x] Only the types listed in Phase P02 scope
