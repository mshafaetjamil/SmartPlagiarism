using System.Buffers.Binary;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// How many pixels decoding a picture will produce, read from its header without
/// decoding any of them. The file's size says nothing about this: decoding costs
/// a few bytes a pixel, and a picture of one colour compresses to almost nothing,
/// so a file of a few megabytes can declare a picture that would not fit in memory.
///
/// The format is told by the file's leading bytes, never by the content type its
/// package declares. Covers every format <see cref="PictureReader"/> passes to
/// OCR - PNG, JPEG, GIF, BMP, TIFF and WebP - with the BCL alone, as the Engine's
/// dependency rule asks.
/// </summary>
internal static class PictureSize
{
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> Vp8StartCode => [0x9D, 0x01, 0x2A];

    /// <returns>
    /// The number of pixels, or null when the bytes are not a picture in a known
    /// format, or its header is damaged or cut short.
    /// </returns>
    public static long? DecodedPixels(ReadOnlySpan<byte> picture)
    {
        if (picture.StartsWith(PngSignature))
        {
            return Png(picture);
        }

        if (picture.StartsWith(JpegSignature))
        {
            return Jpeg(picture);
        }

        if (picture.StartsWith("GIF87a"u8) || picture.StartsWith("GIF89a"u8))
        {
            return Gif(picture);
        }

        if (picture.StartsWith("BM"u8))
        {
            return Bmp(picture);
        }

        if (picture.StartsWith("II*\0"u8) || picture.StartsWith("MM\0*"u8))
        {
            return Tiff(picture);
        }

        if (picture.Length >= 16 && picture.StartsWith("RIFF"u8) && picture[8..12].SequenceEqual("WEBP"u8))
        {
            return WebP(picture);
        }

        return null;
    }

    /// <summary>Width and height open the IHDR chunk, which must come first.</summary>
    private static long? Png(ReadOnlySpan<byte> png) =>
        png.Length >= 24 && png[12..16].SequenceEqual("IHDR"u8)
            ? Area(BinaryPrimitives.ReadUInt32BigEndian(png[16..]), BinaryPrimitives.ReadUInt32BigEndian(png[20..]))
            : null;

