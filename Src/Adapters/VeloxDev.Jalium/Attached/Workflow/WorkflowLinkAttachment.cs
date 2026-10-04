using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Everything a Jalium element needs to become one link view, attached to it in a single call. The element stays
/// yours — what a link looks like is your <c>OnRender</c> — and this helper supplies only what you cannot reasonably
/// write yourself: the binding and endpoint tracking (rebind-safe, for a pooled view), the self-bounding that keeps
/// the renderer from culling the link, the geometry, the hit-test contract, and this link's own pointer events.
/// </summary>
/// <remarks>
/// <para>
/// Attach it in your view's constructor and give it the port layout and the stroke; the view is then an ordinary
/// element that draws the link however it wants:
/// <code>
/// public sealed class LinkView : FrameworkElement
/// {
///     private readonly WorkflowLinkAttachment link;
///
///     public LinkView()
///     {
///         link = WorkflowLinkAttachment.Attach(this);
///         link.PortLayout = SlotView.Layout;
///         link.LinkColor = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);
///         link.Thickness = 2;
///     }
///
///     protected override void OnRender(DrawingContext dc)
///     {
///         base.OnRender(dc);
///         link.Paint(dc);   // 最短的一版；不想这样画，就照着 link.Curve 自己画
///     }
/// }
/// </code>
/// </para>
/// <para>
/// The element is positioned at the curve's own canvas-local bounding box and sized to it, and the geometry is
/// handed back in element-local coordinates — so the layout box always equals the drawn content and travels with it
/// through any zoom. That is not a stylistic choice: the renderer culls a child entirely when its layout box misses
/// the viewport clip and never looks at the drawn content, so a full-canvas box that goes stale during a zoom burst
/// takes the whole link layer with it while the self-bounded cards keep rendering.
/// </para>
/// </remarks>
public sealed class WorkflowLinkAttachment
{
    // 一个元素一份：Attach 幂等，池与宿主都用 For 找它。
    private static readonly ConditionalWeakTable<FrameworkElement, WorkflowLinkAttachment> Attachments = new();

    // 控制点的最小水平拉出量：两个端口靠得很近时，0.5·dx 会让曲线退化成一条直线段，
    // 失去「从端口水平出来」的形状。
    private const double DefaultPullMinimum = 40;

    /// <summary>Pen half-width plus antialias air, so the box always covers the stroke.</summary>
    private const double BoxPad = 6;

    private readonly FrameworkElement target;

