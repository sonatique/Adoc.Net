namespace AdocNet.Converters.Html;

/// <summary>
/// Built-in HTML themes that provide CSS styling for rendered documents.
/// </summary>
public enum HtmlTheme
{
    /// <summary>No built-in CSS — output is a bare HTML fragment (default).</summary>
    None,

    /// <summary>Clean, modern default theme with a sans-serif font stack and muted colors.</summary>
    Default,

    /// <summary>Theme inspired by the Asciidoctor default stylesheet.</summary>
    Asciidoctor,

    /// <summary>Minimal theme with maximum readability and very little decoration.</summary>
    Clean,
}
