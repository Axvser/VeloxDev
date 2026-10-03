using System;
using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// A poolable link view: a cubic Bézier that leaves each port horizontally, bounded to its own content.
/// </summary>
/// <remarks>
/// <para>
/// The element is positioned at the curve's own canvas-local bounding box and sized to it, and the render pass
/// bakes the geometry back into element-local coordinates — so the layout box always equals the drawn content and
/// travels with it through any zoom. That is not a stylistic choice: the renderer culls a child entirely when its
/// layout box misses the viewport clip and never looks at the drawn content, so a full-canvas box that goes stale
/// during a zoom burst takes the whole link layer with it while the self-bounded cards keep rendering. Baking the
/// geometry back by the box origin cancels the repositioning, so the on-screen output is unchanged; only the cull
/// box differs.
/// </para>
/// <para>
/// The link comes from the <c>DataContext</c>, and only real visible links are drawn — the surface paints its
/// drag preview inline. Derive from it for the stroke; the binding, the endpoint tracking and the boxing are the
/// same in every host.
/// </para>
/// </remarks>
public class WorkflowLinkView : FrameworkElement
{
    // 控制点的最小水平拉出量：两个端口靠得很近时，0.5·dx 会让曲线退化成一条直线段，
    // 失去「从端口水平出来」的形状。
    private const double PullMinimum = 40;

    /// <summary>Pen half-width plus antialias air, so the box always covers the stroke.</summary>
    private const double BoxPad = 6;

