using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Demo.Views;

public sealed partial class LinkView : UserControl
{
    public LinkView()
    {
        InitializeComponent();
        IsHitTestVisible = false;
        Canvas.SetZIndex(this, -100);

        // 创建路径用于绘制贝塞尔曲线
        _path = new Path
        {
            Stroke = new SolidColorBrush(Colors.Cyan),
            StrokeThickness = 2,
            IsHitTestVisible = false
        };

        this.Content = _path;
    }

    private readonly Path _path;

    // 依赖属性
    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(
            nameof(StartLeft),
            typeof(double),
            typeof(LinkView),
            new PropertyMetadata(0d, OnPositionChanged));

    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(
            nameof(StartTop),
            typeof(double),
            typeof(LinkView),
            new PropertyMetadata(0d, OnPositionChanged));

    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(
            nameof(EndLeft),
            typeof(double),
            typeof(LinkView),
            new PropertyMetadata(0d, OnPositionChanged));

    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(
            nameof(EndTop),
            typeof(double),
            typeof(LinkView),
            new PropertyMetadata(0d, OnPositionChanged));

    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(
            nameof(CanRender),
            typeof(bool),
            typeof(LinkView),
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
        var control = (LinkView)d;
        control.UpdateLinkPath();
    }

    private static void OnCanRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LinkView)d;
        control.OnCanRenderChanged((bool)e.OldValue, (bool)e.NewValue);
    }

    private void OnCanRenderChanged(bool oldValue, bool newValue)
    {
        UpdateLinkPath();
    }

    private void UpdateLinkPath()
    {
        if (!CanRender)
        {
            _path.Data = null;
            return;
        }

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
        var pathGeometry = new PathGeometry();
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
        pathGeometry.Figures.Add(pathFigure);

        _path.Data = pathGeometry;
    }
}