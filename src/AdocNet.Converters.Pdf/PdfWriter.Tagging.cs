using System.Text;

namespace AdocNet.Converters.Pdf;

/// <summary>
/// Tagged PDF (PDF/UA-1, ISO 14289-1): a logical structure tree whose elements own the page
/// content through marked-content identifiers (MCIDs), decoration marked as artifacts, and the
/// parent tree that maps content and annotations back to their elements.
/// </summary>
internal sealed partial class PdfWriter
{
    /// <summary>A node of the logical structure tree.</summary>
    internal sealed class StructElem(string type, StructElem? parent)
    {
        public readonly string Type = type;
        public readonly StructElem? Parent = parent;
        /// <summary>Children in reading order: <see cref="StructElem"/>, <see cref="MarkedRef"/> or <see cref="AnnotRef"/>.</summary>
        public readonly List<object> Kids = [];
        public string? Alt, ActualText, Lang;
        /// <summary>Element identifier (<c>/ID</c>), required on notes.</summary>
        public string? Id;
        /// <summary>Layout/table attributes (<c>/A</c> dictionary body), e.g. <c>/O /Table /Scope /Column</c>.</summary>
        public string? Attributes;
        public int ObjectId;
        /// <summary>False for an element begun inside an artifact: it is not part of the tree.</summary>
        public bool Attached;
    }

    /// <summary>Content on page <see cref="PageIndex"/> marked with <see cref="Mcid"/>.</summary>
    internal readonly record struct MarkedRef(int PageIndex, int Mcid);

    /// <summary>An annotation (object id known once the page is written) owned by a structure element.</summary>
    internal sealed class AnnotRef(int pageIndex)
    {
        public readonly int PageIndex = pageIndex;
        public int ObjectId;
    }

    /// <summary>True when the output is a tagged PDF (PDF/UA-1).</summary>
    internal bool Tagged { get; set; }

    /// <summary>Natural language of the document (<c>/Lang</c>), e.g. <c>en</c>.</summary>
    internal string Lang { get; set; } = "en";

    private StructElem? _structRoot;
    private StructElem? _currentElem;
    private StructElem? _markedOwner; // element whose marked-content sequence is open on this page
    private int _artifactDepth;
    private readonly List<StructElem> _pageMcidOwners = []; // index = MCID on the open page
    private readonly List<(int Key, List<StructElem> Owners)> _pageParentEntries = [];
    private readonly List<(int Key, StructElem Owner)> _annotParentEntries = [];
    private int _nextStructParentKey;

    /// <summary>The <c>Document</c> element; created on first use.</summary>
    private StructElem StructRoot => _structRoot ??= new StructElem("Document", null);

    /// <summary>The element new content and elements belong to.</summary>
    internal StructElem CurrentElement => _currentElem ?? StructRoot;

    /// <summary>Opens a structure element as a child of the current one; content written next belongs to it.</summary>
    internal StructElem BeginTag(string type, string? alt = null, string? attributes = null, string? actualText = null)
    {
        var elem = new StructElem(type, CurrentElement) { Alt = alt, Attributes = attributes, ActualText = actualText };
        if (!Tagged || _artifactDepth > 0) return elem;
        CloseMarked();
        elem.Attached = true;
        CurrentElement.Kids.Add(elem);
        _currentElem = elem;
        return elem;
    }

