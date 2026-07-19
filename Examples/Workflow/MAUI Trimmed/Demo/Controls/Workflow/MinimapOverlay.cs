using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

/// <summary>
/// A minimap overlay that renders a thumbnail overview of a workflow surface.
/// Implements <see cref="IWorkflowMinimapOverlay"/> for automatic data updates
/// from <see cref="WorkflowSurfaceBehavior"/>.
/// </summary>
public sealed class MinimapOverlay : GraphicsView, IDrawable, IWorkflowMinimapOverlay
{
    // ── Bindable Properties ──────────────────────────────────────────────────

    private static void OnVisualProp(BindableObject b, object o, object n) => ((MinimapOverlay)b).Invalidate();

    public static readonly BindableProperty ScrollOffsetXProperty = BindableProperty.Create(nameof(ScrollOffsetX), typeof(double), typeof(MinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ScrollOffsetYProperty = BindableProperty.Create(nameof(ScrollOffsetY), typeof(double), typeof(MinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ContentOffsetXProperty = BindableProperty.Create(nameof(ContentOffsetX), typeof(double), typeof(MinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ContentOffsetYProperty = BindableProperty.Create(nameof(ContentOffsetY), typeof(double), typeof(MinimapOverlay), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportWidthProperty = BindableProperty.Create(nameof(ViewportWidth), typeof(double), typeof(MinimapOverlay), 1d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportHeightProperty = BindableProperty.Create(nameof(ViewportHeight), typeof(double), typeof(MinimapOverlay), 1d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty WorkflowTreeProperty = BindableProperty.Create(nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(MinimapOverlay), null,
        propertyChanged: (b, o, n) => ((MinimapOverlay)b).OnTreeChanged((IWorkflowTreeViewModel?)n));
    public static readonly BindableProperty IsMinimapVisibleProperty = BindableProperty.Create(nameof(IsMinimapVisible), typeof(bool), typeof(MinimapOverlay), true, propertyChanged: OnVisualProp);

    // ── CLR accessors ────────────────────────────────────────────────────────

    public double ScrollOffsetX { get => (double)GetValue(ScrollOffsetXProperty); set => SetValue(ScrollOffsetXProperty, value); }
    public double ScrollOffsetY { get => (double)GetValue(ScrollOffsetYProperty); set => SetValue(ScrollOffsetYProperty, value); }
    public double ContentOffsetX { get => (double)GetValue(ContentOffsetXProperty); set => SetValue(ContentOffsetXProperty, value); }
    public double ContentOffsetY { get => (double)GetValue(ContentOffsetYProperty); set => SetValue(ContentOffsetYProperty, value); }
    public double ViewportWidth { get => (double)GetValue(ViewportWidthProperty); set => SetValue(ViewportWidthProperty, value); }
    public double ViewportHeight { get => (double)GetValue(ViewportHeightProperty); set => SetValue(ViewportHeightProperty, value); }
    public IWorkflowTreeViewModel? WorkflowTree { get => (IWorkflowTreeViewModel?)GetValue(WorkflowTreeProperty); set => SetValue(WorkflowTreeProperty, value); }
    public bool IsMinimapVisible { get => (bool)GetValue(IsMinimapVisibleProperty); set => SetValue(IsMinimapVisibleProperty, value); }

    // ── Constructor ──────────────────────────────────────────────────────────

    public MinimapOverlay()
    {
        Drawable = this;
        HeightRequest = 140;
        WidthRequest = 200;
    }

    // ── IWorkflowTreeViewModel subscription ──────────────────────────────────

    private void OnTreeChanged(IWorkflowTreeViewModel? newTree) { MarkDirty(); }
    private void MarkDirty() { Invalidate(); }

    // ── IDrawable ────────────────────────────────────────────────────────────

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        try
        {
            if (!IsMinimapVisible) return;
            if (canvas is null) return;

            // Background
            canvas.FillColor = Color.FromArgb("#141922");
            canvas.FillRoundedRectangle(dirtyRect, 4);

            // Border
            canvas.StrokeColor = Color.FromArgb("#94A3B8");
            canvas.StrokeSize = 1;
            canvas.DrawRoundedRectangle(dirtyRect, 4);

            var tree = WorkflowTree;
            if (tree?.Nodes is null) return;

            // Compute content bounds from nodes
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool hasNode = false;

            foreach (var node in tree.Nodes)
            {
                float nx = double.IsNaN(node.Anchor.Horizontal) ? 0 : (float)node.Anchor.Horizontal;
                float ny = double.IsNaN(node.Anchor.Vertical) ? 0 : (float)node.Anchor.Vertical;
                float nw = (float)Math.Max(1, node.Size.Width);
                float nh = (float)Math.Max(1, node.Size.Height);
                minX = Math.Min(minX, nx);
                minY = Math.Min(minY, ny);
                maxX = Math.Max(maxX, nx + nw);
                maxY = Math.Max(maxY, ny + nh);
                hasNode = true;
            }

            if (!hasNode) return;

            float pad = 4;
            float contentW = maxX - minX + pad * 2;
            float contentH = maxY - minY + pad * 2;
            float drawW = dirtyRect.Width - pad * 2;
            float drawH = dirtyRect.Height - pad * 2;

            if (contentW <= 0 || contentH <= 0 || drawW <= 0 || drawH <= 0)
                return;

            float scale = Math.Min(drawW / contentW, drawH / contentH);

            // Draw nodes
            canvas.FillColor = Color.FromArgb("#38BDF8");
            foreach (var node in tree.Nodes)
            {
                float x = ((float)node.Anchor.Horizontal - minX + pad) * scale + pad;
                float y = ((float)node.Anchor.Vertical - minY + pad) * scale + pad;
                float w = Math.Max(2, (float)node.Size.Width * scale);
                float h = Math.Max(2, (float)node.Size.Height * scale);
                canvas.FillRoundedRectangle(x, y, w, h, 1);
            }

            // Draw viewport indicator
            float vx = ((float)ScrollOffsetX - (float)ContentOffsetX - minX + pad) * scale + pad;
            float vy = ((float)ScrollOffsetY - (float)ContentOffsetY - minY + pad) * scale + pad;
            float vw = Math.Max(4, (float)ViewportWidth * scale);
            float vh = Math.Max(4, (float)ViewportHeight * scale);
            canvas.StrokeColor = Color.FromArgb("#FFFFFF");
            canvas.StrokeSize = 1.5f;
            canvas.DrawRectangle(vx, vy, vw, vh);
        }
        catch
        {
            // Swallow rendering exceptions to avoid crashing the UI
        }
    }
}
