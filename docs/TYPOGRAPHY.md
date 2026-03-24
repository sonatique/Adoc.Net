# Typography (PDF)

AdocNet beta.4 adds typography improvements for PDF rendering:
hyphenation, configurable paragraph spacing, and tighter justification.

## Hyphenation

English hyphenation uses the Liang/Knuth algorithm with standard TeX
patterns from the CTAN hyph-utf8 package (LPPL licensed).

### Enabling Hyphenation

```csharp
var options = new PdfRenderOptions
{
    EnableHyphenation = true
};
```

Hyphenation is **disabled by default** for backward compatibility with beta.3.

### How It Works

When a word doesn't fit on the current line, the hyphenator finds valid
break points and inserts a hyphen at the best position. This reduces
excessive word spacing in justified paragraphs.

Rules:
- Minimum word length: 5 characters
- Minimum before hyphen: 2 characters
- Minimum after hyphen: 3 characters
- Only English (US) patterns are included

### Justification Improvement

With hyphenation enabled, the maximum inter-word spacing is clamped to
**1.5x** normal space width (down from 2x without hyphenation). This
produces tighter, more professional-looking justified text.

## Paragraph Spacing

Two new properties control vertical spacing around paragraphs:

| Property | Default | Description |
|----------|---------|-------------|
| `ParagraphSpacingBefore` | `0` | Space before each paragraph (points) |
| `ParagraphSpacingAfter` | `8` | Space after each paragraph (points) |

The defaults match beta.3 behavior exactly.

### Example

```csharp
var options = new PdfRenderOptions
{
    ParagraphSpacingBefore = 4f,
    ParagraphSpacingAfter = 12f
};
```

## Section Spacing

The `SectionSpacing` property controls the vertical space before each
section heading:

```csharp
var options = new PdfRenderOptions
{
    SectionSpacing = 24f  // default: 16
};
```

## Line Spacing

The existing `LineSpacing` multiplier (default: 1.35) controls the
line-to-line distance: `leading = fontSize × lineSpacing`.

```csharp
var options = new PdfRenderOptions
{
    LineSpacing = 1.5f  // more generous spacing
};
```

## Typography Options Summary

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `FontSize` | `float` | `11` | Body text size (points) |
| `CodeFontSize` | `float` | `9` | Code block text size |
| `TitleFontSize` | `float` | `24` | Document title size |
| `HeadingScale` | `float` | `0.85` | Each heading level = previous × scale |
| `LineSpacing` | `float` | `1.35` | Leading multiplier |
| `EnableHyphenation` | `bool` | `false` | Enable English hyphenation |
| `ParagraphSpacingBefore` | `float` | `0` | Space before paragraphs |
| `ParagraphSpacingAfter` | `float` | `8` | Space after paragraphs |
| `SectionSpacing` | `float` | `16` | Space before sections |

## Backward Compatibility

All new properties default to values that produce output identical to beta.3.
Existing documents rendered with default options will not change appearance.
