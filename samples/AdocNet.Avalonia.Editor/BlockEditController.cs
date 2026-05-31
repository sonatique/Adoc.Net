using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AdocNet.Ast;
using AdocNet.Avalonia.Editor.ViewModels;

namespace AdocNet.Avalonia.Editor;

/// <summary>
/// Block-WYSIWYG controller for the preview pane.
///
/// <para>Hooks pointer events on every rendered top-level block. When the
/// user clicks a block, the rendered control is replaced in-place with an
/// <see cref="TextEditor"/> prefilled with the block's source slice (read
/// from the source editor at the offsets resolved from the AST node's
/// <see cref="AstNode.Source"/> range). On commit (Enter / focus loss)
/// the edited slice is spliced back into the source editor at the same
/// range, which triggers a normal parse-render cycle in the view-model.
/// On cancel (Escape) the source is left unchanged and a re-render is
/// requested.</para>
///
/// <para>The controller stays decoupled from <see cref="MainWindow"/> by
/// taking the source editor and the view-model as constructor arguments.
/// It re-wires its click handlers each time <see cref="EditorViewModel.Rendered"/>
/// fires because incremental rendering replaces individual children in
/// the panel.</para>
/// </summary>
internal sealed class BlockEditController
{
    private readonly TextEditor _sourceEditor;
    private readonly EditorViewModel _viewModel;
    private readonly ContentControl _previewHost;

    private InPlaceEditState? _active;

    public BlockEditController(TextEditor sourceEditor, EditorViewModel viewModel, ContentControl previewHost)
    {
        _sourceEditor = sourceEditor ?? throw new ArgumentNullException(nameof(sourceEditor));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _previewHost = previewHost ?? throw new ArgumentNullException(nameof(previewHost));
    }

    /// <summary>
    /// Re-attach click handlers on the freshly-rendered preview. Called
    /// from <c>EditorViewModel.Rendered</c>. Idempotent — handlers are
    /// added unconditionally because new controls are produced each
    /// time a Modified section is spliced in.
    /// </summary>
    public void OnRendered(Control preview, DocumentNode document)
    {
        // If an in-place edit was active and the document was re-rendered
        // around it, clear the state — the new render replaces the
        // editor control we were attached to.
        _active = null;

        var panel = ExtractTopPanel(preview);
        if (panel is null) return;

        // Each top-level AST child is rendered as exactly one container
        // tagged with a SectionTag carrying that child's AST index (see
        // IncrementalAvaloniaRenderer). Attach the click handler to those
        // containers and resolve the AST node via the tag's index — never
        // via the raw panel position, which an optional leading document-
        // title block would shift, and which section flattening used to
        // misalign. The SectionTag is left intact so the incremental
        // renderer can still locate containers on the next update.
        foreach (var c in panel.Children)
        {
            if (c is not Control child) continue;
            if (child.Tag is not IncrementalAvaloniaRenderer.SectionTag) continue;

            child.PointerPressed -= OnAnyBlockPointerPressed; // safe even when not previously attached
            child.PointerPressed += OnAnyBlockPointerPressed;
        }

        _currentDocument = document;
    }

    private DocumentNode? _currentDocument;

    private void OnAnyBlockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Right-click is reserved for the context menu (delete block etc.).
        if (e.GetCurrentPoint(null).Properties.IsRightButtonPressed)
        {
            ShowContextMenu(sender as Control, e);
            return;
        }

        // Block-WYSIWYG entry is gated on a double-click. A single click
        // would steal selection / scrolling from the preview, which
        // doesn't match the user's expectation for read-only viewing.
        if (e.ClickCount < 2) return;

        if (sender is not Control control) return;
        if (control.Tag is not IncrementalAvaloniaRenderer.SectionTag tag) return;
        if (_currentDocument is null) return;
        if (tag.Index < 0 || tag.Index >= _currentDocument.Children.Count) return;

        var node = _currentDocument.Children[tag.Index];
        if (node.Source.IsNone) return;

