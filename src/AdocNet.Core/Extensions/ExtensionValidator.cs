namespace AdocNet.Extensions;

/// <summary>
/// Validates an extension directory for correctness before publishing or installation.
/// Checks manifest validity, DLL loading, processor discovery, version compatibility,
/// dependency satisfaction, and signing verification.
/// </summary>
public sealed class ExtensionValidator
{
    private readonly ExtensionRegistry? _registry;

    /// <summary>
    /// Creates a validator that checks dependencies against the given registry.
    /// If registry is null, dependency checks are skipped.
    /// </summary>
    public ExtensionValidator(ExtensionRegistry? registry = null)
    {
        _registry = registry;
    }

    /// <summary>
    /// Validates the extension at the specified directory path.
    /// Returns a list of check results in order of execution.
    /// </summary>
    public IReadOnlyList<ValidationResult> Validate(string extensionPath)
    {
        if (extensionPath is null)
            throw new ArgumentNullException(nameof(extensionPath));

        var results = new List<ValidationResult>();
        var fullPath = Path.GetFullPath(extensionPath);

        // Check 1: extension.json exists and is valid
        var manifest = ValidateManifest(fullPath, results);
        if (manifest is null)
            return results; // Can't continue without a valid manifest

        // Check 2: Required fields present
        ValidateRequiredFields(manifest, results);

        // Check 3: Entry DLL exists
        var entryPath = Path.Combine(fullPath, manifest.Entry);
        if (!ValidateEntryDll(entryPath, manifest.Entry, results))
            return results; // Can't continue without the DLL

        // Check 4 & 5: DLL loads and has processors
        ValidateDllLoading(entryPath, results);

        // Check 6: API version compatible
        ValidateApiVersion(manifest, results);

        // Check 7: minAdocNetVersion compatible
        ValidateMinVersion(manifest, results);

        // Check 8: maxAdocNetVersion compatible
        ValidateMaxVersion(manifest, results);

        // Check 9: Dependencies satisfiable
        ValidateDependencies(manifest, results);

        // Check 10: Public key token matches
        ValidateSigning(entryPath, manifest, results);

        return results;
    }

    private static ExtensionManifest? ValidateManifest(
        string extensionPath, List<ValidationResult> results)
    {
        var manifestPath = Path.Combine(extensionPath, "extension.json");
        if (!File.Exists(manifestPath))
        {
            results.Add(new ValidationResult(ValidationStatus.Fail,
                "Manifest", "extension.json not found"));
            return null;
        }

        string json;
        try
        {
            json = File.ReadAllText(manifestPath);
        }
        catch (Exception ex)
        {
            results.Add(new ValidationResult(ValidationStatus.Fail,
                "Manifest", $"Failed to read extension.json: {ex.Message}"));
            return null;
        }

        var warnings = new List<string>();
        var manifest = ExtensionManifest.Parse(json, extensionPath, msg => warnings.Add(msg));

        if (manifest is null)
        {
            var reason = warnings.Count > 0 ? warnings[0] : "Invalid JSON";
            results.Add(new ValidationResult(ValidationStatus.Fail,
                "Manifest", $"Invalid extension.json: {reason}"));
            return null;
        }

        results.Add(new ValidationResult(ValidationStatus.Pass,
            "Manifest", "extension.json is valid"));
        return manifest;
    }

