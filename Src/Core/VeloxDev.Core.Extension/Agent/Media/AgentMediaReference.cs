using System;
using System.IO;
using Microsoft.Extensions.AI;

namespace VeloxDev.AI;

/// <summary>
/// A remote payload the provider can fetch — <see cref="UriContent"/> rather than <see cref="DataContent"/>.
/// <para>
/// Use this when the bytes already live at an HTTP(S) URL and the provider is allowed to fetch them. A
/// <c>file:</c> URI is rejected: a provider cannot read the caller's disk, and pretending otherwise moves a
/// local path into a request that will fail at the endpoint.
/// </para>
/// <para>
/// No bytes are downloaded here, so dimensions, duration and size cannot be checked. The URL's last path
/// segment is the display name unless one is supplied.
/// </para>
/// </summary>
public sealed class AgentMediaReference : IAgentMediaContent
{
    private AgentMediaReference(Uri uri, AgentMediaKind kind, string mediaType, string? name)
    {
        Uri = uri;
        Kind = kind;
        MediaType = mediaType;
        Name = name;
    }

    /// <summary>The absolute HTTP(S) location.</summary>
    public Uri Uri { get; }

    /// <inheritdoc />
    public AgentMediaKind Kind { get; }

    /// <inheritdoc />
    public string MediaType { get; }

    /// <inheritdoc />
    public string? Name { get; }

    /// <summary>
    /// Creates a reference to a remote payload.
    /// </summary>
    /// <param name="url">An absolute <c>http</c> or <c>https</c> URL.</param>
    /// <param name="mediaType">
    /// The media type. When omitted it is inferred from the URL's extension; an unknown extension is
    /// rejected rather than sent as <c>application/octet-stream</c>.
    /// </param>
    /// <param name="name">A display name; defaults to the URL's file name.</param>
    /// <exception cref="AgentMediaException">The URL is not HTTP(S) or the media type cannot be established.</exception>
    public static AgentMediaReference FromUrl(string url, string? mediaType = null, string? name = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new AgentMediaException(AgentMediaError.InvalidUrl, $"'{url}' is not an absolute URL.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new AgentMediaException(
                AgentMediaError.UnsupportedKind,
                $"A remote payload must use http or https; got '{uri.Scheme}'. Read the bytes and use AgentMedia.FromBytes instead.");

        var canonical = AgentMediaFormats.CanonicalMediaType(mediaType);
        if (canonical is null)
        {
            var extension = Path.GetExtension(uri.AbsolutePath);
            canonical = AgentMediaFormats.MediaTypeFromExtension(extension);
        }

        if (canonical is null)
            throw new AgentMediaException(
                AgentMediaError.UnsupportedMediaType,
                "The URL does not name a known media type. Pass an explicit mediaType such as image/png or application/pdf.");

        var kind = AgentMediaFormats.KindOfMediaType(canonical);
        if (kind == AgentMediaKind.Unknown)
            throw new AgentMediaException(
                AgentMediaError.UnsupportedMediaType,
                $"'{canonical}' cannot be classified. Pass a media type with a known top level (image/audio/video/text).");

        var display = name;
        if (string.IsNullOrWhiteSpace(display))
        {
            display = Path.GetFileName(uri.LocalPath);
            if (string.IsNullOrWhiteSpace(display)) display = null;
        }

        return new AgentMediaReference(uri, kind, canonical, display);
    }

    /// <inheritdoc />
    public AIContent ToContent() => new UriContent(Uri, MediaType);
}
