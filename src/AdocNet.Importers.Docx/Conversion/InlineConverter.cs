using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using AdocNet.Ast;

namespace AdocNet.Importers.Docx;

/// <summary>Inline content produced from one paragraph.</summary>
internal sealed class InlineResult
{
    public List<InlineNode> Inlines { get; } = new();

    /// <summary>A <c>w:br</c> without a type — an explicit line break inside the paragraph.</summary>
    public bool HasHardBreak { get; set; }

    /// <summary>A <c>w:br w:type="page"</c> was seen inside the paragraph.</summary>
    public bool HasPageBreak { get; set; }

    /// <summary>True when nothing but whitespace came out.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (var node in Inlines)
            {
                if (node is TextInlineNode text)
                {
                    if (!string.IsNullOrWhiteSpace(text.Value)) return false;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }
    }
}

/// <summary>
/// Converts paragraph-level WordprocessingML content (runs, hyperlinks,
/// bookmarks, fields, drawings, notes) into Adoc.Net inline nodes.
/// </summary>
internal sealed class InlineConverter
{
    private readonly ConversionContext _ctx;
    private readonly bool _inTableCell;
    private readonly List<List<InlineNode>> _outputs = new();
    private readonly List<FieldState> _fields = new();
    private readonly List<PendingSegment> _segments = new();
    private InlineResult _result = new();
    private string? _paragraphStyleId;
    private int _noteDepth;

    public InlineConverter(ConversionContext ctx, bool inTableCell = false, int noteDepth = 0)
    {
        _ctx = ctx;
        _inTableCell = inTableCell;
        _noteDepth = noteDepth;
    }

    /// <summary>Raw text + formatting, before adjacent same-format merging.</summary>
    private sealed class PendingSegment
    {
        public required StringBuilder Text { get; init; }
        public required RunFormat Format { get; init; }
    }

    private sealed class FieldState
    {
        public StringBuilder Instruction { get; } = new();
        public bool InResult { get; set; }
    }

    private List<InlineNode> Output => _outputs[_outputs.Count - 1];

    public InlineResult ConvertParagraph(XElement paragraph, string? paragraphStyleId)
    {
        _result = new InlineResult();
        _paragraphStyleId = paragraphStyleId;
        _outputs.Clear();
        _outputs.Add(_result.Inlines);

        foreach (var child in paragraph.Elements())
        {
            if (child.Name == Ns.W + "pPr") continue;
            ConvertElement(child);
        }

        FlushSegments();
        return _result;
    }

    // ── Element dispatch ────────────────────────────────────────────────────

    private void ConvertElement(XElement element)
    {
        var name = element.Name;

        if (name == Ns.W + "r") { ConvertRun(element); return; }
        if (name == Ns.W + "hyperlink") { ConvertHyperlink(element); return; }
        if (name == Ns.W + "bookmarkStart") { ConvertBookmark(element); return; }
        if (name == Ns.W + "fldSimple") { ConvertSimpleField(element); return; }

        if (name == Ns.W + "ins")
        {
            if (_ctx.Options.TrackedChanges == TrackedChangeHandling.Accept) ConvertChildren(element);
            else _ctx.Report.Add(DocxIssueSeverity.Info, "revision.insertion-rejected",
                "Tracked insertion dropped (TrackedChangeHandling.Reject).", _ctx.ParagraphIndex);
            return;
        }

        if (name == Ns.W + "del")
        {
            if (_ctx.Options.TrackedChanges == TrackedChangeHandling.Reject) ConvertChildren(element);
            else _ctx.Report.Add(DocxIssueSeverity.Info, "revision.deletion-accepted",
                "Tracked deletion dropped (TrackedChangeHandling.Accept).", _ctx.ParagraphIndex);
            return;
        }

        if (name == Ns.W + "sdt")
        {
            // Structured document tag (content control): the content lives in
            // w:sdtContent; the tag itself carries no visible content.
            var content = element.Element(Ns.W + "sdtContent");
            if (content is not null) ConvertChildren(content);
            return;
        }

        if (name == Ns.W + "smartTag" || name == Ns.W + "customXml"
            || name == Ns.W + "moveFrom" || name == Ns.W + "moveTo"
            || name == Ns.W + "bdo" || name == Ns.W + "dir")
        {
            ConvertChildren(element);
            return;
        }

        if (name == Ns.W + "commentRangeStart" || name == Ns.W + "commentRangeEnd"
            || name == Ns.W + "bookmarkEnd" || name == Ns.W + "proofErr"
            || name == Ns.W + "lastRenderedPageBreak")
        {
            return;
        }

        if (name == Ns.Mc + "AlternateContent")
        {
            // Prefer the mc:Choice payload; mc:Fallback is the legacy VML copy
            // of the same content, so taking both would duplicate it.
            var choice = element.Element(Ns.Mc + "Choice") ?? element.Element(Ns.Mc + "Fallback");
            if (choice is not null) ConvertChildren(choice);
            return;
        }
    }

