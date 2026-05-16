# Third-Party Notices

AdocNet is MIT-licensed. This document acknowledges the third-party
content bundled in or redistributed by AdocNet packages.

## Bundled in `AdocNet.Converters.Epub`

The EPUB converter embeds the standard asciidoctor-epub3 asset payload so
generated EPUBs match the reference renderer byte-for-byte. All bundled
assets are themselves OSS-licensed under terms compatible with MIT
redistribution.

### Fonts

| File | Source | License |
|------|--------|---------|
| `notoserif-regular-latin.ttf`<br>`notoserif-italic-latin.ttf`<br>`notoserif-bold-latin.ttf`<br>`notoserif-bolditalic-latin.ttf` | Google Noto Serif | SIL Open Font License 1.1 |
| `mplus1p-regular-latin.ttf`<br>`mplus1p-light-latin.ttf`<br>`mplus1p-bold-latin.ttf` | M+ FONTS (M+ 1p, Latin subset) | SIL Open Font License 1.1 |
| `mplus1mn-regular-ascii-conums.ttf`<br>`mplus1mn-italic-ascii.ttf`<br>`mplus1mn-bold-ascii.ttf`<br>`mplus1mn-bolditalic-ascii.ttf` | M+ FONTS (M+ 1mn, ASCII subset, with callout glyph overrides) | SIL Open Font License 1.1 |
| `fa-solid-900.ttf` | Font Awesome 5 Free Solid | SIL Open Font License 1.1 |
| `assorted-icons.ttf` | asciidoctor-epub3 custom subset | SIL Open Font License 1.1 |

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

## License texts

### SIL Open Font License 1.1

Full text: <https://scripts.sil.org/OFL>

Summary: redistribution of the fonts is permitted, including bundling
inside other software, provided the fonts are not sold by themselves,
their reserved names are preserved when modified, and this notice is
distributed with any redistribution.

### MIT License (asciidoctor-epub3)

Copyright (c) 2014-2020 Dan Allen and the Asciidoctor Project.

Permission is hereby granted, free of charge, to any person obtaining a
copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions: the above copyright notice and this permission
notice shall be included in all copies or substantial portions of the
Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.

## How to verify

Run `unzip -l` on the published `AdocNet.Converters.Epub.{version}.nupkg`
to see which assets are embedded. All embedded resources live under
`src/AdocNet.Converters.Epub/Resources/` in the source tree.

## Reporting issues

If you believe a bundled asset is missing attribution or is being
distributed under incompatible terms, please open an issue at
<https://github.com/sonatique/Adoc.Net/issues>.
