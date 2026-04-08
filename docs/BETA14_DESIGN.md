# Beta.14 Design — Ecosystem Readiness

## Overview

Three themes: dependency-ordered loading, extension signing verification,
and extension validation tool. All additive — no existing interfaces changed.

---

## Theme A — Dependency-Ordered Loading

## 1. DependencyResolver

New class: `src/AdocNet.Core/Extensions/DependencyResolver.cs`

**Algorithm**: Kahn's algorithm (BFS-based topological sort).

```
Input:  List<(string name, IReadOnlyList<string> dependencies)>
Output: List<string> — names in dependency order (dependencies first)
Throws: InvalidOperationException on cycle (message includes cycle path)
```

**Steps**:
1. Build adjacency list: for each extension, map name → list of dependents.
2. Compute in-degree for each node (number of dependencies).
3. Seed queue with all nodes having in-degree 0 (no dependencies).
4. BFS: dequeue node, add to result, decrement in-degree of all dependents.
   When a dependent reaches in-degree 0, enqueue it.
5. If result count < input count: cycle exists. Walk remaining nodes to find
   the cycle and include it in the exception message.

**Complexity**: O(V + E) where V = extension count, E = total dependency edges.

**Signature**:
```csharp
public static class DependencyResolver
{
    /// <summary>
    /// Returns extension names in dependency order (dependencies load first).
    /// Throws InvalidOperationException if a cycle is detected.
    /// </summary>
    public static IReadOnlyList<string> Resolve(
        IReadOnlyList<(string Name, IReadOnlyList<string> Dependencies)> extensions);
}
```

**Dependency name matching**: case-sensitive, exact match (consistent with
`ExtensionRegistry.Find()` which is case-insensitive — but dependency specs
in manifests use exact names).

## 2. Integration with ExtensionDirectoryLoader

Current flow in `LoadInstalledExtensions()`:
```
1. Get subdirs, sort alphabetically
2. For each subdir: load manifest, check enabled, check version, load DLL
```

New flow:
```
1. Get subdirs (any order)
2. For each subdir: load manifest, check enabled, check version compat
   → collect valid manifests (don't load DLLs yet)
3. Extract (name, dependencies) from each manifest
4. Call DependencyResolver.Resolve() to get load order
5. For each name in dependency order: load the DLL
6. If cycle detected: warn + fall back to alphabetical order (graceful degradation)
```

The key change: **two-pass approach**. First pass reads and validates manifests.
Second pass loads DLLs in dependency order.

**Fallback**: If `DependencyResolver.Resolve()` throws (cycle), log a warning
and fall back to alphabetical order. Extensions still load — just not in optimal order.
This preserves the existing "never crash" guarantee.

## 3. Edge Cases

| Case | Behavior |
|------|----------|
| Missing dependency (not installed) | Extension loads anyway. DependencyValidator emits warning. Resolver ignores unknown deps. |
| Self-dependency (A depends on A) | Treated as a cycle. Warning + fallback to alphabetical. |
| Optional dependency | Not supported in beta.14. All deps treated equally. |
| No dependencies | Extension has in-degree 0, loads early (stable sort within same tier). |
| Extension not in dependency graph | Added with in-degree 0, loads normally. |
| Duplicate extension names | Not possible — registry enforces unique names. |

**Resolver only sorts known extensions.** If A depends on B but B is not in the
input list (not installed, disabled, incompatible), the resolver treats A as having
no dependency on B. B is simply absent from the graph. DependencyValidator handles
the "B not installed" warning separately — the resolver doesn't duplicate that logic.

---

## Theme B — Extension Signing Verification

## 4. Manifest Field — publicKeyToken

New optional field in `extension.json`:
```json
{
  "name": "my-extension",
  "version": "1.0.0",
  "entry": "MyExtension.dll",
  "publicKeyToken": "b77a5c561934e089"
}
```

**Format**: 16-character lowercase hexadecimal string (representing 8 bytes).
This matches the output of `sn -T MyExtension.dll` and .NET's standard format.

**Changes to ExtensionManifest**:
- Add `string? PublicKeyToken` property
- Parse from `"publicKeyToken"` JSON field
- Validate format: must be exactly 16 hex chars if present, or null

## 5. Verification Flow

Location: `ExtensionDirectoryLoader.LoadInstalledExtensions()`, after loading
the assembly but before adding processors to the result list.

Actually, the verification should happen **before** `ExtensionLoader.LoadAssembly()`
is impractical — we need the loaded assembly to get the token. So:

**Flow** (in ExtensionDirectoryLoader or a new helper):
1. Load assembly via `ExtensionLoader.LoadAssembly()` (existing code)
2. If manifest has `publicKeyToken`:
   a. Get loaded assembly's token: `assembly.GetName().GetPublicKeyToken()`
   b. Convert to hex string
   c. Compare (case-insensitive) to manifest value
   d. Match → proceed, add processors to results
   e. Mismatch → skip with warning, set state to Incompatible

