using System;
using System.Globalization;
using Microsoft.Extensions.AI;

namespace VeloxDev.AI;

/// <summary>
/// What a user turn carried besides text — the display record of an image, not the image itself.
/// <para>
/// The transcript shows what the user attached without retaining the pixels: a long conversation would
/// otherwise keep every frame's bytes alive in the view model. The bytes the model actually received live in
/// the framework's session, exactly as the text does.
/// </para>
/// </summary>
public sealed class AgentAttachment
{
    /// <summary>Creates the record.</summary>
    /// <param name="name">The display name, when one is known.</param>
    /// <param name="mediaType">The canonical media type.</param>
    /// <param name="byteCount">The raw payload size, when it is known (remote references have none).</param>
    /// <param name="uri">The remote location, for a <see cref="UriContent"/> attachment.</param>
    public AgentAttachment(string? name, string mediaType, long? byteCount = null, string? uri = null)
    {
        Name = name;
        MediaType = mediaType ?? "application/octet-stream";
        ByteCount = byteCount;
        Uri = uri;
    }

    /// <summary>The display name, or <see langword="null"/> when the caller supplied none.</summary>
    public string? Name { get; }

    /// <summary>The canonical media type.</summary>
    public string MediaType { get; }

    /// <summary>The broad class of the payload, derived from <see cref="MediaType"/>.</summary>
    public AgentMediaKind Kind => AgentMediaFormats.KindOfMediaType(MediaType);

    /// <summary>The raw payload size, when it is known.</summary>
    public long? ByteCount { get; }

    /// <summary>The remote location, for a URL reference.</summary>
    public string? Uri { get; }

    /// <summary>A one-line description for a transcript: <c>frame.png (image/png, 79 KB)</c>.</summary>
    public string Describe()
    {
        var name = !string.IsNullOrWhiteSpace(Name)
            ? Name!
            : !string.IsNullOrWhiteSpace(Uri)
                ? Uri!
                : MediaType;

        return ByteCount is long bytes
            ? $"{name} ({MediaType}, {FormatBytes(bytes)})"
            : $"{name} ({MediaType})";
    }

    /// <summary>
    /// Reads the attachment out of the content block the framework carries. Text and non-media content
    /// produce nothing.
    /// </summary>
    internal static bool TryFromContent(AIContent? content, out AgentAttachment attachment)
    {
        switch (content)
        {
            case DataContent data:
                attachment = new AgentAttachment(data.Name, data.MediaType, data.Data.Length);
                return true;

            case UriContent uri:
                attachment = new AgentAttachment(
                    NameFromUri(uri.Uri), uri.MediaType, byteCount: null, uri.Uri.ToString());
                return true;

            default:
                attachment = null!;
                return false;
        }
    }

    private static string? NameFromUri(Uri source)
    {
        try
        {
            var name = System.IO.Path.GetFileName(source.LocalPath);
            return string.IsNullOrWhiteSpace(name) ? null : global::System.Uri.UnescapeDataString(name);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        if (bytes < 1024 * 1024)
            return (bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        return (bytes / (1024d * 1024d)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
    }
}
