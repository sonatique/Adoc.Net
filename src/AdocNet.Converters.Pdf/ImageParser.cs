using System.IO.Compression;

namespace AdocNet.Converters.Pdf;

/// <summary>
/// Minimal image header parser for JPEG and PNG files. Pure managed — no external dependencies.
/// Extracts dimensions, color info, and prepares pixel data for PDF embedding.
/// </summary>
internal static class ImageParser
{
    internal readonly record struct ImageInfo(
        int Width, int Height, int Components, int BitsPerComponent,
        ImageFormat Format, byte[] Data, byte[]? AlphaData);

    internal enum ImageFormat { Jpeg, Png }

    // ── JPEG ────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a JPEG file to extract dimensions and component count.
    /// Returns the entire JPEG as <see cref="ImageInfo.Data"/> for DCTDecode embedding.
    /// </summary>
    internal static ImageInfo? TryParseJpeg(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            return null;

        int offset = 2;
        while (offset + 4 < data.Length)
        {
            if (data[offset] != 0xFF)
                return null;

            byte marker = data[offset + 1];

            // Skip fill bytes
            if (marker == 0xFF)
            {
                offset++;
                continue;
            }

            // SOF0 (baseline) or SOF2 (progressive)
            if (marker is 0xC0 or 0xC2)
            {
                if (offset + 9 >= data.Length)
                    return null;

                int bitsPerComponent = data[offset + 4];
                int height = (data[offset + 5] << 8) | data[offset + 6];
                int width = (data[offset + 7] << 8) | data[offset + 8];
                int components = data[offset + 9];

                if (width <= 0 || height <= 0 || components <= 0)
                    return null;

                return new ImageInfo(width, height, components, bitsPerComponent,
                    ImageFormat.Jpeg, data, null);
            }

            // Skip other markers
            if (offset + 3 >= data.Length)
                return null;

            int segmentLength = (data[offset + 2] << 8) | data[offset + 3];
            offset += 2 + segmentLength;
        }

        return null;
    }

