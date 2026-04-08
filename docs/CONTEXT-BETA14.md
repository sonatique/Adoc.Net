# Beta.14 Context — Ecosystem Readiness

> Generated from source code analysis. Read-only discovery phase.

## 1. DependencySpec (`src/AdocNet.Core/Extensions/DependencySpec.cs`)

### Parse Format
- Input: string like `"name >= version"` or just `"name"`
- Parses on `>=` operator (only operator supported)
- Returns `DependencySpec` with `Name` (required) and `MinVersion` (nullable)
- Returns `null` for empty/whitespace or missing name
- No `<=`, `==`, `!=`, or range operators supported

### Properties
- `string Name` — required extension name
- `string? MinVersion` — minimum version or null (any version)

### Constructor
- `DependencySpec(string name, string? minVersion)` — throws on null name

## 2. DependencyValidator (`src/AdocNet.Core/Extensions/DependencyValidator.cs`)

### Validate Flow
1. Iterates `extension.Dependencies` (list of strings)
2. Parses each string via `DependencySpec.Parse()`
3. Looks up `registry.Find(spec.Name)` — returns `ExtensionInfo?`
4. If not found: emits warning "depends on X which is not installed"
5. If found but version too low: emits warning with installed vs required version
6. Uses `ExtensionDirectoryLoader.IsVersionCompatible()` for version comparison

### Key Behavior
- **Warn-only, never blocks loading** — all dependency issues are warnings
- No topological ordering — just checks if dependencies exist
- No cycle detection
- Static class, stateless

## 3. ExtensionDirectoryLoader (`src/AdocNet.Core/Extensions/ExtensionDirectoryLoader.cs`)

### Load Order: ALPHABETICAL (no dependency sorting)
1. Gets subdirectories of extension root dir
2. **Sorts alphabetically** by directory name (`StringComparison.Ordinal`)
3. For each subdir: loads manifest, checks enabled state, checks version compat
4. Calls `ExtensionLoader.LoadAssembly()` for each valid extension

### Version Checks (already exist)
- `MinAdocNetVersion`: skips if current < min
- `MaxAdocNetVersion`: skips if current > max (added in beta.12)
- `IsVersionCompatible(current, minimum)`: strips semver prerelease, compares numeric, then prerelease string
- `IsApiVersionCompatible(host, ext)`: major must match, ext minor <= host minor

### What's Missing
- **No dependency-based load ordering** — if A depends on B but "A" < "B" alphabetically, A loads first
- **No signing verification** — `Assembly.GetName().GetPublicKeyToken()` never called
- **No topological sort** — extensions load in filesystem order regardless of dependencies

## 4. ExtensionManifest (`src/AdocNet.Core/Extensions/ExtensionManifest.cs`)

### Current Fields
| Field | Type | Required | Source |
|-------|------|----------|--------|
| `Name` | `string` | Yes | `"name"` |
| `Version` | `string` | No (default "0.0.0") | `"version"` |
| `Description` | `string` | No (default "") | `"description"` |
| `Entry` | `string` | Yes | `"entry"` |
| `MinAdocNetVersion` | `string?` | No | `"minAdocNetVersion"` |
| `MaxAdocNetVersion` | `string?` | No | `"maxAdocNetVersion"` |
| `ApiVersion` | `string?` | No | `"apiVersion"` |
| `Dependencies` | `IReadOnlyList<string>` | No | `"dependencies"` (array or comma-sep) |
| `DirectoryPath` | `string` | Set internally | Extension directory path |

### Missing Fields (beta.14 adds)
- **`publicKeyToken`** — not in manifest, not parsed, not checked

### JSON Parsing
- Uses `SimpleJsonParser.ParseFlatObject()` for scalar fields
- Dependencies parsed via `ParseDependenciesArray()` (string array) or comma-separated fallback
- Private constructor — only created via `Load()` or `Parse()` static methods

## 5. ExtensionLoader (`src/AdocNet.Core/Extensions/ExtensionLoader.cs`)

### Assembly Loading
- **net6.0+**: Uses `ExtensionLoadContext` (custom `AssemblyLoadContext`, added beta.13)
  - Checks if assembly already loaded in default context to avoid duplicates
  - Creates isolated context per extension
