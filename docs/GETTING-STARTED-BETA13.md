# AdocNet v1.0.0-beta.13 — Getting Started

## Prerequisites

- Beta.12 must be merged and stable
- Create a branch: `git checkout -b beta13`

## Setup

Unzip at your AdocNet project root.

## What Beta.13 Adds — Three Themes

**No backward compatibility constraint.** No users, no forks, no watchers.

### Theme A — bool Process() Return (API Improvement)
- All 3 processor interfaces change from `void Process()` to `bool Process()`
- `true` = "I handled this node, skip remaining processors for this node"
- `false` = "continue to next processor" (preserves current behavior)
- IDocumentProcessor also gains RenderContext parameter (consistency fix)
- All existing processors updated to return `false` (no behavior change)

### Theme B — AssemblyLoadContext Isolation
- Each extension DLL loads in its own `AssemblyLoadContext` on net6.0+
- Prevents version conflicts between extensions
- Collectible contexts enable unloading
- `Assembly.LoadFrom` fallback on netstandard2.0

### Theme C — Hot-Reloading
- `AdocEngine.EnableHotReload` — watch extension directories for DLL changes
- On change: unload old context, load new DLL, re-register, clear cache
- FileSystemWatcher with 500ms debounce
- Requires net6.0+ (PlatformNotSupportedException on ns2.0)

## Phase Sequence (matches actual command files)

```
/b13-p00         Context Discovery           (8 criteria)
/b13-p01         Design Document             (10 criteria)
/b13-p02         bool Process() + Migration  (12 criteria) <- HIGH, many files
/b13-p03         AssemblyLoadContext          (11 criteria) <- HIGH, conditional compilation
/b13-p04         Hot-Reloading               (11 criteria) <- HIGH, FileSystemWatcher
/b13-reflect     Self-Reflection             <- recommended after P04
/b13-check-a     System Integrity            (13 criteria) <- GATE
/b13-p05         Documentation               (8 criteria)
/b13-check-c     Final Validation            (26 criteria) <- GATE
```

## Tips

- P02 is the most impactful: changes 3 interfaces + all implementors + pipeline + tests
- P02 Step 6 (fix all tests) will be the longest single step — many mock processors to update
- P03 uses `#if NET6_0_OR_GREATER` — verify both TFMs build
- P04's FileSystemWatcher needs debounce — DLL writes aren't atomic
- Use `/b13-reflect` after P04 to verify no `void Process` remains anywhere
