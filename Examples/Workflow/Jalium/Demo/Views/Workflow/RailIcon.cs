using System;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Demo.Views.Workflow;

internal enum RailIconKind
{
    Explorer,
    Run,
    Agent,
    Mcp,
    Output,
    Save,
    Open,
    Undo,
    Redo,
    Reload,
}

// 活动栏与侧栏的图标。Jalium 的文本管线在这台机器上把 Segoe MDL2 Assets / Segoe Fluent Icons /
// Segoe UI Symbol 的私用区码点都画成缺字方框（三家字体实测一致），所以这一家的 rail 不用图标字体，
// 改为在 18×18 的设计坐标里用路径画；换字号不会改变线宽比例，因为整张图按控件边长缩放。
internal sealed class RailIcon : Canvas
{
    private Brush _foreground = new SolidColorBrush(Colors.White);

    public RailIcon()
    {
        Background = new SolidColorBrush(Colors.Transparent);
        IsHitTestVisible = false;
    }

    public RailIconKind Kind { get; init; }

    /// <summary>路径按这个边长设计；控件实际边长与它不同时整体缩放。</summary>
    public double DesignSize { get; init; } = 18;

    public Brush Foreground
    {
        get => _foreground;
        set
        {
            if (ReferenceEquals(_foreground, value)) return;
            _foreground = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        var scale = Math.Min(width, height) / DesignSize;
        if (scale <= 0) return;
        var offsetX = (width - DesignSize * scale) / 2;
        var offsetY = (height - DesignSize * scale) / 2;

        var pen = new Pen(_foreground, 1.5 * scale);

        Point P(double x, double y) => new(offsetX + x * scale, offsetY + y * scale);

        void Line(double x1, double y1, double x2, double y2) => dc.DrawLine(pen, P(x1, y1), P(x2, y2));

        void Polyline(bool closed, bool filled, params (double X, double Y)[] points)
        {
            if (points.Length < 2) return;

            var figure = new PathFigure
            {
                StartPoint = P(points[0].X, points[0].Y),
                IsClosed = closed,
                IsFilled = filled,
            };
            var segment = new PolyLineSegment();
            for (var i = 1; i < points.Length; i++)
                segment.Points.Add(P(points[i].X, points[i].Y));
            figure.Segments.Add(segment);

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            dc.DrawGeometry(filled ? _foreground : null, filled ? null : pen, geometry);
        }

        void Rect(double x, double y, double w, double h)
            => dc.DrawRectangle(null, pen, new Rect(offsetX + x * scale, offsetY + y * scale, w * scale, h * scale));

        void Ellipse(double cx, double cy, double rx, double ry)
            => dc.DrawEllipse(null, pen, P(cx, cy), rx * scale, ry * scale);

        switch (Kind)
        {
            case RailIconKind.Explorer:
                // 两页叠在一起：后一页只露右上角，读起来是「资源管理器」而不是单个文档。
                Polyline(true, false, (8, 2), (14, 2), (14, 11), (8, 11));
                Polyline(true, false, (4, 5), (10, 5), (10, 16), (4, 16));
                break;

            case RailIconKind.Run:
                Polyline(true, true, (4, 3), (15, 9), (4, 15));
                break;

            case RailIconKind.Agent:
                Polyline(true, false,
                    (2, 4), (16, 4), (16, 13), (9, 13), (5.5, 16), (5.5, 13), (2, 13));
                break;

            case RailIconKind.Mcp:
                Ellipse(9, 9, 7, 7);
                Ellipse(9, 9, 3.2, 7);
                Line(2, 9, 16, 9);
                break;

            case RailIconKind.Output:
                Rect(2, 3, 14, 12);
                Polyline(false, false, (5, 7), (8, 9.5), (5, 12));
                Line(10, 12, 13, 12);
                break;

            case RailIconKind.Save:
                // 软盘：外壳 + 上方滑盖 + 下方标签。
                Rect(3, 2, 12, 14);
                Rect(6, 2, 5, 3);
                Rect(6, 10, 6, 6);
                break;

            case RailIconKind.Open:
                Polyline(true, false,
                    (2, 5), (7, 5), (8.5, 7), (16, 7), (16, 14), (2, 14));
                break;

            case RailIconKind.Undo:
                Line(15, 9, 5, 9);
                Polyline(false, false, (8, 6), (5, 9), (8, 12));
                break;

            case RailIconKind.Redo:
                Line(3, 9, 13, 9);
                Polyline(false, false, (10, 6), (13, 9), (10, 12));
                break;

            case RailIconKind.Reload:
                Ellipse(9, 9, 6, 6);
                Polyline(false, false, (11.5, 3.2), (14.2, 5.6), (11.2, 7.2));
                break;
        }
    }
}
