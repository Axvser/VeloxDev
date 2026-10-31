using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace VeloxDev.AI;

/// <summary>
/// Builds the user message that carries multimodal input.
/// <para>
/// <b>Media belongs in a user message.</b> An OpenAI-compatible chat-completions tool message has a text
/// channel only: an image, audio or document returned from an <c>AIFunction</c> becomes a base64 JSON
/// string, not something the model can consume. A screenshot tool therefore returns its geometry report
/// (and, at most, a path); the host sends the payload back as a user turn through this type.
/// </para>
/// <para>
/// The text block comes first and the media follows in the caller's order, which is the order providers
/// expect and the order a transcript should show.
/// </para>
/// </summary>
public static class AgentMessage
{
    /// <summary>Builds a user message from text and already-validated media.</summary>
    /// <param name="text">The prompt; empty is allowed when at least one payload is present.</param>
    /// <param name="media">The payloads, in the order they should appear.</param>
    /// <exception cref="ArgumentException">There is neither text nor a payload.</exception>
    public static ChatMessage User(string? text, params IAgentMediaContent[] media)
        => User(text, (IEnumerable<IAgentMediaContent>)media);

    /// <summary>Builds a user message from text and already-validated media.</summary>
    /// <param name="text">The prompt; empty is allowed when at least one payload is present.</param>
    /// <param name="media">The payloads, in the order they should appear.</param>
    /// <exception cref="ArgumentException">There is neither text nor a payload.</exception>
    public static ChatMessage User(string? text, IEnumerable<IAgentMediaContent>? media)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(text)) contents.Add(new TextContent(text!));

        if (media is not null)
        {
            foreach (var item in media)
            {
                if (item is null) continue;
                contents.Add(item.ToContent());
            }
        }

        if (contents.Count == 0)
            throw new ArgumentException("A user message needs text or at least one media payload.", nameof(text));

        return new ChatMessage(ChatRole.User, contents);
    }

    /// <summary>
    /// Builds a user message from raw <see cref="AIContent"/> blocks — the escape hatch for content this
    /// layer does not wrap (a custom <see cref="AIContent"/>, a <c>FunctionResultContent</c>, …).
    /// No validation or normalisation is applied.
    /// </summary>
    /// <param name="contents">The content blocks, in order.</param>
    /// <exception cref="ArgumentException">No content was supplied.</exception>
    public static ChatMessage FromContents(params AIContent[] contents)
    {
        if (contents is null || contents.Length == 0)
            throw new ArgumentException("At least one content block is required.", nameof(contents));

        return new ChatMessage(ChatRole.User, contents);
    }

    /// <summary>Validates the media synchronously and builds the user message.</summary>
    /// <param name="text">The prompt; empty is allowed when at least one payload is present.</param>
    /// <param name="media">The payloads, in the order they should appear.</param>
    /// <param name="options">The limits; <see langword="null"/> uses the shipped defaults.</param>
    /// <exception cref="AgentMediaException">A payload cannot be sent as-is and no synchronous conversion is possible.</exception>
    public static ChatMessage CreateUser(
        string? text, IEnumerable<AgentMedia>? media, AgentMediaOptions? options = null)
    {
        var validated = new List<IAgentMediaContent>();
        if (media is not null)
            foreach (var item in media)
                if (item is not null)
                    validated.Add(item.Normalize(options));

        return User(text, validated);
    }

    /// <summary>Validates, converts or shrinks the media, then builds the user message.</summary>
    /// <param name="text">The prompt; empty is allowed when at least one payload is present.</param>
    /// <param name="media">The payloads, in the order they should appear.</param>
    /// <param name="options">The limits and transcoder; <see langword="null"/> uses the shipped defaults.</param>
    /// <param name="cancellationToken">Cancels any conversion.</param>
    /// <exception cref="AgentMediaException">A payload cannot be made acceptable.</exception>
    public static async Task<ChatMessage> CreateUserAsync(
        string? text,
        IEnumerable<AgentMedia>? media,
        AgentMediaOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var validated = new List<IAgentMediaContent>();
        if (media is not null)
        {
            foreach (var item in media)
            {
                if (item is null) continue;
                validated.Add(await item.NormalizeAsync(options, cancellationToken).ConfigureAwait(false));
            }
        }

        return User(text, validated);
    }
}