    private Color _linkColor = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);
    private double _thickness = 2;

    private IWorkflowLinkViewModel? _link;
    private INotifyPropertyChanged? _layoutNotify;
    private PropertyChangedEventHandler? _layoutHandler;
    private INotifyPropertyChanged? _senderNodeNotify;
    private INotifyPropertyChanged? _receiverNodeNotify;

    // 元素被摆在画布局部坐标的哪个位置（Canvas.Left/Top），以及它的大小 —— OnRender 靠它把几何烘焙回局部坐标。
    private double _viewX;
    private double _viewY;

    /// <summary>Creates the link view.</summary>
    protected WorkflowLinkView()
    {
        IsHitTestVisible = false;
        Panel.SetZIndex(this, -100);
        DataContextChanged += OnDataContextChanged;
    }

    private WorkflowPortLayout _portLayout = new();

    /// <summary>Where the cards put their ports, in design coordinates.</summary>
    /// <remarks>Assign the same instance the cards and the surface use, or the endpoints will miss the ports.</remarks>
    public WorkflowPortLayout PortLayout
    {
        get => _portLayout;
        set
        {
            if (ReferenceEquals(_portLayout, value) || value is null) return;
            _portLayout = value;
            UpdateBounds();
            InvalidateVisual();
        }
    }

    /// <summary>The stroke colour.</summary>
    public Color LinkColor
    {
        get => _linkColor;
        set
        {
            if (_linkColor == value) return;
            _linkColor = value;
            InvalidateVisual();
        }
    }

    /// <summary>The stroke width, in pixels.</summary>
    public double Thickness
    {
        get => _thickness;
        set
        {
            if (_thickness == value) return;
            _thickness = value;
            UpdateBounds();
            InvalidateVisual();
        }
    }

    /// <summary>The link this view is showing, taken from the <c>DataContext</c>.</summary>
    protected IWorkflowLinkViewModel? Link => _link;

    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        if (_link is null || !_link.IsVisible || IsDragPreview(_link)) return;
        if (EndpointsCanvasLocal() is not { } ep) return;

        // 把画布局部的几何烘焙回元素局部：元素被摆在 (_viewX,_viewY)，所以 local = canvas − (_viewX,_viewY)。
        // 这一步抵消掉 UpdateBounds 里的重定位，屏幕上的输出与画在 (0,0) 完全一样 —— 曲线只会在盒子过期时才
        // 跑出元素自己的盒子，而现在按构造成立那不可能。
        var from = new Point(ep.FromP.X - _viewX, ep.FromP.Y - _viewY);
        var to = new Point(ep.ToP.X - _viewX, ep.ToP.Y - _viewY);
        var pen = new Pen(new SolidColorBrush(_linkColor), _thickness);

        // 与其它 GUI 一致的三次贝塞尔（镜像 workflow-tree-view）：两个控制点各自水平拉开，
        // 连线因此从两端水平出线、中间平滑过渡，没有折角。
        var pull = Math.Max(PullMinimum, Math.Abs(to.X - from.X) * 0.5);
        var c1 = new Point(from.X + pull, from.Y);
        var c2 = new Point(to.X - pull, to.Y);

        var figure = new PathFigure { StartPoint = from, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new BezierSegment(c1, c2, to, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, pen, geometry);
    }

    // 只有树的拖拽预览（VirtualLink）为真：它的端点是占位的 SlotDefaultViewModel，没挂在节点上。
    // 表面自己在 OnPostRender 里画那个预览，所以池化视图必须跳过它。真实连线永远不是这个类型 ——
    // 即使插槽被摘下来也照画不误，而不是消失。
    private static bool IsDragPreview(IWorkflowLinkViewModel link)
        => link.Sender is SlotDefaultViewModel && link.Receiver is SlotDefaultViewModel;

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        Unsubscribe();
        UnsubscribeLayout();

        _link = DataContext as IWorkflowLinkViewModel;
        if (_link is INotifyPropertyChanged linkNotify) linkNotify.PropertyChanged += OnLinkChanged;
        if (_link?.Sender is INotifyPropertyChanged senderNotify) senderNotify.PropertyChanged += OnLinkChanged;
        if (_link?.Receiver is INotifyPropertyChanged receiverNotify) receiverNotify.PropertyChanged += OnLinkChanged;

        // 表面按 node.Anchor 摆连线（它从不写 slot.Anchor），所以连线必须直接跟着两端节点走 —— 拖一张卡片，
        // 连接立刻跟上。
        if (_link?.Sender?.Parent is INotifyPropertyChanged senderNode)
        {
            _senderNodeNotify = senderNode;
            senderNode.PropertyChanged += OnNodeChanged;
        }

        if (_link?.Receiver?.Parent is INotifyPropertyChanged receiverNode)
        {
            _receiverNodeNotify = receiverNode;
            receiverNode.PropertyChanged += OnNodeChanged;
        }

        // 任何布局变化（缩放 Scale、覆盖增长 → ActualOffset）都会折叠端点，所以自适应盒子必须与重画同频重算 ——
        // 与 NodeView 的定位同节奏。
        if (_link?.Sender.Parent?.Parent?.Layout is INotifyPropertyChanged layout)
        {
            _layoutNotify = layout;
            _layoutHandler = (_, _) =>
            {
                UpdateBounds();
                InvalidateVisual();
            };
            layout.PropertyChanged += _layoutHandler;
        }

        UpdateBounds();
        InvalidateVisual();
    }

    private void Unsubscribe()
    {
        if (_link is INotifyPropertyChanged linkNotify) linkNotify.PropertyChanged -= OnLinkChanged;
        if (_link?.Sender is INotifyPropertyChanged senderNotify) senderNotify.PropertyChanged -= OnLinkChanged;
        if (_link?.Receiver is INotifyPropertyChanged receiverNotify) receiverNotify.PropertyChanged -= OnLinkChanged;

        if (_senderNodeNotify is not null)
        {
            _senderNodeNotify.PropertyChanged -= OnNodeChanged;
            _senderNodeNotify = null;
        }

        if (_receiverNodeNotify is not null)
        {
            _receiverNodeNotify.PropertyChanged -= OnNodeChanged;
            _receiverNodeNotify = null;
        }

        _link = null;
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

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            UpdateBounds();
            InvalidateVisual();
        }
    }

    private void OnLinkChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateBounds();
        InvalidateVisual();
    }

    // 端口的权威中心（node.Anchor + 设计局部 · s），与表面不维护的 slot.Anchor 无关。端点还没有位置时返回 null
    // （插槽被摘且没有测量过的锚点），于是这条连线被跳过，而不是从原点画一条退化的线。
    private Point? PortCenter(IWorkflowSlotViewModel slot)
    {
        var node = slot.Parent;
        if (node is null)
        {
            // 脱离的端点：退回一个由 GUI 写过的插槽锚点（WPF 那几家维护 slot.Anchor）；否则这个端点没有位置。
            var anchor = slot.Anchor;
            if (!double.IsNaN(anchor.Horizontal) && !double.IsNaN(anchor.Vertical))
                return new Point(anchor.Horizontal, anchor.Vertical);
            return null;
        }

        if (WorkflowPortGeometry.IndexOf(node, slot) is { } found)
        {
            return found.IsInput
                ? WorkflowPortGeometry.InputCenter(node, PortLayout)
                : WorkflowPortGeometry.OutputCenter(node, found.Index, PortLayout);
        }

        // 插槽挂在节点上、但不在当前的端口枚举里（选择器切换后留下的陈旧实例）：夹到卡片上，让连接保持可见、
        // 大致在位，等模型把插槽重新建立起来。
        return WorkflowPortGeometry.Center(
            node, PortLayout.DesignWidth - PortLayout.OutputInset, PortLayout.DesignHeight / 2.0, PortLayout);
    }

    // 两个端点在画布局部坐标下的位置（折叠后的端口 + 布局偏移 + 标尺预留），与 NodeView 的定位、表面的
    // OriginX/Y 同一个坐标系。任一端点没有位置时为 null。
    private (Point FromP, Point ToP)? EndpointsCanvasLocal()
    {
        if (_link is null) return null;

        var origin = _link.Sender.Parent?.Parent?.Layout.ActualOffset ?? new Offset();
        var rx = origin.Horizontal + WorkflowGridDecorator.RulerThickness;
        var ry = origin.Vertical + WorkflowGridDecorator.RulerThickness;

        var from = PortCenter(_link.Sender);
        var to = PortCenter(_link.Receiver);
        if (from is null || to is null) return null;
        return (new Point(from.Value.X + rx, from.Value.Y + ry),
                new Point(to.Value.X + rx, to.Value.Y + ry));
    }

    // 把元素移动并缩放到折线自己的画布局部包围盒，于是它的布局盒子总等于（留出边距后）画出来的内容 ——
    // 渲染器按盒子裁剪子元素、不看内容，而深缩放下消失的正是那个过期的「整块画布」盒子。
    private void UpdateBounds()
    {
        if (_link is null || !_link.IsVisible || IsDragPreview(_link)) return;
        if (EndpointsCanvasLocal() is not { } ep) return;

        double x1 = Math.Min(ep.FromP.X, ep.ToP.X) - BoxPad;
        double y1 = Math.Min(ep.FromP.Y, ep.ToP.Y) - BoxPad;
        double x2 = Math.Max(ep.FromP.X, ep.ToP.X) + BoxPad;
        double y2 = Math.Max(ep.FromP.Y, ep.ToP.Y) + BoxPad;

        _viewX = x1;
        _viewY = y1;
        Canvas.SetLeft(this, _viewX);
        Canvas.SetTop(this, _viewY);
        Width = Math.Max(1, x2 - x1);
        Height = Math.Max(1, y2 - y1);
    }
}
