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

    public static readonly DependencyProperty StartAnchorProperty =
        DependencyProperty.Register(
            nameof(StartAnchor),
            typeof(Anchor),
            typeof(BezierCurveView),
            new PropertyMetadata(new Anchor(), OnStartAnchorChanged));

    public static readonly DependencyProperty EndAnchorProperty =
        DependencyProperty.Register(
            nameof(EndAnchor),
            typeof(Anchor),
            typeof(BezierCurveView),
            new PropertyMetadata(new Anchor(), OnEndAnchorChanged));

    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(
            nameof(CanRender),
            typeof(bool),
            typeof(BezierCurveView),
            new PropertyMetadata(true, OnCanRenderChanged));

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

    private static void OnStartAnchorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BezierCurveView)d;
        control.OnStartAnchorChanged((Anchor)e.OldValue, (Anchor)e.NewValue);
    }

    private static void OnEndAnchorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BezierCurveView)d;
        control.OnEndAnchorChanged((Anchor)e.OldValue, (Anchor)e.NewValue);
    }

    private static void OnCanRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (BezierCurveView)d;
        control.OnCanRenderChanged((bool)e.OldValue, (bool)e.NewValue);
    }

    private void OnStartAnchorChanged(Anchor oldValue, Anchor newValue)
    {
        InvalidateVisual();
    }

    private void OnEndAnchorChanged(Anchor oldValue, Anchor newValue)
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

        // 渲染连接线
        var pen = new Pen(Brushes.Cyan, 2);
        context.DrawGeometry(null, pen, pathGeometry);
    }
}