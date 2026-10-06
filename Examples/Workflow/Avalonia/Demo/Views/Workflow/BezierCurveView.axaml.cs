using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace Demo;

/// <summary>
/// Cubic Bézier connection that leaves each port horizontally.
/// <para>
/// The no-band counterpart of <see cref="PolylineCurveView"/>: same curve, without the travelling light. It
/// flattens that curve into a <see cref="LinkCurve"/> and publishes it to the link's helper, so the surface
/// hit-tests the exact shape this view painted; hover and press are routed by Core's shared input router. The
/// highlight is this demo's: it subscribes to this link's own <see cref="IInputEvents"/> pointer events and
/// writes <see cref="IsHighlighted"/> when the hovered link is its own, which then only decides how lit it looks.
/// </para>
/// </summary>
public partial class BezierCurveView : Control
{
    // 控制点的最小水平拉出量：两个端口靠得很近时，0.5·dx 会让曲线退化成一条直线段。
    private const double PullMinimum = 40;

    // Core 的扁平化曲线，与命中契约共享；本视图只拿它判「画出来的这条」。
    private LinkCurve? _curve;

    public BezierCurveView()
    {
        InitializeComponent();
        IsHitTestVisible = true;
        Focusable = true;

        // 与 Polyline 那条同款：悬停取焦点（Delete 需要）会连带触发 ScrollViewer 的「把焦点元素滚进视口」
        // （BringIntoViewOnFocusChange 默认 true），而本视图是整块画布大小 ⇒ 鼠标碰到线画布就跳一段。
        // 在发源地吃掉这条请求，节点卡的自动滚进视口不受影响。
        AddHandler(RequestBringIntoViewEvent, (_, e) => e.Handled = true);

        RefreshGeometry();
    }

    #region Avalonia property definitions

