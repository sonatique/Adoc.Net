using System.IO;
using global::Avalonia.Controls;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Platform.Storage;
using AvaloniaEdit;
using AdocNet.Avalonia.Editor.Commands;
using AdocNet.Avalonia.Editor.ViewModels;
using AdocNet.Ast;

namespace AdocNet.Avalonia.Editor;

public partial class MainWindow : Window
{
    private readonly EditorViewModel _vm = new();
    private string? _currentFilePath;

    public MainWindow()
    {
        InitializeComponent();

        // Wire VM → view: every time a parse+render completes we get a
        // fresh Avalonia control tree for the preview pane plus updated
        // status-bar data.
        _vm.Rendered += OnRendered;

        // AvaloniaEdit's TextChanged fires for every edit (toolbar commands
        // and direct keystrokes both go through it). We rebuild a fresh
        // DocumentSnapshot from the editor text rather than tracking
        // incremental offsets — the parse-render path is debounced anyway.
        SourceEditor.TextChanged += OnSourceEditorTextChanged;
        SourceEditor.TextArea.Caret.PositionChanged += (_, _) => UpdateCaretContext();

        // Keyboard shortcuts: Ctrl+B/I/`, Ctrl+O, Ctrl+S, plus heading H0–H4.
        WireKeyBindings();

        // Seed the editor with the bundled sample so the app does something
        // visible on first launch.
        var samplePath = Path.Combine(AppContext.BaseDirectory, "sample.adoc");
        if (File.Exists(samplePath))
        {
            LoadFile(samplePath);
        }
        else
        {
            SourceEditor.Text = "= Untitled\n\nStart typing.\n";
        }
    }

    // ── File operations ───────────────────────────────────────────────────

    private async void OnOpenClick(object? sender, RoutedEventArgs e) => await OpenAsync();

    private async Task OpenAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open AsciiDoc file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("AsciiDoc")
                {
                    Patterns = ["*.adoc", "*.asciidoc", "*.txt"],
                },
            ],
        });

        if (files.Count == 0) return;
        var path = files[0].TryGetLocalPath();
        if (path is null) return;
        LoadFile(path);
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e) => await SaveAsync();

    private async Task SaveAsync()
    {
        if (_currentFilePath is null)
        {
            var dest = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save AsciiDoc file",
                DefaultExtension = "adoc",
                SuggestedFileName = "document.adoc",
            });
            _currentFilePath = dest?.TryGetLocalPath();
            if (_currentFilePath is null) return;
        }

        await File.WriteAllTextAsync(_currentFilePath, SourceEditor.Text);
        Title = $"AdocNet Hybrid Editor — {Path.GetFileName(_currentFilePath)}";
    }

    private void LoadFile(string path)
    {
        var text = File.ReadAllText(path);
        _currentFilePath = path;
        SourceEditor.Text = text; // TextChanged handler kicks in
        SaveButton.IsEnabled = true;
        Title = $"AdocNet Hybrid Editor — {Path.GetFileName(path)}";
    }

    // ── Parse-render loop ─────────────────────────────────────────────────

    private void OnSourceEditorTextChanged(object? sender, EventArgs e)
    {
        // Skip incremental delta tracking and just reset the VM's text
        // wholesale — the debounce window absorbs the cost and this side-
        // steps any edge cases between Avalonia's change events and the
        // DocumentChange model.
        _vm.ResetText(SourceEditor.Text);
        LengthLabel.Text = $"{SourceEditor.Text.Length} chars";
    }

    private void OnRendered(EditorRenderResult r)
    {
        PreviewHost.Content = r.Preview;
        VersionLabel.Text   = $"v{r.Snapshot.Version}";
        ParseTimeLabel.Text = $"parsed in {r.ParseAndRender.TotalMilliseconds:F1} ms";
        DiagsLabel.Text     = $"{r.Snapshot.Diagnostics.Count} diagnostics";
        UpdateCaretContext();
    }

    private void UpdateCaretContext()
    {
        var doc = _vm.Snapshot.Document;
        if (doc is null)
        {
            CaretContextLabel.Text = string.Empty;
            return;
        }

        var caret = SourceEditor.TextArea.Caret;
        var node = CaretContext.Resolve(doc, caret.Line, caret.Column);
        CaretContextLabel.Text = node is null ? string.Empty : $"in {CaretContext.Describe(node)}";
    }

    // ── Toolbar handlers ─────────────────────────────────────────────────

    private void OnBoldClick(object? sender, RoutedEventArgs e)
        => FormattingCommands.Bold(SourceEditor);

    private void OnItalicClick(object? sender, RoutedEventArgs e)
        => FormattingCommands.Italic(SourceEditor);

    private void OnMonoClick(object? sender, RoutedEventArgs e)
        => FormattingCommands.Monospace(SourceEditor);

    private void OnBulletListClick(object? sender, RoutedEventArgs e)
        => ListCommands.BulletList(SourceEditor);

    private void OnNumberedListClick(object? sender, RoutedEventArgs e)
        => ListCommands.NumberedList(SourceEditor);

    private void OnLinkClick(object? sender, RoutedEventArgs e)
        => InsertCommands.Link(SourceEditor);

    private void OnImageClick(object? sender, RoutedEventArgs e)
        => InsertCommands.Image(SourceEditor);

    private void OnTableClick(object? sender, RoutedEventArgs e)
        => InsertCommands.Table(SourceEditor);

    private void OnQuoteClick(object? sender, RoutedEventArgs e)
        => BlockCommands.QuoteBlock(SourceEditor);

    private void OnAdmonitionClick(object? sender, RoutedEventArgs e)
        => BlockCommands.Admonition(SourceEditor);

    private void OnCodeBlockClick(object? sender, RoutedEventArgs e)
        => BlockCommands.CodeBlock(SourceEditor);

    private void OnHrClick(object? sender, RoutedEventArgs e)
        => InsertCommands.ThematicBreak(SourceEditor);

    private bool _suppressHeadingHandler;
    private void OnHeadingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressHeadingHandler) return;
        var idx = HeadingPicker.SelectedIndex;
        switch (idx)
        {
            case 0: HeadingCommands.None(SourceEditor); break;
            case 1: HeadingCommands.H1(SourceEditor);   break;
            case 2: HeadingCommands.H2(SourceEditor);   break;
            case 3: HeadingCommands.H3(SourceEditor);   break;
            case 4: HeadingCommands.H4(SourceEditor);   break;
        }
        // Reset the picker so picking the same level twice still triggers
        // the command (selection-changed only fires on actual changes).
        _suppressHeadingHandler = true;
        HeadingPicker.SelectedIndex = -1;
        _suppressHeadingHandler = false;
    }

    // ── Keyboard shortcuts ────────────────────────────────────────────────

    private void WireKeyBindings()
    {
        AddBinding(KeyGesture.Parse("Ctrl+O"), () => _ = OpenAsync());
        AddBinding(KeyGesture.Parse("Ctrl+S"), () => _ = SaveAsync());
        AddBinding(KeyGesture.Parse("Ctrl+B"), () => FormattingCommands.Bold(SourceEditor));
        AddBinding(KeyGesture.Parse("Ctrl+I"), () => FormattingCommands.Italic(SourceEditor));
        AddBinding(KeyGesture.Parse("Ctrl+OemBackquote"), () => FormattingCommands.Monospace(SourceEditor));
    }

    private void AddBinding(KeyGesture gesture, Action action)
    {
        KeyBindings.Add(new KeyBinding
        {
            Gesture = gesture,
            Command = new RelayCommand(action),
        });
    }

    private sealed class RelayCommand(Action execute) : System.Windows.Input.ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
