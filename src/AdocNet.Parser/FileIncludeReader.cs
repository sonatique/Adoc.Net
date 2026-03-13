using AdocNet;

namespace AdocNet.Parser;

/// <summary>
/// Default <see cref="IIncludeReader"/> that reads files from the local filesystem.
/// </summary>
public sealed class FileIncludeReader : IIncludeReader
{
    public static FileIncludeReader Instance { get; } = new();

    public bool Exists(string path) => File.Exists(path);

    public string Read(string path) => File.ReadAllText(path);
}
