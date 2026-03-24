# AdocNet v1.0.0-beta.3 — Rendering Design

> Phase P01 — design document, no code.
> Reference: `docs/CONTEXT-PDF.md` for current renderer internals.

---

## 1. TrueType Font System

### 1.1 TTF Tables Parsed

| Tag | Purpose | Currently parsed | Beta.3 change |
|-----|---------|-----------------|---------------|
| `head` | Global font metrics (unitsPerEm) | Yes | No change |
| `hhea` | Horizontal header (ascender, descender, numberOfHMetrics) | Yes | No change |
| `hmtx` | Horizontal metrics (glyph advance widths) | Yes | No change |
| `cmap` | Character-to-glyph mapping | Yes (format 4 only) | Add format 12 for full Unicode |
| `OS/2` | OS/2 and Windows metrics (sTypoAscender/Descender) | Yes | No change |
| `name` | Font names (PostScript name) | Yes | No change |
| `maxp` | Maximum profile (numGlyphs) | Yes (numGlyphs only) | No change |
| `glyf` | Glyph outlines | **No** | Add for subsetting — copy glyph data by offset |
| `loca` | Glyph location index | **No** | Add for subsetting — maps glyph ID to offset in glyf |
| `post` | PostScript name mapping | **No** | Not needed (CIDFont uses glyph IDs directly) |
| `cvt ` | Control value table | **No** | Copy as-is during subsetting (hinting) |
| `fpgm` | Font program | **No** | Copy as-is during subsetting (hinting) |
| `prep` | CVT program | **No** | Copy as-is during subsetting (hinting) |

### 1.2 Glyph-to-CID Mapping Strategy

CIDFont Type 2 uses glyph IDs directly as CIDs. The mapping is:

```
Unicode code point → cmap lookup → glyph ID = CID
```

Text is encoded as hex glyph IDs: `<0041 006F 0072>` for "Aor".
This is already implemented in beta.2. No change needed.

### 1.3 Font Subsetting

**Goal**: Reduce embedded font size from ~50-500KB to ~5-50KB by including only used glyphs.

**Algorithm**:
1. Collect all unique code points used during rendering (already tracked in `_usedCodePoints`)
2. Map code points to glyph IDs via cmap
3. Build a subset containing only the used glyphs

**Tables to rebuild**:
- `glyf` — extract only the used glyph data (referenced by loca offsets)
- `loca` — rebuild with new offsets for the subset
- `hmtx` — include only widths for used glyphs
- `cmap` — rebuild format 4 (or 12) with only used mappings

**Tables to copy as-is**:
- `head`, `hhea`, `OS/2`, `name`, `maxp` (update numGlyphs), `post` (if present)
- `cvt `, `fpgm`, `prep` (hinting tables — copy if present)

**Tables to skip**:
- `GPOS`, `GSUB`, `kern`, `GDEF` — OpenType layout tables, not needed for PDF
- `gasp`, `DSIG`, `LTSH` — display/signing tables

**Composite glyph handling**: Glyph data in `glyf` may reference other glyphs (composite/compound glyphs). The subsetter must recursively include all referenced component glyphs.

### 1.4 ToUnicode CMap Structure

```
/CIDInit /ProcSet findresource begin
12 dict begin
begincmap
/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def
/CMapName /Adobe-Identity-UCS def
/CMapType 2 def
1 begincodespacerange
<0000> <FFFF>
endcodespacerange
{N} beginbfchar
<{glyphId}> <{unicodeHex}>
...
endbfchar
endcmap
CMapName currentdict /CMap defineresource pop
end
end
```

Each entry maps a glyph ID (CID) to the Unicode code point it represents. This enables copy/paste and text search in PDF viewers.

### 1.5 Glyph Widths — PDF `/W` Array

For CIDFont Type 2, widths are specified per-glyph:

```
/W [0 [500] 36 [722 667 722 ...] 68 [556 611 556 ...]]
```

Format: `startCID [width1 width2 ...]` — consecutive glyph widths from startCID.
Widths are in units of 1/1000 of the font's unitsPerEm scaled to 1000.

### 1.6 Missing Glyph Fallback

