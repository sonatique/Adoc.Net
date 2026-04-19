# Beta.8 — Extension Registry & Discovery Design

> Design document for AdocNet v1.0.0-beta.8.
> No code in this document — implementation follows in P02-P05.

---

## 1. Registry Purpose

The Extension Registry is a **local cached index** of installed extensions.
It aggregates manifest data from `~/.adocnet/extensions/` subdirectories
into a single `registry.json` file for fast querying without re-scanning
every directory on each operation.

**Source of truth**: the filesystem (`~/.adocnet/extensions/` subdirectories
with their `extension.json` manifests). The registry is a derived cache.

**If `registry.json` is missing or corrupt, it can be rebuilt** by scanning
the extension directory and reading all manifests. No data is lost.

**No remote functionality.** No network access, no downloads, no marketplace.
`ext search` searches locally-installed extensions only.

---

## 2. Registry Format (registry.json)

### Location

`~/.adocnet/registry.json` — same parent as the `extensions/` directory.

Platform paths:
- Windows: `%USERPROFILE%\.adocnet\registry.json`
- Linux/macOS: `~/.adocnet/registry.json`

### Format

```json
{
  "version": "1",
  "extensions": [
    {
      "name": "diagram",
      "version": "1.0.0",
      "description": "Diagram support via external tools",
      "path": "/home/user/.adocnet/extensions/diagram",
      "dependencies": "syntax-highlight >= 1.0.0, core-utils >= 0.5.0"
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

### Design Decisions

- **`"version": "1"`** — stored as a string (not integer) so `SimpleJsonParser`
  can read it without integer parsing. Enables future format migration.
  If version is missing or unrecognized, rebuild from filesystem.

- **`"dependencies"`** — stored as a single comma-separated string (not a JSON array).
  This keeps every field as a flat string value, which `SimpleJsonParser` already handles.
  Format: `"name >= version, name >= version"`. Empty string = no dependencies.

- **`"path"`** — absolute, normalized via `Path.GetFullPath()`. No relative paths,
  no trailing separators.

- **Extensions sorted by name** (ordinal) for deterministic output.

- **One version per extension name.** Duplicate names are not allowed.
  Installing an extension with the same name replaces the existing one.

---

## 3. JSON Handling — Extensions to SimpleJsonParser

### Decision: Extend SimpleJsonParser (Option 1)

Rationale: `SimpleJsonParser` is internal (209 lines), well-tested, and already handles
the hard parts (string escaping, whitespace, error recovery). The registry format is
deliberately designed to use only flat string values — no arrays, no integers, no nesting.
This means `ParseFlatObject()` already works for individual extension entries.

The only new capability needed is **parsing an array of flat objects** for the
`"extensions"` field.

### New Methods

#### Reading

```csharp
// Parse a JSON object where one key maps to an array of flat objects.
// Returns: (metadata dict, list of entry dicts)
// Example: { "version": "1", "extensions": [ {flat}, {flat} ] }
internal static (Dictionary<string, string> metadata,
                 List<Dictionary<string, string>> items)
    ParseObjectWithArray(string json, string arrayKey)
```

Implementation approach:
1. Reuse existing `ReadString`, `SkipWhitespace`, `SkipValue` helpers
2. Parse the top-level object. For string values, store in metadata dict.
3. When the key matches `arrayKey`, parse as `[` then repeated flat objects `]`.
4. Each array element is parsed via the existing flat-object logic.
5. ~60-80 lines of new code.

#### Writing

```csharp
// Serialize a registry structure to JSON.
internal static string SerializeRegistry(
    Dictionary<string, string> metadata,
    string arrayKey,
    List<Dictionary<string, string>> items,
    string[] fieldOrder)
