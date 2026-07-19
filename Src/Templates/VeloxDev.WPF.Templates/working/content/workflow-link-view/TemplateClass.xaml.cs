// VeloxDev customization: Customize line geometry, color, and thickness here.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>
/// Orthogonal (polyline) connection with golden-ratio stubs.
/// Passive visual only — no hover, highlight, or keyboard interaction.
/// </summary>
public partial class TemplateClass : UserControl
{
    public TemplateClass()
    {
        InitializeComponent();
        IsHitTestVisible = false;
        Panel.SetZIndex(this, -100);

        DataContextChanged += (_, _) => InvalidateVisual();
    }

    #region Dependency properties

    public static readonly DependencyProperty StartLeftProperty =
        DependencyProperty.Register(nameof(StartLeft), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty StartTopProperty =
        DependencyProperty.Register(nameof(StartTop), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty EndLeftProperty =
        DependencyProperty.Register(nameof(EndLeft), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty EndTopProperty =
        DependencyProperty.Register(nameof(EndTop), typeof(double), typeof(TemplateClass), new PropertyMetadata(0d, OnRenderChanged));
    public static readonly DependencyProperty CanRenderProperty =
        DependencyProperty.Register(nameof(CanRender), typeof(bool), typeof(TemplateClass), new PropertyMetadata(true, OnRenderChanged));
    public static readonly DependencyProperty IsVirtualProperty =
        DependencyProperty.Register(nameof(IsVirtual), typeof(bool), typeof(TemplateClass), new PropertyMetadata(false, OnRenderChanged));
    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Color), typeof(TemplateClass), new PropertyMetadata((Color)ColorConverter.ConvertFromString("TemplateLinkColor"), OnRenderChanged));

    public double StartLeft { get => (double)GetValue(StartLeftProperty); set => SetValue(StartLeftProperty, value); }
    public double StartTop { get => (double)GetValue(StartTopProperty); set => SetValue(StartTopProperty, value); }
    public double EndLeft { get => (double)GetValue(EndLeftProperty); set => SetValue(EndLeftProperty, value); }
    public double EndTop { get => (double)GetValue(EndTopProperty); set => SetValue(EndTopProperty, value); }
    public bool CanRender { get => (bool)GetValue(CanRenderProperty); set => SetValue(CanRenderProperty, value); }
    public bool IsVirtual { get => (bool)GetValue(IsVirtualProperty); set => SetValue(IsVirtualProperty, value); }
    public Color LineColor { get => (Color)GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }

    private static void OnRenderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TemplateClass)d).InvalidateVisual();

    private bool IsVirtualLink
        => IsVirtual
            || DataContext is IWorkflowLinkViewModel
            {
                Sender.Parent: null,
                Receiver.Parent: null
            };

    #endregion

    #region Render

    protected override void OnRender(DrawingContext ctx)
    {
        base.OnRender(ctx);
        if (!CanRender) return;

        var points = BuildPoints();
        if (points.Count < 2) return;

        var color = LineColor;
        var thickness = TemplateLinkThickness;
        var brush = new SolidColorBrush(color);

        var pen = IsVirtualLink
            ? new Pen(brush, thickness) { DashStyle = new DashStyle(new double[] { 4, 2 }, 0) }
            : new Pen(brush, thickness);

        for (int i = 0; i < points.Count - 1; i++)
            ctx.DrawLine(pen, points[i], points[i + 1]);

        if (!IsVirtualLink)
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
}
