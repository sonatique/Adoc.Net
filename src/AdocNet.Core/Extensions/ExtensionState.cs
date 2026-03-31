namespace AdocNet.Extensions;

/// <summary>
/// Describes the state of an extension after a load attempt.
/// </summary>
public enum ExtensionState
{
    /// <summary>Extension loaded successfully; all processors instantiated and registered.</summary>
    Loaded,

    /// <summary>Extension failed to load (bad assembly, missing constructor, instantiation error).</summary>
    Failed,

    /// <summary>Extension was disabled due to repeated runtime failures (exceeded MaxProcessorFailures).</summary>
    Disabled,

    /// <summary>Extension skipped because its required API version is incompatible with the host.</summary>
    Incompatible
}