    private void ConvertChildren(XElement element)
    {
        foreach (var child in element.Elements()) ConvertElement(child);
    }

    // ── Runs ────────────────────────────────────────────────────────────────

    private void ConvertRun(XElement run)
    {
        var rPr = run.Element(Ns.W + "rPr");
        var characterStyleId = rPr?.Element(Ns.W + "rStyle").WVal();
        var format = RunFormat.Resolve(rPr, characterStyleId, _paragraphStyleId, _ctx.Styles);

        _ctx.Report.Runs++;
        CountFormatting(format);

        foreach (var child in run.Elements())
        {
            var name = child.Name;

            if (name == Ns.W + "t") { AppendText(child.Value, format); continue; }
            if (name == Ns.W + "delText")
            {
                if (_ctx.Options.TrackedChanges == TrackedChangeHandling.Reject) AppendText(child.Value, format);
                continue;
            }

            if (name == Ns.W + "tab")
            {
                // A layout tab has no AsciiDoc equivalent inside a paragraph;
                // a single space keeps word separation without inventing
                // alignment that the renderer cannot honour.
                AppendText(" ", format);
                _ctx.Report.Approximated("tab.flattened",
                    "Layout tab replaced with a space.", _ctx.ParagraphIndex);
                continue;
            }

            if (name == Ns.W + "br")
            {
                var type = child.Attribute(Ns.W + "type")?.Value;
                if (type == "page")
                {
                    _result.HasPageBreak = true;
                    _ctx.Report.Count(mapped: true);
                }
                else if (type == "column")
                {
                    _ctx.Report.Lost("column-break.dropped",
                        "Column break has no AsciiDoc equivalent.", _ctx.ParagraphIndex);
                }
                else
                {
                    FlushSegments();
                    Output.Add(new TextInlineNode { Value = "\n" });
                    _result.HasHardBreak = true;
                    _ctx.Report.Count(mapped: true);
                }

                continue;
            }

            if (name == Ns.W + "cr") { FlushSegments(); Output.Add(new TextInlineNode { Value = "\n" }); _result.HasHardBreak = true; continue; }
            if (name == Ns.W + "noBreakHyphen") { AppendText("-", format); continue; }
            if (name == Ns.W + "softHyphen") { continue; }
            if (name == Ns.W + "sym") { AppendSymbol(child, format); continue; }
            if (name == Ns.W + "drawing") { ConvertDrawing(child, format); continue; }
            if (name == Ns.W + "pict") { ConvertVmlPicture(child); continue; }
            if (name == Ns.W + "footnoteReference") { ConvertNoteReference(child, endnote: false); continue; }
            if (name == Ns.W + "endnoteReference") { ConvertNoteReference(child, endnote: true); continue; }
            if (name == Ns.W + "commentReference") { ConvertCommentReference(child); continue; }
            if (name == Ns.W + "fldChar") { ConvertFieldChar(child); continue; }
            if (name == Ns.W + "instrText") { AppendInstruction(child.Value); continue; }
            if (name == Ns.W + "ptab") { AppendText(" ", format); continue; }

            if (name == Ns.W + "object" || name == Ns.W + "pgNum" || name == Ns.W + "footnoteRef"
                || name == Ns.W + "endnoteRef")
            {
                if (name == Ns.W + "object")
                {
                    _ctx.Report.Lost("embedded-object.dropped",
                        "Embedded OLE object dropped; AsciiDoc has no equivalent.", _ctx.ParagraphIndex);
                }

                continue;
            }

            if (name == Ns.Mc + "AlternateContent") { ConvertElement(child); continue; }
        }
    }

