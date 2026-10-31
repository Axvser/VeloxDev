using System;
using System.Collections.Generic;

namespace VeloxDev.AI;

/// <summary>
/// The limits and policies applied before multimodal content is put into a user message.
/// <para>
/// The defaults are the provider-common subset: images PNG/JPEG/WebP/GIF, audio MP3/WAV/M4A/Ogg/FLAC/AAC,
/// video MP4/WebM/QuickTime/AVI, documents PDF/RTF/Office/OpenDocument, text/plain, HTML, CSV, JSON and XML.
/// BMP/TIFF/AVIF/HEIC/SVG and arbitrary binary stay opt-in because providers commonly refuse them.
/// </para>
/// <para>
/// <b>Base64 inflates the payload by about a third.</b> The byte limits are measured on raw bytes; the
/// request body is larger. A host sending many frames or long videos should lower the per-kind limit rather
/// than rely on the global fallback.
/// </para>
/// </summary>
public sealed class AgentMediaOptions
{
    /// <summary>A fresh set of the shipped defaults.</summary>
    public static AgentMediaOptions CreateDefault() => new();

    /// <summary>
    /// Defaults sized for vision cost rather than only for provider acceptance: 1280×720 for images, the
    /// size the cost guidance recommends downscaling to. Pair it with a transcoder, or a larger frame is
    /// rejected instead of being shrunk.
    /// </summary>
    public static AgentMediaOptions CreateVisionDefault()
    {
        var options = new AgentMediaOptions
        {
            MaxWidth = 1280,
            MaxHeight = 720,
        };
        return options;
    }

    /// <summary>The media kinds that may be sent. Binary is opt-in.</summary>
    public ISet<AgentMediaKind> AllowedKinds { get; } = new HashSet<AgentMediaKind>
    {
        AgentMediaKind.Image,
        AgentMediaKind.Audio,
        AgentMediaKind.Video,
        AgentMediaKind.Document,
        AgentMediaKind.Text,
    };

    /// <summary>
    /// The exact media types that may be sent. <see langword="null"/> means every type of an allowed kind;
    /// the default set is the provider-common subset. This is the fine-grained switch after
    /// <see cref="AllowedKinds"/>.
    /// </summary>
    public ISet<string>? AllowedMediaTypes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp",
        "image/gif",

        "audio/mpeg",
        "audio/wav",
        "audio/mp4",
        "audio/ogg",
        "audio/flac",
        "audio/aac",

        "video/mp4",
        "video/webm",
        "video/quicktime",
        "video/x-msvideo",

        "application/pdf",
        "application/rtf",
        "application/msword",
        "application/vnd.ms-excel",
        "application/vnd.ms-powerpoint",
        "application/epub+zip",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.oasis.opendocument.text",
        "application/vnd.oasis.opendocument.spreadsheet",
        "application/vnd.oasis.opendocument.presentation",

        "text/plain",
        "text/markdown",
        "text/html",
        "text/csv",
        "application/json",
        "application/xml",
        "application/xhtml+xml",
    };

    /// <summary>The fallback byte limit for a kind without an entry in <see cref="MaxBytesByKind"/>.</summary>
    public long MaxBytes { get; set; } = 25L * 1024 * 1024;

    /// <summary>Per-kind limits; a video may be larger than a screenshot.</summary>
    public IDictionary<AgentMediaKind, long> MaxBytesByKind { get; } = new Dictionary<AgentMediaKind, long>
    {
        [AgentMediaKind.Image] = 10L * 1024 * 1024,
        [AgentMediaKind.Audio] = 25L * 1024 * 1024,
        [AgentMediaKind.Video] = 100L * 1024 * 1024,
        [AgentMediaKind.Document] = 25L * 1024 * 1024,
        [AgentMediaKind.Text] = 2L * 1024 * 1024,
        [AgentMediaKind.Binary] = 10L * 1024 * 1024,
    };

    /// <summary>The largest accepted image width, in pixels. Default <c>4096</c>.</summary>
    public int MaxWidth { get; set; } = 4096;

    /// <summary>The largest accepted image height, in pixels. Default <c>4096</c>.</summary>
    public int MaxHeight { get; set; } = 4096;

    /// <summary>
    /// When set, a declared media type that contradicts the bytes is rejected instead of corrected.
    /// The default is <see langword="false"/> and the bytes win.
    /// </summary>
    public bool StrictMediaType { get; set; }

    /// <summary>Whether an animated GIF/WebP is accepted. Default <see langword="false"/>.</summary>
    public bool AllowAnimated { get; set; }

    /// <summary>
    /// The codec used when a payload must be converted or shrunk. Without one, such a payload is rejected
    /// with <see cref="AgentMediaError.TranscoderRequired"/>; the library itself carries no pixel/audio/video
    /// codec so that it stays usable on every platform.
    /// </summary>
    public IAgentMediaTranscoder? Transcoder { get; set; }

    /// <summary>
    /// The image container a transcoder should produce when the source image format is not itself a good
    /// target. Default PNG. A JPEG source that only exceeds the byte limit is re-encoded as JPEG instead.
    /// </summary>
    public AgentMediaFormat TranscodeFormat { get; set; } = AgentMediaFormat.Png;

    /// <summary>
    /// The media type a non-image payload should be converted to. <see langword="null"/> means re-encode in
    /// the source type (a host codec can lower a bitrate or flatten a document); a kind that is not allowed
    /// needs an explicit target, or it cannot be transcoded at all.
    /// </summary>
    public string? TranscodeMediaType { get; set; }

    /// <summary>The quality hint passed to the transcoder for lossy output, 1–100. Default <c>85</c>.</summary>
    public int TranscodeQuality { get; set; } = 85;

    internal long MaxBytesFor(AgentMediaKind kind)
        => MaxBytesByKind.TryGetValue(kind, out var limit) ? limit : MaxBytes;

    internal bool IsMediaTypeAllowed(string mediaType)
        => AllowedMediaTypes is null || AllowedMediaTypes.Contains(mediaType);

    internal void Validate()
    {
        if (MaxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(MaxBytes), "MaxBytes must be positive.");
        if (MaxWidth <= 0) throw new ArgumentOutOfRangeException(nameof(MaxWidth), "MaxWidth must be positive.");
        if (MaxHeight <= 0) throw new ArgumentOutOfRangeException(nameof(MaxHeight), "MaxHeight must be positive.");
        if (TranscodeQuality is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(TranscodeQuality), "TranscodeQuality must be between 1 and 100.");
        if (TranscodeFormat == AgentMediaFormat.Unknown)
            throw new ArgumentOutOfRangeException(nameof(TranscodeFormat), "A transcode target format is required.");
        if (AllowedKinds.Count == 0)
            throw new ArgumentException("At least one media kind must be allowed.", nameof(AllowedKinds));

        foreach (var pair in MaxBytesByKind)
        {
            if (pair.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(MaxBytesByKind), $"The limit for {pair.Key} must be positive.");
        }
    }
}
