using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace Demo;

/// <summary>
/// Orthogonal (polyline) connection with golden-ratio stubs.
/// Passive visual only — no hover, highlight, or keyboard interaction.
/// </summary>
public partial class LinkView : Control
{
    public LinkView()
    {
        InitializeComponent();
        IsHitTestVisible = false;
    }

    #region Avalonia 属性定义

    public static readonly StyledProperty<double> StartLeftProperty =
        AvaloniaProperty.Register<LinkView, double>(nameof(StartLeft));

    public static readonly StyledProperty<double> StartTopProperty =
        AvaloniaProperty.Register<LinkView, double>(nameof(StartTop));

    public static readonly StyledProperty<double> EndLeftProperty =
        AvaloniaProperty.Register<LinkView, double>(nameof(EndLeft));

    public static readonly StyledProperty<double> EndTopProperty =
        AvaloniaProperty.Register<LinkView, double>(nameof(EndTop));

    public static readonly StyledProperty<bool> CanRenderProperty =
        AvaloniaProperty.Register<LinkView, bool>(nameof(CanRender), true);

    public static readonly StyledProperty<bool> IsVirtualProperty =
        AvaloniaProperty.Register<LinkView, bool>(nameof(IsVirtual), false);

    public static readonly StyledProperty<Color> LineColorProperty =
        AvaloniaProperty.Register<LinkView, Color>(nameof(LineColor), Colors.Cyan);

    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<LinkView, double>(nameof(LineThickness), 2.0);

    public double StartLeft
    {
        get => GetValue(StartLeftProperty);
        set => SetValue(StartLeftProperty, value);
    }

    public double StartTop
    {
        get => GetValue(StartTopProperty);
        set => SetValue(StartTopProperty, value);
    }

    public double EndLeft
    {
        get => GetValue(EndLeftProperty);
        set => SetValue(EndLeftProperty, value);
    }

    public double EndTop
    {
        get => GetValue(EndTopProperty);
        set => SetValue(EndTopProperty, value);
    }

    public bool CanRender
    {
        get => GetValue(CanRenderProperty);
        set => SetValue(CanRenderProperty, value);
    }

    public bool IsVirtual
    {
        get => GetValue(IsVirtualProperty);
        set => SetValue(IsVirtualProperty, value);
    }

    public Color LineColor
    {
        get => GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public double LineThickness
    {
        get => GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    static LinkView()
    {
        AffectsRender<LinkView>(
            StartLeftProperty, StartTopProperty, EndLeftProperty, EndTopProperty,
            CanRenderProperty, IsVirtualProperty, LineColorProperty,
            LineThicknessProperty);
    }

    #endregion

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!CanRender) return;

        var points = BuildPoints();
        if (points.Count < 2) return;

        var color = LineColor;
        var thickness = LineThickness;
        var brush = new ImmutableSolidColorBrush(color);

        var pen = IsVirtualLink
            ? new Pen(brush, thickness) { DashStyle = new DashStyle([4.0, 2.0], 0) }
            : new Pen(brush, thickness);

        for (int i = 0; i < points.Count - 1; i++)
            context.DrawLine(pen, points[i], points[i + 1]);

        if (!IsVirtualLink)
            DrawArrowhead(context, points[^2], points[^1], color);
    }

    private bool IsVirtualLink
        => IsVirtual
            || DataContext is IWorkflowLinkViewModel
            {
                Sender.Parent: null,
                Receiver.Parent: null
            };

    private IReadOnlyList<Point> BuildPoints()
    {
        double dx = EndLeft - StartLeft;
        const double phi = 0.6180339887;
        double stub = dx / 2.0 * (1.0 - phi);
        return [
            new Point(StartLeft, StartTop),
            new Point(StartLeft + stub, StartTop),
            new Point(EndLeft - stub, EndTop),
            new Point(EndLeft, EndTop)
        ];
    }

    private void DrawArrowhead(DrawingContext context, Point from, Point tip, Color color)
    {
        var t = new Vector(tip.X - from.X, tip.Y - from.Y);
        double len = t.Length;
        if (len < 0.001) return;
        t = t.Normalize();

        double al = 12, aw = 8;
        var perp = new Vector(-t.Y, t.X);
        var baseP = new Point(tip.X - t.X * al, tip.Y - t.Y * al);
        var w1 = new Point(baseP.X + perp.X * (aw / 2), baseP.Y + perp.Y * (aw / 2));
        var w2 = new Point(baseP.X - perp.X * (aw / 2), baseP.Y - perp.Y * (aw / 2));

        var arrowGeo = new StreamGeometry();
        using (var ctx = arrowGeo.Open())
        {
            ctx.BeginFigure(tip, true);
            ctx.LineTo(w1);
            ctx.LineTo(w2);
        }

        var brush = new ImmutableSolidColorBrush(color);
        context.DrawGeometry(brush, null, arrowGeo);
    }
}