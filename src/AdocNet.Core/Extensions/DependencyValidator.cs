namespace AdocNet.Extensions;

/// <summary>
/// Validates extension dependencies against the registry.
/// Produces warnings for missing or incompatible dependencies but never blocks loading.
/// </summary>
public static class DependencyValidator
{
    /// <summary>
    /// Validates that all dependencies of the specified extension are satisfied
    /// by extensions currently in the registry. Invokes <paramref name="onWarning"/>
    /// for each missing or incompatible dependency.
    /// </summary>
    /// <param name="extension">The extension whose dependencies to check.</param>
    /// <param name="registry">The registry to check against.</param>
    /// <param name="onWarning">Callback for dependency warnings.</param>
    public static void Validate(ExtensionInfo extension, ExtensionRegistry registry, Action<string>? onWarning)
    {
        if (extension is null)
            throw new ArgumentNullException(nameof(extension));
        if (registry is null)
            throw new ArgumentNullException(nameof(registry));

        foreach (var depString in extension.Dependencies)
        {
            var spec = DependencySpec.Parse(depString);
            if (spec is null)
                continue;

            var installed = registry.Find(spec.Name);

            if (installed is null)
            {
                onWarning?.Invoke(
                    $"Extension '{extension.Name}' depends on '{depString}' which is not installed.");
                continue;
            }

            if (spec.MinVersion is not null &&
                !ExtensionDirectoryLoader.IsVersionCompatible(installed.Version, spec.MinVersion))
            {
                onWarning?.Invoke(
                    $"Extension '{extension.Name}' depends on '{depString}', " +
                    $"but installed version is {installed.Version}.");
            }
        }
    }
}
