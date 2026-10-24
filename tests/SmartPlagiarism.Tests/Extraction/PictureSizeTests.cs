using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SmartPlagiarism.Engine.Extraction;

namespace SmartPlagiarism.Tests.Extraction;

/// <summary>
/// Reading how many pixels a picture will decode to from its header alone, which
/// is what lets one too large to decode safely be skipped before OCR touches it.
/// Each format's header is built by hand: only its first few bytes are needed.
/// </summary>
public class PictureSizeTests
{
    [Fact]
    public void Png_IsMeasuredFromItsHeader()
    {
        PictureSize.DecodedPixels(PngFixture.Create(width: 30, height: 20)).Should().Be(600);
    }

    [Fact]
    public void Png_HeaderAloneDeclaringAnEnormousPicture_IsMeasuredWithoutDecodingIt()
    {
        var header = PngFixture.Create()[..24];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(16), 100_000);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(20), 100_000);

        PictureSize.DecodedPixels(header).Should().Be(10_000_000_000);
    }

    [Fact]
    public void Jpeg_IsMeasuredFromItsFrameHeader_NotFromTheExifThumbnailBeforeIt()
    {
        byte[] thumbnailFrame = [0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x10, 0x00, 0x10];
        byte[] jpeg =
        [
            0xFF, 0xD8,
            .. JpegSegment(0xE1, [.. "Exif\0\0"u8, .. thumbnailFrame]),
            .. JpegSegment(0xDB, new byte[65]),

            // A progressive frame header: 3000 high, 4000 wide, three components.
            .. JpegSegment(0xC2, [0x08, 0x0B, 0xB8, 0x0F, 0xA0, 0x03, .. new byte[9]]),
        ];

        PictureSize.DecodedPixels(jpeg).Should().Be(12_000_000);
    }

    [Fact]
    public void Jpeg_WhosePixelDataStartsBeforeAnyFrameHeader_CannotBeMeasured()
    {
        byte[] jpeg = [0xFF, 0xD8, .. JpegSegment(0xDA, new byte[10])];

        PictureSize.DecodedPixels(jpeg).Should().BeNull();
    }

    [Fact]
    public void Gif_CountsEveryFrameOfAnAnimation_NotJustTheScreenSize()
    {
        byte[] gif =
        [
            .. "GIF89a"u8, .. Le16(10), .. Le16(10), 0x80, 0x00, 0x00,
            .. new byte[6],
            0x21, 0xF9, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. GifFrame(width: 100, height: 50),
            .. GifFrame(width: 100, height: 50),
            0x3B,
        ];

        PictureSize.DecodedPixels(gif).Should().Be(10_000);
    }

    [Theory]
    [InlineData(300, 200)]
    [InlineData(300, -200)]
    public void Bmp_IsMeasuredFromItsInfoHeader_WhicheverWayUpItIsStored(int width, int height)
    {
        var bmp = new byte[54];
        "BM"u8.CopyTo(bmp);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), height);

        PictureSize.DecodedPixels(bmp).Should().Be(60_000);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tiff_IsMeasuredFromItsFirstDirectory_InEitherByteOrder(bool littleEndian)
    {
        var tiff = new byte[8 + 2 + (3 * 12) + 4];
        (littleEndian ? "II*\0"u8 : "MM\0*"u8).CopyTo(tiff);
        Write32(tiff, 4, 8, littleEndian);
        Write16(tiff, 8, 3, littleEndian);

        WriteEntry(tiff, 10, tag: 254, type: 4, value: 0, littleEndian);
        WriteEntry(tiff, 22, tag: 256, type: 3, value: 640, littleEndian);
        WriteEntry(tiff, 34, tag: 257, type: 4, value: 480, littleEndian);

        PictureSize.DecodedPixels(tiff).Should().Be(307_200);
    }

    [Fact]
    public void WebP_Lossy_IsMeasuredFromItsFrameHeader()
    {
        byte[] frame = [0x00, 0x00, 0x00, 0x9D, 0x01, 0x2A, .. Le16(640), .. Le16(480)];

        PictureSize.DecodedPixels(WebP("VP8 ", frame)).Should().Be(307_200);
    }

    [Fact]
    public void WebP_Lossless_IsMeasuredFromItsHeader()
    {
        var bits = (640u - 1) | ((480u - 1) << 14);
        byte[] header = [0x2F, .. Le32(bits)];

        PictureSize.DecodedPixels(WebP("VP8L", header)).Should().Be(307_200);
    }

    [Fact]
    public void WebP_Extended_IsMeasuredFromItsCanvas()
    {
        byte[] header = [0x00, 0x00, 0x00, 0x00, .. Le32(640 - 1)[..3], .. Le32(480 - 1)[..3]];

        PictureSize.DecodedPixels(WebP("VP8X", header)).Should().Be(307_200);
    }

    [Fact]
    public void BytesInNoKnownFormat_CannotBeMeasured()
    {
        PictureSize.DecodedPixels("not a picture at all"u8).Should().BeNull();
        PictureSize.DecodedPixels([]).Should().BeNull();
    }

    [Fact]
    public void HeaderCutShort_CannotBeMeasured()
    {
        PictureSize.DecodedPixels(PngFixture.Create()[..20]).Should().BeNull();
    }

    [Fact]
    public void PictureDeclaringNoPixels_CannotBeMeasured()
    {
        PictureSize.DecodedPixels(PngFixture.Create(width: 0, height: 10)).Should().BeNull();
    }

    private static byte[] JpegSegment(byte marker, byte[] data) =>
        [0xFF, marker, .. Be16(data.Length + 2), .. data];

    /// <summary>An image descriptor, the LZW code size, one data block and the terminator.</summary>
    private static byte[] GifFrame(int width, int height) =>
        [0x2C, 0x00, 0x00, 0x00, 0x00, .. Le16(width), .. Le16(height), 0x00, 0x02, 0x02, 0x4C, 0x01, 0x00];

    private static byte[] WebP(string chunk, byte[] data) =>
        [.. "RIFF"u8, .. Le32((uint)(12 + data.Length)), .. "WEBP"u8, .. Encoding.ASCII.GetBytes(chunk), .. Le32((uint)data.Length), .. data];

    private static void WriteEntry(byte[] tiff, int offset, ushort tag, ushort type, uint value, bool littleEndian)
    {
        Write16(tiff, offset, tag, littleEndian);
        Write16(tiff, offset + 2, type, littleEndian);
        Write32(tiff, offset + 4, 1, littleEndian);

        // A SHORT sits at the start of the four-byte value field.
        if (type == 3)
        {
            Write16(tiff, offset + 8, (ushort)value, littleEndian);
        }
        else
        {
            Write32(tiff, offset + 8, value, littleEndian);
        }
    }

    private static void Write16(byte[] bytes, int offset, ushort value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offset), value);
        }
    }

    private static void Write32(byte[] bytes, int offset, uint value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), value);
        }
    }

    private static byte[] Le16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] Be16(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
        return bytes;
    }

    private static byte[] Le32(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }
}
