using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Everything a WinForms control needs to become one workflow node card, attached to it in a single call. The
/// control stays yours — what the card looks like is your <c>OnPaint</c> — and this helper supplies the binding,
/// the placement, the zoom collapse, the model events, and the reflective lookups that read a title, an input slot
/// and a slot label off whatever node view-model the host generated.
/// </summary>
/// <remarks>
/// <para>
/// Attach it in your card's constructor and give it the backdrop; the card is then an ordinary control that draws
/// itself. The surface places it through <see cref="ApplySurfacePosition"/>, so a card never has to find the
/// surface's pan by walking its parent chain.
/// </para>
/// <code>
/// public sealed class NodeView : UserControl
/// {
///     private readonly WorkflowNodeAttachment node;
///
///     public NodeView()
///     {
///         node = WorkflowNodeAttachment.Attach(this);
///         node.Rebound += (_, _) =&gt; RebuildRows();
///         node.CollapseChanged += (_, e) =&gt; Reflow(e.Collapse);
///     }
///
///     protected override void OnPaintBackground(PaintEventArgs e) { … }   // 卡片完全由你画
/// }
/// </code>
/// </remarks>
public sealed class WorkflowNodeAttachment : IWorkflowSurfaceNodeView
{
    // 一个控件一份：Attach 幂等，池与表面都用 For 找它。
    private static readonly ConditionalWeakTable<Control, WorkflowNodeAttachment> Attachments = new();

    private readonly Control target;
    private readonly ModelChangeRelay relay;
    private readonly IWorkflowNodeEventSink eventSink;

    private IWorkflowNodeViewModel? node;
    private IDisposable? modelEvents;
    private Color surfaceBackdrop = Color.FromArgb(0x1E, 0x1E, 0x1E);

