using System.Globalization;

namespace AdocNet.Converters.Pdf;

/// <summary>
/// Parses asciidoctor-pdf compatible YAML theme files and produces <see cref="PdfRenderOptions"/>.
/// Supports the subset of YAML used by asciidoctor-pdf themes: indentation-based nesting,
/// string/number values, and the font catalog structure.
/// </summary>
public static class PdfThemeLoader
{
    /// <summary>
    /// Loads a theme file and returns configured <see cref="PdfRenderOptions"/>.
    /// </summary>
    /// <param name="themePath">Path to the YAML theme file.</param>
    /// <param name="fontsDir">Base directory for resolving font file paths. Null = same directory as theme file.</param>
    public static PdfRenderOptions Load(string themePath, string? fontsDir = null) =>
        Load(themePath, fontsDir, baseDirectory: null);

    /// <summary>
    /// Loads a theme file and returns configured <see cref="PdfRenderOptions"/> whose
    /// <see cref="PdfRenderOptions.BaseDirectory"/> (where document images resolve) is set.
    /// </summary>
    /// <param name="themePath">Path to the YAML theme file.</param>
    /// <param name="fontsDir">Base directory for resolving font file paths. Null = same directory as theme file.</param>
    /// <param name="baseDirectory">Directory document image paths are relative to (usually the document's folder).</param>
    public static PdfRenderOptions Load(string themePath, string? fontsDir, string? baseDirectory)
    {
        var themeDir = Path.GetDirectoryName(Path.GetFullPath(themePath)) ?? ".";
        fontsDir ??= themeDir;
        var lines = File.ReadAllLines(themePath);
        var props = ParseYaml(lines);
        return BuildOptions(props, fontsDir, themeDir, baseDirectory);
    }

    /// <summary>
    /// Parses simple YAML into a flat dictionary with dot-separated keys.
    /// E.g., "heading-h2:\n  font-size: 16" → {"heading-h2.font-size": "16"}
    /// </summary>
    /// <summary>
    /// Normalizes a YAML key to kebab-case for canonical lookup. Catalog font
    /// family names live two levels deep under <c>font.catalog</c> and may
    /// contain underscores in their style children (<c>normal/bold/italic/bold_italic</c>);
    /// the family name itself is also preserved verbatim because it's a
    /// user-facing identifier referenced from <c>base.font_family: Noto Serif</c>.
    /// Conversion only happens for top-level + nested-property keys.
    /// </summary>
    private static string NormalizeKey(string prefix, string key)
    {
        // Don't touch font catalog family names (they're identifiers like
        // "Noto Serif" that may contain spaces; not relevant here) or their
        // style child keys (normal, bold, italic, bold_italic — bold_italic
        // is the canonical form per asciidoctor-pdf, keep it).
        if (prefix.StartsWith("font.catalog", StringComparison.Ordinal))
            return key;
        return key.Replace('_', '-');
    }