When a code point has no glyph in the primary font:
1. Use glyph ID 0 (the `.notdef` glyph — typically a rectangle or blank)
2. Width falls back to `unitsPerEm / 2` (already implemented in `GetGlyphWidth`)

No font cascading — this keeps the implementation simple and deterministic.

### 1.7 Font Bundling Decision

**Decision: Hybrid approach.**

- **Standard fonts** (Helvetica, Courier) remain the default — no font files needed
- **User-provided paths** via `PdfRenderOptions.FontPath` etc. — already supported
- **No bundled embedded resource** — avoids license issues with font files, keeps package small

The standard fonts already cover ASCII + Latin-1 via WinAnsiEncoding. TrueType embedding is opt-in for full Unicode support.

---

## 2. Image Embedding

### 2.1 JPEG Dimensions

Extracted from SOF0 (0xC0) or SOF2 (0xC2) markers:
- Byte 4: bits per component
- Bytes 5-6: height (big-endian uint16)
- Bytes 7-8: width (big-endian uint16)
- Byte 9: number of components (1=gray, 3=RGB, 4=CMYK)

**Already implemented.** No changes needed.

### 2.2 JPEG XObject Structure

```
<< /Type /XObject /Subtype /Image
   /Width {w} /Height {h}
   /ColorSpace /DeviceRGB  (or /DeviceGray, /DeviceCMYK)
   /BitsPerComponent {bpc}
   /Filter /DCTDecode
   /Length {dataLength} >>
stream
{raw JPEG data}
endstream
```

**Already implemented.** No changes needed.

### 2.3 PNG Decompression

1. Collect all IDAT chunks into a single byte stream
2. Skip 2-byte zlib header
3. Decompress with `DeflateStream`
4. De-filter scanlines (each row has a 1-byte filter prefix):
   - 0=None, 1=Sub, 2=Up, 3=Average, 4=Paeth

**Already implemented.** No changes needed.

### 2.4 RGBA Alpha Handling

For RGBA (color type 6):
1. Split each pixel into RGB (3 bytes) + Alpha (1 byte)
2. RGB data → FlateDecode image XObject (`/ColorSpace /DeviceRGB`)
3. Alpha data → FlateDecode SMask XObject (`/ColorSpace /DeviceGray`)
4. Main image references SMask: `/SMask {smaskObjId} 0 R`

**Already implemented.** No changes needed.

### 2.5 Interlaced PNG

**Not supported.** The parser checks `interlaceMethod != 0` and returns null. The renderer falls back to the gray placeholder. This is acceptable — interlaced PNGs are rare in documentation.

### 2.6 Image Scaling

```
displayWidth = min(imageWidth, contentWidth)
scale = displayWidth / imageWidth
displayHeight = imageHeight * scale
```

Images never upscale (scale capped at 1.0). If the image doesn't fit the current page, a page break is inserted first.

**Already implemented.** No changes needed for beta.3.

---

## 3. Hyperlinks

### 3.1 PDF Link Annotation Structure

```
<< /Type /Annot /Subtype /Link
   /Rect [x1 y1 x2 y2]
   /Border [0 0 0]
   /C [0 0 1]
   /A << /Type /Action /S /URI /URI (https://example.com) >> >>
```

**Already implemented** except:
- No `/C` color array (links are invisible)
- No visual styling on the text itself

### 3.2 Clickable Rectangle Computation

When a `TextSegment` has a URL:
1. Before rendering the segment text, record `(x, cursorY)` as the start position
2. Measure the text width: `MeasureText(text, font, fontSize)`
3. Create annotation: `AddLinkAnnotation(x, cursorY - descent, textWidth, fontSize + descent, url)`

**Beta.3 improvement**: The annotation is already created. Add visual styling:
- Set text color to blue (RGB 0, 0, 0.8) before rendering link text segments
- Reset to black after
- No underline (would require drawing a separate line — keep simple)

### 3.3 Internal Anchors

For cross-references (`<<id>>`), internal named destinations could be supported:
```
/Dest [pageObj /XYZ x y null]
```