    // ── PNG ─────────────────────────────────────────────────────────────

    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>
    /// Parses a PNG file, extracts raw pixel data (with filter bytes removed),
    /// and returns FlateDecode-compressed data for PDF embedding.
    /// Supports RGB (type 2), RGBA (type 6), and Grayscale (type 0).
    /// </summary>
    internal static ImageInfo? TryParsePng(byte[] data)
    {
        if (data.Length < 8 + 25) // signature + minimum IHDR chunk
            return null;

        // Verify PNG signature
        for (int i = 0; i < PngSignature.Length; i++)
        {
            if (data[i] != PngSignature[i])
                return null;
        }

        // Parse IHDR chunk (must be the first chunk)
        int offset = 8;
        int ihdrLength = ReadBigEndianInt32(data, offset);
        if (ihdrLength < 13)
            return null;

        string ihdrType = System.Text.Encoding.ASCII.GetString(data, offset + 4, 4);
        if (ihdrType != "IHDR")
            return null;

        int width = ReadBigEndianInt32(data, offset + 8);
        int height = ReadBigEndianInt32(data, offset + 12);
        int bitDepth = data[offset + 16];
        int colorType = data[offset + 17];
        int compressionMethod = data[offset + 18];
        int filterMethod = data[offset + 19];
        int interlaceMethod = data[offset + 20];

        if (width <= 0 || height <= 0 || bitDepth != 8)
            return null;

        // We only support non-interlaced images
        if (interlaceMethod != 0)
            return null;

        // We only support deflate compression and adaptive filtering
        if (compressionMethod != 0 || filterMethod != 0)
            return null;

        // Determine components from color type
        int components = colorType switch
        {
            0 => 1, // Grayscale
            2 => 3, // RGB
            6 => 4, // RGBA
            _ => 0
        };

        if (components == 0)
            return null;

        // Collect all IDAT chunk data
        offset = 8; // Reset to after signature
        using var idatStream = new MemoryStream();

        while (offset + 8 <= data.Length)
        {
            int chunkLength = ReadBigEndianInt32(data, offset);
            string chunkType = System.Text.Encoding.ASCII.GetString(data, offset + 4, 4);

            if (offset + 12 + chunkLength > data.Length)
                break;

            if (chunkType == "IDAT")
            {
                idatStream.Write(data, offset + 8, chunkLength);
            }
            else if (chunkType == "IEND")
            {
                break;
            }

            offset += 12 + chunkLength; // 4 length + 4 type + data + 4 CRC
        }

        if (idatStream.Length == 0)
            return null;

        // Decompress IDAT data (zlib = 2-byte header + deflate data + 4-byte checksum)
        byte[] compressedData = idatStream.ToArray();
        if (compressedData.Length < 3)
            return null;

        byte[] rawPixelData;
        try
        {
            // Skip the 2-byte zlib header
            using var compressedStream = new MemoryStream(compressedData, 2, compressedData.Length - 2);
            using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            deflateStream.CopyTo(decompressed);
            rawPixelData = decompressed.ToArray();
        }
        catch
        {
            return null;
        }

        // Each scanline has a filter byte prefix
        int bytesPerPixel = components;
        int scanlineWidth = bytesPerPixel * width;
        int expectedLength = height * (1 + scanlineWidth);

        if (rawPixelData.Length < expectedLength)
            return null;

        // Remove filter bytes and de-filter each scanline
        byte[] unfilteredData = new byte[height * scanlineWidth];
        byte[] previousRow = new byte[scanlineWidth];

        for (int row = 0; row < height; row++)
        {
            int srcOffset = row * (1 + scanlineWidth);
            int dstOffset = row * scanlineWidth;
            byte filterType = rawPixelData[srcOffset];

            byte[] currentRow = new byte[scanlineWidth];

            for (int x = 0; x < scanlineWidth; x++)
            {
                byte rawByte = rawPixelData[srcOffset + 1 + x];
                byte a = x >= bytesPerPixel ? currentRow[x - bytesPerPixel] : (byte)0;
                byte b = previousRow[x];
                byte c = x >= bytesPerPixel ? previousRow[x - bytesPerPixel] : (byte)0;

                currentRow[x] = filterType switch
                {
                    0 => rawByte,                                       // None
                    1 => (byte)(rawByte + a),                           // Sub
                    2 => (byte)(rawByte + b),                           // Up
                    3 => (byte)(rawByte + (byte)((a + b) / 2)),         // Average
                    4 => (byte)(rawByte + PaethPredictor(a, b, c)),     // Paeth
                    _ => rawByte
                };
            }

            Array.Copy(currentRow, 0, unfilteredData, dstOffset, scanlineWidth);
            previousRow = currentRow;
        }

        // For RGBA, split into RGB + Alpha
        byte[]? alphaData = null;
        byte[] colorData;

        if (colorType == 6) // RGBA
        {
            int pixelCount = width * height;
            colorData = new byte[pixelCount * 3];
            alphaData = new byte[pixelCount];

            for (int i = 0; i < pixelCount; i++)
            {
                colorData[i * 3] = unfilteredData[i * 4];
                colorData[i * 3 + 1] = unfilteredData[i * 4 + 1];
                colorData[i * 3 + 2] = unfilteredData[i * 4 + 2];
                alphaData[i] = unfilteredData[i * 4 + 3];
            }

            components = 3; // The image XObject uses RGB; alpha goes to SMask
        }
        else
        {
            colorData = unfilteredData;
        }

        // Compress with DeflateStream for FlateDecode
        byte[] compressedColor = DeflateCompress(colorData);
        byte[]? compressedAlpha = alphaData is not null ? DeflateCompress(alphaData) : null;

        return new ImageInfo(width, height, components, bitDepth,
            ImageFormat.Png, compressedColor, compressedAlpha);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static int ReadBigEndianInt32(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];

    private static byte PaethPredictor(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc) return a;
        if (pb <= pc) return b;
        return c;
    }

    private static byte[] DeflateCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }
}