    /// <summary>Closes the element opened by the matching <see cref="BeginTag"/>.</summary>
    internal void EndTag(StructElem elem)
    {
        if (!Tagged || !elem.Attached) return;
        CloseMarked();
        // Tolerate unbalanced nesting (an exception unwinding several levels): close up to elem.
        for (var e = _currentElem; e is not null; e = e.Parent)
        {
            if (e != elem) continue;
            _currentElem = e.Parent == StructRoot ? null : e.Parent;
            return;
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> with its content owned by an element opened earlier (a label
    /// drawn after the content it labels: reading order is the tree's, not the drawing order).
    /// </summary>
    internal void InElement(StructElem elem, Action body)
    {
        if (!Tagged || !elem.Attached || _artifactDepth > 0) { body(); return; }
        CloseMarked();
        var saved = _currentElem;
        _currentElem = elem;
        try { body(); }
        finally { CloseMarked(); _currentElem = saved; }
    }

    /// <summary>Runs <paramref name="body"/> inside a structure element.</summary>
    internal void Tag(string type, Action body, string? alt = null, string? attributes = null)
    {
        var elem = BeginTag(type, alt, attributes);
        try { body(); }
        finally { EndTag(elem); }
    }

    /// <summary>
    /// Before painting content: makes sure it sits in a marked-content sequence owned by the current
    /// element (or inside an artifact). Content outside every element gets an implicit paragraph.
    /// </summary>
    private void MarkContent()
    {
        if (!Tagged || _currentStream is null || _artifactDepth > 0) return;
        if (_currentElem is null)
        {
            // Untagged content would fail PDF/UA; give it a paragraph of its own.
            var p = new StructElem("P", StructRoot);
            StructRoot.Kids.Add(p);
            _currentElem = p;
            OpenMarked(p);
            _currentElem = null;
            return;
        }
        if (_markedOwner == _currentElem) return;
        CloseMarked();
        OpenMarked(_currentElem);
    }

    private void OpenMarked(StructElem owner)
    {
        int mcid = _pageMcidOwners.Count;
        _pageMcidOwners.Add(owner);
        owner.Kids.Add(new MarkedRef(_currentPageNumber - 1, mcid));
        _currentStream!.Append('/').Append(owner.Type).Append(" <</MCID ").Append(mcid).Append(">> BDC\n");
        _markedOwner = owner;
    }

    /// <summary>Ends the open marked-content sequence, if any.</summary>
    private void CloseMarked()
    {
        if (_markedOwner is null || _currentStream is null) return;
        _currentStream.Append("EMC\n");
        _markedOwner = null;
    }

    /// <summary>Starts decoration (rules, backgrounds, icons) that assistive technology skips.</summary>
    internal void BeginArtifact()
    {
        if (!Tagged || _currentStream is null) return;
        if (_artifactDepth++ > 0) return;
        CloseMarked();
        _currentStream.Append("/Artifact BMC\n");
    }

    internal void EndArtifact()
    {
        if (!Tagged || _currentStream is null || _artifactDepth == 0) return;
        if (--_artifactDepth == 0) _currentStream.Append("EMC\n");
    }

    /// <summary>Runs drawing that is decoration (an artifact) in tagged output.</summary>
    internal void Artifact(Action draw)
    {
        BeginArtifact();
        try { draw(); }
        finally { EndArtifact(); }
    }

    /// <summary>
    /// True while drawing figure content (an image or SVG under a <c>Figure</c>): vector drawing is
    /// then real content, not decoration.
    /// </summary>
    internal bool DrawingFigure { get; set; }

    /// <summary>Before vector drawing or an image: decoration (artifact) unless a figure is being drawn.</summary>
    private void BeginDecoration()
    {
        if (!Tagged) return;
        if (DrawingFigure) MarkContent();
        else BeginArtifact();
    }

    private void EndDecoration()
    {
        if (Tagged && !DrawingFigure) EndArtifact();
    }

    /// <summary>Drawing commands inserted after the fact (a code block's background), as an artifact.</summary>
    private string AsArtifact(string commands) => Tagged ? "/Artifact BMC\n" + commands + "EMC\n" : commands;

    /// <summary><c>/StructParent</c> and <c>/Contents</c> entries of a deferred internal link annotation.</summary>
    private string AnnotTagging(DeferredInternalLink deferred) =>
        deferred.StructParent is { } key
            ? $"/StructParent {key} /Contents {PdfTextString(deferred.Link.Contents ?? deferred.Link.DestinationId)} "
            : "";

    /// <summary>At page end: closes marked content and records the page's MCID owners for the parent tree.</summary>
    private int? FinishPageTags()
    {
        if (!Tagged) return null;
        CloseMarked();
        if (_artifactDepth > 0) { _currentStream!.Append("EMC\n"); _artifactDepth = 0; }
        int key = _nextStructParentKey++;
        _pageParentEntries.Add((key, new List<StructElem>(_pageMcidOwners)));
        _pageMcidOwners.Clear();
        return key;
    }

    /// <summary>Registers an annotation owned by <paramref name="owner"/>; returns its <c>/StructParent</c> key.</summary>
    private int RegisterAnnotationOwner(StructElem owner, AnnotRef annot)
    {
        owner.Kids.Add(annot);
        int key = _nextStructParentKey++;
        _annotParentEntries.Add((key, owner));
        return key;
    }

    /// <summary>
    /// Writes the structure tree, role-free (standard types only), and its parent tree; returns the
    /// <c>StructTreeRoot</c> object id.
    /// </summary>
    private int WriteStructTree()
    {
        int rootId = AllocPlaceholder();
        var root = StructRoot;
        AssignIds(root);

        void AssignIds(StructElem e)
        {
            e.ObjectId = AllocPlaceholder();
            foreach (var k in e.Kids) if (k is StructElem c) AssignIds(c);
        }

        var sb = new StringBuilder();
        void Write(StructElem e, int parentId)
        {
            sb.Clear();
            sb.Append("<< /Type /StructElem /S /").Append(e.Type).Append(" /P ").Append(parentId).Append(" 0 R");
            int? page = null;
            foreach (var k in e.Kids)
                if (k is MarkedRef first) { page = first.PageIndex; break; }
            if (page is { } pg && pg < _pageObjectIds.Count) sb.Append(" /Pg ").Append(_pageObjectIds[pg]).Append(" 0 R");
            if (e.Alt is not null) sb.Append(" /Alt ").Append(PdfTextString(e.Alt));
            if (e.ActualText is not null) sb.Append(" /ActualText ").Append(PdfTextString(e.ActualText));
            if (e.Lang is not null) sb.Append(" /Lang ").Append(PdfTextString(e.Lang));
            if (e.Id is not null) sb.Append(" /ID (").Append(EscapePdfString(e.Id)).Append(')');
            if (e.Attributes is not null) sb.Append(" /A << ").Append(e.Attributes).Append(" >>");
            sb.Append(" /K [");
            foreach (var k in e.Kids)
            {
                switch (k)
                {
                    case StructElem c:
                        sb.Append(' ').Append(c.ObjectId).Append(" 0 R");
                        break;
                    case MarkedRef m when m.PageIndex < _pageObjectIds.Count:
                        if (page == m.PageIndex) sb.Append(' ').Append(m.Mcid);
                        else sb.Append(" << /Type /MCR /Pg ").Append(_pageObjectIds[m.PageIndex]).Append(" 0 R /MCID ").Append(m.Mcid).Append(" >>");
                        break;
                    case AnnotRef a when a.ObjectId > 0:
                        sb.Append(" << /Type /OBJR /Obj ").Append(a.ObjectId).Append(" 0 R");
                        if (a.PageIndex < _pageObjectIds.Count) sb.Append(" /Pg ").Append(_pageObjectIds[a.PageIndex]).Append(" 0 R");
                        sb.Append(" >>");
                        break;
                }
            }
            sb.Append(" ] >>");
            SetObject(e.ObjectId, new PdfObject(sb.ToString()));
            foreach (var k in e.Kids) if (k is StructElem c) Write(c, e.ObjectId);
        }
        Write(root, rootId);

        // Parent tree: page keys map MCIDs to their elements, annotation keys to their element.
        var nums = new StringBuilder();
        var entries = _pageParentEntries.Select(p => (p.Key, Value: "[" + string.Join(" ", p.Owners.Select(o => $"{o.ObjectId} 0 R")) + "]"))
            .Concat(_annotParentEntries.Select(a => (a.Key, Value: $"{a.Owner.ObjectId} 0 R")))
            .OrderBy(e => e.Key);
        foreach (var (key, value) in entries) nums.Append(' ').Append(key).Append(' ').Append(value);
        int parentTreeId = AllocObject(new PdfObject($"<< /Nums [{nums} ] >>"));

        // Elements with an /ID are listed in the ID tree (sorted by identifier).
        var withIds = new List<StructElem>();
        void CollectIds(StructElem e)
        {
            if (e.Id is not null) withIds.Add(e);
            foreach (var k in e.Kids) if (k is StructElem c) CollectIds(c);
        }
        CollectIds(root);
        string idTree = "";
        if (withIds.Count > 0)
        {
            var names = string.Join(" ", withIds.OrderBy(e => e.Id, StringComparer.Ordinal)
                .Select(e => $"({EscapePdfString(e.Id!)}) {e.ObjectId} 0 R"));
            idTree = $" /IDTree {AllocObject(new PdfObject($"<< /Names [{names}] >>"))} 0 R";
        }

        SetObject(rootId, new PdfObject(
            $"<< /Type /StructTreeRoot /K [{root.ObjectId} 0 R] /ParentTree {parentTreeId} 0 R /ParentTreeNextKey {_nextStructParentKey}{idTree} >>"));
        return rootId;
    }

    /// <summary>
    /// Tagged output must never show the .notdef glyph: characters the font has no glyph for are
    /// dropped when invisible (format characters such as the zero-width space AsciiDoc puts after an
    /// ellipsis) and drawn as '?' otherwise.
    /// </summary>
    private static string Showable(string text, TrueTypeFont font)
    {
        bool clean = true;
        foreach (var cp in PdfFontEmbedder.EnumerateCodePoints(text))
            if (font.GetGlyphId(cp) == 0) { clean = false; break; }
        if (clean) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var cp in PdfFontEmbedder.EnumerateCodePoints(text))
        {
            if (font.GetGlyphId(cp) != 0) sb.Append(char.ConvertFromUtf32(cp));
            else if (cp > 0xFFFF || System.Globalization.CharUnicodeInfo.GetUnicodeCategory((char)cp)
                         is not (System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.Control))
                sb.Append(font.GetGlyphId('?') != 0 ? "?" : "");
        }
        return sb.ToString();
    }

    /// <summary>XMP metadata declaring PDF/UA-1 conformance and the document title.</summary>
    private int WriteXmpMetadata(string? title)
    {
        static string Xml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        string titleXml = title is null ? "" :
            $"<dc:title><rdf:Alt><rdf:li xml:lang=\"x-default\">{Xml(title)}</rdf:li></rdf:Alt></dc:title>";
        string xmp =
            "<?xpacket begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n" +
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n" +
            "<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n" +
            "<rdf:Description rdf:about=\"\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
            "xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\" xmlns:pdfuaid=\"http://www.aiim.org/pdfua/ns/id/\">\n" +
            $"{titleXml}\n" +
            "<pdf:Producer>AdocNet PDF Renderer</pdf:Producer>\n" +
            "<pdfuaid:part>1</pdfuaid:part>\n" +
            "</rdf:Description>\n</rdf:RDF>\n</x:xmpmeta>\n<?xpacket end=\"w\"?>";
        var bytes = Encoding.UTF8.GetBytes(xmp);
        return AllocObject(new PdfObject($"<< /Type /Metadata /Subtype /XML /Length {bytes.Length} >>", bytes));
    }
}
