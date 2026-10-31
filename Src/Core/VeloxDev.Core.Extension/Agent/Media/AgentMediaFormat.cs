using System;
using System.Collections.Generic;
using System.Text;

namespace VeloxDev.AI;

/// <summary>The broad class of a media payload.</summary>
public enum AgentMediaKind
{
    /// <summary>Not classified.</summary>
    Unknown,

    /// <summary>A bitmap image (PNG, JPEG, WebP, GIF, BMP, TIFF, AVIF, HEIC) or SVG.</summary>
    Image,

    /// <summary>Audio (MP3, WAV, FLAC, Ogg, AAC, M4A).</summary>
    Audio,

    /// <summary>Video (MP4, WebM, MKV, AVI, MPEG, QuickTime, FLV).</summary>
    Video,

    /// <summary>A document (PDF, RTF, OOXML/ODF, EPUB).</summary>
    Document,

    /// <summary>Text (plain, Markdown, HTML, CSV, JSON, XML).</summary>
    Text,

    /// <summary>Anything else; requires an explicit opt-in.</summary>
    Binary,
}

/// <summary>The containers this layer recognises by content.</summary>
public enum AgentMediaFormat
{
    /// <summary>Not a recognised container.</summary>
    Unknown,

    Png,
    Jpeg,
    Webp,
    Gif,
    Bmp,
    Tiff,
    Avif,
    Heic,
    Svg,

    Mp3,
    Wav,
    Flac,
    Ogg,
    Aac,
    M4a,

    Mp4,
    Webm,
    Mkv,
    Avi,
    Mpeg,
    QuickTime,
    Flv,

    Pdf,
    Zip,
    Rtf,

    PlainText,
    Html,
    Json,
    Xml,
    Csv,
}

internal readonly struct AgentMediaDescriptor
{
    internal AgentMediaDescriptor(AgentMediaFormat format, AgentMediaKind kind, string mediaType)
    {
        Format = format;
        Kind = kind;
        MediaType = mediaType;
    }

    internal AgentMediaFormat Format { get; }
    internal AgentMediaKind Kind { get; }
    internal string MediaType { get; }
}

internal static class AgentMediaFormats
{
    internal const string OctetStream = "application/octet-stream";

    internal static AgentMediaDescriptor Detect(ReadOnlySpan<byte> bytes, string? declaredMediaType)
    {
        var declared = CanonicalMediaType(declaredMediaType);
        var declaredKind = KindOfMediaType(declared);

        var image = DetectImage(bytes);
        if (image is not null) return Refine(image.Value, declared, declaredKind);

        var other = DetectOther(bytes);
        if (other is not null) return Refine(other.Value, declared, declaredKind);

        if (declared is not null)
        {
            // An image claim without image magic is kept as an Image with an unknown container: the policy
            // layer can then insist on a transcoder instead of silently sending bytes no provider can read.
            // Non-image kinds trust the declaration when their container has no magic of its own.
            var format = declaredKind == AgentMediaKind.Image
                ? AgentMediaFormat.Unknown
                : FormatOfMediaType(declared);
            return new AgentMediaDescriptor(format, declaredKind, declared);
        }

        if (LooksLikeText(bytes))
            return new AgentMediaDescriptor(AgentMediaFormat.PlainText, AgentMediaKind.Text, "text/plain");

        return new AgentMediaDescriptor(AgentMediaFormat.Unknown, AgentMediaKind.Binary, OctetStream);
    }