```

Implementation approach:
1. StringBuilder-based. Write `{`, then metadata fields, then array.
2. `fieldOrder` parameter controls the order of fields within each object
   for deterministic output.
3. Proper JSON string escaping on all values (reuse `Escape()` — inverse of `Unescape()`).
4. Indented output (2 spaces) for human readability.
5. ~40-50 lines of new code.

### What Is NOT Added

- No general nested object parsing
- No number/boolean extraction
- No array-of-arrays
- Scope is strictly bounded to the registry format

---

## 4. ExtensionInfo Model

```csharp
namespace AdocNet.Extensions;

/// <summary>
/// Represents an installed extension's metadata as stored in the registry.
/// </summary>
public sealed class ExtensionInfo
{
    public string Name { get; }
    public string Version { get; }
    public string Description { get; }
    public string InstalledPath { get; }
    public IReadOnlyList<string> Dependencies { get; }
}
```

### Construction

- **From `ExtensionManifest`**: used during rebuild — reads manifest, normalizes path.
  `static FromManifest(ExtensionManifest manifest) -> ExtensionInfo`

- **From registry dict**: used during load — reads parsed JSON fields.
  `static FromDictionary(Dictionary<string, string> fields) -> ExtensionInfo?`

### Dependencies Format

Stored in registry as comma-separated string: `"name >= version, name >= version"`.
Parsed into `IReadOnlyList<string>` where each element is `"name >= version"`.
Empty string -> empty list.

### Equality

Two `ExtensionInfo` are considered the same extension if they have the same `Name`
(case-sensitive, ordinal comparison).

---

## 5. ExtensionRegistry Class

```csharp
namespace AdocNet.Extensions;

/// <summary>
/// Manages the local extension registry (registry.json).
/// Thread-safe for reads once loaded. Writes use atomic file operations.
/// </summary>
public sealed class ExtensionRegistry
{
    // Load or rebuild
    public static ExtensionRegistry Load(string? registryDir, Action<string>? onWarning);

    // Query
    public IReadOnlyList<ExtensionInfo> GetAll();
    public ExtensionInfo? Find(string name);
    public IReadOnlyList<ExtensionInfo> Search(string keyword);

    // Mutate + persist
    public void Add(ExtensionInfo info);
    public bool Remove(string name);
    public void Save();

    // Rebuild from filesystem
    public static ExtensionRegistry Rebuild(string? extensionsDir, Action<string>? onWarning);
}
```

### Load Behavior

1. Compute registry path: `<registryDir>/registry.json` (default: `~/.adocnet/registry.json`)
2. If file doesn't exist -> rebuild from filesystem
3. If file exists, read and parse via `SimpleJsonParser.ParseObjectWithArray()`
4. Validate version field = "1". If missing or unrecognized -> rebuild
5. Parse each extension entry into `ExtensionInfo`
6. Validate against filesystem (see rebuild triggers below)
7. Return loaded registry

### Save Behavior (Atomic Write)

1. Serialize to JSON string via `SimpleJsonParser.SerializeRegistry()`
2. Write to temp file: `registry.json.tmp` in same directory
3. If `registry.json` exists, delete it
4. Rename temp file to `registry.json`
5. This prevents partial writes from corrupting the registry

On Windows, `File.Move` with overwrite requires .NET Core 3.0+. Since we target
netstandard2.0, the sequence is: write temp -> delete original -> rename temp.
If the process crashes between delete and rename, the next load triggers a rebuild.

### Search Behavior

`Search(string keyword)` matches against `Name` and `Description` fields.
Case-insensitive substring match (ordinal ignore-case). Returns matches sorted by name.

### Internal Storage

`List<ExtensionInfo>` sorted by `Name` (ordinal). All mutations maintain sorted order.

---

## 6. Registry Lifecycle and Rebuild Triggers

### Normal Operations

| CLI Command | Registry Effect |
|-------------|----------------|
| `ext install <path>` | Copy files, read manifest, `Add(info)`, `Save()` |
| `ext install <path> --force` | Copy files (overwrite), read manifest, `Add(info)`, `Save()` |
| `ext remove <name>` | Delete directory, `Remove(name)`, `Save()` |
| `ext list` | `Load()`, `GetAll()`, print table |
| `ext info <name>` | `Load()`, `Find(name)`, print details |
| `ext search <keyword>` | `Load()`, `Search(keyword)`, print matches |

### Rebuild Triggers

A full rebuild (scan `~/.adocnet/extensions/`, read all manifests, regenerate registry)
is triggered when ANY of these conditions is detected during `Load()`:

1. **`registry.json` missing** — first use or deleted
2. **`registry.json` corrupt** — invalid JSON, parse failure
3. **Version mismatch** — `"version"` field missing or not `"1"`
4. **Stale entry detected** — extension in registry but directory missing from disk
5. **Missing entry detected** — extension directory exists on disk but not in registry

Conditions 4-5 are checked by comparing registry names against actual subdirectories
during `Load()`. If any mismatch is found, a full rebuild replaces the loaded data.

### Rebuild Process

1. Get extensions directory (default: `~/.adocnet/extensions/`)
2. List subdirectories, sorted alphabetically
3. For each: read `extension.json` via `ExtensionManifest.Load()`
4. Convert each valid manifest to `ExtensionInfo.FromManifest()`
5. Create new `ExtensionRegistry` with the collected entries
6. `Save()` the rebuilt registry
7. Warn about skipped directories (missing/invalid manifests)

---

## 7. CLI Extensions

### New Command: `ext info <name>`

```
$ adocnet ext info diagram

Extension: diagram
Version:   1.0.0
Description: Diagram support via external tools
Path:      /home/user/.adocnet/extensions/diagram
Entry:     DiagramExtension.dll
Min AdocNet: 1.0.0-beta.5
Dependencies:
  - syntax-highlight >= 1.0.0
```

Reads from registry for most fields. For `Entry` and `MinAdocNetVersion`,
reads the actual `extension.json` manifest from disk (these aren't in the registry
since they're only needed for detailed display and loading).

If the extension is not found in the registry, prints an error and exits.

### New Command: `ext search <keyword>`

```
$ adocnet ext search diagram

Search results for "diagram":

  Name      Version  Description
  -------   -------  -----------
  diagram   1.0.0    Diagram support via external tools

1 extension(s) matched.
```

Searches `Name` and `Description` fields. Case-insensitive substring match.
If no matches, prints "No extensions match '<keyword>'."

### Modified Command: `ext list`

Changed from filesystem scan to registry-based. Calls `ExtensionRegistry.Load()`,
then `GetAll()`, then prints the same table format as before.

Behavior is identical to the user — same output format, same sort order.
Performance improvement: reads one JSON file instead of N manifest files.

### Modified Commands: `ext install` and `ext remove`

After filesystem operations complete (copy/delete), update the registry:
- `install`: `registry.Add(info); registry.Save();`
- `remove`: `registry.Remove(name); registry.Save();`

### CliArgs Additions

```csharp
internal abstract record Ext() : CliArgs
{
    internal sealed record ExtList() : Ext;
    internal sealed record ExtInstall(string SourcePath, bool Force = false) : Ext;
    internal sealed record ExtRemove(string Name) : Ext;
    internal sealed record ExtInfo(string Name) : Ext;       // NEW
    internal sealed record ExtSearch(string Keyword) : Ext;  // NEW
}
```

### ParseExtArguments Changes

Add cases for `"info"` and `"search"` in the switch statement.
Both require exactly one positional argument.

---

## 8. Dependency Metadata

### Format in extension.json

```json
{
  "name": "diagram",
  "version": "1.0.0",
  "description": "Diagram support via external tools",
  "entry": "DiagramExtension.dll",
  "minAdocNetVersion": "1.0.0-beta.5",
  "dependencies": "syntax-highlight >= 1.0.0, core-utils >= 0.5.0"
}
```

`"dependencies"` is a **comma-separated string** of dependency specifications.
Each spec: `"<name> >= <version>"`. Only `>=` operator is supported.

### Storage in registry.json

Same format — comma-separated string stored as a flat string value.

### Parsing

```csharp
// Parse "name >= version" into (name, version)
internal static (string name, string minVersion)?
    ParseDependencySpec(string spec)
```

Splits on `" >= "`. Trims whitespace. Returns null if format is invalid.

### Validation

During `ext install` (after adding to registry):
1. Parse the new extension's dependencies
2. For each dependency, check if it exists in the registry
3. If exists, check version compatibility via `ExtensionDirectoryLoader.IsVersionCompatible()`
4. If missing or incompatible: **warn** (do not block install)

```
Warning: Extension 'diagram' depends on 'syntax-highlight >= 1.0.0' which is not installed.
```

During `engine.LoadInstalledExtensions()`: no dependency checking.
Dependencies are advisory — they help users but don't block loading.

### What Dependencies Do NOT Do

- No automatic installation of missing dependencies
- No dependency resolution or ordering
- No transitive dependency checking
- No blocking — always warn, never fail

---

## 9. Engine Integration

### New Methods on AdocEngine

```csharp
/// <summary>
/// Returns metadata for all installed extensions from the registry.
/// Does not load or register any extensions — read-only query.
/// </summary>
public static IReadOnlyList<ExtensionInfo> GetInstalledExtensions(
    string? extensionsDir = null,
    Action<string>? onWarning = null);

/// <summary>
/// Finds a specific installed extension by name from the registry.
/// Does not load or register the extension — read-only query.
/// </summary>
public static ExtensionInfo? FindExtension(
    string name,
    string? extensionsDir = null,
    Action<string>? onWarning = null);
```

These are **static methods** — they don't require an engine instance.
They're convenience wrappers around `ExtensionRegistry.Load()` + query.

They do NOT affect rendering, do NOT register processors, and do NOT
interact with the `_frozen` flag.

---

## 10. Error Handling

### Registry Loading Errors

| Error | Response |
|-------|----------|
| `registry.json` missing | Rebuild silently |
| `registry.json` unreadable (permissions) | Warn, return empty registry |
| `registry.json` invalid JSON | Warn, rebuild |
| `registry.json` unknown version | Warn, rebuild |
| Registry/filesystem mismatch | Rebuild silently |

### Registry Write Errors

| Error | Response |
|-------|----------|
| Cannot create temp file | Warn, skip save (registry is a cache — not fatal) |
| Cannot delete old registry | Warn, skip save |
| Cannot rename temp file | Warn, skip save |
| Directory doesn't exist | Create it (mkdir -p equivalent) |

### Manifest Loading Errors (during rebuild)

| Error | Response |
|-------|----------|
| Missing extension.json | Skip directory, warn |
| Invalid extension.json | Skip directory, warn |
| Missing required fields | Skip directory, warn |

### Atomic Write Strategy

1. Build JSON string in memory
2. Write to `registry.json.tmp` in the same directory
3. Delete `registry.json` if it exists
4. Rename `registry.json.tmp` to `registry.json`

If the process crashes between steps 3 and 4, the registry file is missing.
The next `Load()` detects this and triggers a rebuild. No data is lost because
the filesystem is the source of truth.

---

## 11. Explicit Non-Goals

These are deliberately excluded from beta.8:

- **Remote registry / marketplace** — no network access
- **Auto-updates** — no checking for new versions
- **Extension downloads** — `ext install` takes a local path only
- **Sandboxing** — extensions run in the same process with full trust
- **Dependency resolution** — only validation (warn if missing)
- **Transitive dependencies** — only direct dependencies checked
- **Extension signing** — no verification of extension authenticity
- **Enable/disable state** — deferred to a future `ext disable` command
- **Lock files** — single-user local tool, last-write-wins
- **Extension configuration** — extensions have no config system in beta.8

---

## 12. File Plan

### New Files

| File | Purpose |
|------|---------|
| `src/AdocNet.Core/Extensions/ExtensionInfo.cs` | Registry entry model |
| `src/AdocNet.Core/Extensions/ExtensionRegistry.cs` | Registry read/write/rebuild |
| `tests/AdocNet.Core.Tests/Extensions/ExtensionRegistryTests.cs` | Registry tests |
| `tests/AdocNet.Core.Tests/Extensions/SimpleJsonParserRegistryTests.cs` | Extended parser tests |

### Modified Files

| File | Changes |
|------|---------|
| `src/AdocNet.Core/Extensions/SimpleJsonParser.cs` | Add `ParseObjectWithArray`, `SerializeRegistry` |
| `src/AdocNet.Core/Extensions/ExtensionManifest.cs` | Add `Dependencies` property (additive) |
| `src/AdocNet.Cli/ExtensionCommands.cs` | Add `ext info`, `ext search`, registry integration |
| `src/AdocNet.Cli/Program.cs` | Add `ExtInfo`, `ExtSearch` to `CliArgs.Ext` |
| `src/AdocNet.Core/AdocEngine.cs` | Add static `GetInstalledExtensions`, `FindExtension` |
| `Directory.Build.props` | Version bump to `1.0.0-beta.8` |

---

## 13. Phase Mapping

| Phase | Deliverables |
|-------|-------------|
| P02 | `ExtensionInfo` model, `SimpleJsonParser` extensions, `ExtensionRegistry` class, tests |
| P03 | Engine integration, CLI `ext info`/`ext search`, modify `ext list`/`install`/`remove` |
| P04 | Dependency metadata in manifest, validation during install, tests |
| P05 | Documentation, GETTING-STARTED update |
