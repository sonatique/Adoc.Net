# Safety Audit — Beta.9 Hardening Baseline

Audit date: 2026-03-31
Scope: All error-handling paths in the extension system (beta.8 baseline).

---

## 1. ExtensionLoader.cs — Catch Blocks (4 total)

| # | Location (line) | Exception Type | Behavior |
|---|-----------------|---------------|----------|
| 1 | L39 | `BadImageFormatException` | Warning "Not a valid .NET assembly", return empty |
| 2 | L44 | `FileNotFoundException` | Warning with message, return empty |
| 3 | L55 | `ReflectionTypeLoadException` | Warning "Partial load", continue with loadable types |
| 4 | L86 | `Exception` (Activator.CreateInstance) | Unwraps `TargetInvocationException`, warning, skip type |

Additional non-exception error paths:
- L28-32: File.Exists check — warning "Extension not found", return empty
- L74-79: No parameterless constructor — warning, skip type
- L116-119 (LoadDirectory): Directory.Exists check — warning, return empty
- L126-129 (LoadDirectory): No DLLs found — warning, return empty

**Observation**: All 4 catch blocks produce string warnings. No structured error type returned.
The caller gets `List<object>` — no way to know which types failed or why.

---

## 2. ProcessingPipeline.cs — Catch Blocks (3 total)

| # | Location (line) | Scope | Behavior |
|---|-----------------|-------|----------|
| 1 | L30 | Document processor invocation | Warning with processor name + exception, continue to next |
| 2 | L68 | Block processor invocation | Warning with processor name + exception, continue to next |
| 3 | L167 | Inline processor invocation | Warning with processor name + exception, continue to next |

**Observation**: Every processor invocation is wrapped in try/catch. A failing processor
produces a string warning and the pipeline continues. There is:
- No failure count tracked
- No disabling of repeatedly-failing processors
- No per-processor state (loaded/failed/disabled)
- No way to know at the end of a render how many failures occurred

---

## 3. ExtensionDirectoryLoader.cs — Error Paths

| # | Path | Behavior |
|---|------|----------|
| 1 | L31: directory doesn't exist | Return empty (silent — no warning) |
| 2 | L41: ExtensionManifest.Load returns null | Skip extension (warning from manifest loader) |
| 3 | L47-51: Version incompatible | Warning "requires AdocNet >= X", skip |
| 4 | L57-60: Entry DLL not found | Warning "entry DLL not found", skip |

Version compatibility logic (`IsVersionCompatible`):
- Strips semver prerelease suffixes, compares numeric parts
- Release > prerelease for same numeric version
- Unparseable current version → false (incompatible)
- Unparseable minimum version → true (allow)
- Null/empty minimum → true (allow)

**Observation**: Version check is thorough. Error paths all produce warnings.
But there's no structured result — the caller gets `List<object>` with no
indication which extensions were skipped or why.

---

## 4. DependencyValidator.cs — Error Paths

| # | Path | Behavior |
|---|------|----------|
| 1 | DependencySpec.Parse returns null | Skip (silent) |
| 2 | Dependency not installed | Warning "depends on X which is not installed" |
| 3 | Dependency version too low | Warning "depends on X, but installed version is Y" |

**Observation**: Warn-only, never blocks. No exceptions thrown from validation logic
(only from null argument checks). Dependencies are advisory — this is correct for beta.8
and should remain so in beta.9.

---

## 5. AdocEngine.cs — Error Paths

| # | Path | Behavior |
|---|------|----------|
| 1 | ThrowIfFrozen (L218-221) | `InvalidOperationException` if Register/Load after Convert |
| 2 | Constructor null args (L36-37) | `ArgumentNullException` |
| 3 | RegisterDocumentProcessor null (L49) | `ArgumentNullException` |
| 4 | RegisterBlockProcessor null (L60) | `ArgumentNullException` |
| 5 | RegisterInlineProcessor null (L71) | `ArgumentNullException` |
| 6 | LoadExtension → ExtensionLoader.LoadAssembly | Delegates to loader; warnings via OnWarning |
| 7 | LoadExtensions → ExtensionLoader.LoadDirectory | Delegates to loader; warnings via OnWarning |
| 8 | LoadInstalledExtensions → ExtensionDirectoryLoader | Delegates to directory loader; warnings via OnWarning |
| 9 | Convert → ProcessingPipeline.Run | Pipeline catches per-processor; warnings via OnWarning |

Convert flow:
1. Parser(input) — no try/catch; parser exceptions propagate to caller
2. If any processors registered: set `_frozen = true`, create RenderContext, run pipeline
3. Renderer.Render — no try/catch; renderer exceptions propagate to caller

**Observation**: Load methods return `this` (fluent) — no indication of success/failure.
The only feedback channel is `OnWarning`. There are no `LoadExtensionSafe` variants
that return structured results.

---

## 6. ExtensionRegistry.cs — Error Paths

| # | Path | Behavior |
|---|------|----------|
| 1 | L37-38: registry.json missing | Rebuild from filesystem |
| 2 | L43-49: File.ReadAllText fails | Warning, return empty registry |
| 3 | L55-61: JSON parse fails (FormatException) | Warning "corrupt", rebuild |
| 4 | L63-67: Version mismatch | Warning, rebuild |
| 5 | L79: Stale check fails | Rebuild |
| 6 | L113-120 (Rebuild): Save fails | Warning, return registry unsaved |
| 7 | L157-171 (Save): Write/Move fails | Warning, best-effort cleanup of temp file |

**Observation**: Registry is resilient — corrupt/missing state triggers rebuild.
The catch in Save (L166-171) has an empty inner catch for temp file cleanup,
which is acceptable (best-effort). No structured error reporting for callers.

---

## 7. ExtensionManifest.cs — Error Paths