    public static readonly StyledProperty<double> StartLeftProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(StartLeft));

    public static readonly StyledProperty<double> StartTopProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(StartTop));

    public static readonly StyledProperty<double> EndLeftProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(EndLeft));

    public static readonly StyledProperty<double> EndTopProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(EndTop));

    public static readonly StyledProperty<bool> IsHighlightedProperty =
        AvaloniaProperty.Register<BezierCurveView, bool>(nameof(IsHighlighted), false);

    public static readonly StyledProperty<Color> HighlightColorProperty =
        AvaloniaProperty.Register<BezierCurveView, Color>(nameof(HighlightColor), Color.Parse("#FFFFFFFF"));

    public static readonly StyledProperty<bool> CanRenderProperty =
        AvaloniaProperty.Register<BezierCurveView, bool>(nameof(CanRender), true);

    public static readonly StyledProperty<bool> IsVirtualProperty =
        AvaloniaProperty.Register<BezierCurveView, bool>(nameof(IsVirtual), false);

    public static readonly StyledProperty<Color> LineColorProperty =
        AvaloniaProperty.Register<BezierCurveView, Color>(nameof(LineColor), Color.Parse("#DDFFFFFF"));

    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(LineThickness), 2.0);

    public static readonly StyledProperty<IList<double>> DashArrayProperty =
        AvaloniaProperty.Register<BezierCurveView, IList<double>>(nameof(DashArray), [0d]);

    public double StartLeft
    {
        get => GetValue(StartLeftProperty);
        set => SetValue(StartLeftProperty, value);
    }

    public double StartTop
    {
        get => GetValue(StartTopProperty);
        set => SetValue(StartTopProperty, value);
    }

    public double EndLeft
    {
        get => GetValue(EndLeftProperty);
        set => SetValue(EndLeftProperty, value);
    }

    public double EndTop
    {
        get => GetValue(EndTopProperty);
        set => SetValue(EndTopProperty, value);
    }

    public bool IsHighlighted
    {
        get => GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    public Color HighlightColor
    {
        get => GetValue(HighlightColorProperty);
        set => SetValue(HighlightColorProperty, value);
    }

    public bool CanRender
    {
        get => GetValue(CanRenderProperty);
        set => SetValue(CanRenderProperty, value);
    }

    public bool IsVirtual
    {
        get => GetValue(IsVirtualProperty);
        set => SetValue(IsVirtualProperty, value);
    }

    public Color LineColor
    {
        get => GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public double LineThickness
    {
        get => GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    public IList<double> DashArray
    {
        get => GetValue(DashArrayProperty);
        set => SetValue(DashArrayProperty, value);
    }

    static BezierCurveView()
    {
        AffectsRender<BezierCurveView>(
            StartLeftProperty, StartTopProperty, EndLeftProperty, EndTopProperty,
            CanRenderProperty, IsVirtualProperty, LineColorProperty,
            LineThicknessProperty, DashArrayProperty, IsHighlightedProperty, HighlightColorProperty);
    }

    #endregion

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        PublishCurve();
        ResubscribeHub();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnsubscribeHub();
    }

    // VeloxDev customization: 悬停高亮是本 demo 的。订**这条线自己的** Helper 就够了 —— 路由会告诉它指针
    // 什么时候进来、什么时候离开，这里不必再去比 target 是谁。视图比树活得短，改绑与摘树都要退订。
    private IWorkflowLinkViewModel? _inputLink;

    private void ResubscribeHub()
    {
        var link = DataContext as IWorkflowLinkViewModel;
        if (ReferenceEquals(link, _inputLink)) return;

        UnsubscribeHub();
        if (link?.GetHelper() is not IInputEvents events) return;

        _inputLink = link;
        events.Input.PointerEntered += OnPointerEntered;
        events.Input.PointerExited += OnPointerExited;
    }

    private void UnsubscribeHub()
    {
        if (_inputLink?.GetHelper() is not IInputEvents events) return;

        events.Input.PointerEntered -= OnPointerEntered;
        events.Input.PointerExited -= OnPointerExited;
        _inputLink = null;
    }

    private void OnPointerEntered(object? sender, PointerEnteredEventArgs e) => IsHighlighted = true;

    private void OnPointerExited(object? sender, PointerExitedEventArgs e) => IsHighlighted = false;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == StartLeftProperty || change.Property == StartTopProperty
            || change.Property == EndLeftProperty || change.Property == EndTopProperty)
        {
            RefreshGeometry();
        }

        if (change.Property == DataContextProperty)
        {
            // 池化改绑：旧链接不能留着一条指向本视图的曲线，否则它的命中会答在一个已经画着别人的控件上。
            if (change.OldValue is IWorkflowLinkViewModel old && !ReferenceEquals(old, change.NewValue))
            {
                old.PublishCurve(null);
            }

            PublishCurve();
            ResubscribeHub();
        }

        // UsePolyline 在两个视图之间切换显示；接手显示的那个要把曲线（与 sender）重新挂到自己身上。
        if (change.Property == IsVisibleProperty)
        {
            PublishCurve();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!CanRender) return;

        var geometry = CreateBezierGeometry();
        if (geometry == null) return;

        DrawBezierLine(context, geometry);
    }

    private void DrawBezierLine(DrawingContext context, StreamGeometry geometry)
    {
        var color = IsHighlighted ? HighlightColor : LineColor;
        var thickness = IsHighlighted ? LineThickness + 1.5 : LineThickness;
        var brush = new ImmutableSolidColorBrush(color);

        Pen pen;
        if (IsVirtual || (DashArray != null && DashArray.Count > 0))
        {
            var dashArray = IsVirtual ? [4.0, 2.0] : DashArray;
            pen = new Pen(brush, thickness) { DashStyle = new DashStyle(dashArray, 0) };
        }
        else
        {
            pen = new Pen(brush, thickness);
        }

        if (IsHighlighted)
        {
            var glowPen = new Pen(new ImmutableSolidColorBrush(color, 0.25), thickness + 6);
            context.DrawGeometry(null, glowPen, geometry);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    // 曲线由 Core 构建一次并发布给命中契约；绘制仍用同一条公式画平滑贝塞尔（与 LinkCurve 同一拉出量）。
    private void RefreshGeometry()
    {
        _curve = LinkCurve.BuildLinkCubic(DataContext as IWorkflowLinkViewModel, StartLeft, StartTop, EndLeft, EndTop, PullMinimum);
        PublishCurve();
    }

    // 把画出来的形状交给链接的 Helper，界面据此判命中（见 ILinkHitTestable）。
    // 同一链接由两个视图轮流显示（UsePolyline）：没有在显示的那个不能发布 —— 命中契约只存一个
    // Visual，菜单等事件的 sender 必须落在真正被看到的那个控件上，否则菜单会挂在隐藏控件上。
    private void PublishCurve()
    {
        if (!IsVisible || DataContext is not IWorkflowLinkViewModel link)
        {
            return;
        }

        link.PublishCurve(_curve, this);
    }

    // 两个控制点各沿**自己那个口**所在边的外法线拉（Core 的 LinkCurve.LinkCurvePoints 给的），
    // 所以口在上/下边时竖直出线、反向连线也不会把控制点戳进自己节点。
    // 与发布给命中的那条曲线同源 —— 两处若各推一遍几何，弯的地方命中就会对不上指针。
    private (Point C1, Point C2) Controls()
    {
        var points = LinkCurve.LinkCurvePoints(
            DataContext as IWorkflowLinkViewModel, StartLeft, StartTop, EndLeft, EndTop, PullMinimum);
        return (new Point(points[1].X, points[1].Y), new Point(points[2].X, points[2].Y));
    }

    private StreamGeometry? CreateBezierGeometry()
    {
        var (cp1, cp2) = Controls();

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(StartLeft, StartTop), false);
            ctx.CubicBezierTo(cp1, cp2, new Point(EndLeft, EndTop));
        }
        return geometry;
    }
}