**Deferred** — out of scope for beta.3. Cross-references render as bracketed text `[label]`. Full internal linking requires a two-pass renderer (first pass to collect anchor positions, second to resolve references).

---

## 4. Text Quality

### 4.1 Line-Breaking Improvements

**Current behavior**: Break at spaces only. A word that exceeds `maxWidth` is broken mid-character.

**Beta.3 improvements**:
1. Break at hyphens (`-`) as secondary break points
2. Break before long URLs after `/`, `?`, `&`, `=`
3. Never break between a number and its unit (`42 kg` stays together) — **deferred** (complex)

### 4.2 Punctuation That Must Never Start a Line

These characters must never appear at the start of a wrapped line. If wrapping would place them there, pull them back to the previous line:

```
) ] } > , . ; : ! ? — – ' " " ' ‐ …
```

Implementation: after wrapping, if the first character of a new line is in this set, move it (and any preceding space) back to the end of the previous line.

### 4.3 Widow/Orphan Control

- **Orphan**: A paragraph's first line alone at the bottom of a page. Rule: if only 1 line fits before a page break, move the entire paragraph to the next page.
- **Widow**: A paragraph's last line alone at the top of a page. Rule: if only 1 line would spill to the next page, pull 1 extra line to the next page (break 1 line earlier).

Thresholds: minimum 2 lines on each side of a page break.

### 4.4 Justification Spacing Limit

Current behavior: unlimited word spacing for justification, which can produce ugly gaps.

**Beta.3 rule**: Maximum extra space per word = 8 points (already hardcoded as `extraSpacing < 8`). No change needed — the current limit is reasonable.

---

## 5. Table Improvements

### 5.1 Cell Text Height Measurement

For each cell:
1. Wrap text using `WrapText(text, font, fontSize, cellWidth - 2 * padding)`
2. Cell height = `lineCount * leading + 2 * verticalPadding`
3. Row height = `max(cellHeight)` across all cells in the row

**Already implemented.** Minor improvement: add vertical padding (currently 0).

### 5.2 Column Width Algorithm

**No change from beta.2.** The current two-strategy approach works well:
1. Explicit `cols="..."` → proportional distribution
2. Auto-sizing → min-width (longest word) + proportional to text volume

### 5.3 Page Breaking for Tables

**Current**: Each row is checked individually — if it doesn't fit, `EnsurePage()` starts a new page.

**Beta.3 improvement**: When a table continues to a new page and `HasHeader` is true, repeat the header row on the new page. Implementation:
1. Save the header row's `TableRowNode` reference
2. In the body loop, after `EnsurePage()` triggers a new page, re-render the header row
3. Draw the header separator line again

---

## 6. Headers and Footers

### 6.1 Page Structure

Headers and footers are rendered as text operations in the page content stream, positioned in the margin area:
- Header: centered at `y = pageHeight - marginTop + 15` (above content area)
- Footer: centered at `y = marginBottom - 20` (below content area)

**Already implemented.** No structural change needed.

### 6.2 Total Page Count

**Current**: Only `{page}` (current page number) is supported.

**Beta.3**: Add `{pages}` (total page count). This requires a **two-pass approach**:
1. First pass: render all content, count total pages
2. Second pass: re-render with known total page count

**Simpler alternative (chosen)**: Post-process the PDF byte output — search for a placeholder string like `{TOTAL_PAGES}` and replace it with the actual count. Since page count is rendered as text in the content stream, we can replace the placeholder in the already-serialized PDF bytes.

Implementation:
1. During rendering, emit `{pages}` as the literal string `"___TOTAL___"` in the content stream
2. In `ToBytes()`, after serialization, scan the output for `___TOTAL___` and replace with the zero-padded page count
3. Pad with spaces to maintain exact byte offsets (avoiding xref recalculation)

### 6.3 Template Placeholders

| Placeholder | Value | Available in |
|-------------|-------|-------------|
| `{page}` | Current page number | Header, Footer |
| `{pages}` | Total page count | Header, Footer |
| `{title}` | Document title | Header, Footer |

---

## 7. PdfRenderOptions Extensions

### 7.1 New Options

