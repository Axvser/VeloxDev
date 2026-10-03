using System.Collections.Specialized;
using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A poolable node card: it takes its view-model from the <c>DataContext</c>, tracks the node's anchor and size,
/// and gives the host one method to paint the card.
/// </summary>
/// <remarks>
/// <para>
/// The card is painted at its design size on an inner canvas inside a <see cref="Viewbox"/>, so the whole output —
/// chrome, text and ports — scales together when the workspace zooms. Derive from it and override
/// <see cref="DrawCard"/> for the look; the binding, the subscriptions, the placement and the viewbox scaffolding
/// are the same in every host.
/// </para>
/// <para>
/// The port centres the surface hit-tests are <see cref="WorkflowPortGeometry"/>'s, computed from
/// <see cref="PortLayout"/> — not from what the card draws, so a card may draw its ports however it likes as long
/// as it honours the layout.
/// </para>
/// </remarks>
public abstract class WorkflowNodeView : Canvas
{
    private readonly Viewbox _viewbox;
    private readonly NodeCardLayer _layer;

    private IWorkflowNodeViewModel? _node;
    private INotifyPropertyChanged? _layoutNotify;
    private PropertyChangedEventHandler? _layoutHandler;
    private INotifyCollectionChanged? _slotsNotify;

    /// <summary>Creates the card.</summary>
    protected WorkflowNodeView()
    {
        // 卡片按设计尺寸画在内层画布上；Viewbox 填满折叠后的节点盒子，并按 1/scale 缩放它（连端口一起），
        // 与 WPF 那家的节点 Viewbox 同形。
        _layer = new NodeCardLayer(this) { Width = PortLayout.DesignWidth, Height = PortLayout.DesignHeight };
        _viewbox = new Viewbox { Child = _layer, Stretch = Stretch.Uniform };
        Canvas.SetLeft(_viewbox, 0);
        Canvas.SetTop(_viewbox, 0);
        Children.Add(_viewbox);

        DataContextChanged += OnDataContextChanged;
    }

    private WorkflowPortLayout _portLayout = new();

    /// <summary>Where the card puts its ports, in design coordinates.</summary>
    /// <remarks>
    /// Assign the same instance the surface and the link views use — the item template's <c>SlotView.Layout</c> —
    /// or the cards and the hit-testing will disagree about where the ports are.
    /// </remarks>
    public WorkflowPortLayout PortLayout
    {
        get => _portLayout;
        set
        {
            if (ReferenceEquals(_portLayout, value) || value is null) return;
            _portLayout = value;
            _layer.Width = value.DesignWidth;
            _layer.Height = value.DesignHeight;
            RebuildSlotViews();
        }
    }

    /// <summary>
    /// Builds the glyph for one port.
    /// </summary>
    /// <remarks>
    /// Defaults to a plain <see cref="WorkflowSlotView"/>. Point it at your own slot view to restyle the ports;
    /// the card sets its <see cref="WorkflowSlotView.Radius"/> from <see cref="PortLayout"/>, so a view that is not
    /// a <see cref="WorkflowSlotView"/> has to size itself.
    /// </remarks>
    public Func<IWorkflowSlotViewModel, FrameworkElement>? SlotViewFactory { get; set; }

    /// <summary>The node this card is showing, taken from the <c>DataContext</c>.</summary>
    protected IWorkflowNodeViewModel? Node => _node;

    /// <summary>The offset the card places its content at — the layout's offset plus the ruler reserve.</summary>
    /// <remarks>The surface's origin carries the same reserve, so the world axes land on the rulers' inner corner.</remarks>
    protected double OriginX => (_node?.Parent?.Layout.ActualOffset.Horizontal ?? 0) + WorkflowGridDecorator.RulerThickness;

    /// <summary>The vertical counterpart of <see cref="OriginX"/>.</summary>
    protected double OriginY => (_node?.Parent?.Layout.ActualOffset.Vertical ?? 0) + WorkflowGridDecorator.RulerThickness;

    /// <summary>Paints the card at its design size.</summary>
    /// <param name="dc">The drawing context, in design coordinates.</param>
    protected abstract void DrawCard(DrawingContext dc);

    /// <summary>Repaints the card.</summary>
    protected void InvalidateCard() => _layer.InvalidateVisual();

