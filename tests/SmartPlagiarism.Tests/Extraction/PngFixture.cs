using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SmartPlagiarism.Tests.Extraction;

/// <summary>
/// Writes real PNG files, for pictures in the PDF, DOCX and PPTX fixtures. The
/// pixels are noise, so the file does not compress below the size the
/// extractors treat as an icon; what OCR "reads" in them is scripted.
/// </summary>
internal static class PngFixture
{
    private static readonly uint[] s_crcTable = BuildCrcTable();

    internal static byte[] Create(int width = 120, int height = 120)
    {
        var random = new Random(42);
        var pixels = new MemoryStream();

        for (var row = 0; row < height; row++)
        {
            pixels.WriteByte(0); // no filter

            var line = new byte[width * 3];
            random.NextBytes(line);
            pixels.Write(line);
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // bits per channel
        header[9] = 2;  // RGB

        var compressed = new MemoryStream();

        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(pixels.ToArray());
        }

        var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);

        return png.ToArray();
    }

    /// <summary>Width and height from a PNG's header.</summary>
    internal static (int Width, int Height) SizeOf(byte[] png) =>
        (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));

    private static void WriteChunk(Stream png, string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        png.Write(typeBytes);
        png.Write(data);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc([.. typeBytes, .. data]));
        png.Write(crc);
    }

    private static uint Crc(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in bytes)
        {
            crc = s_crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (var n = 0u; n < 256; n++)
        {
            var c = n;

            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