    private static void ValidateRequiredFields(
        ExtensionManifest manifest, List<ValidationResult> results)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(manifest.Name)) missing.Add("name");
        if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Version == "0.0.0")
            missing.Add("version");
        if (string.IsNullOrWhiteSpace(manifest.Entry)) missing.Add("entry");

        if (missing.Count > 0)
        {
            results.Add(new ValidationResult(ValidationStatus.Fail,
                "Required fields", $"Missing: {string.Join(", ", missing)}"));
        }
        else
        {
            results.Add(new ValidationResult(ValidationStatus.Pass,
                "Required fields",
                $"name={manifest.Name}, version={manifest.Version}, entry={manifest.Entry}"));
        }
    }

    private static bool ValidateEntryDll(
        string entryPath, string entryName, List<ValidationResult> results)
    {
        if (File.Exists(entryPath))
        {
            results.Add(new ValidationResult(ValidationStatus.Pass,
                "Entry DLL", $"{entryName} exists"));
            return true;
        }

        results.Add(new ValidationResult(ValidationStatus.Fail,
            "Entry DLL", $"{entryName} not found"));
        return false;
    }

    private static void ValidateDllLoading(
        string entryPath, List<ValidationResult> results)
    {
        var loadWarnings = new List<string>();
        List<object> processors;
        try
        {
            processors = ExtensionLoader.LoadAssembly(entryPath, msg => loadWarnings.Add(msg));
        }
        catch (Exception ex)
        {
            results.Add(new ValidationResult(ValidationStatus.Fail,
                "DLL loading", $"Failed to load: {ex.Message}"));
            results.Add(new ValidationResult(ValidationStatus.Fail,
                "Processors", "Cannot check (DLL failed to load)"));
            return;
        }

        if (loadWarnings.Count > 0 && processors.Count == 0)
        {
            results.Add(new ValidationResult(ValidationStatus.Fail,
                "DLL loading", loadWarnings[0]));
        }
        else
        {
            results.Add(new ValidationResult(ValidationStatus.Pass,
                "DLL loading", "Assembly loaded successfully"));
        }

        if (processors.Count > 0)
        {
            var names = new List<string>();
            foreach (var p in processors)
                names.Add(p.GetType().Name);
            results.Add(new ValidationResult(ValidationStatus.Pass,
                "Processors", $"{processors.Count} found: {string.Join(", ", names)}"));
        }
        else
        {
            results.Add(new ValidationResult(ValidationStatus.Fail,
                "Processors", "No processor types found (IDocumentProcessor, IBlockProcessor, or IInlineProcessor)"));
        }
    }

    private static void ValidateApiVersion(
        ExtensionManifest manifest, List<ValidationResult> results)
    {
        if (manifest.ApiVersion is null)
        {
            results.Add(new ValidationResult(ValidationStatus.Skip,
                "API version", "Not specified"));
            return;
        }

        var hostApi = AdocEngine.ExtensionApiVersion;
        if (ExtensionDirectoryLoader.IsApiVersionCompatible(hostApi, manifest.ApiVersion))
        {
            results.Add(new ValidationResult(ValidationStatus.Pass,
                "API version", $"{manifest.ApiVersion} compatible with host {hostApi}"));
        }
        else
        {
            results.Add(new ValidationResult(ValidationStatus.Fail,
                "API version", $"{manifest.ApiVersion} incompatible with host {hostApi}"));
        }
    }

    private static void ValidateMinVersion(
        ExtensionManifest manifest, List<ValidationResult> results)
    {
        if (manifest.MinAdocNetVersion is null)
        {
            results.Add(new ValidationResult(ValidationStatus.Skip,
                "minAdocNetVersion", "Not specified"));
            return;
        }

        var current = ExtensionDirectoryLoader.GetCurrentAdocNetVersion();
        if (ExtensionDirectoryLoader.IsVersionCompatible(current, manifest.MinAdocNetVersion))
        {
            results.Add(new ValidationResult(ValidationStatus.Pass,
                "minAdocNetVersion",
                $"{manifest.MinAdocNetVersion} satisfied (current: {current})"));
        }
        else
        {
            results.Add(new ValidationResult(ValidationStatus.Fail,
                "minAdocNetVersion",
                $"Requires >= {manifest.MinAdocNetVersion} (current: {current})"));
        }
    }

    private static void ValidateMaxVersion(
        ExtensionManifest manifest, List<ValidationResult> results)
    {
        if (manifest.MaxAdocNetVersion is null)
        {
            results.Add(new ValidationResult(ValidationStatus.Skip,
                "maxAdocNetVersion", "Not specified"));
            return;
        }

        var current = ExtensionDirectoryLoader.GetCurrentAdocNetVersion();
        if (ExtensionDirectoryLoader.IsVersionCompatible(manifest.MaxAdocNetVersion, current))
        {
            results.Add(new ValidationResult(ValidationStatus.Pass,
                "maxAdocNetVersion",
                $"{manifest.MaxAdocNetVersion} satisfied (current: {current})"));
        }
        else
        {
            results.Add(new ValidationResult(ValidationStatus.Fail,
                "maxAdocNetVersion",
                $"Requires <= {manifest.MaxAdocNetVersion} (current: {current})"));
        }
    }

    private void ValidateDependencies(
        ExtensionManifest manifest, List<ValidationResult> results)
    {
        if (manifest.Dependencies.Count == 0)
        {
            results.Add(new ValidationResult(ValidationStatus.Skip,
                "Dependencies", "None declared"));
            return;
        }

        if (_registry is null)
        {
            results.Add(new ValidationResult(ValidationStatus.Skip,
                "Dependencies", "No registry available for validation"));
            return;
        }

        var missing = new List<string>();
        var satisfied = new List<string>();

        foreach (var dep in manifest.Dependencies)
        {
            var spec = DependencySpec.Parse(dep);
            if (spec is null) continue;

            var installed = _registry.Find(spec.Name);
            if (installed is null)
            {
                missing.Add(dep);
            }
            else if (spec.MinVersion is not null &&
                     !ExtensionDirectoryLoader.IsVersionCompatible(installed.Version, spec.MinVersion))
            {
                missing.Add($"{dep} (installed: {installed.Version})");
            }
            else
            {
                satisfied.Add(spec.Name);
            }
        }

        if (missing.Count > 0)
        {
            results.Add(new ValidationResult(ValidationStatus.Warn,
                "Dependencies",
                $"{missing.Count} unsatisfied: {string.Join(", ", missing)}"));
        }
        else
        {
            results.Add(new ValidationResult(ValidationStatus.Pass,
                "Dependencies",
                $"All {satisfied.Count} satisfied"));
        }
    }

    private static void ValidateSigning(
        string entryPath, ExtensionManifest manifest, List<ValidationResult> results)
    {
        if (manifest.PublicKeyToken is null)
        {
            results.Add(new ValidationResult(ValidationStatus.Skip,
                "Signing", "publicKeyToken not specified"));
            return;
        }

        try
        {
            var assemblyName = System.Reflection.AssemblyName.GetAssemblyName(entryPath);
            var actualBytes = assemblyName.GetPublicKeyToken();
            var actualToken = SigningHelper.ToHexString(actualBytes);

            if (actualToken.Length == 0)
            {
                results.Add(new ValidationResult(ValidationStatus.Fail,
                    "Signing",
                    $"DLL is unsigned but manifest expects token '{manifest.PublicKeyToken}'"));
                return;
            }

            if (string.Equals(actualToken, manifest.PublicKeyToken, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new ValidationResult(ValidationStatus.Pass,
                    "Signing", $"Token matches: {actualToken}"));
            }
            else
            {
                results.Add(new ValidationResult(ValidationStatus.Fail,
                    "Signing",
                    $"Token mismatch: expected '{manifest.PublicKeyToken}', got '{actualToken}'"));
            }
        }
        catch (Exception ex)
        {
            results.Add(new ValidationResult(ValidationStatus.Fail,
                "Signing", $"Failed to read token: {ex.Message}"));
        }
    }
}