**Problem**: `ExtensionLoader.LoadAssembly()` returns `List<object>` — no access
to the assembly after the call. Two options:

**Option A**: Add signing check inside `ExtensionLoader.LoadAssembly()` — pass
manifest token as parameter. Breaks existing signature.

**Option B**: New method or overload that accepts a manifest token. Add
`LoadAssembly(string path, string? expectedToken, Action<string>? onWarning)`.
Original method calls new method with `null` token (no check).

**Decision**: Option B. New overload keeps backward compat. The check is:
```csharp
if (expectedToken is not null)
{
    var actualToken = assembly.GetName().GetPublicKeyToken();
    var actualHex = SigningHelper.ToHexString(actualToken);
    if (!string.Equals(actualHex, expectedToken, StringComparison.OrdinalIgnoreCase))
    {
        onWarning?.Invoke($"Token mismatch for {path}: expected {expectedToken}, got {actualHex}");
        return new List<object>(); // skip
    }
}
```

**Alternative approach**: Do the signing check in `ExtensionDirectoryLoader` by
loading the assembly name without loading the full assembly:
`AssemblyName.GetAssemblyName(entryPath).GetPublicKeyToken()`. This avoids loading
an untrusted DLL at all. Better security posture.

**Decision: Use `AssemblyName.GetAssemblyName()` approach.** Check token BEFORE
calling `ExtensionLoader.LoadAssembly()`. This way:
- No modification to `ExtensionLoader` signatures needed
- Untrusted DLLs never loaded
- Check happens in `ExtensionDirectoryLoader` alongside other pre-load checks

## 6. Hex Formatting Helper

New internal helper: `SigningHelper` in `src/AdocNet.Core/Extensions/SigningHelper.cs`.

```csharp
internal static class SigningHelper
{
    /// <summary>Converts a public key token byte array to a lowercase hex string.</summary>
    internal static string ToHexString(byte[]? token)
    {
        if (token is null || token.Length == 0)
            return "";
        // Manual hex conversion (no Span on ns2.0)
        var chars = new char[token.Length * 2];
        for (int i = 0; i < token.Length; i++)
        {
            chars[i * 2] = GetHexChar(token[i] >> 4);
            chars[i * 2 + 1] = GetHexChar(token[i] & 0xF);
        }
        return new string(chars);
    }

    /// <summary>Validates that a string is a valid 16-char hex public key token.</summary>
    internal static bool IsValidTokenFormat(string token)
    {
        if (token.Length != 16) return false;
        foreach (var c in token)
            if (!IsHexChar(c)) return false;
        return true;
    }

    private static char GetHexChar(int nibble) =>
        (char)(nibble < 10 ? '0' + nibble : 'a' + nibble - 10);

    private static bool IsHexChar(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
```

---

## Theme C — Extension Validation Tool

## 7. CLI `ext validate`

New command: `adocnet ext validate <extension-path>`

Accepts a path to an extension directory (not yet installed — for pre-publish checking).

**Output format**: one line per check, PASS/FAIL/WARN prefix, overall verdict at end.

```
Validating extension at: /path/to/my-extension

  [PASS] extension.json exists and is valid
  [PASS] Entry DLL exists: MyExtension.dll
  [PASS] 2 processor(s) found: MyBlockProcessor, MyInlineProcessor
  [PASS] API version 1.0 compatible with host 1.0
  [PASS] minAdocNetVersion 1.0.0-beta.7 satisfied (current: 1.0.0-beta.14)
  [WARN] Dependency 'crypto-utils >= 2.0' not installed in registry
  [PASS] publicKeyToken matches: b77a5c561934e089
  [SKIP] maxAdocNetVersion not specified

Overall: PASS (7 passed, 0 failed, 1 warning, 1 skipped)
```

**Exit codes**: 0 = all pass (warnings OK), 1 = any failure.

## 8. Validation Checks

| # | Check | PASS | FAIL | WARN | SKIP |
|---|-------|------|------|------|------|
| 1 | `extension.json` exists and parses | Valid manifest | Missing or invalid JSON | — | — |
| 2 | Entry DLL exists | File found | File missing | — | — |
| 3 | DLL loads and has processors | ≥1 processor type | Load fails or 0 types | — | — |
| 4 | API version compatible | Compatible or not specified | Incompatible | — | Not specified |
| 5 | minAdocNetVersion satisfied | Current >= min | Current < min | — | Not specified |
| 6 | maxAdocNetVersion satisfied | Current <= max | Current > max | — | Not specified |
| 7 | Dependencies satisfiable | All deps installed | — | Missing deps | No deps |
| 8 | publicKeyToken matches | Token matches | Token mismatch or unsigned | — | Not specified |

