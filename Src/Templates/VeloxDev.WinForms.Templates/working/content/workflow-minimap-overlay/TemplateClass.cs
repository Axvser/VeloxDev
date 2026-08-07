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
/// from <see cref="WorkflowSurfaceBehavior"/>.
/// </summary>
public sealed class TemplateClass : Panel, IWorkflowMinimapOverlay
{
    private readonly Color _background = ParseColor("TemplateMinimapBackground");
    private readonly Color _border = ParseColor("TemplateMinimapBorder");
    private readonly Color _nodeFill = ParseColor("TemplateNodeFill");
    private readonly Color _viewportStroke = ParseColor("TemplateViewportStroke");

    public TemplateClass()
    {
        DoubleBuffered = true;
        Width = 200;
        Height = 140;
        SetStyle(ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
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

        var tree = WorkflowTree;
        if (tree?.Nodes is null) return;

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

        if (!hasNode) return;

        const double pad = 4;
        double contentW = maxX - minX + pad * 2;
        double contentH = maxY - minY + pad * 2;
        double drawW = rect.Width - pad * 2;
        double drawH = rect.Height - pad * 2;
        double scale = Math.Min(drawW / contentW, drawH / contentH);

        using var nodeBrush = new SolidBrush(_nodeFill);
        using var viewportPen = new Pen(_viewportStroke, 1.5f);

        foreach (var node in tree.Nodes)
        {
            double x = (node.Anchor.Horizontal - minX + pad) * scale + pad;
            double y = (node.Anchor.Vertical - minY + pad) * scale + pad;
            double w = Math.Max(2, node.Size.Width * scale);
            double h = Math.Max(2, node.Size.Height * scale);
            g.FillRectangle(nodeBrush, (float)x, (float)y, (float)w, (float)h);
        }

        double vx = (ScrollOffsetX - ContentOffsetX - minX + pad) * scale + pad;
        double vy = (ScrollOffsetY - ContentOffsetY - minY + pad) * scale + pad;
        double vw = Math.Max(4, ViewportWidth * scale);
        double vh = Math.Max(4, ViewportHeight * scale);
        g.DrawRectangle(viewportPen, (float)vx, (float)vy, (float)vw, (float)vh);
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
