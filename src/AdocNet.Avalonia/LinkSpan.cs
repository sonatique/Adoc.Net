using global::Avalonia.Controls.Documents;

namespace AdocNet.Avalonia;

/// <summary>
/// A hyperlink in rendered text: a <see cref="Span"/>, so it wraps and selects with the text
/// around it. <see cref="AvaloniaRenderer.LinkAt"/> finds the one under a point.
/// </summary>
public sealed class LinkSpan : Span
{
    public LinkSpan(string href)
    {
        Href = href;
    }

    /// <summary>The link target, passed to <see cref="AvaloniaRenderer.LinkClicked"/>.</summary>
    public string Href { get; }
}
