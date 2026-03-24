# Theming Guide

This document describes how to apply visual themes to HTML and PDF output in AdocNet.

## HTML Themes

### Built-in Themes

AdocNet includes 4 built-in HTML themes:

| Theme | Enum Value | Description |
|-------|-----------|-------------|
| **None** | `HtmlTheme.None` | Bare HTML fragment — no CSS, no `<html>` wrapper |
| **Default** | `HtmlTheme.Default` | Clean sans-serif design with VS Code–inspired syntax colors |
| **Asciidoctor** | `HtmlTheme.Asciidoctor` | Serif body, compatible with Asciidoctor's default stylesheet |
| **Clean** | `HtmlTheme.Clean` | Minimal Georgia serif with maximum readability |
| **Github** | `HtmlTheme.Github` | GitHub-flavored styling with system fonts and GitHub colors |

### Applying a Theme

```csharp
using AdocNet.Converters.Html;

var options = new HtmlRenderOptions { Theme = HtmlTheme.Default };
string html = new HtmlRenderer().RenderToString(doc, options);
```

When a theme is selected (anything other than `None`), the output is automatically
wrapped in a full HTML document with the theme CSS embedded in a `<style>` block.

### Custom CSS

Override or extend any theme with the `CustomCss` property:

```csharp
var options = new HtmlRenderOptions
{
    Theme = HtmlTheme.Default,
    CustomCss = """
        body { max-width: 1200px; }
        .hl-kw { color: #FF0000; }
    """
};
```

Custom CSS is appended after the theme CSS, so your rules naturally override theme defaults.

### Additional HTML Options

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Theme` | `HtmlTheme` | `None` | Built-in theme to apply |
| `CustomCss` | `string?` | `null` | CSS appended after theme CSS |
| `FullDocument` | `bool` | `false` | Wrap in `<!DOCTYPE html>` even without a theme |
| `Title` | `string?` | `null` | `<title>` element (overrides document title) |
| `ExtraHead` | `string?` | `null` | Extra content injected into `<head>` |
| `EnableSyntaxHighlighting` | `bool` | `false` | Server-side syntax highlighting for source blocks |

### Syntax Highlighting in Themes

All 4 built-in themes include CSS rules for syntax highlighting token classes
(`.hl-kw`, `.hl-s`, `.hl-c`, etc.). Each theme uses colors that match its
overall aesthetic. Enable highlighting with:

```csharp
var options = new HtmlRenderOptions
{
    Theme = HtmlTheme.Github,
    EnableSyntaxHighlighting = true
};
```

## PDF Styling

PDF styling is configured through `PdfRenderOptions` properties.
There is no separate theme class — the options object IS the theme.

### Style Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `HeadingColor` | `PdfColor?` | `null` (black) | Color for heading text |
| `BodyColor` | `PdfColor?` | `null` (black) | Color for body text |
| `LinkColor` | `PdfColor?` | `(0, 0, 0.8)` | Color for hyperlinks |
| `CodeBackground` | `PdfColor?` | `(0.95, 0.95, 0.95)` | Code block background |
| `SyntaxColors` | `SyntaxColorScheme?` | `null` | Syntax highlighting colors |
| `SectionSpacing` | `float` | `16` | Space before sections (points) |
| `BlockIndent` | `float` | `24` | Indent for nested blocks (points) |
| `AdmonitionBorderWidth` | `float` | `2` | Admonition left border width |
| `TableHeaderBackground` | `PdfColor?` | `null` | Table header row background |

### PDF Style Presets

Two convenience presets are provided:

```csharp
// Compact: smaller fonts, tighter spacing, narrower margins
PdfRenderOptions.Compact

// Presentation: larger fonts, wider spacing, colored headings
PdfRenderOptions.Presentation
```

### Custom PDF Styling

```csharp
var options = new PdfRenderOptions
{
    HeadingColor = new PdfColor(0.2f, 0f, 0.6f),
    BodyColor = new PdfColor(0.1f, 0.1f, 0.1f),
    SectionSpacing = 24f,
    SyntaxColors = SyntaxColorScheme.Default
};
```
