using System.Text;
using AdocNet.Ast;

namespace AdocNet.Converters.Html;

public sealed partial class HtmlRenderer
{
    /// <summary>
    /// Appends the HTML document prologue: DOCTYPE, &lt;html&gt;, &lt;head&gt; with optional theme CSS.
    /// </summary>
    private static void AppendDocumentPrologue(StringBuilder sb, DocumentNode document, HtmlRenderOptions options)
    {
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"en\">\n");
        sb.Append("<head>\n");
        sb.Append("<meta charset=\"UTF-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n");

        // Title: explicit option > document title > "Untitled"
        var title = options.Title ?? document.Title ?? "Untitled";
        sb.Append("<title>");
        EscapeTo(sb, title);
        sb.Append("</title>\n");

        // CSS: determine source and delivery mechanism
        AppendCssBlock(sb, document, options);

        // Font Awesome CSS when icons=font
        if (document.Attributes.TryGetValue("icons", out var iconsVal)
            && string.Equals(iconsVal, "font", StringComparison.OrdinalIgnoreCase))
        {
            var cdnUrl = document.Attributes.TryGetValue("iconfont-cdn", out var customCdn)
                ? customCdn
                : "https://cdnjs.cloudflare.com/ajax/libs/font-awesome/4.7.0/css/font-awesome.min.css";
            sb.Append("<link rel=\"stylesheet\" href=\"");
            EscapeTo(sb, cdnUrl);
            sb.Append("\">\n");
        }

        // MathJax script when :stem: attribute is set
        if (document.Attributes.ContainsKey("stem"))
        {
            var stemType = document.Attributes.TryGetValue("stem", out var sv) && sv.Length > 0
                ? sv : "latexmath";
            if (string.Equals(stemType, "asciimath", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("<script>\nMathJax = {\n  loader: {load: ['input/asciimath']},\n  asciimath: {delimiters: [['\\\\$','\\\\$']]}\n};\n</script>\n");
            }
            sb.Append("<script src=\"https://cdn.jsdelivr.net/npm/mathjax@3/es5/tex-mml-chtml.js\"></script>\n");
        }

        // Google Fonts when :webfonts: is set
        if (document.Attributes.TryGetValue("webfonts", out var webfontsUrl))
        {
            const string defaultFontUrl = "https://fonts.googleapis.com/css?family=Open+Sans:300,300italic,400,400italic,600,600italic%7CNoto+Serif:400,400italic,700,700italic%7CDroid+Sans+Mono:400,700";
            var fontUrl = webfontsUrl.Length > 0 ? webfontsUrl : defaultFontUrl;
            sb.Append("<link rel=\"stylesheet\" href=\"");
            EscapeTo(sb, fontUrl);
            sb.Append("\">\n");
        }

        if (options.ExtraHead is not null)
            sb.Append(options.ExtraHead).Append('\n');

        // Docinfo header injection
        var docinfoHead = DocinfoHelper.ReadHeaderDocinfo(document.Attributes, options.BaseDirectory);
        if (docinfoHead is not null)
            sb.Append(docinfoHead).Append('\n');

        sb.Append("</head>\n");
        sb.Append("<body>\n");
    }

    /// <summary>
    /// Appends the HTML document epilogue: &lt;/body&gt;&lt;/html&gt;.
    /// </summary>
    private static void AppendDocumentEpilogue(StringBuilder sb, DocumentNode document, HtmlRenderOptions? options)
    {
        // Footer div (unless suppressed by :nofooter:)
        if (!document.Attributes.ContainsKey("nofooter"))
        {
            sb.Append("<div id=\"footer\">\n");
            sb.Append("<div id=\"footer-text\">\n");
            // When :reproducible: is set, suppress timestamps and update labels
            if (!document.Attributes.ContainsKey("reproducible"))
            {
                var lastUpdateLabel = document.Attributes.TryGetValue("last-update-label", out var lul)
                    ? lul : "Last updated";
                sb.Append(lastUpdateLabel);
                sb.Append('\n');
            }
            sb.Append("</div>\n");
            sb.Append("</div>\n");
        }

        // Docinfo footer injection
        var docinfoFooter = DocinfoHelper.ReadFooterDocinfo(
            document.Attributes, options?.BaseDirectory);
        if (docinfoFooter is not null)
            sb.Append(docinfoFooter).Append('\n');

        sb.Append("</body>\n");
        sb.Append("</html>\n");
    }

