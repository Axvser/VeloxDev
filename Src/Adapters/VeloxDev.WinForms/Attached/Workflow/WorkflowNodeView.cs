using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// The mechanism every workflow node card shares: binding, placement, zoom collapse and the reflective lookups
/// that read a title, an input slot and a slot label off whatever node view-model the host generated.
/// </summary>
/// <remarks>
/// <para>
/// Derive from it for the card itself — what the header looks like, which rows it renders, how it paints. Those
/// are the host's design; this type deliberately knows nothing about them.
/// </para>
/// <para>
/// The surface places the card through <see cref="IWorkflowSurfaceNodeView"/>, so a card never has to find the
/// surface's pan by walking its parent chain.
/// </para>
/// </remarks>
public abstract class WorkflowNodeView : UserControl, IWorkflowSurfaceNodeView
{
    private IWorkflowNodeViewModel? _node;
    private readonly ModelChangeRelay _relay;
    private readonly IWorkflowNodeEventSink _eventSink;
    private IDisposable? _modelEvents;

    /// <summary>Creates the card.</summary>
    protected WorkflowNodeView()
    {
        _relay = new ModelChangeRelay(this, OnNodePropertyChanged);
        _eventSink = new NodeEventSink(this);

        // 不透明底色：WinForms 里没有可靠的透明合成，卡片自己擦成不透明色。alpha 强制 255 —— .NET 10 上给
        // BackColor 一个半透明值会抛（没有 SupportsTransparentBackColor 时要求 A == 0xFF）。
        BackColor = Color.FromArgb(255, SurfaceBackdrop);
    }

    /// <summary>Raised when the bound node's anchor or size changes, after the card has repositioned itself.</summary>
    /// <remarks>
    /// The surface's minimap subscribes to this: a node drag moves the card without panning, and the minimap
    /// would otherwise only repaint on a pan.
    /// </remarks>
    public event Action? AnchorChanged;

    /// <summary>Gets or sets the node bound to this card.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowNodeViewModel? ViewModel
    {
        get => _node;
        set
        {
            if (ReferenceEquals(_node, value)) return;

            UnsubscribeNode();

            _node = value;
            Tag = value;
            _relay.Set(value as INotifyPropertyChanged);
            // 基类在学到自己所管的节点这一处接上模型事件；换绑时 UnsubscribeNode 已经摘掉上一份订阅。
            _modelEvents = value is null ? null : WorkflowEventRelay.Attach(value, _eventSink);

            if (_node?.Slots is INotifyCollectionChanged slots)
            {
                slots.CollectionChanged += OnSlotsCollectionChanged;
            }

            OnNodeRebound();
            NodeTitle = ReadNodeTitle();
            ApplyPosition();
            Invalidate();
        }
    }

    /// <summary>The bound node's display title, read reflectively — <see cref="IWorkflowNodeViewModel"/> has none.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string NodeTitle { get; private set; } = string.Empty;

    /// <summary>1/Scale, identity at scale 1 — the factor the card's fixed design metrics shrink by.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Collapse { get; private set; } = 1d;

    /// <summary>Colour the card erases to behind its rounded body; set it to the surface background.</summary>
    protected virtual Color SurfaceBackdrop => Color.FromArgb(0x1E, 0x1E, 0x1E);

    /// <summary>The signed pan the surface last handed over.</summary>
    protected Point SurfacePanOffset { get; private set; }

    /// <summary>The world origin the surface last handed over.</summary>
    protected Offset SurfaceContentOffset { get; private set; } = new(0, 0);

    /// <inheritdoc />
    void IWorkflowSurfaceNodeView.ApplySurfacePosition(Point panOffset, Offset contentOffset)
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
    protected void ApplyPosition()
    {
        if (_node is null || Parent is null) return;

        Location = new Point(
            (int)Math.Round(_node.Anchor.Horizontal) + SurfacePanOffset.X + (int)Math.Round(SurfaceContentOffset.Horizontal),
            (int)Math.Round(_node.Anchor.Vertical) + SurfacePanOffset.Y + (int)Math.Round(SurfaceContentOffset.Vertical));
        Size = new System.Drawing.Size(
            (int)Math.Round(_node.Size.Width),
            (int)Math.Round(_node.Size.Height));

        // 卡片内部那些固定度量按折叠因子重排，否则低缩放时会裁掉。
        Collapse = ComputeCollapseFactor();
        OnCollapseChanged(Collapse);
    }

    /// <summary>Called when the bound node changes, or when its <c>Slots</c> collection does.</summary>
    /// <remarks>Rebuild the card's slot visuals here.</remarks>
    protected virtual void OnNodeRebound()
    {
    }

    /// <summary>Called when the node's title changes.</summary>
    protected virtual void OnTitleChanged()
    {
    }

    /// <summary>Called when the zoom collapse factor changes; re-flow the card's fixed metrics here.</summary>
    /// <param name="collapse">1/Scale.</param>
    protected virtual void OnCollapseChanged(double collapse)
    {
    }

