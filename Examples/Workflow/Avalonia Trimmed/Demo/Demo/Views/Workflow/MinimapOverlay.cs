using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo;

/// <summary>
/// A minimap overlay that renders a thumbnail overview of a workflow surface.
/// Implements <see cref="IWorkflowMinimapOverlay"/> for automatic data updates
/// from <see cref="WorkflowSurfaceBehavior"/>.
/// </summary>
public sealed class MinimapOverlay : Control, IWorkflowMinimapOverlay
{
    // ── Styled Properties ────────────────────────────────────────────────────

    public static readonly StyledProperty<double> ScrollOffsetXProperty =
        AvaloniaProperty.Register<MinimapOverlay, double>(nameof(ScrollOffsetX));
    public static readonly StyledProperty<double> ScrollOffsetYProperty =
        AvaloniaProperty.Register<MinimapOverlay, double>(nameof(ScrollOffsetY));
    public static readonly StyledProperty<double> ContentOffsetXProperty =
        AvaloniaProperty.Register<MinimapOverlay, double>(nameof(ContentOffsetX));
    public static readonly StyledProperty<double> ContentOffsetYProperty =
        AvaloniaProperty.Register<MinimapOverlay, double>(nameof(ContentOffsetY));
    public static readonly StyledProperty<double> ViewportWidthProperty =
        AvaloniaProperty.Register<MinimapOverlay, double>(nameof(ViewportWidth), 1d);
    public static readonly StyledProperty<double> ViewportHeightProperty =
        AvaloniaProperty.Register<MinimapOverlay, double>(nameof(ViewportHeight), 1d);
    public static readonly StyledProperty<IWorkflowTreeViewModel?> WorkflowTreeProperty =
        AvaloniaProperty.Register<MinimapOverlay, IWorkflowTreeViewModel?>(nameof(WorkflowTree));
    public static readonly StyledProperty<bool> IsMinimapVisibleProperty =
        AvaloniaProperty.Register<MinimapOverlay, bool>(nameof(IsMinimapVisible), true);

    // ── CLR accessors ────────────────────────────────────────────────────────

    public double ScrollOffsetX { get => GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public double ViewportWidth { get => GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }
    public IWorkflowTreeViewModel? WorkflowTree { get => GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public bool IsMinimapVisible { get => GetValue(IsMinimapVisibleProperty); set => SetValue(IsMinimapVisibleProperty, value); }

    static MinimapOverlay()
    {
        AffectsRender<MinimapOverlay>(
            ScrollOffsetXProperty, ScrollOffsetYProperty,
            ContentOffsetXProperty, ContentOffsetYProperty,
            ViewportWidthProperty, ViewportHeightProperty,
            WorkflowTreeProperty, IsMinimapVisibleProperty);
    }

    public MinimapOverlay()
    {
        Width = 200;
        Height = 140;
        ClipToBounds = true;
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    private static readonly IBrush BackgroundBrush =
        new ImmutableSolidColorBrush(Color.Parse("#D21922"));
    private static readonly Pen BorderPen = CreatePen("#DC94A3B8", 1);
    private static readonly IBrush NodeBrush =
        new ImmutableSolidColorBrush(Color.Parse("#DC38BDF8"));
    private static readonly Pen ViewportPen = CreatePen("#F0FFFFFF", 1.5);

    private static Pen CreatePen(string color, double thickness)
        => new(new ImmutableSolidColorBrush(Color.Parse(color)), thickness);

    public override void Render(DrawingContext context)
    {
        if (!IsMinimapVisible) return;

        var rect = new Rect(Bounds.Size);
        context.DrawRectangle(BackgroundBrush, BorderPen, rect);

        var tree = WorkflowTree;
        if (tree?.Nodes is null) return;

        // Compute content bounds
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool hasNode = false;

        foreach (var node in tree.Nodes)
        {
            double nx = node.Anchor.Horizontal;
            double ny = node.Anchor.Vertical;
            double nw = node.Size.Width;
            double nh = node.Size.Height;
            minX = Math.Min(minX, nx);
            minY = Math.Min(minY, ny);
            maxX = Math.Max(maxX, nx + nw);
            maxY = Math.Max(maxY, ny + nh);
            hasNode = true;
        }

        if (!hasNode) return;

        double pad = 4;
        double contentW = maxX - minX + pad * 2;
        double contentH = maxY - minY + pad * 2;
        double drawW = rect.Width - pad * 2;
        double drawH = rect.Height - pad * 2;
        double scale = Math.Min(drawW / contentW, drawH / contentH);

        // Draw nodes
        foreach (var node in tree.Nodes)
        {
            double x = (node.Anchor.Horizontal - minX + pad) * scale + pad;
            double y = (node.Anchor.Vertical - minY + pad) * scale + pad;
            double w = Math.Max(2, node.Size.Width * scale);
            double h = Math.Max(2, node.Size.Height * scale);
            context.DrawRectangle(NodeBrush, null, new Rect(x, y, w, h));
        }

        // Draw viewport indicator
        double vx = (ScrollOffsetX - ContentOffsetX - minX + pad) * scale + pad;
        double vy = (ScrollOffsetY - ContentOffsetY - minY + pad) * scale + pad;
        double vw = Math.Max(4, ViewportWidth * scale);
        double vh = Math.Max(4, ViewportHeight * scale);
        context.DrawRectangle(null, ViewportPen, new Rect(vx, vy, vw, vh));
    }
}
