using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Routes a GUI's pointer and keyboard input to the workflow components under it. Adapters translate their
/// platform's input into the standard arguments and hand it here; hosts and component views subscribe on the
/// components instead of re-deriving anything per platform.
///
/// The route owns exactly one piece of state — which component the pointer is on — so a host can read it without
/// keeping its own bookkeeping. It performs <b>no</b> action of its own: deleting a link, highlighting it, opening a
/// menu are all the host's, written where they can be seen and changed (see <c>IInputEvents</c>).
/// </summary>
/// <remarks>
/// <para>
/// The adapter decides <b>what</b> was hit (its own gesture logic already knows) and passes it as
/// <see cref="PointerEventArgs.Target"/>; this class decides <b>who hears about it</b> — the target first,
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
/// <seealso cref="IInputEvents"/>
public sealed class WorkflowInput
{
    // 一棵树一个：与树同寿，适配器与宿主都用这个调用取它。
    private static readonly ConditionalWeakTable<IWorkflowTreeViewModel, WorkflowInput> Instances = new();

    private readonly IWorkflowTreeViewModel tree;
    private IWorkflowViewModel? pointerTarget;
    private IWorkflowSlotViewModel? connectionSender;
    private IWorkflowSlotViewModel? previewReceiver;
    private ConnectionPreviewState previewState;
    private ConnectionCheck? previewCheck;

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
    /// by <see cref="Route(PointerEventArgs)"/> unless <see cref="IsSuspended"/> is set.
    /// </summary>
    public IWorkflowViewModel? PointerTarget => pointerTarget;

    /// <summary>The link the pointer is currently on, or <see langword="null"/> — <see cref="PointerTarget"/> as a link.</summary>
    public IWorkflowLinkViewModel? HoveredLink => pointerTarget as IWorkflowLinkViewModel;

    /// <summary>The port a connection drag started from, or <see langword="null"/> when no drag is in progress.</summary>
    public IWorkflowSlotViewModel? ConnectionSender => connectionSender;

    /// <summary>The candidate port under the pointer during a connection drag, or <see langword="null"/>.</summary>
    public IWorkflowSlotViewModel? PreviewReceiver => previewReceiver;

    /// <summary>The current stage of the connection drag.</summary>
    public ConnectionPreviewState PreviewState => previewState;

    /// <summary>
    /// The check for the candidate under the pointer, or <see langword="null"/> when the pointer is not over a
    /// candidate. Invalid candidates still carry a check, so a view can explain why they are red.
    /// </summary>
    public ConnectionCheck? PreviewCheck => previewCheck;

    /// <summary>
    /// Raised whenever <see cref="ConnectionSender"/>, <see cref="PreviewReceiver"/> or <see cref="PreviewState"/>
    /// changes. A link view subscribes here to paint the rubber band per stage; it does not decide the connection.
    /// </summary>
    public event EventHandler<ConnectionPreviewEventArgs>? PreviewChanged;

    /// <summary>
    /// Raised once for a new candidate the framework would accept, before it is marked as a valid target. A
    /// subscriber can refuse it for this preview only; it can never turn a framework refusal into a pass.
    /// </summary>
    /// <remarks>
    /// Not raised on every pointer move: the same candidate is reported once, and again only when
    /// <see cref="StandardEx.WorkflowTreeEx.StandardRefreshConnectionPreview"/> is called explicitly. Use that
    /// method after an asynchronous check completes. The release-time
    /// <see cref="IWorkflowTreeEvents.Connecting"/> veto remains the final decision.
    /// </remarks>
    public event EventHandler<ConnectionPreviewValidatingEventArgs>? PreviewValidating;

    /// <summary>Starts (or replaces) the connection drag's sender and resets the candidate.</summary>
    internal void SetConnectionSender(IWorkflowSlotViewModel? sender)
    {
        if (sender is null)
        {
            ClearConnectionPreview();
            return;
        }

        if (ReferenceEquals(connectionSender, sender)) return;

        if (connectionSender is { } previous)
        {
            previous.State &= ~SlotState.PreviewSender;
            previous.GetHelper().UpdateState();
        }

        connectionSender = sender;
        ClearPreviewReceiver();
        previewCheck = null;
        SetPreviewState(ConnectionPreviewState.Started);
        RaisePreviewChanged();
    }

