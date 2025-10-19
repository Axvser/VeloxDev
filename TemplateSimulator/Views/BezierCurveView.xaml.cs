using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.Core.WorkflowSystem;

namespace TemplateSimulator.Views;

public partial class BezierCurveView : UserControl
{
    public BezierCurveView()
    {
        InitializeComponent();
        IsHitTestVisible = false;
        Panel.SetZIndex(this, -100);
    }

    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(
            nameof(StartLeft),
            typeof(double),
            typeof(BezierCurveView),
            new PropertyMetadata(0d, OnStartLeftChanged));

    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(
            nameof(StartTop),
            typeof(double),
            typeof(BezierCurveView),
            new PropertyMetadata(0d, OnStartTopChanged));

    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(
            nameof(EndLeft),
            typeof(double),
            typeof(BezierCurveView),
            new PropertyMetadata(0d, OnEndLeftChanged));

    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(
            nameof(EndTop),
            typeof(double),
            typeof(BezierCurveView),
            new PropertyMetadata(0d, OnEndTopChanged));

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

    private static void OnStartLeftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BezierCurveView)d;
        control.OnStartLeftChanged((double)e.OldValue, (double)e.NewValue);
    }

    private static void OnStartTopChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BezierCurveView)d;
        control.OnStartTopChanged((double)e.OldValue, (double)e.NewValue);
    }

    private static void OnEndLeftChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BezierCurveView)d;
        control.OnEndLeftChanged((double)e.OldValue, (double)e.NewValue);
    }

    private static void OnEndTopChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BezierCurveView)d;
        control.OnEndTopChanged((double)e.OldValue, (double)e.NewValue);
    }

    private static void OnCanRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BezierCurveView)d;
        control.OnCanRenderChanged((bool)e.OldValue, (bool)e.NewValue);
    }

    private void OnStartLeftChanged(double oldValue, double newValue)
    {
        InvalidateVisual();
    }

    private void OnStartTopChanged(double oldValue, double newValue)
    {
        InvalidateVisual();
    }

    private void OnEndLeftChanged(double oldValue, double newValue)
    {
        InvalidateVisual();
    }

    private void OnEndTopChanged(double oldValue, double newValue)
    {
        InvalidateVisual();
    }

    private void OnCanRenderChanged(bool oldValue, bool newValue)
    {
        InvalidateVisual();
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

        // 渲染连接线
        var pen = new Pen(Brushes.Cyan, 2);
        context.DrawGeometry(null, pen, pathGeometry);
    }
}