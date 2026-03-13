namespace AdocNet;

/// <summary>
/// Abstraction for reading include target files.
/// Implement to resolve includes from custom sources (databases, HTTP, embedded resources).
/// The default implementation (<c>FileIncludeReader</c>) reads from the local filesystem.
/// </summary>
public interface IIncludeReader
{
    /// <summary>Returns true when the file at <paramref name="path"/> exists and is readable.</summary>
    bool Exists(string path);

    /// <summary>Reads the full text content of the file at <paramref name="path"/>.</summary>
    string Read(string path);
}
