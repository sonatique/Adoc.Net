# Extension Packaging Guide

AdocNet supports a standard extension packaging format that enables installing,
listing, and removing extensions from a central directory. Installed extensions
are automatically loaded when rendering documents.

## Extension Directory Structure

Extensions are installed to `~/.adocnet/extensions/`, with each extension in
its own subdirectory:

```
~/.adocnet/extensions/
    my-extension/
        extension.json       <- manifest (required)
        MyExtension.dll      <- entry-point DLL
        SomeDependency.dll   <- additional dependencies
    another-extension/
        extension.json
        AnotherExt.dll
```

Platform-specific paths:

| Platform | Path |
|----------|------|
| Windows | `C:\Users\<user>\.adocnet\extensions\` |
| Linux | `/home/<user>/.adocnet/extensions/` |
| macOS | `/Users/<user>/.adocnet/extensions/` |

## Manifest Format (extension.json)

Every extension directory must contain an `extension.json` manifest file:

```json
{
  "name": "my-extension",
  "version": "1.2.0",
  "description": "Custom inline macros for technical documentation",
  "entry": "MyExtension.dll",
  "minAdocNetVersion": "1.0.0-beta.7"
}
```

### Field Reference

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `name` | Yes | — | Unique extension identifier |
| `version` | No | `"0.0.0"` | Extension version for display |
| `description` | No | `""` | Short human-readable description |
| `entry` | Yes | — | Relative path to entry-point DLL |
| `minAdocNetVersion` | No | `null` | Minimum compatible AdocNet version |

- `name` and `entry` are required. Missing either causes the extension to be skipped.
- `minAdocNetVersion` is checked against the running AdocNet version. If the current
  version is older, the extension is skipped with a warning.
- Unknown fields are silently ignored for forward compatibility.

## Building an Extension

### 1. Create a Class Library

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AdocNet.Core" Version="1.0.0-beta.7" />
  </ItemGroup>
</Project>
```

### 2. Implement Processor Interfaces

```csharp
using AdocNet;
using AdocNet.Ast;
using AdocNet.Extensions;

public sealed class TimestampProcessor : IDocumentProcessor, IExtension
{
    public string Name => "timestamp";
    public string Version => "1.0.0";

    public void Process(DocumentNode document)
    {
        // Add a timestamp paragraph at the end of every document
        var para = new ParagraphNode
        {
            Text = $"Generated: {DateTime.UtcNow:yyyy-MM-dd}",
            Inlines = [new TextInlineNode
            {
                Value = $"Generated: {DateTime.UtcNow:yyyy-MM-dd}"
            }],
        };
        document.AddChild(para);
    }
}
```

Extensions must have a **parameterless constructor**. Use `IExtension` for metadata
(optional but recommended for diagnostic messages).

### 3. Build and Create Manifest

```bash
dotnet build -c Release
```

Create `extension.json` alongside the DLL:

```json
{
  "name": "my-timestamp",
  "version": "1.0.0",
  "description": "Adds a generation timestamp to documents",
  "entry": "MyTimestamp.dll",
  "minAdocNetVersion": "1.0.0-beta.7"
}
```

### 4. Package Structure

Your extension directory should contain:

```
my-timestamp/
    extension.json
    MyTimestamp.dll
    (any dependency DLLs)
```

## Installing Extensions

Use the CLI to install an extension from a directory:

```bash
adocnet ext install ./path/to/my-timestamp/
```

This copies the entire directory to `~/.adocnet/extensions/my-timestamp/`.

If the extension is already installed, use `--force` to overwrite:

```bash
adocnet ext install ./path/to/my-timestamp/ --force
```

## Listing Installed Extensions

```bash
adocnet ext list
```

Output:

```
Installed extensions (~/.adocnet/extensions/):

  Name              Version  Description
  ----              -------  -----------
  my-timestamp      1.0.0    Adds a generation timestamp to documents
  custom-macros     0.5.0    Custom inline macros

2 extension(s) installed.
```

## Removing Extensions

```bash
adocnet ext remove my-timestamp
```

This deletes the `~/.adocnet/extensions/my-timestamp/` directory.

## Automatic Loading

Installed extensions are automatically loaded before rendering. The loading order is:

1. Installed extensions from `~/.adocnet/extensions/` (alphabetical by folder name)
2. Extensions from `--extensions` flags (in order specified)
3. Extensions from `--extension-dir` flags (in order specified)

To skip auto-loading of installed extensions:

```bash
adocnet input.adoc --no-auto-extensions
```

This only suppresses installed extensions. Explicitly specified `--extensions` and
`--extension-dir` flags still apply.

## Programmatic API

Load installed extensions in your own code:

```csharp
using AdocNet;
using AdocNet.Converters.Html;
using AdocNet.Parser;

var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document);

// Load from default directory (~/.adocnet/extensions/)
engine.LoadInstalledExtensions();

// Or from a custom directory
engine.LoadInstalledExtensions("/path/to/my-extensions/");

using var output = File.Create("output.html");
engine.Convert(source, output);
```

## Version Compatibility

When `minAdocNetVersion` is set in the manifest, AdocNet compares it against
the running version. If the current version is older than the minimum, the
extension is skipped with a warning:

```
Warning: Extension 'my-ext' requires AdocNet >= 1.0.0-beta.8, current is 1.0.0-beta.7, skipping
```

Version comparison handles semver prerelease tags:
- `1.0.0` (release) is newer than `1.0.0-beta.7` (prerelease)
- `1.0.0-beta.8` is newer than `1.0.0-beta.7`

## Error Handling

All errors during extension loading are non-fatal. Invalid extensions are skipped
with a warning via `OnWarning`:

| Scenario | Behavior |
|----------|----------|
| Missing `extension.json` | Warning, skip directory |
| Invalid JSON in manifest | Warning, skip directory |
| Missing `name` or `entry` | Warning, skip directory |
| Entry DLL not found | Warning, skip extension |
| Version incompatible | Warning, skip extension |
| DLL load failure | Warning, skip (delegated to ExtensionLoader) |

## Extension Registry (beta.8)

Installed extensions are tracked in a local registry (`~/.adocnet/registry.json`)
that provides fast querying and dependency validation. The registry is automatically
maintained — `ext install` and `ext remove` update it, and it self-repairs if
corrupted or out of sync with the filesystem.

Extensions can declare dependencies in their manifest:

```json
{
  "name": "diagram",
  "version": "1.0.0",
  "entry": "DiagramExtension.dll",
  "dependencies": ["syntax-highlight >= 1.0.0"]
}
```

See [Extension Registry Guide](EXTENSION_REGISTRY.md) for full documentation
on the registry format, search commands, and dependency validation.

## See Also

- [Extension Developer Guide](EXTENSIONS.md) — writing processors and renderers
- [Dynamic Extensions Guide](DYNAMIC_EXTENSIONS.md) — loading from raw DLL paths
- [Extension Registry Guide](EXTENSION_REGISTRY.md) — registry, search, and dependency validation
- [CLI Reference](CLI.md) — command-line tool