    private static AgentMediaDescriptor Refine(
        AgentMediaDescriptor magic, string? declared, AgentMediaKind declaredKind)
    {
        if (declared is null || declaredKind == AgentMediaKind.Unknown) return magic;

        // The bytes win for images; a wrong claim is corrected (or rejected under strict checking).
        if (magic.Kind == AgentMediaKind.Image && declaredKind == AgentMediaKind.Image) return magic;

        // ISO BMFF and EBML are shared by audio and video; the declared type disambiguates them.
        if ((magic.Kind == AgentMediaKind.Audio || magic.Kind == AgentMediaKind.Video) &&
            (declaredKind == AgentMediaKind.Audio || declaredKind == AgentMediaKind.Video))
            return new AgentMediaDescriptor(magic.Format, declaredKind, declared);

        // OOXML/ODF are ZIP containers; the declared type is the only thing that names the document.
        if (magic.Format == AgentMediaFormat.Zip && declaredKind == AgentMediaKind.Document)
            return new AgentMediaDescriptor(magic.Format, declaredKind, declared);

        if (magic.Kind == AgentMediaKind.Text &&
            (declaredKind == AgentMediaKind.Text || declaredKind == AgentMediaKind.Document))
            return new AgentMediaDescriptor(magic.Format, declaredKind, declared);

        if (magic.Kind == AgentMediaKind.Binary && declaredKind != AgentMediaKind.Binary)
            return new AgentMediaDescriptor(FormatOfMediaType(declared), declaredKind, declared);

        return magic;
    }

    private static AgentMediaDescriptor? DetectImage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return Of(AgentMediaFormat.Png);

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return Of(AgentMediaFormat.Jpeg);

        if (AsciiAt(bytes, 0, "GIF87a") || AsciiAt(bytes, 0, "GIF89a"))
            return Of(AgentMediaFormat.Gif);

        if (bytes.Length >= 12 && AsciiAt(bytes, 0, "RIFF") && AsciiAt(bytes, 8, "WEBP"))
            return Of(AgentMediaFormat.Webp);

        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
            return Of(AgentMediaFormat.Bmp);

        if (bytes.Length >= 4 && (AsciiAt(bytes, 0, "II*\0") || AsciiAt(bytes, 0, "MM\0*")))
            return Of(AgentMediaFormat.Tiff);

        if (bytes.Length >= 12 && AsciiAt(bytes, 4, "ftyp"))
        {
            var brand = Ascii(bytes, 8, 4);
            if (brand is "avif" or "avis") return Of(AgentMediaFormat.Avif);
            if (brand is "heic" or "heix" or "hevc" or "hevx" or "mif1" or "msf1")
                return Of(AgentMediaFormat.Heic);
        }

        if (LooksLikeSvg(bytes)) return Of(AgentMediaFormat.Svg);

