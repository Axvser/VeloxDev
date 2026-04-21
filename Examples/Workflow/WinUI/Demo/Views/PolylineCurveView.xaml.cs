using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;
using Windows.Foundation;

namespace Demo.Views;

/// <summary>
/// Orthogonal (polyline) connection with golden-ratio stubs.
/// Hover to highlight, Delete to remove.
/// </summary>
public sealed partial class PolylineCurveView : UserControl
{
    private readonly Path _path;
    private readonly Path _arrowPath;

    public PolylineCurveView()
    {
        InitializeComponent();
        IsHitTestVisible = true;
        Canvas.SetZIndex(this, -100);

        var container = new Grid();
        _path = new Path { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        _arrowPath = new Path { IsHitTestVisible = false };
        container.Children.Add(_path);
        container.Children.Add(_arrowPath);
        this.Content = container;

        PointerEntered += (_, _) => { IsHighlighted = true; Focus(FocusState.Pointer); };
        PointerExited += (_, _) => IsHighlighted = false;
        PointerMoved += OnHoverPointerMoved;
    }

    #region Dependency properties

    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnChanged));
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(true, OnChanged));
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnChanged));
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Windows.UI.Color), typeof(PolylineCurveView), new PropertyMetadata(Colors.Cyan, OnChanged));
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Windows.UI.Color LineColor { get => (Windows.UI.Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PolylineCurveView)d).UpdatePath();

    #endregion

    private void UpdatePath()
    {
        if (!CanRender)
        {
            _path.Data = null;
            _arrowPath.Data = null;
            return;
        }

        var pts = BuildPoints();
        var color = IsHighlighted ? Microsoft.UI.Colors.OrangeRed : LineColor;
        var thickness = IsHighlighted ? 3.5 : 2.0;
        var brush = new SolidColorBrush(color);
        _path.Stroke = brush;
        _path.StrokeThickness = thickness;

        if (IsVirtual)
            _path.StrokeDashArray = new DoubleCollection { 4, 2 };
        else
            _path.StrokeDashArray = null;

        var geo = new PathGeometry();
        var fig = new PathFigure { StartPoint = pts[0], IsClosed = false };
        for (int i = 1; i < pts.Count; i++)
            fig.Segments.Add(new LineSegment { Point = pts[i] });
        geo.Figures.Add(fig);
        _path.Data = geo;

        // Arrowhead
        if (!IsVirtual && pts.Count >= 2)
        {
            var from = pts[^2];
            var tip = pts[^1];
            double tx = tip.X - from.X, ty = tip.Y - from.Y;
            double len = Math.Sqrt(tx * tx + ty * ty);
            if (len > 0.001) { tx /= len; ty /= len; }
            double nx = -ty, ny = tx;
            double al = 12, aw = 8;
            var baseP = new Point(tip.X - tx * al, tip.Y - ty * al);
            var arrowGeo = new PathGeometry();
            var arrowFig = new PathFigure { StartPoint = tip, IsClosed = true };
            arrowFig.Segments.Add(new LineSegment { Point = new Point(baseP.X + nx * (aw / 2), baseP.Y + ny * (aw / 2)) });
            arrowFig.Segments.Add(new LineSegment { Point = new Point(baseP.X - nx * (aw / 2), baseP.Y - ny * (aw / 2)) });
            arrowGeo.Figures.Add(arrowFig);
            _arrowPath.Data = arrowGeo;
            _arrowPath.Fill = brush;
        }
        else
        {
            _arrowPath.Data = null;
        }
    }

    private List<Point> BuildPoints()
    {
        double dx = EndLeft - StartLeft;
        const double phi = 0.6180339887;
        double stub = dx / 2.0 * (1.0 - phi);
        return
        [
            new Point(StartLeft, StartTop),
            new Point(StartLeft + stub, StartTop),
            new Point(EndLeft - stub, EndTop),
            new Point(EndLeft, EndTop)
        ];
    }

    #region Interaction

    private void OnHoverPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(this).Position;
        bool over = HitTestLine(pt);
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

    private bool HitTestLine(Point pt)
    {
        const double hitRadius = 6.0;
        var pts = BuildPoints();
        for (int i = 0; i < pts.Count - 1; i++)
            if (DistSeg(pt, pts[i], pts[i + 1]) <= hitRadius) return true;
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

    #endregion
}
