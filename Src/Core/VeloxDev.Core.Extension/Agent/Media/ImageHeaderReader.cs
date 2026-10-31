using System;

namespace VeloxDev.AI;

/// <summary>
/// Reads width and height from the container headers only — no pixel decode.
/// <para>
/// Dimensions are a policy input (the default caps are 4096 px), not a rendering concern. Parsing the
/// header is cheap and works for a frame the host has not decoded, including the common screenshot case.
/// A container whose dimensions cannot be read from its header returns <see langword="false"/>; the size
/// policy then relies on the byte limit alone.
/// </para>
/// </summary>
internal static class ImageHeaderReader
{
    internal static bool TryRead(ReadOnlySpan<byte> bytes, AgentMediaFormat format, out int width, out int height)
    {
        width = height = 0;
        try
        {
            return format switch
            {
                AgentMediaFormat.Png => TryReadPng(bytes, out width, out height),
                AgentMediaFormat.Jpeg => TryReadJpeg(bytes, out width, out height),
                AgentMediaFormat.Gif => TryReadGif(bytes, out width, out height),
                AgentMediaFormat.Webp => TryReadWebp(bytes, out width, out height),
                AgentMediaFormat.Bmp => TryReadBmp(bytes, out width, out height),
                _ => false,
            };
        }
        catch (IndexOutOfRangeException)
        {
            // A truncated header is a corrupt image, not a crash for the caller; the size policy still
            // applies through the byte cap.
            width = height = 0;
            return false;
        }
    }

    private static bool TryReadPng(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = height = 0;
        if (bytes.Length < 24) return false;
        if (!AsciiAt(bytes, 12, "IHDR")) return false;
        width = ReadInt32BigEndian(bytes, 16);
        height = ReadInt32BigEndian(bytes, 20);
        return width > 0 && height > 0;
    }

    private static bool TryReadJpeg(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = height = 0;
        var offset = 2; // after SOI
        while (offset + 3 < bytes.Length)
        {
            if (bytes[offset] != 0xFF) { offset++; continue; }

            var marker = bytes[offset + 1];
            if (marker == 0xFF) { offset++; continue; }
            if (marker is 0xD8 or 0xD9 or 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                offset += 2;
                continue;
            }

            if (marker == 0xDA) return false; // start of scan: no frame header was found

            if (offset + 4 > bytes.Length) return false;
            var length = (bytes[offset + 2] << 8) | bytes[offset + 3];
            if (length < 2) return false;

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (offset + 9 > bytes.Length) return false;
                height = (bytes[offset + 5] << 8) | bytes[offset + 6];
                width = (bytes[offset + 7] << 8) | bytes[offset + 8];
                return width > 0 && height > 0;
            }

            offset += 2 + length;
        }

        return false;
    }

    private static bool TryReadGif(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = height = 0;
        if (bytes.Length < 10) return false;
        width = bytes[6] | (bytes[7] << 8);
        height = bytes[8] | (bytes[9] << 8);
        return width > 0 && height > 0;
    }

    private static bool TryReadWebp(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = height = 0;
        if (bytes.Length < 30) return false;
        if (!AsciiAt(bytes, 0, "RIFF") || !AsciiAt(bytes, 8, "WEBP")) return false;

        if (AsciiAt(bytes, 12, "VP8X"))
        {
            // Extended header: canvas size minus one, three bytes each, little endian.
            width = 1 + (bytes[24] | (bytes[25] << 8) | (bytes[26] << 16));
            height = 1 + (bytes[27] | (bytes[28] << 8) | (bytes[29] << 16));
            return true;
        }

        if (AsciiAt(bytes, 12, "VP8L"))
        {
            if (bytes[20] != 0x2F) return false;
            var b1 = bytes[21];
            var b2 = bytes[22];
            var b3 = bytes[23];
            var b4 = bytes[24];
            width = 1 + (((b2 & 0x3F) << 8) | b1);
            height = 1 + (((b4 & 0x0F) << 10) | (b3 << 2) | ((b2 & 0xC0) >> 6));
            return true;
        }

        if (AsciiAt(bytes, 12, "VP8 "))
        {
            // Lossy bitstream: 3-byte frame tag, then the 9D 01 2A start code, then two 14-bit sizes.
            if (bytes[23] != 0x9D || bytes[24] != 0x01 || bytes[25] != 0x2A) return false;
            width = (bytes[26] | (bytes[27] << 8)) & 0x3FFF;
            height = (bytes[28] | (bytes[29] << 8)) & 0x3FFF;
            return width > 0 && height > 0;
        }

        return false;
    }

    private static bool TryReadBmp(ReadOnlySpan<byte> bytes, out int width, out int height)
    {
        width = height = 0;
        if (bytes.Length < 26) return false;

        width = ReadInt32LittleEndian(bytes, 18);
        var rawHeight = ReadInt32LittleEndian(bytes, 22);
        height = rawHeight < 0 ? -rawHeight : rawHeight; // a negative height means top-down pixels
        return width > 0 && height > 0;
    }

    private static int ReadInt32BigEndian(ReadOnlySpan<byte> bytes, int offset)
        => (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private static int ReadInt32LittleEndian(ReadOnlySpan<byte> bytes, int offset)
        => bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);

    private static bool AsciiAt(ReadOnlySpan<byte> bytes, int offset, string text)
    {
        if (offset < 0 || offset + text.Length > bytes.Length) return false;
        for (var i = 0; i < text.Length; i++)
            if (bytes[offset + i] != (byte)text[i])
                return false;
        return true;
    }
}
