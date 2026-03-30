# Extension Registry Guide

AdocNet maintains a local registry of installed extensions for fast querying
and dependency validation. The registry is a cached index — the filesystem
(`~/.adocnet/extensions/`) is always the source of truth.

## Registry Location

The registry file lives at `~/.adocnet/registry.json`, alongside the
`extensions/` directory:

```
~/.adocnet/
    registry.json        <- cached index of installed extensions
    extensions/
        my-extension/
            extension.json
            MyExtension.dll
        another-ext/
            extension.json
            AnotherExt.dll
```

## Registry Format

```json
{
  "version": "1",
  "extensions": [
    {
      "name": "diagram",
      "version": "1.0.0",
      "description": "Diagram support via external tools",
      "path": "/home/user/.adocnet/extensions/diagram",
      "dependencies": "syntax-highlight >= 1.0.0"
    },
    {
      "name": "syntax-highlight",
      "version": "2.1.0",
      "description": "Syntax highlighting for code blocks",
      "path": "/home/user/.adocnet/extensions/syntax-highlight",
      "dependencies": ""
    }
  ]
}
```

- `version` — format version (currently `"1"`). Unrecognized versions trigger a rebuild.
- `extensions` — array of installed extension records, sorted by name.
- `path` — absolute, normalized path to the extension directory.
- `dependencies` — comma-separated dependency specs, or empty string.

## CLI Commands

### ext list

Lists all installed extensions from the registry.

```bash
adocnet ext list
```

### ext info

Shows detailed information about a specific extension.

```bash
adocnet ext info diagram
```

Output:
```
Extension: diagram
Version:   1.0.0
Description: Diagram support via external tools
Path:      /home/user/.adocnet/extensions/diagram
Entry:     DiagramExtension.dll
Min AdocNet: 1.0.0-beta.5
Dependencies:
  - syntax-highlight >= 1.0.0
```

### ext search

Searches installed extensions by keyword (case-insensitive, matches name and description).

```bash
adocnet ext search diagram
```

Output:
```
Search results for "diagram":

  Name      Version  Description
  -------   -------  -----------
  diagram   1.0.0    Diagram support via external tools

1 extension(s) matched.
```

### ext install

Installs an extension and updates the registry.

```bash
adocnet ext install ./path/to/extension/
adocnet ext install ./path/to/extension/ --force
```

### ext remove

Removes an extension and updates the registry.

```bash
adocnet ext remove diagram
```

## Dependency Metadata

Extensions can declare dependencies on other extensions in their `extension.json`
manifest:

```json
{
  "name": "diagram",
  "version": "1.0.0",
  "description": "Diagram support",
  "entry": "DiagramExtension.dll",
  "dependencies": ["syntax-highlight >= 1.0.0", "core-utils >= 0.5.0"]
}
```

Dependencies can also be specified as a comma-separated string:

```json
{
  "dependencies": "syntax-highlight >= 1.0.0, core-utils >= 0.5.0"
}
```

### Dependency Format

Each dependency spec follows the pattern `name >= version` or just `name`:

- `syntax-highlight >= 1.0.0` — requires version 1.0.0 or higher
- `core-utils` — requires any version

### Validation Behavior

Dependencies are **advisory only**:
- Missing dependencies produce a warning but do not block installation or loading
- Incompatible versions produce a warning but do not block
- No automatic installation of missing dependencies
- No transitive dependency resolution

```
Warning: Extension 'diagram' depends on 'syntax-highlight >= 1.0.0' which is not installed.
```

## Registry Rebuild

The registry is automatically rebuilt from the filesystem when:

- `registry.json` is missing (first use, or manually deleted)
- `registry.json` is corrupt (invalid JSON)
- Format version is unrecognized
- Extension folder exists on disk but not in registry
- Extension in registry but folder missing from disk

Rebuild scans `~/.adocnet/extensions/`, reads all `extension.json` manifests,
and regenerates the registry. Since the filesystem is the source of truth,
no data is lost.

To force a rebuild, delete `~/.adocnet/registry.json` — the next CLI command
will regenerate it.

## Programmatic API

Query the registry from code without loading extensions:

```csharp
using AdocNet;
using AdocNet.Extensions;

// List all installed extensions
var extensions = AdocEngine.GetInstalledExtensions();
foreach (var ext in extensions)
    Console.WriteLine($"{ext.Name} v{ext.Version}: {ext.Description}");

// Find a specific extension
var info = AdocEngine.FindExtension("diagram");
if (info is not null)
    Console.WriteLine($"Found: {info.Name} at {info.InstalledPath}");
```

These are read-only queries — they do not load DLLs or register processors.

## Atomic Writes

The registry uses atomic file writes to prevent corruption:

1. JSON is serialized in memory
2. Written to `registry.json.tmp`
3. Existing `registry.json` is deleted
4. Temp file is renamed to `registry.json`

If a crash occurs between steps 3 and 4, the missing file triggers a rebuild.

## See Also

- [Extension Developer Guide](EXTENSIONS.md) — writing processors and renderers
- [Extension Packaging Guide](EXTENSION_PACKAGING.md) — manifest format and installation
- [Dynamic Extensions Guide](DYNAMIC_EXTENSIONS.md) — loading from raw DLL paths
- [CLI Reference](CLI.md) — command-line tool
