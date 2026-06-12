# AdocNet CLI Reference

The AdocNet command-line tools convert AsciiDoc files to HTML, PDF,
DocBook, and EPUB. They support watch mode with auto-rebuild and a live
preview server with browser hot-reload.

## Installation

```bash
dotnet tool install --global AdocNet.Tool         # adocnet (default: HTML)
dotnet tool install --global AdocNet.Pdf          # adocnet-pdf
dotnet tool install --global AdocNet.Epub         # adocnet-epub
dotnet tool install --global AdocNet.DocBook      # adocnet-docbook
```

## Tools

| Command | Default format | Equivalent |
|---------|---------------|------------|
| `adocnet` | HTML | `asciidoctor` |
| `adocnet-pdf` | PDF | `asciidoctor-pdf` |
| `adocnet-epub` | EPUB | `asciidoctor-epub3` |
| `adocnet-docbook` | DocBook XML | — |

All tools accept the same flags. The only difference is the default output format.

## Synopsis

```
adocnet <input.adoc|directory> [options]
adocnet-pdf <input.adoc|directory> [options]
adocnet preview <path> [preview-options]
```

## Converting Files

By default, output is written to a file with the same name and the appropriate
extension. Use `-o -` for stdout output.

### Single file

```bash
adocnet README.adoc                     # → README.html
adocnet-pdf README.adoc                 # → README.pdf
adocnet README.adoc -b pdf              # → README.pdf (same as above)
adocnet README.adoc -o -                # → stdout
adocnet README.adoc -o custom.html      # → custom.html
```

### Directory conversion

```bash
adocnet docs/ -r -D build/              # all .adoc → .html in build/
adocnet-pdf docs/ -r -D build/          # all .adoc → .pdf in build/
```

### All output formats

```bash
adocnet input.adoc                      # → input.html
adocnet input.adoc -b pdf               # → input.pdf
adocnet input.adoc -b docbook5          # → input.xml
adocnet input.adoc -b epub              # → input.epub
```

## Watch Mode

Automatically rebuild when files change:

```bash
adocnet docs/ --watch -v
adocnet-pdf README.adoc -w
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

| Option | Description |
|--------|-------------|
| `-b, --backend <fmt>` | Output format: `html5` (default), `pdf`, `docbook5`, `epub`, `man`, `revealjs` |
| `-o <file>` | Write output to file (use `-` for stdout) |
| `-D, --destination-dir <dir>` | Write output files to directory |
| `-a, --attribute <k=v>` | Set a document attribute |
| `-n, --section-numbers` | Auto-number section titles |
| `-e, --embedded` | Wrap HTML in a full standalone document with CSS theme. **Note:** this is the inverse of Asciidoctor's `-e`, which emits a fragment; AdocNet's default output is already the fragment. |
| `--theme <name>` | CSS theme: `default`, `asciidoctor`, `clean`, `github` |
| `--dump-ast` | Print AST tree instead of rendering |
| `-w, --watch` | Watch for changes and rebuild |
| `-v, --verbose` | Show per-file status with timing |
| `-q, --quiet` | Suppress non-error output |
| `-r, --recursive` | Include subdirectories for directory input |
| `--config <file>` | Load config from file (default: discover `adocnet.json`) |
| `-h, --help` | Show help |

### Preview Options

| Option | Default | Description |
|--------|---------|-------------|
| `--port <N>` | 5500 | HTTP server port |
| `--no-open` | false | Don't auto-launch browser |
| `--theme <name>` | asciidoctor | CSS theme for preview |
| `-r, --recursive` | false | Include subdirectories |

## Document Attributes

Set attributes from the command line with `-a`:

```bash
adocnet input.adoc -a version=2.0 -a author="Jane Doe"
adocnet input.adoc -a sectnums          # enable section numbering
```

## Project Configuration

Place an `adocnet.json` in your project root:

```json
{
  "format": "html",
  "outDir": "build",
  "recursive": true,
  "attributes": {
    "author": "Jane Doe",
    "source-highlighter": "highlight.js"
  }
}
```

The CLI discovers `adocnet.json` by walking up from the input path.
CLI flags override config file values.

## Running from Source

```bash
dotnet run --project src/AdocNet.Cli -- input.adoc
dotnet run --project src/AdocNet.Cli.Pdf -- input.adoc
dotnet run --project src/AdocNet.Cli.Epub -- input.adoc
dotnet run --project src/AdocNet.Cli.DocBook -- input.adoc
```

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Parse errors (output still produced) |
| 2 | Usage error (bad arguments, missing file) |

## Examples

```bash
# Convert with themed HTML
adocnet intro.adoc -e --theme asciidoctor

# Batch convert to PDF
adocnet-pdf docs/ -r -D dist/

# Watch and rebuild
adocnet docs/ -r -w -v

# Preview with custom port
adocnet preview docs/ --port 3000

# Set attributes
adocnet input.adoc -a version=2.0 -a env=production

# Dump AST for debugging
adocnet input.adoc --dump-ast

# Multiple formats
adocnet manual.adoc                     # → manual.html
adocnet-pdf manual.adoc                 # → manual.pdf
adocnet-epub manual.adoc                # → manual.epub
adocnet-docbook manual.adoc             # → manual.xml
```

## See Also

- [Usage Guide](USAGE.md) — library API
- [Renderers Guide](RENDERERS.md) — renderer options
