using System.IO.Compression;
using System.Text;

namespace AdocNet.Converters.Epub;

/// <summary>
/// The entries of an EPUB file, written as a ZIP archive in the order they were added.
/// <para>
/// To a seekable stream the archive is assembled from per-entry pieces, each the local header
/// with data and the central directory record that <see cref="ZipArchive"/> writes for that entry.
/// The bundled assets are the same in every EPUB, so their pieces are compressed once per
/// process. The result is the archive <see cref="ZipArchive"/> writes for these entries; anything
/// the assembly does not handle (a non-seekable stream, ZIP64) is written by
/// <see cref="ZipArchive"/> directly.
/// </para>
/// </summary>
internal sealed class EpubArchive
{
    private static readonly DateTimeOffset DeterministicTimestamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>An entry: its data, or an embedded resource followed by optional extra bytes.</summary>
    private sealed record Entry(string Path, CompressionLevel Level, byte[]? Data, string? ResourceName, byte[]? Appended);

    /// <summary>The bytes <see cref="ZipArchive"/> writes for one entry.</summary>
    private sealed record Piece(byte[] Local, byte[] Central);

    private readonly List<Entry> _entries = [];

    internal void AddStored(string path, byte[] data) => _entries.Add(new Entry(path, CompressionLevel.NoCompression, data, null, null));

    internal void Add(string path, string content) =>
        _entries.Add(new Entry(path, CompressionLevel.Optimal, Encoding.UTF8.GetBytes(content), null, null));

    internal void Add(string path, byte[] data) => _entries.Add(new Entry(path, CompressionLevel.Optimal, data, null, null));

    /// <summary>Adds an embedded resource; <paramref name="appended"/> is written after its content.</summary>
    internal void AddResource(string path, string resourceName, byte[]? appended = null) =>
        _entries.Add(new Entry(path, CompressionLevel.Optimal, null, resourceName, appended));

