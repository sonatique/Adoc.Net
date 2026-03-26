namespace AdocNet.Extensions;

/// <summary>
/// Abstracts invocation of an external diagram tool (PlantUML, Mermaid, etc.).
/// Implementations invoke the tool and return the path to the generated image.
/// </summary>
public interface IDiagramToolRunner
{
    /// <summary>
    /// Returns true if the tool is available on this system.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Generates an image from diagram source text.
    /// </summary>
    /// <param name="language">The diagram language (e.g., "plantuml").</param>
    /// <param name="source">The diagram source text.</param>
    /// <param name="outputDirectory">Directory to write the generated image.</param>
    /// <returns>The path to the generated image file, or null if generation failed.</returns>
    string? Generate(string language, string source, string outputDirectory);
}
