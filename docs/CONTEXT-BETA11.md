# Beta.11 Context Discovery

> Generated: 2026-04-05. Read-only discovery — no source files modified.

## 1. Confirmed Existing Infrastructure

### 1.1 Source Mapping (AdocNet.Ast)

**SourcePosition** (`src/AdocNet.Ast/SourcePosition.cs`)
- `readonly record struct SourcePosition(int Line, int Column) : IComparable<SourcePosition>`
- 1-based line and column
- `None` sentinel (0,0), comparison operators, `IsNone`

**SourceRange** (`src/AdocNet.Ast/SourceRange.cs`)
- `readonly record struct SourceRange(SourcePosition Start, SourcePosition End)`
- `None` sentinel, `IsNone`, `Contains(SourcePosition)`
- Every `AstNode` has `SourceRange Source` property (populated by parser)

### 1.2 Diagnostics (AdocNet.Core)

**DiagnosticSeverity** (`src/AdocNet.Core/DiagnosticSeverity.cs`)
- `enum DiagnosticSeverity { Info, Warning, Error }`

**Diagnostic** (`src/AdocNet.Core/Diagnostic.cs`)
- `sealed record Diagnostic(DiagnosticSeverity Severity, string Message, SourceRange Range)`
- Optional `FilePath` (init-only property)
- `IsError`, `IsWarning` convenience properties

### 1.3 ParseResult (AdocNet.Parser)

**ParseResult** (`src/AdocNet.Parser/ParseResult.cs`)
- `sealed record ParseResult(DocumentNode Document, IReadOnlyList<Diagnostic> Diagnostics)`
- `HasErrors`, `HasWarnings` convenience properties
- Used by `AdocParser.Parse(string)` — returns structured result

### 1.4 AdocEngine (AdocNet.Core)

**AdocEngine** (`src/AdocNet.Core/AdocEngine.cs`) — 413 lines

Constructor: `AdocEngine(IDocumentRenderer renderer, Func<string, DocumentNode> parser)`

Key properties:
- `Renderer` (IDocumentRenderer)
- `Parser` (Func<string, DocumentNode>)
- `OnWarning` (Action<string>?)
- `MaxProcessorFailures` (int, default 3)
- `EnableCaching` (bool, default false)
- `MaxCacheEntries` (int, default 16)
- `ExtensionApiVersion` (const "1.0")

Convert flow (caching enabled):
```
1. ComputeInputHash(input) via SHA-256
2. Check render cache (inputHash + format + options) → if hit, write bytes and return
3. Check parse cache (inputHash) → if miss, Parser(input) and cache result
4. RunExtensions(doc, opts) — ProcessingPipeline.Run()
5. Renderer.Render(doc, buffer, opts) → cache bytes → write to output
```

Convert flow (caching disabled):
```
1. Parser(input)
2. RunExtensions(doc, opts)
3. Renderer.Render(doc, output, opts)
```

Registration methods (all fluent, throw if frozen):
- `RegisterDocumentProcessor/BlockProcessor/InlineProcessor`
- `LoadExtension(string assemblyPath)`
- `LoadExtensions(string directoryPath)`
- `LoadInstalledExtensions()` / `LoadInstalledExtensions(string)`
- `LoadExtensionSafe(string)` / `LoadExtensionsSafe(string)` — return `IReadOnlyList<ExtensionLoadResult>`

Static query methods:
- `GetInstalledExtensions(string?, Action<string>?)` → `IReadOnlyList<ExtensionInfo>`
- `FindExtension(string, string?, Action<string>?)` → `ExtensionInfo?`

Cache infrastructure:
- `LruCache<TKey, TValue>` (internal, thread-safe, O(1) via dict + linked list)
- `CacheKeyBuilder` (SHA-256, deterministic)
- `ClearCache()` public method

### 1.5 RenderContext (AdocNet.Core)

**RenderContext** (`src/AdocNet.Core/RenderContext.cs`) — 48 lines
- `Document` (DocumentNode), `Options` (RenderOptions), `Attributes` (IReadOnlyDictionary)
- `GetOrCreate<T>(Func<T> factory)` — typed per-render state bag
- **No `AddDiagnostic` method** — beta.11 adds this

### 1.6 IDiagramToolRunner (AdocNet.Core/Extensions)

**IDiagramToolRunner** (`src/AdocNet.Core/Extensions/IDiagramToolRunner.cs`)
```csharp
public interface IDiagramToolRunner
{
    bool IsAvailable { get; }
    string? Generate(string language, string source, string outputDirectory);
}
```
- `ProcessDiagramToolRunner` — concrete implementation invoking external tools via Process
- `DiagramBlockProcessor` — uses IDiagramToolRunner, supports PlantUML/Mermaid/Ditaa/Graphviz/DOT
- Beta.11 adds `KrokiDiagramToolRunner` implementing same interface via HTTP

### 1.7 ExtensionInfo (AdocNet.Core/Extensions)

**ExtensionInfo** (`src/AdocNet.Core/Extensions/ExtensionInfo.cs`) — 109 lines
- Properties: `Name`, `Version`, `Description`, `InstalledPath`, `Dependencies`
- `FromManifest(ExtensionManifest)` — factory from manifest
- `FromDictionary(Dictionary<string,string>)` — factory from JSON fields
- **No `Enabled` property** — beta.11 adds this

### 1.8 CLI ExtensionCommands