        return null;
    }

    private static AgentMediaDescriptor? DetectOther(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 4 && AsciiAt(bytes, 0, "%PDF"))
            return Of(AgentMediaFormat.Pdf);

        if (bytes.Length >= 12 && AsciiAt(bytes, 0, "RIFF"))
        {
            if (AsciiAt(bytes, 8, "WAVE")) return Of(AgentMediaFormat.Wav);
            if (AsciiAt(bytes, 8, "AVI ")) return Of(AgentMediaFormat.Avi);
        }

        if (bytes.Length >= 4 && AsciiAt(bytes, 0, "fLaC")) return Of(AgentMediaFormat.Flac);
        if (bytes.Length >= 4 && AsciiAt(bytes, 0, "OggS")) return Of(AgentMediaFormat.Ogg);

        if (bytes.Length >= 3 && AsciiAt(bytes, 0, "ID3")) return Of(AgentMediaFormat.Mp3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF)
        {
            if (bytes[1] is 0xFB or 0xF3 or 0xF2 or 0xFA or 0xE3 or 0xE2)
                return Of(AgentMediaFormat.Mp3);
            if (bytes[1] is 0xF1 or 0xF9)
                return Of(AgentMediaFormat.Aac);
        }

        if (bytes.Length >= 12 && AsciiAt(bytes, 4, "ftyp"))
        {
            var brand = Ascii(bytes, 8, 4);
            if (brand is "M4A " or "M4B " or "M4P ") return Of(AgentMediaFormat.M4a);
            if (brand is "qt  ") return Of(AgentMediaFormat.QuickTime);
            if (brand is "isom" or "iso2" or "mp41" or "mp42" or "avc1" or "dash" or "mmp4")
                return Of(AgentMediaFormat.Mp4);
        }

        if (bytes.Length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
        {
            var head = bytes.Slice(0, Math.Min(bytes.Length, 64));
            return IndexOfAscii(head, "webm") >= 0 ? Of(AgentMediaFormat.Webm) : Of(AgentMediaFormat.Mkv);
        }

        if (bytes.Length >= 3 && AsciiAt(bytes, 0, "FLV")) return Of(AgentMediaFormat.Flv);
        if (bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 &&
            (bytes[3] == 0xBA || bytes[3] == 0xB3))
            return Of(AgentMediaFormat.Mpeg);

        if (bytes.Length >= 4 && bytes[0] == (byte)'P' && bytes[1] == (byte)'K' &&
            bytes[2] == 0x03 && bytes[3] == 0x04)
            return Of(AgentMediaFormat.Zip);

        if (bytes.Length >= 5 && AsciiAt(bytes, 0, "{\\rtf")) return Of(AgentMediaFormat.Rtf);

        if (LooksLikeHtml(bytes)) return Of(AgentMediaFormat.Html);
        if (LooksLikeXml(bytes)) return Of(AgentMediaFormat.Xml);

        return null;
    }

    private static AgentMediaDescriptor Of(AgentMediaFormat format)
        => new(format, KindOfFormat(format), MediaTypeOf(format) ?? OctetStream);

    internal static string? MediaTypeOf(AgentMediaFormat format) => format switch
    {
        AgentMediaFormat.Png => "image/png",
        AgentMediaFormat.Jpeg => "image/jpeg",
        AgentMediaFormat.Webp => "image/webp",
        AgentMediaFormat.Gif => "image/gif",
        AgentMediaFormat.Bmp => "image/bmp",
        AgentMediaFormat.Tiff => "image/tiff",
        AgentMediaFormat.Avif => "image/avif",
        AgentMediaFormat.Heic => "image/heic",
        AgentMediaFormat.Svg => "image/svg+xml",
        AgentMediaFormat.Mp3 => "audio/mpeg",
        AgentMediaFormat.Wav => "audio/wav",
        AgentMediaFormat.Flac => "audio/flac",
        AgentMediaFormat.Ogg => "audio/ogg",
        AgentMediaFormat.Aac => "audio/aac",
        AgentMediaFormat.M4a => "audio/mp4",
        AgentMediaFormat.Mp4 => "video/mp4",
        AgentMediaFormat.Webm => "video/webm",
        AgentMediaFormat.Mkv => "video/x-matroska",
        AgentMediaFormat.Avi => "video/x-msvideo",
        AgentMediaFormat.Mpeg => "video/mpeg",
        AgentMediaFormat.QuickTime => "video/quicktime",
        AgentMediaFormat.Flv => "video/x-flv",
        AgentMediaFormat.Pdf => "application/pdf",
        AgentMediaFormat.Zip => "application/zip",
        AgentMediaFormat.Rtf => "application/rtf",
        AgentMediaFormat.PlainText => "text/plain",
        AgentMediaFormat.Html => "text/html",
        AgentMediaFormat.Json => "application/json",
        AgentMediaFormat.Xml => "application/xml",
        AgentMediaFormat.Csv => "text/csv",
        _ => null,
    };

    internal static AgentMediaKind KindOfFormat(AgentMediaFormat format) => format switch
    {
        AgentMediaFormat.Png or AgentMediaFormat.Jpeg or AgentMediaFormat.Webp or AgentMediaFormat.Gif
            or AgentMediaFormat.Bmp or AgentMediaFormat.Tiff or AgentMediaFormat.Avif or AgentMediaFormat.Heic
            or AgentMediaFormat.Svg => AgentMediaKind.Image,

        AgentMediaFormat.Mp3 or AgentMediaFormat.Wav or AgentMediaFormat.Flac
            or AgentMediaFormat.Ogg or AgentMediaFormat.Aac or AgentMediaFormat.M4a => AgentMediaKind.Audio,

        AgentMediaFormat.Mp4 or AgentMediaFormat.Webm or AgentMediaFormat.Mkv
            or AgentMediaFormat.Avi or AgentMediaFormat.Mpeg or AgentMediaFormat.QuickTime
            or AgentMediaFormat.Flv => AgentMediaKind.Video,

        AgentMediaFormat.Pdf or AgentMediaFormat.Rtf => AgentMediaKind.Document,
        AgentMediaFormat.Zip => AgentMediaKind.Binary,
        AgentMediaFormat.PlainText or AgentMediaFormat.Html or AgentMediaFormat.Json
            or AgentMediaFormat.Xml or AgentMediaFormat.Csv => AgentMediaKind.Text,
        _ => AgentMediaKind.Unknown,
    };

    internal static bool IsImageFormat(AgentMediaFormat format)
        => KindOfFormat(format) == AgentMediaKind.Image;

    internal static string? FileExtensionOf(AgentMediaFormat format) => format switch
    {
        AgentMediaFormat.Png => ".png",
        AgentMediaFormat.Jpeg => ".jpg",
        AgentMediaFormat.Webp => ".webp",
        AgentMediaFormat.Gif => ".gif",
        AgentMediaFormat.Bmp => ".bmp",
        AgentMediaFormat.Tiff => ".tiff",
        AgentMediaFormat.Avif => ".avif",
        AgentMediaFormat.Heic => ".heic",
        AgentMediaFormat.Svg => ".svg",
        AgentMediaFormat.Mp3 => ".mp3",
        AgentMediaFormat.Wav => ".wav",
        AgentMediaFormat.Flac => ".flac",
        AgentMediaFormat.Ogg => ".ogg",
        AgentMediaFormat.Aac => ".aac",
        AgentMediaFormat.M4a => ".m4a",
        AgentMediaFormat.Mp4 => ".mp4",
        AgentMediaFormat.Webm => ".webm",
        AgentMediaFormat.Mkv => ".mkv",
        AgentMediaFormat.Avi => ".avi",
        AgentMediaFormat.Mpeg => ".mpeg",
        AgentMediaFormat.QuickTime => ".mov",
        AgentMediaFormat.Flv => ".flv",
        AgentMediaFormat.Pdf => ".pdf",
        AgentMediaFormat.Zip => ".zip",
        AgentMediaFormat.Rtf => ".rtf",
        AgentMediaFormat.PlainText => ".txt",
        AgentMediaFormat.Html => ".html",
        AgentMediaFormat.Json => ".json",
        AgentMediaFormat.Xml => ".xml",
        AgentMediaFormat.Csv => ".csv",
        _ => null,
    };

    internal static string Describe(AgentMediaFormat format) => format switch
    {
        AgentMediaFormat.Png => "PNG",
        AgentMediaFormat.Jpeg => "JPEG",
        AgentMediaFormat.Webp => "WebP",
        AgentMediaFormat.Gif => "GIF",
        AgentMediaFormat.Bmp => "BMP",
        AgentMediaFormat.Tiff => "TIFF",
        AgentMediaFormat.Avif => "AVIF",
        AgentMediaFormat.Heic => "HEIC",
        AgentMediaFormat.Svg => "SVG",
        AgentMediaFormat.Mp3 => "MP3",
        AgentMediaFormat.Wav => "WAV",
        AgentMediaFormat.Flac => "FLAC",
        AgentMediaFormat.Ogg => "Ogg",
        AgentMediaFormat.Aac => "AAC",
        AgentMediaFormat.M4a => "M4A",
        AgentMediaFormat.Mp4 => "MP4",
        AgentMediaFormat.Webm => "WebM",
        AgentMediaFormat.Mkv => "Matroska",
        AgentMediaFormat.Avi => "AVI",
        AgentMediaFormat.Mpeg => "MPEG",
        AgentMediaFormat.QuickTime => "QuickTime",
        AgentMediaFormat.Flv => "FLV",
        AgentMediaFormat.Pdf => "PDF",
        AgentMediaFormat.Zip => "ZIP",
        AgentMediaFormat.Rtf => "RTF",
        AgentMediaFormat.PlainText => "text",
        AgentMediaFormat.Html => "HTML",
        AgentMediaFormat.Json => "JSON",
        AgentMediaFormat.Xml => "XML",
        AgentMediaFormat.Csv => "CSV",
        _ => "unknown",
    };

    internal static string Describe(AgentMediaKind kind) => kind switch
    {
        AgentMediaKind.Image => "image",
        AgentMediaKind.Audio => "audio",
        AgentMediaKind.Video => "video",
        AgentMediaKind.Document => "document",
        AgentMediaKind.Text => "text",
        AgentMediaKind.Binary => "binary",
        _ => "unknown",
    };

    internal static string? CanonicalMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return null;

        var value = mediaType!.Trim();
        var semicolon = value.IndexOf(';');
        if (semicolon >= 0) value = value.Substring(0, semicolon).Trim();
        if (value.Length == 0) return null;

        value = value.ToLowerInvariant();
        return value switch
        {
            "image/jpg" or "image/pjpeg" => "image/jpeg",
            "image/x-png" => "image/png",
            "image/x-webp" => "image/webp",
            "image/x-gif" => "image/gif",
            "image/x-bmp" or "image/x-ms-bmp" => "image/bmp",
            "image/x-tiff" => "image/tiff",
            "image/heif" or "image/heic-sequence" or "image/heif-sequence" => "image/heic",

            "audio/mp3" or "audio/x-mp3" or "audio/mpeg3" => "audio/mpeg",
            "audio/x-wav" or "audio/wave" => "audio/wav",
            "audio/x-flac" => "audio/flac",
            "audio/x-m4a" or "audio/m4a" => "audio/mp4",
            "audio/x-aac" => "audio/aac",
            "audio/x-ogg" => "audio/ogg",

            "video/avi" or "video/msvideo" => "video/x-msvideo",
            "video/mkv" => "video/x-matroska",
            "video/x-m4v" => "video/mp4",
            "video/mov" => "video/quicktime",

            "application/x-pdf" => "application/pdf",
            "application/x-json" => "application/json",
            "application/xml" or "text/xml" => "application/xml",
            "text/x-markdown" or "application/x-markdown" => "text/markdown",
            "application/x-zip-compressed" => "application/zip",

            "application/octet-stream" => OctetStream,
            _ => value,
        };
    }

    internal static AgentMediaKind KindOfMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return AgentMediaKind.Unknown;

        if (mediaType!.StartsWith("image/", StringComparison.Ordinal)) return AgentMediaKind.Image;
        if (mediaType.StartsWith("audio/", StringComparison.Ordinal)) return AgentMediaKind.Audio;
        if (mediaType.StartsWith("video/", StringComparison.Ordinal)) return AgentMediaKind.Video;
        if (mediaType.StartsWith("text/", StringComparison.Ordinal)) return AgentMediaKind.Text;

        return mediaType switch
        {
            "application/json" or "application/ld+json" or "application/x-ndjson"
                or "application/javascript" or "application/x-javascript"
                or "application/xml" or "application/xhtml+xml"
                or "application/yaml" or "application/x-yaml" => AgentMediaKind.Text,

            "application/pdf" or "application/rtf" or "application/msword"
                or "application/vnd.ms-excel" or "application/vnd.ms-powerpoint"
                or "application/epub+zip" => AgentMediaKind.Document,

            _ when mediaType.StartsWith("application/vnd.openxmlformats-officedocument.", StringComparison.Ordinal)
                || mediaType.StartsWith("application/vnd.oasis.opendocument.", StringComparison.Ordinal)
                => AgentMediaKind.Document,

            "application/zip" or OctetStream => AgentMediaKind.Binary,
            _ => AgentMediaKind.Binary,
        };
    }

    internal static AgentMediaFormat FormatOfMediaType(string? mediaType) => mediaType switch
    {
        "image/png" => AgentMediaFormat.Png,
        "image/jpeg" => AgentMediaFormat.Jpeg,
        "image/webp" => AgentMediaFormat.Webp,
        "image/gif" => AgentMediaFormat.Gif,
        "image/bmp" => AgentMediaFormat.Bmp,
        "image/tiff" => AgentMediaFormat.Tiff,
        "image/avif" => AgentMediaFormat.Avif,
        "image/heic" => AgentMediaFormat.Heic,
        "image/svg+xml" => AgentMediaFormat.Svg,
        "audio/mpeg" => AgentMediaFormat.Mp3,
        "audio/wav" => AgentMediaFormat.Wav,
        "audio/flac" => AgentMediaFormat.Flac,
        "audio/ogg" => AgentMediaFormat.Ogg,
        "audio/aac" => AgentMediaFormat.Aac,
        "audio/mp4" => AgentMediaFormat.M4a,
        "video/mp4" => AgentMediaFormat.Mp4,
        "video/webm" => AgentMediaFormat.Webm,
        "video/x-matroska" => AgentMediaFormat.Mkv,
        "video/x-msvideo" => AgentMediaFormat.Avi,
        "video/mpeg" => AgentMediaFormat.Mpeg,
        "video/quicktime" => AgentMediaFormat.QuickTime,
        "video/x-flv" => AgentMediaFormat.Flv,
        "application/pdf" => AgentMediaFormat.Pdf,
        "application/rtf" => AgentMediaFormat.Rtf,
        "application/zip" => AgentMediaFormat.Zip,
        "text/plain" or "text/markdown" => AgentMediaFormat.PlainText,
        "text/html" => AgentMediaFormat.Html,
        "application/json" => AgentMediaFormat.Json,
        "application/xml" => AgentMediaFormat.Xml,
        "text/csv" => AgentMediaFormat.Csv,
        _ when mediaType is not null &&
               (mediaType.StartsWith("application/vnd.openxmlformats-officedocument.", StringComparison.Ordinal) ||
                mediaType.StartsWith("application/vnd.oasis.opendocument.", StringComparison.Ordinal) ||
                mediaType == "application/epub+zip") => AgentMediaFormat.Zip,
        _ => AgentMediaFormat.Unknown,
    };

    internal static AgentMediaFormat FormatOfExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => AgentMediaFormat.Png,
        ".jpg" or ".jpeg" or ".jpe" => AgentMediaFormat.Jpeg,
        ".webp" => AgentMediaFormat.Webp,
        ".gif" => AgentMediaFormat.Gif,
        ".bmp" => AgentMediaFormat.Bmp,
        ".tif" or ".tiff" => AgentMediaFormat.Tiff,
        ".avif" => AgentMediaFormat.Avif,
        ".heic" or ".heif" => AgentMediaFormat.Heic,
        ".svg" => AgentMediaFormat.Svg,
        ".mp3" => AgentMediaFormat.Mp3,
        ".wav" => AgentMediaFormat.Wav,
        ".flac" => AgentMediaFormat.Flac,
        ".ogg" or ".oga" => AgentMediaFormat.Ogg,
        ".aac" => AgentMediaFormat.Aac,
        ".m4a" => AgentMediaFormat.M4a,
        ".mp4" or ".m4v" => AgentMediaFormat.Mp4,
        ".webm" => AgentMediaFormat.Webm,
        ".mkv" => AgentMediaFormat.Mkv,
        ".avi" => AgentMediaFormat.Avi,
        ".mpeg" or ".mpg" => AgentMediaFormat.Mpeg,
        ".mov" => AgentMediaFormat.QuickTime,
        ".flv" => AgentMediaFormat.Flv,
        ".pdf" => AgentMediaFormat.Pdf,
        ".zip" => AgentMediaFormat.Zip,
        ".rtf" => AgentMediaFormat.Rtf,
        ".txt" => AgentMediaFormat.PlainText,
        ".md" or ".markdown" => AgentMediaFormat.PlainText,
        ".html" or ".htm" => AgentMediaFormat.Html,
        ".json" => AgentMediaFormat.Json,
        ".xml" => AgentMediaFormat.Xml,
        ".csv" => AgentMediaFormat.Csv,
        _ => AgentMediaFormat.Unknown,
    };

    internal static string? FileExtensionFor(string mediaType, AgentMediaFormat format) => mediaType switch
    {
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation" => ".pptx",
        "application/vnd.oasis.opendocument.text" => ".odt",
        "application/vnd.oasis.opendocument.spreadsheet" => ".ods",
        "application/vnd.oasis.opendocument.presentation" => ".odp",
        "application/msword" => ".doc",
        "application/vnd.ms-excel" => ".xls",
        "application/vnd.ms-powerpoint" => ".ppt",
        "application/epub+zip" => ".epub",
        "text/markdown" => ".md",
        "text/plain" => ".txt",
        "text/html" => ".html",
        "text/csv" => ".csv",
        "application/json" => ".json",
        "application/xml" => ".xml",
        _ => FileExtensionOf(format),
    };

    internal static string? MediaTypeFromExtension(string extension)
        => extension.ToLowerInvariant() switch
        {
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".odt" => "application/vnd.oasis.opendocument.text",
            ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
            ".odp" => "application/vnd.oasis.opendocument.presentation",
            ".doc" => "application/msword",
            ".xls" => "application/vnd.ms-excel",
            ".ppt" => "application/vnd.ms-powerpoint",
            ".epub" => "application/epub+zip",
            ".md" or ".markdown" => "text/markdown",

            // Source code and configuration are text. `text/plain` is the interoperable type; a
            // language-specific `text/x-…` is not something a vision/audio/document endpoint can be expected
            // to know. The bytes are still checked for text-ness before this hint is accepted.
            ".cs" or ".fs" or ".fsx" or ".vb" or ".java" or ".kt" or ".kts" or ".scala" or ".groovy"
                or ".py" or ".rb" or ".go" or ".rs" or ".c" or ".h" or ".cc" or ".cpp" or ".cxx" or ".hpp"
                or ".m" or ".mm" or ".swift" or ".php" or ".pl" or ".lua" or ".r" or ".jl" or ".dart"
                or ".js" or ".jsx" or ".ts" or ".tsx" or ".vue" or ".svelte"
                or ".css" or ".scss" or ".sass" or ".less"
                or ".sql" or ".sh" or ".bash" or ".zsh" or ".ps1" or ".bat" or ".cmd"
                or ".yaml" or ".yml" or ".toml" or ".ini" or ".cfg" or ".conf" or ".log"
                or ".tex" or ".proto" or ".graphql" or ".gql"
                => "text/plain",

            _ => MediaTypeOf(FormatOfExtension(extension)),
        };

    internal static bool IsAnimated(AgentMediaFormat format, ReadOnlySpan<byte> bytes)
    {
        if (format == AgentMediaFormat.Webp && bytes.Length >= 21 && AsciiAt(bytes, 12, "VP8X"))
            return (bytes[20] & 0x02) != 0;

        if (format == AgentMediaFormat.Gif && bytes.Length >= 20)
        {
            var haystack = bytes.Slice(0, Math.Min(bytes.Length, 4096));
            return IndexOfAscii(haystack, "NETSCAPE2.0") >= 0;
        }

        return false;
    }

    internal static string SupportedList(IEnumerable<string> mediaTypes)
    {
        var list = new List<string>();
        foreach (var mediaType in mediaTypes) list.Add(mediaType);
        return list.Count == 0 ? "none" : string.Join(", ", list);
    }

    internal static string SupportedKinds(IEnumerable<AgentMediaKind> kinds)
    {
        var list = new List<string>();
        foreach (var kind in kinds)
            if (kind != AgentMediaKind.Unknown)
                list.Add(Describe(kind));
        return list.Count == 0 ? "none" : string.Join(", ", list);
    }

    private static bool AsciiAt(ReadOnlySpan<byte> bytes, int offset, string text)
    {
        if (offset < 0 || offset + text.Length > bytes.Length) return false;
        for (var i = 0; i < text.Length; i++)
            if (bytes[offset + i] != (byte)text[i])
                return false;
        return true;
    }

    private static string Ascii(ReadOnlySpan<byte> bytes, int offset, int length)
    {
        if (offset + length > bytes.Length) return string.Empty;
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = (char)bytes[offset + i];
        return new string(chars);
    }

    private static int IndexOfAscii(ReadOnlySpan<byte> bytes, string text)
    {
        for (var i = 0; i + text.Length <= bytes.Length; i++)
            if (AsciiAt(bytes, i, text))
                return i;
        return -1;
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> bytes)
    {
        var offset = SkipBomAndWhitespace(bytes);
        if (offset >= bytes.Length || bytes[offset] != (byte)'<') return false;

        var window = bytes.Slice(offset, Math.Min(bytes.Length - offset, 4096));
        for (var i = 0; i + 4 <= window.Length; i++)
        {
            if (window[i] == (byte)'<' &&
                (window[i + 1] == (byte)'s' || window[i + 1] == (byte)'S') &&
                (window[i + 2] == (byte)'v' || window[i + 2] == (byte)'V') &&
                (window[i + 3] == (byte)'g' || window[i + 3] == (byte)'G'))
                return true;
        }

        return false;
    }

    private static bool LooksLikeHtml(ReadOnlySpan<byte> bytes)
    {
        var offset = SkipBomAndWhitespace(bytes);
        if (offset >= bytes.Length || bytes[offset] != (byte)'<') return false;
        return AsciiAtIgnoreCase(bytes, offset, "<!doctype html") || AsciiAtIgnoreCase(bytes, offset, "<html");
    }

    private static bool LooksLikeXml(ReadOnlySpan<byte> bytes)
    {
        var offset = SkipBomAndWhitespace(bytes);
        return offset < bytes.Length && AsciiAtIgnoreCase(bytes, offset, "<?xml") && !LooksLikeSvg(bytes);
    }

    private static int SkipBomAndWhitespace(ReadOnlySpan<byte> bytes)
    {
        var offset = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) offset = 3;
        while (offset < bytes.Length && (bytes[offset] == (byte)' ' || bytes[offset] == (byte)'\t' ||
                                         bytes[offset] == (byte)'\r' || bytes[offset] == (byte)'\n'))
            offset++;
        return offset;
    }

    private static bool AsciiAtIgnoreCase(ReadOnlySpan<byte> bytes, int offset, string text)
    {
        if (offset + text.Length > bytes.Length) return false;
        for (var i = 0; i < text.Length; i++)
        {
            var left = bytes[offset + i];
            var right = (byte)text[i];
            if (left >= 'A' && left <= 'Z') left = (byte)(left + 32);
            if (right >= 'A' && right <= 'Z') right = (byte)(right + 32);
            if (left != right) return false;
        }

        return true;
    }

    internal static bool LooksLikeText(ReadOnlySpan<byte> bytes)
    {
        var count = Math.Min(bytes.Length, 4096);
        for (var i = 0; i < count; i++)
            if (bytes[i] == 0) return false;

        try
        {
            var text = new UTF8Encoding(false, true).GetString(bytes.Slice(0, count).ToArray());
            var printable = 0;
            foreach (var c in text)
                if (!char.IsControl(c) || c is '\r' or '\n' or '\t') printable++;
            return text.Length == 0 || printable >= text.Length * 0.9;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