Checks 1-2 are fatal: if they fail, remaining checks are skipped (can't validate
without a manifest or DLL).

## 9. Validation Implementation

New class: `ExtensionValidator` in `src/AdocNet.Core/Extensions/ExtensionValidator.cs`.

```csharp
public sealed class ExtensionValidator
{
    public IReadOnlyList<ValidationResult> Validate(string extensionPath);
}

public sealed class ValidationResult
{
    public ValidationStatus Status { get; }   // Pass, Fail, Warn, Skip
    public string CheckName { get; }
    public string Message { get; }
}

public enum ValidationStatus { Pass, Fail, Warn, Skip }
```

Reuses:
- `ExtensionManifest.Load()` for check 1
- `File.Exists()` for check 2
- `ExtensionLoader.LoadAssembly()` for check 3
- `ExtensionDirectoryLoader.IsApiVersionCompatible()` for check 4
- `ExtensionDirectoryLoader.IsVersionCompatible()` for checks 5-6
- `ExtensionRegistry.Load()` + `DependencyValidator` for check 7
- `AssemblyName.GetAssemblyName()` + `SigningHelper` for check 8

CLI integration: `ExtensionCommands.ExecuteValidate(string path)` calls
`ExtensionValidator.Validate()`, formats output, returns exit code.

---

## Cross-Cutting Concerns

## 10. Testing Strategy

### DependencyResolver Tests
- Empty input → empty output
- Single extension, no deps → returns it
- Linear chain A→B→C → C, B, A order
- Diamond: A→B, A→C, B→D, C→D → D first, A last
- Cycle A→B→A → throws with cycle description
- Self-dependency → throws
- Missing dependency (not in input) → ignored, extension still in output
- Large graph (10+ extensions) → correct order

### SigningHelper Tests
- Null/empty token → empty string
- Known 8-byte token → correct hex string
- Valid format check: 16 hex chars → true
- Invalid: wrong length, non-hex chars → false

### ExtensionValidator Tests
- Valid extension directory → all PASS
- Missing extension.json → FAIL on check 1, rest skipped
- Missing entry DLL → FAIL on check 2
- No processors → FAIL on check 3
- Incompatible API version → FAIL
- Missing dependency → WARN
- Token mismatch → FAIL (requires signed test assembly or mock)

### Integration in ExtensionDirectoryLoader
- Extensions with deps loaded in correct order
- Cycle detected → warning + fallback to alphabetical
- Signed extension with matching token → loads
- Signed extension with wrong token → skipped with warning

## 11. Explicit Non-Goals

- **Remote registry**: no network access, no downloads
- **Certificate chains**: no X.509, no PKI infrastructure
- **NuGet-based signing**: no Authenticode, no NuGet signature verification
- **Transitive dependency resolution**: only direct dependencies considered by resolver
- **Dependency version negotiation**: no version ranges, no conflict resolution
- **Auto-update**: no checking for newer versions
- **Sandbox enforcement**: signing is identity check, not permission system
- **Assembly binding redirects**: no version unification across extensions

---

## File Plan

### New Files
| File | Description |
|------|-------------|
| `src/AdocNet.Core/Extensions/DependencyResolver.cs` | Topological sort (Kahn's) |
| `src/AdocNet.Core/Extensions/SigningHelper.cs` | Hex formatting + token validation |
| `src/AdocNet.Core/Extensions/ExtensionValidator.cs` | Validation checks |
| `src/AdocNet.Core/Extensions/ValidationResult.cs` | Result model + enum |
| `tests/AdocNet.Core.Tests/Extensions/DependencyResolverTests.cs` | Resolver tests |
| `tests/AdocNet.Core.Tests/Extensions/SigningHelperTests.cs` | Hex helper tests |
| `tests/AdocNet.Core.Tests/Extensions/ExtensionValidatorTests.cs` | Validator tests |

### Modified Files
| File | Change |
|------|--------|
| `src/AdocNet.Core/Extensions/ExtensionManifest.cs` | Add `PublicKeyToken` property + parsing |
| `src/AdocNet.Core/Extensions/ExtensionDirectoryLoader.cs` | Two-pass loading + signing check |
| `src/AdocNet.Cli/ExtensionCommands.cs` | Add `ext validate` subcommand |
| `src/AdocNet.Cli/CliArgs.cs` | Add `ExtValidate` case |

## Implementation Order

1. **P02**: DependencyResolver + integration in ExtensionDirectoryLoader
2. **P03**: SigningHelper + ExtensionManifest.PublicKeyToken + signing check in loader
3. **P04**: ExtensionValidator + CLI `ext validate`
4. **P05**: Documentation
