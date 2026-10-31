using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace VeloxDev.AI;

/// <summary>
/// A multimodal payload the Agent may send, validated against its actual bytes rather than a file extension.
/// <para>
/// The Agent Framework carries local media as a <see cref="DataContent"/> and remote media as a
/// <see cref="UriContent"/> inside a user <see cref="ChatMessage"/>. This type is the step before that: it
/// sniffs the container, classifies the kind, canonicalises the media type, reads image dimensions, and
/// applies the caller's limits. Images, audio, video, documents, text and (opt-in) arbitrary binary share
/// the same path.
/// </para>
/// <para>
/// <b>Bytes win over the declared media type.</b> A picker that says <c>image/jpeg</c> over PNG bytes is
/// corrected, not sent; set <see cref="AgentMediaOptions.StrictMediaType"/> to reject the pair instead.
/// A claim of an image with no image magic is rejected at construction: a provider cannot classify it.
/// </para>
/// <para>
/// <b>A tool result cannot carry media on OpenAI-compatible chat completions.</b> The tool message has a text
/// channel only; media returned by an <c>AIFunction</c> arrives as base64 JSON, not as pixels or sound.
/// Media belongs in the user message, which is what <see cref="AgentMessage"/> builds.
/// </para>
/// </summary>
public sealed class AgentMedia : IAgentMediaContent
{
    private readonly ReadOnlyMemory<byte> mediaBytes;

    private AgentMedia(
        ReadOnlyMemory<byte> bytes,
        AgentMediaFormat format,
        AgentMediaKind kind,
        string mediaType,
        string? name,
        string? declaredMediaType,
        int? width,
        int? height)
    {
        mediaBytes = bytes;
        Format = format;
        Kind = kind;
        MediaType = mediaType;
        Name = name;
        DeclaredMediaType = declaredMediaType;
        Width = width;
        Height = height;
    }

    /// <summary>The raw container bytes.</summary>
    public ReadOnlyMemory<byte> Bytes => mediaBytes;

    /// <summary>The recognised container, or <see cref="AgentMediaFormat.Unknown"/> for a generic binary blob.</summary>
    public AgentMediaFormat Format { get; }

    /// <summary>The broad class of the payload.</summary>
    public AgentMediaKind Kind { get; }

    /// <summary>The canonical media type taken from the bytes (or from the declaration when there is no magic).</summary>
    public string MediaType { get; }

    /// <summary>The display name, when the caller supplied one.</summary>
    public string? Name { get; }

    /// <summary>
    /// The media type the caller claimed, canonicalised, when one was supplied. Kept so a strict check can
    /// report the contradiction rather than silently correcting it.
    /// </summary>
    public string? DeclaredMediaType { get; }

    /// <summary>The width read from an image header, when the header exposes one.</summary>
    public int? Width { get; }

    /// <summary>The height read from an image header, when the header exposes one.</summary>
    public int? Height { get; }

    /// <summary>The raw byte count; base64 expansion is not included.</summary>
    public int ByteCount => mediaBytes.Length;

    /// <summary>Whether the header says this image is an animated GIF or WebP.</summary>
    public bool IsAnimated
        => Kind == AgentMediaKind.Image && AgentMediaFormats.IsAnimated(Format, mediaBytes.Span);

    /// <summary>
    /// Creates a payload from bytes, sniffing the container and canonicalising the media type.
    /// </summary>
    /// <param name="bytes">The bytes. The constructor copies them, so later mutation cannot change the payload.</param>
    /// <param name="mediaType">The caller's claim, if any; the bytes win when the two disagree.</param>
    /// <param name="name">A display name; providers do not require one.</param>
    /// <exception cref="AgentMediaException">The bytes are empty, or an image was claimed but the bytes are not an image.</exception>
    public static AgentMedia FromBytes(ReadOnlyMemory<byte> bytes, string? mediaType = null, string? name = null)
    {
        if (bytes.IsEmpty)
            throw new AgentMediaException(AgentMediaError.Empty, "A media payload needs at least one byte.");

        var copy = bytes.ToArray();
        var declared = AgentMediaFormats.CanonicalMediaType(mediaType);
        var declaredKind = AgentMediaFormats.KindOfMediaType(declared);
        var descriptor = AgentMediaFormats.Detect(copy, declared);

        if (declaredKind == AgentMediaKind.Image && descriptor.Kind != AgentMediaKind.Image)
        {
            throw new AgentMediaException(
                AgentMediaError.UnknownFormat,
                $"An image was declared ('{declared}'), but the bytes are not a recognised image. "
                + $"First bytes: {Hex(copy)}.");
        }

        var width = 0;
        var height = 0;
        if (descriptor.Kind == AgentMediaKind.Image)
            ImageHeaderReader.TryRead(copy, descriptor.Format, out width, out height);

        return new AgentMedia(
            copy,
            descriptor.Format,
            descriptor.Kind,
            descriptor.MediaType,
            name,
            declared,
            width > 0 ? width : null,
            height > 0 ? height : null);
    }

