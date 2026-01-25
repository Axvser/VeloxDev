using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System.Collections.Generic;

namespace Demo;

public partial class BezierCurveView : Control
{
    public BezierCurveView()
    {
        InitializeComponent();
        IsHitTestVisible = false;
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
            LineThicknessProperty, DashArrayProperty);
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
        var brush = new ImmutableSolidColorBrush(LineColor);
        var pen = new Pen(brush, LineThickness);

        // 设置虚线样式
        if (IsVirtual || (DashArray != null && DashArray.Count > 0))
        {
            var dashArray = IsVirtual ? [4.0, 2.0] : DashArray;
            pen = new Pen(brush, LineThickness)
            {
                DashStyle = new DashStyle(dashArray, 0)
            };
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
}