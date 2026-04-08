namespace AdocNet.Extensions;

/// <summary>
/// Represents the outcome of a single validation check.
/// </summary>
public enum ValidationStatus
{
    /// <summary>The check passed.</summary>
    Pass,

    /// <summary>The check failed (blocks extension use).</summary>
    Fail,

    /// <summary>The check produced a warning (non-blocking).</summary>
    Warn,

    /// <summary>The check was skipped (not applicable).</summary>
    Skip
}

/// <summary>
/// Represents the result of a single validation check performed by <see cref="ExtensionValidator"/>.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>Gets the outcome of the check.</summary>
    public ValidationStatus Status { get; }

    /// <summary>Gets the name of the check (e.g., "Manifest", "Entry DLL").</summary>
    public string CheckName { get; }

    /// <summary>Gets a human-readable message describing the result.</summary>
    public string Message { get; }

    /// <summary>Creates a new validation result.</summary>
    public ValidationResult(ValidationStatus status, string checkName, string message)
    {
        Status = status;
        CheckName = checkName ?? throw new ArgumentNullException(nameof(checkName));
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }
}
