using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>Faithful port of the Jalium NodeEditorDemo's Minimap: content-fit over the node
/// bounding box, cyan node rects, translucent viewport with a near-opaque stroke, drag-to-pan
/// through the surface's edge-aware navigation.</summary>
internal sealed class Minimap : Border
{
    private const double MiniW = 200;
    private const double MiniH = 140;
    private const double ContentPad = 8;

    private static readonly SolidColorBrush s_bg = new(Color.FromArgb(0xD2, 0x14, 0x19, 0x22));
    private static readonly SolidColorBrush s_border = new(Color.FromArgb(0xDC, 0x94, 0xA3, 0xB8));
    private static readonly SolidColorBrush s_node = new(Color.FromArgb(0xDC, 0x38, 0xBD, 0xF8));
    private static readonly SolidColorBrush s_viewportFill = new(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush s_viewportStroke = new(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));
    private static readonly Pen s_viewportPen = new(s_viewportStroke, 1);

    private readonly NodeEditorSurface _surface;
    private readonly ScrollViewer _viewer;
    private bool _dragging;

    public Minimap(NodeEditorSurface surface, ScrollViewer viewer)
    {
        _surface = surface;
        _viewer = viewer;
        Width = MiniW;
        Height = MiniH;
        Background = s_bg;
        BorderBrush = s_border;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        ClipToBounds = true;

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMiniMouseDown));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMiniMouseMove));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMiniMouseUp));
    }

    public void Update() => InvalidateVisual();

    private Rect ComputeContent()
    {
        var nodes = _surface.Tree?.Nodes;
        if (nodes is null || nodes.Count == 0)
        {
            return new Rect(0, 0, 1, 1);
        }

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var node in nodes)
        {
            minX = Math.Min(minX, node.Anchor.Horizontal);
            minY = Math.Min(minY, node.Anchor.Vertical);
            maxX = Math.Max(maxX, node.Anchor.Horizontal + node.Size.Width);
            maxY = Math.Max(maxY, node.Anchor.Vertical + node.Size.Height);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private (double Ox, double Oy, double Scale) ComputeTransform(Rect bounds)
    {
        double drawW = Math.Max(1, MiniW - ContentPad * 2);
        double drawH = Math.Max(1, MiniH - ContentPad * 2);
        double scale = Math.Min(drawW / Math.Max(1, bounds.Width), drawH / Math.Max(1, bounds.Height));
        double ox = ContentPad + (drawW - bounds.Width * scale) / 2.0;
        double oy = ContentPad + (drawH - bounds.Height * scale) / 2.0;
        return (ox, oy, scale);
    }

    private void PanToMini(Point mini)
    {
        var bounds = ComputeContent();
        var (ox, oy, scale) = ComputeTransform(bounds);
        if (scale <= 0)
        {
            return;
        }

        double wx = (mini.X - ox) / scale + bounds.X;
        double wy = (mini.Y - oy) / scale + bounds.Y;
        _surface.NavigateToWorld(wx, wy);
    }

    private void OnMiniMouseDown(object? sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _dragging = true;
            CaptureMouse();
            PanToMini(e.GetPosition(this));
            e.Handled = true;
        }
    }

    private void OnMiniMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragging)
        {
            PanToMini(e.GetPosition(this));
            e.Handled = true;
        }
    }

    private void OnMiniMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (_dragging && e.ChangedButton == MouseButton.Left)
        {
            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var bounds = ComputeContent();
        var (ox, oy, scale) = ComputeTransform(bounds);
        if (scale <= 0)
        {
            return;
        }

        var nodes = _surface.Tree?.Nodes;
        if (nodes is not null)
        {
            foreach (var node in nodes)
            {
                var p = new Point(ox + (node.Anchor.Horizontal - bounds.X) * scale, oy + (node.Anchor.Vertical - bounds.Y) * scale);
                dc.DrawRoundedRectangle(s_node, null,
                    new Rect(p.X, p.Y, Math.Max(1, node.Size.Width * scale), Math.Max(1, node.Size.Height * scale)), 2, 2);
            }
        }

        double worldLeft = _viewer.HorizontalOffset - _surface.OriginX;
        double worldTop = _viewer.VerticalOffset - _surface.OriginY;
        var v = new Point(ox + (worldLeft - bounds.X) * scale, oy + (worldTop - bounds.Y) * scale);
        double vw = Math.Max(2, _viewer.ViewportWidth * scale);
        double vh = Math.Max(2, _viewer.ViewportHeight * scale);
        vw = Math.Min(vw, MiniW);
        vh = Math.Min(vh, MiniH);
        v.X = Math.Max(0, Math.Min(MiniW - vw, v.X));
        v.Y = Math.Max(0, Math.Min(MiniH - vh, v.Y));
        dc.DrawRectangle(s_viewportFill, s_viewportPen, new Rect(v.X, v.Y, vw, vh));
    }
}