    private void CountFormatting(RunFormat format)
    {
        // Every formatting toggle is a content unit: the ones AsciiDoc can
        // express count as mapped, the ones it cannot count against fidelity.
        if (format.Bold) _ctx.Report.Count(mapped: true);
        if (format.Italic) _ctx.Report.Count(mapped: true);
        if (format.Monospace) _ctx.Report.Count(mapped: true);
        if (format.Highlighted) _ctx.Report.Count(mapped: true);
        if (format.VerticalAlign != RunVerticalAlign.Baseline) _ctx.Report.Count(mapped: true);

        if (format.Underline || format.Strike || format.SmallCaps || format.AllCaps)
        {
            if (_ctx.Options.PreserveFormattingAsRoles)
            {
                _ctx.Report.Count(mapped: true);
            }
            else
            {
                _ctx.Report.Lost("run.decoration-dropped",
                    "Underline/strikethrough/caps dropped (PreserveFormattingAsRoles is off).", _ctx.ParagraphIndex);
            }
        }

        if (format.Color is not null)
        {
            _ctx.Report.Lost("run.color-dropped",
                $"Font colour #{format.Color} has no AsciiDoc equivalent.", _ctx.ParagraphIndex);
        }
    }

    private void AppendText(string text, RunFormat format)
    {
        if (text.Length == 0) return;

        var last = _segments.Count > 0 ? _segments[_segments.Count - 1] : null;
        if (last is not null && FormatEquals(last.Format, format))
        {
            last.Text.Append(text);
            return;
        }

        _segments.Add(new PendingSegment { Text = new StringBuilder(text), Format = format });
    }

    private void AppendSymbol(XElement sym, RunFormat format)
    {
        var charCode = sym.Attribute(Ns.W + "char")?.Value;
        if (charCode is null) return;
        if (!int.TryParse(charCode, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code)) return;

        // Symbol/Wingdings characters live in the private use area; map the
        // handful that carry meaning and fall back to the raw code point.
        var mapped = code switch
        {
            0xF0B7 or 0xF061 => "•", // bullet
            0xF0A7 => "▪",           // small black square
            0xF0FC or 0xF0FE => "✔", // check mark
            0xF0E0 => "→",           // right arrow
            0xF0AE => "→",
            _ => code >= 0xF000 && code <= 0xF0FF
                ? ((char)(code - 0xF000)).ToString()
                : char.ConvertFromUtf32(code),
        };

        AppendText(mapped, format);
    }

    private static bool FormatEquals(RunFormat a, RunFormat b)
        => a.Bold == b.Bold && a.Italic == b.Italic && a.Monospace == b.Monospace
           && a.Underline == b.Underline && a.Strike == b.Strike
           && a.SmallCaps == b.SmallCaps && a.AllCaps == b.AllCaps
           && a.Highlighted == b.Highlighted && a.VerticalAlign == b.VerticalAlign
           && string.Equals(a.Color, b.Color, StringComparison.Ordinal);

    /// <summary>
    /// Materialises buffered text segments into inline nodes. Escaping happens
    /// here, on the merged text of adjacent same-format runs, so that markup
    /// characters Word split across runs are still seen as one string.
    /// </summary>
    private void FlushSegments()
    {
        if (_segments.Count == 0) return;

        foreach (var segment in _segments)
        {
            var text = segment.Text.ToString();
            if (text.Length == 0) continue;
            Output.Add(BuildFormatted(text, segment.Format));
        }

        _segments.Clear();
    }

