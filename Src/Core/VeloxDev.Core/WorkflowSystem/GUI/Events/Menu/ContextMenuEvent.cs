using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A host reporting what its context menu just did, so the surface can keep its own state in step.
///
/// Menus are the platform's own object — they are positioned by it, dismissed by it, and cannot be observed from
/// Core. So the host says what happened here, and the surface learns it the same way on every platform.
/// </summary>
/// <remarks>
/// Reporting <see cref="ContextMenuPhase.Opened"/> suspends the hover, so the pointer travelling onto the menu
/// does not clear the very selection the menu acts on; <see cref="ContextMenuPhase.Closed"/> releases it. A host
/// that reports both no longer has to keep <see cref="LinkInteraction.IsSuspended"/> itself.
/// </remarks>
[AgentContext(AgentLanguages.Chinese, "宿主报回来的一次右键菜单开合，用来同步挂起状态")]
[AgentContext(AgentLanguages.English, "One context-menu open/close reported back by the host, to keep the suspended state in step")]
public sealed record ContextMenuEvent
{
    /// <summary>Creates the report.</summary>
    /// <param name="phase">Whether the menu opened or closed.</param>
    /// <param name="position">Where it opened — ignored when closing.</param>
    /// <param name="link">
    /// The link the menu is about, or <see langword="null"/> for a menu on empty canvas.
    /// </param>
    public ContextMenuEvent(ContextMenuPhase phase, Anchor position, IWorkflowLinkViewModel? link = null)
    {
        Phase = phase;
        Position = position;
        Link = link;
    }

    /// <summary>Whether the menu opened or closed.</summary>
    public ContextMenuPhase Phase { get; init; }

    /// <summary>Where the menu opened, in the links' own coordinate space. Meaningless when closing.</summary>
    public Anchor Position { get; init; }

    /// <summary>The link the menu is about, or <see langword="null"/> when it is the canvas' own menu.</summary>
    public IWorkflowLinkViewModel? Link { get; init; }
}
