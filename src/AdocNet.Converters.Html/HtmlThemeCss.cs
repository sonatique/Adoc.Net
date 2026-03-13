namespace AdocNet.Converters.Html;

/// <summary>
/// Provides CSS strings for built-in HTML themes.
/// </summary>
internal static class HtmlThemeCss
{
    /// <summary>Returns the CSS for the given theme, or null if <see cref="HtmlTheme.None"/>.</summary>
    public static string? GetCss(HtmlTheme theme) => theme switch
    {
        HtmlTheme.Default => DefaultTheme,
        HtmlTheme.Asciidoctor => AsciidoctorTheme,
        HtmlTheme.Clean => CleanTheme,
        _ => null,
    };

    // ── Default Theme ───────────────────────────────────────────────────
    private const string DefaultTheme = """
        *, *::before, *::after { box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
            font-size: 16px;
            line-height: 1.6;
            color: #333;
            background: #fff;
            max-width: 960px;
            margin: 2rem auto;
            padding: 0 1.5rem;
        }
        h1, h2, h3, h4, h5, h6 {
            color: #222;
            margin-top: 1.5em;
            margin-bottom: 0.5em;
            line-height: 1.3;
        }
        h1 { font-size: 2em; border-bottom: 2px solid #e0e0e0; padding-bottom: 0.3em; }
        h2 { font-size: 1.5em; border-bottom: 1px solid #e0e0e0; padding-bottom: 0.2em; }
        h3 { font-size: 1.25em; }
        a { color: #2563eb; text-decoration: none; }
        a:hover { text-decoration: underline; }
        code, .monospace {
            font-family: "SFMono-Regular", Consolas, "Liberation Mono", Menlo, monospace;
            font-size: 0.9em;
            background: #f5f5f5;
            padding: 0.15em 0.3em;
            border-radius: 3px;
        }
        pre {
            background: #f5f5f5;
            border: 1px solid #e0e0e0;
            border-radius: 4px;
            padding: 1em;
            overflow-x: auto;
            line-height: 1.45;
        }
        pre code { background: none; padding: 0; }
        blockquote, .quoteblock {
            border-left: 4px solid #d0d0d0;
            margin-left: 0;
            padding: 0.5em 1em;
            color: #555;
        }
        .quoteblock .attribution { font-style: italic; color: #777; margin-top: 0.5em; }
        table {
            border-collapse: collapse;
            width: 100%;
            margin: 1em 0;
        }
        th, td {
            border: 1px solid #d0d0d0;
            padding: 0.5em 0.75em;
            text-align: left;
        }
        th { background: #f5f5f5; font-weight: 600; }
        .stripes-odd tr:nth-child(odd) td { background: #fafafa; }
        .stripes-even tr:nth-child(even) td { background: #fafafa; }
        .admonitionblock {
            margin: 1em 0;
            padding: 0.75em 1em;
            border-radius: 4px;
            border-left: 4px solid;
        }
        .admonitionblock.note { border-color: #2563eb; background: #eff6ff; }
        .admonitionblock.tip { border-color: #16a34a; background: #f0fdf4; }
        .admonitionblock.warning { border-color: #d97706; background: #fffbeb; }
        .admonitionblock.caution { border-color: #dc2626; background: #fef2f2; }
        .admonitionblock.important { border-color: #7c3aed; background: #f5f3ff; }
        .admonitionblock .icon { font-weight: 700; text-transform: uppercase; margin-bottom: 0.25em; }
        .imageblock { text-align: center; margin: 1em 0; }
        .imageblock img { max-width: 100%; height: auto; }
        .title { font-weight: 600; margin-bottom: 0.25em; }
        .sidebarblock {
            background: #f9f9f9;
            border: 1px solid #e0e0e0;
            border-radius: 4px;
            padding: 1em;
            margin: 1em 0;
        }
        .exampleblock {
            border: 1px solid #e0e0e0;
            border-radius: 4px;
            padding: 1em;
            margin: 1em 0;
        }
        .listingblock { margin: 1em 0; }
        .listingblock .title { font-size: 0.9em; color: #555; }
        #toc { margin: 1.5em 0; padding: 1em; background: #fafafa; border: 1px solid #e0e0e0; border-radius: 4px; }
        #toc .title { font-size: 1.1em; font-weight: 700; margin-bottom: 0.5em; }
        #toc ul { list-style: none; padding-left: 1.5em; }
        #toc > ul { padding-left: 0; }
        #toc a { color: #333; }
        #toc a:hover { color: #2563eb; }
        #footnotes { margin-top: 2em; border-top: 1px solid #e0e0e0; padding-top: 1em; font-size: 0.9em; }
        mark, .highlight { background: #fef08a; padding: 0.1em 0.2em; }
        .verseblock pre { font-family: inherit; background: none; border: none; padding: 0; white-space: pre-wrap; }
        """;

