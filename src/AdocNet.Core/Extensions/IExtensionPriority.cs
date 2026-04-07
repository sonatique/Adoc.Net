namespace AdocNet.Extensions;

/// <summary>
/// Optional interface for processors to declare their execution priority.
/// Lower values execute first. Default priority (no interface) is 1000.
/// Within the same priority, registration order (FIFO) is preserved.
/// </summary>
public interface IExtensionPriority
{
    /// <summary>
    /// Execution priority. Lower values execute first.
    /// Typical ranges: 0-100 (early), 500 (normal), 900-1000 (late).
    /// Default for processors not implementing this interface: 1000.
    /// </summary>
    int Priority { get; }
}
