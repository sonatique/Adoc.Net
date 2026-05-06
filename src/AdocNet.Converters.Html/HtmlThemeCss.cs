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
        HtmlTheme.Github => GithubTheme,
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
        /* Horizontal description lists use class="hdlist" — borderless,
           narrow padding so the term/description appear as a compact two-column
           layout. Mirrors asciidoctor's .hdlist CSS rules verbatim. */
        .hdlist > table { border: 0; background: none; width: auto; margin: 1em 0; }
        .hdlist > table > tbody > tr { vertical-align: top; }
        .hdlist > table > tbody > tr > td { border: 0; }
        .hdlist td.hdlist1 { padding: 0 0.625em 0.5em 0; font-weight: bold; vertical-align: top; }
        .hdlist td.hdlist2 { padding: 0 0 0.5em 0; }
        .hdlist td.hdlist2 > p { margin: 0; }
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
        .hl-kw { color: #0000C0; font-weight: 600; }
        .hl-s { color: #A31515; }
        .hl-c { color: #008000; font-style: italic; }
        .hl-n { color: #098658; }
        .hl-t { color: #267F99; }
        .hl-p { color: #505050; }
        .hl-a { color: #8B008B; }
        .hl-pp { color: #808080; }
        """;

    // ── Asciidoctor Theme ───────────────────────────────────────────────
    private const string AsciidoctorTheme = """
        /* Pull the exact font family asciidoctor.css uses so body text renders
           at the same width/x-height as the reference. Without this @import
           Chrome falls back to a local serif (typically Times New Roman) which
           is narrower and visually smaller at the same point size. */
        @import url('https://fonts.googleapis.com/css?family=Open+Sans:300,300italic,400,400italic,600,600italic%7CNoto+Serif:400,400italic,700,700italic%7CDroid+Sans+Mono:400,700');

        *, *::before, *::after { box-sizing: border-box; }
        body {
            font-family: "Noto Serif", "DejaVu Serif", serif;
            font-size: 16px;
            /* Asciidoctor's body line-height is 1; paragraphs override to 1.6.
               Setting body to 1.6 caused inline elements (e.g. inline code spans,
               headings without explicit line-height) to inherit the larger value
               and create extra vertical space. */
            line-height: 1;
            color: rgba(0, 0, 0, 0.8);
            background: #fff;
            margin: 0;
            padding: 0;
        }
        /* Width and padding live on #header/#content/#footer, matching asciidoctor's
           layout where the constrained box is the inner container, not <body>. */
        #header, #content, #footer {
            width: 100%;
            margin: 0 auto;
            max-width: 62.5em;
            padding: 0 0.9375em;
        }
        #content { margin-top: 1.25em; }
        /* Section headings: terracotta, sans-serif, light weight (asciidoctor.css parity).
           Asciidoctor declares line-height: 1.0125em in the base rule, but the
           @media (min-width: 768px) rule overrides it to 1.2 — which is what
           viewports of any reasonable size actually get. Use 1.2 to match. */
        h2, h3, h4, h5, h6 {
            font-family: "Open Sans", "DejaVu Sans", sans-serif;
            color: #ba3925;
            font-weight: 300;
            font-style: normal;
            line-height: 1.2;
            word-spacing: -0.05em;
            margin-top: 1em;
            margin-bottom: 0.5em;
            text-rendering: optimizeLegibility;
        }
        /* Document title: dark colour + horizontal separator below.
           Asciidoctor uses `#content > h1:first-child:not([class])` for this; we
           match the equivalent via `#header h1` since AdocNet wraps the document
           title in `<div id="header">` (matches asciidoctor's full-doc layout). */
        #header h1 {
            font-family: "Open Sans", "DejaVu Sans", sans-serif;
            font-weight: 300;
            font-style: normal;
            color: rgba(0, 0, 0, 0.85);
            line-height: 1.2;
            letter-spacing: -0.01em;
            word-spacing: -0.05em;
            /* When a .details block follows, asciidoctor anchors the horizontal
               rule on the .details block instead of the title. Without details,
               the title gets the rule. We replicate via #header h1:only-child
               + #header .details below. */
            padding-bottom: 8px;
            margin-top: 2.25rem;
            margin-bottom: 0;
            font-size: 2.75em;
            text-rendering: optimizeLegibility;
        }
        /* Author / revision details block — asciidoctor lays it out as a flex row
           BELOW the title and ABOVE a thin grey horizontal rule. The <br> elements
           we emit are hidden; flex separators (\u22c5 / —) take their place. */
        #header h1 + .details {
            border-bottom: 1px solid #dddddf;
            line-height: 1.45;
            padding-top: 0.25em;
            padding-bottom: 0.25em;
            color: rgba(0, 0, 0, 0.6);
            display: flex;
            flex-flow: row wrap;
        }
        #header h1:last-child {
            /* No details block: title carries the horizontal rule itself. */
            border-bottom: 1px solid #dddddf;
        }
        #header .details span:first-child { margin-left: -0.125em; }
        #header .details span.email a { color: rgba(0, 0, 0, 0.85); }
        #header .details br { display: none; }
        #header .details br + span:before { content: "\00a0\2013\00a0"; }
        #header .details br + span.author:before { content: "\00a0\22c5\00a0"; color: rgba(0, 0, 0, 0.85); }
        #header .details br + span#revremark:before { content: "\00a0|\00a0"; }
        /* Reset <pre> browser default margins (asciidoctor parity). Without this,
           Chrome adds 1em top + 1em bottom margin to every <pre>, accumulating
           ~30px per code block and pushing subsequent sections higher. */
        pre { margin: 0; }
        /* Letter-spacing on text-bearing elements (asciidoctor.css applies to
           h1, h2, p, td.content, span.alt, summary). */
        h1, h2, p, td.content, summary { letter-spacing: -0.01em; }
        /* Sizes match asciidoctor.css's @media (min-width: 768px) values */
        h2 { font-size: 2.3125em; }
        h3 { font-size: 1.6875em; }
        h4 { font-size: 1.4375em; }
        h5 { font-size: 1.125em; }
        h6 { font-size: 1em; }

        /* Section separator: thin grey rule between consecutive top-level
           sections. Asciidoctor uses `.sect1 + .sect1 { border-top: ... }`
           plus `.sect1 { padding-bottom: 1.25em }` for breathing room. */
        .sect1 { padding-bottom: 1.25em; }
        .sect1:last-child { padding-bottom: 0; }
        .sect1 + .sect1 { border-top: 1px solid #e7e7e9; }

        /* Typography reset (asciidoctor.css parity). Browser defaults give
           paragraphs `margin: 1em 0`; asciidoctor uses margin-bottom only
           with 1.25em which produces tighter top spacing under headings and
           consistent rhythm between paragraphs.
           font-size: 1.0625rem matches asciidoctor's @media (min-width: 768px)
           rule that bumps body text from 1em (16px) to 17px — without this
           bump, every paragraph and code block is shorter than the reference
           and the visible spacing accumulates as tighter section gaps. */
        p, blockquote, dt, td.content {
            font-size: 1.0625rem;
            line-height: 1.6;
            margin: 0 0 1.25rem;
            text-rendering: optimizeLegibility;
        }
        /* Asciidoctor's `.paragraph:last-child p { margin-bottom: 0 }` only applies
           INSIDE quoteblocks (full selector: `.quoteblock blockquote > .paragraph:last-child p`).
           For top-level sections, the last paragraph keeps its 1.25rem margin-bottom —
           which is part of the visible breathing room before the horizontal separator. */
        .literalblock, .listingblock, .stemblock, .videoblock { margin-bottom: 1.25em; }
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
        /* Listing block <pre>: asciidoctor uses .90625em font-size and
           1em padding inside the .content wrapper. Targeting the wrapper
           variant matches AdocNet's emitted HTML structure.
           color, font-family, and text-rendering match asciidoctor.css's
           computed values exactly so inline content (code spans, monospace
           kerning) renders byte-identical. */
        .listingblock > .content > pre,
        .literalblock pre,
        pre {
            background: #f7f7f8;
            border-radius: 4px;
            padding: 1em;
            overflow-x: auto;
            line-height: 1.45;
            font-size: 0.90625em;
            color: rgba(0, 0, 0, 0.9);
            font-family: "Droid Sans Mono", "DejaVu Sans Mono", monospace;
            text-rendering: optimizeSpeed;
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
        /* Horizontal description lists — asciidoctor renders class="hdlist"
           tables borderless with narrow padding; .hdlist1 is bold. */
        .hdlist > table { border: 0; background: none; width: auto; margin: 1em 0; }
        .hdlist > table > tbody > tr { vertical-align: top; }
        .hdlist > table > tbody > tr > td { border: 0; }
        .hdlist td.hdlist1 { padding: 0 0.625em 0.5em 0; font-weight: bold; vertical-align: top; }
        .hdlist td.hdlist2 { padding: 0 0 0.5em 0; }
        .hdlist td.hdlist2 > p { margin: 0; }
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
        /* Note: .listingblock margin is set above via the asciidoctor parity rule
           (.literalblock, .listingblock, .stemblock, .videoblock { margin-bottom: 1.25em }).
           Don't add an additional `.listingblock { margin: 1em 0 }` here — it would
           reintroduce the unwanted 1em margin-top that asciidoctor doesn't have. */
        .listingblock .title { font-style: italic; }
        /* Inline TOC (default :toc: position): asciidoctor uses border-top +
           border-bottom only, no full border; tight padding. Title is #toctitle
           (div id). TOC list uses Open Sans, NOT the body's serif font. */
        #toc { margin-top: 1em; padding-bottom: 0.5em; border-top: 1px solid #dddddf; border-bottom: 1px solid #e7e7e9; }
        #toctitle {
            color: #7a2518;
            font-family: "Open Sans", "DejaVu Sans", sans-serif;
            font-weight: 300;
            font-size: 1.2em;
            margin-top: 1em;
            margin-bottom: 0.5em;
            line-height: 1.0125em;
        }
        #toc ul { font-family: "Open Sans", "DejaVu Sans", sans-serif; list-style-type: none; padding-left: 1.5em; margin: 0; line-height: 1.5; }
        #toc > ul { padding-left: 0; margin-left: 0.125em; }
        #toc a { color: #2156a5; text-decoration: none; }
        #toc a:hover, #toc a:focus { color: #1d4b8f; text-decoration: underline; }
        /* :toc: left|right layout — body gets toc2 + toc-left/right class,
           the in-header TOC gets class="toc2" and is fixed-positioned in the
           side margin. Activates only on viewports >= 768px (asciidoctor parity).
           Asciidoctor's effective width is 20em on >= 1280px viewports. */
        @media (min-width: 768px) {
            body.toc2 { padding-left: 20em; padding-right: 0; }
            body.toc2 #toc.toc2 {
                margin-top: 0 !important;
                background: #f8f8f7;
                position: fixed;
                width: 20em;
                left: 0;
                top: 0;
                border-right: 1px solid #efefed;
                border-top: 0 !important;
                border-bottom: 0 !important;
                z-index: 1000;
                padding: 1.25em 1em;
                height: 100%;
                overflow: auto;
            }
            body.toc2 #toc.toc2 #toctitle { margin-top: 0; margin-bottom: 0.8rem; font-size: 1.2em; }
            body.toc2 #toc.toc2 > ul { font-size: 0.9em; margin-bottom: 0; }
            body.toc2 #toc.toc2 ul ul { margin-left: 0; padding-left: 1em; }
            body.toc2.toc-right { padding-left: 0; padding-right: 20em; }
            body.toc2.toc-right #toc.toc2 {
                border-right-width: 0;
                border-left: 1px solid #efefed;
                left: auto;
                right: 0;
            }
        }
        #footnotes { margin-top: 2em; border-top: 1px solid #ddddd8; padding-top: 0.5em; font-size: 0.875em; }
        mark, .highlight { background: #ffc14f; }
        .verseblock pre { font-family: "Noto Serif", "DejaVu Serif", serif; background: none; border: none; padding: 0; white-space: pre-wrap; }
        .hl-kw { color: #7a2518; font-weight: 600; }
        .hl-s { color: #19407c; }
        .hl-c { color: #6a6a6a; font-style: italic; }
        .hl-n { color: #116644; }
        .hl-t { color: #19407c; }
        .hl-p { color: rgba(0,0,0,0.6); }
        .hl-a { color: #7a2518; }
        .hl-pp { color: #808080; }
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
        .hl-kw { color: #333; font-weight: 700; }
        .hl-s { color: #555; }
        .hl-c { color: #999; font-style: italic; }
        .hl-n { color: #555; }
        .hl-t { color: #333; }
        .hl-p { color: #777; }
        .hl-a { color: #555; }
        .hl-pp { color: #999; }
        """;

    // ── Github Theme ──────────────────────────────────────────────────
    private const string GithubTheme = """
        *, *::before, *::after { box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "Noto Sans", Helvetica, Arial, sans-serif;
            font-size: 16px;
            line-height: 1.5;
            color: #1f2328;
            background: #fff;
            max-width: 1012px;
            margin: 0 auto;
            padding: 2rem 1.5rem;
        }
        h1, h2, h3, h4, h5, h6 {
            font-weight: 600;
            color: #1f2328;
            margin-top: 1.5em;
            margin-bottom: 0.5em;
            line-height: 1.25;
        }
        h1 { font-size: 2em; border-bottom: 1px solid #d1d9e0; padding-bottom: 0.3em; }
        h2 { font-size: 1.5em; border-bottom: 1px solid #d1d9e0; padding-bottom: 0.3em; }
        h3 { font-size: 1.25em; }
        a { color: #0969da; text-decoration: none; }
        a:hover { text-decoration: underline; }
        code, .monospace {
            font-family: ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, "Liberation Mono", monospace;
            font-size: 0.85em;
            background: rgba(175, 184, 193, 0.2);
            padding: 0.2em 0.4em;
            border-radius: 6px;
        }
        pre {
            background: #f6f8fa;
            border: 1px solid #d1d9e0;
            border-radius: 6px;
            padding: 1em;
            overflow-x: auto;
            line-height: 1.45;
        }
        pre code { background: none; padding: 0; border-radius: 0; }
        blockquote, .quoteblock {
            border-left: 4px solid #d1d9e0;
            margin-left: 0;
            padding: 0.5em 1em;
            color: #636c76;
        }
        .quoteblock .attribution { font-style: italic; color: #636c76; margin-top: 0.5em; }
        table { border-collapse: collapse; width: 100%; margin: 1em 0; }
        th, td { border: 1px solid #d1d9e0; padding: 6px 13px; text-align: left; }
        th { background: #f6f8fa; font-weight: 600; }
        .stripes-odd tr:nth-child(odd) td { background: #f6f8fa; }
        .stripes-even tr:nth-child(even) td { background: #f6f8fa; }
        .admonitionblock { margin: 1em 0; padding: 0.75em 1em; border-radius: 6px; border-left: 4px solid; }
        .admonitionblock.note { border-color: #0969da; background: #ddf4ff; }
        .admonitionblock.tip { border-color: #1a7f37; background: #dafbe1; }
        .admonitionblock.warning { border-color: #9a6700; background: #fff8c5; }
        .admonitionblock.caution { border-color: #cf222e; background: #ffebe9; }
        .admonitionblock.important { border-color: #8250df; background: #fbefff; }
        .admonitionblock .icon { font-weight: 700; text-transform: uppercase; margin-bottom: 0.25em; }
        .imageblock { text-align: center; margin: 1em 0; }
        .imageblock img { max-width: 100%; height: auto; }
        .title { font-weight: 600; margin-bottom: 0.25em; }
        .sidebarblock { background: #f6f8fa; border: 1px solid #d1d9e0; border-radius: 6px; padding: 1em; margin: 1em 0; }
        .exampleblock { border: 1px solid #d1d9e0; border-radius: 6px; padding: 1em; margin: 1em 0; }
        .listingblock { margin: 1em 0; }
        .listingblock .title { font-size: 0.85em; color: #636c76; }
        #toc { margin: 1.5em 0; padding: 1em; background: #f6f8fa; border: 1px solid #d1d9e0; border-radius: 6px; }
        #toc .title { font-size: 1.1em; font-weight: 600; margin-bottom: 0.5em; }
        #toc ul { list-style: none; padding-left: 1.5em; }
        #toc > ul { padding-left: 0; }
        #toc a { color: #0969da; }
        #footnotes { margin-top: 2em; border-top: 1px solid #d1d9e0; padding-top: 1em; font-size: 0.85em; }
        mark, .highlight { background: #fff8c5; padding: 0.1em 0.2em; }
        .verseblock pre { font-family: inherit; background: none; border: none; padding: 0; white-space: pre-wrap; }
        .hl-kw { color: #cf222e; font-weight: 600; }
        .hl-s { color: #0a3069; }
        .hl-c { color: #6e7781; font-style: italic; }
        .hl-n { color: #0550ae; }
        .hl-t { color: #953800; }
        .hl-p { color: #1f2328; }
        .hl-a { color: #8250df; }
        .hl-pp { color: #cf222e; }
        """;
}
