namespace AdocNet.Extensions;

/// <summary>
/// Structured result of an extension load attempt. Returned by safe loading methods.
/// </summary>
public sealed class ExtensionLoadResult
{
    /// <summary>Gets the extension name (from IExtension.Name, type name, or assembly name).</summary>
    public string Name { get; }

    /// <summary>Gets the state after the load attempt.</summary>
    public ExtensionState State { get; }

    /// <summary>Gets the failure reason, or null if the extension loaded successfully.</summary>
    public string? FailureReason { get; }

    /// <summary>Gets the list of processor instances loaded from this extension.</summary>
    public IReadOnlyList<object> Processors { get; }

    /// <summary>
    /// Initializes a new <see cref="ExtensionLoadResult"/>.
    /// </summary>
    /// <param name="name">Extension name.</param>
    /// <param name="state">Load result state.</param>
    /// <param name="failureReason">Failure reason, or null on success.</param>
    /// <param name="processors">Loaded processor instances, or null/empty on failure.</param>
    public ExtensionLoadResult(string name, ExtensionState state, string? failureReason,
        IReadOnlyList<object>? processors)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        State = state;
        FailureReason = failureReason;
        Processors = processors ?? Array.Empty<object>();
    }
}
