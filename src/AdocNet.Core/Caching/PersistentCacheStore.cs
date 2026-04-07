using System.Text;

namespace AdocNet.Caching;

/// <summary>
/// Disk-based cache store for render output. One file per cache entry.
/// Uses atomic writes (temp file + rename) to prevent corruption.
/// Thread-safe via lock — single-user tool, no cross-process locking.
/// </summary>
internal sealed class PersistentCacheStore
{
    private static readonly byte[] Magic = { 0x41, 0x44, 0x43, 0x00 }; // "ADC\0"
    private const uint FormatVersion = 1;
    private const string SubDir = "v1";

    private readonly object _lock = new();
    private readonly string _cacheDir;
    private readonly string _engineVersion;
    private readonly int _maxEntries;

    /// <summary>
    /// Initializes a persistent cache store.
    /// </summary>
    /// <param name="cacheDirectory">Root cache directory (e.g. ~/.adocnet/cache/).</param>
    /// <param name="engineVersion">Current AdocNet version for invalidation.</param>
    /// <param name="maxEntries">Maximum number of cache files. Oldest evicted when exceeded.</param>
    public PersistentCacheStore(string cacheDirectory, string engineVersion, int maxEntries)
    {
        _cacheDir = Path.Combine(cacheDirectory, SubDir);
        _engineVersion = engineVersion ?? "";
        _maxEntries = maxEntries > 0 ? maxEntries : 256;
    }

    /// <summary>
    /// Attempts to load a cached render result from disk.
    /// Returns false if the file is missing, corrupt, or has a version mismatch.
    /// </summary>
    public bool TryLoad(string key, out byte[] value)
    {
        value = Array.Empty<byte>();
        var path = GetEntryPath(key);

        lock (_lock)
        {
            if (!File.Exists(path))
                return false;

            try
            {
                var data = File.ReadAllBytes(path);
                if (!TryParseEntry(data, out var storedVersion, out var content))
                    return false;

                if (storedVersion != _engineVersion)
                {
                    TryDeleteFile(path);
                    return false;
                }

                value = content;
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Saves a render result to disk using atomic write (temp file + rename).
    /// Evicts oldest entries if the cache exceeds <see cref="_maxEntries"/>.
    /// </summary>
    public void Save(string key, byte[] value)
    {
        lock (_lock)
        {
            EnsureDirectory();

            var path = GetEntryPath(key);
            var tempPath = path + ".tmp";

            try
            {
                var data = BuildEntry(value);
                File.WriteAllBytes(tempPath, data);
                MoveFile(tempPath, path);
                EvictExcess();
            }
            catch (IOException)
            {
                TryDeleteFile(tempPath);
            }
        }
    }

    /// <summary>
    /// Deletes all cached files from the persistent cache directory.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            if (!Directory.Exists(_cacheDir))
                return;

            try
            {
                var files = Directory.GetFiles(_cacheDir, "*.bin");
                foreach (var file in files)
                    TryDeleteFile(file);

                // Also clean up any leftover temp files
                var temps = Directory.GetFiles(_cacheDir, "*.tmp");
                foreach (var temp in temps)
                    TryDeleteFile(temp);
            }
            catch (IOException)
            {
                // Best-effort cleanup
            }
        }
    }

    private byte[] BuildEntry(byte[] content)
    {
        var versionBytes = Encoding.UTF8.GetBytes(_engineVersion);
        var total = Magic.Length + 4 + 2 + versionBytes.Length + content.Length;
        var result = new byte[total];
        var offset = 0;

        // Magic
        Array.Copy(Magic, 0, result, offset, Magic.Length);
        offset += Magic.Length;

        // Format version (uint32 LE)
        result[offset++] = (byte)(FormatVersion & 0xFF);
        result[offset++] = (byte)((FormatVersion >> 8) & 0xFF);
        result[offset++] = (byte)((FormatVersion >> 16) & 0xFF);
        result[offset++] = (byte)((FormatVersion >> 24) & 0xFF);

        // Version string length (uint16 LE)
        var vLen = (ushort)versionBytes.Length;
        result[offset++] = (byte)(vLen & 0xFF);
        result[offset++] = (byte)((vLen >> 8) & 0xFF);

        // Version string
        Array.Copy(versionBytes, 0, result, offset, versionBytes.Length);
        offset += versionBytes.Length;

        // Content
        Array.Copy(content, 0, result, offset, content.Length);

        return result;
    }

    private static bool TryParseEntry(byte[] data, out string version, out byte[] content)
    {
        version = "";
        content = Array.Empty<byte>();

        // Minimum size: 4 (magic) + 4 (format) + 2 (version len) = 10
        if (data.Length < 10)
            return false;

        // Check magic
        for (int i = 0; i < Magic.Length; i++)
        {
            if (data[i] != Magic[i])
                return false;
        }

        // Check format version
        var fmt = (uint)(data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24));
        if (fmt != FormatVersion)
            return false;

        // Read version string
        var vLen = (ushort)(data[8] | (data[9] << 8));
        if (data.Length < 10 + vLen)
            return false;

        version = Encoding.UTF8.GetString(data, 10, vLen);

        // Remaining bytes are content
        var contentStart = 10 + vLen;
        content = new byte[data.Length - contentStart];
        Array.Copy(data, contentStart, content, 0, content.Length);

        return true;
    }

    private void EvictExcess()
    {
        try
        {
            var files = new DirectoryInfo(_cacheDir).GetFiles("*.bin");
            if (files.Length <= _maxEntries)
                return;

            // Sort by last write time, oldest first
            Array.Sort(files, (a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));

            var toDelete = files.Length - _maxEntries;
            for (int i = 0; i < toDelete; i++)
                TryDeleteFile(files[i].FullName);
        }
        catch (IOException)
        {
            // Best-effort eviction
        }
    }

    private string GetEntryPath(string key)
    {
        return Path.Combine(_cacheDir, key + ".bin");
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_cacheDir))
            Directory.CreateDirectory(_cacheDir);
    }

    private static void MoveFile(string source, string destination)
    {
        // Delete destination first if it exists (File.Move doesn't overwrite on all platforms)
        TryDeleteFile(destination);
        File.Move(source, destination);
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
    }
}