    private InlineNode BuildFormatted(string text, RunFormat format)
    {
        if (format.VerticalAlign == RunVerticalAlign.Superscript)
            return new SuperscriptInlineNode { Content = AsciidocText.EscapeInline(text, _inTableCell) };
        if (format.VerticalAlign == RunVerticalAlign.Subscript)
            return new SubscriptInlineNode { Content = AsciidocText.EscapeInline(text, _inTableCell) };

        // Monospace content is not subject to further substitutions in
        // AsciiDoc, so it needs no escaping beyond the cell separator.
        var escaped = format.Monospace
            ? (_inTableCell ? text.Replace("|", "\\|") : text)
            : AsciidocText.EscapeInline(text, _inTableCell);

        InlineNode node = new TextInlineNode { Value = escaped };

        if (format.Monospace) node = new MonospaceInlineNode { Children = new List<InlineNode> { node } };
        if (format.Italic) node = new EmphasisInlineNode { Children = new List<InlineNode> { node } };
        if (format.Bold) node = new StrongInlineNode { Children = new List<InlineNode> { node } };

        var roles = DecorationRoles(format);
        if (format.Highlighted)
        {
            node = new HighlightInlineNode { Children = new List<InlineNode> { node }, Roles = roles };
        }
        else if (roles is not null)
        {
            // Roles need a carrier; the unstyled mark span `[.role]#text#` is
            // the AsciiDoc idiom for "text with a role and no other semantics".
            node = new HighlightInlineNode { Children = new List<InlineNode> { node }, Roles = roles };
        }

        return node;
    }

    private List<string>? DecorationRoles(RunFormat format)
    {
        if (!_ctx.Options.PreserveFormattingAsRoles) return null;

        List<string>? roles = null;
        if (format.Underline) (roles ??= new List<string>()).Add("underline");
        if (format.Strike) (roles ??= new List<string>()).Add("line-through");
        if (format.SmallCaps) (roles ??= new List<string>()).Add("small-caps");
        if (format.AllCaps) (roles ??= new List<string>()).Add("uppercase");
        return roles;
    }

    // ── Hyperlinks, bookmarks, notes ────────────────────────────────────────

    private void ConvertHyperlink(XElement hyperlink)
    {
        var relationshipId = hyperlink.Attribute(Ns.R + "id")?.Value;
        var anchor = hyperlink.Attribute(Ns.W + "anchor")?.Value;

        var label = ConvertNested(hyperlink);
        var labelMarkup = InlineMarkupWriter.Write(label).Trim();
        var labelText = InlineMarkupWriter.PlainText(label).Trim();

        _ctx.Report.Hyperlinks++;

        if (relationshipId is not null)
        {
            var url = _ctx.ResolveHyperlink(relationshipId);
            if (url is null)
            {
                Output.AddRange(label);
                return;
            }

            if (anchor is not null) url += "#" + anchor;

            _ctx.Report.Count(mapped: true);
            if (labelMarkup.Length == 0 || string.Equals(labelText, url, StringComparison.Ordinal))
                Output.Add(new LinkInlineNode { Url = url });
            else
                Output.Add(new InlineLinkMacroNode { Url = url, Label = labelMarkup });
            return;
        }

        if (anchor is not null)
        {
            _ctx.Report.Count(mapped: true);
            var target = AsciidocText.ToId(anchor);
            Output.Add(new CrossReferenceInlineNode
            {
                Target = target,
                Label = labelText.Length > 0 ? labelText : null,
            });
            return;
        }

        Output.AddRange(label);
    }

    private void ConvertBookmark(XElement bookmark)
    {
        var name = bookmark.Attribute(Ns.W + "name")?.Value;
        if (name is null || name == "_GoBack") return;

        FlushSegments();
        _ctx.Report.Bookmarks++;
        _ctx.Report.Count(mapped: true);
        Output.Add(new InlineAnchorNode { Id = _ctx.ReserveId(AsciidocText.ToId(name)) });
    }

    private void ConvertNoteReference(XElement reference, bool endnote)
    {
        var id = reference.Attribute(Ns.W + "id")?.Value;
        if (id is null) return;

        FlushSegments();
        _ctx.Report.Footnotes++;

        var store = endnote ? _ctx.Endnotes : _ctx.Footnotes;
        if (!store.TryGetValue(id, out var note))
        {
            _ctx.Report.Lost(endnote ? "endnote.missing" : "footnote.missing",
                $"Note {id} is referenced but its body is absent.", _ctx.ParagraphIndex);
            return;
        }

        if (_noteDepth > 0)
        {
            // AsciiDoc footnotes do not nest; keep the text inline instead of
            // producing an unparseable nested macro.
            _ctx.Report.Approximated("footnote.nested-flattened",
                "Nested note flattened into the outer note's text.", _ctx.ParagraphIndex);
            Output.AddRange(NoteInlines(note));
            return;
        }

        _ctx.Report.Count(mapped: true);
        if (endnote)
        {
            _ctx.Report.Add(DocxIssueSeverity.Info, "endnote.mapped-as-footnote",
                "Endnote imported as an AsciiDoc footnote.", _ctx.ParagraphIndex);
        }

        Output.Add(new FootnoteInlineNode { Inlines = NoteInlines(note) });
    }

