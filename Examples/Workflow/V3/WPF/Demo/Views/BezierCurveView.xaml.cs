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

    // 拆分后的依赖属性
    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(
            nameof(StartLeft),
            typeof(double),
            typeof(BezierCurveView),
            new PropertyMetadata(0d, OnPositionChanged));

    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(
            nameof(StartTop),
            typeof(double),
            typeof(BezierCurveView),
            new PropertyMetadata(0d, OnPositionChanged));

    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(
            nameof(EndLeft),
            typeof(double),
            typeof(BezierCurveView),
            new PropertyMetadata(0d, OnPositionChanged));

    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(
            nameof(EndTop),
            typeof(double),
            typeof(BezierCurveView),
            new PropertyMetadata(0d, OnPositionChanged));

    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(
            nameof(CanRender),
            typeof(bool),
            typeof(BezierCurveView),
            new PropertyMetadata(true, OnCanRenderChanged));

    public double StartLeft
    {
        get => (double)GetValue(StartLeftProperty);
        set => SetValue(StartLeftProperty, value);
    }

    public double StartTop
    {
        get => (double)GetValue(StartTopProperty);
        set => SetValue(StartTopProperty, value);
    }

    public double EndLeft
    {
        get => (double)GetValue(EndLeftProperty);
        set => SetValue(EndLeftProperty, value);
    }

    public double EndTop
    {
        get => (double)GetValue(EndTopProperty);
        set => SetValue(EndTopProperty, value);
    }

    public bool CanRender
    {
        get => (bool)GetValue(CanRenderProperty);
        set => SetValue(CanRenderProperty, value);
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BezierCurveView)d;
        control.InvalidateVisual();
    }

    private static void OnCanRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BezierCurveView)d;
        control.OnCanRenderChanged();
    }

    private void OnCanRenderChanged()
    {
        InvalidateVisual();
    }

    // 新增 IsVirtual 依赖属性
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(
            nameof(IsVirtual),
            typeof(bool),
            typeof(BezierCurveView),
            new PropertyMetadata(false, OnRenderPropertyChanged)); // 默认值为 false，变化时触发重绘

    public bool IsVirtual
    {
        get => (bool)GetValue(IsVirtualProperty);
        set => SetValue(IsVirtualProperty, value);
    }

    // 修改原有的 OnPositionChanged 和 OnCanRenderChanged 方法，统一使用一个新的重绘触发方法
    private static void OnRenderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BezierCurveView)d;
        control.InvalidateVisual(); // 触发重绘
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);

        if (!CanRender)
            return;

        // 计算差距
        var diffx = EndLeft - StartLeft;
        var diffy = EndTop - StartTop;

        // 基于点位差距计算控制点
        var cp1 = new Point(
            StartLeft + diffx * 0.618,
            StartTop + diffy * 0.1);
        var cp2 = new Point(
            EndLeft - diffx * 0.618,
            EndTop - diffy * 0.1);

        // 创建贝塞尔曲线几何图形
        var pathFigure = new PathFigure
        {
            StartPoint = new Point(StartLeft, StartTop),
            IsClosed = false
        };

        var bezierSegment = new BezierSegment
        {
            Point1 = cp1,
            Point2 = cp2,
            Point3 = new Point(EndLeft, EndTop)
        };

        pathFigure.Segments.Add(bezierSegment);

        var pathGeometry = new PathGeometry();
        pathGeometry.Figures.Add(pathFigure);

        // 1. 创建画笔并根据 IsVirtual 属性设置虚线样式
        var pen = new Pen(Brushes.Cyan, 2);
        if (IsVirtual)
        {
            // 设置虚线样式：每段长度4，间隔2
            pen.DashStyle = DashStyles.Dash;
            // 你也可以自定义虚线 pattern，例如：
            // pen.DashStyle = new DashStyle(new double[] { 4, 2 }, 0);
        }
        var pen1 = new Pen(Brushes.Red, 2);

        // 渲染连接线
        context.DrawGeometry(null, pen, pathGeometry);

        // 2. 在曲线终点绘制箭头
        DrawArrowhead(context, pathGeometry, pen1);
    }

    /// <summary>
    /// 在路径终点绘制箭头
    /// </summary>
    /// <param name="context">绘制上下文</param>
    /// <param name="pathGeometry">路径几何图形，用于计算终点切线方向</param>
    /// <param name="pen">用于绘制箭头的画笔，继承其颜色和粗细</param>
    private static void DrawArrowhead(DrawingContext context, PathGeometry pathGeometry, Pen pen)
    {
        if (!pathGeometry.IsEmpty() && pathGeometry.Figures.Count > 0)
        {
            var pathFigure = pathGeometry.Figures[0];
            if (pathFigure.Segments.Count > 0 && pathFigure.Segments[0] is BezierSegment bezierSegment)
            {
                // 获取曲线终点
                Point arrowTip = bezierSegment.Point3;

                // 计算曲线在终点的切线方向（近似使用最后一个控制点到终点的方向）
                Vector tangentDirection = new(bezierSegment.Point3.X - bezierSegment.Point2.X,
                                                    bezierSegment.Point3.Y - bezierSegment.Point2.Y);
                tangentDirection.Normalize(); // 标准化为单位向量

                // 如果方向向量无效（长度为0），则使用一个默认方向（例如水平向右）
                if (double.IsNaN(tangentDirection.X) || double.IsNaN(tangentDirection.Y))
                {
                    tangentDirection = new Vector(1, 0);
                }

                // 定义箭头大小（可根据线条粗细调整）
                double arrowLength = 12 + pen.Thickness * 2; // 箭头长度
                double arrowWidth = 8 + pen.Thickness * 1.5; // 箭头底部宽度

                // 计算垂直于切线方向的向量（用于确定箭头两翼的方向）
                Vector perpendicular = new(-tangentDirection.Y, tangentDirection.X);

                // 计算箭头三个顶点的位置
                Point arrowHeadBase = arrowTip - tangentDirection * arrowLength; // 箭头底部中点（沿切线方向回退）
                Point arrowWing1 = arrowHeadBase + perpendicular * arrowWidth / 2; // 箭头左翼
                Point arrowWing2 = arrowHeadBase - perpendicular * arrowWidth / 2; // 箭头右翼

                // 创建箭头几何图形
                StreamGeometry arrowGeometry = new();
                using (StreamGeometryContext geometryContext = arrowGeometry.Open())
                {
                    geometryContext.BeginFigure(arrowTip, true, true); // 从箭头尖端开始，设置是否填充（true）和闭合（true）
                    geometryContext.LineTo(arrowWing1, true, false);
                    geometryContext.LineTo(arrowWing2, true, false);
                }
                arrowGeometry.Freeze(); // 冻结几何图形以提高性能

                // 绘制箭头（填充，使用线条颜色）
                context.DrawGeometry(pen.Brush, null, arrowGeometry);

                // 可选：用与线条相同的画笔描边箭头，使其边框与线条风格一致
                // 如果不需要特别强调箭头边框，可以省略下一行
                context.DrawGeometry(null, pen, arrowGeometry);
            }
        }
    }
}