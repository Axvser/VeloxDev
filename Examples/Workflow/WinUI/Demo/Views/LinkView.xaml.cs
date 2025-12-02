using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using Windows.Foundation;

namespace Demo.Views;

public sealed partial class LinkView : UserControl
{
    public LinkView()
    {
        InitializeComponent();
        IsHitTestVisible = false;
        Canvas.SetZIndex(this, -100);

        // 创建主容器
        var container = new Grid();

        // 创建路径用于绘制贝塞尔曲线
        _path = new Path
        {
            Stroke = new SolidColorBrush(Colors.Cyan),
            StrokeThickness = 2,
            IsHitTestVisible = false
        };

        // 创建箭头路径
        _arrowPath = new Path
        {
            Fill = new SolidColorBrush(Colors.Cyan),
            IsHitTestVisible = false
        };

        container.Children.Add(_path);
        container.Children.Add(_arrowPath);
        this.Content = container;
    }

    private readonly Path _path;
    private readonly Path _arrowPath;

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

    public bool IsVirtual
    {
        get => (bool)GetValue(IsVirtualProperty);
        set => SetValue(IsVirtualProperty, value);
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

    private static void OnIsVirtualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (LinkView)d;
        control.UpdateLinkPath();
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

        // 设置线条样式
        _path.Stroke = new SolidColorBrush(Colors.Cyan);
        _path.StrokeThickness = 2;

        if (IsVirtual)
        {
            _path.StrokeDashArray = new DoubleCollection { 4, 2 }; // 虚线样式
        }
        else
        {
            _path.StrokeDashArray = null; // 实线
        }

        _path.Data = pathGeometry;

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
        var arrowGeometry = new PathGeometry();
        var arrowFigure = new PathFigure
        {
            StartPoint = arrowTip,
            IsClosed = true
        };

        arrowFigure.Segments.Add(new LineSegment { Point = arrowLeft });
        arrowFigure.Segments.Add(new LineSegment { Point = arrowRight });

        arrowGeometry.Figures.Add(arrowFigure);
        _arrowPath.Data = arrowGeometry;
        _arrowPath.Fill = new SolidColorBrush(Colors.Violet);
    }
}