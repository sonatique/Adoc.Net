using System.Collections.Generic;
using AdocNet.Ast;

namespace AdocNet.Importers.Docx;

/// <summary>An image (or other binary part) referenced by the imported document.</summary>
public sealed class DocxMediaItem
{
    /// <summary>Part name inside the .docx package, e.g. <c>word/media/image1.png</c>.</summary>
    public required string PartName { get; init; }

    /// <summary>
    /// Path used in the emitted AsciiDoc macro, relative to the output
    /// document — e.g. <c>media/image1.png</c>.
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>Raw bytes of the part.</summary>
    public required byte[] Content { get; init; }

    /// <summary>Absolute path the bytes were written to, when the importer wrote them.</summary>
    public string? WrittenPath { get; internal set; }
}

/// <summary>Outcome of importing a .docx package.</summary>
public sealed class DocxImportResult
{
    /// <summary>The imported document as an Adoc.Net AST.</summary>
    public required DocumentNode Document { get; init; }

    /// <summary>What was mapped, approximated and lost.</summary>
    public required DocxImportReport Report { get; init; }

    /// <summary>Images referenced by <see cref="Document"/>, in first-use order.</summary>
    public required IReadOnlyList<DocxMediaItem> Media { get; init; }
}
