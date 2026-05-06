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
    public static PdfRenderOptions Load(string themePath, string? fontsDir = null)
    {
        var themeDir = Path.GetDirectoryName(Path.GetFullPath(themePath)) ?? ".";
        fontsDir ??= themeDir;
        var lines = File.ReadAllLines(themePath);
        var props = ParseYaml(lines);
        return BuildOptions(props, fontsDir, themeDir);
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
    internal static PdfRenderOptions BuildOptions(Dictionary<string, string> props, string fontsDir, string? themeDir = null)
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

            // Page geometry
            MarginTop = marginTop,
            MarginRight = marginRight,
            MarginBottom = marginBottom,
            MarginLeft = marginLeft,

            // Typography
            FontSize = GetFloat(props, "base.font-size") ?? 11f,
            CodeFontSize = GetFloat(props, "code.font-size") ?? GetFloat(props, "codespan.font-size") ?? 9f,
            TitleFontSize = GetFloat(props, "heading-h1.font-size") ?? GetFloat(props, "title-page.title.font-size") ?? 24f,
            LineSpacing = GetFloat(props, "base.line-height") ?? 1.35f,
            TitleLineHeight = GetFloat(props, "heading-h1.line-height"),

            // Per-heading sizes
            Heading2FontSize = GetFloat(props, "heading-h2.font-size"),
            Heading3FontSize = GetFloat(props, "heading-h3.font-size"),
            Heading4FontSize = GetFloat(props, "heading-h4.font-size"),
            Heading5FontSize = GetFloat(props, "heading-h5.font-size"),

            // Per-heading margin-bottom
            Heading2MarginBottom = GetFloat(props, "heading-h2.margin-bottom"),
            Heading3MarginBottom = GetFloat(props, "heading-h3.margin-bottom"),
            Heading4MarginBottom = GetFloat(props, "heading-h4.margin-bottom"),
            Heading5MarginBottom = GetFloat(props, "heading-h5.margin-bottom"),

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
                      ?? new PdfColor(0.066f, 0.337f, 0.624f), // #115fa6 fallback

            // Table styling
            TableBorderColor = ParseColor(GetString(props, "table.border-color")),
            TableHeaderBackground = ParseColor(GetString(props, "table.head.background-color")),
            TableHeaderFontColor = ParseColor(GetString(props, "table.head.font-color")),

            // Code block styling
            CodeBorderColor = ParseColor(GetString(props, "code.border-color"))
                            ?? new PdfColor(0.8f, 0.8f, 0.8f),

            // Inline codespan styling - only set background if theme explicitly specifies it (matches Asciidoctor default)
            CodespanBackground = ParseColor(GetString(props, "codespan.background-color")),

            // Footer image (SVG logo)
            FooterImagePath = ResolveFooterImage(props, themeDir ?? fontsDir),
            FooterImageWidth = ParseFooterImageWidth(props),

            // Paragraph spacing from prose or base margin
            ParagraphSpacingAfter = GetFloat(props, "prose.margin-bottom")
                                  ?? GetFloat(props, "base.margin-bottom")
                                  ?? 8f,

            // Section spacing from heading margins
            SectionSpacing = GetFloat(props, "heading-h2.margin-top") ?? 16f,

            // Title margins
            TitleMarginTop = GetFloat(props, "heading-h1.margin-top") ?? 0f,
            TitleMarginBottom = GetFloat(props, "heading-h1.margin-bottom") ?? 16f,
        };

        return options;
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

        return ResolveFontCatalogPath(props, headingFamily, "normal", fontsDir);
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
        // Format: [top, right, bottom, left] or single number
        var trimmed = value.Trim().TrimStart('[').TrimEnd(']');
        var parts = trimmed.Split(',');

        if (parts.Length == 4)
        {
            top = ParseFloatSafe(parts[0].Trim());
            right = ParseFloatSafe(parts[1].Trim());
            bottom = ParseFloatSafe(parts[2].Trim());
            left = ParseFloatSafe(parts[3].Trim());
        }
        else if (parts.Length == 1)
        {
            top = right = bottom = left = ParseFloatSafe(parts[0].Trim());
        }
    }

    // ── Color parsing ────────────────────────────────────────────────────

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
        => props.TryGetValue(key, out var v) ? v : null;

    private static float? GetFloat(Dictionary<string, string> props, string key)
    {
        if (!props.TryGetValue(key, out var v)) return null;
        return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : null;
    }

    private static float ParseFloatSafe(string s)
        => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;
}
