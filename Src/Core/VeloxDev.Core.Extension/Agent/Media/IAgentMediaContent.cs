using Microsoft.Extensions.AI;

namespace VeloxDev.AI;

/// <summary>
/// Something a user message can carry as media: bytes (<see cref="AgentMedia"/>) or a remote reference
/// (<see cref="AgentMediaReference"/>).
/// <para>
/// The message builder accepts this rather than only bytes because a provider that can fetch a URL does not
/// need the host to download it first, and the transcript still has a name and a media type to show.
/// </para>
/// </summary>
public interface IAgentMediaContent
{
    /// <summary>The broad class of the payload.</summary>
    AgentMediaKind Kind { get; }

    /// <summary>The canonical media type, e.g. <c>image/png</c>, <c>audio/mpeg</c>, <c>application/pdf</c>.</summary>
    string MediaType { get; }

    /// <summary>The display name, when the caller supplied one.</summary>
    string? Name { get; }

    /// <summary>Turns this payload into the content block the agent framework sends.</summary>
    AIContent ToContent();
}
