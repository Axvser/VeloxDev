using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.Core.WorkflowSystem;

namespace Demo.Views;

public partial class BezierCurveView : UserControl
{
    public BezierCurveView()
    {
        InitializeComponent();
        IsHitTestVisible = false;
        Panel.SetZIndex(this, -100);
    }

    // 使用 Anchor 类作为依赖属性类型
    public static readonly DependencyProperty StartAnchorProperty =
        DependencyProperty.Register(
            nameof(StartAnchor),
            typeof(Anchor),
            typeof(BezierCurveView),
            new FrameworkPropertyMetadata(
                new Anchor(),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnRenderPropertyChanged));

    public static readonly DependencyProperty EndAnchorProperty =
        DependencyProperty.Register(
            nameof(EndAnchor),
            typeof(Anchor),
            typeof(BezierCurveView),
            new FrameworkPropertyMetadata(
                new Anchor(),
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnRenderPropertyChanged));

    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(
            nameof(CanRender),
            typeof(bool),
            typeof(BezierCurveView),
            new FrameworkPropertyMetadata(
                true,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnRenderPropertyChanged));

    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(
            nameof(IsVirtual),
            typeof(bool),
            typeof(BezierCurveView),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.AffectsRender,
                OnRenderPropertyChanged));

    public Anchor StartAnchor
    {
        get => (Anchor)GetValue(StartAnchorProperty);
        set => SetValue(StartAnchorProperty, value);
    }

    public Anchor EndAnchor
    {
        get => (Anchor)GetValue(EndAnchorProperty);
        set => SetValue(EndAnchorProperty, value);
    }

    public bool CanRender
    {
        get => (bool)GetValue(CanRenderProperty);
        set => SetValue(CanRenderProperty, value);
    }

    public bool IsVirtual
    {
        get => (bool)GetValue(IsVirtualProperty);
        set => SetValue(IsVirtualProperty, value);
    }

    private static void OnRenderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BezierCurveView)d;
        control.InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        if (!CanRender || StartAnchor is null || EndAnchor is null)
            return;

        // 计算差距
        var diffx = EndAnchor.Left - StartAnchor.Left;
        var diffy = EndAnchor.Top - StartAnchor.Top;

        // 基于点位差距计算控制点
        var cp1 = new Point(
            StartAnchor.Left + diffx * 0.618,
            StartAnchor.Top + diffy * 0.1);
        var cp2 = new Point(
            EndAnchor.Left - diffx * 0.618,
            EndAnchor.Top - diffy * 0.1);

        // 创建贝塞尔曲线几何图形
        var pathFigure = new PathFigure
        {
            StartPoint = new Point(StartAnchor.Left, StartAnchor.Top),
            IsClosed = false
        };

        var bezierSegment = new BezierSegment
        {
            Point1 = cp1,
            Point2 = cp2,
            Point3 = new Point(EndAnchor.Left, EndAnchor.Top)
        };

        pathFigure.Segments.Add(bezierSegment);

        var pathGeometry = new PathGeometry();
        pathGeometry.Figures.Add(pathFigure);

        // 创建画笔并根据 IsVirtual 属性设置虚线样式
        var pen = new Pen(Brushes.Cyan, 2);
        if (IsVirtual)
        {
            pen.DashStyle = DashStyles.Dash;
        }

        // 渲染连接线
        context.DrawGeometry(null, pen, pathGeometry);

        // 在曲线终点绘制箭头
        DrawArrowhead(context, pathGeometry, pen);
    }

    /// <summary>
    /// 在路径终点绘制箭头
    /// </summary>
    private static void DrawArrowhead(DrawingContext context, PathGeometry pathGeometry, Pen pen)
    {
        if (pathGeometry.IsEmpty() || pathGeometry.Figures.Count == 0)
            return;

        var pathFigure = pathGeometry.Figures[0];
        if (pathFigure.Segments.Count == 0 || pathFigure.Segments[0] is not BezierSegment bezierSegment)
            return;

        // 获取曲线终点
        Point arrowTip = bezierSegment.Point3;

        // 计算曲线在终点的切线方向
        Vector tangentDirection = new(
            bezierSegment.Point3.X - bezierSegment.Point2.X,
            bezierSegment.Point3.Y - bezierSegment.Point2.Y);

        tangentDirection.Normalize();

        // 如果方向向量无效，使用默认方向
        if (double.IsNaN(tangentDirection.X) || double.IsNaN(tangentDirection.Y))
        {
            tangentDirection = new Vector(1, 0);
        }

        // 定义箭头大小
        double arrowLength = 12 + pen.Thickness * 2;
        double arrowWidth = 8 + pen.Thickness * 1.5;

        // 计算垂直于切线方向的向量
        Vector perpendicular = new(-tangentDirection.Y, tangentDirection.X);

        // 计算箭头三个顶点的位置
        Point arrowHeadBase = arrowTip - tangentDirection * arrowLength;
        Point arrowWing1 = arrowHeadBase + perpendicular * arrowWidth / 2;
        Point arrowWing2 = arrowHeadBase - perpendicular * arrowWidth / 2;

        // 创建箭头几何图形
        StreamGeometry arrowGeometry = new();
        using (StreamGeometryContext geometryContext = arrowGeometry.Open())
        {
            geometryContext.BeginFigure(arrowTip, true, true);
            geometryContext.LineTo(arrowWing1, true, false);
            geometryContext.LineTo(arrowWing2, true, false);
        }
        arrowGeometry.Freeze();

        // 绘制箭头
        context.DrawGeometry(pen.Brush, null, arrowGeometry);
        context.DrawGeometry(null, pen, arrowGeometry);
    }
}