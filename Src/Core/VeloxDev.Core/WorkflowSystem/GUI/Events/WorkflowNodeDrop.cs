using System;
using System.Runtime.CompilerServices;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Owns the node drag-in gesture for one tree: the draft node, the pointer's last position, and the rule that
/// releasing over the canvas creates the node while leaving without releasing cancels it.
/// </summary>
/// <remarks>
/// <para>
/// It is the node-side counterpart of <see cref="IWorkflowTreeViewModel.VirtualLink"/>: an external host sets a
/// draft with <see cref="Begin{TContext}"/> (or the surface-less <see cref="Begin(IWorkflowNodeViewModel, Anchor?)"/>)
/// and clears it with <see cref="Cancel"/>. While it is set, every pointer move routed through
/// <see cref="WorkflowInput"/> writes the pointer's world position into the draft's
/// <see cref="IWorkflowNodeViewModel.Anchor"/>; a release over the canvas commits it through
/// <see cref="IWorkflowTreeViewModel.CreateNodeCommand"/>, and the pointer leaving the surface cancels it.
/// </para>
/// <para>
/// Rendering is the surface's job: <see cref="IWorkflowNodeDropSurface{TRenderContext}"/> is handed the draft
/// and the platform's own context (a decorator layer, an items panel, or a parent container) on begin, move and
/// end. A host that renders the draft itself can call <see cref="Begin(IWorkflowNodeViewModel, Anchor?)"/> instead
/// and bind to <see cref="PendingNode"/>.
/// </para>
/// <para>
/// A draft must be a node that is <b>not</b> attached to a tree yet. It is not added by the drag itself: the node
/// is created only on <see cref="Commit"/>, so a cancel leaves the tree untouched and undo has one entry to
/// remove when the drag does commit.
/// </para>
/// </remarks>
/// <seealso cref="IWorkflowNodeDropSurface{TRenderContext}"/>
/// <seealso cref="WorkflowInput"/>
public sealed class WorkflowNodeDrop
{
    // 一棵树一个：与树同寿，适配器与宿主用同一个调用取它。
    private static readonly ConditionalWeakTable<IWorkflowTreeViewModel, WorkflowNodeDrop> Instances = new();

    private readonly IWorkflowTreeViewModel tree;
    private IWorkflowNodeViewModel? pending;
    private Action<IWorkflowNodeViewModel>? begin;
    private Action<IWorkflowNodeViewModel>? move;
    private Action<IWorkflowNodeViewModel, bool>? end;

    private WorkflowNodeDrop(IWorkflowTreeViewModel tree) => this.tree = tree;

