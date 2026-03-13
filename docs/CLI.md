# AdocNet CLI Reference

The `adocnet` command-line tool converts AsciiDoc files to HTML, PDF,
DocBook, and EPUB. It supports watch mode with auto-rebuild and a live
preview server with browser hot-reload.

## Installation

```bash
dotnet tool install --global AdocNet.Tool
```

## Synopsis

```
adocnet <input.adoc|directory> [options]
adocnet preview <path> [preview-options]
```

## Converting Files

### Single file to stdout

```bash
adocnet README.adoc
```

### Single file to output file

```bash
adocnet README.adoc -o README.html
```

### Directory conversion

```bash
adocnet docs/ -r -f html --out-dir build/
```

Converts all `.adoc` files in `docs/` (recursively with `-r`) to HTML,
writing output to `build/` preserving directory structure.

### Output formats

```bash
adocnet docs/ -f html --out-dir build/html
adocnet docs/ -f pdf --out-dir build/pdf
adocnet docs/ -f docbook --out-dir build/xml
adocnet docs/ -f epub --out-dir build/epub
```

## Watch Mode

Automatically rebuild when files change:

```bash
adocnet docs/ --watch -v
adocnet README.adoc -w -o README.html
```

Press `Ctrl+C` to stop.

## Live Preview

Start a local HTTP server with browser auto-refresh:

```bash
adocnet preview docs/
adocnet preview docs/ --port 8080 --no-open
adocnet preview README.adoc --theme clean
```

Opens a browser to `http://localhost:5500/` with an index of all documents.
Editing any `.adoc` file triggers a rebuild and browser refresh via WebSocket.

## Options Reference

### General Options

| Option | Short | Description |
|--------|-------|-------------|
| `--help` | `-h` | Show help |
| `-o <file>` | | Write output to file (single-file mode only) |
| `-f <format>` | | Output format: `html` (default), `pdf`, `docbook`, `epub` |
| `--out-dir <dir>` | | Write output files to directory |
| `-r, --recursive` | | Include subdirectories for directory input |
| `-w, --watch` | | Watch for changes and rebuild |
| `-v, --verbose` | | Show per-file status with timing |
| `-q, --quiet` | | Suppress non-error output |
| `--config <file>` | | Load config from file (default: discover `adocnet.json`) |

### HTML Options

| Option | Description |
|--------|-------------|
| `--styled` | Wrap output in full HTML document with CSS theme |
| `--theme <name>` | CSS theme: `default`, `asciidoctor`, `clean` |

### Debug Options

| Option | Description |
|--------|-------------|
| `--dump-ast` | Print AST tree instead of rendering |

### Preview Options

| Option | Default | Description |
|--------|---------|-------------|
| `--port <N>` | 5500 | HTTP server port |
| `--no-open` | false | Don't auto-launch browser |
| `--theme <name>` | asciidoctor | CSS theme for preview |
| `-r, --recursive` | false | Include subdirectories |

## Project Configuration

Place an `adocnet.json` in your project root:

```json
{
  "format": "html",
  "outDir": "build",
  "recursive": true,
  "styled": true,
  "theme": "asciidoctor",
  "attributes": {
    "author": "Jane Doe",
    "source-highlighter": "highlight.js"
  }
}
```

The CLI discovers `adocnet.json` by walking up from the input path.
CLI flags override config file values.

CLI flags override config file values.

## Running from Source

```bash
dotnet run --project src/AdocNet.Cli -- input.adoc
dotnet run --project src/AdocNet.Cli -- docs/ -r -f html --out-dir build/
dotnet run --project src/AdocNet.Cli -- preview docs/
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Parse errors (output still produced) |
| 2 | Usage error (bad arguments, missing file) |

## Examples

```bash
# Convert single file with styled output
adocnet intro.adoc --styled --theme asciidoctor -o intro.html

# Batch convert docs to PDF
adocnet docs/ -r -f pdf --out-dir dist/

# Watch with verbose output
adocnet docs/ -r -w -v

# Preview with custom port
adocnet preview docs/ --port 3000

# Use explicit config
adocnet docs/ --config custom-config.json -v

# Dump AST for debugging
adocnet input.adoc --dump-ast

# Generate EPUB from a book
adocnet book.adoc -f epub -o book.epub

# Multiple formats from one source
adocnet manual.adoc -f html -o manual.html
adocnet manual.adoc -f pdf -o manual.pdf
```

## See Also

- [Usage Guide](USAGE.md) — library API
- [Renderers Guide](RENDERERS.md) — renderer options