    private List<InlineNode> NoteInlines(XElement note)
    {
        var inlines = new List<InlineNode>();
        var first = true;
        foreach (var paragraph in note.Elements(Ns.W + "p"))
        {
            var converter = new InlineConverter(_ctx, _inTableCell, _noteDepth + 1);
            var styleId = paragraph.Element(Ns.W + "pPr")?.Element(Ns.W + "pStyle").WVal();
            var result = converter.ConvertParagraph(paragraph, styleId);
            if (result.IsEmpty) continue;

            if (!first)
            {
                // A footnote macro is a single inline; multi-paragraph note
                // bodies join with a space.
                inlines.Add(new TextInlineNode { Value = " " });
                _ctx.Report.Approximated("footnote.multi-paragraph-joined",
                    "Multi-paragraph note joined into a single footnote.", _ctx.ParagraphIndex);
            }

            inlines.AddRange(result.Inlines);
            first = false;
        }

        // A note body opens with the reference mark and often a space; trim the
        // leading whitespace that leaves behind.
        if (inlines.Count > 0 && inlines[0] is TextInlineNode lead)
        {
            var trimmed = lead.Value.TrimStart();
            inlines[0] = new TextInlineNode { Value = trimmed };
        }

        return inlines;
    }

    private void ConvertCommentReference(XElement reference)
    {
        var id = reference.Attribute(Ns.W + "id")?.Value;
        if (id is null) return;

        if (_ctx.Options.Comments == CommentHandling.Ignore)
        {
            _ctx.Report.Lost("comment.dropped", "Word comment dropped (CommentHandling.Ignore).", _ctx.ParagraphIndex);
            return;
        }

        if (!_ctx.Comments.TryGetValue(id, out var comment))
        {
            _ctx.Report.Lost("comment.missing", $"Comment {id} is referenced but its body is absent.", _ctx.ParagraphIndex);
            return;
        }

        // Line comments are block-level in AsciiDoc; the block converter picks
        // these up from the report-free side channel below.
        _ctx.Report.Count(mapped: true);
        PendingComments.Add(CommentText(comment));
    }

    /// <summary>Comment texts collected while converting the current paragraph.</summary>
    public List<string> PendingComments { get; } = new();

    private string CommentText(XElement comment)
    {
        var sb = new StringBuilder();
        foreach (var text in comment.Descendants(Ns.W + "t"))
        {
            sb.Append(text.Value);
        }

        return sb.ToString().Replace("\r", " ").Replace("\n", " ");
    }

    // ── Fields ──────────────────────────────────────────────────────────────

    private void ConvertFieldChar(XElement fldChar)
    {
        var type = fldChar.Attribute(Ns.W + "fldCharType")?.Value;
        switch (type)
        {
            case "begin":
                FlushSegments();
                _fields.Add(new FieldState());
                _outputs.Add(new List<InlineNode>());
                break;

            case "separate":
                if (_fields.Count > 0)
                {
                    FlushSegments();
                    _fields[_fields.Count - 1].InResult = true;
                    // Anything before the separator is the instruction echo;
                    // the result starts fresh.
                    Output.Clear();
                }

                break;

            case "end":
                if (_fields.Count > 0)
                {
                    FlushSegments();
                    var result = _outputs[_outputs.Count - 1];
                    _outputs.RemoveAt(_outputs.Count - 1);
                    var field = _fields[_fields.Count - 1];
                    _fields.RemoveAt(_fields.Count - 1);
                    MaterialiseField(field.Instruction.ToString(), result);
                }

                break;
        }
    }

    private void AppendInstruction(string text)
    {
        if (_fields.Count == 0) return;
        _fields[_fields.Count - 1].Instruction.Append(text);
    }

    private void ConvertSimpleField(XElement fldSimple)
    {
        var instruction = fldSimple.Attribute(Ns.W + "instr")?.Value ?? string.Empty;
        var result = ConvertNested(fldSimple);
        MaterialiseField(instruction, result);
    }