    // ── Asciidoctor Theme ───────────────────────────────────────────────
    private const string AsciidoctorTheme = """
        *, *::before, *::after { box-sizing: border-box; }
        body {
            font-family: "Noto Serif", "DejaVu Serif", serif;
            font-size: 16px;
            line-height: 1.6;
            color: rgba(0, 0, 0, 0.8);
            background: #fff;
            max-width: 960px;
            margin: 0 auto;
            padding: 0 1.5rem 2rem;
        }
        h1, h2, h3, h4, h5, h6 {
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
            color: #ba3925;
            font-weight: 300;
            margin-top: 1.5em;
            margin-bottom: 0.5em;
        }
        h1 { font-size: 2.125em; }
        h2 { font-size: 1.6875em; }
        h3 { font-size: 1.375em; }
        h4 { font-size: 1.125em; }
        a { color: #2156a5; text-decoration: none; }
        a:hover { text-decoration: underline; }
        code, .monospace {
            font-family: "Droid Sans Mono", "DejaVu Sans Mono", monospace;
            font-size: 0.9em;
            color: rgba(0, 0, 0, 0.9);
        }
        p code, li code, td code {
            background: #f7f7f8;
            border: 1px solid #e0e0dc;
            border-radius: 4px;
            padding: 0.1em 0.4em;
        }
        pre {
            background: #f7f7f8;
            border-radius: 4px;
            padding: 1em;
            overflow-x: auto;
            line-height: 1.45;
        }
        pre code { background: none; border: none; padding: 0; }
        .quoteblock {
            margin: 1em 0;
            padding: 0.25em 1.5em;
            border-left: 5px solid #e0e0dc;
        }
        .quoteblock .attribution { font-size: 0.9em; color: rgba(0, 0, 0, 0.6); margin-top: 0.75em; }
        table {
            border-collapse: collapse;
            width: 100%;
            margin: 1em 0;
            background: #fff;
        }
        th, td { border: 1px solid #dedede; padding: 0.5em 0.625em; }
        th { background: #f7f8f7; }
        .stripes-odd tr:nth-child(odd) td { background: #f8f8f7; }
        .stripes-even tr:nth-child(even) td { background: #f8f8f7; }
        .admonitionblock {
            margin: 1em 0;
        }
        .admonitionblock table { border: 0; background: none; width: 100%; }
        .admonitionblock td.icon { text-align: center; width: 80px; font-size: 1.5em; font-weight: 700; }
        .admonitionblock.note td.icon { color: #19407c; }
        .admonitionblock.tip td.icon { color: #111; }
        .admonitionblock.warning td.icon { color: #bf6900; }
        .admonitionblock.caution td.icon { color: #bf3400; }
        .admonitionblock.important td.icon { color: #bf0000; }
        .imageblock { text-align: center; margin: 1em 0; }
        .imageblock img { max-width: 100%; height: auto; }
        .title { font-style: italic; font-weight: 400; color: #7a2518; }
        .sidebarblock {
            background: #f3f3f2;
            border-radius: 4px;
            padding: 1.25em;
            margin: 1em 0;
        }
        .exampleblock { border: 1px solid #e6e6e6; border-radius: 4px; padding: 1em; margin: 1em 0; }
        .listingblock { margin: 1em 0; }
        .listingblock .title { font-style: italic; }
        #toc { margin: 1em 0; padding: 1.25em; background: #f8f8f7; border: 1px solid #e0e0dc; }
        #toc .title { color: #7a2518; }
        #toc ul { list-style: none; padding-left: 1.25em; }
        #toc > ul { padding-left: 0; }
        #toc a { color: #2156a5; }
        #footnotes { margin-top: 2em; border-top: 1px solid #ddddd8; padding-top: 0.5em; font-size: 0.875em; }
        mark, .highlight { background: #ffc14f; }
        .verseblock pre { font-family: "Noto Serif", "DejaVu Serif", serif; background: none; border: none; padding: 0; white-space: pre-wrap; }
        """;

