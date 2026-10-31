using System;
using System.Threading;
using System.Threading.Tasks;

namespace VeloxDev.AI;

/// <summary>Why a payload is being handed to a transcoder.</summary>
public enum AgentMediaTranscodeReason
{
    /// <summary>The media kind is not in <see cref="AgentMediaOptions.AllowedKinds"/>.</summary>
    UnsupportedKind,

    /// <summary>The media type is not in <see cref="AgentMediaOptions.AllowedMediaTypes"/>.</summary>
    UnsupportedMediaType,

    /// <summary>An image was declared but its container is not one this layer recognises.</summary>
    UnrecognizedFormat,

    /// <summary>The raw bytes exceed the kind's limit.</summary>
    OversizeBytes,

    /// <summary>An image exceeds <see cref="AgentMediaOptions.MaxWidth"/> or <see cref="AgentMediaOptions.MaxHeight"/>.</summary>
    OversizeDimensions,

    /// <summary>An image is animated and animation is not allowed.</summary>
    Animated,
}

/// <summary>Everything a transcoder needs to decide how to re-encode.</summary>
public sealed class AgentMediaTranscodeRequest
{
    internal AgentMediaTranscodeRequest(
        AgentMediaTranscodeReason reason,
        AgentMediaKind targetKind,
        string targetMediaType,
        int maxWidth,
        int maxHeight,
        long maxBytes,
        int quality)
    {
        Reason = reason;
        TargetKind = targetKind;
        TargetMediaType = targetMediaType;
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        MaxBytes = maxBytes;
        Quality = quality;
    }

    /// <summary>Why the transcode was requested.</summary>
    public AgentMediaTranscodeReason Reason { get; }

    /// <summary>The kind the result should have.</summary>
    public AgentMediaKind TargetKind { get; }

    /// <summary>The media type the result should use, e.g. <c>image/png</c> or <c>audio/mpeg</c>.</summary>
    public string TargetMediaType { get; }

    /// <summary>The result must be at most this wide, in pixels (images only).</summary>
    public int MaxWidth { get; }

    /// <summary>The result must be at most this tall, in pixels (images only).</summary>
    public int MaxHeight { get; }

    /// <summary>The result must be at most this many raw bytes; zero means no byte target.</summary>
    public long MaxBytes { get; }

    /// <summary>Quality hint for lossy targets, 1–100.</summary>
    public int Quality { get; }
}

/// <summary>
/// Converts or shrinks media that the caller's <see cref="AgentMediaOptions"/> cannot send as-is.
/// <para>
/// <b>The library carries no media codec on purpose.</b> A platform-neutral package that took native image,
/// audio or video dependencies would force them on every consumer; hosts already have their own (WPF image
/// APIs, SkiaSharp, MAUI media, ffmpeg). The conversion belongs to the host and this interface is the seam.
/// A host registers one through <see cref="AgentMediaOptions.Transcoder"/>;
/// <see cref="DelegateAgentMediaTranscoder"/> wraps a lambda.
/// </para>
/// <para>
/// The result is validated again by the caller: its kind and media type must be allowed, its bytes must fit
/// the target kind's limit, and an image must honour the requested dimensions and animation policy.
/// Returning the source unchanged is therefore not a way to opt out.
/// </para>
/// </summary>
public interface IAgentMediaTranscoder
{
    /// <summary>Produces a payload that satisfies <paramref name="request"/>.</summary>
    /// <param name="source">The payload that needs converting.</param>
    /// <param name="request">The target kind/media type and the limits the result must meet.</param>
    /// <param name="cancellationToken">Cancels the conversion.</param>
    /// <returns>The converted payload.</returns>
    Task<AgentMedia> TranscodeAsync(
        AgentMedia source,
        AgentMediaTranscodeRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Wraps a delegate as an <see cref="IAgentMediaTranscoder"/>.</summary>
public sealed class DelegateAgentMediaTranscoder : IAgentMediaTranscoder
{
    private readonly Func<AgentMedia, AgentMediaTranscodeRequest, CancellationToken, Task<AgentMedia>> _transcode;

    /// <summary>Creates the adapter.</summary>
    /// <param name="transcode">The conversion. It receives the source, the request, and the cancellation token.</param>
    public DelegateAgentMediaTranscoder(
        Func<AgentMedia, AgentMediaTranscodeRequest, CancellationToken, Task<AgentMedia>> transcode)
        => _transcode = transcode ?? throw new ArgumentNullException(nameof(transcode));

    /// <inheritdoc />
    public Task<AgentMedia> TranscodeAsync(
        AgentMedia source,
        AgentMediaTranscodeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (request is null) throw new ArgumentNullException(nameof(request));
        return _transcode(source, request, cancellationToken);
    }
}
