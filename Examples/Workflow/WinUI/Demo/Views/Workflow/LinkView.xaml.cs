using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using VeloxDev.WorkflowSystem;
using Windows.Foundation;

namespace Demo.Views;

public sealed partial class LinkView : UserControl
{
    private static readonly DoubleCollection VirtualStrokeDashArray = [4, 2];

    public LinkView()
    {
        InitializeComponent();
        IsHitTestVisible = true;
        Canvas.SetZIndex(this, -100);

        // 创建主容器
        var container = new Grid();

        // 创建路径用于绘制贝塞尔曲线
        _path = new Path
        {
            Stroke = _strokeBrush,
            StrokeThickness = 2,
            IsHitTestVisible = false
        };

        // 创建箭头路径
        _arrowPath = new Path
        {
            Fill = _arrowBrush,
            IsHitTestVisible = false
        };

        container.Children.Add(_path);
        container.Children.Add(_arrowPath);
        this.Content = container;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PointerEntered += (_, _) => { IsHighlighted = true; Focus(FocusState.Pointer); };
        PointerExited += (_, _) => IsHighlighted = false;
        PointerMoved += OnHoverPointerMoved;
    }

    private readonly Path _path;
    private readonly Path _arrowPath;
    private readonly SolidColorBrush _strokeBrush = new(Colors.Cyan);
    private readonly SolidColorBrush _arrowBrush = new(Colors.Cyan);
    private readonly PathGeometry _pathGeometry = new();
    private readonly PathFigure _pathFigure = new() { IsClosed = false };
    private readonly BezierSegment _bezierSegment = new();
    private readonly PathGeometry _arrowGeometry = new();
    private readonly PathFigure _arrowFigure = new() { IsClosed = true };
    private readonly LineSegment _arrowLeftSegment = new();
    private readonly LineSegment _arrowRightSegment = new();
    private bool _updatePending;
    private bool _isLoaded;

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

    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(
            nameof(LineColor),
            typeof(Windows.UI.Color),
            typeof(LinkView),
            new PropertyMetadata(Colors.White, OnPositionChanged));

    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(
            nameof(IsVirtual),
            typeof(bool),
            typeof(LinkView),
            new PropertyMetadata(false, OnIsVirtualChanged));

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

