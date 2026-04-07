namespace AdocNet.Extensions;

/// <summary>
/// Transforms rendered output after the renderer has completed.
/// Use cases: HTML minification, watermarking, custom post-processing.
/// Registered via <c>AdocEngine.RegisterOutputProcessor()</c>.
/// </summary>
public interface IOutputProcessor
{
    /// <summary>
    /// Transforms the rendered output bytes.
    /// </summary>
    /// <param name="renderedOutput">The rendered output bytes from the renderer (or previous processor).</param>
    /// <param name="format">The renderer format string (e.g., "html", "pdf").</param>
    /// <returns>The transformed output bytes.</returns>
    byte[] Process(byte[] renderedOutput, string format);
}
