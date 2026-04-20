using Demo.ViewModels;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// Orthogonal (polyline) connection with golden-ratio stubs.
/// Hover to highlight, Delete to remove.
/// </summary>
public partial class PolylineCurveView : UserControl
{
    public PolylineCurveView()
    {
        InitializeComponent();
        IsHitTestVisible = true;
        Focusable = true;
        Panel.SetZIndex(this, -100);

        MouseEnter += (_, _) => { IsHighlighted = true; Focus(); };
        MouseLeave += (_, _) => IsHighlighted = false;
        MouseMove += OnHoverMouseMove;
    }

    #region Dependency properties

    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(PolylineCurveView), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(true, OnRenderChanged));
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnRenderChanged));
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Color), typeof(PolylineCurveView), new PropertyMetadata(Colors.Cyan, OnRenderChanged));
    public static readonly DependencyProperty IsHighlightedProperty =
        DependencyProperty.Register(nameof(IsHighlighted), typeof(bool), typeof(PolylineCurveView), new PropertyMetadata(false, OnRenderChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public bool IsHighlighted { get => (bool)GetValue(IsHighlightedProperty); set => SetValue(IsHighlightedProperty, value); }

    private static void OnRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PolylineCurveView)d).InvalidateVisual();

    #endregion

    #region Render

    protected override void OnRender(DrawingContext ctx)
    {
        base.OnRender(ctx);
        if (!CanRender) return;

        var points = BuildPoints();
        if (points.Count < 2) return;

        var color = IsHighlighted ? Colors.OrangeRed : LineColor;
        var thickness = IsHighlighted ? 3.5 : 2.0;
        var brush = new SolidColorBrush(color);

        var pen = IsVirtual
            ? new Pen(brush, thickness) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) }
            : new Pen(brush, thickness);

        if (IsHighlighted)
        {
            var glowPen = new Pen(new SolidColorBrush(Color.FromArgb(60, color.R, color.G, color.B)), thickness + 6);
            for (int i = 0; i < points.Count - 1; i++)
                ctx.DrawLine(glowPen, points[i], points[i + 1]);
        }

        for (int i = 0; i < points.Count - 1; i++)
            ctx.DrawLine(pen, points[i], points[i + 1]);

        if (!IsVirtual)
            DrawArrowhead(ctx, points[^2], points[^1], brush, thickness);
    }

    private List<Point> BuildPoints()
    {
        var s = new Point(StartLeft, StartTop);
        var e = new Point(EndLeft, EndTop);
        double dx = EndLeft - StartLeft;
        const double phi = 0.6180339887;
        double stub = dx / 2.0 * (1.0 - phi);
        var p1 = new Point(s.X + stub, s.Y);
        var p4 = new Point(e.X - stub, e.Y);
        return [s, p1, p4, e];
    }

    private static void DrawArrowhead(DrawingContext ctx, Point from, Point tip, Brush brush, double thickness)
    {
        var t = new Vector(tip.X - from.X, tip.Y - from.Y);
        if (t.LengthSquared < 0.001) return;
        t.Normalize();
        double al = 12, aw = 8;
        var perp = new Vector(-t.Y, t.X);
        var baseP = new Point(tip.X - t.X * al, tip.Y - t.Y * al);
        var w1 = new Point(baseP.X + perp.X * (aw / 2), baseP.Y + perp.Y * (aw / 2));
        var w2 = new Point(baseP.X - perp.X * (aw / 2), baseP.Y - perp.Y * (aw / 2));
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(tip, true, true);
            c.LineTo(w1, true, false);
            c.LineTo(w2, true, false);
        }
        geo.Freeze();
        ctx.DrawGeometry(brush, null, geo);
    }

    #endregion

    #region Interaction

    private void OnHoverMouseMove(object sender, MouseEventArgs e)
    {
        var pt = e.GetPosition(this);
        bool over = HitTestLine(pt);
        if (over && !IsHighlighted) { IsHighlighted = true; Focus(); }
        else if (!over && IsHighlighted) IsHighlighted = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Delete && IsHighlighted)
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
        if (len2 < 0.0001) return (p - a).Length;
        double t = Math.Clamp(((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2, 0, 1);
        return (p - new Point(a.X + t * abx, a.Y + t * aby)).Length;
    }

    #endregion
}