- **netstandard2.0**: Uses `Assembly.LoadFrom()`

### No Signing Checks
- `Assembly.GetName().GetPublicKeyToken()` is **never called** anywhere in the codebase
- No verification of assembly identity after loading
- grep confirms: zero matches for `GetPublicKeyToken` or `PublicKeyToken` in `src/`

### Processor Discovery
- Scans for types implementing `IDocumentProcessor`, `IBlockProcessor`, `IInlineProcessor`
- Requires parameterless constructor
- Sorted by `FullName` for deterministic order

## 6. ExtensionInfo (`src/AdocNet.Core/Extensions/ExtensionInfo.cs`)

### Properties
- `Name`, `Version`, `Description`, `InstalledPath`, `Dependencies`, `Enabled`
- `Enabled` defaults to `true`
- `FromManifest()` creates from manifest, normalizes path
- `FromDictionary()` creates from JSON fields (used by registry deserialization)
- `DependenciesToString()` / `ParseDependencies()` for comma-separated serialization

## 7. CLI ExtensionCommands (`src/AdocNet.Cli/ExtensionCommands.cs`)

### Existing Subcommands
| Command | Description |
|---------|-------------|
| `ext list` | List installed extensions with status |
| `ext install <path> [--force]` | Install from directory or zip |
| `ext remove <name>` | Uninstall extension |
| `ext info <name>` | Show extension details |
| `ext search <keyword>` | Search installed extensions |
| `ext status` | Show per-extension load state |
| `ext enable <name>` | Enable a disabled extension |
| `ext disable <name>` | Disable an extension |

### Missing Subcommands (beta.14 adds)
- **`ext validate <path>`** — not implemented, not in parser, not in usage string

### Implementation Pattern
- `ParseExtArguments()` returns `CliArgs.Ext.*` discriminated union
- `Execute()` pattern-matches on the union type
- Each command is a private static method returning exit code (0/1)

## 8. Summary of Beta.14 Gaps

### Theme A — Dependency-Ordered Loading
- `DependencySpec` and `DependencyValidator` exist but only do warn-only checks
- `ExtensionDirectoryLoader` sorts alphabetically, not by dependency graph
- No topological sort anywhere — need new `DependencyResolver` class
- Kahn's algorithm (BFS topo sort) is the planned approach

### Theme B — Extension Signing Verification
- `Assembly.GetName().GetPublicKeyToken()` is available on both TFMs but never used
- No `publicKeyToken` field in `ExtensionManifest`
- No signing check in `ExtensionDirectoryLoader` or `ExtensionLoader`
- Need: manifest field + post-load token comparison + skip on mismatch

### Theme C — Extension Validation Tool
- No `ext validate` subcommand exists
- Need: CLI command, validation logic (manifest, DLL, processors, deps, versions, signing)
- All building blocks exist (manifest loading, assembly scanning, version checks)
- Just need to compose them into a validation report

## 9. Key Types and Locations

| Type | File | Role |
|------|------|------|
| `DependencySpec` | `src/AdocNet.Core/Extensions/DependencySpec.cs` | Parse dep strings |
| `DependencyValidator` | `src/AdocNet.Core/Extensions/DependencyValidator.cs` | Warn-only dep check |
| `ExtensionDirectoryLoader` | `src/AdocNet.Core/Extensions/ExtensionDirectoryLoader.cs` | Load from extension dirs |
| `ExtensionLoader` | `src/AdocNet.Core/Extensions/ExtensionLoader.cs` | Load from DLLs |
| `ExtensionManifest` | `src/AdocNet.Core/Extensions/ExtensionManifest.cs` | Parse extension.json |
| `ExtensionInfo` | `src/AdocNet.Core/Extensions/ExtensionInfo.cs` | Registry entry model |
| `ExtensionCommands` | `src/AdocNet.Cli/ExtensionCommands.cs` | CLI ext subcommands |
| `ExtensionLoadContext` | `src/AdocNet.Core/Extensions/ExtensionLoadContext.cs` | ALC isolation (net6.0+) |
| `AdocEngine` | `src/AdocNet.Core/AdocEngine.cs` | Main engine |
