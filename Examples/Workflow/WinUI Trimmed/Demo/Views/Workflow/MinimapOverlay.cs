using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// A minimap overlay that renders a thumbnail overview of a workflow surface.
/// Implements <see cref="IWorkflowMinimapOverlay"/> for automatic data updates
/// from <see cref="WorkflowSurfaceBehavior"/>.
/// </summary>
public sealed class MinimapOverlay : Canvas, IWorkflowMinimapOverlay
{
    // ── Dependency Properties ────────────────────────────────────────────────

    private static void OnPropChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MinimapOverlay)d).InvalidateArrange();

    public static readonly DependencyProperty ScrollOffsetXProperty =
        DependencyProperty.Register(nameof(ScrollOffsetX), typeof(double), typeof(MinimapOverlay), new PropertyMetadata(0d, OnPropChanged));
    public static readonly DependencyProperty ScrollOffsetYProperty =
        DependencyProperty.Register(nameof(ScrollOffsetY), typeof(double), typeof(MinimapOverlay), new PropertyMetadata(0d, OnPropChanged));
    public static readonly DependencyProperty ContentOffsetXProperty =
        DependencyProperty.Register(nameof(ContentOffsetX), typeof(double), typeof(MinimapOverlay), new PropertyMetadata(0d, OnPropChanged));
    public static readonly DependencyProperty ContentOffsetYProperty =
        DependencyProperty.Register(nameof(ContentOffsetY), typeof(double), typeof(MinimapOverlay), new PropertyMetadata(0d, OnPropChanged));
    public static readonly DependencyProperty ViewportWidthProperty =
        DependencyProperty.Register(nameof(ViewportWidth), typeof(double), typeof(MinimapOverlay), new PropertyMetadata(1d, OnPropChanged));
    public static readonly DependencyProperty ViewportHeightProperty =
        DependencyProperty.Register(nameof(ViewportHeight), typeof(double), typeof(MinimapOverlay), new PropertyMetadata(1d, OnPropChanged));
    public static readonly DependencyProperty WorkflowTreeProperty =
        DependencyProperty.Register(nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(MinimapOverlay), new PropertyMetadata(null, (d, e) => ((MinimapOverlay)d).OnTreeChanged((IWorkflowTreeViewModel?)e.NewValue)));
    public static readonly DependencyProperty IsMinimapVisibleProperty =
        DependencyProperty.Register(nameof(IsMinimapVisible), typeof(bool), typeof(MinimapOverlay), new PropertyMetadata(true, OnPropChanged));

    // ── CLR accessors ────────────────────────────────────────────────────────

    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => (double)GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }
    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public bool IsMinimapVisible { get => (bool)GetValue(IsMinimapVisibleProperty); set => SetValue(IsMinimapVisibleProperty, value); }

    // ── State ────────────────────────────────────────────────────────────────

    private IWorkflowTreeViewModel? _subscribedTree;
    private readonly List<(double X, double Y, double W, double H)> _lastNodeRects = [];
    private bool _pendingRefresh = true;

    // ── Shape pools ──────────────────────────────────────────────────────────

    private Rectangle? _bgRect;
    private Rectangle? _borderRect;
    private Rectangle? _viewportRect;
    private readonly List<Rectangle> _nodeRects = [];

    private static readonly SolidColorBrush BackgroundBrush = CreateBrush("#D21922");
    private static readonly SolidColorBrush BorderBrush = CreateBrush("#DC94A3B8");
    private static readonly SolidColorBrush NodeBrush = CreateBrush("#DC38BDF8");
    private static readonly SolidColorBrush ViewportBrush = CreateBrush("#F0FFFFFF");

    private static SolidColorBrush CreateBrush(string color)
        => new(ParseColor(color));

    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = hex.Length >= 8 ? byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber) : (byte)255;
        byte r = byte.Parse(hex[^6..^4], System.Globalization.NumberStyles.HexNumber);
        byte g = byte.Parse(hex[^4..^2], System.Globalization.NumberStyles.HexNumber);
        byte b = byte.Parse(hex[^2..], System.Globalization.NumberStyles.HexNumber);
        return Windows.UI.Color.FromArgb(a, r, g, b);
    }

    public MinimapOverlay()
    {
        Width = 200;
        Height = 140;
        Loaded += (_, _) => Redraw();
        SizeChanged += (_, _) => Redraw();
    }

    private void OnTreeChanged(IWorkflowTreeViewModel? newTree) { MarkDirty(); }
    private void MarkDirty() { _pendingRefresh = true; Redraw(); }

    private void Redraw()
    {
        if (!IsLoaded || !IsMinimapVisible) return;

        // Clear previous shapes
        Children.Clear();
        _nodeRects.Clear();
        _viewportRect = null;
        _bgRect = null;
        _borderRect = null;

        // Background
        _bgRect = new Rectangle { Fill = BackgroundBrush, Width = Width, Height = Height, RadiusX = 4, RadiusY = 4 };
        Children.Add(_bgRect);

        // Border
        _borderRect = new Rectangle
        {
            Stroke = BorderBrush, StrokeThickness = 1,
            Width = Width, Height = Height, RadiusX = 4, RadiusY = 4
        };
        Children.Add(_borderRect);

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
        double drawW = Width - pad * 2;
        double drawH = Height - pad * 2;
        double scale = Math.Min(drawW / contentW, drawH / contentH);

        // Draw nodes
        foreach (var node in tree.Nodes)
        {
            double x = (node.Anchor.Horizontal - minX + pad) * scale + pad;
            double y = (node.Anchor.Vertical - minY + pad) * scale + pad;
            double w = Math.Max(2, node.Size.Width * scale);
            double h = Math.Max(2, node.Size.Height * scale);
            var rect = new Rectangle { Fill = NodeBrush, Width = w, Height = h, RadiusX = 1, RadiusY = 1 };
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            Children.Add(rect);
            _nodeRects.Add(rect);
        }

        // Viewport indicator
        double vx = (ScrollOffsetX - ContentOffsetX - minX + pad) * scale + pad;
        double vy = (ScrollOffsetY - ContentOffsetY - minY + pad) * scale + pad;
        double vw = Math.Max(4, ViewportWidth * scale);
        double vh = Math.Max(4, ViewportHeight * scale);
        _viewportRect = new Rectangle
        {
            Stroke = ViewportBrush, StrokeThickness = 1.5,
            Width = vw, Height = vh
        };
        Canvas.SetLeft(_viewportRect, vx);
        Canvas.SetTop(_viewportRect, vy);
        Children.Add(_viewportRect);
    }
}
