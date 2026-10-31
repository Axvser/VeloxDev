using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The surface half of a node drag-in: shows, moves and removes the draft node while Core owns the gesture.
/// </summary>
/// <typeparam name="TRenderContext">
/// The framework object the preview is drawn in — a decorator layer, an items panel, or the parent container.
/// Core treats it as opaque and passes back exactly the instance the surface supplied, so a platform with a
/// decorator and one with only a parent container can both implement this without Core knowing which is which.
/// </typeparam>
/// <remarks>
/// <para>
/// Core decides <b>when</b> the preview changes: it activates on <see cref="WorkflowNodeDrop.Begin{TContext}"/>,
/// follows the pointer's world position on every move, and calls <see cref="End"/> once — either because the
/// pointer was released over the canvas (the node was created) or because it left the surface (the drag was
/// cancelled). The surface decides <b>how</b> that looks; it is free to draw a full node card, a ghost, or only
/// an outline, and to adjust the node (centering, snapping) before drawing.
/// </para>
/// <para>
/// A draft node is not in the tree yet. The node handed to these methods already carries the pointer position
/// in <see cref="IWorkflowNodeViewModel.Anchor"/> when <see cref="Move"/> is called; whatever it carries when
/// the gesture ends is what gets created.
/// </para>
/// </remarks>
/// <seealso cref="WorkflowNodeDrop"/>
[AgentContext(AgentLanguages.Chinese, "节点拖入的画布侧契约：接收临时节点与框架自己的渲染上下文（装饰层/容器），负责预览的创建、移动与移除")]
[AgentContext(AgentLanguages.English, "Surface-side contract for dragging a node in: receives the draft node and the framework's own render context (decorator layer/container) and owns the preview's create/move/remove")]
public interface IWorkflowNodeDropSurface<TRenderContext>
{
    /// <summary>Called once when the drag starts, after the draft's anchor has been set to the initial pointer.</summary>
    /// <param name="node">The draft node, not yet part of the tree.</param>
    /// <param name="context">The rendering context this surface was begun with.</param>
    void Begin(IWorkflowNodeViewModel node, TRenderContext context);

    /// <summary>Called on every pointer move; <paramref name="node"/> already carries the new world anchor.</summary>
    /// <param name="node">The draft node.</param>
    /// <param name="context">The rendering context this surface was begun with.</param>
    void Move(IWorkflowNodeViewModel node, TRenderContext context);

    /// <summary>
    /// Called once when the gesture ends. <paramref name="committed"/> is <see langword="true"/> when the node
    /// was added to the tree (released over the canvas) and <see langword="false"/> when the drag was cancelled
    /// (the pointer left the surface, the host cleared it, or another draft replaced it).
    /// </summary>
    /// <param name="node">The draft node.</param>
    /// <param name="context">The rendering context this surface was begun with.</param>
    /// <param name="committed">Whether the node was created.</param>
    void End(IWorkflowNodeViewModel node, TRenderContext context, bool committed);
}