**ExtensionCommands** (`src/AdocNet.Cli/ExtensionCommands.cs`) — 384 lines

Existing subcommands:
- `ext list` — displays installed extensions from registry
- `ext install <source-path> [--force]` — copies directory, validates manifest, updates registry
- `ext remove <name>` — deletes directory, updates registry
- `ext info <name>` — shows detailed extension info
- `ext search <keyword>` — searches registry locally
- `ext status` — shows per-extension load state (Loaded/Failed/Incompatible)

Beta.11 adds:
- `ext enable <name>` — sets Enabled=true in registry
- `ext disable <name>` — sets Enabled=false in registry

### 1.9 Existing Extension/Hardening Types

- `IExtension` — optional metadata (Name, Version)
- `ExtensionManifest` — manifest model with `ApiVersion` field
- `ExtensionLoader` — Assembly.LoadFrom + reflection scanning
- `ExtensionDirectoryLoader` — scans `~/.adocnet/extensions/`, version compat
- `ExtensionRegistry` — Load/Save/Add/Remove/Find/Search/Rebuild
- `ExtensionState` — enum: Loaded, Failed, Disabled, Incompatible
- `ExtensionLoadResult` — structured load result (Name, State, FailureReason, Processors)
- `ProcessingPipeline` — depth-first walk, per-processor try/catch, failure counting
- `DependencyValidator` — warn-only dependency checking

## 2. Confirmed NOT Existing (Beta.11 New Work)

Grep for `DocumentChange|DocumentSnapshot|IOutputProcessor|KrokiDiagram|IExtensionLifecycle` across `src/` returned **zero results**.

| Type | Status | Notes |
|------|--------|-------|
| `DocumentChange` | Does not exist | Immutable struct for incremental edits |
| `DocumentSnapshot` | Does not exist | Versioned document state |
| `ParseIncremental` | Does not exist | New AdocEngine method |
| `IOutputProcessor` | Does not exist | Post-render transformation interface |
| `KrokiDiagramToolRunner` | Does not exist | HTTP-based IDiagramToolRunner via Kroki |
| `IExtensionLifecycle` | Does not exist | Optional init/dispose for resource-holding extensions |
| Zip install support | Does not exist | `ext install` only handles directories |
| Extension enable/disable | Does not exist | No `Enabled` on ExtensionInfo |
| `RenderContext.AddDiagnostic` | Does not exist | No diagnostic collection on RenderContext |

## 3. Key Architecture Notes

### 3.1 Caching Integration Point
`ParseIncremental` can leverage the existing `LruCache<string, DocumentNode>` parse cache.
Flow: apply DocumentChanges to get new text → compute hash → check parse cache → if miss, full re-parse.
No true incremental parser — cache-assisted only.

### 3.2 IOutputProcessor Pipeline Position
Current: Parse → Extensions → Render
Beta.11: Parse → Extensions → Render → **OutputProcessors**
Must run after `Renderer.Render()` completes, operating on the rendered bytes.

### 3.3 Extension Enable/Disable Storage
`ExtensionInfo` needs an `Enabled` property (default true).
`ExtensionRegistry` already persists to `registry.json` — add `enabled` field.
`ExtensionDirectoryLoader.LoadInstalledExtensions` must skip disabled extensions.

### 3.4 Kroki HTTP Dependency
`KrokiDiagramToolRunner` introduces `HttpClient` usage — the only network dependency.
Must be clearly opt-in. Implements existing `IDiagramToolRunner` interface.
No new NuGet packages needed — `HttpClient` is in `System.Net.Http` (available on both TFMs).

### 3.5 IExtensionLifecycle
Custom interface (not `IDisposable`). Optional — extensions that don't implement it are unaffected.
Called by loading infrastructure at appropriate lifecycle points.

### 3.6 Extension Diagnostics
`RenderContext` gets `AddDiagnostic(Diagnostic)` + `Diagnostics` collection.
Extensions emit diagnostics during processing. Available after Convert() via engine property.
Connects to LSP diagnostic pipeline for editor scenarios.

## 4. Project Conventions

- Solution: `.slnx`
- Core TFMs: `netstandard2.0;net10.0`
- LangVersion: `preview`, Nullable: `enable`, TreatWarningsAsErrors: `true`
- Test framework: NUnit (`[Test]`)
- Zero external NuGet deps on Core
- Version: bump to `1.0.0-beta.11`

## 5. File Map — Where Beta.11 Code Goes

| New Type | Location |
|----------|----------|
| `DocumentChange` | `src/AdocNet.Core/Editor/DocumentChange.cs` |
| `DocumentSnapshot` | `src/AdocNet.Core/Editor/DocumentSnapshot.cs` |
| `IOutputProcessor` | `src/AdocNet.Core/Extensions/IOutputProcessor.cs` |
| `IExtensionLifecycle` | `src/AdocNet.Core/Extensions/IExtensionLifecycle.cs` |
| `KrokiDiagramToolRunner` | `src/AdocNet.Core/Extensions/KrokiDiagramToolRunner.cs` |
| Zip install logic | `src/AdocNet.Cli/ExtensionCommands.cs` (modify) |
| Enable/disable | `src/AdocNet.Core/Extensions/ExtensionInfo.cs` (modify) + CLI |
| `ParseIncremental` | `src/AdocNet.Core/AdocEngine.cs` (modify) |
| `AddDiagnostic` | `src/AdocNet.Core/RenderContext.cs` (modify) |
