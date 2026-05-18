using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Threading;
using AdocNet.Ast;
using AdocNet.Editor;
using AdocNet.Layout.Builders;
using AdocNet.Parser;

namespace AdocNet.Avalonia.Editor.ViewModels;

/// <summary>
/// State + orchestration for the hybrid editor:
/// <list type="number">
///   <item><description>Track the current <see cref="DocumentSnapshot"/>
///     (version + text + parsed document).</description></item>
///   <item><description>On every source change, schedule a debounced parse
///     + render. Inflight parses are cancelled when newer changes arrive,
///     so the preview always lags the source by at most one debounce
///     window.</description></item>
///   <item><description>Marshal the rendered <see cref="Control"/> back to
///     the UI thread.</description></item>
/// </list>
/// </summary>
internal sealed class EditorViewModel
{
    /// <summary>How long a quiet period must pass before a parse starts.</summary>
    public TimeSpan DebounceInterval { get; init; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Latest snapshot. Updated synchronously on every <see cref="ApplyTextReplacement"/>.</summary>
    public DocumentSnapshot Snapshot { get; private set; } = DocumentSnapshot.Initial(string.Empty);

    /// <summary>
    /// Fires after each successful parse + render. The handler receives the
    /// fully built Avalonia preview control, the new snapshot (with its
    /// <see cref="DocumentSnapshot.Document"/> populated), and parse timing.
    /// Always invoked on the UI thread.
    /// </summary>
    public event Action<EditorRenderResult>? Rendered;

    private CancellationTokenSource? _inflight;
    private readonly LayoutBuilder _layoutBuilder = new();
    private readonly global::AdocNet.Avalonia.AvaloniaRenderer _renderer = new();

    /// <summary>Replace the entire text with the new value (e.g. file open).</summary>
    public void ResetText(string text)
    {
        Snapshot = DocumentSnapshot.Initial(text);
        ScheduleParseRender();
    }

    /// <summary>
    /// Replace the text in <paramref name="offset"/>..<paramref name="offset"/>
    /// + <paramref name="length"/> with <paramref name="newText"/>. This is the
    /// hook AvaloniaEdit's <c>TextChanged</c> event raises for every typed
    /// character and every command invocation.
    /// </summary>
    public void ApplyTextReplacement(int offset, int length, string newText)
    {
        var change = new DocumentChange(offset, length, newText);
        Snapshot = Snapshot.ApplyChanges(new[] { change });
        ScheduleParseRender();
    }

    private void ScheduleParseRender()
    {
        _inflight?.Cancel();
        var cts = new CancellationTokenSource();
        _inflight = cts;
        var snapshotForTask = Snapshot;
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceInterval, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;

                var sw = Stopwatch.StartNew();
                var result = AdocParser.Parse(snapshotForTask.Text);
                var layout = _layoutBuilder.Build(result.Document);
                var control = _renderer.Render(layout);
                sw.Stop();

                if (token.IsCancellationRequested) return;

                // Snapshot now carries the parsed Document so subsequent edits
                // can compare against it (used by Block-WYSIWYG / incremental
                // rendering in later phases).
                var parsedSnapshot = new DocumentSnapshot(
                    snapshotForTask.Version,
                    snapshotForTask.Text,
                    result.Document,
                    result.Diagnostics);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    Snapshot = parsedSnapshot;
                    Rendered?.Invoke(new EditorRenderResult(
                        control, parsedSnapshot, sw.Elapsed));
                });
            }
            catch (TaskCanceledException) { /* expected */ }
            catch (OperationCanceledException) { /* expected */ }
        }, token);
    }
}

/// <summary>Result of a single parse + render cycle, surfaced to the view.</summary>
internal sealed record EditorRenderResult(
    Control Preview,
    DocumentSnapshot Snapshot,
    TimeSpan ParseAndRender);