    /// <summary>Records the candidate port under the pointer and the check computed for it.</summary>
    internal void SetConnectionPreview(IWorkflowSlotViewModel? receiver, ConnectionCheck check)
    {
        if (connectionSender is null)
        {
            ClearConnectionPreview();
            return;
        }

        var nextCheck = receiver is null ? (ConnectionCheck?)null : check;
        var next = receiver is null
            ? ConnectionPreviewState.Started
            : check.IsValid ? ConnectionPreviewState.ValidTarget : ConnectionPreviewState.InvalidTarget;

        if (ReferenceEquals(previewReceiver, receiver)
            && previewState == next
            && Nullable.Equals(previewCheck, nextCheck))
        {
            return;
        }

        ClearPreviewReceiver();
        previewReceiver = receiver;
        previewCheck = nextCheck;

        if (receiver is not null)
        {
            receiver.State |= SlotState.PreviewReceiver;
            receiver.GetHelper().UpdateState();
        }

        SetPreviewState(next);
        RaisePreviewChanged();
    }

    /// <summary>Ends the drag preview and clears both ports' preview state.</summary>
    internal void ClearConnectionPreview()
    {
        if (connectionSender is null && previewReceiver is null && previewState == ConnectionPreviewState.None)
            return;

        connectionSender = null;
        ClearPreviewReceiver();
        previewCheck = null;
        SetPreviewState(ConnectionPreviewState.None);
        RaisePreviewChanged();
    }

    private void ClearPreviewReceiver()
    {
        if (previewReceiver is not null)
        {
            previewReceiver.State &= ~SlotState.PreviewReceiver;
            previewReceiver.GetHelper().UpdateState();
        }

        previewReceiver = null;
    }

    private void SetPreviewState(ConnectionPreviewState state)
    {
        previewState = state;
        if (tree.VirtualLink is IConnectionPreviewLink link) link.SetPreview(state);
    }

    /// <summary>Lets subscribers refuse a framework-valid candidate for this preview.</summary>
    internal ConnectionCheck RaisePreviewValidating(
        IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver, ConnectionCheck check)
    {
        var handler = PreviewValidating;
        if (handler is null) return check;

        var args = new ConnectionPreviewValidatingEventArgs(sender, receiver, check);
        handler.Invoke(this, args);
        return args.Check;
    }

    private void RaisePreviewChanged()
        => PreviewChanged?.Invoke(
            this, new ConnectionPreviewEventArgs(connectionSender, previewReceiver, previewState, previewCheck));

    /// <summary>
    /// Reach either side of a drawn line, in the same units the curves were published in. An adapter passes this
    /// into <see cref="LinkHitTestEx.HitTestVisibleLinks"/> when it resolves a link target on a shared surface.
    /// </summary>
    public double HitRadius { get; set; } = LinkHitTestEx.DefaultHitRadius;

    /// <summary>
    /// When set, pointer movement no longer updates <see cref="PointerTarget"/>. A host that has opened its own
    /// popup raises this while it is open, so the move that got the pointer there — and the exit that follows when
    /// the pointer travels onto the popup — do not clear the very selection the popup is about to act on.
    /// </summary>
    public bool IsSuspended { get; set; }

    /// <summary>
    /// How a subscriber scrolls this surface after taking a wheel over, or <see langword="null"/> until an adapter
    /// registers one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wheel belongs to the adapter: it keeps the platform's scroll container from scrolling the workflow
    /// surface and applies the scroll itself, so <see cref="WorkflowEventHandle.PreventDefault"/> on a
    /// <see cref="PointerWheelEventArgs"/> means the surface does not scroll this time. A subscriber that sets it
    /// has taken that wheel over, and scrolls through here — the alternative is one platform branch per host.
    /// </para>
    /// <para>
    /// Unlike the rest of this type it is not the route's own state: an adapter writes it while it is attached, and
    /// clears it when it detaches, so a host reads it as a capability that may not be there.
    /// </para>
    /// </remarks>
    public IWorkflowSurfaceScroller? Scroller { get; set; }

    /// <summary>Routes a pointer event to the target and its ancestors, then applies the framework's own reaction.</summary>
    /// <param name="e">The event to route.</param>
    /// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// When the pointer moves onto a different component, both ends of that change are told first: the one it left
    /// through a <see cref="PointerExitedEventArgs"/> of its own, the one it arrived at through a
    /// <see cref="PointerEnteredEventArgs"/> of its own, each carrying this event's position. A component
    /// can therefore subscribe to its own helper alone and hear its whole hover — it does not have to watch every
    /// other component's events and compare targets.
    /// </remarks>
    public void Route(PointerEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        EnterAndLeaveTargets(e);

        if (Bubble(e)) return;

        ApplyDefault(e);
    }