    // ── Clean Theme ─────────────────────────────────────────────────────
    private const string CleanTheme = """
        *, *::before, *::after { box-sizing: border-box; }
        body {
            font-family: Georgia, "Times New Roman", serif;
            font-size: 18px;
            line-height: 1.8;
            color: #111;
            background: #fff;
            max-width: 720px;
            margin: 3rem auto;
            padding: 0 1.5rem;
        }
        h1, h2, h3, h4, h5, h6 {
            font-family: inherit;
            font-weight: 700;
            color: #111;
            margin-top: 2em;
            margin-bottom: 0.5em;
        }
        h1 { font-size: 1.8em; }
        h2 { font-size: 1.4em; }
        h3 { font-size: 1.15em; }
        a { color: #111; text-decoration: underline; }
        code, .monospace {
            font-family: Menlo, Consolas, monospace;
            font-size: 0.85em;
        }
        pre {
            border-left: 3px solid #ccc;
            padding: 1em 1.5em;
            overflow-x: auto;
            line-height: 1.5;
        }
        pre code { background: none; }
        .quoteblock { border-left: 3px solid #ccc; margin: 1em 0; padding: 0.5em 1.5em; font-style: italic; }
        .quoteblock .attribution { font-style: normal; font-size: 0.9em; color: #555; }
        table { border-collapse: collapse; width: 100%; margin: 1.5em 0; }
        th, td { border-bottom: 1px solid #ddd; padding: 0.5em 0.75em; text-align: left; }
        th { font-weight: 700; }
        .admonitionblock { margin: 1.5em 0; padding: 1em; border: 1px solid #ddd; }
        .admonitionblock .icon { font-weight: 700; text-transform: uppercase; font-size: 0.85em; letter-spacing: 0.05em; }
        .imageblock { text-align: center; margin: 1.5em 0; }
        .imageblock img { max-width: 100%; height: auto; }
        .title { font-weight: 700; font-size: 0.9em; text-transform: uppercase; letter-spacing: 0.05em; margin-bottom: 0.25em; }
        .sidebarblock { border: 1px solid #ddd; padding: 1em; margin: 1.5em 0; }
        .exampleblock { border: 1px solid #ddd; padding: 1em; margin: 1.5em 0; }
        .listingblock { margin: 1.5em 0; }
        #toc { margin: 2em 0; }
        #toc .title { text-transform: uppercase; letter-spacing: 0.1em; font-size: 0.85em; }
        #toc ul { list-style: none; padding-left: 1.5em; }
        #toc > ul { padding-left: 0; }
        #toc a { color: #111; }
        #footnotes { margin-top: 3em; border-top: 1px solid #ddd; padding-top: 1em; font-size: 0.85em; }
        mark, .highlight { background: #ff0; }
        .verseblock pre { font-family: Georgia, serif; background: none; border: none; padding: 0; white-space: pre-wrap; }
        """;
}
