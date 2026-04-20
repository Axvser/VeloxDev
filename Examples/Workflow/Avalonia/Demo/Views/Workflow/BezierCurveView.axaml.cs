using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace Demo;

public partial class BezierCurveView : Control
{
    public BezierCurveView()
    {
        InitializeComponent();
        IsHitTestVisible = true;
        Focusable = true;

        CurveSelectionManager.SelectionChanged += owner =>
        {
            if (owner != this && IsSelected)
                IsSelected = false;
        };
    }

    #region Avalonia 属性定义

    public static readonly StyledProperty<double> StartLeftProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(StartLeft));

    public static readonly StyledProperty<double> StartTopProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(StartTop));

    public static readonly StyledProperty<double> EndLeftProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(EndLeft));

    public static readonly StyledProperty<double> EndTopProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(EndTop));

    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<BezierCurveView, bool>(nameof(IsSelected), false);

    public static readonly StyledProperty<bool> CanRenderProperty =
        AvaloniaProperty.Register<BezierCurveView, bool>(nameof(CanRender), true);

    public static readonly StyledProperty<bool> IsVirtualProperty =
        AvaloniaProperty.Register<BezierCurveView, bool>(nameof(IsVirtual), false);

    public static readonly StyledProperty<Color> LineColorProperty =
        AvaloniaProperty.Register<BezierCurveView, Color>(nameof(LineColor), Colors.Cyan);

    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<BezierCurveView, double>(nameof(LineThickness), 2.0);

    public static readonly StyledProperty<IList<double>> DashArrayProperty =
        AvaloniaProperty.Register<BezierCurveView, IList<double>>(nameof(DashArray), [0d]);

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

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
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

    public IList<double> DashArray
    {
        get => GetValue(DashArrayProperty);
        set => SetValue(DashArrayProperty, value);
    }

    static BezierCurveView()
    {
        AffectsRender<BezierCurveView>(
            StartLeftProperty, StartTopProperty, EndLeftProperty, EndTopProperty,
            CanRenderProperty, IsVirtualProperty, LineColorProperty,
            LineThicknessProperty, DashArrayProperty, IsSelectedProperty);
    }

    #endregion

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!CanRender) return;

        var geometry = CreateBezierGeometry();
        if (geometry == null) return;

        DrawBezierLine(context, geometry);

        // 如果不是虚线，绘制箭头
        if (!IsVirtual && (DashArray == null || DashArray.Count == 0))
        {
            DrawArrowhead(context);
        }
    }

    private void DrawBezierLine(DrawingContext context, StreamGeometry geometry)
    {
        var color = IsSelected ? Colors.OrangeRed : LineColor;
        var thickness = IsSelected ? LineThickness + 1.5 : LineThickness;
        var brush = new ImmutableSolidColorBrush(color);

        Pen pen;
        if (IsVirtual || (DashArray != null && DashArray.Count > 0))
        {
            var dashArray = IsVirtual ? [4.0, 2.0] : DashArray;
            pen = new Pen(brush, thickness) { DashStyle = new DashStyle(dashArray, 0) };
        }
        else
        {
            pen = new Pen(brush, thickness);
        }

        if (IsSelected)
        {
            var glowPen = new Pen(new ImmutableSolidColorBrush(color, 0.25), thickness + 6);
            context.DrawGeometry(null, glowPen, geometry);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private StreamGeometry? CreateBezierGeometry()
    {
        var diffx = EndLeft - StartLeft;

        // 计算控制点（三阶贝塞尔曲线需要两个控制点）
        var cp1 = new Point(StartLeft + diffx * 0.3, StartTop);
        var cp2 = new Point(EndLeft - diffx * 0.3, EndTop);

        var startPoint = new Point(StartLeft, StartTop);
        var endPoint = new Point(EndLeft, EndTop);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(startPoint, false);
            ctx.CubicBezierTo(cp1, cp2, endPoint);
        }
        return geometry;
    }

    private void DrawArrowhead(DrawingContext context)
    {
        var diffx = EndLeft - StartLeft;

        // 计算箭头方向（使用曲线末端的方向）
        var cp2 = new Point(EndLeft - diffx * 0.3, EndTop);
        var arrowTip = new Point(EndLeft, EndTop);

        var tangent = new Vector(arrowTip.X - cp2.X, arrowTip.Y - cp2.Y);
        tangent = tangent.Normalize();

        double arrowLength = 12;
        double arrowWidth = 8;

        var perp = new Vector(-tangent.Y, tangent.X);
        var basePt = new Point(
            arrowTip.X - tangent.X * arrowLength,
            arrowTip.Y - tangent.Y * arrowLength);
        var wing1 = new Point(
            basePt.X + perp.X * (arrowWidth / 2),
            basePt.Y + perp.Y * (arrowWidth / 2));
        var wing2 = new Point(
            basePt.X - perp.X * (arrowWidth / 2),
            basePt.Y - perp.Y * (arrowWidth / 2));

        var arrowGeo = new StreamGeometry();
        using (var ctx = arrowGeo.Open())
        {
            ctx.BeginFigure(arrowTip, true);
            ctx.LineTo(wing1);
            ctx.LineTo(wing2);
        }

        var brush = new ImmutableSolidColorBrush(LineColor);
        var pen = new Pen(brush, LineThickness);

        context.DrawGeometry(brush, null, arrowGeo);
        context.DrawGeometry(null, pen, arrowGeo);
    }

    #region Interaction

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        IsSelected = true;
        CurveSelectionManager.Select(this);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        CurveSelectionManager.Deselect(this);
        IsSelected = false;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pt = e.GetPosition(this);
        bool over = HitTestCurve(pt);
        if (over && !IsSelected)
        {
            IsSelected = true;
            CurveSelectionManager.Select(this);
            Focus();
        }
        else if (!over && IsSelected)
        {
            CurveSelectionManager.Deselect(this);
            IsSelected = false;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Delete && IsSelected)
        {
            if (DataContext is IWorkflowLinkViewModel vm)
                vm.DeleteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private bool HitTestCurve(Point pt)
    {
        const double hitRadius = 6.0;
        const int segments = 40;

        var diffx = EndLeft - StartLeft;
        var cp1 = new Point(StartLeft + diffx * 0.3, StartTop);
        var cp2 = new Point(EndLeft - diffx * 0.3, EndTop);
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
        for (int i = 1; i <= segments; i++)
        {
            var next = Eval((double)i / segments);
            if (DistanceToSegment(pt, prev, next) <= hitRadius) return true;
            prev = next;
        }
        return false;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = ab.X * ab.X + ab.Y * ab.Y;
        if (len2 < 0.0001) return new Vector(p.X - a.X, p.Y - a.Y).Length;
        double t = ((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / len2;
        t = Math.Clamp(t, 0.0, 1.0);
        var proj = new Point(a.X + t * ab.X, a.Y + t * ab.Y);
        return new Vector(p.X - proj.X, p.Y - proj.Y).Length;
    }

    #endregion
}