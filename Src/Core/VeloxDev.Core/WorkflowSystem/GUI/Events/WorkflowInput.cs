using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Routes a GUI's pointer and keyboard input to the workflow components under it, and applies the few reactions the
/// framework itself owns. Adapters translate their platform's input into the standard arguments and hand it here;
/// hosts and component views subscribe on the components instead of re-deriving anything per platform.
/// </summary>
/// <remarks>
/// <para>
/// The adapter decides <b>what</b> was hit (its own gesture logic already knows) and passes it as
/// <see cref="WorkflowPointerEventArgs.Target"/>; this class decides <b>who hears about it</b> — the target first,
/// then its ancestors, each of them seeing the same argument instance and the same
/// <see cref="WorkflowEventHandle"/>. A handler therefore runs <b>before</b> the framework's own reaction, and can
/// suppress it (<see cref="WorkflowEventHandle.PreventDefault"/>) or keep the event from going further up
/// (<see cref="WorkflowEventHandle.StopPropagation"/>).
/// </para>
/// <para>
/// There is one instance per tree, created on first use and kept as long as the tree lives — the same shape the
/// per-component hubs used to have, so an adapter and a host still meet at one call.
/// </para>
/// </remarks>
/// <seealso cref="IWorkflowInputEvents"/>
public sealed class WorkflowInput
{
    // 一棵树一个：与树同寿，适配器与宿主都用这个调用取它。
    private static readonly ConditionalWeakTable<IWorkflowTreeViewModel, WorkflowInput> Instances = new();

    private readonly IWorkflowTreeViewModel tree;
    private IWorkflowViewModel? pointerTarget;

    /// <summary>Creates the input route for one tree.</summary>
    /// <param name="tree">The tree whose components receive input.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public WorkflowInput(IWorkflowTreeViewModel tree) => this.tree = tree ?? throw new ArgumentNullException(nameof(tree));

