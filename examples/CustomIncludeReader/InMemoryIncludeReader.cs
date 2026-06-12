using AdocNet;

namespace CustomIncludeReader;

/// <summary>
/// An <see cref="IIncludeReader"/> that resolves include targets from an in-memory dictionary.
/// Useful for testing, embedded documentation, or scenarios where content is not on disk.
/// </summary>
public sealed class InMemoryIncludeReader : IIncludeReader
{
    private readonly Dictionary<string, string> _files;

    public InMemoryIncludeReader(Dictionary<string, string> files)
    {
        _files = files;
    }

    public bool Exists(string path)
        => _files.ContainsKey(Path.GetFileName(path));

    public string Read(string path)
        => _files[Path.GetFileName(path)];
}