| # | Name | Type | Default | Description |
|---|------|------|---------|-------------|
| 1 | `FontSize` | `float` | 11f | Base body text font size in points |
| 2 | `CodeFontSize` | `float` | 9f | Code block font size |
| 3 | `LineSpacing` | `float` | 1.35f | Line spacing multiplier (leading = fontSize * lineSpacing) |
| 4 | `LinkColor` | `PdfColor?` | `(0, 0, 0.8)` | Color for hyperlink text. Null = no coloring |
| 5 | `RepeatTableHeader` | `bool` | true | Repeat header row when table spans pages |
| 6 | `PageNumberFormat` | `string?` | null | Alias for FooterText. Supports `{page}`, `{pages}` |
| 7 | `TitleFontSize` | `float` | 24f | Document title font size |
| 8 | `HeadingScale` | `float` | 0.85f | Each heading level = previous × scale (H1=TitleFontSize, H2=H1×scale, ...) |
| 9 | `CodeBackground` | `PdfColor?` | `(0.95, 0.95, 0.95)` | Background color for code blocks. Null = no background |
| 10 | `AdmonitionBorderWidth` | `float` | 2f | Left border width for admonition blocks |

### 7.2 PdfColor Type

```csharp
/// <summary>RGB color for PDF rendering (values 0.0–1.0).</summary>
public readonly record struct PdfColor(float R, float G, float B);
```

---

## 8. Testing Strategy

### 8.1 Determinism Validation

Generate PDF from a reference document twice → compare byte arrays. If not identical, the test fails. This is already done in `Render_produces_deterministic_output`.

**Beta.3 extension**: Add a multi-feature determinism test that exercises all new features (fonts, images, links, tables, headers) and verifies byte-identical output.

### 8.2 PDF Structural Testing

Tests scan the PDF byte output (as ASCII string) for expected content:
- `Does.Contain("/Type /Font")` — font present
- `Does.Contain("Hello")` — text rendered
- `Does.Contain("/Subtype /Link")` — link annotation present
- `Does.Contain("/CIDFontType2")` — embedded font present

This approach works because PDF content streams and dictionaries are ASCII-readable. Binary streams (images, fonts) are opaque but their dictionary headers are testable.

### 8.3 Cross-Platform Consistency

The CI matrix runs on ubuntu, windows, macos. Determinism tests verify byte-identical output on all three platforms. Key risks:
- Floating-point formatting: mitigated by using `CultureInfo.InvariantCulture` and fixed-precision `F2` formatting
- Line endings: PDF uses `\n` explicitly (not platform-dependent)
- Font file paths: tests use `FindSystemFont()` which picks platform-appropriate paths
- DeflateStream output: may vary by platform — for determinism tests, use standard fonts (not embedded) to avoid deflate variance

### 8.4 New Test Categories for Beta.3

| Category | Tests to add |
|----------|-------------|
| Font subsetting | Subset contains only used glyphs, ToUnicode CMap present, `/W` array correct |
| cmap format 12 | Characters above U+FFFF mapped correctly |
| Link styling | Blue color in content stream, annotation rect matches text position |
| Table headers | Header repeats on page break |
| Page count | `{pages}` placeholder replaced correctly |
| Text quality | Punctuation not at line start, orphan/widow control |
| Options | Each new option produces expected change in output |

---

## 9. Implementation Order

| Phase | Focus | Depends on |
|-------|-------|------------|
| P02 | TrueType font system (subsetting, cmap format 12, ToUnicode) | P01 |
| Check A | Font system integrity verification | P02 |
| P03 | Image embedding improvements (already mostly done) | P01 |
| P04 | Hyperlink visual styling | P01 |
| P05 | Text quality (line breaking, widow/orphan) | P01 |
| P06 | Table improvements (header repeat, vertical padding) | P01 |
| Check B | Rendering integrity verification | P03-P06 |
| P07 | Headers/footers ({pages} placeholder) | P01 |
| P08 | PdfRenderOptions extensions | P02-P07 |
| P09 | Rendering regression tests | P02-P08 |
| P10 | Documentation | P09 |
| Reflect | Self-reflection | P10 |
| Check C | Final validation | All |