    internal static Dictionary<string, string> ParseYaml(string[] lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var indentStack = new List<(int Indent, string Prefix)> { (0, "") };

        foreach (var rawLine in lines)
        {
            // Strip comments, but only when # is preceded by whitespace (not inside values like #365f91).
            // A # at column 0 is always a comment. A # after a space is a comment only if not inside a value.
            var line = rawLine;
            if (line.Length > 0 && line[0] == '#') { continue; }
            // Find comment: # preceded by whitespace, but not inside a quoted string
            int commentStart = FindCommentStart(line);
            if (commentStart >= 0) line = line[..commentStart];
            if (string.IsNullOrWhiteSpace(line)) continue;

            int indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            var trimmed = line[indent..].TrimEnd();
            if (trimmed.Length == 0) continue;

            // Pop stack to find the right nesting level
            while (indentStack.Count > 1 && indentStack[^1].Indent >= indent)
                indentStack.RemoveAt(indentStack.Count - 1);

            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx <= 0) continue;

            var key = trimmed[..colonIdx].Trim();
            var value = colonIdx + 1 < trimmed.Length ? trimmed[(colonIdx + 1)..].Trim() : "";

            var prefix = indentStack[^1].Prefix;
            // Asciidoctor-pdf themes accept both snake_case (font_family) and
            // kebab-case (font-family) for the same key. Normalize to kebab
            // here so lookups in BuildOptions use a single canonical form.
            // Catalog family names ("Noto Serif") may legitimately contain
            // underscores in the value; we only normalize the KEY, not the
            // value — and only when this isn't a catalog family child key.
            var normalizedKey = NormalizeKey(prefix, key);
            var fullKey = prefix.Length > 0 ? $"{prefix}.{normalizedKey}" : normalizedKey;

            if (value.Length == 0)
            {
                // This is a parent key — push onto stack
                indentStack.Add((indent, fullKey));
            }
            else
            {
                // Strip surrounding quotes if present
                if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
                    value = value[1..^1];
                else if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value[1..^1];

                result[fullKey] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Builds PdfRenderOptions from parsed theme properties.
    /// </summary>
    internal static PdfRenderOptions BuildOptions(Dictionary<string, string> props, string fontsDir, string? themeDir = null,
        string? baseDirectory = null)
    {
        // Resolve font paths from the font catalog
        string? fontRegular = ResolveFontPath(props, "base.font-family", "normal", fontsDir);
        string? fontBold = ResolveFontPath(props, "base.font-family", "bold", fontsDir);
        string? fontItalic = ResolveFontPath(props, "base.font-family", "italic", fontsDir);
        string? codeFontFamily = GetString(props, "code.font-family") ?? GetString(props, "codespan.font-family");
        string? fontMono = ResolveFontCatalogPath(props, codeFontFamily, "normal", fontsDir);
        string? fontMonoBold = ResolveFontCatalogPath(props, codeFontFamily, "bold", fontsDir);
        string? fontMonoItalic = ResolveFontCatalogPath(props, codeFontFamily, "italic", fontsDir);
        string? fontMonoBoldItalic = ResolveFontCatalogPath(props, codeFontFamily, "bold_italic", fontsDir);
        string? fontHeading = ResolveHeadingFontPath(props, fontsDir);

        // Parse margins: asciidoctor-pdf uses [top, right, bottom, left] array
        float marginTop = 72f, marginRight = 72f, marginBottom = 72f, marginLeft = 72f;
        if (props.TryGetValue("page.margin", out var marginStr))
            ParseMargins(marginStr, ref marginTop, ref marginRight, ref marginBottom, ref marginLeft);

        var pageSize = ParsePageSize(GetString(props, "page.size"));

        var options = new PdfRenderOptions
        {
            // Fonts
            FontPath = fontRegular,
            BoldFontPath = fontBold,
            ItalicFontPath = fontItalic,
            MonoFontPath = fontMono,
            MonoBoldFontPath = fontMonoBold,
            MonoItalicFontPath = fontMonoItalic,
            MonoBoldItalicFontPath = fontMonoBoldItalic,
            HeadingFontPath = fontHeading,

            // Page geometry (asciidoctor-pdf default page.size: A4 = 595.28 x 841.89)
            PageWidth = pageSize.Width,
            PageHeight = pageSize.Height,
            MarginTop = marginTop,
            MarginRight = marginRight,
            MarginBottom = marginBottom,
            MarginLeft = marginLeft,

            // Typography. Asciidoctor-pdf themes use heading.h1-font-size (nested
            // under heading:) while our older templates used heading-h1.font-size
            // (separate key). Try the asciidoctor form first, then the legacy form.
            FontSize = GetFloat(props, "base.font-size") ?? 11f,
            CodeFontSize = GetFloat(props, "code.font-size") ?? GetFloat(props, "codespan.font-size") ?? 9f,
            TitleFontSize = GetFloat(props, "heading.h1-font-size")
                          ?? GetFloat(props, "heading-h1.font-size")
                          ?? GetFloat(props, "title-page.title.font-size") ?? 24f,
            // asciidoctor-pdf default theme: $base_line_height_length / $base_font_size = 12 / 10.5.
            LineSpacing = GetFloat(props, "base.line-height") ?? 12f / 10.5f,
            TitleLineHeight = GetFloat(props, "heading.h1-line-height") ?? GetFloat(props, "heading-h1.line-height"),

            // Per-heading sizes
            Heading2FontSize = GetFloat(props, "heading.h2-font-size") ?? GetFloat(props, "heading-h2.font-size"),
            Heading3FontSize = GetFloat(props, "heading.h3-font-size") ?? GetFloat(props, "heading-h3.font-size"),
            Heading4FontSize = GetFloat(props, "heading.h4-font-size") ?? GetFloat(props, "heading-h4.font-size"),
            Heading5FontSize = GetFloat(props, "heading.h5-font-size") ?? GetFloat(props, "heading-h5.font-size"),

            // Per-heading margin-bottom — asciidoctor's heading.margin_bottom
            // (= $vertical_rhythm * 0.9 ≈ 10.8pt) applies to all heading levels.
            // We override h1 (used for level-1 sections in articles) to a smaller
            // value because compact heading leading + 10.8 margin gives too much
            // advance for the larger 22pt font (measured 32.8 vs REF 29.20).
            Heading2MarginBottom = GetFloat(props, "heading.h2-margin-bottom") ?? GetFloat(props, "heading-h2.margin-bottom") ?? 7.2f,
            Heading3MarginBottom = GetFloat(props, "heading.h3-margin-bottom") ?? GetFloat(props, "heading-h3.margin-bottom") ?? GetFloat(props, "heading.margin-bottom"),
            Heading4MarginBottom = GetFloat(props, "heading.h4-margin-bottom") ?? GetFloat(props, "heading-h4.margin-bottom") ?? GetFloat(props, "heading.margin-bottom"),
            Heading5MarginBottom = GetFloat(props, "heading.h5-margin-bottom") ?? GetFloat(props, "heading-h5.margin-bottom") ?? GetFloat(props, "heading.margin-bottom"),

            // Heading color: global fallback, then h1 for title
            HeadingColor = ParseColor(GetString(props, "heading-h1.font-color"))
                         ?? ParseColor(GetString(props, "heading.font-color")),

            // Per-heading colors (null = fall back to HeadingColor at render time)
            Heading2Color = ParseColor(GetString(props, "heading-h2.font-color")),
            Heading3Color = ParseColor(GetString(props, "heading-h3.font-color")),
            Heading4Color = ParseColor(GetString(props, "heading-h4.font-color")),
            Heading5Color = ParseColor(GetString(props, "heading-h5.font-color")),

            // Body color
            BodyColor = ParseColor(GetString(props, "base.font-color")),

            // Headers and footers
            ShowPageNumbers = true,
            HeaderText = BuildHeaderFooterTemplate(props, "header"),
            FooterText = BuildHeaderFooterTemplate(props, "footer"),
            HeaderFontSize = GetFloat(props, "header.recto.right.font-size")
                           ?? GetFloat(props, "header.font-size") ?? 9f,
            FooterFontSize = GetFloat(props, "footer.recto.right.font-size")
                           ?? GetFloat(props, "footer.font-size") ?? 9f,
            HeaderFontColor = ParseColor(GetString(props, "header.recto.right.font-color"))
                            ?? ParseColor(GetString(props, "header.font-color")),
            FooterFontColor = ParseColor(GetString(props, "footer.recto.right.font-color"))
                            ?? ParseColor(GetString(props, "footer.font-color")),
            HeaderAlignment = PdfAlignment.Right,
            FooterAlignment = PdfAlignment.Right,
            HeaderHeight = GetFloat(props, "header.height") ?? 0f,
            FooterHeight = GetFloat(props, "footer.height") ?? 0f,

            // Running content start-at
            RunningContentStartAt = GetString(props, "running-content.start-at"),

            // Link color (asciidoctor-pdf default theme: #428BCA blue)
            LinkColor = ParseColor(GetString(props, "link.font-color"))
                      ?? ParseColor("428BCA"), // asciidoctor-pdf default theme link colour

            // Asciidoctor-pdf centers the document title for article doctype.
            // Themes may explicitly override via heading-h1.text-align.
            TitleAlignment = ParseAlignment(GetString(props, "heading-h1.text-align"))
                          ?? PdfAlignment.Center,

            // Table styling
            TableBorderColor = ParseColor(GetString(props, "table.border-color")),
            TableHeaderBackground = ParseColor(GetString(props, "table.head.background-color")),
            TableHeaderFontColor = ParseColor(GetString(props, "table.head.font-color")),

            // Code block styling
            CodeBorderColor = ParseColor(GetString(props, "code.border-color"))
                            ?? new PdfColor(0.8f, 0.8f, 0.8f),

            // Inline codespan styling - only set background if theme explicitly specifies it (matches Asciidoctor default)
            CodespanBackground = ParseColor(GetString(props, "codespan.background-color")),

            // Inline codespan font color (asciidoctor-pdf uses #B12146 dark red)
            CodespanColor = ParseColor(GetString(props, "codespan.font-color")),

            // Footer image (SVG logo)
            FooterImagePath = ResolveFooterImage(props, themeDir ?? fontsDir),
            FooterImageWidth = ParseFooterImageWidth(props),

            // Paragraph spacing — asciidoctor-pdf uses block.margin_bottom
            // (= $vertical_rhythm = 12pt). Try the asciidoctor key first, then
            // our legacy keys.
            ParagraphSpacingAfter = GetFloat(props, "block.margin-bottom")
                                  ?? GetFloat(props, "prose.margin-bottom")
                                  ?? GetFloat(props, "base.margin-bottom")
                                  ?? 12f, // default theme: $block_margin_bottom = $vertical_rhythm

            // Section spacing — empirically calibrated against asciidoctor-pdf:
            // body→heading baseline-to-baseline is ~43pt (vs 33pt with raw theme value).
            // Theme heading.margin_top (= $vertical_rhythm * 0.4 ≈ 4.8pt) is too small
            // because asciidoctor adds an extra implicit line of space before headings.
            // We add ~one body line_height_length (12pt) on top.
            SectionSpacing = (GetFloat(props, "heading.margin-top")
                          ?? GetFloat(props, "heading-h2.margin-top") ?? 4.8f) + 12f,

            // Title margins. Asciidoctor-pdf doesn't set heading-h1.margin-bottom
            // explicitly but its general heading.margin_bottom (= $vertical_rhythm * 0.9
            // ≈ 10.8pt) applies. Our 16pt fallback was too generous.
            TitleMarginTop = GetFloat(props, "heading-h1.margin-top") ?? 0f,
            TitleMarginBottom = GetFloat(props, "heading-h1.margin-bottom") ?? GetFloat(props, "heading.margin-bottom") ?? 16f,

            TitlePage = BuildTitlePage(props, fontsDir, themeDir ?? fontsDir),
            HeadingLineHeight = GetFloat(props, "heading.line-height"),
            CodeLineHeight = GetFloat(props, "code.line-height"),
            CodePadding = GetFloat(props, "code.padding"),
            HeadingFontColor = ParseColor(GetString(props, "heading.font-color")),
            // asciidoctor-pdf default theme: code background F5F5F5, border 0.75.
            CodeBackground = ParseColor(GetString(props, "code.background-color")) ?? ParseColor("F5F5F5"),
            CodeBorderWidth = GetFloat(props, "code.border-width"),
            TocLineHeight = GetFloat(props, "toc.line-height"),
            ListIndent = GetFloat(props, "list.indent"),
            Heading2FontPath = ResolveHeadingLevelFontPath(props, 2, fontsDir),
            Heading3FontPath = ResolveHeadingLevelFontPath(props, 3, fontsDir),
            Heading4FontPath = ResolveHeadingLevelFontPath(props, 4, fontsDir),
            Heading5FontPath = ResolveHeadingLevelFontPath(props, 5, fontsDir),
            JustifyText = GetString(props, "base.text-align") is { } textAlign
                ? textAlign.Trim().Equals("justify", StringComparison.OrdinalIgnoreCase)
                : null,
            TableLineHeight = GetFloat(props, "table.line-height"),
            TableCellPadding = ParseBox(GetString(props, "table.cell-padding")),
            TableGridWidth = GetFloat(props, "table.grid-width") ?? GetFloat(props, "table.border-width"),
            TableHeadBorderBottomWidth = GetFloat(props, "table.head.border-bottom-width"),
            TocIndent = GetFloat(props, "toc.indent"),
            TocDotLeaderColor = ParseColor(GetString(props, "toc.dot-leader.font-color")),
            HeadingMarginTop = GetFloat(props, "heading.margin-top"),
            HeadingMarginBottom = GetFloat(props, "heading.margin-bottom"),
            Heading2MarginTop = GetFloat(props, "heading.h2-margin-top") ?? GetFloat(props, "heading-h2.margin-top"),
            Heading3MarginTop = GetFloat(props, "heading.h3-margin-top") ?? GetFloat(props, "heading-h3.margin-top"),
            Heading4MarginTop = GetFloat(props, "heading.h4-margin-top") ?? GetFloat(props, "heading-h4.margin-top"),
            Heading5MarginTop = GetFloat(props, "heading.h5-margin-top") ?? GetFloat(props, "heading-h5.margin-top"),
            BaseDirectory = baseDirectory,
        };

        return options;
    }

    /// <summary>
    /// Parses a CSS-like box value: <c>3</c>, <c>[1, 4]</c>, <c>[1, 4, 2]</c> or <c>[1, 2, 3, 4]</c>
    /// into [top, right, bottom, left]. Null when absent or unparsable.
    /// </summary>
    internal static float[]? ParseBox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value!.Trim().TrimStart('[').TrimEnd(']').Split(',');
        var n = new List<float>();
        foreach (var part in parts)
        {
            if (ParseLength(part) is not { } v) return null;
            n.Add(v);
        }
        return n.Count switch
        {
            1 => new[] { n[0], n[0], n[0], n[0] },
            2 => new[] { n[0], n[1], n[0], n[1] },
            3 => new[] { n[0], n[1], n[2], n[1] },
            4 => n.ToArray(),
            _ => null,
        };
    }

    /// <summary>Theme <c>page.size</c>: A4 (default), Letter, Legal, or <c>[width, height]</c> lengths.</summary>
    private static (float Width, float Height) ParsePageSize(string? value)
    {
        switch (value?.Trim().ToUpperInvariant())
        {
            case null or "" or "A4": return (595.28f, 841.89f);
            case "LETTER": return (612f, 792f);
            case "LEGAL": return (612f, 1008f);
        }
        var parts = value!.Trim().TrimStart('[').TrimEnd(']').Split(',');
        if (parts.Length == 2 && ParseLength(parts[0]) is { } w && ParseLength(parts[1]) is { } h)
            return (w, h);
        return (595.28f, 841.89f);
    }

    // ── Title page ───────────────────────────────────────────────────────

    /// <summary>
    /// Maps the <c>title-page</c> keys, falling back to asciidoctor-pdf's default theme
    /// (which <c>extends: default</c> themes inherit) for anything the theme omits.
    /// </summary>
    private static PdfTitlePageOptions BuildTitlePage(Dictionary<string, string> props, string fontsDir, string themeDir)
    {
        float baseSize = GetFloat(props, "base.font-size") ?? 10.5f;
        float baseLineHeight = GetFloat(props, "base.line-height") ?? 12f / 10.5f;
        var align = ParseAlignment(GetString(props, "title-page.text-align")) ?? PdfAlignment.Right;

        PdfTitlePageText Text(string part, float size, PdfColor? color, string style, float lineHeight,
            float marginTop, string? top = null)
        {
            string prefix = $"title-page.{part}.";
            var fontStyle = (GetString(props, prefix + "font-style") ?? style).Replace('-', '_');
            var family = GetString(props, prefix + "font-family");
            return new PdfTitlePageText
            {
                FontPath = family is null ? null : ResolveFontCatalogPath(props, family, fontStyle, fontsDir),
                FontSize = GetFloat(props, prefix + "font-size") ?? size,
                Color = ParseColor(GetString(props, prefix + "font-color")) ?? color,
                Bold = fontStyle is "bold" or "bold_italic",
                Italic = fontStyle is "italic" or "bold_italic",
                LineHeight = GetFloat(props, prefix + "line-height") ?? lineHeight,
                MarginTop = GetFloat(props, prefix + "margin-top") ?? marginTop,
                MarginBottom = GetFloat(props, prefix + "margin-bottom") ?? 0f,
                Top = GetString(props, prefix + "top") ?? top,
            };
        }

        var logo = GetString(props, "title-page.logo.image");
        var background = GetString(props, "title-page.background-image");
        return new PdfTitlePageOptions
        {
            Alignment = align,
            BackgroundImagePath = ResolveThemeImage(background, themeDir),
            LogoImagePath = ResolveThemeImage(logo, themeDir),
            LogoTop = GetString(props, "title-page.logo.top") ?? "10%",
            LogoAlignment = ParseAlignment(GetString(props, "title-page.logo.align")),
            LogoWidth = logo is null ? null : ParseLength(ImageMacroAttribute(logo, "pdfwidth")),
            Title = Text("title",
                GetFloat(props, "heading-h1.font-size") ?? (float)Math.Floor(baseSize * 2.6f),
                ParseColor(GetString(props, "role.subtitle.font-color")) ?? ParseColor("999999"),
                "normal", 0.9f, 0f, top: "55%"),
            Subtitle = Text("subtitle",
                GetFloat(props, "heading-h3.font-size") ?? (float)Math.Round(baseSize * 1.7f, MidpointRounding.AwayFromZero),
                null, "bold_italic", 1f, 0f),
            Authors = Text("authors", (float)Math.Round(baseSize * 1.25f, MidpointRounding.AwayFromZero),
                ParseColor("181818"), "normal", baseLineHeight, baseSize * 1.25f),
            Revision = Text("revision", baseSize, null, "normal", baseLineHeight, baseSize * 1.25f),
        };
    }

    private static string? ResolveThemeImage(string? value, string themeDir)
    {
        if (value is null) return null;
        var path = ExtractImageMacroPath(value) ?? value;
        var full = Path.IsPathRooted(path) ? path : Path.Combine(themeDir, path);
        return File.Exists(full) ? Path.GetFullPath(full) : null;
    }

    private static string? ImageMacroAttribute(string value, string name)
    {
        int open = value.IndexOf('['), close = value.LastIndexOf(']');
        if (open < 0 || close <= open) return null;
        foreach (var part in value[(open + 1)..close].Split(','))
        {
            var kv = part.Split(new[] { '=' }, 2);
            if (kv.Length == 2 && kv[0].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return kv[1].Trim();
        }
        return null;
    }

    /// <summary>Parses a length such as <c>180</c>, <c>2.5in</c>, <c>6cm</c>, <c>50mm</c> or <c>96px</c> into points.</summary>
    internal static float? ParseLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value!.Trim();
        (string Suffix, float Factor)[] units = { ("in", 72f), ("cm", 72f / 2.54f), ("mm", 72f / 25.4f), ("px", 0.75f), ("pt", 1f) };
        foreach (var (suffix, factor) in units)
        {
            if (v.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && float.TryParse(v[..^suffix.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                return n * factor;
        }
        return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var pts) ? pts : null;
    }

    // ── Font resolution ──────────────────────────────────────────────────

    private static string? ResolveFontPath(Dictionary<string, string> props, string familyKey, string style, string fontsDir)
    {
        var familyName = GetString(props, familyKey);
        if (familyName is null) return null;
        return ResolveFontCatalogPath(props, familyName, style, fontsDir);
    }

    private static string? ResolveFontCatalogPath(Dictionary<string, string> props, string? familyName, string style, string fontsDir)
    {
        if (familyName is null) return null;

        // Look up in font catalog: font.catalog.{family}.{style}
        var catalogKey = $"font.catalog.{familyName}.{style}";
        if (!props.TryGetValue(catalogKey, out var relativePath)) return null;

        return ResolveAbsoluteFontPath(relativePath, fontsDir);
    }

    private static string? ResolveHeadingFontPath(Dictionary<string, string> props, string fontsDir)
    {
        // Try heading-specific font family, fall back to h1
        var headingFamily = GetString(props, "heading-h2.font-family")
                         ?? GetString(props, "heading-h1.font-family");
        if (headingFamily is null) return null;

        // Headings default to bold (asciidoctor-pdf heading.font-style: bold).
        var style = (GetString(props, "heading-h2.font-style") ?? GetString(props, "heading.font-style") ?? "bold")
            .Replace('-', '_');
        return ResolveFontCatalogPath(props, headingFamily, style, fontsDir)
            ?? ResolveFontCatalogPath(props, headingFamily, "normal", fontsDir);
    }

    /// <summary>
    /// Font of one heading level as asciidoctor-pdf resolves it: heading-hN family/style, then
    /// heading's, then the base family; style defaults to bold. Null when not in the catalog.
    /// </summary>
    private static string? ResolveHeadingLevelFontPath(Dictionary<string, string> props, int level, string fontsDir)
    {
        var family = GetString(props, $"heading-h{level}.font-family") ?? GetString(props, $"heading.h{level}-font-family")
                  ?? GetString(props, "heading.font-family") ?? GetString(props, "base.font-family");
        if (family is null) return null;
        var style = (GetString(props, $"heading-h{level}.font-style") ?? GetString(props, $"heading.h{level}-font-style")
                  ?? GetString(props, "heading.font-style") ?? "bold").Replace('-', '_');
        return ResolveFontCatalogPath(props, family, style, fontsDir)
            ?? ResolveFontCatalogPath(props, family, "normal", fontsDir);
    }

    private static string? ResolveAbsoluteFontPath(string relativePath, string fontsDir)
    {
        // Asciidoctor-pdf supports a GEM_FONTS_DIR placeholder in font catalog
        // paths that resolves to the bundled fonts folder
        // (asciidoctor-pdf-X.Y.Z/data/fonts/). When the theme path comes from
        // such a gem install, fontsDir points at data/themes/ — go up one level
        // and into data/fonts/ to find the bundled TTFs.
        if (relativePath.StartsWith("GEM_FONTS_DIR/", StringComparison.Ordinal)
            || relativePath.StartsWith("GEM_FONTS_DIR\\", StringComparison.Ordinal))
        {
            var bundledName = relativePath.Substring("GEM_FONTS_DIR/".Length);
            var gemFontsDir = ResolveGemFontsDir(fontsDir);
            if (gemFontsDir is not null)
            {
                var bundled = Path.Combine(gemFontsDir, bundledName);
                if (File.Exists(bundled)) return Path.GetFullPath(bundled);
            }
            // Fall through to the standard resolution paths below using just
            // the filename portion — picks up matching TTFs in fontsDir.
            relativePath = bundledName;
        }

        // Try as-is first, then relative to fontsDir
        if (File.Exists(relativePath)) return Path.GetFullPath(relativePath);

        var resolved = Path.Combine(fontsDir, relativePath);
        if (File.Exists(resolved)) return Path.GetFullPath(resolved);

        // Try with Fonts subdirectory
        var withFonts = Path.Combine(fontsDir, "Fonts", relativePath);
        if (File.Exists(withFonts)) return Path.GetFullPath(withFonts);

        // Try filename only (strip directory prefix like "fonts/") to match system font dirs
        var filename = Path.GetFileName(relativePath);
        if (filename != relativePath)
        {
            var byName = Path.Combine(fontsDir, filename);
            if (File.Exists(byName)) return Path.GetFullPath(byName);
        }

        return null;
    }

    /// <summary>
    /// Locates the asciidoctor-pdf gem's bundled fonts directory.
    /// Strategy: from the supplied fontsDir, walk up looking for a sibling
    /// "fonts" directory under "data" (matching the
    /// asciidoctor-pdf-X.Y.Z/data/fonts/ install layout).
    /// </summary>
    private static string? ResolveGemFontsDir(string fontsDir)
    {
        // If fontsDir is already a "fonts" dir, use it directly.
        if (string.Equals(Path.GetFileName(fontsDir.TrimEnd(Path.DirectorySeparatorChar, '/')),
                "fonts", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(fontsDir))
        {
            return fontsDir;
        }

        // Common case: theme is in data/themes/, fonts are in data/fonts/.
        var parent = Path.GetDirectoryName(fontsDir.TrimEnd(Path.DirectorySeparatorChar, '/'));
        if (parent is not null)
        {
            var sibling = Path.Combine(parent, "fonts");
            if (Directory.Exists(sibling)) return sibling;
        }
        return null;
    }

    // ── Header/Footer template building ──────────────────────────────────

    private static string? BuildHeaderFooterTemplate(Dictionary<string, string> props, string section)
    {
        // Try recto.right.content first (most common), then recto.left, then recto.center
        var content = GetString(props, $"{section}.recto.right.content")
                   ?? GetString(props, $"{section}.recto.center.content")
                   ?? GetString(props, $"{section}.recto.left.content");

        if (content is null) return null;

        // Translate asciidoctor-pdf placeholders to AdocNet placeholders
        return content
            .Replace("{page-number}", "{page}")
            .Replace("{page-count}", "{pages}")
            .Replace("{chapter-title}", "{section-title}")
            .Replace("{section-or-chapter-title}", "{section-title}");
    }

    // ── Footer image parsing ────────────────────────────────────────────

    private static string? ResolveFooterImage(Dictionary<string, string> props, string fontsDir)
    {
        var bgImage = GetString(props, "footer.background-image");
        if (bgImage is null) return null;

        // Parse "image:path/to/file.svg[attrs]" inline macro
        string? path = ExtractImageMacroPath(bgImage);
        if (path is null) return null;

        return ResolveAbsoluteFontPath(path, fontsDir);
    }

    private static float ParseFooterImageWidth(Dictionary<string, string> props)
    {
        var bgImage = GetString(props, "footer.background-image");
        if (bgImage is null) return 64f;

        // Parse pdfwidth=NNN from the image attributes
        int bracketStart = bgImage.IndexOf('[');
        int bracketEnd = bgImage.IndexOf(']');
        if (bracketStart < 0 || bracketEnd < 0 || bracketEnd <= bracketStart) return 64f;

        var attrs = bgImage[(bracketStart + 1)..bracketEnd];
        foreach (var part in attrs.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("pdfwidth=", StringComparison.OrdinalIgnoreCase))
            {
                var value = trimmed[9..].Trim();
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float w))
                    return w;
            }
        }
        return 64f;
    }

    /// <summary>
    /// Extracts the path from an asciidoctor image macro: "image:path/to/file.svg[attrs]" → "path/to/file.svg"
    /// </summary>
    private static string? ExtractImageMacroPath(string value)
    {
        if (!value.StartsWith("image:", StringComparison.OrdinalIgnoreCase)) return null;
        int pathStart = 6;
        int bracketStart = value.IndexOf('[', pathStart);
        if (bracketStart < 0) return value[pathStart..].Trim();
        return value[pathStart..bracketStart].Trim();
    }

    // ── Margin parsing ───────────────────────────────────────────────────

    private static void ParseMargins(string value, ref float top, ref float right, ref float bottom, ref float left)
    {
        // Format: [top, right, bottom, left] or single number. Each value may have
        // a unit suffix (in, mm, cm, pt). Asciidoctor-pdf themes use "0.5in" etc.
        var trimmed = value.Trim().TrimStart('[').TrimEnd(']');
        var parts = trimmed.Split(',');

        if (parts.Length == 4)
        {
            top = ParseLengthSafe(parts[0].Trim());
            right = ParseLengthSafe(parts[1].Trim());
            bottom = ParseLengthSafe(parts[2].Trim());
            left = ParseLengthSafe(parts[3].Trim());
        }
        else if (parts.Length == 1)
        {
            top = right = bottom = left = ParseLengthSafe(parts[0].Trim());
        }
    }

    /// <summary>
    /// Parses a length value with optional unit suffix (in, mm, cm, pt).
    /// Returns the value in PDF points (1pt = 1/72 inch).
    /// </summary>
    private static float ParseLengthSafe(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0f;
        // Strip surrounding quotes (YAML may emit "0.5in" with quotes preserved)
        s = s.Trim('\'', '"');
        // Find where the numeric part ends
        int unitStart = 0;
        while (unitStart < s.Length && (char.IsDigit(s[unitStart]) || s[unitStart] == '.' || s[unitStart] == '-' || s[unitStart] == '+'))
            unitStart++;
        var numPart = s.Substring(0, unitStart);
        var unit = unitStart < s.Length ? s.Substring(unitStart).Trim().ToLowerInvariant() : "";
        if (!float.TryParse(numPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return 0f;
        return unit switch
        {
            "in" => v * 72f,
            "mm" => v * 72f / 25.4f,
            "cm" => v * 72f / 2.54f,
            "px" => v * 72f / 96f,
            _    => v, // pt or no unit
        };
    }

    // ── Color parsing ────────────────────────────────────────────────────

    /// <summary>
    /// Parses asciidoctor-pdf alignment values ("left", "center", "right",
    /// "justify") into a PdfAlignment enum. "justify" is treated as Left
    /// (the writer applies its own justification at line render time).
    /// </summary>
    internal static PdfAlignment? ParseAlignment(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "left" => PdfAlignment.Left,
            "center" => PdfAlignment.Center,
            "right" => PdfAlignment.Right,
            "justify" => PdfAlignment.Left,
            _ => null,
        };
    }

    internal static PdfColor? ParseColor(string? value)
    {
        if (value is null || value.Length == 0) return null;

        // Remove # prefix if present
        var hex = value.TrimStart('#');

        if (hex.Length == 6 &&
            int.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
            int.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
            int.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return new PdfColor(r / 255f, g / 255f, b / 255f);
        }

        // Named colors
        return value.ToLowerInvariant() switch
        {
            "black" => new PdfColor(0, 0, 0),
            "white" => new PdfColor(1, 1, 1),
            "red" => new PdfColor(1, 0, 0),
            "blue" => new PdfColor(0, 0, 1),
            "green" => new PdfColor(0, 0.5f, 0),
            _ => null,
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Finds the start of a YAML comment (# preceded by whitespace and not inside a value).
    /// Returns -1 if no comment found.
    /// </summary>
    private static int FindCommentStart(string line)
    {
        // After a colon+space, the rest is a value — # inside values like #365f91 is not a comment.
        int colonIdx = line.IndexOf(':');
        if (colonIdx >= 0)
        {
            // Only look for comments before the colon
            for (int i = 0; i < colonIdx; i++)
            {
                if (line[i] == '#' && (i == 0 || line[i - 1] == ' '))
                    return i;
            }
            return -1; // No comment in the value portion
        }

        // No colon: look for # preceded by whitespace
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '#' && (i == 0 || line[i - 1] == ' '))
                return i;
        }
        return -1;
    }

    private static string? GetString(Dictionary<string, string> props, string key)
    {
        if (!props.TryGetValue(key, out var v)) return null;
        // Resolve $varname references (e.g. heading.font_color: $base_font_color
        // points at base.font-color: 333333). Only chase one level — sufficient
        // for the asciidoctor-pdf default theme.
        if (v.Length > 0 && v[0] == '$')
        {
            var resolved = ResolveStringVar(v, props);
            if (resolved is not null) return resolved;
        }
        return v;
    }

    /// <summary>
    /// Resolves a <c>$varname</c> reference to its string value via the same
    /// snake→nested key lookup the formula evaluator uses.
    /// </summary>
    private static string? ResolveStringVar(string expr, Dictionary<string, string> props)
    {
        var rawName = expr.TrimStart('$').Trim();
        if (props.TryGetValue(rawName, out var v)) return v;
        var kebab = rawName.Replace('_', '-');
        if (props.TryGetValue(kebab, out var v2)) return v2;
        // Try splitting on the LAST underscore as a dot (e.g. base_font_color → base.font-color).
        var lastUnd = rawName.LastIndexOf('_');
        while (lastUnd > 0)
        {
            var candidate = (rawName.Substring(0, lastUnd).Replace('_', '.')
                + "." + rawName.Substring(lastUnd + 1)).Replace('_', '-');
            if (props.TryGetValue(candidate, out var v3)) return v3;
            lastUnd = rawName.LastIndexOf('_', lastUnd - 1);
        }
        return null;
    }

    private static float? GetFloat(Dictionary<string, string> props, string key)
    {
        if (!props.TryGetValue(key, out var v)) return null;
        if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            return f;
        return EvaluateFormula(v, props);
    }

    /// <summary>
    /// Evaluates the small subset of asciidoctor-pdf theme formula syntax we
    /// need to compute font sizes from base values. Supports
    /// <c>$varname</c> substitution, the four arithmetic operators, and the
    /// <c>floor() / round() / ceil()</c> wrapper functions. Resolves vars
    /// recursively (with cycle protection) so chained references work.
    /// </summary>
    internal static float? EvaluateFormula(string expr, Dictionary<string, string> props,
        HashSet<string>? resolving = null)
    {
        if (string.IsNullOrWhiteSpace(expr)) return null;
        var s = expr.Trim();

        // Wrapper functions: floor(...), round(...), ceil(...)
        foreach (var fn in new[] { ("floor(", new Func<float, float>(x => (float)Math.Floor(x))),
                                    ("round(", new Func<float, float>(x => (float)Math.Round(x, MidpointRounding.AwayFromZero))),
                                    ("ceil(",  new Func<float, float>(x => (float)Math.Ceiling(x))) })
        {
            if (s.StartsWith(fn.Item1, StringComparison.OrdinalIgnoreCase) && s.EndsWith(")", StringComparison.Ordinal))
            {
                var inner = s.Substring(fn.Item1.Length, s.Length - fn.Item1.Length - 1);
                var v = EvaluateFormula(inner, props, resolving);
                return v is null ? null : fn.Item2(v.Value);
            }
        }

        // $varname → recursive resolve (with cycle protection)
        if (s.StartsWith("$", StringComparison.Ordinal) && !ContainsOperator(s))
        {
            var rawName = s.Substring(1);
            // Asciidoctor uses `$var_name` (snake_case); our props store kebab-case. Try both.
            var name = rawName;
            var kebab = rawName.Replace('_', '-').Replace('.', '-');
            // Map common variable references with `_` separator to nested key path.
            // E.g. $heading_h1_font_size → heading.h1-font-size
            if (props.TryGetValue(kebab, out var v1)) name = kebab;
            else if (props.TryGetValue(name, out _)) { /* already set */ }
            else
            {
                // Try last underscore as dot separator (heading_h1_font_size → heading.h1-font-size).
                var lastUnd = rawName.LastIndexOf('_');
                while (lastUnd > 0)
                {
                    var candidate = rawName.Substring(0, lastUnd).Replace('_', '.')
                        + "." + rawName.Substring(lastUnd + 1).Replace('_', '-');
                    candidate = candidate.Replace('_', '-');
                    if (props.ContainsKey(candidate)) { name = candidate; break; }
                    lastUnd = rawName.LastIndexOf('_', lastUnd - 1);
                }
            }
            if (resolving is not null && resolving.Contains(name)) return null;
            resolving ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            resolving.Add(name);
            if (!props.TryGetValue(name, out var raw)) return BuiltinDefault(rawName);
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                return f;
            return EvaluateFormula(raw, props, resolving);
        }

        // Arithmetic: tokenize and apply * / + - left-to-right (no precedence).
        // Sufficient for the asciidoctor-pdf formulas we encounter (unary expressions
        // like `$base_font_size * 2.6`).
        var tokens = TokenizeFormula(s);
        if (tokens.Count == 0) return null;
        float? acc = TokenValue(tokens[0], props, resolving);
        if (acc is null) return null;
        for (int i = 1; i + 1 < tokens.Count; i += 2)
        {
            var op = tokens[i];
            var rhs = TokenValue(tokens[i + 1], props, resolving);
            if (rhs is null) return null;
            acc = op switch
            {
                "*" => acc.Value * rhs.Value,
                "/" => rhs.Value == 0 ? null : acc.Value / rhs.Value,
                "+" => acc.Value + rhs.Value,
                "-" => acc.Value - rhs.Value,
                _ => null,
            };
            if (acc is null) return null;
        }
        return acc;
    }

    /// <summary>
    /// Hardcoded asciidoctor-pdf default values for variables that may appear
    /// in theme formulas but not in a parsed key (e.g. <c>$base_font_size</c>
    /// when the theme doesn't redefine it). Mirrors the defaults from base-theme.yml.
    /// </summary>
    private static float? BuiltinDefault(string rawName) => rawName switch
    {
        "base_font_size" => 10.5f,
        "base_font_size_large" => 13f,    // round(10.5 * 1.25)
        "base_font_size_small" => 9f,     // round(10.5 * 0.85)
        "base_font_size_min" => 7.875f,   // 10.5 * 0.75
        "base_line_height_length" => 12f,
        "vertical_rhythm" => 12f,
        "horizontal_rhythm" => 12f,
        _ => null,
    };

    private static bool ContainsOperator(string s)
    {
        foreach (var c in s) if (c == '*' || c == '+' || c == '-' || c == '/') return true;
        return false;
    }

    private static List<string> TokenizeFormula(string s)
    {
        var tokens = new List<string>();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == ' ') { if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); } continue; }
            if (c == '*' || c == '+' || c == '/' || (c == '-' && sb.Length > 0))
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                tokens.Add(c.ToString());
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    private static float? TokenValue(string tok, Dictionary<string, string> props, HashSet<string>? resolving)
    {
        if (float.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f;
        if (tok.StartsWith("$", StringComparison.Ordinal))
            return EvaluateFormula(tok, props, resolving);
        return null;
    }

    private static float ParseFloatSafe(string s)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
}
