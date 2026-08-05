using System;

namespace AdocNet.Importers.Docx;

/// <summary>
/// Raised when a .docx package cannot be read at all — not a ZIP container, no
/// main document part, or a malformed XML part. Content the importer merely
/// cannot represent in AsciiDoc is reported through
/// <see cref="DocxImportReport"/> instead of thrown.
/// </summary>
public sealed class DocxImportException : Exception
{
    public DocxImportException(string message) : base(message) { }

    public DocxImportException(string message, Exception innerException) : base(message, innerException) { }
}
