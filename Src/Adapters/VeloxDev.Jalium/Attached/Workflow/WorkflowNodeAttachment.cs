using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Everything a Jalium element needs to become one workflow node card, attached to it in a single call. The element
/// stays yours — what the card looks like is your <see cref="Render"/> handler — and this helper supplies the
/// binding, the placement, the scale-collapsing viewbox, the port glyphs, and the model events.
/// </summary>
/// <remarks>
/// <para>
/// Attach it in your card's constructor and subscribe <see cref="Render"/>; the card draws at its design size and the
/// viewbox scales it (ports included) to the collapsed node box:
/// <code>
/// public sealed class NodeView : Canvas
/// {
///     private readonly WorkflowNodeAttachment node;
///
///     public NodeView()
///     {
///         node = WorkflowNodeAttachment.Attach(this);
///         node.PortLayout = SlotView.Layout;
///         node.Render += (_, e) =&gt; DrawCard(e.Context);
///     }
/// }
/// </code>
/// </para>
/// <para>
/// The card must be a <see cref="Canvas"/>: the ports are child elements placed at design coordinates, exactly as
/// the card's own drawing is.
/// </para>
/// </remarks>
public sealed class WorkflowNodeAttachment
{
    // 一个元素一份：Attach 幂等，池与宿主都用 For 找它。
    private static readonly ConditionalWeakTable<Canvas, WorkflowNodeAttachment> Attachments = new();

    private readonly Canvas target;
    private readonly NodeCardLayer layer;
    private readonly Viewbox viewbox;
    private readonly IWorkflowNodeEventSink eventSink;

    private IWorkflowNodeViewModel? node;
    private INotifyPropertyChanged? layoutNotify;
    private PropertyChangedEventHandler? layoutHandler;
    private INotifyCollectionChanged? slotsNotify;
    private IDisposable? nodeEvents;
    private WorkflowPortLayout portLayout = new();

