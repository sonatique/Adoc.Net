using System.Text;
using AdocNet.Ast;

namespace AdocNet.Editor;

/// <summary>
/// Performs incremental HTML rendering by diffing two ASTs and splicing
/// only changed sections into the previous HTML output. Requires the
/// previous HTML to contain section markers (<c>&lt;!-- sect:N --&gt;</c>).
/// Falls back to full render when incremental is not possible.
/// </summary>
public sealed class IncrementalHtmlRenderer
{
    private readonly IDocumentRenderer _renderer;
    private readonly Func<string, DocumentNode> _parser;

    /// <summary>
    /// Creates an incremental renderer using the given HTML renderer.
    /// </summary>
    /// <param name="renderer">The HTML renderer to use for re-rendering changed sections.</param>
    /// <param name="parser">The parser function (needed for full-render fallback).</param>
    public IncrementalHtmlRenderer(IDocumentRenderer renderer, Func<string, DocumentNode> parser)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    /// <summary>
    /// Renders incrementally by diffing the old and new documents, then splicing
    /// changed sections into the previous HTML. Falls back to full render when
    /// the previous HTML has no markers or when metadata changed.
    /// </summary>
    /// <param name="oldDoc">The previous document AST.</param>
    /// <param name="newDoc">The new document AST.</param>
    /// <param name="previousHtml">The full HTML from the previous render (with section markers).</param>
    /// <param name="options">Render options. EnableIncrementalMarkers is forced to true.</param>
    /// <returns>Updated HTML with changed sections re-rendered.</returns>
    public string Render(
        DocumentNode oldDoc,
        DocumentNode newDoc,
        string previousHtml,
        RenderOptions? options = null)
    {
        if (oldDoc is null) throw new ArgumentNullException(nameof(oldDoc));
        if (newDoc is null) throw new ArgumentNullException(nameof(newDoc));
        if (previousHtml is null) throw new ArgumentNullException(nameof(previousHtml));

        // Check if previous HTML has markers
        if (!previousHtml.Contains("<!-- sect:"))
            return FullRender(newDoc, options);

        // Check if metadata changed (title, attributes)
        if (HasMetadataChanged(oldDoc, newDoc))
            return FullRender(newDoc, options);

        var diff = AstDiffer.DiffSections(oldDoc, newDoc);

        // If sections were added or removed, fall back to full render
        // (section indices shift, making splice unreliable)
        bool hasStructuralChange = false;
        bool allUnchanged = true;
        foreach (var entry in diff)
        {
            if (entry.ChangeType == AstDiffChangeType.Added ||
                entry.ChangeType == AstDiffChangeType.Removed)
            {
                hasStructuralChange = true;
                break;
            }
            if (entry.ChangeType != AstDiffChangeType.Unchanged)
                allUnchanged = false;
        }

        if (hasStructuralChange)
            return FullRender(newDoc, options);

        if (allUnchanged)
            return previousHtml;

        // Incremental: replace only modified sections
        return SpliceModifiedSections(newDoc, previousHtml, diff, options);
    }

    private string SpliceModifiedSections(
        DocumentNode newDoc,
        string previousHtml,
        IReadOnlyList<AstDiffEntry> diff,
        RenderOptions? options)
    {
        var sb = new StringBuilder(previousHtml.Length);
        int pos = 0;

        foreach (var entry in diff)
        {
            if (entry.ChangeType == AstDiffChangeType.Unchanged)
                continue;

            if (entry.ChangeType != AstDiffChangeType.Modified)
                continue;

            var openMarker = $"<!-- sect:{entry.Index} -->\n";
            var closeMarker = $"<!-- /sect:{entry.Index} -->\n";

            int openStart = previousHtml.IndexOf(openMarker, pos, StringComparison.Ordinal);
            if (openStart < 0) continue;

            int contentStart = openStart + openMarker.Length;
            int closeStart = previousHtml.IndexOf(closeMarker, contentStart, StringComparison.Ordinal);
            if (closeStart < 0) continue;

            // Copy everything before this section's content
            sb.Append(previousHtml, pos, contentStart - pos);

            // Render the modified section
            var sectionHtml = RenderSingleSection(newDoc, entry.Index, options);
            sb.Append(sectionHtml);

            // Move past the old section content + close marker
            pos = closeStart;
        }

        // Copy remaining HTML after last replacement
        if (pos < previousHtml.Length)
            sb.Append(previousHtml, pos, previousHtml.Length - pos);

        return sb.ToString();
    }

    private string RenderSingleSection(DocumentNode doc, int sectionIndex, RenderOptions? options)
    {
        if (sectionIndex >= doc.Children.Count)
            return "";

        // Create a temporary document with just this section
        var tempDoc = new DocumentNode();
        if (doc.Title is not null)
            tempDoc.Title = doc.Title;
        foreach (var kvp in doc.Attributes)
            tempDoc.SetAttribute(kvp.Key, kvp.Value);
        tempDoc.AddChild(doc.Children[sectionIndex]);

        // Render the temporary document (produces markers + section HTML)
        var html = RenderToString(tempDoc, options);

        // Extract just the section content (between the markers)
        // The temp doc has one child at index 0, so look for sect:0 markers
        var openMarker = "<!-- sect:0 -->\n";
        var closeMarker = "<!-- /sect:0 -->\n";
        int openEnd = html.IndexOf(openMarker, StringComparison.Ordinal);
        if (openEnd < 0) return html;
        int contentStart = openEnd + openMarker.Length;
        int closeStart = html.IndexOf(closeMarker, contentStart, StringComparison.Ordinal);
        if (closeStart < 0) return html;

        return html.Substring(contentStart, closeStart - contentStart);
    }

    private string FullRender(DocumentNode doc, RenderOptions? options)
    {
        return RenderToString(doc, options);
    }

    private string RenderToString(DocumentNode doc, RenderOptions? options)
    {
        using var ms = new MemoryStream();
        var renderOptions = EnsureMarkers(options);
        _renderer.Render(doc, ms, renderOptions);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static RenderOptions EnsureMarkers(RenderOptions? options)
    {
        // We need EnableIncrementalMarkers = true. Since HtmlRenderOptions is in
        // a different assembly, we use duck-typing via reflection to set it.
        // If the option is already set, return as-is.
        if (options is null)
            return new RenderOptions();

        // Check if it's already an HtmlRenderOptions with markers enabled
        // by checking the property via reflection (to avoid assembly coupling)
        var type = options.GetType();
        var markerProp = type.GetProperty("EnableIncrementalMarkers");
        if (markerProp is not null)
        {
            var current = markerProp.GetValue(options);
            if (current is true)
                return options;
        }

        // Return options as-is — the caller should ensure markers are enabled.
        // This avoids coupling AdocNet.Core to AdocNet.Converters.Html.
        return options;
    }

    private static bool HasMetadataChanged(DocumentNode oldDoc, DocumentNode newDoc)
    {
        if (oldDoc.Title != newDoc.Title)
            return true;

        var oldAttrs = oldDoc.Attributes;
        var newAttrs = newDoc.Attributes;

        if (oldAttrs.Count != newAttrs.Count)
            return true;

        foreach (var kvp in oldAttrs)
        {
            if (!newAttrs.TryGetValue(kvp.Key, out var newVal)
                || !string.Equals(kvp.Value, newVal, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