    /// <summary>Attaches the node machinery to <paramref name="target"/>, or returns the one already attached.</summary>
    /// <param name="target">The control that is going to draw one card.</param>
    /// <returns>The attachment, for chaining the backdrop and the event subscriptions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static WorkflowNodeAttachment Attach(Control target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        return Attachments.GetValue(target, static control => new WorkflowNodeAttachment(control));
    }

    /// <summary>The attachment on <paramref name="target"/>, or <see langword="null"/> when it has none.</summary>
    /// <param name="target">The control to look up.</param>
    public static WorkflowNodeAttachment? For(Control target)
        => target is not null && Attachments.TryGetValue(target, out var attachment) ? attachment : null;

    private WorkflowNodeAttachment(Control target)
    {
        this.target = target;
        relay = new ModelChangeRelay(target, OnNodePropertyChanged);
        eventSink = new NodeEventSink(this);

        // 不透明底色：WinForms 里没有可靠的透明合成，卡片自己擦成不透明色。alpha 强制 255 —— .NET 10 上给
        // BackColor 一个半透明值会抛（没有 SupportsTransparentBackColor 时要求 A == 0xFF）。
        target.BackColor = Color.FromArgb(255, surfaceBackdrop);
        target.HandleCreated += (_, _) => ApplyPosition();
        target.ParentChanged += OnTargetParentChanged;
        target.Disposed += (_, _) =>
        {
            target.ParentChanged -= OnTargetParentChanged;
            Detach();
        };
    }

    /// <summary>Raised when the card has been re-placed, so a host can do its own bookkeeping after the move.</summary>
    public event EventHandler? Repositioned;

    /// <summary>Raised when the bound node changes, or when its <c>Slots</c> collection does. Rebuild the rows here.</summary>
    public event EventHandler? Rebound;

    /// <summary>Raised when the node's title changes.</summary>
    public event EventHandler? TitleChanged;

    /// <summary>Raised when the zoom collapse factor changes; re-flow the card's fixed metrics here.</summary>
    public event EventHandler<CollapseChangedEventArgs>? CollapseChanged;

    /// <summary>Raised before the bound node is placed somewhere new.</summary>
    /// <remarks>Set <see cref="WorkflowEventHandle.PreventDefault"/> on the argument's handle to refuse this one move.</remarks>
    public event EventHandler<NodeMoveEventArgs>? Moving;

    /// <summary>Raised after the bound node was placed.</summary>
    public event EventHandler<NodeMoveEventArgs>? Moved;

    /// <summary>Raised before the bound node's size changes.</summary>
    /// <remarks>Set <see cref="WorkflowEventHandle.PreventDefault"/> on the argument's handle to keep the current size.</remarks>
    public event EventHandler<NodeResizeEventArgs>? Resizing;

    /// <summary>Raised after the bound node's size changed.</summary>
    public event EventHandler<NodeResizeEventArgs>? Resized;

    /// <summary>Raised before the bound node is torn down.</summary>
    /// <remarks>Set <see cref="WorkflowEventHandle.PreventDefault"/> on the argument's handle to keep it in the tree.</remarks>
    public event EventHandler<NodeEventArgs>? Deleting;

    /// <summary>Raised after the bound node was torn down.</summary>
    public event EventHandler<NodeEventArgs>? Deleted;

    /// <summary>Raised when the bound node's anchor or size changes, after the card has repositioned itself.</summary>
    /// <remarks>
    /// The surface's minimap subscribes to this: a node drag moves the card without panning, and the minimap
    /// would otherwise only repaint on a pan.
    /// </remarks>
    public event Action? AnchorChanged;

    /// <summary>Gets or sets the node bound to this card.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowNodeViewModel? Node
    {
        get => node;
        set
        {
            if (ReferenceEquals(node, value)) return;

            UnsubscribeNode();

            node = value;
            target.Tag = value;
            relay.Set(value as INotifyPropertyChanged);
            // 学到所管的节点这一处接上模型事件；换绑时 UnsubscribeNode 已经摘掉上一份订阅。
            modelEvents = value is null ? null : WorkflowEventRelay.Attach(value, eventSink);

            if (node?.Slots is INotifyCollectionChanged slots)
            {
                slots.CollectionChanged += OnSlotsCollectionChanged;
            }

            this.Rebound?.Invoke(this, EventArgs.Empty);
            Title = ReadNodeTitle();
            ApplyPosition();
            target.Invalidate();
        }
    }

    /// <summary>The bound node's display title, read reflectively — <see cref="IWorkflowNodeViewModel"/> has none.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; private set; } = string.Empty;

    /// <summary>1/Scale, identity at scale 1 — the factor the card's fixed design metrics shrink by.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Collapse { get; private set; } = 1d;

    /// <summary>Colour the card erases to behind its rounded body; set it to the surface background.</summary>
    public Color SurfaceBackdrop
    {
        get => surfaceBackdrop;
        set
        {
            if (surfaceBackdrop == value) return;
            surfaceBackdrop = value;
            target.BackColor = Color.FromArgb(255, value);
        }
    }

    /// <summary>The signed pan the surface last handed over.</summary>
    public Point SurfacePanOffset { get; private set; }

    /// <summary>The world origin the surface last handed over.</summary>
    public Offset SurfaceContentOffset { get; private set; } = new(0, 0);

    /// <inheritdoc />
    public void ApplySurfacePosition(Point panOffset, Offset contentOffset)
    {
        // 记下这组投影：卡片自己也会触发重摆（锚点变了、绑定换了），那时手上没有新的投影，复用上一次的。
        SurfacePanOffset = panOffset;
        SurfaceContentOffset = contentOffset;
        ApplyPosition();
    }

    /// <summary>
    /// Places the card at the node anchor plus the surface's projection.
    /// </summary>
    /// <remarks>
    /// Before the surface says anything both parts of the projection are (0,0) — the same rest state a bare canvas
    /// host has. Call it from any host-side trigger that changes the anchor without going through the surface.
    /// </remarks>
    public void ApplyPosition()
    {
        if (node is null || target.Parent is null) return;

        target.Location = new Point(
            (int)Math.Round(node.Anchor.Horizontal) + SurfacePanOffset.X + (int)Math.Round(SurfaceContentOffset.Horizontal),
            (int)Math.Round(node.Anchor.Vertical) + SurfacePanOffset.Y + (int)Math.Round(SurfaceContentOffset.Vertical));
        target.Size = new System.Drawing.Size(
            (int)Math.Round(node.Size.Width),
            (int)Math.Round(node.Size.Height));

        // 卡片内部那些固定度量按折叠因子重排，否则低缩放时会裁掉。
        Collapse = ComputeCollapseFactor();
        CollapseChanged?.Invoke(this, new CollapseChangedEventArgs(Collapse));
        Repositioned?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Finds the node's fixed input slot — the property whose value is a concrete
    /// <see cref="IWorkflowSlotViewModel"/> rather than a <see cref="SlotEnumerator{T}"/>.
    /// </summary>
    /// <returns>The input slot, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Preferred over channel-based detection because an input's channel can still be the generated default when
    /// the card first renders — setting it runs through an asynchronous command.
    /// </remarks>
    public IWorkflowSlotViewModel? ResolveInputSlot()
    {
        if (node is null) return null;

        IWorkflowSlotViewModel? fallback = null;
        foreach (var property in node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            IWorkflowSlotViewModel? slot;
            try
            {
                slot = property.GetValue(node) as IWorkflowSlotViewModel;
            }
            catch
            {
                // 生成的属性在初始化之前有些会抛；跳过。
                continue;
            }

            if (slot is null) continue;
            if (string.Equals(property.Name, "InputSlot", StringComparison.OrdinalIgnoreCase))
            {
                return slot;
            }

            // 只在候选明确是输入（能当源、不能当目标）时才兜底；只有输出能力的属性不能变成输入口。
            var hasSource = (slot.Channel & (SlotChannel.OneSource | SlotChannel.MultipleSources)) != 0;
            var hasTarget = (slot.Channel & (SlotChannel.OneTarget | SlotChannel.MultipleTargets)) != 0;
            if (hasSource && !hasTarget && fallback is null)
            {
                fallback = slot;
            }
        }

        return fallback;
    }

    /// <summary>
    /// Resolves an output slot's display label.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <param name="index">Its position among the outputs, used for the last-resort label.</param>
    /// <returns>The label.</returns>
    /// <remarks>
    /// Enumerated slots carry their name on the owning <see cref="SlotEnumerator{T}"/>'s item, so this reflects
    /// over the node's enumerator properties first, then falls back to a <c>Name</c>/<c>Title</c> property, then to
    /// a positional label.
    /// </remarks>
    public string ResolveSlotLabel(IWorkflowSlotViewModel slot, int index)
    {
        if (ReadEnumeratorLabel(slot) is { Length: > 0 } name)
        {
            return name;
        }

        var fallback = slot.GetType().GetProperty("Name") ?? slot.GetType().GetProperty("Title");
        if (fallback?.GetValue(slot)?.ToString() is { Length: > 0 } text)
        {
            return text;
        }

        return $"Output {index + 1}";
    }

    /// <summary>Disposes and removes every child of <paramref name="parent"/>.</summary>
    /// <param name="parent">The container to clear.</param>
    public static void DisposeChildren(Control parent)
    {
        foreach (var child in parent.Controls.OfType<Control>())
        {
            child.Dispose();
        }

        parent.Controls.Clear();
    }

    /// <summary>Parses a <c>#RRGGBB</c>, <c>#AARRGGBB</c> or named colour.</summary>
    /// <param name="hex">The colour text.</param>
    /// <returns>The colour.</returns>
    public static Color ParseColor(string hex) => WorkflowSurfaceColors.Parse(hex);

    // 句柄建好、或挂到父容器时把位置补一次（否则第一帧停在原点）。
    private void OnTargetParentChanged(object? sender, EventArgs e) => ApplyPosition();

    private void UnsubscribeNode()
    {
        relay.Clear();
        modelEvents?.Dispose();
        modelEvents = null;

        if (node?.Slots is INotifyCollectionChanged slots)
        {
            slots.CollectionChanged -= OnSlotsCollectionChanged;
        }
    }

    private void OnSlotsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (target.InvokeRequired)
        {
            target.BeginInvoke(new NotifyCollectionChangedEventHandler(OnSlotsCollectionChanged), sender, e);
            return;
        }

        // 插槽集合变了（例如某个 SlotEnumerator 换了选择器类型）：按当前集合重建行，让名字与图形对得上。
        Rebound?.Invoke(this, EventArgs.Empty);
        ApplyPosition();
        target.Invalidate();
    }

    private void OnNodePropertyChanged(PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Name" or "Title" or null or "")
        {
            Title = ReadNodeTitle();
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }

        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor)
            or nameof(IWorkflowNodeViewModel.Size)
            or null or "")
        {
            ApplyPosition();
            AnchorChanged?.Invoke();
        }

        target.Invalidate();
    }

    // IWorkflowNodeViewModel 不暴露名字，所以反射找 Name / Title —— 对任何节点视图模型都成立，包括
    // VeloxDev 自带的那些。
    private string ReadNodeTitle()
    {
        if (node is null) return string.Empty;

        var property = node.GetType().GetProperty("Name") ?? node.GetType().GetProperty("Title");
        return property?.GetValue(node)?.ToString() ?? string.Empty;
    }

    // 1/Scale（缩放为 1 时是 1）。缩放为 0 按 1 处理 —— 那是个守卫，不是受支持的缩放级别。
    private double ComputeCollapseFactor()
    {
        var h = node?.Parent?.Layout?.Scale?.Horizontal ?? 1d;
        return h == 0d ? 1d : 1d / h;
    }

    // 在节点的枚举器属性里找哪个条目引用了这个插槽，取它的名字。
    private string? ReadEnumeratorLabel(IWorkflowSlotViewModel slot)
    {
        if (node is null) return null;

        foreach (var property in node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var value = property.GetValue(node);
            if (value is null) continue;

            var enumeratorType = value.GetType();
            if (!enumeratorType.IsGenericType
                || enumeratorType.GetGenericTypeDefinition() != typeof(SlotEnumerator<>))
            {
                continue;
            }

            if (FindEnumeratorLabel(enumeratorType, value, slot) is { } label)
            {
                return label;
            }
        }

        return null;
    }

    private static string? FindEnumeratorLabel(Type enumeratorType, object enumerator, IWorkflowSlotViewModel target)
    {
        var itemsProperty = enumeratorType.GetProperty("Items");
        if (itemsProperty?.GetValue(enumerator) is not System.Collections.IEnumerable items)
        {
            return null;
        }

        foreach (var item in items)
        {
            if (item is null) continue;

            var slotProperty = item.GetType().GetProperty("Slot");
            if (slotProperty?.GetValue(item) is not IWorkflowSlotViewModel slot
                || !ReferenceEquals(slot, target))
            {
                continue;
            }

            return item.GetType().GetProperty("Name")?.GetValue(item)?.ToString();
        }

        return null;
    }

    // 基类自己接模型事件、自己摘订阅，把每条转发进对应的可重写钩子；宿主只重写钩子，不碰 Helper。
    /// <summary>Carries the collapse factor a card re-flows its fixed metrics by.</summary>
    public sealed class CollapseChangedEventArgs(double collapse) : EventArgs
    {
        /// <summary>1/Scale, identity at scale 1.</summary>
        public double Collapse { get; } = collapse;
    }

    private void Detach()
    {
        UnsubscribeNode();
        relay.Clear();
    }

    private sealed class NodeEventSink(WorkflowNodeAttachment owner) : IWorkflowNodeEventSink
    {
        public void OnMoving(NodeMoveEventArgs e) => owner.Moving?.Invoke(owner, e);
        public void OnMoved(NodeMoveEventArgs e) => owner.Moved?.Invoke(owner, e);
        public void OnResizing(NodeResizeEventArgs e) => owner.Resizing?.Invoke(owner, e);
        public void OnResized(NodeResizeEventArgs e) => owner.Resized?.Invoke(owner, e);
        public void OnDeleting(NodeEventArgs e) => owner.Deleting?.Invoke(owner, e);
        public void OnDeleted(NodeEventArgs e) => owner.Deleted?.Invoke(owner, e);
    }
}