    private IWorkflowLinkViewModel? link;
    private INotifyPropertyChanged? layoutNotify;
    private PropertyChangedEventHandler? layoutHandler;
    private INotifyPropertyChanged? senderNodeNotify;
    private INotifyPropertyChanged? receiverNodeNotify;
    private WorkflowPortLayout portLayout = new();
    private Color linkColor = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);
    private double thickness = 2;
    private double pullMinimum = DefaultPullMinimum;

    // 元素被摆在画布局部坐标的哪个位置（Canvas.Left/Top），以及曲线在元素局部坐标下的四个控制点。
    private double viewX;
    private double viewY;
    private Point[]? curve;

    /// <summary>Attaches the link machinery to <paramref name="target"/>, or returns the one already attached.</summary>
    /// <param name="target">The element that is going to draw one link.</param>
    /// <returns>The attachment, for chaining the palette and the event subscriptions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public static WorkflowLinkAttachment Attach(FrameworkElement target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        return Attachments.GetValue(target, static element => new WorkflowLinkAttachment(element));
    }

    /// <summary>The attachment on <paramref name="target"/>, or <see langword="null"/> when it has none.</summary>
    /// <param name="target">The element to look up.</param>
    public static WorkflowLinkAttachment? For(FrameworkElement target)
        => target is not null && Attachments.TryGetValue(target, out var attachment) ? attachment : null;

    private WorkflowLinkAttachment(FrameworkElement target)
    {
        this.target = target;

        // 连线不吃指针（表面统一处理命中），并且永远排在卡片后面。
        target.IsHitTestVisible = false;
        Panel.SetZIndex(target, -100);
        target.DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Raised when the pointer arrives over this link.</summary>
    public event EventHandler<WorkflowPointerEnteredEventArgs>? PointerEntered;

    /// <summary>Raised when the pointer leaves this link.</summary>
    public event EventHandler<WorkflowPointerExitedEventArgs>? PointerLeft;

    /// <summary>Raised when a pointer button goes down over this link.</summary>
    public event EventHandler<WorkflowPointerPressedEventArgs>? PointerPressed;

    /// <summary>Raised when a pointer button comes up over this link.</summary>
    public event EventHandler<WorkflowPointerReleasedEventArgs>? PointerReleased;

    /// <summary>Raised when a key goes down while this link is the key's target (the pointer is on it).</summary>
    public event EventHandler<WorkflowKeyDownEventArgs>? KeyDown;

    /// <summary>Raised when a key comes up while this link is the key's target.</summary>
    public event EventHandler<WorkflowKeyUpEventArgs>? KeyUp;

    /// <summary>The link this element currently draws, taken from the <c>DataContext</c>.</summary>
    public IWorkflowLinkViewModel? Link => link;

    /// <summary>
    /// The four Bézier control points of the curve, in this element's own coordinates — what <see cref="Paint"/>
    /// draws, published for hit-testing at the same time. <see langword="null"/> when there is nothing to draw.
    /// </summary>
    public Point[]? Curve => curve;

    /// <summary>Where the cards put their ports, in design coordinates.</summary>
    /// <remarks>Assign the same instance the cards and the surface use, or the endpoints will miss the ports.</remarks>
    public WorkflowPortLayout PortLayout
    {
        get => portLayout;
        set
        {
            if (ReferenceEquals(portLayout, value) || value is null) return;
            portLayout = value;
            UpdateGeometry();
        }
    }

    /// <summary>The stroke colour.</summary>
    public Color LinkColor
    {
        get => linkColor;
        set
        {
            if (linkColor == value) return;
            linkColor = value;
            target.InvalidateVisual();
        }
    }

    /// <summary>The stroke width, in pixels.</summary>
    public double Thickness
    {
        get => thickness;
        set
        {
            if (thickness == value) return;
            thickness = value;
            UpdateGeometry();
        }
    }

    /// <summary>The least horizontal distance each control point is pulled away from its own endpoint.</summary>
    /// <remarks>
    /// The pull actually used is the larger of this and half the horizontal gap between the endpoints, so two ports
    /// close together do not degenerate the curve into a straight segment.
    /// </remarks>
    public double PullMinimum
    {
        get => pullMinimum;
        set
        {
            if (pullMinimum == value || value < 0) return;
            pullMinimum = value;
            UpdateGeometry();
        }
    }

    /// <summary>
    /// Binds this element to a link: subscribes to it, to both its endpoints and to the layout that collapses them,
    /// and rebuilds the geometry. Called by the view pool when a pooled element is handed to another link.
    /// </summary>
    /// <param name="link">The link, or <see langword="null"/> to detach.</param>
    public void Bind(IWorkflowLinkViewModel? link)
    {
        Unsubscribe();
        HookInput(this.link, null);

        this.link = link;
        HookInput(null, link);

        if (link is INotifyPropertyChanged linkNotify) linkNotify.PropertyChanged += OnLinkChanged;
        if (link?.Sender is INotifyPropertyChanged senderNotify) senderNotify.PropertyChanged += OnLinkChanged;
        if (link?.Receiver is INotifyPropertyChanged receiverNotify) receiverNotify.PropertyChanged += OnLinkChanged;

        // 表面按 node.Anchor 摆连线（它从不写 slot.Anchor），所以连线必须直接跟着两端节点走 —— 拖一张卡片，
        // 连接立刻跟上。
        if (link?.Sender?.Parent is INotifyPropertyChanged senderNode)
        {
            senderNodeNotify = senderNode;
            senderNode.PropertyChanged += OnNodeChanged;
        }

        if (link?.Receiver?.Parent is INotifyPropertyChanged receiverNode)
        {
            receiverNodeNotify = receiverNode;
            receiverNode.PropertyChanged += OnNodeChanged;
        }

        // 任何布局变化（缩放 Scale、覆盖增长 → ActualOffset）都会折叠端点，所以自适应盒子必须与重画同频重算。
        if (link?.Sender?.Parent?.Parent?.Layout is INotifyPropertyChanged layout)
        {
            layoutNotify = layout;
            layoutHandler = (_, _) => UpdateGeometry();
            layout.PropertyChanged += layoutHandler;
        }

        UpdateGeometry();
    }

    /// <summary>
    /// Draws the resting line for <see cref="Curve"/>, which is what a link looks like with nothing else added.
    /// Call it from your <c>OnRender</c>, or ignore it and draw the curve yourself — either way the geometry has
    /// already been published for hit-testing.
    /// </summary>
    /// <param name="dc">The drawing context.</param>
    public void Paint(DrawingContext dc)
    {
        if (dc is null) throw new ArgumentNullException(nameof(dc));
        if (curve is not { Length: 4 } points) return;

        // 与其它 GUI 一致的三次贝塞尔（镜像 workflow-tree-view）：两个控制点各自水平拉开，
        // 连线因此从两端水平出线、中间平滑过渡，没有折角。
        var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
        figure.Segments.Add(new BezierSegment(points[1], points[2], points[3], true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(linkColor), thickness), geometry);
    }

    // 换绑就是换订阅：这条线的四个指针事件跟着走，池化元素因此不需要视图自己记一份。
    private void HookInput(IWorkflowLinkViewModel? previous, IWorkflowLinkViewModel? next)
    {
        if (previous?.GetHelper() is IWorkflowInputEvents old)
        {
            old.Input.PointerEntered -= OnInputPointerEntered;
            old.Input.PointerExited -= OnInputPointerExited;
            old.Input.PointerPressed -= OnInputPointerPressed;
            old.Input.PointerReleased -= OnInputPointerReleased;
            old.Input.KeyDown -= OnInputKeyDown;
            old.Input.KeyUp -= OnInputKeyUp;
        }

        if (next?.GetHelper() is IWorkflowInputEvents now)
        {
            now.Input.PointerEntered += OnInputPointerEntered;
            now.Input.PointerExited += OnInputPointerExited;
            now.Input.PointerPressed += OnInputPointerPressed;
            now.Input.PointerReleased += OnInputPointerReleased;
            now.Input.KeyDown += OnInputKeyDown;
            now.Input.KeyUp += OnInputKeyUp;
        }
    }

    private void OnInputPointerEntered(object? sender, WorkflowPointerEnteredEventArgs e) => PointerEntered?.Invoke(this, e);

    private void OnInputPointerExited(object? sender, WorkflowPointerExitedEventArgs e) => PointerLeft?.Invoke(this, e);

    private void OnInputPointerPressed(object? sender, WorkflowPointerPressedEventArgs e) => PointerPressed?.Invoke(this, e);

    private void OnInputPointerReleased(object? sender, WorkflowPointerReleasedEventArgs e) => PointerReleased?.Invoke(this, e);

    private void OnInputKeyDown(object? sender, WorkflowKeyDownEventArgs e) => KeyDown?.Invoke(this, e);

    private void OnInputKeyUp(object? sender, WorkflowKeyUpEventArgs e) => KeyUp?.Invoke(this, e);

    private void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
        => Bind(target.DataContext as IWorkflowLinkViewModel);

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            UpdateGeometry();
        }
    }

    private void OnLinkChanged(object? sender, PropertyChangedEventArgs e) => UpdateGeometry();

    // 几何、命中的曲线、自适应盒子、以及画要用的四个点，全在这一处算出来 —— 不依赖视图是否真的画了一笔：
    // 画法归用户，命中契约与盒子归这里。
    private void UpdateGeometry()
    {
        if (link is null || !link.IsVisible || IsDragPreview(link))
        {
            Clear();
            return;
        }

        if (EndpointsCanvasLocal() is not { } ep)
        {
            Clear();
            return;
        }

        // 先摆盒子：下面的元素局部坐标要用新的 viewX/viewY 折回去。
        double x1 = Math.Min(ep.FromP.X, ep.ToP.X) - BoxPad;
        double y1 = Math.Min(ep.FromP.Y, ep.ToP.Y) - BoxPad;
        double x2 = Math.Max(ep.FromP.X, ep.ToP.X) + BoxPad;
        double y2 = Math.Max(ep.FromP.Y, ep.ToP.Y) + BoxPad;

        viewX = x1;
        viewY = y1;
        Canvas.SetLeft(target, viewX);
        Canvas.SetTop(target, viewY);
        target.Width = Math.Max(1, x2 - x1);
        target.Height = Math.Max(1, y2 - y1);

        // 端点在**模型系**里给：Core 的命中判定会拿指针去比 node.Anchor / node.Size（模型系），而本家
        // 表面系比模型系多一个 Origin（= ActualOffset + 标尺带）。不同系时遮挡守卫会把画得出来的一段
        // 当成「压在卡片下面」而跳过整条线 —— 症状是悬停不亮、右键无菜单、Delete 到不了路由（本家实测）。
        // 所以这里减掉 Origin，与表面的指针（见 WorkflowTreeView.RoutePointer）对齐。
        var worldFrom = new Point(ep.FromP.X - ep.OriginX, ep.FromP.Y - ep.OriginY);
        var worldTo = new Point(ep.ToP.X - ep.OriginX, ep.ToP.Y - ep.OriginY);

        // 四个控制点归 Core 算：每个口沿**自己实际贴着的那条边**向外拉，规则与其余六家同一条 ——
        // 就是「端口实际在哪、离父节点哪条边最近」，与本家端口画在哪、谁当发送端都无关。
        link.PublishCurve(
            LinkCurve.BuildLinkCubic(
                link, worldFrom.X, worldFrom.Y, worldTo.X, worldTo.Y,
                pullMinimum, LinkCurve.DefaultSampleCount),
            target);

        // 把模型系的四个点烘焙回元素局部：模型 → 画布（+Origin）→ 元素（−viewX/viewY）。
        // 这一步抵消掉上面那次重定位，屏幕上的输出与画在 (0,0) 完全一样。
        var points = LinkCurve.LinkCurvePoints(
            link, worldFrom.X, worldFrom.Y, worldTo.X, worldTo.Y, pullMinimum);
        curve =
        [
            new Point(points[0].X + ep.OriginX - viewX, points[0].Y + ep.OriginY - viewY),
            new Point(points[1].X + ep.OriginX - viewX, points[1].Y + ep.OriginY - viewY),
            new Point(points[2].X + ep.OriginX - viewX, points[2].Y + ep.OriginY - viewY),
            new Point(points[3].X + ep.OriginX - viewX, points[3].Y + ep.OriginY - viewY),
        ];

        target.InvalidateVisual();
    }

    // 画不出来：曲线撤掉（不留上一条，否则一个已经不画线的位置继续可命中），盒子也放着不动。
    private void Clear()
    {
        curve = null;
        link?.PublishCurve(null);
        target.InvalidateVisual();
    }

    private void Detach()
    {
        Unsubscribe();
        HookInput(link, null);
        Clear();
        link = null;
    }

    private void Unsubscribe()
    {
        if (link is INotifyPropertyChanged linkNotify) linkNotify.PropertyChanged -= OnLinkChanged;
        if (link?.Sender is INotifyPropertyChanged senderNotify) senderNotify.PropertyChanged -= OnLinkChanged;
        if (link?.Receiver is INotifyPropertyChanged receiverNotify) receiverNotify.PropertyChanged -= OnLinkChanged;

        if (senderNodeNotify is not null)
        {
            senderNodeNotify.PropertyChanged -= OnNodeChanged;
            senderNodeNotify = null;
        }

        if (receiverNodeNotify is not null)
        {
            receiverNodeNotify.PropertyChanged -= OnNodeChanged;
            receiverNodeNotify = null;
        }

        if (layoutNotify is not null)
        {
            layoutNotify.PropertyChanged -= layoutHandler;
            layoutNotify = null;
            layoutHandler = null;
        }
    }

    // 只有树的拖拽预览（VirtualLink）为真：它的端点是占位的 SlotDefaultViewModel，没挂在节点上。
    // 表面自己在 OnPostRender 里画那个预览，所以池化视图必须跳过它。真实连线永远不是这个类型 ——
    // 即使插槽被摘下来也照画不误，而不是消失。
    private static bool IsDragPreview(IWorkflowLinkViewModel link)
        => link.Sender is SlotDefaultViewModel && link.Receiver is SlotDefaultViewModel;

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
                ? WorkflowPortGeometry.InputCenter(node, portLayout)
                : WorkflowPortGeometry.OutputCenter(node, found.Index, portLayout);
        }

        // 插槽挂在节点上、但不在当前的端口枚举里（选择器切换后留下的陈旧实例）：夹到卡片上，让连接保持可见、
        // 大致在位，等模型把插槽重新建立起来。
        return WorkflowPortGeometry.Center(
            node, portLayout.DesignWidth - portLayout.OutputInset, portLayout.DesignHeight / 2.0, portLayout);
    }

    // 两个端点在画布局部坐标下的位置（折叠后的端口 + 布局偏移 + 标尺预留），与 NodeView 的定位、表面的
    // OriginX/Y 同一个坐标系。任一端点没有位置时为 null。
    // OriginX/Y 一并交回：发布给 Core 的曲线要减掉它们换到模型系，见 UpdateGeometry。
    private (Point FromP, Point ToP, double OriginX, double OriginY)? EndpointsCanvasLocal()
    {
        if (link is null) return null;

        var origin = link.Sender.Parent?.Parent?.Layout.ActualOffset ?? new Offset();
        var rx = origin.Horizontal + WorkflowGridDecorator.RulerThickness;
        var ry = origin.Vertical + WorkflowGridDecorator.RulerThickness;

        var from = PortCenter(link.Sender);
        var to = PortCenter(link.Receiver);
        if (from is null || to is null) return null;
        return (new Point(from.Value.X + rx, from.Value.Y + ry),
                new Point(to.Value.X + rx, to.Value.Y + ry), rx, ry);
    }

}