    // 每个端口一个槽位视图，摆在 PortLayout 说的位置上。卡片自己只画外壳与文字 —— 端口是视图，不是画出来的圆点，
    // 这样用户的 slot-view 条目才有东西可改。
    private void RebuildSlotViews()
    {
        _layer.Children.Clear();
        if (_node is null) return;

        var inputs = WorkflowPortGeometry.Inputs(_node);
        if (inputs.Count > 0)
        {
            PlaceSlot(inputs[0].Slot, PortLayout.InputPortX, PortLayout.DesignHeight / 2.0, PortLayout.InputPortRadius);
        }

        var outputs = WorkflowPortGeometry.Outputs(_node);
        for (int i = 0; i < outputs.Count; i++)
        {
            double rowCenter = PortLayout.TitleBarH + PortLayout.RowH * i + PortLayout.RowH / 2.0;
            PlaceSlot(outputs[i].Slot, PortLayout.DesignWidth - PortLayout.OutputInset, rowCenter, PortLayout.OutputPortRadius);
        }
    }

    private void PlaceSlot(IWorkflowSlotViewModel slot, double designX, double designY, double radius)
    {
        var view = SlotViewFactory?.Invoke(slot) ?? new WorkflowSlotView();
        view.DataContext = slot;

        if (view is WorkflowSlotView glyph) glyph.Radius = radius;

        Canvas.SetLeft(view, designX - radius);
        Canvas.SetTop(view, designY - radius);
        _layer.Children.Add(view);
    }

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_node is INotifyPropertyChanged old) old.PropertyChanged -= OnNodeChanged;
        UnsubscribeLayout();
        UnsubscribeSlots();

        _node = DataContext as IWorkflowNodeViewModel;
        if (_node is INotifyPropertyChanged notify) notify.PropertyChanged += OnNodeChanged;

        // 工作区缩放会改 ActualOffset，卡片要跟着移位。
        if (_node?.Parent?.Layout is INotifyPropertyChanged layout)
        {
            _layoutNotify = layout;
            _layoutHandler = (_, _) => ApplyPosition();
            layout.PropertyChanged += _layoutHandler;
        }

        // 插槽增减（选择器切换）或某个插槽的 State 变化（连线建立/删除，Core 的 UpdateState）都要重画，
        // 端口颜色才跟得上。
        if (_node?.Slots is INotifyCollectionChanged slots)
        {
            _slotsNotify = slots;
            slots.CollectionChanged += OnSlotsChanged;
        }

        if (_node is not null)
        {
            foreach (var slot in _node.Slots)
            {
                if (slot is INotifyPropertyChanged sp) sp.PropertyChanged += OnSlotChanged;
            }
        }

        RebuildSlotViews();
        ApplyPosition();
        InvalidateCard();
    }

    private void UnsubscribeSlots()
    {
        if (_slotsNotify is not null)
        {
            _slotsNotify.CollectionChanged -= OnSlotsChanged;
            _slotsNotify = null;
        }

        if (_node is not null)
        {
            foreach (var slot in _node.Slots)
            {
                if (slot is INotifyPropertyChanged sp) sp.PropertyChanged -= OnSlotChanged;
            }
        }
    }

    private void UnsubscribeLayout()
    {
        if (_layoutNotify is not null)
        {
            _layoutNotify.PropertyChanged -= _layoutHandler;
            _layoutNotify = null;
            _layoutHandler = null;
        }
    }

    private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is INotifyPropertyChanged sp) sp.PropertyChanged += OnSlotChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is INotifyPropertyChanged sp) sp.PropertyChanged -= OnSlotChanged;
            }
        }

        RebuildSlotViews();
        InvalidateCard();
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
        if (_node is null) return;

        Canvas.SetLeft(this, _node.Anchor.Horizontal + OriginX);
        Canvas.SetTop(this, _node.Anchor.Vertical + OriginY);
        Width = _node.Size.Width;
        Height = _node.Size.Height;
        _viewbox.Width = Width;
        _viewbox.Height = Height;
    }

    // 设计尺寸的内层画布：自己把卡片（chrome、文字、端口）画在设计坐标上，Viewbox 再缩放到折叠后的节点盒子。
    private sealed class NodeCardLayer : Canvas
    {
        private readonly WorkflowNodeView _owner;

        public NodeCardLayer(WorkflowNodeView owner) => _owner = owner;

        protected override void OnRender(DrawingContext dc) => _owner.DrawCard(dc);
    }
}
