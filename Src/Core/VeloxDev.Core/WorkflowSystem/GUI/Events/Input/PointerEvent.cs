using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// A pointer event translated out of a GUI framework, positioned with a Core <see cref="Anchor"/>.
///
/// Adapters do not share a pointer type, so each one converts its native event into this record and hands it to
/// <see cref="LinkInteraction"/>; which link is under the pointer, and what that means for hover and selection,
/// is then decided once, in Core, identically on every platform.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "适配器翻译出来的一次指针事件，位置用 Core 的 Anchor 表示")]
[AgentContext(AgentLanguages.English, "One pointer event translated out of a GUI framework, positioned with a Core Anchor")]
public sealed record PointerEvent
{
    /// <summary>Creates a pointer event.</summary>
    /// <param name="phase">What the pointer did.</param>
    /// <param name="position">
    /// Where it happened, in the same coordinate space the links published their curves in
    /// (see <see cref="ILinkHitTestable"/>).
    /// </param>
    /// <param name="button">Which button, for <see cref="PointerPhase.Pressed"/> and <see cref="PointerPhase.Released"/>.</param>
    /// <param name="clickCount">Consecutive click count, for hosts that distinguish a double click.</param>
    public PointerEvent(PointerPhase phase, Anchor position, PointerButtonKind button = PointerButtonKind.None, int clickCount = 1)
    {
        Phase = phase;
        Position = position;
        Button = button;
        ClickCount = clickCount;
    }

    /// <summary>What the pointer did.</summary>
    public PointerPhase Phase { get; init; }

    /// <summary>Where it happened, in the space the curves were published in.</summary>
    public Anchor Position { get; init; }

    /// <summary>Which button, for a press or a release.</summary>
    public PointerButtonKind Button { get; init; }

    /// <summary>Consecutive click count.</summary>
    public int ClickCount { get; init; }
}