    /// <summary>
    /// Builds a map from anchor IDs to section/block titles for cross-reference resolution.
    /// </summary>
    private Dictionary<string, string> BuildIdTitleMap(DocumentNode document)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectTitles(document, map);
        return map;
    }

    /// <summary>
    /// Builds a reverse map from section/block titles to anchor IDs for natural cross-reference resolution.
    /// </summary>
    private static Dictionary<string, string> BuildTitleIdMap(Dictionary<string, string> idTitles)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, title) in idTitles)
            map.TryAdd(title, id);
        return map;
    }

    private static void CollectTitles(AstNode node, Dictionary<string, string> map)
    {
        if (node is SectionNode section && section.Id is not null)
            map.TryAdd(section.Id, section.Reftext ?? section.Title);
        else if (node is BlockNode block && block.Id is not null)
        {
            // Reftext from [[id,reftext]] takes priority over inferred titles
            if (block.Reftext is not null)
                map.TryAdd(block.Id, block.Reftext);
            else if (node is DelimitedBlockNode db && db.Title is not null)
                map.TryAdd(block.Id, db.Title);
            else if (node is BlockImageNode img)
                map.TryAdd(block.Id, img.Alt);
        }

        foreach (var child in node.Children)
        {
            // Collect reftext from inline anchors: [[id,reftext]] inside flowing text
            if (child is InlineAnchorNode anchor && anchor.Reftext is not null)
                map.TryAdd(anchor.Id, anchor.Reftext);
            CollectTitles(child, map);
        }

        // Also walk inline content (e.g., ParagraphNode.Inlines, SectionNode.TitleInlines)
        // where [[id,reftext]] and anchor:id[reftext] nodes live.
        if (node is ParagraphNode para)
        {
            foreach (var inline in para.Inlines)
            {
                if (inline is InlineAnchorNode inlineAnchor && inlineAnchor.Reftext is not null)
                    map.TryAdd(inlineAnchor.Id, inlineAnchor.Reftext);
            }
        }
    }

    /// <summary>
    /// Emits CSS as either an inline <c>&lt;style&gt;</c> block or a <c>&lt;link&gt;</c> tag,
    /// depending on document attributes <c>:linkcss:</c>, <c>:stylesheet:</c>, and <c>:stylesdir:</c>.
    /// API-level <see cref="HtmlRenderOptions.CustomCss"/> always takes precedence.
    /// </summary>
    private static void AppendCssBlock(StringBuilder sb, DocumentNode document, HtmlRenderOptions options)
    {
        var attrs = document.Attributes;
        var themeCss = HtmlThemeCss.GetCss(options.Theme);
        bool hasStylesheetAttr = attrs.TryGetValue("stylesheet", out var stylesheetVal);
        bool useLink = attrs.ContainsKey("linkcss");

        // Precedence: API CustomCss > :stylesheet: attribute > theme CSS
        if (options.CustomCss is not null)
        {
            // API wins — always embed inline
            sb.Append("<style>\n");
            if (themeCss is not null)
                sb.Append(themeCss).Append('\n');
            sb.Append(options.CustomCss).Append('\n');
            sb.Append("</style>\n");
            return;
        }

        if (hasStylesheetAttr && stylesheetVal!.Length == 0)
            return; // :stylesheet: (empty) = suppress all CSS

        if (useLink)
        {
            // Link mode: emit <link rel="stylesheet" href="...">
            var href = ResolveStylesheetHref(
                hasStylesheetAttr ? stylesheetVal : null, attrs);
            if (href is not null)
            {
                sb.Append("<link rel=\"stylesheet\" href=\"");
                EscapeTo(sb, href);
                sb.Append("\">\n");
            }
            return;
        }

        // Embed mode: inline <style> block
        if (themeCss is not null)
        {
            sb.Append("<style>\n");
            sb.Append(themeCss).Append('\n');
            sb.Append("</style>\n");
        }
    }

    /// <summary>
    /// Resolves the stylesheet href for <c>&lt;link&gt;</c> mode.
    /// </summary>
    private static string? ResolveStylesheetHref(
        string? filename,
        IReadOnlyDictionary<string, string> attributes)
    {
        var name = filename ?? "asciidoctor.css";

        // Absolute URL: use as-is
        if (name.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return name;

        var dir = attributes.TryGetValue("stylesdir", out var sd) ? sd : ".";
        return dir.Length > 0 ? $"{dir}/{name}" : name;
    }

    private static void RenderIndex(StringBuilder sb, IndexNode index)
    {
        sb.Append("<div class=\"index\">\n");

        char currentLetter = '\0';
        bool listOpen = false;

        foreach (var entry in index.Entries)
        {
            if (entry.Term.Length == 0) continue;

            char firstLetter = char.ToUpperInvariant(entry.Term[0]);
            if (firstLetter != currentLetter)
            {
                if (listOpen)
                    sb.Append("</ul>\n");
                currentLetter = firstLetter;
                sb.Append("<h3>");
                sb.Append(currentLetter);
                sb.Append("</h3>\n<ul>\n");
                listOpen = true;
            }

            sb.Append("<li>");
            EscapeTo(sb, entry.Term);
            if (entry.SubTerms.Count > 0)
            {
                sb.Append(", ");
                for (int i = 0; i < entry.SubTerms.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    EscapeTo(sb, entry.SubTerms[i]);
                }
            }
            sb.Append("</li>\n");
        }

        if (listOpen)
            sb.Append("</ul>\n");

        sb.Append("</div>\n");
    }
}