        EnterEditMode(control, node);
        e.Handled = true;
    }

    private void ShowContextMenu(Control? control, PointerPressedEventArgs e)
    {
        if (control is null) return;
        if (control.Tag is not IncrementalAvaloniaRenderer.SectionTag tag) return;
        if (_currentDocument is null) return;
        if (tag.Index < 0 || tag.Index >= _currentDocument.Children.Count) return;

        int astIndex = tag.Index;
        var node = _currentDocument.Children[astIndex];
        var items = new List<global::Avalonia.Controls.Control>
        {
            BuildMenuItem("Edit block (double-click)", () => EnterEditMode(control, node)),
            BuildMenuItem("Duplicate block", () => DuplicateBlock(astIndex)),
            BuildMenuItem("Delete block", () => DeleteBlock(node)),
        };

        // AST-mutation features (Full WYSIWYG): each command mutates the
        // typed AST node, emits it fresh via AsciidocEmitter, and splices
        // the new slice back into the source. The rest of the document
        // stays byte-identical.
        if (node is BlockNode)
        {
            items.Add(new Separator());
            items.Add(BuildMenuItem("Toggle role: [.warning]",   () => ToggleRole(astIndex, "warning")));
            items.Add(BuildMenuItem("Toggle role: [.important]", () => ToggleRole(astIndex, "important")));
            items.Add(BuildMenuItem("Toggle role: [.lead]",      () => ToggleRole(astIndex, "lead")));

            if (node is ParagraphNode)
            {
                items.Add(new Separator());
                items.Add(BuildMenuItem("Promote to heading H1", () => PromoteToHeading(astIndex, 1)));
                items.Add(BuildMenuItem("Promote to heading H2", () => PromoteToHeading(astIndex, 2)));
                items.Add(BuildMenuItem("Promote to heading H3", () => PromoteToHeading(astIndex, 3)));
            }
        }

        var menu = new ContextMenu { ItemsSource = items };
        menu.Open(control);
        e.Handled = true;
    }

    private void ToggleRole(int blockIndex, string role)
    {
        if (_currentDocument is null) return;
        var newSource = AstMutationCommands.ToggleBlockRole(
            _sourceEditor.Text, _currentDocument, blockIndex, role);
        if (!string.Equals(newSource, _sourceEditor.Text, StringComparison.Ordinal))
            _sourceEditor.Text = newSource;
    }

    private void DuplicateBlock(int blockIndex)
    {
        if (_currentDocument is null) return;
        var newSource = AstMutationCommands.DuplicateBlock(
            _sourceEditor.Text, _currentDocument, blockIndex);
        if (!string.Equals(newSource, _sourceEditor.Text, StringComparison.Ordinal))
            _sourceEditor.Text = newSource;
    }

    private void PromoteToHeading(int blockIndex, int level)
    {
        if (_currentDocument is null) return;
        var newSource = AstMutationCommands.PromoteToHeading(
            _sourceEditor.Text, _currentDocument, blockIndex, level);
        if (!string.Equals(newSource, _sourceEditor.Text, StringComparison.Ordinal))
            _sourceEditor.Text = newSource;
    }

    private static MenuItem BuildMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    // ── In-place edit ─────────────────────────────────────────────────────

    private void EnterEditMode(Control blockControl, AstNode node)
    {
        var (start, length) = SourceRangeOffsets.Resolve(_sourceEditor.Text, node.Source);
        if (length <= 0) return;

        // Replace the clicked container directly (located by reference) so
        // the swap is correct regardless of the container's absolute panel
        // position — i.e. independent of a leading document-title block.
        var panel = ExtractTopPanel((Control)_previewHost.Content!);
        if (panel is null) return;
        int index = panel.Children.IndexOf(blockControl);
        if (index < 0) return;

        var slice = _sourceEditor.Text.Substring(start, length);

        var inplace = new TextEditor
        {
            Document = new TextDocument(slice),
            FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace"),
            FontSize = 13,
            ShowLineNumbers = false,
            WordWrap = true,
            Background = Brushes.LightYellow,
            Padding = new Thickness(8),
        };

        panel.Children[index] = inplace;

        _active = new InPlaceEditState
        {
            Editor = inplace,
            SourceStart = start,
            SourceLength = length,
            OriginalSlice = slice,
        };

        inplace.KeyDown += OnInPlaceKeyDown;
        inplace.LostFocus += OnInPlaceLostFocus;
        inplace.Focus();
    }

    private void OnInPlaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (_active is null) return;
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            Commit();
            e.Handled = true;
        }
    }

    private void OnInPlaceLostFocus(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Defer to the next dispatcher tick so the keyboard event that
        // caused focus loss (e.g. Tab) finishes processing first.
        global::Avalonia.Threading.Dispatcher.UIThread.Post(Commit);
    }

    private void Commit()
    {
        if (_active is null) return;
        var newSlice = _active.Editor.Text;
        var start = _active.SourceStart;
        var length = _active.SourceLength;
        var originalSlice = _active.OriginalSlice;

        // Detach handlers before clearing _active so a follow-up event
        // doesn't re-fire on the dying editor.
        _active.Editor.KeyDown -= OnInPlaceKeyDown;
        _active.Editor.LostFocus -= OnInPlaceLostFocus;
        _active = null;

        var (action, s, len) = DecideCommit(_sourceEditor.Text, start, length, originalSlice, newSlice);
        switch (action)
        {
            case CommitAction.Replace:
                _sourceEditor.Document.Replace(s, len, newSlice);
                // The source TextEditor's TextChanged handler tells the VM to
                // re-parse; the incremental renderer splices the freshly
                // rendered block back into the panel.
                break;
            default:
                // No change, or stale offsets — just re-render so the rendered
                // view replaces the in-place editor without touching the source.
                _viewModel.ResetText(_sourceEditor.Text);
                break;
        }
    }

    internal enum CommitAction { Replace, NoChange, Abort }

    /// <summary>
    /// Pure decision for committing an in-place edit. Clamps the range to the
    /// document, then:
    /// <list type="bullet">
    ///   <item><description><see cref="CommitAction.Abort"/> when the range is
    ///     out of bounds, or the current source there no longer equals the slice
    ///     that was opened for editing (the document shifted under us) — splicing
    ///     would corrupt unrelated text.</description></item>
    ///   <item><description><see cref="CommitAction.NoChange"/> when the edited
    ///     text equals the current source.</description></item>
    ///   <item><description><see cref="CommitAction.Replace"/> otherwise, with
    ///     the clamped (start, length) to splice.</description></item>
    /// </list>
    /// </summary>
    internal static (CommitAction Action, int Start, int Length) DecideCommit(
        string sourceText, int start, int length, string originalSlice, string newSlice)
    {
        if (start < 0 || start > sourceText.Length)
            return (CommitAction.Abort, start, length);
        if (start + length > sourceText.Length)
            length = sourceText.Length - start;

        var currentSlice = sourceText.Substring(start, length);
        if (!string.Equals(currentSlice, originalSlice, StringComparison.Ordinal))
            return (CommitAction.Abort, start, length);
        if (string.Equals(newSlice, currentSlice, StringComparison.Ordinal))
            return (CommitAction.NoChange, start, length);
        return (CommitAction.Replace, start, length);
    }

    private void Cancel()
    {
        if (_active is null) return;
        _active.Editor.KeyDown -= OnInPlaceKeyDown;
        _active.Editor.LostFocus -= OnInPlaceLostFocus;
        _active = null;

        // Re-parse the unmodified source to restore the rendered control.
        _viewModel.ResetText(_sourceEditor.Text);
    }

    // ── Delete block ──────────────────────────────────────────────────────

    private void DeleteBlock(AstNode node)
    {
        var (start, length) = SourceRangeOffsets.Resolve(_sourceEditor.Text, node.Source);
        if (length <= 0) return;

        // Extend the deletion to absorb the trailing blank line so we
        // don't leave a hole of separator-only whitespace.
        int extra = 0;
        int srcLen = _sourceEditor.Text.Length;
        if (start + length < srcLen && _sourceEditor.Text[start + length] == '\n') extra++;
        if (start + length + extra < srcLen && _sourceEditor.Text[start + length + extra] == '\n') extra++;
        _sourceEditor.Document.Remove(start, length + extra);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static StackPanel? ExtractTopPanel(Control rendered) => rendered switch
    {
        ScrollViewer sv when sv.Content is StackPanel sp => sp,
        StackPanel sp => sp,
        _ => null,
    };

    private sealed class InPlaceEditState
    {
        public required TextEditor Editor { get; init; }
        public required int SourceStart { get; init; }
        public required int SourceLength { get; init; }
        public required string OriginalSlice { get; init; }
    }
}
