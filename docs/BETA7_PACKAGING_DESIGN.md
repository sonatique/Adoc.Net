# Beta.7 — Extension Packaging Design

> Design-only document. No code changes.

---

## 1. Extension Directory Structure

### Default Location

```
~/.adocnet/extensions/
    my-extension/
        extension.json       <- manifest (required)
        MyExtension.dll      <- entry point DLL (referenced by manifest)
        SomeDependency.dll   <- optional additional assemblies
```

### Platform Paths

```csharp
var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
var extensionsDir = Path.Combine(home, ".adocnet", "extensions");
```

| Platform | Path |
|----------|------|
| Windows | `C:\Users\<user>\.adocnet\extensions\` |
| Linux | `/home/<user>/.adocnet/extensions/` |
| macOS | `/Users/<user>/.adocnet/extensions/` |

`Environment.GetFolderPath(SpecialFolder.UserProfile)` is available on netstandard2.0.

### Directory Lifecycle

- **Does not exist**: skip silently during auto-load. Created on first `ext install`.
- **Exists but empty**: no extensions loaded, no warning.
- **Contains subdirectories**: each subdirectory is a candidate extension.

### Naming Convention

Extension folder name should match `extension.json`'s `name` field. This is validated
on install but not enforced on load (the manifest `name` field is authoritative).

---

## 2. Manifest Format (extension.json)

### Schema

```json
{
  "name": "my-extension",
  "version": "1.0.0",
  "description": "Short description of what this extension does",
  "entry": "MyExtension.dll",
  "minAdocNetVersion": "1.0.0-beta.7"
}
```

### Field Definitions

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `name` | string | **Yes** | — | Unique identifier. Must match `^[a-z0-9]([a-z0-9._-]*[a-z0-9])?$`. |
| `version` | string | No | `"0.0.0"` | Extension version for display purposes. |
| `description` | string | No | `""` | Short human-readable description. |
| `entry` | string | **Yes** | — | Relative path to the entry-point DLL within the extension folder. |
| `minAdocNetVersion` | string | No | `null` | Minimum compatible AdocNet version. If set, checked before loading. |

### Validation Rules

1. `name` is required and must be non-empty.
2. `entry` is required and must be non-empty.
3. `entry` must point to an existing `.dll` file relative to the extension folder.
4. All other fields are optional — missing fields get defaults.
5. Unknown fields are silently ignored (forward compatibility).

### JSON Parsing Decision

**Decision: System.Text.Json with conditional NuGet package.**

Reasoning:
- The manifest is a flat JSON object with 5 string fields — simple but still JSON.
- A hand-written parser risks bugs with edge cases (escape sequences, Unicode, BOM, trailing commas).
- `System.Text.Json` is the standard .NET JSON library, well-tested, and has minimal API surface for this use.
- On `net10.0`: built-in, zero added deps.
- On `netstandard2.0`: requires the `System.Text.Json` NuGet package (conditional reference).

```xml
<!-- In AdocNet.Core.csproj -->
<ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
  <PackageReference Include="System.Text.Json" Version="9.0.0" />
</ItemGroup>
```

This is the first external NuGet dependency on Core, but it's a Microsoft-maintained,
well-established package that's already built-in on modern TFMs. The alternative
(hand-writing a JSON parser) creates maintenance burden for a solved problem.

---

## 3. ExtensionManifest Model

### Class Design

```csharp
namespace AdocNet.Extensions;

/// <summary>
/// Represents the parsed contents of an extension.json manifest file.
/// </summary>
public sealed class ExtensionManifest
{
    public string Name { get; }
    public string Version { get; }
    public string Description { get; }
    public string Entry { get; }
    public string? MinAdocNetVersion { get; }

    /// <summary>Full path to the extension directory containing this manifest.</summary>
    public string DirectoryPath { get; }
}
```

### Deserialization

Use `System.Text.Json.JsonSerializer.Deserialize<T>()` with a private DTO class:

```csharp
private sealed class ManifestJson
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("entry")]
    public string? Entry { get; set; }

    [JsonPropertyName("minAdocNetVersion")]
    public string? MinAdocNetVersion { get; set; }
}
```

The DTO is internal. The public `ExtensionManifest` is constructed after validation,
ensuring it always holds valid data.

### Parse Method

```csharp
public static ExtensionManifest? Parse(string extensionDirectory, Action<string>? onWarning)
```

Returns `null` if manifest is missing, corrupt, or invalid. Warnings emitted via callback.

---

## 4. ExtensionDirectoryLoader

### Purpose

Scans a directory of extension folders (typically `~/.adocnet/extensions/`), reads each
manifest, validates, checks version compatibility, and loads the entry DLL.

### Public API

```csharp
namespace AdocNet.Extensions;

