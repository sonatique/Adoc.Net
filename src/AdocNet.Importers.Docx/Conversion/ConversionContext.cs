using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace AdocNet.Importers.Docx;

/// <summary>
/// Shared state for one import: the package and its resolved parts, the option
/// set, the running report, and the media/footnote/id registries that the
/// block and inline converters both write into.
/// </summary>
internal sealed class ConversionContext
{
    private readonly Dictionary<string, DocxMediaItem> _mediaByPart = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DocxMediaItem> _media = new();
    private readonly HashSet<string> _usedIds = new(StringComparer.Ordinal);

    public required OpcPackage Package { get; init; }
    public required string DocumentPartName { get; init; }
    public required IReadOnlyDictionary<string, OpcRelationship> DocumentRelationships { get; init; }
    public required StyleTable Styles { get; init; }
    public required NumberingTable Numbering { get; init; }
    public required DocxImportOptions Options { get; init; }
    public required DocxImportReport Report { get; init; }

    /// <summary>Footnote/endnote bodies keyed by <c>w:id</c>.</summary>
    public Dictionary<string, XElement> Footnotes { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, XElement> Endnotes { get; } = new(StringComparer.Ordinal);

    /// <summary>Comment bodies keyed by <c>w:id</c>.</summary>
    public Dictionary<string, XElement> Comments { get; } = new(StringComparer.Ordinal);

    /// <summary>Set when a TOC field was seen, so the header can carry <c>:toc:</c>.</summary>
    public bool SawTableOfContents { get; set; }

    /// <summary>
    /// <c>dc:title</c> from the package core properties, when present. The
    /// block converter consults it before promoting a first heading to the
    /// document title.
    /// </summary>
    public string? CoreTitle { get; set; }

    public IReadOnlyList<DocxMediaItem> Media => _media;

    /// <summary>1-based index of the body paragraph being converted, for issue locations.</summary>
    public int ParagraphIndex { get; set; }

    /// <summary>
    /// Registers an image part, returning the path to use in the AsciiDoc
    /// macro. Repeated references to the same part share one media item.
    /// </summary>
    public string? RegisterImage(string relationshipId)
    {
        if (!DocumentRelationships.TryGetValue(relationshipId, out var rel))
        {
            Report.Lost("image.relationship-missing",
                $"Image relationship '{relationshipId}' is not declared by the document part.", ParagraphIndex);
            return null;
        }

        if (rel.IsExternal)
        {
            // Linked (not embedded) image: the URI is all we have, and it is
            // a perfectly good image target.
            return rel.Target;
        }

        if (rel.PartName is null) return null;
        if (_mediaByPart.TryGetValue(rel.PartName, out var existing)) return existing.RelativePath;

        var bytes = Package.ReadBytes(rel.PartName);
        if (bytes is null)
        {
            Report.Lost("image.part-missing",
                $"Image part '{rel.PartName}' is referenced but absent from the package.", ParagraphIndex);
            return null;
        }

        var fileName = FileNameOf(rel.PartName);
        var relativePath = string.IsNullOrEmpty(Options.MediaDirectoryName)
            ? fileName
            : Options.MediaDirectoryName.TrimEnd('/') + "/" + fileName;

        var item = new DocxMediaItem
        {
            PartName = rel.PartName,
            RelativePath = relativePath,
            Content = bytes,
        };

        _mediaByPart[rel.PartName] = item;
        _media.Add(item);
        return relativePath;
    }

    /// <summary>Resolves a hyperlink relationship to its target URI.</summary>
    public string? ResolveHyperlink(string relationshipId)
    {
        if (DocumentRelationships.TryGetValue(relationshipId, out var rel)) return rel.Target;

        Report.Lost("hyperlink.relationship-missing",
            $"Hyperlink relationship '{relationshipId}' is not declared by the document part.", ParagraphIndex);
        return null;
    }

    /// <summary>
    /// Reserves an id, appending a numeric suffix if it is already taken, so
    /// two identically-titled headings do not collide.
    /// </summary>
    public string ReserveId(string candidate)
    {
        if (_usedIds.Add(candidate)) return candidate;

        for (var i = 2; ; i++)
        {
            var alternative = candidate + "_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_usedIds.Add(alternative)) return alternative;
        }
    }

    private static string FileNameOf(string partName)
    {
        var slash = partName.LastIndexOf('/');
        return slash < 0 ? partName : partName.Substring(slash + 1);
    }
}
