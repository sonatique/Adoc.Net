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

        int n = Math.Min(panel.Children.Count, document.Children.Count);
        for (int i = 0; i < n; i++)
        {
            if (panel.Children[i] is not Control child) continue;

            // Capture by value so the closure sees the right index.
            int blockIndex = i;
            child.PointerPressed -= OnAnyBlockPointerPressed; // safe even when not previously attached
            child.PointerPressed += OnAnyBlockPointerPressed;
            child.Tag = new BlockClickTag(blockIndex, child.Tag);
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
        if (control.Tag is not BlockClickTag tag) return;
        if (_currentDocument is null) return;
        if (tag.BlockIndex < 0 || tag.BlockIndex >= _currentDocument.Children.Count) return;

        var node = _currentDocument.Children[tag.BlockIndex];
        if (node.Source.IsNone) return;

        EnterEditMode(tag.BlockIndex, node);
        e.Handled = true;
    }

    private void ShowContextMenu(Control? control, PointerPressedEventArgs e)
    {
        if (control is null) return;
        if (control.Tag is not BlockClickTag tag) return;
        if (_currentDocument is null) return;
        if (tag.BlockIndex < 0 || tag.BlockIndex >= _currentDocument.Children.Count) return;

        var node = _currentDocument.Children[tag.BlockIndex];
        var menu = new ContextMenu
        {
            ItemsSource = new[]
            {
                BuildMenuItem("Edit block", () => EnterEditMode(tag.BlockIndex, node)),
                BuildMenuItem("Delete block", () => DeleteBlock(node)),
            },
        };
        menu.Open(control);
        e.Handled = true;
    }

    private static MenuItem BuildMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    // ── In-place edit ─────────────────────────────────────────────────────

    private void EnterEditMode(int blockIndex, AstNode node)
    {
        var (start, length) = SourceRangeOffsets.Resolve(_sourceEditor.Text, node.Source);
        if (length <= 0) return;

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

        var panel = ExtractTopPanel((Control)_previewHost.Content!);
        if (panel is null) return;
        if (blockIndex < 0 || blockIndex >= panel.Children.Count) return;

        var replacedControl = (Control)panel.Children[blockIndex];
        panel.Children[blockIndex] = inplace;

        _active = new InPlaceEditState
        {
            Editor = inplace,
            BlockIndex = blockIndex,
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

        // Detach handlers before clearing _active so a follow-up event
        // doesn't re-fire on the dying editor.
        _active.Editor.KeyDown -= OnInPlaceKeyDown;
        _active.Editor.LostFocus -= OnInPlaceLostFocus;
        _active = null;

        if (start < 0 || start > _sourceEditor.Text.Length) return;
        if (start + length > _sourceEditor.Text.Length) length = _sourceEditor.Text.Length - start;
        if (string.Equals(newSlice, _sourceEditor.Document.GetText(start, length), StringComparison.Ordinal))
        {
            // No change — just request a re-render so the rendered view
            // replaces the in-place editor.
            _viewModel.ResetText(_sourceEditor.Text);
            return;
        }

        _sourceEditor.Document.Replace(start, length, newSlice);
        // The source TextEditor's TextChanged handler will tell the VM
        // to re-parse; the incremental renderer will splice the freshly
        // rendered block back into the panel.
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

    /// <summary>
    /// Tag we attach to each clickable preview block. Wraps the original
    /// renderer tag (so the incremental renderer's <c>SectionTag</c> is
    /// still accessible) plus the block's positional index.
    /// </summary>
    private readonly record struct BlockClickTag(int BlockIndex, object? InnerTag);

    private sealed class InPlaceEditState
    {
        public required TextEditor Editor { get; init; }
        public required int BlockIndex { get; init; }
        public required int SourceStart { get; init; }
        public required int SourceLength { get; init; }
        public required string OriginalSlice { get; init; }
    }
}