    /// <summary>Attaches the node machinery to <paramref name="target"/>, or returns the one already attached.</summary>
    /// <param name="target">The canvas that is going to draw one card.</param>
    /// <returns>The attachment, for chaining the layout and the event subscriptions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static WorkflowNodeAttachment Attach(Canvas target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        return Attachments.GetValue(target, static canvas => new WorkflowNodeAttachment(canvas));
    }

    /// <summary>The attachment on <paramref name="target"/>, or <see langword="null"/> when it has none.</summary>
    /// <param name="target">The element to look up.</param>
    public static WorkflowNodeAttachment? For(Canvas target)
        => target is not null && Attachments.TryGetValue(target, out var attachment) ? attachment : null;

    private WorkflowNodeAttachment(Canvas target)
    {
        this.target = target;
        eventSink = new NodeEventSink(this);

        // 卡片按设计尺寸画在内层画布上；Viewbox 填满折叠后的节点盒子，并按 1/scale 缩放它（连端口一起），
        // 与 WPF 那家的节点 Viewbox 同形。
        layer = new NodeCardLayer(this) { Width = portLayout.DesignWidth, Height = portLayout.DesignHeight };
        viewbox = new Viewbox { Child = layer, Stretch = Stretch.Uniform };
        Canvas.SetLeft(viewbox, 0);
        Canvas.SetTop(viewbox, 0);
        target.Children.Add(viewbox);

        target.DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Raised when the card has to draw itself, at its design size. The context is the inner layer's, so what you
    /// paint here scales with the card.
    /// </summary>
    public event EventHandler<CardRenderEventArgs>? Render;

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

    /// <summary>Where the card puts its ports, in design coordinates.</summary>
    /// <remarks>
    /// Assign the same instance the surface and the link views use — the item template's <c>SlotView.Layout</c> —
    /// or the cards and the hit-testing will disagree about where the ports are.
    /// </remarks>
    public WorkflowPortLayout PortLayout
    {
        get => portLayout;
        set
        {
            if (ReferenceEquals(portLayout, value) || value is null) return;
            portLayout = value;
            layer.Width = value.DesignWidth;
            layer.Height = value.DesignHeight;
            RebuildSlotViews();
        }
    }

    /// <summary>
    /// Builds the glyph for one port.
    /// </summary>
    /// <remarks>
    /// Defaults to a plain element with a <see cref="WorkflowSlotAttachment"/> on it. Point it at your own slot view
    /// to restyle the ports; the card writes the size into the attachment, so any element works.
    /// </remarks>
    public Func<IWorkflowSlotViewModel, FrameworkElement>? SlotViewFactory { get; set; }

    /// <summary>How the card reads the node's title.</summary>
    /// <remarks>
    /// A title is not a port: the model does not describe one and neither does anything derived from it, so the
    /// view — which knows its node type — declares it here. Without a declaration the card has no title.
    /// </remarks>
    public Func<IWorkflowNodeViewModel, string>? NodeTitle { get; set; }

    /// <summary>The node this card is showing, taken from the <c>DataContext</c>.</summary>
    public IWorkflowNodeViewModel? Node => node;

    /// <summary>The offset the card places its content at — the layout's offset plus the ruler reserve.</summary>
    /// <remarks>The surface's origin carries the same reserve, so the world axes land on the rulers' inner corner.</remarks>
    public double OriginX => (node?.Parent?.Layout.ActualOffset.Horizontal ?? 0) + WorkflowGridDecorator.RulerThickness;

    /// <summary><see cref="OriginX"/> on the vertical axis.</summary>
    public double OriginY => (node?.Parent?.Layout.ActualOffset.Vertical ?? 0) + WorkflowGridDecorator.RulerThickness;

    /// <summary>Repaints the card — call it when something you drew from changed.</summary>
    public void InvalidateCard() => layer.InvalidateVisual();


    private void RebuildSlotViews()
    {
        layer.Children.Clear();
        if (node is null) return;

        if (LayoutPorts is { } layout)
        {
            layout(this);
            return;
        }

        // 默认排布：一个输入贴左边，输出每个一行往下。卡片设了 LayoutPorts 就整块由它接管。
        DefaultLayout();
    }

    private void DefaultLayout()
    {
        var inputs = Inputs;
        if (inputs.Count > 0)
        {
            PlacePort(inputs[0].Slot, portLayout.InputPortX, portLayout.DesignHeight / 2.0, portLayout.InputPortRadius);
        }

        var outputs = Outputs;
        for (int i = 0; i < outputs.Count; i++)
        {
            double rowCenter = portLayout.TitleBarH + portLayout.RowH * i + portLayout.RowH / 2.0;
            PlacePort(outputs[i].Slot, portLayout.DesignWidth - portLayout.OutputInset, rowCenter, portLayout.OutputPortRadius);
        }
    }

    /// <summary>The node's input ports, as the model gives them.</summary>
    /// <remarks>What this card does with them is <see cref="LayoutPorts"/>'s business, not this property's.</remarks>
    public IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Inputs
        => node is null ? [] : WorkflowPortGeometry.Inputs(node);

    /// <summary>The node's output ports, as the model gives them.</summary>
    public IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Outputs
        => node is null ? [] : WorkflowPortGeometry.Outputs(node);

    /// <summary>
    /// Where this card puts its ports — set it and the placement is entirely yours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unset, the card lays out one input on the left edge and one row per output down the right — the shape every
    /// shipped template starts from. Set, you get the whole placement: read <see cref="Inputs"/> and
    /// <see cref="Outputs"/> (or <see cref="Node"/>'s slots directly, and their channels) and call
    /// <see cref="PlacePort"/> for each one you want drawn, wherever you want it. Nothing is placed for you.
    /// </para>
    /// <para>
    /// A delegate rather than a base class on purpose: the card is your own control — the same division the
    /// attachment's other hooks use, and the same one WinForms' card has, where the template does this in its own
    /// <c>OnRebound</c>.
    /// </para>
    /// </remarks>
    public Action<WorkflowNodeAttachment>? LayoutPorts { get; set; }

    /// <summary>Draws one port glyph at a design-local position on the card.</summary>
    /// <param name="slot">The slot this glyph stands for.</param>
    /// <param name="designX">X in the card's design coordinates.</param>
    /// <param name="designY">Y in the card's design coordinates.</param>
    /// <param name="radius">The glyph's radius, which is also its hit area.</param>
    public void PlacePort(IWorkflowSlotViewModel slot, double designX, double designY, double radius)
    {
        var view = SlotViewFactory?.Invoke(slot) ?? new FrameworkElement();
        view.DataContext = slot;

        // 端口多大由这里的布局决定，所以尺寸写进「附加」而不是要求对方是某个类型。
        WorkflowSlotAttachment.Attach(view).Radius = radius;

        Canvas.SetLeft(view, designX - radius);
        Canvas.SetTop(view, designY - radius);
        layer.Children.Add(view);
    }

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (node is INotifyPropertyChanged old) old.PropertyChanged -= OnNodeChanged;
        UnsubscribeLayout();
        UnsubscribeSlots();
        UnsubscribeNodeEvents();

        node = target.DataContext as IWorkflowNodeViewModel;
        if (node is INotifyPropertyChanged notify) notify.PropertyChanged += OnNodeChanged;

        // 模型事件由 Core 的 relay 接一次，转发到事件；Helper 不提供事件时 Attach 返回 null。
        nodeEvents = node is null ? null : WorkflowEventRelay.Attach(node, eventSink);

        // 工作区缩放会改 ActualOffset，卡片要跟着移位。
        if (node?.Parent?.Layout is INotifyPropertyChanged layout)
        {
            layoutNotify = layout;
            layoutHandler = (_, _) => ApplyPosition();
            layout.PropertyChanged += layoutHandler;
        }

        if (node?.Slots is INotifyCollectionChanged slots)
        {
            slotsNotify = slots;
            slots.CollectionChanged += OnSlotsChanged;
        }

        RebuildSlotViews();
        ApplyPosition();
    }

    private void UnsubscribeSlots()
    {
        if (slotsNotify is not null)
        {
            slotsNotify.CollectionChanged -= OnSlotsChanged;
            slotsNotify = null;
        }
    }

    private void UnsubscribeLayout()
    {
        if (layoutNotify is not null)
        {
            layoutNotify.PropertyChanged -= layoutHandler;
            layoutNotify = null;
            layoutHandler = null;
        }
    }

    private void UnsubscribeNodeEvents()
    {
        nodeEvents?.Dispose();
        nodeEvents = null;
    }

    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (node?.Slots is INotifyCollectionChanged slots)
        {
            UnsubscribeSlots();
            slotsNotify = slots;
            slots.CollectionChanged += OnSlotsChanged;
        }

        RebuildSlotViews();
        ApplyPosition();
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowSlotViewModel.State))
        {
            InvalidateCard();
        }
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            ApplyPosition();
        }
    }

    private void ApplyPosition()
    {
        if (node is null) return;

        Canvas.SetLeft(target, node.Anchor.Horizontal + OriginX);
        Canvas.SetTop(target, node.Anchor.Vertical + OriginY);
        target.Width = node.Size.Width;
        target.Height = node.Size.Height;
        viewbox.Width = target.Width;
        viewbox.Height = target.Height;
    }

    /// <summary>Carries the drawing context a card paints itself with, at its design size.</summary>
    public sealed class CardRenderEventArgs(DrawingContext context) : EventArgs
    {
        /// <summary>The context to draw with — the inner layer's, so it scales with the card.</summary>
        public DrawingContext Context { get; } = context;
    }

    // 设计尺寸的内层画布：把卡片（chrome、文字、端口）画在设计坐标上，Viewbox 再缩放到折叠后的节点盒子。
    private sealed class NodeCardLayer(WorkflowNodeAttachment owner) : Canvas
    {
        protected override void OnRender(DrawingContext dc)
            => owner.Render?.Invoke(owner, new CardRenderEventArgs(dc));
    }

    // relay 的宿主：把每个 sink 方法原样转给对应的事件。
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