| # | Path | Behavior |
|---|------|----------|
| 1 | L60-63: extension.json missing | Warning, return null |
| 2 | L68-75: File.ReadAllText fails | Warning, return null |
| 3 | L100-106: JSON parse fails (FormatException) | Warning, return null |
| 4 | L108-112: Empty JSON | Warning, return null |
| 5 | L121-125: Missing "name" field | Warning, return null |
| 6 | L127-131: Missing "entry" field | Warning, return null |
| 7 | L177-179: Dependencies array parse fails | Return empty (silent) |

---

## 8. Identified Gaps — What Beta.9 Must Address

### Gap 1: No Per-Extension State Tracking

There is no `ExtensionState` enum or equivalent. After loading, an extension is either
in the processor list or silently absent. The system cannot distinguish between:
- Extension loaded successfully
- Extension failed to load (bad DLL, no constructor, etc.)
- Extension skipped (version incompatible)
- Extension disabled due to repeated failures

**Impact**: Users cannot query "why isn't my extension running?" without reading warning logs.

### Gap 2: No Failure-Based Disabling

ProcessingPipeline catches exceptions per-processor invocation but:
- Does not count failures per processor
- Does not disable processors that fail repeatedly
- A buggy processor produces a warning on every single document node it processes
- No `MaxProcessorFailures` threshold

**Impact**: A processor throwing on every paragraph will produce O(n) warnings per render,
with no circuit-breaker behavior.

### Gap 3: Only String Warnings — No Structured Reporting

Every error path outputs via `Action<string>?`. There is no:
- `ExtensionLoadResult` type with structured fields (name, state, reason, processors)
- Machine-readable error classification
- Way to programmatically react to specific failure types

**Impact**: Tooling and CLI cannot present structured status without parsing warning strings.

### Gap 4: No Safe Loading Methods

`LoadExtension()` and `LoadExtensions()` return `AdocEngine` (fluent).
There are no `LoadExtensionSafe()` / `LoadExtensionsSafe()` variants that return
`IReadOnlyList<ExtensionLoadResult>`.

**Impact**: Callers who need to know what loaded (and what didn't) must wire up OnWarning
and parse the warning strings.

### Gap 5: No API Version Compatibility

There is `minAdocNetVersion` for runtime version, but no `apiVersion` concept:
- No `AdocEngine.ExtensionApiVersion` constant
- No manifest `apiVersion` field
- No check for extension API compatibility vs. host API version

**Impact**: An extension built for a future API version will load and potentially
crash at runtime with confusing errors instead of a clear "incompatible API version" message.

### Gap 6: No CLI Extension Status Command

The CLI has `ext list`, `ext install`, `ext remove`, `ext info`, `ext search`.
There is no `ext status` command showing per-extension runtime state.

**Impact**: Users have no visibility into which extensions are loaded, failed, or disabled
in a running engine instance.

---

## 9. Error Paths With NO Test Coverage

| Error Path | File | Status |
|-----------|------|--------|
| `ReflectionTypeLoadException` during GetExportedTypes | ExtensionLoader L55 | **No test** — hard to trigger without a corrupted assembly |
| `Activator.CreateInstance` throws | ExtensionLoader L86 | **No test** — TestExtension has NoCtorProcessor but that tests the no-ctor path, not ctor-throws |
| `FileNotFoundException` during Assembly.LoadFrom | ExtensionLoader L44 | **No test** — rare; file existed at check but disappeared before load |
| `File.ReadAllText` fails for extension.json | ExtensionManifest L68 | **No test** — requires I/O failure |
| `File.ReadAllText` fails for registry.json | ExtensionRegistry L43 | **No test** — requires I/O failure |
| `Save()` write failure | ExtensionRegistry L157 | **No test** — requires filesystem failure |
| ProcessingPipeline document processor throws | ProcessingPipeline L30 | **No test** |
| ProcessingPipeline block processor throws | ProcessingPipeline L68 | **No test** |
| ProcessingPipeline inline processor throws | ProcessingPipeline L167 | **No test** |
| Registry version mismatch | ExtensionRegistry L63 | **No test** |
| Registry stale detection | ExtensionRegistry L79 | **No test** (integration tests may cover indirectly) |

---

## 10. Failures Caught But Not Reported Structurally

All of the following produce string warnings only (no structured type):

1. **ExtensionLoader**: all 4 catch blocks + 4 conditional paths → 8 string warnings
2. **ProcessingPipeline**: 3 catch blocks → 3 string warnings per invocation
3. **ExtensionDirectoryLoader**: 3 conditional paths → 3 string warnings
4. **DependencyValidator**: 2 conditional paths → 2 string warnings
5. **ExtensionRegistry**: 5 catch/conditional paths → 5 string warnings
6. **ExtensionManifest**: 6 conditional paths + 1 catch → 7 string warnings

**Total: 28 distinct warning-producing paths, all string-only.**

None produce structured data. None return error codes, enum values, or typed results.

---

## 11. Summary — What Exists vs. What's Missing

| Capability | Exists in beta.8? | Beta.9 adds? |
|-----------|-------------------|--------------|
| Load-time error catching | Yes (4 catch blocks in ExtensionLoader) | ExtensionLoadResult |
| Runtime error catching | Yes (3 catch blocks in ProcessingPipeline) | Failure counting + disabling |
| Version compat checking | Yes (IsVersionCompatible) | API version field |
| Dependency validation | Yes (warn-only) | No change |
| Per-extension state | **No** | ExtensionState enum |
| Failure-based disabling | **No** | MaxProcessorFailures threshold |
| Structured load results | **No** | LoadExtensionSafe / LoadExtensionsSafe |
| CLI status command | **No** | ext status |
| API version constant | **No** | ExtensionApiVersion const |
