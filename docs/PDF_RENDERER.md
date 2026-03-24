# PDF Renderer Guide

The AdocNet PDF renderer produces PDF 1.4 documents using pure managed C# with no external dependencies. It supports TrueType font embedding with Unicode, images, hyperlinks, tables, headers/footers, and configurable page layout.

## Quick Start

```csharp
byte[] pdf = Adoc.ToPdf(source);
File.WriteAllBytes("output.pdf", pdf);
```

With options:

```csharp
var options = new PdfRenderOptions
{
    FontPath = "/path/to/NotoSans-Regular.ttf",
    ShowPageNumbers = true,
    PageWidth = 612f,  // US Letter
    PageHeight = 792f,
};
byte[] pdf = new PdfRenderer().RenderToBytes(document, options);
```

## Fonts

### Standard Fonts (No Configuration Needed)

The renderer includes four built-in PDF standard fonts:

| Key | Font | Usage |
|-----|------|-------|
| F1 | Helvetica | Body text |
| F2 | Helvetica-Bold | Bold, headings |
| F3 | Helvetica-Oblique | Italic |
| F4 | Courier | Monospace, code blocks |

Standard fonts support ASCII and Latin-1 characters. For full Unicode, use TrueType embedding.

### TrueType Font Embedding (Unicode Support)

Set `FontPath` to embed a `.ttf` font with full Unicode support:

```csharp
var options = new PdfRenderOptions
{
    FontPath = "fonts/NotoSans-Regular.ttf",
    BoldFontPath = "fonts/NotoSans-Bold.ttf",
    ItalicFontPath = "fonts/NotoSans-Italic.ttf",
    MonoFontPath = "fonts/NotoSansMono-Regular.ttf",
};
```

Embedded fonts are automatically subsetted to include only the glyphs used in the document, keeping file sizes small.

**Supported**: `.ttf` files with cmap format 4 (BMP Unicode) or format 12 (full Unicode).

**Not supported**: `.ttc` collections, `.otf` OpenType, font synthesis (bold/italic from a single weight).

## Images

### JPEG

JPEG images are embedded directly using the DCTDecode filter. Dimensions and color space are extracted from SOF markers. Supports RGB, grayscale, and CMYK.

### PNG

PNG images are decompressed, de-filtered, and re-compressed with FlateDecode. Supports 8-bit RGB, RGBA (with alpha via SMask), and grayscale. Non-interlaced only.

### Image Scaling

Images are scaled to fit the content width while maintaining aspect ratio. Images are never upscaled. If an image doesn't fit the current page, a page break is inserted.

### Missing Images

If an image file cannot be found or parsed, a gray placeholder with the alt text is rendered instead.

## Hyperlinks

External URLs are rendered as clickable PDF link annotations. Both bare URLs and AsciiDoc link macros are supported:

```asciidoc
Visit https://example.com for details.
See link:https://docs.example.com[the docs] for more.
```

## Tables

Tables support:

- **Auto-sizing**: Column widths are calculated from content (minimum word width + proportional text volume)
- **Explicit widths**: `[cols="3,1,1"]` distributes widths proportionally
- **Cell wrapping**: Long text wraps within cells, row height adjusts automatically
- **Page breaks**: Large tables split across pages
- **Header repetition**: When `RepeatTableHeader` is true (default), the header row is repeated on each continuation page
- **Alignment**: Left, right, and center alignment via column specs

## Headers and Footers

### Page Numbers

```csharp
var options = new PdfRenderOptions { ShowPageNumbers = true };
// Produces "Page 1", "Page 2", etc. centered in the footer
```

### Custom Templates

```csharp
var options = new PdfRenderOptions
{
    HeaderText = "My Document",
    FooterText = "Page {page} of {pages}",
};
```

| Placeholder | Value |
|-------------|-------|
| `{page}` | Current page number |
| `{pages}` | Total page count |

## Configuration

All options have sensible defaults. `new PdfRenderOptions()` produces A4 output with 1-inch margins and Helvetica fonts.

### Page Geometry

| Property | Default | Description |
|----------|---------|-------------|
| `PageWidth` | 595 (A4) | Page width in points |
| `PageHeight` | 842 (A4) | Page height in points |
| `MarginLeft` | 72 (1 inch) | Left margin |
| `MarginRight` | 72 | Right margin |
| `MarginTop` | 72 | Top margin |
| `MarginBottom` | 72 | Bottom margin |

Presets: `PdfRenderOptions.Letter` (612x792), `PdfRenderOptions.A4`.

### Typography

| Property | Default | Description |
|----------|---------|-------------|
| `FontSize` | 11 | Body text size in points |
| `CodeFontSize` | 9 | Code block font size |
| `TitleFontSize` | 24 | Document title font size |
| `HeadingScale` | 0.85 | Each heading level = previous x scale |
| `LineSpacing` | 1.35 | Line spacing multiplier |

### Visual Styling

| Property | Default | Description |
|----------|---------|-------------|
| `LinkColor` | (0, 0, 0.8) | Hyperlink text color (dark blue) |
| `CodeBackground` | (0.95, 0.95, 0.95) | Code block background (light gray) |
| `AdmonitionBorderWidth` | 2 | Left border width for admonitions |
| `RepeatTableHeader` | true | Repeat header on table page breaks |

### Text Quality

- Lines never start with closing punctuation (`)`, `.`, `;`, `:`, `!`, `?`, etc.)
- Justification spacing is capped at 2x normal word space to prevent ugly gaps
- Last lines of justified paragraphs are left-aligned

## Determinism

PDF output is fully deterministic: identical input always produces byte-identical output across runs and platforms. This is achieved by:

- Fixed creation date in PDF metadata
- Consistent object numbering
- Culture-invariant number formatting
- No random IDs or timestamps

## Limitations

- No kerning or ligatures
- No multi-column layout
- No table of contents in PDF output
- No bookmark/outline tree
- Interlaced PNGs fall back to placeholder
- `.ttc` font collections not supported
