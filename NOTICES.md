# Third-Party Notices

AdocNet is MIT-licensed. This document acknowledges the third-party content
bundled in or redistributed by AdocNet packages. The license texts referenced
below are included at the end of this file.

This file is packed into every AdocNet package that embeds third-party assets
(`AdocNet.Converters.Epub`, `AdocNet.Converters.Pdf`, and the `AdocNet`
meta-package that depends on them).

## Bundled in `AdocNet.Converters.Epub`

The EPUB converter embeds the standard asciidoctor-epub3 asset payload so
generated EPUBs match the reference renderer.

### Fonts

| File | Source | License |
|------|--------|---------|
| `notoserif-regular-latin.ttf`<br>`notoserif-italic-latin.ttf`<br>`notoserif-bold-latin.ttf`<br>`notoserif-bolditalic-latin.ttf` | Google Noto Serif | Apache License 2.0 |
| `mplus1p-regular-latin.ttf`<br>`mplus1p-light-latin.ttf`<br>`mplus1p-bold-latin.ttf` | M+ FONTS (M+ 1p, Latin subset) | M+ FONTS License |
| `mplus1mn-regular-ascii-conums.ttf`<br>`mplus1mn-italic-ascii.ttf`<br>`mplus1mn-bold-ascii.ttf`<br>`mplus1mn-bolditalic-ascii.ttf` | M+ FONTS (M+ 1mn, ASCII subset, with callout glyph overrides) | M+ FONTS License |
| `fa-solid-900.ttf` | Font Awesome 6 Free (Solid) | SIL Open Font License 1.1 (font); icon designs CC BY 4.0 |
| `assorted-icons.ttf` | asciidoctor-epub3 custom subset | SIL Open Font License 1.1 |
| `DejaVuSans.ttf` | DejaVu Sans (PDF Unicode fallback for arrows, check marks, geometric shapes, math symbols) | Bitstream Vera Fonts License + public-domain additions |

### Stylesheets

| File | Source | License |
|------|--------|---------|
| `epub3.css`<br>`epub3-css3-only.css`<br>`epub3-fonts.css` | asciidoctor-epub3 | MIT |

### Images

| File | Source | License |
|------|--------|---------|
| `avatar.jpg` (chapter byline default avatar)<br>`headshot.jpg` (default headshot) | asciidoctor-epub3 | MIT |

### Reader-specific metadata

| File | Source | License |
|------|--------|---------|
| `com.apple.ibooks.display-options.xml` | asciidoctor-epub3 | MIT |

## Bundled in `AdocNet.Converters.Pdf`

The PDF converter embeds Font Awesome icon fonts for admonition and inline icons.

| File | Source | License |
|------|--------|---------|
| `fa-solid.ttf` | Font Awesome 5 Free (Solid) | SIL Open Font License 1.1 (font); icon designs CC BY 4.0 |
| `fa-regular.ttf` | Font Awesome 5 Free (Regular) | SIL Open Font License 1.1 (font); icon designs CC BY 4.0 |

## Derived in `AdocNet.Converters.Html`

The `HtmlTheme.Asciidoctor` stylesheet is a port of Asciidoctor's default
`asciidoctor.css` (`src/AdocNet.Converters.Html/HtmlThemeCss.cs`), used under the
Asciidoctor project's MIT license (see below).

## License texts

### SIL Open Font License 1.1

Full text: <https://openfontlicense.org/>

Summary: redistribution of the fonts is permitted, including bundling inside
other software, provided the fonts are not sold by themselves, their reserved
names are preserved when modified, and this notice is distributed with any
redistribution.

### Apache License 2.0 (Google Noto Serif)

Full text: <https://www.apache.org/licenses/LICENSE-2.0>

Summary: a permissive license allowing redistribution and modification provided
the license, copyright notice, and (where present) NOTICE attributions are
included with redistributions.

### M+ FONTS License

Full text: <https://web.archive.org/web/20211118100135/https://mplus-fonts.osdn.jp/about-en.html>

Summary: the M+ fonts may be freely used, redistributed (including bundled in
other software), and modified, with no royalty and no requirement to credit,
provided the fonts are not misrepresented as the original when modified.

### Creative Commons Attribution 4.0 (Font Awesome icon designs)

Full text: <https://creativecommons.org/licenses/by/4.0/>

Summary: the Font Awesome Free icon designs may be used and redistributed with
attribution to Font Awesome (<https://fontawesome.com>).

### MIT License (asciidoctor / asciidoctor-epub3)

Copyright (c) 2014-2020 Dan Allen and the Asciidoctor Project.

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions: the above copyright notice and this
permission notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.

## How to verify

Run `unzip -l` on a published `AdocNet.Converters.{Epub,Pdf}.{version}.nupkg`
to see which assets are embedded. Embedded resources live under
`src/AdocNet.Converters.Epub/Resources/` and
`src/AdocNet.Converters.Pdf/` in the source tree.

## Reporting issues

If you believe a bundled asset is missing attribution or is being distributed
under incompatible terms, please open an issue at
<https://github.com/sonatique/Adoc.Net/issues>.
