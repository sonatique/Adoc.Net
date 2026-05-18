using global::Avalonia;
using global::Avalonia.Headless;
using AdocNet.Avalonia.Editor;

[assembly: AvaloniaTestApplication(typeof(AdocNet.Avalonia.Editor.Tests.HeadlessAppBuilder))]

namespace AdocNet.Avalonia.Editor.Tests;

/// <summary>
/// Boots a minimal Avalonia application in headless mode so that
/// <c>TextEditor</c> instances can be created without a real window
/// surface. Required for command unit tests that exercise AvaloniaEdit.
/// </summary>
public static class HeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true });
}
