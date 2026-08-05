using System;
using System.Collections.Generic;

namespace AdocNet.Importers.Docx;

/// <summary>How revision marks (<c>w:ins</c> / <c>w:del</c>) are resolved.</summary>
public enum TrackedChangeHandling
{
    /// <summary>Insertions are kept, deletions dropped — what Word shows as "final".</summary>
    Accept,

    /// <summary>Insertions are dropped, deletions kept — what Word shows as "original".</summary>
    Reject,
}

/// <summary>How Word comments are handled.</summary>
public enum CommentHandling
{
    /// <summary>Comments are dropped (they are reported, not lost silently).</summary>
    Ignore,

    /// <summary>Each comment becomes an AsciiDoc line comment before its anchoring block.</summary>
    LineComments,
}

/// <summary>
/// Knobs for <see cref="DocxImporter"/>. Defaults target the common case: a
/// Word document authored with the built-in Heading/List/Quote styles.
/// </summary>
public sealed class DocxImportOptions
{
    public static DocxImportOptions Default { get; } = new();

    /// <summary>
    /// Directory that extracted images are written to, relative to the output
    /// document. Also used as the path prefix in emitted <c>image::</c> targets.
    /// </summary>
    public string MediaDirectoryName { get; init; } = "media";

    /// <summary>
    /// When false, images are still mapped to <c>image::</c> nodes and their
    /// bytes are exposed on <see cref="DocxImportResult.Media"/>, but
    /// <see cref="DocxImporter.ImportFile"/> does not write them to disk.
    /// </summary>
    public bool ExtractMedia { get; init; } = true;

    /// <summary>
    /// Promote a document that starts with a single top-level heading to the
    /// AsciiDoc document title (<c>= Title</c>), shifting the remaining
    /// headings up one level. Applies only when the document has no paragraph
    /// styled <c>Title</c> and no <c>dc:title</c> core property.
    /// </summary>
    public bool PromoteFirstHeadingToTitle { get; init; } = true;

    /// <summary>
    /// Recognise paragraphs and single-cell tables beginning with
    /// <c>NOTE:</c>, <c>TIP:</c>, <c>IMPORTANT:</c>, <c>WARNING:</c> or
    /// <c>CAUTION:</c> as AsciiDoc admonitions.
    /// </summary>
    public bool DetectAdmonitions { get; init; } = true;

    /// <summary>
    /// Map paragraphs whose style or run fonts are monospaced (Consolas,
    /// Courier New, …) to listing blocks instead of paragraphs.
    /// </summary>
    public bool DetectCodeBlocks { get; init; } = true;

    /// <summary>
    /// Carry Word core properties (<c>dc:creator</c>, <c>dc:description</c>,
    /// revision, modification date) into the AsciiDoc document header.
    /// </summary>
    public bool ImportCoreProperties { get; init; } = true;

    /// <summary>Which side of a tracked change ends up in the output.</summary>
    public TrackedChangeHandling TrackedChanges { get; init; } = TrackedChangeHandling.Accept;

    /// <summary>What to do with Word comments.</summary>
    public CommentHandling Comments { get; init; } = CommentHandling.Ignore;

    /// <summary>
    /// Extra paragraph-style mappings, keyed by style id or style name
    /// (case-insensitive). Values are AsciiDoc roles applied to the produced
    /// block, letting a document's house styles survive the round trip as
    /// <c>[.rolename]</c> even when they have no structural equivalent.
    /// </summary>
    public IReadOnlyDictionary<string, string> StyleRoleMap { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Preserve direct character formatting that AsciiDoc has no native markup
    /// for (underline, strikethrough, colour, small caps) as inline roles —
    /// <c>[.underline]#text#</c>. Turning this off drops the formatting and
    /// records it in the report.
    /// </summary>
    public bool PreserveFormattingAsRoles { get; init; } = true;

    /// <summary>
    /// Emit a <c>:toc:</c> attribute when the document contains a Word table
    /// of contents field.
    /// </summary>
    public bool ConvertTocFieldToAttribute { get; init; } = true;
}
