# Importing Word documents (.docx → AsciiDoc)

`AdocNet.Importers.Docx` reads a WordprocessingML package into the Adoc.Net AST;
`AdocNet.Emitter` then serialises that AST to AsciiDoc source. The importer is
pure managed code with no dependency on Word, `System.IO.Packaging` or the Open
XML SDK, and multi-targets `netstandard2.0`, `net8.0` and `net10.0` like the
rest of the library.

```csharp
using AdocNet.Importers.Docx;

var importer = new DocxImporter();

// Straight to AsciiDoc source
string adoc = importer.ToAsciiDoc("handbook.docx");

// Or keep the AST, the extracted media and the fidelity report
DocxImportResult result = importer.ImportFile("handbook.docx");
Console.Write(result.Report.ToSummary());

// Convert a file and write images next to the output
importer.ConvertFile("handbook.docx", "out/handbook.adoc");
```

## Command line

```
dotnet tool install --global AdocNet.Docx    # installs docx2adoc
docx2adoc handbook.docx -o handbook.adoc --report
```

| Option | Effect |
|--------|--------|
| `-o, --output <path>` | Write to a file. Without it, source goes to stdout and no media is written. |
| `--media-dir <name>` | Directory name for extracted images beside the output (default `media`). |
| `--no-media` | Keep images out of the file system (they stay on `DocxImportResult.Media`). |
| `--report` | Print the fidelity report to stderr. |
| `--min-fidelity <n>` | Exit with code 3 when fidelity falls below `n` percent — useful in a pipeline. |
| `--reject-revisions` | Keep the original text of tracked changes instead of the revised text. |
| `--comments` | Import Word comments instead of dropping them. |
| `--no-admonitions`, `--no-code-blocks` | Turn off the corresponding heuristic. |
| `--no-properties` | Do not copy core properties into the document header. |
| `--plain-formatting` | Drop underline/strikethrough/caps/colour instead of keeping them as roles. |
| `--keep-heading` | Do not promote a leading top-level heading to the document title. |

## What is mapped

| Word | AsciiDoc |
|------|----------|
| Heading 1–9 (by style name, `w:outlineLvl`, or a `w:basedOn` ancestor) | Section levels 1–5, normalised so no level is skipped |
| `Title` / `Subtitle` styles, `dc:title` | `= Document Title: Subtitle` |
| Paragraphs | Paragraphs; explicit line breaks become `[%hardbreaks]` |
| Numbered/bulleted lists (`numbering.xml`, including `lvlOverride` and `numStyleLink`) | `*` / `.` lists with nesting; `[loweralpha]`, `[upperroman]`, … and `start=` |
| A `ListParagraph` paragraph after a list item | List item continuation |
| Tables | `|===` tables with `cols` widths, header row, `gridSpan` → colspan, `vMerge` → rowspan |
| A cell with several blocks | AsciiDoc-styled cell (`a|`); nested tables re-delimited with `!` |
| Inline and floating images (DrawingML and VML) | `image:` / `image::` with pixel width/height from the EMU extent |
| `Caption` paragraphs | Block title on the neighbouring image/table, with Word's caption number stripped |
| Hyperlinks, `HYPERLINK` fields | `link:target[label]`, or a bare URL when the label is the URL |
| Bookmarks, internal links, `REF` fields | `[[anchor]]` (hoisted onto the block or section) and `<<anchor,label>>` |
| Footnotes and endnotes | `footnote:[…]` |
| Text boxes and grouped shapes | Sidebar blocks (`****`) after the anchoring paragraph |
| `Quote` / `IntenseQuote` paragraphs | `____` quote block (consecutive paragraphs merge) |
| Monospaced paragraphs (`HTMLPreformatted`, `PlainText`, code styles, or all-monospace runs) | `----` listing block, verbatim |
| `NOTE:`/`TIP:`/`IMPORTANT:`/`WARNING:`/`CAUTION:` paragraphs and single-cell callout tables | Admonitions |
| Page breaks, `pageBreakBefore`, bottom-bordered empty paragraphs | `<<<` and `'''` |
| Bold, italic, monospace fonts/styles, highlight, super/subscript | `*`, `_`, `` ` ``, `#`, `^`, `~` — unconstrained forms when the span starts or ends mid-word |
| Underline, strikethrough, small caps, all caps, font colour | Roles: `[.underline]`, `[.line-through]`, `[.small-caps]`, `[.uppercase]`, `[.color-rrggbb]` |
| Tracked changes | Revised text by default, original with `--reject-revisions` |
| `TOC` field | `:toc:` attribute (the cached entries are dropped; the backend regenerates them) |
| Content controls (`w:sdt`), smart tags, markup-compatibility alternates | Unwrapped to their content |
| Core properties | `:author:`, `:description:`, `:keywords:`, `:revnumber:`, `:revdate:` |