    /// <summary>The one drag-in state for this tree, created on first use and kept as long as the tree lives.</summary>
    /// <param name="tree">The tree the draft will be created in.</param>
    /// <returns>The tree's drag-in state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static WorkflowNodeDrop For(IWorkflowTreeViewModel tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));
        return Instances.GetValue(tree, static t => new WorkflowNodeDrop(t));
    }

    // 指针路由每次移动都会问一次；没有拖入时不能顺手创建实例。
    internal static WorkflowNodeDrop? TryFor(IWorkflowTreeViewModel tree)
        => Instances.TryGetValue(tree, out var drop) ? drop : null;

    /// <summary>
    /// Raised when <see cref="IsActive"/> changes: a draft began, committed or was cancelled. A host that draws the
    /// draft itself (without a surface) uses this to show and hide its own preview.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>The tree this gesture creates nodes in.</summary>
    public IWorkflowTreeViewModel Tree => tree;

    /// <summary>Whether a draft is currently set — the flag a surface reads to show its preview.</summary>
    public bool IsActive => pending is not null;

    /// <summary>The node being dragged in, or <see langword="null"/> when no drag is in progress.</summary>
    public IWorkflowNodeViewModel? PendingNode => pending;

    /// <summary>
    /// The last world position applied to the draft, or <see langword="null"/> before the first drag. Kept after
    /// the gesture ends, so a host can read where the last drop happened.
    /// </summary>
    public Anchor? Pointer { get; private set; }

    /// <summary>
    /// Starts a drag with no surface. The host renders the draft itself (bind to <see cref="PendingNode"/>)
    /// and Core still owns the pointer, release and cancel rules.
    /// </summary>
    /// <param name="node">The unattached draft node.</param>
    /// <param name="pointer">The initial world position; omitted, the node's current anchor is kept.</param>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="node"/> already belongs to a tree.</exception>
    public void Begin(IWorkflowNodeViewModel node, Anchor? pointer = null)
        => BeginCore(node, pointer, null, null, null);

    /// <summary>
    /// Starts a drag and lets <paramref name="surface"/> draw it. The surface is called on begin, on every
    /// move, and once at the end — with <c>committed: true</c> when the node was created.
    /// </summary>
    /// <typeparam name="TContext">The surface's own rendering context type.</typeparam>
    /// <param name="node">The unattached draft node.</param>
    /// <param name="surface">The surface that draws the preview.</param>
    /// <param name="context">The framework object the preview lives in.</param>
    /// <param name="pointer">The initial world position; omitted, the node's current anchor is kept.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="node"/> or <paramref name="surface"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="node"/> already belongs to a tree.</exception>
    public void Begin<TContext>(
        IWorkflowNodeViewModel node, IWorkflowNodeDropSurface<TContext> surface, TContext context, Anchor? pointer = null)
    {
        if (surface is null) throw new ArgumentNullException(nameof(surface));
        BeginCore(
            node,
            pointer,
            n => surface.Begin(n, context),
            n => surface.Move(n, context),
            (n, committed) => surface.End(n, context, committed));
    }

    private void BeginCore(
        IWorkflowNodeViewModel node,
        Anchor? pointer,
        Action<IWorkflowNodeViewModel>? onBegin,
        Action<IWorkflowNodeViewModel>? onMove,
        Action<IWorkflowNodeViewModel, bool>? onEnd)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));
        if (node.Parent is not null)
            throw new ArgumentException("The draft node must not belong to a tree yet; create a fresh node for each drag.", nameof(node));

        // 换一个草稿 = 取消上一个：上一个的 End(false) 必须发出去，否则它的预览会留在画面上。
        if (pending is not null) Cancel();

        pending = node;
        begin = onBegin;
        move = onMove;
        end = onEnd;

        var start = pointer ?? node.Anchor;
        Pointer = start;
        ApplyPointer(node, start);
        begin?.Invoke(node);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves the draft to a world position. Called by <see cref="WorkflowInput"/> for every pointer move; a host
    /// that starts the drag outside the surface may call it directly until the pointer enters.
    /// </summary>
    /// <param name="pointer">The new world position.</param>
    public void MoveTo(Anchor pointer)
    {
        if (pending is not { } node) return;

        Pointer = pointer;
        ApplyPointer(node, pointer);
        move?.Invoke(node);
    }

    /// <summary>
    /// Ends the drag by creating the draft. Its <see cref="IWorkflowNodeViewModel.Anchor"/> is updated to the
    /// release position first, so the created node lands where the pointer was let go. Does nothing when no
    /// drag is in progress.
    /// </summary>
    /// <param name="pointer">The release position; omitted, the last moved position is used.</param>
    public void Commit(Anchor? pointer = null)
    {
        if (pending is not { } node) return;

        if (pointer is { } release) MoveTo(release);

        var onEnd = end;
        Clear();
        onEnd?.Invoke(node, true);
        tree.CreateNodeCommand.Execute(node);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ends the drag without creating anything. Called when the pointer leaves the surface, when the host clears
    /// the gesture, or when a second draft replaces this one.
    /// </summary>
    public void Cancel()
    {
        if (pending is not { } node) return;

        var onEnd = end;
        Clear();
        onEnd?.Invoke(node, false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Clear()
    {
        pending = null;
        begin = null;
        move = null;
        end = null;
    }

    // 只写位置，不动图层：草稿的层由宿主创建时决定，拖入不该把它丢回 0 层。
    private static void ApplyPointer(IWorkflowNodeViewModel node, Anchor pointer)
        => node.Anchor = new Anchor(pointer.Horizontal, pointer.Vertical, node.Anchor.Layer);
}