    /// <summary>Called before the bound node is placed somewhere new.</summary>
    /// <param name="e">The placement that is about to happen, both anchors complete.</param>
    /// <remarks>Set <see cref="WorkflowEventHandle.PreventDefault"/> on the argument's handle to refuse this one move.</remarks>
    protected virtual void OnMoving(NodeMoveEventArgs e)
    {
    }

    /// <summary>Called after the bound node was placed.</summary>
    /// <param name="e">The placement that happened.</param>
    protected virtual void OnMoved(NodeMoveEventArgs e)
    {
    }

    /// <summary>Called before the bound node's size changes.</summary>
    /// <param name="e">The resize that is about to happen.</param>
    /// <remarks>Set <see cref="WorkflowEventHandle.PreventDefault"/> on the argument's handle to keep the current size.</remarks>
    protected virtual void OnResizing(NodeResizeEventArgs e)
    {
    }

    /// <summary>Called after the bound node's size changed.</summary>
    /// <param name="e">The resize that happened.</param>
    protected virtual void OnResized(NodeResizeEventArgs e)
    {
    }

    /// <summary>Called before the bound node is torn down.</summary>
    /// <param name="e">The node about to be deleted.</param>
    /// <remarks>Set <see cref="WorkflowEventHandle.PreventDefault"/> on the argument's handle to keep it in the tree.</remarks>
    protected virtual void OnDeleting(NodeEventArgs e)
    {
    }

    /// <summary>Called after the bound node was torn down.</summary>
    /// <param name="e">The node that was deleted.</param>
    protected virtual void OnDeleted(NodeEventArgs e)
    {
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
    protected IWorkflowSlotViewModel? ResolveInputSlot()
    {
        if (_node is null) return null;

        IWorkflowSlotViewModel? fallback = null;
        foreach (var property in _node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            IWorkflowSlotViewModel? slot;
            try
            {
                slot = property.GetValue(_node) as IWorkflowSlotViewModel;
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
    protected string ResolveSlotLabel(IWorkflowSlotViewModel slot, int index)
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
    protected static void DisposeChildren(Control parent)
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
    protected static Color ParseColor(string hex) => WorkflowSurfaceColors.Parse(hex);

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyPosition();
    }

    /// <inheritdoc />
    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        ApplyPosition();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnsubscribeNode();
        }

        base.Dispose(disposing);
    }

    private void UnsubscribeNode()
    {
        _relay.Clear();
        _modelEvents?.Dispose();
        _modelEvents = null;

        if (_node?.Slots is INotifyCollectionChanged slots)
        {
            slots.CollectionChanged -= OnSlotsCollectionChanged;
        }
    }

    private void OnSlotsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new NotifyCollectionChangedEventHandler(OnSlotsCollectionChanged), sender, e);
            return;
        }

        // 插槽集合变了（例如某个 SlotEnumerator 换了选择器类型）：按当前集合重建行，让名字与图形对得上。
        OnNodeRebound();
        ApplyPosition();
        Invalidate();
    }

    private void OnNodePropertyChanged(PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Name" or "Title" or null or "")
        {
            NodeTitle = ReadNodeTitle();
            OnTitleChanged();
        }

        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor)
            or nameof(IWorkflowNodeViewModel.Size)
            or null or "")
        {
            ApplyPosition();
            AnchorChanged?.Invoke();
        }

        Invalidate();
    }

    // IWorkflowNodeViewModel 不暴露名字，所以反射找 Name / Title —— 对任何节点视图模型都成立，包括
    // VeloxDev 自带的那些。
    private string ReadNodeTitle()
    {
        if (_node is null) return string.Empty;

        var property = _node.GetType().GetProperty("Name") ?? _node.GetType().GetProperty("Title");
        return property?.GetValue(_node)?.ToString() ?? string.Empty;
    }

    // 1/Scale（缩放为 1 时是 1）。缩放为 0 按 1 处理 —— 那是个守卫，不是受支持的缩放级别。
    private double ComputeCollapseFactor()
    {
        var h = _node?.Parent?.Layout?.Scale?.Horizontal ?? 1d;
        return h == 0d ? 1d : 1d / h;
    }

    // 在节点的枚举器属性里找哪个条目引用了这个插槽，取它的名字。
    private string? ReadEnumeratorLabel(IWorkflowSlotViewModel slot)
    {
        if (_node is null) return null;

        foreach (var property in _node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var value = property.GetValue(_node);
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
    private sealed class NodeEventSink(WorkflowNodeView owner) : IWorkflowNodeEventSink
    {
        public void OnMoving(NodeMoveEventArgs e) => owner.OnMoving(e);
        public void OnMoved(NodeMoveEventArgs e) => owner.OnMoved(e);
        public void OnResizing(NodeResizeEventArgs e) => owner.OnResizing(e);
        public void OnResized(NodeResizeEventArgs e) => owner.OnResized(e);
        public void OnDeleting(NodeEventArgs e) => owner.OnDeleting(e);
        public void OnDeleted(NodeEventArgs e) => owner.OnDeleted(e);
    }
}
