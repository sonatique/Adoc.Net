using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace AdocNet.Importers.Docx;

/// <summary>
/// A single relationship entry from an OPC <c>.rels</c> part.
/// </summary>
internal sealed class OpcRelationship
{
    public required string Id { get; init; }
    public required string Type { get; init; }

    /// <summary>Raw target as written in the <c>.rels</c> part.</summary>
    public required string Target { get; init; }

    /// <summary>True when <c>TargetMode="External"</c> — the target is a URI, not a part.</summary>
    public bool IsExternal { get; init; }

    /// <summary>
    /// Package-absolute part name (no leading slash) for internal targets;
    /// null for external ones.
    /// </summary>
    public string? PartName { get; init; }
}

/// <summary>
/// Minimal read-only Open Packaging Conventions container over a ZIP stream.
/// Only what a .docx importer needs: part lookup by name, XML part parsing and
/// relationship resolution. No dependency on <c>System.IO.Packaging</c>, which
/// is unavailable on netstandard2.0.
/// </summary>
internal sealed class OpcPackage : IDisposable
{
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyDictionary<string, OpcRelationship>> _relCache =
        new(StringComparer.OrdinalIgnoreCase);

    private OpcPackage(ZipArchive archive)
    {
        _archive = archive;
        foreach (var entry in archive.Entries)
        {
            // Zip entries in a .docx use forward slashes and no leading slash.
            _entries[Normalize(entry.FullName)] = entry;
        }
    }

    public static OpcPackage Open(Stream stream, bool leaveOpen = true)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen);
        }
        catch (InvalidDataException ex)
        {
            throw new DocxImportException("The input is not a readable ZIP/OPC container (a .docx file is a ZIP archive).", ex);
        }

        return new OpcPackage(archive);
    }

    public bool HasPart(string partName) => _entries.ContainsKey(Normalize(partName));

    /// <summary>Reads a part's raw bytes, or null when the part is absent.</summary>
    public byte[]? ReadBytes(string partName)
    {
        if (!_entries.TryGetValue(Normalize(partName), out var entry)) return null;

        using var source = entry.Open();
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Parses an XML part, or returns null when the part is absent. Malformed
    /// XML raises <see cref="DocxImportException"/> — a .docx with a broken
    /// document part cannot be imported, and silently producing an empty
    /// document would misreport fidelity.
    /// </summary>
    public XDocument? ReadXml(string partName)
    {
        if (!_entries.TryGetValue(Normalize(partName), out var entry)) return null;

        using var source = entry.Open();
        try
        {
            // DTD processing stays off: .docx parts never legitimately carry a
            // DTD, and honouring one would open the door to entity-expansion
            // attacks from untrusted documents.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreWhitespace = false,
            };
            using var reader = XmlReader.Create(source, settings);
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new DocxImportException($"Part '{partName}' is not well-formed XML.", ex);
        }
    }

    /// <summary>
    /// Relationships declared by <paramref name="partName"/>. Pass an empty
    /// string for the package-level relationships (<c>_rels/.rels</c>).
    /// </summary>
    public IReadOnlyDictionary<string, OpcRelationship> GetRelationships(string partName)
    {
        partName = Normalize(partName);
        if (_relCache.TryGetValue(partName, out var cached)) return cached;

        var relsPart = RelationshipPartFor(partName);
        var map = new Dictionary<string, OpcRelationship>(StringComparer.Ordinal);
        var doc = ReadXml(relsPart);
        if (doc?.Root is not null)
        {
            var baseDirectory = DirectoryOf(partName);
            foreach (var rel in doc.Root.Elements(Ns.PkgRel + "Relationship"))
            {
                var id = rel.Attribute("Id")?.Value;
                var type = rel.Attribute("Type")?.Value;
                var target = rel.Attribute("Target")?.Value;
                if (id is null || type is null || target is null) continue;

                var external = string.Equals(rel.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase);
                map[id] = new OpcRelationship
                {
                    Id = id,
                    Type = type,
                    Target = target,
                    IsExternal = external,
                    PartName = external ? null : ResolvePartName(baseDirectory, target),
                };
            }
        }

        _relCache[partName] = map;
        return map;
    }

    /// <summary>
    /// First relationship of <paramref name="type"/> declared by
    /// <paramref name="partName"/>, or null when there is none.
    /// </summary>
    public OpcRelationship? FindRelationship(string partName, string type)
    {
        foreach (var rel in GetRelationships(partName).Values)
        {
            if (string.Equals(rel.Type, type, StringComparison.Ordinal)) return rel;
        }

        return null;
    }

    private static string RelationshipPartFor(string partName)
    {
        if (partName.Length == 0) return "_rels/.rels";
        var dir = DirectoryOf(partName);
        var file = partName.Substring(dir.Length);
        return dir + "_rels/" + file + ".rels";
    }

    /// <summary>Directory portion of a part name, with a trailing slash (or empty at the root).</summary>
    private static string DirectoryOf(string partName)
    {
        var slash = partName.LastIndexOf('/');
        return slash < 0 ? string.Empty : partName.Substring(0, slash + 1);
    }

    /// <summary>
    /// Resolves a relationship target against the declaring part's directory,
    /// collapsing <c>.</c> and <c>..</c> segments. Absolute targets
    /// (<c>/word/media/x.png</c>) are taken as package-absolute.
    /// </summary>
    internal static string ResolvePartName(string baseDirectory, string target)
    {
        var combined = target.StartsWith("/", StringComparison.Ordinal)
            ? target.Substring(1)
            : baseDirectory + target;

        combined = combined.Replace('\\', '/');

        var segments = combined.Split('/');
        var stack = new List<string>(segments.Length);
        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment == ".") continue;
            if (segment == "..")
            {
                if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                continue;
            }

            stack.Add(segment);
        }

        return string.Join("/", stack.ToArray());
    }

    private static string Normalize(string partName)
    {
        var normalized = partName.Replace('\\', '/');
        return normalized.StartsWith("/", StringComparison.Ordinal) ? normalized.Substring(1) : normalized;
    }

    public void Dispose() => _archive.Dispose();
}