/// <summary>
/// Loads extensions from a structured extension directory where each subdirectory
/// contains an extension.json manifest and the corresponding DLL(s).
/// </summary>
public static class ExtensionDirectoryLoader
{
    /// <summary>
    /// Scans subdirectories of <paramref name="extensionsRootDir"/> for extension.json
    /// manifests, validates them, and loads the entry-point DLLs.
    /// </summary>
    public static List<object> LoadInstalledExtensions(
        string extensionsRootDir, Action<string>? onWarning)

    /// <summary>
    /// Returns the default extension directory path (~/.adocnet/extensions/).
    /// </summary>
    public static string GetDefaultExtensionDirectory()
}
```

### Loading Algorithm

```
LoadInstalledExtensions(rootDir):
  1. If rootDir doesn't exist -> return empty list (no warning — normal case).
  2. List subdirectories of rootDir.
  3. Sort subdirectories alphabetically by name (determinism).
  4. For each subdirectory:
     a. Look for extension.json. If missing -> warn, skip.
     b. Parse manifest. If invalid -> warn, skip.
     c. Check version compatibility. If incompatible -> warn, skip.
     d. Resolve entry DLL path = subdirectory + manifest.Entry.
     e. If DLL doesn't exist -> warn, skip.
     f. Call ExtensionLoader.LoadAssembly(dllPath, onWarning).
     g. Append results.
  5. Return aggregated list.
```

### Reuse

`ExtensionDirectoryLoader` reuses `ExtensionLoader.LoadAssembly()` for the actual
DLL loading, reflection scanning, and instantiation. It only adds the manifest-aware
discovery layer on top.

---

## 5. Version Compatibility

### Strategy

Compare `minAdocNetVersion` from the manifest against the running AdocNet version.

### Getting Current Version

```csharp
typeof(AdocEngine).Assembly.GetName().Version
```

This returns the `AssemblyVersion`, which is derived from the `<Version>` property
in `Directory.Build.props` (e.g., `1.0.0-beta.7`).

However, `Assembly.GetName().Version` returns only the numeric portion (`1.0.0.0`),
dropping the prerelease tag. For prerelease-aware comparison, use:

```csharp
typeof(AdocEngine).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
    .InformationalVersion
```

This returns the full version string including prerelease tags: `1.0.0-beta.7`.

### Comparison Algorithm

Use `NuGet.Versioning`-style semver comparison? No — that would add a dependency.

Instead, implement a simple comparison:

1. Parse both versions into `(major, minor, patch, prerelease)` tuples.
2. Compare `major.minor.patch` numerically.
3. For prerelease: a release version (`1.0.0`) is newer than any prerelease (`1.0.0-beta.N`).
4. Among prereleases of the same version, compare the suffix lexicographically.

This handles the expected version format (`1.0.0-beta.N`) without external deps.

### Compatibility Rule

An extension is compatible if:
- `minAdocNetVersion` is null or empty (always compatible), OR
- Current AdocNet version >= `minAdocNetVersion`

If incompatible: emit warning via `OnWarning`, skip the extension entirely.

### Implementation

```csharp
internal static class VersionComparer
{
    /// <summary>
    /// Returns true if <paramref name="current"/> >= <paramref name="minimum"/>.
    /// </summary>
    public static bool IsCompatible(string current, string minimum)
}
```

Small internal static class, ~30-40 lines. Tested with unit tests covering:
- `1.0.0-beta.7` >= `1.0.0-beta.7` (equal, compatible)
- `1.0.0-beta.8` >= `1.0.0-beta.7` (newer, compatible)
- `1.0.0-beta.6` >= `1.0.0-beta.7` (older, incompatible)
- `1.0.0` >= `1.0.0-beta.7` (release >= prerelease, compatible)
- `1.0.0-beta.7` >= `1.0.0` (prerelease < release, incompatible)
- null/empty minimum (always compatible)

---

## 6. Engine Integration

### New Method on AdocEngine

```csharp
/// <summary>
/// Loads extensions from the default extension directory (~/.adocnet/extensions/).
/// Each subdirectory must contain an extension.json manifest.
/// Must be called before the first Convert() call.
/// </summary>
public AdocEngine LoadInstalledExtensions()
{
    ThrowIfFrozen();
    var dir = ExtensionDirectoryLoader.GetDefaultExtensionDirectory();
    var extensions = ExtensionDirectoryLoader.LoadInstalledExtensions(dir, OnWarning);
    RegisterExtensions(extensions);
    return this;
}

