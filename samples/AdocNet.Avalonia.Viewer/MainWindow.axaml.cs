using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AdocNet.Layout.Builders;
using AdocNet.Parser;

namespace AdocNet.Avalonia.Viewer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open AsciiDoc File",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("AsciiDoc Files")
                {
                    Patterns = ["*.adoc", "*.asciidoc", "*.txt"],
                },
            ],
        });

        if (files.Count == 0)
            return;

        var file = files[0];
        var path = file.TryGetLocalPath();
        if (path == null)
            return;

        var text = File.ReadAllText(path);
        RenderDocument(text, Path.GetFileName(path));
    }

    private void RenderDocument(string asciidoc, string fileName)
    {
        var result = AdocParser.Parse(asciidoc);
        var layout = new LayoutBuilder().Build(result.Document);
        var rendered = new AdocNet.Avalonia.AvaloniaRenderer().Render(layout);

        ContentHost.Content = rendered;
        FileLabel.Text = fileName;
    }
}