    /// <summary>Routes a key event to the target and its ancestors, then applies the framework's own reaction.</summary>
    /// <param name="e">The event to route.</param>
    /// <exception cref="ArgumentNullException"><paramref name="e"/> is <see langword="null"/>.</exception>
    public void Route(KeyEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        // Keys have no default action: Core only routes them. Deleting is the host's — subscribe to KeyDown
        // and run link.DeleteCommand (the demos and the overlay take the same path: the library reports, the
        // host decides).
        Bubble(e);
    }

    // 指针换了目标：先给留下那个发一次 Exited，再给新那个发一次 Entered，然后才是这一条本身。
    // 逐组件订阅因此只需要 enter/leave 两件事，不必盯着每一条别人的事件比 target。
    // Exited 与 Entered 本身不合成 —— 那两条就是这件事本身，适配器已经按平台的意思报过了。
    //
    // ⚠ 这两条**各自是一个独立的动作**（「离开了 A」「进入了 B」），所以各自领一个新句柄。这与
    // 「一个动作一个句柄」不冲突：那里的「动作」指它们本身，不指触发它们的这次移动。
    // ⚠ 返回值**故意丢掉**：框架对「指针进来了」没有自己的手可执行，`PreventDefault` 在这两条上
    // 没有消费者；而 `StopPropagation` 照样有用 —— 它截断的是这一条自己的冒泡，在 `Bubble` 里就生效了。
    private void EnterAndLeaveTargets(PointerEventArgs e)
    {
        if (e is PointerExitedEventArgs or PointerEnteredEventArgs) return;

        var previous = pointerTarget;
        if (ReferenceEquals(previous, e.Target)) return;

        if (previous is not null)
        {
            Bubble(new PointerExitedEventArgs(
                e.Position, e.Modifiers, e.Source, previous, new WorkflowEventHandle()));
        }

        if (e.Target is not null)
        {
            Bubble(new PointerEnteredEventArgs(
                e.Position, e.Modifiers, e.Source, e.Target, new WorkflowEventHandle()));
        }
    }

    // 冒泡：目标先看到，再逐级上溯。返 true 表示这次被否决 —— 框架默认动作整体不执行。
    private bool Bubble(PointerEventArgs e)
    {
        var prevented = false;

        foreach (var helper in Chain(e.Target))
        {
            if (helper is not IInputEvents events) continue;

            events.Input.Raise(e);

            if (e.Handle.PreventDefault) prevented = true;
            if (e.Handle.StopPropagation) break;
        }

        return prevented;
    }

    private bool Bubble(KeyEventArgs e)
    {
        var prevented = false;

        foreach (var helper in Chain(e.Target))
        {
            if (helper is not IInputEvents events) continue;

            events.Input.Raise(e);

            if (e.Handle.PreventDefault) prevented = true;
            if (e.Handle.StopPropagation) break;
        }

        return prevented;
    }

    // 目标 -> 祖先，**树永远是最后一级**。
    // 树的取法一律用适配器传进来的那棵（`tree`），**不从模型的 Parent 推**：拖拽预览的插槽、还没挂到树上的
    // 节点、以及占位用的旧 Sender，它们的 Parent 链随时可能是 null 或指向别处，而适配器手里那棵永远是对的。
    // 中间那几级才走模型关系（只有 slot → 它所属的 node 这一跳）。
    private IEnumerable<IWorkflowHelper> Chain(IWorkflowViewModel? target)
    {
        switch (target)
        {
            case IWorkflowSlotViewModel slot:
                yield return slot.GetHelper();
                if (slot.Parent is { } owner) yield return owner.GetHelper();
                break;

            case IWorkflowNodeViewModel node:
                yield return node.GetHelper();
                break;

            case IWorkflowLinkViewModel link:
                yield return link.GetHelper();
                break;
        }

        // 空白画布（target 为 null）与树本身也落到这里：链上只剩树一级。
        yield return tree.GetHelper();
    }

    // 框架自己的反应只有一件：记住指针停在哪。宿主与 Delete 读它，**七家的键盘也一律拿它当 target**
    // （键没有自己的命中）。所以**任何带 Target 的指针事件都要更新它**，不能只认 Entered/Moved ——
    // 否则「滚过一格但指针没移动过」之后按键盘，target 还是上一次移动时的那一个。
    private void ApplyDefault(PointerEventArgs e)
    {
        if (IsSuspended) return;

        pointerTarget = e is PointerExitedEventArgs ? null : e.Target;
    }
}
