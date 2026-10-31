using System;

namespace VeloxDev.AI;

/// <summary>What an <see cref="AgentMediaException"/> is about.</summary>
public enum AgentMediaError
{
    /// <summary>The payload had no bytes.</summary>
    Empty,

    /// <summary>An image was claimed but the bytes are not a recognised image container.</summary>
    UnknownFormat,

    /// <summary>The media kind is not in <see cref="AgentMediaOptions.AllowedKinds"/>.</summary>
    UnsupportedKind,

    /// <summary>The media type is not in <see cref="AgentMediaOptions.AllowedMediaTypes"/>.</summary>
    UnsupportedMediaType,

    /// <summary>The declared media type contradicts the bytes and strict checking is on.</summary>
    MediaTypeMismatch,

    /// <summary>The payload is over the configured byte limit for its kind.</summary>
    TooLarge,

    /// <summary>An image is over a configured dimension limit.</summary>
    TooWide,

    /// <summary>An image is animated and animation is not allowed.</summary>
    Animated,

    /// <summary>A data URI was malformed or did not carry the declared media.</summary>
    InvalidDataUri,

    /// <summary>A remote media URL was malformed.</summary>
    InvalidUrl,

    /// <summary>The payload needs a transcode and no <see cref="IAgentMediaTranscoder"/> was supplied.</summary>
    TranscoderRequired,

    /// <summary>The supplied transcoder threw.</summary>
    TranscodeFailed,

    /// <summary>The supplied transcoder returned a payload that still breaks the configured limits.</summary>
    InvalidTranscoderResult,
}

/// <summary>
/// A rejected media payload, with an error the caller can branch on and a message that says what to do next.
/// <para>
/// The same bytes can be valid for one host's limits and invalid for another's; a host that wants to fall
/// back to a text-only turn needs to tell "too large" from "not an image" without parsing the message.
/// </para>
/// </summary>
public sealed class AgentMediaException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="error">What went wrong.</param>
    /// <param name="message">A message that names the payload and the next step.</param>
    /// <param name="innerException">The transcoder's failure, when there was one.</param>
    public AgentMediaException(AgentMediaError error, string message, Exception? innerException = null)
        : base(message, innerException)
        => Error = error;

    /// <summary>What went wrong.</summary>
    public AgentMediaError Error { get; }
}