    internal void WriteTo(Stream output)
    {
        if (output.CanSeek && TryAssemble(output.Position) is { } pieces)
        {
            // Offsets are stream positions, as ZipArchive records them.
            long localOffset = output.Position;
            foreach (var piece in pieces)
                output.Write(piece.Local, 0, piece.Local.Length);
            long centralOffset = output.Position, centralSize = 0;
            foreach (var piece in pieces)
            {
                var record = (byte[])piece.Central.Clone();
                WriteUInt32(record, 42, (uint)localOffset);
                output.Write(record, 0, record.Length);
                localOffset += piece.Local.Length;
                centralSize += record.Length;
            }
            var end = new byte[22];
            WriteUInt32(end, 0, 0x06054b50);
            WriteUInt16(end, 8, (ushort)pieces.Count);
            WriteUInt16(end, 10, (ushort)pieces.Count);
            WriteUInt32(end, 12, (uint)centralSize);
            WriteUInt32(end, 16, (uint)centralOffset);
            output.Write(end, 0, end.Length);
            return;
        }

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var entry in _entries)
            WriteEntry(archive, entry);
    }

    /// <summary>
    /// The pieces of every entry, or null when an archive starting at <paramref name="start"/> would
    /// need ZIP64 or a piece has a layout this assembly does not reproduce.
    /// </summary>
    private List<Piece>? TryAssemble(long start)
    {
        var pieces = new List<Piece>(_entries.Count);
        var shared = SharedAssetPieces.Value;
        long size = start;
        foreach (var entry in _entries)
        {
            Piece? piece = entry is { ResourceName: { } name, Appended: null } && shared is not null
                && shared.TryGetValue((entry.Path, name), out var cached)
                ? cached
                : PieceOf(entry);
            if (piece is null) return null;
            pieces.Add(piece);
            size += piece.Local.Length + piece.Central.Length;
        }
        return pieces.Count < ushort.MaxValue && size + 22 < uint.MaxValue ? pieces : null;
    }

    private static Piece? PieceOf(Entry entry)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            WriteEntry(archive, entry);
        var pieces = Split(ms.ToArray());
        return pieces is { Count: 1 } ? pieces[0] : null;
    }

    private static readonly Lazy<Dictionary<(string Path, string Resource), Piece>?> SharedAssetPieces = new(() =>
    {
        var entries = EpubRenderer.BundledAssets.Select(a => new Entry(a.ArchivePath, CompressionLevel.Optimal, null, a.ResourceName, null)).ToList();
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var entry in entries)
                WriteEntry(archive, entry);
        var pieces = Split(ms.ToArray());
        if (pieces is null) return null;
        // A missing resource is skipped, so pieces follow the resources that exist.
        var present = entries.Where(e => typeof(EpubArchive).Assembly.GetManifestResourceInfo(e.ResourceName!) is not null).ToList();
        if (pieces.Count != present.Count) return null;
        var map = new Dictionary<(string, string), Piece>();
        for (int i = 0; i < present.Count; i++)
            map[(present[i].Path, present[i].ResourceName!)] = pieces[i];
        return map;
    });

    private static void WriteEntry(ZipArchive archive, Entry entry)
    {
        Stream? resource = null;
        if (entry.ResourceName is not null)
        {
            resource = typeof(EpubArchive).Assembly.GetManifestResourceStream(entry.ResourceName);
            if (resource is null) return; // a missing resource is skipped
        }
        using (resource)
        {
            var zipEntry = archive.CreateEntry(entry.Path, entry.Level);
            zipEntry.LastWriteTime = DeterministicTimestamp;
            using var stream = zipEntry.Open();
            if (resource is not null) resource.CopyTo(stream);
            if (entry.Data is { } data) stream.Write(data, 0, data.Length);
            if (entry.Appended is { } more) stream.Write(more, 0, more.Length);
        }
    }

    /// <summary>
    /// Splits an archive written by <see cref="ZipArchive"/> into its entries' pieces, or null when its
    /// layout is not the plain one: local entries back to back from offset 0, then the central
    /// directory, then an end record without comment, and no ZIP64.
    /// </summary>
    private static List<Piece>? Split(byte[] zip)
    {
        int end = zip.Length - 22;
        if (end < 0 || ReadUInt32(zip, end) != 0x06054b50 || ReadUInt16(zip, end + 20) != 0) return null;
        int count = ReadUInt16(zip, end + 10);
        long centralSize = ReadUInt32(zip, end + 12), centralOffset = ReadUInt32(zip, end + 16);
        if (centralOffset + centralSize != end || ReadUInt16(zip, end + 8) != count) return null;

        var records = new List<(int Start, int Length, long Local)>(count);
        int pos = (int)centralOffset;
        for (int i = 0; i < count; i++)
        {
            if (pos + 46 > end || ReadUInt32(zip, pos) != 0x02014b50) return null;
            int length = 46 + ReadUInt16(zip, pos + 28) + ReadUInt16(zip, pos + 30) + ReadUInt16(zip, pos + 32);
            if (ReadUInt32(zip, pos + 20) == uint.MaxValue || ReadUInt32(zip, pos + 24) == uint.MaxValue) return null;
            records.Add((pos, length, ReadUInt32(zip, pos + 42)));
            pos += length;
        }
        if (pos != end) return null;

        var pieces = new List<Piece>(count);
        long expectedLocal = 0;
        for (int i = 0; i < count; i++)
        {
            var (start, length, local) = records[i];
            long next = i + 1 < count ? records[i + 1].Local : centralOffset;
            if (local != expectedLocal || next <= local) return null;
            var localBytes = new byte[next - local];
            Array.Copy(zip, local, localBytes, 0, localBytes.Length);
            var central = new byte[length];
            Array.Copy(zip, start, central, 0, length);
            WriteUInt32(central, 42, 0);
            pieces.Add(new Piece(localBytes, central));
            expectedLocal = next;
        }
        return pieces;
    }

    private static ushort ReadUInt16(byte[] b, int i) => (ushort)(b[i] | b[i + 1] << 8);

    private static uint ReadUInt32(byte[] b, int i) => (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24);

    private static void WriteUInt16(byte[] b, int i, ushort v)
    {
        b[i] = (byte)v;
        b[i + 1] = (byte)(v >> 8);
    }

    private static void WriteUInt32(byte[] b, int i, uint v)
    {
        b[i] = (byte)v;
        b[i + 1] = (byte)(v >> 8);
        b[i + 2] = (byte)(v >> 16);
        b[i + 3] = (byte)(v >> 24);
    }
}