/// <summary>
/// Loads extensions from a custom extension directory.
/// Each subdirectory must contain an extension.json manifest.
/// Must be called before the first Convert() call.
/// </summary>
public AdocEngine LoadInstalledExtensions(string extensionsRootDir)
{
    ThrowIfFrozen();
    var extensions = ExtensionDirectoryLoader.LoadInstalledExtensions(extensionsRootDir, OnWarning);
    RegisterExtensions(extensions);
    return this;
}
```

### Integration Notes

- Fluent API, consistent with existing `LoadExtension()` / `LoadExtensions()`.
- Respects `_frozen` flag — throws after first `Convert()`.
- Reuses private `RegisterExtensions(List<object>)`.
- `OnWarning` callback passed through for all diagnostics.

---

## 7. CLI `ext` Subcommands

### Subcommand Structure

```
adocnet ext list                    List installed extensions
adocnet ext install <source-path>   Install extension from a directory
adocnet ext remove <name>           Remove an installed extension
```

### `ext list`

Scans `~/.adocnet/extensions/`, reads each manifest, prints a table:

```
Installed extensions (~/.adocnet/extensions/):

  Name              Version    Description
  ────              ───────    ───────────
  my-diagrams       1.2.0      Diagram rendering via Mermaid CLI
  custom-macros     0.5.0      Custom inline macros

2 extension(s) installed.
```

If no extensions installed: `No extensions installed.`
If directory doesn't exist: `No extensions installed.` (same message — don't expose internals.)

### `ext install <source-path>`

1. Validate source path is a directory.
2. Read `extension.json` from source. If missing/invalid -> error.
3. Extract `name` from manifest.
4. Target = `~/.adocnet/extensions/<name>/`.
5. If target exists -> error: `Extension '<name>' is already installed. Remove it first with 'adocnet ext remove <name>'.`
6. Create `~/.adocnet/extensions/` if it doesn't exist.
7. Copy entire source directory to target.
8. Verify: re-read manifest from installed location.
9. Print: `Installed extension '<name>' v<version>.`

No overwrite. No update. Remove + install for updates. Simple and predictable.

### `ext remove <name>`

1. Target = `~/.adocnet/extensions/<name>/`.
2. If target doesn't exist -> error: `Extension '<name>' is not installed.`
3. Delete the directory recursively.
4. Print: `Removed extension '<name>'.`

### Parsing in Program.cs

Follow the existing `preview` subcommand pattern:

```csharp
if (args.Length > 0 && args[0] == "ext")
    return ParseExtArguments(args);
```

Add a new `CliArgs.Ext` variant:

```csharp
internal abstract record Ext(string Action) : CliArgs
{
    internal sealed record List() : Ext("list");
    internal sealed record Install(string SourcePath) : Ext("install");
    internal sealed record Remove(string Name) : Ext("remove");
}
```

### Help Text

```
Extension management:
  adocnet ext list              List installed extensions
  adocnet ext install <path>    Install extension from directory
  adocnet ext remove <name>     Remove an installed extension