    /// <summary>Reads a payload from a file; the file name and extension are hints, the bytes win.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="mediaType">An explicit claim; omitted, the extension supplies a hint.</param>
    public static AgentMedia FromFile(string path, string? mediaType = null)
    {
        var bytes = File.ReadAllBytes(path);
        return FromBytes(bytes, ResolveFileHint(path, mediaType, bytes), Path.GetFileName(path));
    }

    /// <summary>Reads a payload from a file without blocking the caller's thread.</summary>
    /// <param name="path">The file to read.</param>
    /// <param name="mediaType">An explicit claim; omitted, the extension supplies a hint.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<AgentMedia> FromFileAsync(
        string path, string? mediaType = null, CancellationToken cancellationToken = default)
    {
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        using (var memory = new MemoryStream())
        {
            await stream.CopyToAsync(memory, 81920, cancellationToken).ConfigureAwait(false);
            bytes = memory.ToArray();
        }

        return FromBytes(bytes, ResolveFileHint(path, mediaType, bytes), Path.GetFileName(path));
    }

    /// <summary>Reads a payload from a stream, leaving the stream open.</summary>
    /// <param name="stream">The source stream.</param>
    /// <param name="mediaType">The caller's claim, if any; the bytes win.</param>
    /// <param name="name">A display name.</param>
    public static AgentMedia FromStream(Stream stream, string? mediaType = null, string? name = null)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            return FromBytes(memory.ToArray(), mediaType, name);
        }
    }

    /// <summary>Reads a payload from a stream without blocking the caller's thread, leaving the stream open.</summary>
    /// <param name="stream">The source stream.</param>
    /// <param name="mediaType">The caller's claim, if any; the bytes win.</param>
    /// <param name="name">A display name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async Task<AgentMedia> FromStreamAsync(
        Stream stream, string? mediaType = null, string? name = null, CancellationToken cancellationToken = default)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        using (var memory = new MemoryStream())
        {
            await stream.CopyToAsync(memory, 81920, cancellationToken).ConfigureAwait(false);
            return FromBytes(memory.ToArray(), mediaType, name);
        }
    }

    /// <summary>
    /// Parses a <c>data:</c> URI, both the base64 form and the percent-encoded form.
    /// </summary>
    /// <param name="dataUri">The URI, e.g. <c>data:image/png;base64,iVBOR…</c>.</param>
    /// <param name="name">A display name.</param>
    /// <exception cref="AgentMediaException">The URI is malformed.</exception>
    public static AgentMedia FromDataUri(string dataUri, string? name = null)
    {
        if (string.IsNullOrWhiteSpace(dataUri))
            throw new AgentMediaException(AgentMediaError.InvalidDataUri, "A data URI is required.");

        if (!dataUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            throw new AgentMediaException(AgentMediaError.InvalidDataUri, "The value is not a data URI.");

        var comma = dataUri.IndexOf(',');
        if (comma < 0)
            throw new AgentMediaException(AgentMediaError.InvalidDataUri, "The data URI has no payload separator (',').");

        var metadata = dataUri.Substring(5, comma - 5);
        var payload = dataUri.Substring(comma + 1);
        var parts = metadata.Split(';');
        var mediaType = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : null;
        var base64 = false;
        for (var i = 1; i < parts.Length; i++)
            if (string.Equals(parts[i], "base64", StringComparison.OrdinalIgnoreCase))
                base64 = true;

        byte[] bytes;
        try
        {
            bytes = base64 ? Convert.FromBase64String(payload) : PercentDecode(payload);
        }
        catch (FormatException ex)
        {
            throw new AgentMediaException(AgentMediaError.InvalidDataUri, "The data URI payload is invalid.", ex);
        }

        return FromBytes(bytes, mediaType, name);
    }

    /// <summary>Returns the raw bytes as a base64 string.</summary>
    public string ToBase64String() => Convert.ToBase64String(mediaBytes.ToArray());

    /// <summary>Returns the canonical <c>data:</c> URI a provider or host can use directly.</summary>
    public string ToDataUri() => "data:" + MediaType + ";base64," + ToBase64String();

    /// <summary>Builds the <see cref="DataContent"/> the Agent Framework sends.</summary>
    public DataContent ToDataContent()
    {
        var content = new DataContent(mediaBytes, MediaType);
        if (!string.IsNullOrWhiteSpace(Name)) content.Name = Name;
        return content;
    }

    /// <summary>
    /// Builds the content block the Agent Framework sends.
    /// <para>
    /// <b>Text is inlined as <see cref="TextContent"/>, not sent as a data file.</b> The OpenAI chat-completions
    /// adapter drops a <see cref="DataContent"/> whose media type is not image/audio/PDF, so a Markdown or
    /// source file sent that way reaches the model as an empty turn. Text content is understood by every
    /// provider, and the file name travels in a short header.
    /// </para>
    /// </summary>
    public TextContent ToTextContent()
    {
        var text = Encoding.UTF8.GetString(mediaBytes.ToArray());
        var header = string.IsNullOrWhiteSpace(Name) ? string.Empty : $"--- {Name} ({MediaType}) ---\n";
        return new TextContent(header + text);
    }

    /// <inheritdoc />
    public AIContent ToContent()
        => Kind == AgentMediaKind.Text ? ToTextContent() : ToDataContent();

    /// <summary>
    /// Validates this payload against <paramref name="options"/> without converting it.
    /// <para>
    /// Use this when the payload already satisfies the kind, type, size and image limits. When it needs a
    /// transcode, this method throws <see cref="AgentMediaError.TranscoderRequired"/> — call
    /// <see cref="NormalizeAsync"/> instead, because a codec is asynchronous on most hosts.
    /// </para>
    /// </summary>
    /// <param name="options">The limits; <see langword="null"/> uses the shipped defaults.</param>
    /// <returns>This payload, when it is already acceptable.</returns>
    /// <exception cref="AgentMediaException">The payload breaks a limit or the media type is contradicted under strict checking.</exception>
    public AgentMedia Normalize(AgentMediaOptions? options = null)
    {
        options ??= AgentMediaOptions.CreateDefault();
        options.Validate();
        ValidateDeclaredMediaType(options);

        var decision = Evaluate(options);
        if (!decision.NeedsTranscode) return this;

        if (options.Transcoder is not null)
            throw new AgentMediaException(
                AgentMediaError.TranscoderRequired,
                "This payload needs a transcode; call NormalizeAsync so the configured transcoder can run.");

        throw Reject(decision.Reason, options);
    }

    /// <summary>
    /// Validates, and converts or shrinks the payload through <see cref="AgentMediaOptions.Transcoder"/> when
    /// its kind, media type, bytes, image dimensions or animation are not acceptable as-is.
    /// </summary>
    /// <param name="options">The limits and the transcoder; <see langword="null"/> uses the shipped defaults.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>This payload when it is acceptable, otherwise the transcoder's validated result.</returns>
    /// <exception cref="AgentMediaException">The payload cannot be made acceptable.</exception>
    public async Task<AgentMedia> NormalizeAsync(
        AgentMediaOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= AgentMediaOptions.CreateDefault();
        options.Validate();
        ValidateDeclaredMediaType(options);

        var decision = Evaluate(options);
        if (!decision.NeedsTranscode) return this;

        var transcoder = options.Transcoder;
        if (transcoder is null) throw Reject(decision.Reason, options);

        var target = ChooseTarget(decision.Reason, options);
        var request = new AgentMediaTranscodeRequest(
            decision.Reason,
            target.Kind,
            target.MediaType,
            options.MaxWidth,
            options.MaxHeight,
            options.MaxBytesFor(target.Kind),
            options.TranscodeQuality);

        AgentMedia converted;
        try
        {
            converted = await transcoder.TranscodeAsync(this, request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AgentMediaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AgentMediaException(
                AgentMediaError.TranscodeFailed, $"The media transcoder failed: {ex.Message}", ex);
        }

        if (converted is null)
            throw new AgentMediaException(
                AgentMediaError.InvalidTranscoderResult, "The media transcoder returned null.");

        ValidateTranscoderResult(converted, options);

        if (string.IsNullOrWhiteSpace(converted.Name) && !string.IsNullOrWhiteSpace(Name))
            converted = converted.WithName(ReplaceExtension(Name!, converted.MediaType, converted.Format));

        return converted;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var detail = Kind == AgentMediaKind.Image && Width is int w && Height is int h ? $"{w}×{h}" : "—";
        return $"{Name ?? "(unnamed)"} · {MediaType} · {detail} · {ByteCount} B";
    }

    private AgentMedia WithName(string? name)
        => new(mediaBytes, Format, Kind, MediaType, name, DeclaredMediaType, Width, Height);

    /// <summary>
    /// The type hint a file supplies. An explicit caller value wins; an extension-derived text type is only
    /// kept when the bytes really look like text, so a binary file misnamed <c>.md</c> does not get sent as
    /// Markdown.
    /// </summary>
    private static string? ResolveFileHint(string path, string? mediaType, byte[] bytes)
    {
        if (mediaType is not null) return mediaType;

        var hint = AgentMediaFormats.MediaTypeFromExtension(Path.GetExtension(path));
        if (hint is null) return null;

        var canonical = AgentMediaFormats.CanonicalMediaType(hint);
        if (AgentMediaFormats.KindOfMediaType(canonical) == AgentMediaKind.Text &&
            !AgentMediaFormats.LooksLikeText(bytes))
            return null;

        return hint;
    }

    private void ValidateDeclaredMediaType(AgentMediaOptions options)
    {
        if (!options.StrictMediaType) return;
        if (DeclaredMediaType is null) return;
        if (string.Equals(DeclaredMediaType, MediaType, StringComparison.Ordinal)) return;

        throw new AgentMediaException(
            AgentMediaError.MediaTypeMismatch,
            $"The declared media type is '{DeclaredMediaType}', but the bytes are '{MediaType}'. "
            + "Correct the claim or turn off StrictMediaType.");
    }

    private (bool NeedsTranscode, AgentMediaTranscodeReason Reason) Evaluate(AgentMediaOptions options)
    {
        if (!options.AllowedKinds.Contains(Kind))
            return (true, AgentMediaTranscodeReason.UnsupportedKind);

        if (!options.IsMediaTypeAllowed(MediaType))
            return (true, AgentMediaTranscodeReason.UnsupportedMediaType);

        // An image whose container could not be read from its bytes must not be sent as-is, even when the
        // declared media type is on the allowlist: no provider can be trusted to read an unknown container.
        if (Kind == AgentMediaKind.Image && Format == AgentMediaFormat.Unknown)
            return (true, AgentMediaTranscodeReason.UnrecognizedFormat);

        if (Kind == AgentMediaKind.Image && !options.AllowAnimated && IsAnimated)
            return (true, AgentMediaTranscodeReason.Animated);

        if (Kind == AgentMediaKind.Image &&
            ((Width is int width && width > options.MaxWidth) ||
             (Height is int height && height > options.MaxHeight)))
            return (true, AgentMediaTranscodeReason.OversizeDimensions);

        if (ByteCount > options.MaxBytesFor(Kind))
            return (true, AgentMediaTranscodeReason.OversizeBytes);

        return (false, default);
    }

    private AgentMediaException Reject(AgentMediaTranscodeReason reason, AgentMediaOptions options)
    {
        var name = string.IsNullOrWhiteSpace(Name) ? "The payload" : $"'{Name}'";
        return reason switch
        {
            AgentMediaTranscodeReason.UnsupportedKind => new AgentMediaException(
                AgentMediaError.UnsupportedKind,
                $"{name} is {AgentMediaFormats.Describe(Kind)}, which this host does not send. "
                + $"Allowed kinds: {AgentMediaFormats.SupportedKinds(options.AllowedKinds)}. "
                + (options.TranscodeMediaType is null
                    ? "Add the kind, or supply a transcoder and a TranscodeMediaType."
                    : "Supply an IAgentMediaTranscoder to convert it.")),
            AgentMediaTranscodeReason.UnsupportedMediaType => new AgentMediaException(
                AgentMediaError.UnsupportedMediaType,
                $"{name} is '{MediaType}', which this host does not send. "
                + $"Allowed types: {(options.AllowedMediaTypes is null ? "all" : AgentMediaFormats.SupportedList(options.AllowedMediaTypes))}. "
                + "Add the type, or supply an IAgentMediaTranscoder."),
            AgentMediaTranscodeReason.UnrecognizedFormat => new AgentMediaException(
                AgentMediaError.UnknownFormat,
                $"{name} declares '{MediaType}', but its bytes are not a container this layer recognises. "
                + "Supply an IAgentMediaTranscoder that can decode it, or convert it to PNG/JPEG/WebP first."),
            AgentMediaTranscodeReason.OversizeDimensions => new AgentMediaException(
                AgentMediaError.TooWide,
                $"{name} is {Width}×{Height} px, over the {options.MaxWidth}×{options.MaxHeight} limit. "
                + "Shrink it, or supply an IAgentMediaTranscoder that honours the request limits."),
            AgentMediaTranscodeReason.OversizeBytes => new AgentMediaException(
                AgentMediaError.TooLarge,
                $"{name} is {ByteCount} bytes, over the {options.MaxBytesFor(Kind)}-byte limit for "
                + $"{AgentMediaFormats.Describe(Kind)}. Re-encode it, or supply a transcoder that honours the limit."),
            AgentMediaTranscodeReason.Animated => new AgentMediaException(
                AgentMediaError.Animated,
                $"{name} is animated. Send a still frame, or supply an IAgentMediaTranscoder that flattens it."),
            _ => new AgentMediaException(AgentMediaError.UnsupportedMediaType, $"{name} cannot be sent as-is."),
        };
    }

    private (AgentMediaKind Kind, string MediaType) ChooseTarget(
        AgentMediaTranscodeReason reason, AgentMediaOptions options)
    {
        if (Kind != AgentMediaKind.Image)
        {
            var targetMediaType = options.TranscodeMediaType ?? MediaType;
            var targetKind = AgentMediaFormats.KindOfMediaType(targetMediaType);
            if (targetKind == AgentMediaKind.Unknown || !options.AllowedKinds.Contains(targetKind))
                throw new AgentMediaException(
                    AgentMediaError.UnsupportedKind,
                    $"The transcoder target '{targetMediaType}' is not an allowed kind. "
                    + $"Allowed kinds: {AgentMediaFormats.SupportedKinds(options.AllowedKinds)}.");
            if (!options.IsMediaTypeAllowed(targetMediaType))
                throw new AgentMediaException(
                    AgentMediaError.UnsupportedMediaType,
                    $"The transcoder target '{targetMediaType}' is not allowed either. "
                    + "Add it to AllowedMediaTypes or choose another target.");
            return (targetKind, targetMediaType);
        }

        // Animation has to be flattened, so the source format is not a good target for it.
        if (reason == AgentMediaTranscodeReason.Animated)
        {
            var flattened = FirstAllowed(options, "image/png", "image/jpeg", "image/webp");
            if (flattened is not null) return (AgentMediaKind.Image, flattened);
        }

        var target = Format switch
        {
            AgentMediaFormat.Jpeg => "image/jpeg",
            AgentMediaFormat.Webp when options.IsMediaTypeAllowed("image/webp") => "image/webp",
            _ => AgentMediaFormats.MediaTypeOf(options.TranscodeFormat),
        };

        if (target is not null && options.IsMediaTypeAllowed(target))
            return (AgentMediaKind.Image, target);

        var fallback = FirstAllowed(options, "image/png", "image/jpeg", "image/webp");
        if (fallback is not null) return (AgentMediaKind.Image, fallback);

        throw new AgentMediaException(
            AgentMediaError.UnsupportedMediaType,
            "No allowed image media type is left for the transcoder to target. Add image/png, image/jpeg or image/webp.");
    }

    private static string? FirstAllowed(AgentMediaOptions options, params string[] candidates)
    {
        foreach (var candidate in candidates)
            if (options.IsMediaTypeAllowed(candidate))
                return candidate;
        return null;
    }

    private static void ValidateTranscoderResult(AgentMedia converted, AgentMediaOptions options)
    {
        if (converted.Kind == AgentMediaKind.Unknown)
            throw new AgentMediaException(
                AgentMediaError.InvalidTranscoderResult, "The media transcoder returned an unclassified payload.");

        if (converted.Kind == AgentMediaKind.Image && converted.Format == AgentMediaFormat.Unknown)
            throw new AgentMediaException(
                AgentMediaError.InvalidTranscoderResult,
                "The media transcoder returned an image whose container is not recognised.");

        if (!options.AllowedKinds.Contains(converted.Kind))
            throw new AgentMediaException(
                AgentMediaError.InvalidTranscoderResult,
                $"The media transcoder returned {AgentMediaFormats.Describe(converted.Kind)}, which is not allowed.");

        if (!options.IsMediaTypeAllowed(converted.MediaType))
            throw new AgentMediaException(
                AgentMediaError.InvalidTranscoderResult,
                $"The media transcoder returned '{converted.MediaType}', which is not allowed.");

        if (converted.ByteCount > options.MaxBytesFor(converted.Kind))
            throw new AgentMediaException(
                AgentMediaError.InvalidTranscoderResult,
                $"The media transcoder returned {converted.ByteCount} bytes, over the "
                + $"{options.MaxBytesFor(converted.Kind)}-byte limit for {AgentMediaFormats.Describe(converted.Kind)}.");

        if (converted.Kind == AgentMediaKind.Image)
        {
            if ((converted.Width is int width && width > options.MaxWidth) ||
                (converted.Height is int height && height > options.MaxHeight))
                throw new AgentMediaException(
                    AgentMediaError.InvalidTranscoderResult,
                    $"The media transcoder returned {converted.Width}×{converted.Height} px, over the "
                    + $"{options.MaxWidth}×{options.MaxHeight} limit.");

            if (!options.AllowAnimated && converted.IsAnimated)
                throw new AgentMediaException(
                    AgentMediaError.InvalidTranscoderResult,
                    "The media transcoder returned an animated image while animation is not allowed.");
        }
    }

    private static string ReplaceExtension(string name, string mediaType, AgentMediaFormat format)
    {
        // The media type can be more specific than the container (a docx is a ZIP with a declared type),
        // so it wins for the display name.
        var extension = AgentMediaFormats.FileExtensionFor(mediaType, format);
        if (string.IsNullOrEmpty(extension)) return name;

        var dot = name.LastIndexOf('.');
        return dot > 0 && dot > name.LastIndexOf(Path.DirectorySeparatorChar)
            ? name.Substring(0, dot) + extension
            : name + extension;
    }

    private static byte[] PercentDecode(string value)
    {
        var bytes = new List<byte>(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '%')
            {
                if (i + 2 >= value.Length || !TryHex(value[i + 1], out var high) || !TryHex(value[i + 2], out var low))
                    throw new FormatException($"Malformed percent escape at offset {i}.");

                bytes.Add((byte)((high << 4) | low));
                i += 2;
                continue;
            }

            if (c <= 0x7F)
            {
                bytes.Add((byte)c);
                continue;
            }

            bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
        }

        return bytes.ToArray();
    }

    private static bool TryHex(char c, out int value)
    {
        if (c >= '0' && c <= '9') { value = c - '0'; return true; }
        if (c >= 'a' && c <= 'f') { value = c - 'a' + 10; return true; }
        if (c >= 'A' && c <= 'F') { value = c - 'A' + 10; return true; }
        value = 0;
        return false;
    }

    private static string Hex(byte[] bytes)
    {
        var count = Math.Min(bytes.Length, 12);
        var builder = new StringBuilder(count * 3);
        for (var i = 0; i < count; i++)
        {
            if (i > 0) builder.Append(' ');
            builder.Append(bytes[i].ToString("X2"));
        }
        if (bytes.Length > count) builder.Append(" …");
        return builder.ToString();
    }
}
