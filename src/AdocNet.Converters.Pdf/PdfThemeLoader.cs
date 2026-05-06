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

            // Typography. Asciidoctor-pdf themes use heading.h1-font-size (nested
            // under heading:) while our older templates used heading-h1.font-size
            // (separate key). Try the asciidoctor form first, then the legacy form.
            FontSize = GetFloat(props, "base.font-size") ?? 11f,
            CodeFontSize = GetFloat(props, "code.font-size") ?? GetFloat(props, "codespan.font-size") ?? 9f,
            TitleFontSize = GetFloat(props, "heading.h1-font-size")
                          ?? GetFloat(props, "heading-h1.font-size")
                          ?? GetFloat(props, "title-page.title.font-size") ?? 24f,
            LineSpacing = GetFloat(props, "base.line-height") ?? 1.35f,
            TitleLineHeight = GetFloat(props, "heading.h1-line-height") ?? GetFloat(props, "heading-h1.line-height"),

            // Per-heading sizes
            Heading2FontSize = GetFloat(props, "heading.h2-font-size") ?? GetFloat(props, "heading-h2.font-size"),
            Heading3FontSize = GetFloat(props, "heading.h3-font-size") ?? GetFloat(props, "heading-h3.font-size"),
            Heading4FontSize = GetFloat(props, "heading.h4-font-size") ?? GetFloat(props, "heading-h4.font-size"),
            Heading5FontSize = GetFloat(props, "heading.h5-font-size") ?? GetFloat(props, "heading-h5.font-size"),

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
        => props.TryGetValue(key, out var v) ? v : null;

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