```

---

## 8. Automatic Loading

### Decision: Option C — Always load unless `--no-extensions` is passed.

Reasoning:
- Extensions are explicitly installed by the user. They expect them to take effect.
- Requiring an opt-in flag (`--auto-extensions`) defeats the purpose of installing.
- A `--no-extensions` escape hatch handles debugging and one-off override scenarios.
- Consistent with how most CLI tools work (git hooks, npm scripts — run by default).

### Implementation

In `ConvertCommand`, before rendering:

```csharp
if (!run.NoExtensions)
{
    engine.LoadInstalledExtensions();
}
```

The `--no-extensions` flag is added to `CliArgs.Run` (default `false`).

### Interaction with Existing Flags

- `--extensions <path>` and `--extension-dir <dir>` continue to work as before.
- Installed extensions load **first** (from `~/.adocnet/extensions/`).
- CLI-specified extensions load **after** (from `--extensions` / `--extension-dir`).
- `--no-extensions` suppresses only auto-loading of installed extensions.
  CLI-specified `--extensions` / `--extension-dir` still apply (explicit > implicit).

### Loading Order

```
1. Auto-load installed extensions (unless --no-extensions)
2. Load --extensions paths (in order specified)
3. Load --extension-dir paths (in order specified)
```

This is deterministic and predictable.

---

## 9. Error Handling

All errors during extension loading are non-fatal. The pattern matches beta.6:
warn via `OnWarning` callback, skip the problematic item, continue.

### Error Cases

| Scenario | Behavior |
|----------|----------|
| `~/.adocnet/extensions/` doesn't exist | Silent skip (normal case — no extensions installed) |
| Subdirectory has no `extension.json` | Warn: `Extension '<dir>': missing extension.json, skipping` |
| `extension.json` is not valid JSON | Warn: `Extension '<dir>': invalid extension.json: <parse error>` |
| `name` field missing | Warn: `Extension '<dir>': manifest missing required 'name' field` |
| `entry` field missing | Warn: `Extension '<dir>': manifest missing required 'entry' field` |
| Entry DLL doesn't exist | Warn: `Extension '<name>': entry DLL not found: <path>` |
| Version incompatible | Warn: `Extension '<name>' requires AdocNet >= <min>, current is <current>, skipping` |
| DLL load failure | Delegated to `ExtensionLoader.LoadAssembly` error handling |

### CLI Command Errors

| Scenario | Behavior |
|----------|----------|
| `ext install <path>` — path doesn't exist | Error exit: `Source path not found: <path>` |
| `ext install <path>` — no extension.json | Error exit: `No extension.json found in <path>` |
| `ext install <path>` — already installed | Error exit: `Extension '<name>' is already installed.` |
| `ext remove <name>` — not installed | Error exit: `Extension '<name>' is not installed.` |

CLI commands are user-facing operations — they return nonzero exit codes on error,
unlike the loading pipeline which warns and continues.

---

## 10. Testing Strategy

### Unit Tests

**ExtensionManifest parsing:**
- Valid manifest with all fields
- Manifest with only required fields (name, entry)
- Missing name -> null result + warning
- Missing entry -> null result + warning
- Invalid JSON -> null result + warning
- Empty file -> null result + warning
- Unknown fields silently ignored

**VersionComparer:**
- Equal versions (compatible)
- Newer version (compatible)
- Older version (incompatible)
- Release vs prerelease
- Null/empty minimum (always compatible)
- Malformed version strings -> treat as incompatible or warn

**ExtensionDirectoryLoader:**
- Empty directory -> empty list
- Nonexistent directory -> empty list (no warning for default dir)
- Single valid extension -> loads successfully
- Multiple extensions -> deterministic alphabetical order
- Mix of valid and invalid -> loads valid, warns on invalid

### Integration Tests

**Install/remove cycle:**
- Install from directory -> files copied, manifest readable
- List after install -> extension appears
- Remove -> directory deleted
- List after remove -> empty

**Auto-loading:**
- Set up temp extension directory, configure engine, convert -> extension runs
- Same with `--no-extensions` -> extension does not run

### Test Infrastructure

Use temp directories (`Path.GetTempPath()`) for all tests.
Create test manifests and dummy DLLs programmatically.
Reuse `tests/AdocNet.TestExtension/` DLL for real loading tests.

---

## 11. Explicit Non-Goals

These are **out of scope** for beta.7:

1. **Remote registry / marketplace** — no download from URL, no package index.
2. **Dependency resolution** — extensions are independent. No inter-extension deps.
3. **Zip/nupkg packaging** — install source is a plain directory. Archive formats deferred.
4. **Extension signing or verification** — no signature checks. Trust model is local-only.
5. **Sandboxing or isolation** — extensions run in the host process with full trust.
6. **Extension configuration** — no per-extension settings file. Extensions use document attributes.
7. **Update command** — `ext install` does not overwrite. Use `ext remove` + `ext install`.
8. **Extension templates / scaffolding** — no `ext init` or `ext new` command.

---

## 12. File Plan

### New Files

| File | Description |
|------|-------------|
| `src/AdocNet.Core/Extensions/ExtensionManifest.cs` | Manifest model + JSON parsing |
| `src/AdocNet.Core/Extensions/ExtensionDirectoryLoader.cs` | Scan + load from extension dir |
| `src/AdocNet.Core/Extensions/VersionComparer.cs` | Semver-ish comparison |
| `src/AdocNet.Cli/ExtensionCommands.cs` | `ext list`, `ext install`, `ext remove` |
| `tests/AdocNet.Tests/Extensions/ExtensionManifestTests.cs` | Manifest parsing tests |
| `tests/AdocNet.Tests/Extensions/VersionComparerTests.cs` | Version comparison tests |
| `tests/AdocNet.Tests/Extensions/ExtensionDirectoryLoaderTests.cs` | Directory loading tests |

### Modified Files

| File | Change |
|------|--------|
| `Directory.Build.props` | Version -> `1.0.0-beta.7` |
| `src/AdocNet.Core/AdocNet.Core.csproj` | Add conditional System.Text.Json reference |
| `src/AdocNet.Core/AdocEngine.cs` | Add `LoadInstalledExtensions()` methods |
| `src/AdocNet.Cli/Program.cs` | Add `ext` subcommand parsing, `--no-extensions` flag |
| `src/AdocNet.Cli/ConvertCommand.cs` | Auto-load installed extensions |

### Phase Mapping

| Phase | Work |
|-------|------|
| P02 | ExtensionManifest model + JSON parsing + tests |
| P03 | ExtensionDirectoryLoader + VersionComparer + tests |
| P04 | Engine integration + CLI ext subcommands + auto-loading + tests |
| P05 | Documentation |
