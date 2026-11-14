using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace AdocNet.Importers.Html;

/// <summary>
/// Converts the semantic subset of an HTML DOM to AsciiDoc blocks: headings, paragraphs, inline
/// formatting, code, quotes, lists (task lists, nesting), description lists, tables (spans,
/// alignment), links, images (data URIs become media), footnotes (Markdig/pandoc layout) and
/// raw-HTML passthrough blocks. Anything else is reported.
/// </summary>
internal sealed class HtmlConverter
{
    /// <summary>Attribute marking a raw HTML block (from Markdown) to pass through as <c>++++</c>.</summary>
    internal const string PassthroughAttribute = "data-adocnet-passthrough";

    private static readonly HashSet<string> InlineTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "abbr", "b", "bdi", "bdo", "big", "br", "cite", "code", "data", "del", "dfn", "em", "font", "i", "img",
        "input", "ins", "kbd", "label", "mark", "q", "s", "samp", "small", "span", "strike", "strong", "sub", "sup",
        "time", "tt", "u", "var", "wbr",
    };

    private static readonly HashSet<string> SilentlyDropped = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template", "link", "meta", "head", "title", "base",
    };

    private static readonly HashSet<string> DroppedContent = new(StringComparer.OrdinalIgnoreCase)
    {
        "iframe", "form", "button", "select", "textarea", "canvas", "svg", "math", "object", "embed", "map", "dialog",
    };

    private readonly AsciiDocBuilder _b;
    private readonly ImportOptions _options;
    private readonly Dictionary<string, IElement> _footnotes = new(StringComparer.Ordinal);
    private readonly HashSet<IElement> _footnoteContainers = [];
    private readonly HashSet<string> _linkTargets = new(StringComparer.Ordinal);
    private IElement? _promotedHeading;
    private int _headingShift;
    private int _lastSectionLevel;

    private HtmlConverter(AsciiDocBuilder builder, ImportOptions options)
    {
        _b = builder;
        _options = options;
    }

    private ImportReport R => _b.Report;

    /// <summary>Converts the children of <paramref name="root"/> (usually <c>body</c>) into blocks; may set the builder's title.</summary>
    public static List<string> Convert(IElement root, AsciiDocBuilder builder, ImportOptions options)
    {
        var converter = new HtmlConverter(builder, options);
        converter.Prepare(root);
        return converter.Blocks(root, 1);
    }

    private void Prepare(IElement root)
    {
        foreach (var container in root.QuerySelectorAll("div.footnotes, section.footnotes, aside.footnotes, [role=doc-endnotes]"))
        {
            _footnoteContainers.Add(container);
            foreach (var li in container.QuerySelectorAll("li[id]"))
                _footnotes[li.Id!] = li;
        }
        foreach (var a in root.QuerySelectorAll("a[href^='#']"))
            _linkTargets.Add(a.GetAttribute("href")!.Substring(1));

        var headings = root.QuerySelectorAll("h1, h2, h3, h4, h5, h6")
            .Where(h => !_footnoteContainers.Any(c => c.Contains(h))).ToList();
        if (headings.Count == 0) return;
        int top = headings.Min(Level);
        var first = headings[0];
        if (_options.PromoteFirstHeadingToTitle && _b.Title is null && top == 1 && Level(first) == top
            && headings.Count(h => Level(h) == top) == 1 && !HasTextBefore(root, first))
        {
            _promotedHeading = first;
            _b.Title = Normalize(first.TextContent).Trim();
            _headingShift = top;
        }
        else
        {
            _headingShift = top - 1;
        }
    }

    private static int Level(IElement heading) => heading.LocalName[1] - '0';

    private static bool HasTextBefore(IElement root, IElement target)
    {
        foreach (var node in root.Descendants())
        {
            if (node == target) return false;
            if (node is IText text && !string.IsNullOrWhiteSpace(text.Data) && !InsideIgnored(text)) return true;
            if (node is IElement { LocalName: "img" }) return true;
        }
        return false;
    }

    private static bool InsideIgnored(INode node)
    {
        for (var p = node.ParentElement; p is not null; p = p.ParentElement)
            if (SilentlyDropped.Contains(p.LocalName)) return true;
        return false;
    }

    // ── Blocks ──────────────────────────────────────────────────────────

    private List<string> Blocks(INode parent, int listDepth)
    {
        var blocks = new List<string>();
        var run = new List<INode>();
        void Flush()
        {
            if (run.Count > 0 && Paragraph(run, null) is { } p) blocks.Add(p);
            run.Clear();
        }
        foreach (var child in parent.ChildNodes)
        {
            if (child is IText || child is IElement e && IsInline(e))
            {
                run.Add(child);
                continue;
            }
            if (child is not IElement element) continue; // comments, processing instructions
            Flush();
            blocks.AddRange(Block(element, listDepth));
        }
        Flush();
        return blocks;
    }

    private static bool IsInline(IElement e) =>
        InlineTags.Contains(e.LocalName) && !(e.LocalName == "a" && e.Children.Any(c => !IsInline(c)));

    private IEnumerable<string> Block(IElement e, int listDepth)
    {
        if (_footnoteContainers.Contains(e) || e == _promotedHeading) return [];
        var name = e.LocalName;
        if (SilentlyDropped.Contains(name))
        {
            if (name is "script" or "style" or "noscript")
                R.Add(ImportIssueSeverity.Info, $"html.{name}.dropped", $"<{name}> content is not document content and was dropped.");
            return [];
        }
        if (DroppedContent.Contains(name))
        {
            R.Lost($"html.{name}.dropped", $"<{name}> has no AsciiDoc equivalent and was dropped.");
            return [];
        }

        string? block = name switch
        {
            "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => Heading(e),
            "p" => Paragraph(e.ChildNodes, e),
            "pre" => Pre(e),
            "blockquote" => Quote(e, listDepth),
            "ul" or "ol" => List(e, 1),
            "dl" => DescriptionList(e, 1),
            "table" => Table(e),
            "hr" => Mapped("'''"),
            "figure" => Figure(e, listDepth),
            "img" => BlockImage(e, null),
            "video" or "audio" => Media(e),
            "details" => Details(e, listDepth),
            _ => null,
        };
        if (block is not null) return [WithAnchor(e, block)];
        // Containers (div, section, article, main, header, footer, aside, nav, center, address, li outside a list…).
        return Blocks(e, listDepth);
    }

    private string Mapped(string block)
    {
        R.Mapped();
        return block;
    }

    private string WithAnchor(IElement e, string block) =>
        e.LocalName is not ("h1" or "h2" or "h3" or "h4" or "h5" or "h6") && e.Id is { Length: > 0 } id && _linkTargets.Contains(id) && ValidId(id)
            ? $"[[{id}]]\n{block}"
            : block;

    private static bool ValidId(string id) => Regex.IsMatch(id, @"^[A-Za-z_][\w.\-]*$");

    private string? Heading(IElement e)
    {
        int level = Math.Max(1, Level(e) - _headingShift);
        if (level > _lastSectionLevel + 1)
        {
            R.Add(ImportIssueSeverity.Info, "heading.level.adjusted", "A heading skipped a level; it was moved up to keep sections in sequence.");
            level = _lastSectionLevel + 1;
        }
        level = Math.Min(level, 5);
        _lastSectionLevel = level;
        var text = Render(Inlines(e.ChildNodes), blockStart: false).Replace(" +\n", " ").Trim();
        if (text.Length == 0) return null;
        R.Headings++;
        R.Mapped();
        var id = e.Id is { Length: > 0 } i && ValidId(i) ? $"[#{i}]\n" : "";
        return $"{id}{new string('=', level + 1)} {text}";
    }

    private string? Paragraph(IEnumerable<INode> nodes, IElement? element)
    {
        var list = nodes.ToList();
        var significant = list.Where(n => n is IElement || n is IText t && !string.IsNullOrWhiteSpace(t.Data)).ToList();
        if (significant.Count == 1 && significant[0] is IElement { LocalName: "img" } img)
            return BlockImage(img, null);
        if (significant.Count == 1 && significant[0] is IElement { LocalName: "a" } a
            && a.Children.Length == 1 && a.Children[0].LocalName == "img" && string.IsNullOrWhiteSpace(a.TextContent))
            return BlockImage(a.Children[0], a.GetAttribute("href"));

        var text = Render(Inlines(list), blockStart: true).Trim();
        text = TrimBreaks(text);
        if (text.Length == 0) return null;
        R.Paragraphs++;
        R.Mapped();
        return text;
    }

    private static string TrimBreaks(string text)
    {
        while (text.EndsWith(" +", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 2).TrimEnd();
        return text;
    }

    private string Pre(IElement e)
    {
        var raw = e.TextContent.Replace("\r\n", "\n");
        if (raw.EndsWith("\n", StringComparison.Ordinal)) raw = raw.Substring(0, raw.Length - 1);
        if (e.HasAttribute(PassthroughAttribute))
        {
            R.Approximated("html.block.passthrough", "Raw HTML was kept as a passthrough block (rendered only by HTML backends).");
            var d = AsciiDocBuilder.Delimiter('+', raw);
            return $"{d}\n{raw}\n{d}";
        }
        R.CodeBlocks++;
        R.Mapped();
        var lang = Language(e) ?? (e.QuerySelector("code") is { } code ? Language(code) : null);
        var delimiter = AsciiDocBuilder.Delimiter('-', raw);
        return (lang is null ? "" : $"[source,{lang}]\n") + $"{delimiter}\n{raw}\n{delimiter}";
    }

    private static string? Language(IElement e)
    {
        foreach (var cls in e.ClassList)
        {
            var m = Regex.Match(cls, @"^(?:language|lang|highlight-source)-([\w+#.\-]+)$");
            if (m.Success) return m.Groups[1].Value;
        }
        return e.GetAttribute("data-lang") is { Length: > 0 } dl ? dl : null;
    }

    private string? Quote(IElement e, int listDepth)
    {
        var inner = string.Join("\n\n", Blocks(e, listDepth));
        if (inner.Length == 0) return null;
        R.Mapped();
        var d = AsciiDocBuilder.Delimiter('_', inner);
        return $"{d}\n{inner}\n{d}";
    }

    private string? Figure(IElement e, int listDepth)
    {
        var caption = e.QuerySelector("figcaption");
        var images = e.QuerySelectorAll("img").ToList();
        if (images.Count == 1 && e.QuerySelector("table, pre, ul, ol") is null)
        {
            var link = images[0].ParentElement is { LocalName: "a" } a ? a.GetAttribute("href") : null;
            return BlockImage(images[0], link, caption is null ? null : Normalize(caption.TextContent).Trim());
        }
        var blocks = Blocks(e, listDepth);
        return blocks.Count == 0 ? null : string.Join("\n\n", blocks);
    }

    private string? Details(IElement e, int listDepth)
    {
        var summary = e.Children.FirstOrDefault(c => c.LocalName == "summary");
        var inner = string.Join("\n\n", Blocks(e, listDepth));
        if (inner.Length == 0 && summary is null) return null;
        R.Mapped();
        var d = AsciiDocBuilder.Delimiter('=', inner);
        var title = summary is null ? "" : "." + AsciiDocBuilder.Inline(Normalize(summary.TextContent).Trim()) + "\n";
        return $"[%collapsible]\n{title}{d}\n{inner}\n{d}";
    }

    private string? Media(IElement e)
    {
        var src = e.GetAttribute("src") ?? e.QuerySelector("source[src]")?.GetAttribute("src");
        if (string.IsNullOrEmpty(src))
        {
            R.Lost($"html.{e.LocalName}.dropped", $"<{e.LocalName}> without a source was dropped.");
            return null;
        }
        R.Mapped();
        return $"{e.LocalName}::{AsciiDocBuilder.Target(src!)}[]";
    }

    // ── Images ──────────────────────────────────────────────────────────

    private string? ImageTarget(IElement img)
    {
        var src = img.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(src))
        {
            R.Lost("image.source.missing", "An image without a source was dropped.");
            return null;
        }
        R.Images++;
        var m = Regex.Match(src!, @"^data:image/([\w.+-]+)(;[^,]*)?,(.*)$", RegexOptions.Singleline);
        if (!m.Success) return AsciiDocBuilder.Target(src!);
        try
        {
            var bytes = m.Groups[2].Value.Contains(";base64")
                ? System.Convert.FromBase64String(m.Groups[3].Value.Trim())
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(m.Groups[3].Value));
            var ext = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "jpeg" or "jpg" or "pjpeg" => "jpg",
                "svg+xml" => "svg",
                var other => Regex.Replace(other, @"[^a-z0-9]", ""),
            };
            return _b.AddMedia("data-uri", bytes, ext.Length == 0 ? "bin" : ext);
        }
        catch (FormatException)
        {
            R.Lost("image.data-uri.invalid", "An inline data-URI image could not be decoded.");
            return null;
        }
    }

    private string ImageAttributes(IElement img)
    {
        var parts = new List<string> { AsciiDocBuilder.MacroText(img.GetAttribute("alt") ?? "") };
        string? width = Dimension(img.GetAttribute("width")), height = Dimension(img.GetAttribute("height"));
        if (width is not null) parts.Add(width);
        if (height is not null) { if (width is null) parts.Add(""); parts.Add(height); }
        if (img.GetAttribute("title") is { Length: > 0 } title) parts.Add($"title=\"{title.Replace("\"", "&quot;")}\"");
        return string.Join(",", parts);
    }

    private static string? Dimension(string? value) =>
        value is not null && Regex.IsMatch(value.Trim(), @"^\d+(\.\d+)?(px)?$") ? value.Trim().Replace("px", "") : null;

    private string? BlockImage(IElement img, string? link, string? caption = null)
    {
        if (ImageTarget(img) is not { } target) return null;
        R.Mapped();
        var attrs = ImageAttributes(img);
        if (link is { Length: > 0 }) attrs += $",link={AsciiDocBuilder.Target(link)}";
        var title = caption is { Length: > 0 } ? "." + AsciiDocBuilder.Inline(caption) + "\n" : "";
        return $"{title}image::{target}[{attrs}]";
    }

    // ── Lists ───────────────────────────────────────────────────────────

    private string? List(IElement list, int depth)
    {
        bool ordered = list.LocalName == "ol";
        var marker = new string(ordered ? '.' : '*', Math.Min(depth, 5));
        var lines = new List<string>();
        if (ordered && int.TryParse(list.GetAttribute("start"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) && start != 1)
            lines.Add($"[start={start.ToString(CultureInfo.InvariantCulture)}]");
        if (ordered && list.GetAttribute("reversed") is not null)
            R.Add(ImportIssueSeverity.Info, "list.reversed", "A reversed ordered list was imported in normal order.");

        foreach (var li in list.Children.Where(c => c.LocalName == "li"))
        {
            R.ListItems++;
            R.Mapped();
            var (lead, rest) = SplitItem(li);
            var check = TaskCheckbox(li);
            var text = Render(Inlines(lead.Where(n => n != check)), blockStart: false).Trim();
            text = TrimBreaks(text);
            if (check is not null && !ordered) text = (check.HasAttribute("checked") ? "[x] " : "[ ] ") + text;
            else if (check is not null) text = (check.HasAttribute("checked") ? "☑ " : "☐ ") + text;
            lines.Add($"{marker} {(text.Length == 0 ? "{empty}" : text)}");
            foreach (var child in rest)
            {
                if (child is IElement { LocalName: "ul" or "ol" } nested)
                {
                    if (List(nested, depth + 1) is { } sub) lines.Add(sub);
                }
                else if (child is IElement { LocalName: "dl" } dl)
                {
                    if (DescriptionList(dl, 1) is { } sub) lines.Add("+\n" + sub);
                }
                else
                {
                    foreach (var block in child is IElement ce ? Block(ce, depth) : Blocks(child, depth))
                        lines.Add("+\n" + block);
                }
            }
        }
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>The item's own text (leading inline content, or its first paragraph) and the blocks after it.</summary>
    private static (List<INode> Lead, List<INode> Blocks) SplitItem(IElement li)
    {
        var lead = new List<INode>();
        var rest = new List<INode>();
        bool leadDone = false;
        foreach (var child in li.ChildNodes)
        {
            bool blank = child is IText t && string.IsNullOrWhiteSpace(t.Data);
            bool inline = child is IText || child is IElement e && IsInline(e);
            if (!leadDone && inline) { lead.Add(child); continue; }
            if (!leadDone && child is IElement { LocalName: "p" } p && lead.All(n => n is IText x && string.IsNullOrWhiteSpace(x.Data)))
            {
                lead = p.ChildNodes.ToList();
                leadDone = true;
                continue;
            }
            leadDone = true;
            if (!blank) rest.Add(child);
        }
        return (lead, rest);
    }

    private static IElement? TaskCheckbox(IElement li)
    {
        var first = li.QuerySelector("input[type=checkbox]");
        if (first is null) return null;
        // Only a checkbox that opens the item (directly or in its first paragraph) makes a task item.
        var parent = first.ParentElement;
        return parent == li || parent is { LocalName: "p" } && parent.ParentElement == li && parent == li.FirstElementChild ? first : null;
    }

    private string? DescriptionList(IElement dl, int depth)
    {
        var marker = depth switch { 1 => "::", 2 => ":::", 3 => "::::", _ => ";;" };
        var lines = new List<string>();
        foreach (var child in dl.Children)
        {
            if (child.LocalName == "dt")
            {
                R.ListItems++;
                R.Mapped();
                lines.Add(Render(Inlines(child.ChildNodes), blockStart: true).Trim() + marker);
            }
            else if (child.LocalName == "dd" && lines.Count > 0)
            {
                var (lead, rest) = SplitItem(child);
                var text = TrimBreaks(Render(Inlines(lead), blockStart: false).Trim());
                if (text.Length > 0) lines[lines.Count - 1] += " " + text;
                foreach (var node in rest)
                {
                    if (node is IElement { LocalName: "dl" } nested)
                    {
                        if (DescriptionList(nested, depth + 1) is { } sub) lines.Add(sub);
                    }
                    else
                        foreach (var block in node is IElement ce ? Block(ce, 1) : Blocks(node, 1))
                            lines.Add("+\n" + block);
                }
            }
        }
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    // ── Tables ──────────────────────────────────────────────────────────

    private string? Table(IElement table)
    {
        var rows = table.QuerySelectorAll("tr").Where(tr => tr.Closest("table") == table).ToList();
        if (rows.Count == 0) return null;
        R.Tables++;
        R.Mapped();

        // Column count with row spans carried down.
        var carry = new List<int>();
        int columns = 0;
        foreach (var tr in rows)
        {
            int col = 0, used = 0;
            foreach (var cell in tr.Children.Where(c => c.LocalName is "td" or "th"))
            {
                while (col < carry.Count && carry[col] > 0) { col++; used++; }
                int span = Span(cell, "colspan"), rspan = Span(cell, "rowspan");
                for (int i = 0; i < span; i++)
                {
                    while (carry.Count <= col) carry.Add(0);
                    carry[col] = Math.Max(carry[col], rspan);
                    col++;
                }
                used += span;
            }
            while (col < carry.Count) { if (carry[col] > 0) used++; col++; }
            columns = Math.Max(columns, used);
            for (int i = 0; i < carry.Count; i++) if (carry[i] > 0) carry[i]--;
        }
        if (columns == 0) return null;

        var headRows = rows.Where(tr => tr.ParentElement?.LocalName == "thead").ToList();
        var footRows = rows.Where(tr => tr.ParentElement?.LocalName == "tfoot").ToList();
        var bodyRows = rows.Except(headRows).Except(footRows).ToList();
        if (headRows.Count == 0 && bodyRows.Count > 1 && bodyRows[0].Children.All(c => c.LocalName == "th"))
        {
            headRows.Add(bodyRows[0]);
            bodyRows.RemoveAt(0);
        }
        if (headRows.Count > 1)
        {
            R.Add(ImportIssueSeverity.Warning, "table.header.rows", "AsciiDoc tables have one header row; the others became body rows.");
            bodyRows.InsertRange(0, headRows.Skip(1));
            headRows.RemoveRange(1, headRows.Count - 1);
        }
        if (footRows.Count > 1)
        {
            R.Add(ImportIssueSeverity.Warning, "table.footer.rows", "AsciiDoc tables have one footer row; the others became body rows.");
            bodyRows.AddRange(footRows.Take(footRows.Count - 1));
            footRows.RemoveRange(0, footRows.Count - 1);
        }

        // Column alignment from the first row that spans every column without merges.
        var align = new string?[columns];
        var alignRow = rows.FirstOrDefault(tr => tr.Children.Count(c => c.LocalName is "td" or "th") == columns
                                                  && tr.Children.All(c => Span(c, "colspan") == 1));
        if (alignRow is not null)
        {
            int i = 0;
            foreach (var cell in alignRow.Children.Where(c => c.LocalName is "td" or "th")) align[i++] = Align(cell);
        }

        var options = new List<string>();
        if (headRows.Count > 0) options.Add("header");
        if (footRows.Count > 0) options.Add("footer");
        var sb = new StringBuilder();
        if (table.QuerySelector("caption") is { } caption && caption.Closest("table") == table && Normalize(caption.TextContent).Trim() is { Length: > 0 } ct)
            sb.Append('.').Append(AsciiDocBuilder.BlockStart(AsciiDocBuilder.Inline(ct))).Append('\n');
        sb.Append("[cols=\"").Append(string.Join(",", align.Select(a => a is null ? "1" : a == "^" ? "^1" : a == ">" ? ">1" : "1")))
          .Append('"');
        if (options.Count > 0) sb.Append(",options=\"").Append(string.Join(",", options)).Append('"');
        sb.Append("]\n|===\n");

        var ordered = headRows.Concat(bodyRows).Concat(footRows).ToList();
        for (int r = 0; r < ordered.Count; r++)
        {
            int col = 0;
            foreach (var cell in ordered[r].Children.Where(c => c.LocalName is "td" or "th"))
            {
                sb.Append(Cell(cell, align.ElementAtOrDefault(col))).Append('\n');
                col += Span(cell, "colspan");
            }
            if (r < ordered.Count - 1) sb.Append('\n');
        }
        sb.Append("|===");
        return sb.ToString();
    }

    private static int Span(IElement cell, string attribute) =>
        int.TryParse(cell.GetAttribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 1 ? Math.Min(n, 1000) : 1;

    private static string? Align(IElement cell)
    {
        var a = (cell.GetAttribute("align") ?? Regex.Match(cell.GetAttribute("style") ?? "", @"text-align\s*:\s*(\w+)").Groups[1].Value).ToLowerInvariant();
        return a switch { "center" => "^", "right" => ">", _ => null };
    }

    private string Cell(IElement cell, string? columnAlign)
    {
        R.TableCells++;
        R.Mapped();
        int colspan = Span(cell, "colspan"), rowspan = Span(cell, "rowspan");
        var spec = new StringBuilder();
        if (colspan > 1) spec.Append(colspan.ToString(CultureInfo.InvariantCulture));
        if (rowspan > 1) spec.Append('.').Append(rowspan.ToString(CultureInfo.InvariantCulture));
        if (spec.Length > 0) spec.Append('+');
        var align = Align(cell);
        if (align != columnAlign) spec.Append(align ?? "<");

        bool blocky = cell.Children.Any(c => !IsInline(c) && !(c.LocalName == "p" && cell.Children.Count(x => x.LocalName == "p") == 1));
        if (cell.QuerySelector("table") is not null)
        {
            R.Approximated("table.nested", "A nested table was flattened into its cell's text.");
            return spec + "|" + AsciiDocBuilder.Inline(Normalize(cell.TextContent).Trim(), inTableCell: true);
        }
        if (blocky)
        {
            var inner = string.Join("\n\n", Blocks(cell, 1)).Replace("|", "\\|");
            return spec + "a|" + inner;
        }
        var nodes = cell.Children.Length == 1 && cell.Children[0].LocalName == "p" ? cell.Children[0].ChildNodes : cell.ChildNodes;
        var text = TrimBreaks(Render(Inlines(nodes, inTableCell: true), blockStart: false).Trim());
        return spec + "|" + text;
    }

    // ── Inlines ─────────────────────────────────────────────────────────

    private abstract class Tok;
    private sealed class Text(string value) : Tok { public string Value = value; }
    private sealed class Raw(string value) : Tok { public readonly string Value = value; }
    private sealed class Open(Mark mark) : Tok { public readonly Mark Mark = mark; public bool Constrained; }
    private sealed class Close(Open open) : Tok { public readonly Open Open = open; }
    private sealed record Mark(string Constrained, string Unconstrained, string? Role = null);

    private static readonly Mark Strong = new("*", "**"), Emphasis = new("_", "__"), Mono = new("`", "``"), Highlight = new("#", "##");

    private List<Tok> Inlines(IEnumerable<INode> nodes, bool inTableCell = false)
    {
        var toks = new List<Tok>();
        foreach (var node in nodes) Inline(node, toks, inTableCell);
        return toks;
    }

    private void Wrap(IElement e, Mark mark, List<Tok> toks, bool inTableCell)
    {
        var open = new Open(mark);
        toks.Add(open);
        foreach (var child in e.ChildNodes) Inline(child, toks, inTableCell);
        toks.Add(new Close(open));
    }

    private void Inline(INode node, List<Tok> toks, bool inTableCell)
    {
        if (node is IText text)
        {
            var value = Normalize(text.Data);
            if (value.Length > 0) toks.Add(new Text(AsciiDocBuilder.Inline(value, inTableCell)));
            return;
        }
        if (node is not IElement e) return;
        switch (e.LocalName)
        {
            case "strong" or "b": Wrap(e, Strong, toks, inTableCell); break;
            case "em" or "i" or "cite" or "dfn" or "var": Wrap(e, Emphasis, toks, inTableCell); break;
            case "mark": Wrap(e, Highlight, toks, inTableCell); break;
            case "del" or "s" or "strike": Wrap(e, new Mark("#", "##", "line-through"), toks, inTableCell); break;
            case "u" or "ins": Wrap(e, new Mark("#", "##", "underline"), toks, inTableCell); break;
            case "code" or "tt" or "kbd" or "samp": toks.Add(new Raw(Code(e.TextContent))); R.Mapped(); break;
            case "sup":
                if (FootnoteRef(e) is { } fnInSup) { toks.Add(new Raw(fnInSup)); break; }
                toks.Add(new Raw("^" + Script(e, inTableCell) + "^"));
                break;
            case "sub": toks.Add(new Raw("~" + Script(e, inTableCell) + "~")); break;
            case "br": toks.Add(new Raw(" +\n")); break;
            case "wbr": break;
            case "q": toks.Add(new Text("\"")); foreach (var c in e.ChildNodes) Inline(c, toks, inTableCell); toks.Add(new Text("\"")); break;
            case "img":
                if (ImageTarget(e) is { } target) { R.Mapped(); toks.Add(new Raw($"image:{target}[{ImageAttributes(e)}]")); }
                break;
            case "a": Link(e, toks, inTableCell); break;
            case "input":
                R.Lost("html.input.dropped", "A form control was dropped.");
                break;
            default:
                if (SilentlyDropped.Contains(e.LocalName) || DroppedContent.Contains(e.LocalName))
                {
                    if (DroppedContent.Contains(e.LocalName)) R.Lost($"html.{e.LocalName}.dropped", $"<{e.LocalName}> has no AsciiDoc equivalent and was dropped.");
                    break;
                }
                if (!IsInline(e))
                {
                    // Block content inside inline context (e.g. a <div> in a <span>): keep its text.
                    toks.Add(new Text(" "));
                    foreach (var c in e.ChildNodes) Inline(c, toks, inTableCell);
                    toks.Add(new Text(" "));
                    break;
                }
                foreach (var c in e.ChildNodes) Inline(c, toks, inTableCell);
                break;
        }
    }

    private string Script(IElement e, bool inTableCell) =>
        Render(Inlines(e.ChildNodes, inTableCell), blockStart: false).Trim().Replace(" ", "{nbsp}");

    private static string Code(string content)
    {
        content = Normalize(content);
        if (content.Length == 0) return "";
        if (!Regex.IsMatch(content, @"[*_`#^~{}\[\]<>+\\'&|.:\-=(]")) return "`" + content + "`";
        if (!content.Contains('+')) return "`+" + content + "+`";
        return "`pass:c[" + content.Replace("]", "\\]") + "]`";
    }

    private void Link(IElement a, List<Tok> toks, bool inTableCell)
    {
        var href = a.GetAttribute("href");
        if (FootnoteRef(a) is { } footnote) { toks.Add(new Raw(footnote)); return; }
        if (a.ClassList.Contains("footnote-back-ref") || a.GetAttribute("role") == "doc-backlink") return;

        if (a.Children.Length == 1 && a.Children[0].LocalName == "img" && string.IsNullOrWhiteSpace(a.TextContent) && !string.IsNullOrEmpty(href))
        {
            if (ImageTarget(a.Children[0]) is { } target)
            {
                R.Mapped();
                R.Links++;
                toks.Add(new Raw($"image:{target}[{ImageAttributes(a.Children[0])},link={AsciiDocBuilder.Target(href!)}]"));
            }
            return;
        }

        if (a.Id is { Length: > 0 } id && ValidId(id) && _linkTargets.Contains(id)) toks.Add(new Raw($"[[{id}]]"));
        var label = Render(Inlines(a.ChildNodes, inTableCell), blockStart: false).Trim();
        if (string.IsNullOrWhiteSpace(href))
        {
            toks.Add(new Raw(label));
            return;
        }
        R.Links++;
        R.Mapped();
        href = href!.Trim();
        if (href.StartsWith("#", StringComparison.Ordinal))
        {
            var target = href.Substring(1);
            toks.Add(new Raw(label.Length == 0 ? $"<<{target}>>" : $"<<{target},{label.Replace(">>", "&gt;&gt;")}>>"));
            return;
        }
        var plain = Normalize(a.TextContent).Trim();
        var labelPart = AsciiDocBuilder.MacroText(label);
        if (Regex.IsMatch(href, @"^(https?|ftp|irc)://", RegexOptions.IgnoreCase))
            toks.Add(new Raw(plain == href ? $"{AsciiDocBuilder.Target(href)}[]" : $"{AsciiDocBuilder.Target(href)}[{labelPart}]"));
        else if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            toks.Add(new Raw(plain == href.Substring(7) ? $"{AsciiDocBuilder.Target(href)}[]" : $"{AsciiDocBuilder.Target(href)}[{labelPart}]"));
        else
            toks.Add(new Raw($"link:{AsciiDocBuilder.Target(href)}[{labelPart}]"));
    }

    /// <summary>A footnote reference (Markdig, pandoc, DPUB roles) rendered as <c>footnote:[…]</c>; null otherwise.</summary>
    private string? FootnoteRef(IElement e)
    {
        var a = e.LocalName == "a" ? e : e.Children.Length == 1 && e.Children[0].LocalName == "a" && string.IsNullOrWhiteSpace(e.TextContent.Replace(e.Children[0].TextContent, "")) ? e.Children[0] : null;
        if (a?.GetAttribute("href") is not { Length: > 1 } href || href[0] != '#') return null;
        if (!_footnotes.TryGetValue(href.Substring(1), out var li)) return null;
        R.Footnotes++;
        R.Mapped();
        var nodes = li.Children.Length == 1 && li.Children[0].LocalName == "p" ? li.Children[0].ChildNodes : li.ChildNodes;
        var text = Render(Inlines(nodes), blockStart: false).Trim();
        return $"footnote:[{TrimBreaks(text).Replace("]", "\\]")}]";
    }

    // ── Rendering inline tokens ─────────────────────────────────────────

    private static string Render(List<Tok> toks, bool blockStart)
    {
        MoveSpacesOutsideMarks(toks);
        // Drop empty mark pairs.
        for (int i = toks.Count - 2; i >= 0; i--)
            if (toks[i] is Open o && toks[i + 1] is Close c && c.Open == o) { toks.RemoveAt(i + 1); toks.RemoveAt(i); }

        var sb = new StringBuilder();
        for (int i = 0; i < toks.Count; i++)
        {
            switch (toks[i])
            {
                case Text t:
                    var value = t.Value;
                    if (blockStart && (sb.Length == 0 || sb[sb.Length - 1] == '\n')) value = AsciiDocBuilder.BlockStart(value.TrimStart());
                    sb.Append(value);
                    break;
                case Raw r:
                    sb.Append(r.Value);
                    break;
                case Open o:
                    int j = toks.FindIndex(i, x => x is Close c && c.Open == o);
                    char prev = sb.Length == 0 ? '\0' : sb[sb.Length - 1];
                    char next = j + 1 < toks.Count ? toks[j + 1] switch { Text tt when tt.Value.Length > 0 => tt.Value[0], Raw rr when rr.Value.Length > 0 => rr.Value[0], _ => 'x' } : '\0';
                    o.Constrained = !Wordish(prev) && !Wordish(next);
                    if (o.Mark.Role is { } role) sb.Append("[.").Append(role).Append(']');
                    sb.Append(o.Constrained ? o.Mark.Constrained : o.Mark.Unconstrained);
                    break;
                case Close c:
                    sb.Append(c.Open.Constrained ? c.Open.Mark.Constrained : c.Open.Mark.Unconstrained);
                    break;
            }
        }
        return sb.ToString();
    }

    private static bool Wordish(char c) => c != '\0' && (char.IsLetterOrDigit(c) || c is '_' or '*' or '`' or '#' or '^' or '~' or '+' or ']' or ':' or ';' or '}' or '\\');

    private static void MoveSpacesOutsideMarks(List<Tok> toks)
    {
        for (int i = 0; i < toks.Count; i++)
        {
            if (toks[i] is Open && i + 1 < toks.Count && toks[i + 1] is Text t && t.Value.StartsWith(" ", StringComparison.Ordinal))
            {
                t.Value = t.Value.TrimStart();
                toks.Insert(i, new Text(" "));
                i++;
            }
            if (toks[i] is Close && i > 0 && toks[i - 1] is Text u && u.Value.EndsWith(" ", StringComparison.Ordinal))
            {
                u.Value = u.Value.TrimEnd();
                toks.Insert(i + 1, new Text(" "));
            }
        }
        // Collapse doubled spaces created at joins.
        for (int i = toks.Count - 1; i > 0; i--)
            if (toks[i] is Text a && toks[i - 1] is Text b && b.Value.EndsWith(" ", StringComparison.Ordinal) && a.Value.StartsWith(" ", StringComparison.Ordinal))
                a.Value = a.Value.TrimStart();
    }

    /// <summary>HTML whitespace collapsing.</summary>
    private static string Normalize(string text) => Regex.Replace(text, @"[ \t\r\n\f]+", " ");
}