    /// <summary>
    /// Turns a field into inline nodes. Fields whose meaning survives the
    /// conversion (hyperlinks, references) become the equivalent AsciiDoc
    /// construct; the rest contribute their cached result text, which is what
    /// a reader of the Word document sees.
    /// </summary>
    private void MaterialiseField(string instruction, List<InlineNode> result)
    {
        var trimmed = instruction.Trim();
        var keyword = FieldKeyword(trimmed);

        switch (keyword)
        {
            case "HYPERLINK":
            {
                var url = FieldQuotedArgument(trimmed);
                var anchor = FieldSwitchArgument(trimmed, "\\l");
                if (url is null && anchor is not null)
                {
                    _ctx.Report.Count(mapped: true);
                    Output.Add(new CrossReferenceInlineNode
                    {
                        Target = AsciidocText.ToId(anchor),
                        Label = NullIfEmpty(InlineMarkupWriter.PlainText(result).Trim()),
                    });
                    return;
                }

                if (url is null) break;
                if (anchor is not null) url += "#" + anchor;

                _ctx.Report.Hyperlinks++;
                _ctx.Report.Count(mapped: true);
                var label = InlineMarkupWriter.Write(result).Trim();
                if (label.Length == 0 || string.Equals(label, url, StringComparison.Ordinal))
                    Output.Add(new LinkInlineNode { Url = url });
                else
                    Output.Add(new InlineLinkMacroNode { Url = url, Label = label });
                return;
            }

            case "REF":
            {
                var target = FieldFirstArgument(trimmed);
                if (target is null) break;
                _ctx.Report.Count(mapped: true);
                Output.Add(new CrossReferenceInlineNode
                {
                    Target = AsciidocText.ToId(target),
                    Label = NullIfEmpty(InlineMarkupWriter.PlainText(result).Trim()),
                });
                return;
            }

            case "TOC":
                _ctx.SawTableOfContents = true;
                _ctx.Report.Count(mapped: _ctx.Options.ConvertTocFieldToAttribute);
                if (!_ctx.Options.ConvertTocFieldToAttribute)
                {
                    _ctx.Report.Add(DocxIssueSeverity.Info, "toc.dropped",
                        "Table-of-contents field dropped.", _ctx.ParagraphIndex);
                }

                // The cached result is a snapshot of the generated TOC; a
                // renderer regenerates it, so keeping it would duplicate.
                return;

            case "PAGEREF":
            case "PAGE":
            case "NUMPAGES":
                _ctx.Report.Lost("field.page-reference-dropped",
                    $"Field '{keyword}' refers to page geometry, which AsciiDoc does not model.", _ctx.ParagraphIndex);
                Output.AddRange(result);
                return;

            case "SEQ":
            case "STYLEREF":
            case "DATE":
            case "TIME":
            case "AUTHOR":
            case "TITLE":
            case "FILENAME":
            case "DOCPROPERTY":
            case "MERGEFIELD":
            case "INCLUDEPICTURE":
                _ctx.Report.Approximated("field.result-kept",
                    $"Field '{keyword}' replaced by its last computed value.", _ctx.ParagraphIndex);
                Output.AddRange(result);
                return;
        }

        if (result.Count > 0)
        {
            _ctx.Report.Approximated("field.result-kept",
                $"Field '{keyword ?? "unknown"}' replaced by its last computed value.", _ctx.ParagraphIndex);
        }

        Output.AddRange(result);
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static string? FieldKeyword(string instruction)
    {
        var space = instruction.IndexOf(' ');
        var keyword = space < 0 ? instruction : instruction.Substring(0, space);
        return keyword.Length == 0 ? null : keyword.ToUpperInvariant();
    }

    /// <summary>First argument of a field instruction, quoted or bare.</summary>
    private static string? FieldFirstArgument(string instruction)
    {
        var quoted = FieldQuotedArgument(instruction);
        if (quoted is not null) return quoted;

        var parts = instruction.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && !parts[1].StartsWith("\\", StringComparison.Ordinal) ? parts[1] : null;
    }

    private static string? FieldQuotedArgument(string instruction)
    {
        var open = instruction.IndexOf('"');
        if (open < 0) return null;
        var close = instruction.IndexOf('"', open + 1);
        return close < 0 ? null : instruction.Substring(open + 1, close - open - 1);
    }

    private static string? FieldSwitchArgument(string instruction, string switchName)
    {
        var index = instruction.IndexOf(switchName, StringComparison.Ordinal);
        if (index < 0) return null;

        var rest = instruction.Substring(index + switchName.Length).Trim();
        if (rest.Length == 0) return null;
        if (rest[0] == '"')
        {
            var close = rest.IndexOf('"', 1);
            return close < 0 ? null : rest.Substring(1, close - 1);
        }

        var space = rest.IndexOf(' ');
        return space < 0 ? rest : rest.Substring(0, space);
    }

    // ── Drawings ────────────────────────────────────────────────────────────

    private void ConvertDrawing(XElement drawing, RunFormat format)
    {
        var anchor = drawing.Element(Ns.Wp + "anchor");
        var container = drawing.Element(Ns.Wp + "inline") ?? anchor;
        if (container is null) return;

        if (anchor is not null)
        {
            _ctx.Report.Approximated("image.floating-position-lost",
                "Floating image imported as an inline/block image; wrapping and position are lost.", _ctx.ParagraphIndex);
        }

        var blip = FirstDescendant(container, Ns.A + "blip");
        var relationshipId = blip?.Attribute(Ns.R + "embed")?.Value ?? blip?.Attribute(Ns.R + "link")?.Value;
        if (relationshipId is null)
        {
            _ctx.Report.Lost("drawing.unsupported",
                "Drawing without an image part (chart, SmartArt or shape) dropped.", _ctx.ParagraphIndex);
            return;
        }

        var target = _ctx.RegisterImage(relationshipId);
        if (target is null) return;

        var docPr = FirstDescendant(container, Ns.Wp + "docPr");
        var alt = docPr?.Attribute("descr")?.Value ?? docPr?.Attribute("name")?.Value ?? string.Empty;

        var extent = container.Element(Ns.Wp + "extent");
        var width = EmuToPixels(extent?.Attribute("cx")?.Value);
        var height = EmuToPixels(extent?.Attribute("cy")?.Value);

        FlushSegments();
        _ctx.Report.Images++;
        _ctx.Report.Count(mapped: true);
        Output.Add(new InlineImageNode
        {
            Target = target,
            Alt = alt,
            Width = width,
            Height = height,
        });
    }

    private void ConvertVmlPicture(XElement pict)
    {
        var imageData = FirstDescendant(pict, Ns.V + "imagedata");
        var relationshipId = imageData?.Attribute(Ns.R + "id")?.Value;
        if (relationshipId is null)
        {
            _ctx.Report.Lost("vml-shape.dropped", "VML shape without image data dropped.", _ctx.ParagraphIndex);
            return;
        }

        var target = _ctx.RegisterImage(relationshipId);
        if (target is null) return;

        FlushSegments();
        _ctx.Report.Images++;
        _ctx.Report.Count(mapped: true);
        Output.Add(new InlineImageNode
        {
            Target = target,
            Alt = imageData?.Attribute(Ns.V + "title")?.Value ?? string.Empty,
        });
    }

    private static XElement? FirstDescendant(XElement root, XName name)
    {
        foreach (var element in root.Descendants(name)) return element;
        return null;
    }

    /// <summary>
    /// English Metric Units to CSS pixels (914400 EMU per inch, 96 px per
    /// inch). Returns null for absent or zero extents so the macro omits the
    /// attribute rather than emitting <c>0</c>.
    /// </summary>
    internal static string? EmuToPixels(string? emu)
    {
        if (emu is null) return null;
        if (!long.TryParse(emu, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= 0) return null;
        var pixels = (int)Math.Round(value / 9525.0);
        return pixels <= 0 ? null : pixels.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Converts a container's children in a nested output buffer.</summary>
    private List<InlineNode> ConvertNested(XElement container)
    {
        FlushSegments();
        _outputs.Add(new List<InlineNode>());
        foreach (var child in container.Elements())
        {
            if (child.Name == Ns.W + "pPr") continue;
            ConvertElement(child);
        }

        FlushSegments();
        var nested = _outputs[_outputs.Count - 1];
        _outputs.RemoveAt(_outputs.Count - 1);
        return nested;
    }
}
