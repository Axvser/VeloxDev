using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// A minimap overlay that renders a thumbnail overview of a workflow surface.
/// Implements <see cref="IWorkflowMinimapOverlay"/> for automatic data updates
/// from <see cref="WorkflowSurfaceBehavior"/>. The viewport outline is draggable:
/// dragging raises <see cref="IWorkflowMinimapScrollSource.ViewportScrollRequested"/>
/// so the host surface can pan to match.
/// </summary>
public sealed class TemplateClass : Panel, IWorkflowMinimapOverlay, IWorkflowMinimapScrollSource
{
    private readonly Color _background = ParseColor("TemplateMinimapBackground");
    private readonly Color _border = ParseColor("TemplateMinimapBorder");
    private readonly Color _nodeFill = ParseColor("TemplateNodeFill");
    private readonly Color _viewportStroke = ParseColor("TemplateViewportStroke");

    // Overlay margin from the surface's top-right corner.
    private const int CornerMargin = 12;

    private bool _dragging;
    private Point _dragOffset;

    public TemplateClass()
    {
        DoubleBuffered = true;
        Width = 200;
        Height = 140;
        Anchor = AnchorStyles.Top | AnchorStyles.Right;
        SetStyle(ControlStyles.ResizeRedraw, true);
        // Opaque background: WinForms has no reliable transparent compositing, so
        // the overlay erases to its own color instead of letting the surface show
        // through a transparent panel. Force alpha to 255 — a translucent BackColor
        // throws in .NET 10 (Control.set_BackColor requires A == 0xFF without
        // SupportsTransparentBackColor).
        BackColor = Color.FromArgb(255, _background);
    }

    /// <summary>
    /// Raised while the user drags the viewport; carries the desired world-space
    /// scroll offsets (<see cref="ScrollOffsetX"/> / <see cref="ScrollOffsetY"/>).
    /// </summary>
    public event Action<double, double>? ViewportScrollRequested;

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        if (Parent is not null)
        {
            Parent.Resize -= OnParentResized;
            Parent.Resize += OnParentResized;
        }