    public Windows.UI.Color LineColor
    {
        get => (Windows.UI.Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public bool IsVirtual
    {
        get => (bool)GetValue(IsVirtualProperty);
        set => SetValue(IsVirtualProperty, value);
    }

    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(LinkView), new PropertyMetadata(false, OnPositionChanged));

    public bool IsHighlighted
    {
        get => (bool)GetValue(IsHighlightedProperty);
        set => SetValue(IsHighlightedProperty, value);
    }

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LinkView)d;
        control.ScheduleUpdate();
    }

    private static void OnCanRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LinkView)d;
        control.OnCanRenderChanged((bool)e.OldValue, (bool)e.NewValue);
    }

    private static void OnIsVirtualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LinkView)d;
        control.ScheduleUpdate();
    }

    private void OnCanRenderChanged(bool oldValue, bool newValue)
    {
        ScheduleUpdate();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        EnsureGeometry();
        ScheduleUpdate();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;
        _updatePending = false;
    }

    private void EnsureGeometry()
    {
        if (_pathFigure.Segments.Count == 0)
        {
            _pathFigure.Segments.Add(_bezierSegment);
            _pathGeometry.Figures.Add(_pathFigure);
        }

        if (_arrowFigure.Segments.Count == 0)
        {
            _arrowFigure.Segments.Add(_arrowLeftSegment);
            _arrowFigure.Segments.Add(_arrowRightSegment);
            _arrowGeometry.Figures.Add(_arrowFigure);
        }
    }

    private void ScheduleUpdate()
    {
        if (!_isLoaded)
        {
            return;
        }

        if (_updatePending)
        {
            return;
        }

        _updatePending = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            _updatePending = false;
            if (_isLoaded)
            {
                UpdateLinkPath();
            }
        });
    }

    private void UpdateLinkPath()
    {
        EnsureGeometry();

        if (!CanRender)
        {
            _path.Data = null;
            _arrowPath.Data = null;
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
        _pathFigure.StartPoint = new Point(StartLeft, StartTop);
        _bezierSegment.Point1 = cp1;
        _bezierSegment.Point2 = cp2;
        _bezierSegment.Point3 = new Point(EndLeft, EndTop);

        // 设置线条样式
        var lineColor = IsHighlighted ? Microsoft.UI.Colors.OrangeRed : LineColor;
        var strokeThickness = IsHighlighted ? 3.5 : 2.0;
        _strokeBrush.Color = lineColor;
        _path.StrokeThickness = strokeThickness;
        _arrowBrush.Color = lineColor;

        if (IsVirtual)
        {
            _path.StrokeDashArray = VirtualStrokeDashArray; // 虚线样式
        }
        else
        {
            _path.StrokeDashArray = null; // 实线
        }

        _path.Data = _pathGeometry;

        // 绘制箭头
        DrawArrow();
    }

    private void DrawArrow()
    {
        const double arrowLength = 10;
        const double arrowWidth = 6;

        // 计算终点处的切线方向（贝塞尔曲线终点导数）
        var diffx = EndLeft - StartLeft;
        var diffy = EndTop - StartTop;

        var cp1 = new Point(
            StartLeft + diffx * 0.618,
            StartTop + diffy * 0.1);

        var cp2 = new Point(
            EndLeft - diffx * 0.618,
            EndTop - diffy * 0.1);

        // 计算贝塞尔曲线在终点的切线方向
        var tangentX = 3 * (EndLeft - cp2.X);
        var tangentY = 3 * (EndTop - cp2.Y);

        // 归一化切线向量
        var length = Math.Sqrt(tangentX * tangentX + tangentY * tangentY);
        if (length <= double.Epsilon)
        {
            _arrowPath.Data = null;
            return;
        }

        var unitTangentX = tangentX / length;
        var unitTangentY = tangentY / length;

        // 计算法线向量（旋转90度）
        var unitNormalX = -unitTangentY;
        var unitNormalY = unitTangentX;

        // 计算箭头三个点
        var arrowTip = new Point(EndLeft, EndTop);
        var arrowLeft = new Point(
            EndLeft - arrowLength * unitTangentX + arrowWidth * unitNormalX,
            EndTop - arrowLength * unitTangentY + arrowWidth * unitNormalY);
        var arrowRight = new Point(
            EndLeft - arrowLength * unitTangentX - arrowWidth * unitNormalX,
            EndTop - arrowLength * unitTangentY - arrowWidth * unitNormalY);

        // 创建箭头几何图形
        _arrowFigure.StartPoint = arrowTip;
        _arrowLeftSegment.Point = arrowLeft;
        _arrowRightSegment.Point = arrowRight;
        _arrowPath.Data = _arrowGeometry;
    }

    private void OnHoverPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this).Position;
        bool over = HitTestCurve(pt);
        if (over && !IsHighlighted) { IsHighlighted = true; Focus(FocusState.Pointer); }
        else if (!over && IsHighlighted) IsHighlighted = false;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Windows.System.VirtualKey.Delete && IsHighlighted)
        {
            if (DataContext is IWorkflowLinkViewModel vm)
                vm.DeleteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private bool HitTestCurve(Point pt)
    {
        const double hitRadius = 6.0;
        const int segs = 40;
        double diffx = EndLeft - StartLeft, diffy = EndTop - StartTop;
        var cp1 = new Point(StartLeft + diffx * 0.618, StartTop + diffy * 0.1);
        var cp2 = new Point(EndLeft - diffx * 0.618, EndTop - diffy * 0.1);
        var p0 = new Point(StartLeft, StartTop);
        var p3 = new Point(EndLeft, EndTop);
        Point Eval(double t)
        {
            double mt = 1 - t;
            return new Point(
                mt * mt * mt * p0.X + 3 * mt * mt * t * cp1.X + 3 * mt * t * t * cp2.X + t * t * t * p3.X,
                mt * mt * mt * p0.Y + 3 * mt * mt * t * cp1.Y + 3 * mt * t * t * cp2.Y + t * t * t * p3.Y);
        }
        var prev = Eval(0);
        for (int i = 1; i <= segs; i++)
        {
            var next = Eval((double)i / segs);
            if (DistSeg(pt, prev, next) <= hitRadius) return true;
            prev = next;
        }
        return false;
    }

    private static double DistSeg(Point p, Point a, Point b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y;
        double len2 = abx * abx + aby * aby;
        if (len2 < 0.0001) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        double t = Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2, 0, 1);
        double px = a.X + t * abx - p.X, py = a.Y + t * aby - p.Y;
        return Math.Sqrt(px * px + py * py);
    }
}