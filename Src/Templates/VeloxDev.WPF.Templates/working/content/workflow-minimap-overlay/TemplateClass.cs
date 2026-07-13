using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// A minimap overlay that renders a thumbnail overview of a workflow surface.
/// Implements <see cref="IWorkflowMinimapOverlay"/> for automatic data updates
/// from <see cref="WorkflowSurfaceBehavior"/>.
/// </summary>
public sealed class TemplateClass : FrameworkElement, IWorkflowMinimapOverlay
{
    // ── Dependency Properties ────────────────────────────────────────────────

    private static void OnPropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TemplateClass)d).InvalidateVisual();

    public static readonly DependencyProperty ScrollOffsetXProperty =
        DependencyProperty.Register(nameof(ScrollOffsetX), typeof(double), typeof(TemplateClass),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnPropChanged));
    public static readonly DependencyProperty ScrollOffsetYProperty =
        DependencyProperty.Register(nameof(ScrollOffsetY), typeof(double), typeof(TemplateClass),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnPropChanged));
    public static readonly DependencyProperty ContentOffsetXProperty =
        DependencyProperty.Register(nameof(ContentOffsetX), typeof(double), typeof(TemplateClass),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnPropChanged));
    public static readonly DependencyProperty ContentOffsetYProperty =
        DependencyProperty.Register(nameof(ContentOffsetY), typeof(double), typeof(TemplateClass),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnPropChanged));
    public static readonly DependencyProperty ViewportWidthProperty =
        DependencyProperty.Register(nameof(ViewportWidth), typeof(double), typeof(TemplateClass),
            new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender, OnPropChanged));
    public static readonly DependencyProperty ViewportHeightProperty =
        DependencyProperty.Register(nameof(ViewportHeight), typeof(double), typeof(TemplateClass),
            new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender, OnPropChanged));
    public static readonly DependencyProperty WorkflowTreeProperty =
        DependencyProperty.Register(nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(TemplateClass),
            new FrameworkPropertyMetadata(null, (d, e) => ((TemplateClass)d).OnTreeChanged((IWorkflowTreeViewModel?)e.NewValue)));
    public static readonly DependencyProperty IsMinimapVisibleProperty =
        DependencyProperty.Register(nameof(IsMinimapVisible), typeof(bool), typeof(TemplateClass),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnPropChanged));

    // ── CLR accessors ────────────────────────────────────────────────────────

    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => (double)GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }
    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public bool IsMinimapVisible { get => (bool)GetValue(IsMinimapVisibleProperty); set => SetValue(IsMinimapVisibleProperty, value); }

    // ── Brushes ──────────────────────────────────────────────────────────────

    private static readonly Brush BackgroundBrush = CreateBrush("TemplateMinimapBackground");
    private static readonly Pen BorderPen = CreatePen("TemplateMinimapBorder", 1);
    private static readonly Brush NodeBrush = CreateBrush("TemplateNodeFill");
    private static readonly Pen ViewportPen = CreatePen("TemplateViewportStroke", 1.5);

    private static Brush CreateBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        return new SolidColorBrush(color);
    }

    private static Pen CreatePen(string hex, double thickness)
        => new(CreateBrush(hex), thickness);

    // ── State ────────────────────────────────────────────────────────────────

    private IWorkflowTreeViewModel? _subscribedTree;

    public TemplateClass()
    {
        Width = 200;
        Height = 140;
        ClipToBounds = true;
    }

    private void OnTreeChanged(IWorkflowTreeViewModel? newTree) { InvalidateVisual(); }

    // ── Rendering ────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (!IsMinimapVisible) return;

        var rect = new Rect(RenderSize);
        drawingContext.DrawRectangle(BackgroundBrush, BorderPen, rect);

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
            drawingContext.DrawRectangle(NodeBrush, null, new Rect(x, y, w, h));
        }

        // Draw viewport indicator
        double vx = (ScrollOffsetX - ContentOffsetX - minX + pad) * scale + pad;
        double vy = (ScrollOffsetY - ContentOffsetY - minY + pad) * scale + pad;
        double vw = Math.Max(4, ViewportWidth * scale);
        double vh = Math.Max(4, ViewportHeight * scale);
        drawingContext.DrawRectangle(null, ViewportPen, new Rect(vx, vy, vw, vh));
    }
}