        PositionAtTopRight();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PositionAtTopRight();
    }

    private void OnParentResized(object? sender, EventArgs e) => PositionAtTopRight();

    /// <summary>
    /// Anchors this overlay to the top-right of its host surface. The surface's
    /// size is only known once it is added and laid out, so the position is
    /// recomputed whenever the parent resizes.
    /// </summary>
    private void PositionAtTopRight()
    {
        if (Parent is null) return;

        Location = new Point(
            Math.Max(0, Parent.ClientSize.Width - Width - CornerMargin),
            CornerMargin);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetX { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetY { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetX { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetY { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ViewportWidth { get; set; } = 1;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ViewportHeight { get; set; } = 1;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowTreeViewModel? WorkflowTree { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsMinimapVisible { get; set; } = true;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (!IsMinimapVisible) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0, 0, Width, Height);

        using var bgBrush = new SolidBrush(_background);
        using var borderPen = new Pen(_border, 1f);
        g.FillRectangle(bgBrush, rect);
        g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);

        var layout = ComputeLayout();
        if (layout is null) return;
        var l = layout.Value;

        using var nodeBrush = new SolidBrush(_nodeFill);
        using var viewportPen = new Pen(_viewportStroke, 1.5f);

        foreach (var node in l.Tree.Nodes)
        {
            double x = l.Ox + (node.Anchor.Horizontal - l.MinX) * l.Scale;
            double y = l.Oy + (node.Anchor.Vertical - l.MinY) * l.Scale;
            double w = Math.Max(2, node.Size.Width * l.Scale);
            double h = Math.Max(2, node.Size.Height * l.Scale);
            g.FillRectangle(nodeBrush, (float)x, (float)y, (float)w, (float)h);
        }

        var vp = ViewportRect(l);
        g.DrawRectangle(viewportPen, (float)vp.X, (float)vp.Y, (float)vp.Width, (float)vp.Height);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        var layout = ComputeLayout();
        if (layout is null) return;
        var l = layout.Value;
        var vp = ViewportRect(l);

        // The block is the anchor; the viewport follows it (matches the Razor adapter):
        //  - Pressing ON the block keeps it where it is and aligns the viewport to it.
        //  - Pressing ELSEWHERE moves the block's center to the cursor, retreating to just
        //    inside the minimap if that would push the block over an edge.
        double targetCenterX, targetCenterY;
        if (vp.Contains(e.Location))
        {
            targetCenterX = vp.X + vp.Width / 2;
            targetCenterY = vp.Y + vp.Height / 2;
        }
        else
        {
            var tlX = Math.Max(0, Math.Min(Width - vp.Width, e.X - vp.Width / 2));
            var tlY = Math.Max(0, Math.Min(Height - vp.Height, e.Y - vp.Height / 2));
            targetCenterX = tlX + vp.Width / 2;
            targetCenterY = tlY + vp.Height / 2;
        }

        // Grab the block at a fixed offset from its target center; dragging keeps that offset so
        // the block follows the pointer (offset ~0 when the press re-centered it on the cursor).
        _dragOffset = new Point((int)(e.X - targetCenterX), (int)(e.Y - targetCenterY));
        _dragging = true;
        Capture = true;
        UpdateViewportFromPointer(e.X, e.Y, l);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var layout = ComputeLayout();
        if (layout is null) return;
        UpdateViewportFromPointer(e.X, e.Y, layout.Value);
    }

    private void UpdateViewportFromPointer(int x, int y, MinimapLayout l)
    {
        // The block's center tracks the cursor (minus the fixed grab offset); the viewport centers
        // on whatever world point that center maps to, matching WPF/Avalonia/WinUI/MAUI/Razor.
        // No content clamp: the surface grows instead, so the block can be dragged to the minimap
        // edge and pan the surface into empty space.
        double cx = x - _dragOffset.X;
        double cy = y - _dragOffset.Y;
        double sx = (cx - l.Ox) / l.Scale + l.MinX - ViewportWidth / 2 + ContentOffsetX;
        double sy = (cy - l.Oy) / l.Scale + l.MinY - ViewportHeight / 2 + ContentOffsetY;

        // Reflect the target immediately so the block moves on the very first press/drag event,
        // before the canvas round-trip applies (the canvas sync then confirms/settles it).
        ScrollOffsetX = sx;
        ScrollOffsetY = sy;
        Invalidate();

        ViewportScrollRequested?.Invoke(sx, sy);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;

        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        // Capture was stolen or released outside the minimap (e.g. alt-tab);
        // stop dragging so the next press starts a fresh grab.
        if (!Capture)
        {
            _dragging = false;
        }
    }

    /// <summary>
    /// Canvas bounds + scale shared by painting and drag mapping. Returns null when
    /// there are no nodes yet, so the minimap stays blank until the tree lays out.
    /// </summary>
    private MinimapLayout? ComputeLayout()
    {
        var tree = WorkflowTree;
        if (tree?.Nodes is null) return null;

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        var hasNode = false;

        foreach (var node in tree.Nodes)
        {
            minX = Math.Min(minX, node.Anchor.Horizontal);
            minY = Math.Min(minY, node.Anchor.Vertical);
            maxX = Math.Max(maxX, node.Anchor.Horizontal + node.Size.Width);
            maxY = Math.Max(maxY, node.Anchor.Vertical + node.Size.Height);
            hasNode = true;
        }

        if (!hasNode) return null;

        // The minimap canvas is the node content only (no viewport union), matching
        // the other five GUI frameworks: the content is fitted at a content-fit scale
        // and centered in the drawable area, and the viewport block is clamped at the
        // minimap edge. Panning past the content then extends the surface instead of
        // re-fitting the whole minimap around the viewport.
        const double pad = 4;
        double contentW = Math.Max(1, maxX - minX);
        double contentH = Math.Max(1, maxY - minY);
        double drawW = Width - pad * 2;
        double drawH = Height - pad * 2;
        double scale = Math.Min(drawW / contentW, drawH / contentH);
        // Centered content-fit: ox/oy is the top-left of the scaled content, centered
        // in the drawable area — the same transform the WPF/Avalonia/WinUI/MAUI
        // ComputeTransform and the Razor Recompute mapping produce, so a drag/click
        // inverts the same mapping the paint uses.
        double ox = pad + (drawW - contentW * scale) / 2;
        double oy = pad + (drawH - contentH * scale) / 2;
        return new MinimapLayout(tree, minX, minY, scale, ox, oy);
    }

    private RectangleF ViewportRect(MinimapLayout l)
    {
        // Map the viewport through the same centered content-fit transform the nodes
        // use, then clamp the block inside the minimap so it never leaves the bounds —
        // matching WPF/Avalonia/WinUI/MAUI/Razor. When the user drags it to an edge,
        // the requested scroll grows and the surface pans into the empty space.
        double vx = l.Ox + (ScrollOffsetX - ContentOffsetX - l.MinX) * l.Scale;
        double vy = l.Oy + (ScrollOffsetY - ContentOffsetY - l.MinY) * l.Scale;
        double vw = Math.Max(4, ViewportWidth * l.Scale);
        double vh = Math.Max(4, ViewportHeight * l.Scale);
        vx = Math.Max(0, Math.Min(Width - vw, vx));
        vy = Math.Max(0, Math.Min(Height - vh, vy));
        return new RectangleF((float)vx, (float)vy, (float)vw, (float)vh);
    }

    private readonly struct MinimapLayout
    {
        public readonly IWorkflowTreeViewModel Tree;
        public readonly double MinX, MinY, Scale, Ox, Oy;

        public MinimapLayout(
            IWorkflowTreeViewModel tree,
            double minX, double minY, double scale, double ox, double oy)
        {
            Tree = tree;
            MinX = minX;
            MinY = minY;
            Scale = scale;
            Ox = ox;
            Oy = oy;
        }
    }

    private static Color ParseColor(string hex)
    {
        var value = hex.Trim();
        if (value.StartsWith("#", StringComparison.Ordinal))
        {
            var digits = value.Substring(1);
            if (digits.Length == 8)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }

            if (digits.Length == 6)
            {
                return Color.FromArgb(
                    byte.Parse(digits.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    byte.Parse(digits.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            }
        }

        return Color.FromName(value);
    }
}

/// <summary>
/// Implemented by minimap overlays whose viewport can be dragged to request surface
/// scrolling. Kept off <see cref="IWorkflowMinimapOverlay"/> so the shared adapter
/// interface stays framework-agnostic.
/// </summary>
public interface IWorkflowMinimapScrollSource
{
    event Action<double, double>? ViewportScrollRequested;
}
