using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VeloxDev.Core.Extension.Test.Agent.Media;

/// <summary>
/// Minimal containers with known headers. They are not meant to render; the image layer reads the magic
/// bytes and the dimension fields, which is exactly what a provider's format check and a size policy need.
/// </summary>
internal static class MediaFixtures
{
    /// <summary>A real 1×1 PNG, so at least one fixture survives a decoder as well as the sniffers.</summary>
    public static byte[] Png1x1() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    public static byte[] Png(int width, int height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        AddBigEndian(bytes, 13);
        bytes.AddRange(Encoding.ASCII.GetBytes("IHDR"));
        AddBigEndian(bytes, width);
        AddBigEndian(bytes, height);
        bytes.AddRange(new byte[] { 8, 6, 0, 0, 0 });
        bytes.AddRange(new byte[] { 0, 0, 0, 0 }); // CRC is not read
        AddBigEndian(bytes, 0);
        bytes.AddRange(Encoding.ASCII.GetBytes("IEND"));
        bytes.AddRange(new byte[] { 0xAE, 0x42, 0x60, 0x82 });
        return bytes.ToArray();
    }

    public static byte[] Jpeg(int width, int height)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        bytes.AddRange(new byte[]
        {
            0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00,
            0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        });
        bytes.AddRange(new byte[]
        {
            0xFF, 0xC0, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)height,
            (byte)(width >> 8), (byte)width,
            0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        });
        bytes.AddRange(new byte[] { 0xFF, 0xD9 });
        return bytes.ToArray();
    }

    public static byte[] Gif(int width, int height, bool animated = false)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("GIF89a"))
        {
            (byte)width, (byte)(width >> 8),
            (byte)height, (byte)(height >> 8),
            0x00, 0x00, 0x00,
        };

        if (animated)
        {
            bytes.Add(0x21);
            bytes.Add(0xFF);
            bytes.Add(0x0B);
            bytes.AddRange(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            bytes.AddRange(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
        }

        bytes.Add(0x3B);
        return bytes.ToArray();
    }

    public static byte[] Webp(int width, int height, bool animated = false)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("RIFF"));
        AddLittleEndian(bytes, 22); // RIFF size; not read by the header reader
        bytes.AddRange(Encoding.ASCII.GetBytes("WEBP"));
        bytes.AddRange(Encoding.ASCII.GetBytes("VP8X"));
        AddLittleEndian(bytes, 10); // chunk size
        bytes.Add(animated ? (byte)0x02 : (byte)0x00);
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00 });
        AddLittleEndian24(bytes, width - 1);
        AddLittleEndian24(bytes, height - 1);
        bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // pad to the reader's 30-byte floor
        return bytes.ToArray();
    }

    public static byte[] Bmp(int width, int height)
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("BM"));
        bytes.AddRange(new byte[12]);
        AddLittleEndian(bytes, 40); // DIB header size
        AddLittleEndian(bytes, width);
        AddLittleEndian(bytes, height);
        return bytes.ToArray();
    }

    public static byte[] Svg() => Encoding.UTF8.GetBytes(
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"></svg>");

    public static byte[] Unknown() => new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };

    public static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF");

    public static byte[] Mp3() => new byte[] { (byte)'I', (byte)'D', (byte)'3', 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

    public static byte[] Wav()
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("RIFF"));
        bytes.AddRange(new byte[4]);
        bytes.AddRange(Encoding.ASCII.GetBytes("WAVEfmt "));
        bytes.AddRange(new byte[16]);
        return bytes.ToArray();
    }

    public static byte[] Flac() => Encoding.ASCII.GetBytes("fLaC").Concat(new byte[16]).ToArray();

    public static byte[] Ogg() => Encoding.ASCII.GetBytes("OggS").Concat(new byte[16]).ToArray();

    public static byte[] Mp4()
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("....ftypisom"));
        bytes.AddRange(new byte[16]);
        return bytes.ToArray();
    }

    public static byte[] Webm()
    {
        var bytes = new List<byte> { 0x1A, 0x45, 0xDF, 0xA3 };
        bytes.AddRange(Encoding.ASCII.GetBytes("webm"));
        bytes.AddRange(new byte[16]);
        return bytes.ToArray();
    }

    public static byte[] Zip() => new byte[] { (byte)'P', (byte)'K', 0x03, 0x04, 0x00, 0x00, 0x00, 0x00 };

    public static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);

    public static string PercentEncode(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 3);
        foreach (var value in bytes)
            builder.Append('%').Append(value.ToString("X2"));
        return builder.ToString();
    }

    private static void AddBigEndian(List<byte> bytes, int value)
    {
        bytes.Add((byte)(value >> 24));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)value);
    }

    private static void AddLittleEndian(List<byte> bytes, int value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)(value >> 16));
        bytes.Add((byte)(value >> 24));
    }

    private static void AddLittleEndian24(List<byte> bytes, int value)
    {
        bytes.Add((byte)value);
        bytes.Add((byte)(value >> 8));
        bytes.Add((byte)(value >> 16));
    }
}