    /// <summary>
    /// The size is in the start-of-frame segment. Segments before it are skipped
    /// whole by their declared length - among them APP1, whose EXIF thumbnail has a
    /// frame header of its own that must not be mistaken for the picture's.
    /// </summary>
    private static long? Jpeg(ReadOnlySpan<byte> jpeg)
    {
        var position = 2;

        while (position + 4 <= jpeg.Length)
        {
            if (jpeg[position] != 0xFF)
            {
                return null;
            }

            var marker = jpeg[position + 1];

            if (marker == 0xFF)
            {
                // A fill byte before the marker.
                position++;
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                // Standalone markers carry no length.
                position += 2;
                continue;
            }

            if (marker is 0xD9 or 0xDA)
            {
                // End of image, or the start of the pixel data: the frame header
                // should have come before either.
                return null;
            }

            // SOF0-SOF15, apart from three markers sharing the range: DHT, JPG and DAC.
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                // Marker, length, precision, then height and width.
                return position + 9 <= jpeg.Length
                    ? Area(
                        BinaryPrimitives.ReadUInt16BigEndian(jpeg[(position + 7)..]),
                        BinaryPrimitives.ReadUInt16BigEndian(jpeg[(position + 5)..]))
                    : null;
            }

            position += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg[(position + 2)..]);
        }

        return null;
    }

    /// <summary>
    /// Every frame of an animation is decoded, each at the size its own image
    /// descriptor declares, so all of them count - not only the logical screen
    /// size at the top of the file.
    /// </summary>
    private static long? Gif(ReadOnlySpan<byte> gif)
    {
        if (gif.Length < 13)
        {
            return null;
        }

        var position = 13 + ColorTableLength(gif[10]);
        long pixels = 0;

        while (position < gif.Length)
        {
            switch (gif[position])
            {
                case 0x3B:
                    // The trailer.
                    return pixels > 0 ? pixels : null;

                case 0x21:
                    // An extension: introducer, label, then data sub-blocks.
                    position = SkipSubBlocks(gif, position + 2);
                    break;

                case 0x2C:
                    // An image descriptor: position, size, flags, an optional local
                    // colour table, the LZW code size, then the data sub-blocks.
                    if (position + 10 > gif.Length
                        || Area(
                            BinaryPrimitives.ReadUInt16LittleEndian(gif[(position + 5)..]),
                            BinaryPrimitives.ReadUInt16LittleEndian(gif[(position + 7)..])) is not { } frame)
                    {
                        return null;
                    }

                    pixels += frame;
                    position = SkipSubBlocks(gif, position + 10 + ColorTableLength(gif[position + 9]) + 1);
                    break;

                default:
                    return null;
            }
        }

        // Cut short before the trailer: the frames found so far are still decoded.
        return pixels > 0 ? pixels : null;
    }

    private static int ColorTableLength(byte packedFields) =>
        (packedFields & 0x80) == 0 ? 0 : 3 << ((packedFields & 0x07) + 1);

    /// <returns>The position after the zero-length block that ends them.</returns>
    private static int SkipSubBlocks(ReadOnlySpan<byte> gif, int position)
    {
        while (position < gif.Length)
        {
            var size = gif[position++];

            if (size == 0)
            {
                break;
            }

            position += size;
        }

        return position;
    }

    /// <summary>
    /// The DIB header follows the 14-byte file header. The oldest kind holds
    /// 16-bit sizes; every later one 32-bit, the height negative for a bitmap
    /// stored top row first.
    /// </summary>
    private static long? Bmp(ReadOnlySpan<byte> bmp)
    {
        if (bmp.Length < 26)
        {
            return null;
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(bmp[14..]) == 12
            ? Area(BinaryPrimitives.ReadUInt16LittleEndian(bmp[18..]), BinaryPrimitives.ReadUInt16LittleEndian(bmp[20..]))
            : Area(
                (ulong)Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(bmp[18..])),
                (ulong)Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(bmp[22..])));
    }

    /// <summary>
    /// Width and length are tags 256 and 257 of the first image file directory -
    /// the image a decoder reads from a multi-page file.
    /// </summary>
    private static long? Tiff(ReadOnlySpan<byte> tiff)
    {
        var littleEndian = tiff[0] == (byte)'I';

        if (tiff.Length < 8)
        {
            return null;
        }

        var directory = ReadUInt32(tiff[4..], littleEndian);

        if (directory > tiff.Length - 2L)
        {
            return null;
        }

        var start = (int)directory;
        var entries = ReadUInt16(tiff[start..], littleEndian);
        ulong width = 0;
        ulong height = 0;

        for (var index = 0; index < entries; index++)
        {
            var entry = start + 2 + (index * 12);

            if (entry + 12 > tiff.Length)
            {
                return null;
            }

            // Tag, type, count, then the value itself when it fits in four bytes,
            // as a width does: SHORT (3) at the start of the field, or LONG (4).
            var value = ReadUInt16(tiff[(entry + 2)..], littleEndian) switch
            {
                3 => ReadUInt16(tiff[(entry + 8)..], littleEndian),
                4 => ReadUInt32(tiff[(entry + 8)..], littleEndian),
                _ => 0u,
            };

            switch (ReadUInt16(tiff[entry..], littleEndian))
            {
                case 256:
                    width = value;
                    break;
                case 257:
                    height = value;
                    break;
            }
        }

        return Area(width, height);
    }

    /// <summary>
    /// A RIFF container whose first chunk is VP8 (lossy), VP8L (lossless) or VP8X
    /// (extended: the canvas of an animation, or of a picture with transparency).
    /// </summary>
    private static long? WebP(ReadOnlySpan<byte> webp)
    {
        var chunk = webp[12..16];

        if (chunk.SequenceEqual("VP8 "u8))
        {
            // A 3-byte frame tag and the start code, then 14-bit width and height.
            return webp.Length >= 30 && webp[23..26].SequenceEqual(Vp8StartCode)
                ? Area(
                    BinaryPrimitives.ReadUInt16LittleEndian(webp[26..]) & 0x3FFFu,
                    BinaryPrimitives.ReadUInt16LittleEndian(webp[28..]) & 0x3FFFu)
                : null;
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            // A signature byte, then width - 1 and height - 1 in 14 bits each.
            if (webp.Length < 25 || webp[20] != 0x2F)
            {
                return null;
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(webp[21..]);
            return Area((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            // Four bytes of flags, then canvas width - 1 and height - 1 in 24 bits each.
            return webp.Length >= 30 ? Area(UInt24(webp[24..]) + 1, UInt24(webp[27..]) + 1) : null;
        }

        return null;
    }

    /// <summary>Null for an empty picture, which no decoder will produce either.</summary>
    private static long? Area(ulong width, ulong height)
    {
        if (width == 0 || height == 0)
        {
            return null;
        }

        // Each side fits in 32 bits, so the product fits in 64 unsigned ones.
        var pixels = width * height;
        return pixels > long.MaxValue ? long.MaxValue : (long)pixels;
    }

    private static uint UInt24(ReadOnlySpan<byte> bytes) => (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16));

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
}