    /// <summary>
    /// The one input route for this tree, created on first use and kept as long as the tree lives.
    /// </summary>
    /// <param name="tree">The tree to route input for.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static WorkflowInput For(IWorkflowTreeViewModel tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));
        return Instances.GetValue(tree, static t => new WorkflowInput(t));
    }

    /// <summary>The tree this route belongs to — the one an adapter passes to <see cref="For"/>.</summary>
    public IWorkflowTreeViewModel Tree => tree;

    /// <summary>
    /// The component the pointer is currently on, or <see langword="null"/> when it is over empty canvas. Maintained
    /// by <see cref="Route(WorkflowPointerEventArgs)"/> unless <see cref="IsSuspended"/> is set.
    /// </summary>
    public IWorkflowViewModel? PointerTarget => pointerTarget;

    /// <summary>The link the pointer is currently on, or <see langword="null"/> — <see cref="PointerTarget"/> as a link.</summary>
    public IWorkflowLinkViewModel? HoveredLink => pointerTarget as IWorkflowLinkViewModel;

    /// <summary>
    /// Reach either side of a drawn line, in the same units the curves were published in. An adapter passes this
    /// into <see cref="LinkHitTestEx.HitTestVisibleLinks"/> when it resolves a link target on a shared surface.
    /// </summary>
    public double HitRadius { get; set; } = LinkHitTestEx.DefaultHitRadius;

    /// <summary>
    /// Whether the Delete key removes the link it is routed against, by itself (<c>link.DeleteCommand</c>). On by
    /// default, so a generated project deletes out of the box; turn it off to confirm first, or refuse one deletion
    /// through <see cref="WorkflowEventHandle.PreventDefault"/> on that event.
    /// </summary>
    public bool AutoDelete { get; set; } = true;

    /// <summary>
    /// When set, pointer movement no longer updates <see cref="PointerTarget"/>. A host that has opened its own
    /// popup raises this while it is open, so the move that got the pointer there — and the exit that follows when
    /// the pointer travels onto the popup — do not clear the very selection the popup is about to act on.
    /// </summary>
    public bool IsSuspended { get; set; }

    /// <summary>Routes a pointer event to the target and its ancestors, then applies the framework's own reaction.</summary>
    /// <param name="e">The event to route.</param>
    /// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// When the pointer moves onto a different component, both ends of that change are told first: the one it left
    /// through a <see cref="WorkflowPointerExitedEventArgs"/> of its own, the one it arrived at through a
    /// <see cref="WorkflowPointerEnteredEventArgs"/> of its own, each carrying this event's position. A component
    /// can therefore subscribe to its own helper alone and hear its whole hover — it does not have to watch every
    /// other component's events and compare targets.
    /// </remarks>
    public void Route(WorkflowPointerEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        EnterAndLeaveTargets(e);

        if (Bubble(e)) return;

        ApplyDefault(e);
    }

    /// <summary>Routes a key event to the target and its ancestors, then applies the framework's own reaction.</summary>
    /// <param name="e">The event to route.</param>
    /// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
    public void Route(WorkflowKeyEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        if (Bubble(e)) return;

        ApplyDefault(e);
    }

    // 指针换了目标：先给留下那个发一次 Exited，再给新那个发一次 Entered，然后才是这一条本身。
    // 逐组件订阅因此只需要 enter/leave 两件事，不必盯着每一条别人的事件比 target。
    // Exited 与 Entered 本身不合成 —— 那两条就是这件事本身，适配器已经按平台的意思报过了。
    private void EnterAndLeaveTargets(WorkflowPointerEventArgs e)
    {
        if (e is WorkflowPointerExitedEventArgs or WorkflowPointerEnteredEventArgs) return;

        var previous = pointerTarget;
        if (ReferenceEquals(previous, e.Target)) return;

        if (previous is not null)
        {
            Bubble(new WorkflowPointerExitedEventArgs(
                e.Position, e.Modifiers, e.Source, previous, new WorkflowEventHandle()));
        }

        if (e.Target is not null)
        {
            Bubble(new WorkflowPointerEnteredEventArgs(
                e.Position, e.Modifiers, e.Source, e.Target, new WorkflowEventHandle()));
        }
    }

    // 冒泡：目标先看到，再逐级上溯。返 true 表示这次被否决 —— 框架默认动作整体不执行。
    private bool Bubble(WorkflowPointerEventArgs e)
    {
        var prevented = false;

        foreach (var helper in Chain(e.Target))
        {
            if (helper is not IWorkflowInputEvents events) continue;

            events.Input.Raise(e);

            if (e.Handle.PreventDefault) prevented = true;
            if (e.Handle.StopPropagation) break;
        }

        return prevented;
    }

    private bool Bubble(WorkflowKeyEventArgs e)
    {
        var prevented = false;

        foreach (var helper in Chain(e.Target))
        {
            if (helper is not IWorkflowInputEvents events) continue;

            events.Input.Raise(e);

            if (e.Handle.PreventDefault) prevented = true;
            if (e.Handle.StopPropagation) break;
        }

        return prevented;
    }

    // 目标 -> 祖先。树永远在最后：连线的树用适配器传进来的那棵，不从 Sender 推 —— 占位与拖拽预览的旧 Sender 可能是 null。
    private IEnumerable<IWorkflowHelper> Chain(IWorkflowViewModel? target)
    {
        switch (target)
        {
            case IWorkflowSlotViewModel slot:
                yield return slot.GetHelper();
                if (slot.Parent is not { } owner) break;
                yield return owner.GetHelper();
                if (owner.Parent is { } slotTree) yield return slotTree.GetHelper();
                break;

            case IWorkflowNodeViewModel node:
                yield return node.GetHelper();
                if (node.Parent is { } nodeTree) yield return nodeTree.GetHelper();
                break;

            case IWorkflowLinkViewModel link:
                yield return link.GetHelper();
                yield return tree.GetHelper();
                break;

            // 空白画布（target 为 null）与树本身：只有树一级。
            default:
                yield return tree.GetHelper();
                break;
        }
    }

    // 框架自己的反应，只有两件事：记住指针停在哪（供宿主与 Delete 用），以及 Delete 落在连线上就删掉它。
    private void ApplyDefault(WorkflowPointerEventArgs e)
    {
        if (IsSuspended) return;

        switch (e)
        {
            case WorkflowPointerEnteredEventArgs or WorkflowPointerMovedEventArgs:
                pointerTarget = e.Target;
                break;

            case WorkflowPointerExitedEventArgs:
                pointerTarget = null;
                break;
        }
    }

    private void ApplyDefault(WorkflowKeyEventArgs e)
    {
        if (e is not WorkflowKeyDownEventArgs) return;
        if (e.Key != WorkflowKey.Delete) return;
        if (!AutoDelete) return;
        if (e.Target is not IWorkflowLinkViewModel link) return;

        link.DeleteCommand.Execute(null);
    }
}