Formatting a run inherits from its *paragraph* style is deliberately **not**
turned into inline markup: Word's heading styles are bold and its Quote style is
italic, and `== *Heading*` says nothing the section marker does not already say.
Direct run formatting and character styles are honoured.

## What cannot be mapped

AsciiDoc has no model for page geometry or Word's presentation layer, so these
are reported rather than silently dropped:

- Page size, margins, headers, footers, columns, section properties
- Tab stops (a tab inside a paragraph becomes a space), line spacing, indents
- Exact fonts and sizes; a floating image's wrap and position
- Charts, SmartArt and drawing shapes with no picture or text
- OLE/embedded objects, column breaks
- Comments, unless `--comments` is given

## Fidelity

`DocxImportReport.Fidelity` is a **content-mapping ratio**: the share of
content-bearing WordprocessingML units — run text, formatting toggles,
paragraphs, list items, table cells, images, links, notes, breaks — that reached
the AST as an equivalent AsciiDoc construct. It says nothing about visual
fidelity, which is not a meaningful target for a format with no page model.

```
fidelity: 99.63% (272/273 units)
paragraphs: 87, runs: 214, sections: 6, lists items: 12, tables: 3, images: 2, links: 4, footnotes: 1
issues:
  [Warning] run.color-as-role ×9 — Font colour #7A7A7A kept as a role; its rendering depends on the backend stylesheet.
  [Loss] comment.dropped ×1 — Word comment dropped (CommentHandling.Ignore).
```

Measured over a corpus of 69 real Word documents (contracts, invoices, reports,
quizzes, forms, and files converted from PDF): **99.8 % weighted fidelity, 62 of
69 documents at 100 %, lowest 96.4 %**. The remaining losses are decorative VML
shapes, comments, column breaks and one SmartArt drawing.

The stronger guarantee is text preservation, which is verified rather than
asserted: `RealWorldCorpusTests` imports every `.docx` in a directory, emits
AsciiDoc, parses it back with `AdocNet.Parser`, renders it to HTML and checks
that every word of the Word document is still present. Point it at your own
corpus with:

```
ADOCNET_DOCX_CORPUS=/path/to/docs dotnet test tests/AdocNet.Importers.Docx.Tests
```

### How literal text is protected

Word text is literal; AsciiDoc source is not. The importer neutralises anything
that would be re-interpreted, and only where a substitution would actually fire
(so `2 * 3` is left alone):

- Quote pairs, attribute references, cross references, anchors, callouts and
  bare URLs are escaped with a backslash.
- Text replacements (`(C)`, `--`, `...`, arrows, apostrophes) and macro-shaped
  text get an inline passthrough instead — a backslash is not honoured there and
  would show up in the output. Since a passthrough also suppresses
  special-character encoding, a span containing `<`, `>` or `&` is wrapped only
  around a run that has none.
- Block markers at the start of a line (`* `, `= `, `//`, `:name:`, `|===`, …)
  are neutralised the same way, including lines created by a hard break.
- Table cell text escapes `|`, and a cell whose whole content is a style letter
  is wrapped, because `|a|b` is read as a styled cell, not two cells.

## Known gaps

- Per-cell alignment specifiers are not emitted: the parser reads a `>` between
  two cell separators as content, so column-wide alignment goes into the `cols`
  attribute instead. See `docs/DEFERRED-PARITY-ITEMS.md`.
- Multi-paragraph footnotes are joined into one, since a footnote macro is a
  single inline.
- Nested tables support one level, matching AsciiDoc's `!===` nesting.
