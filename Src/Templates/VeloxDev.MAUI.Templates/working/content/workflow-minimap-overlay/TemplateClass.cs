using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// A minimap overlay that renders a thumbnail overview of a workflow surface.
/// Implements <see cref="IWorkflowMinimapOverlay"/> for automatic data updates
/// from <see cref="WorkflowSurfaceBehavior"/>.
/// </summary>
public sealed class TemplateClass : GraphicsView, IDrawable, IWorkflowMinimapOverlay
{
    // ── Bindable Properties ──────────────────────────────────────────────────

    private static void OnVisualProp(BindableObject b, object o, object n) => ((TemplateClass)b).Invalidate();

    public static readonly BindableProperty ScrollOffsetXProperty = BindableProperty.Create(nameof(ScrollOffsetX), typeof(double), typeof(TemplateClass), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ScrollOffsetYProperty = BindableProperty.Create(nameof(ScrollOffsetY), typeof(double), typeof(TemplateClass), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ContentOffsetXProperty = BindableProperty.Create(nameof(ContentOffsetX), typeof(double), typeof(TemplateClass), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ContentOffsetYProperty = BindableProperty.Create(nameof(ContentOffsetY), typeof(double), typeof(TemplateClass), 0d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportWidthProperty = BindableProperty.Create(nameof(ViewportWidth), typeof(double), typeof(TemplateClass), 1d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty ViewportHeightProperty = BindableProperty.Create(nameof(ViewportHeight), typeof(double), typeof(TemplateClass), 1d, propertyChanged: OnVisualProp);
    public static readonly BindableProperty WorkflowTreeProperty = BindableProperty.Create(nameof(WorkflowTree), typeof(IWorkflowTreeViewModel), typeof(TemplateClass), null,
        propertyChanged: (b, o, n) => ((TemplateClass)b).OnTreeChanged((IWorkflowTreeViewModel?)n));
    public static readonly BindableProperty IsMinimapVisibleProperty = BindableProperty.Create(nameof(IsMinimapVisible), typeof(bool), typeof(TemplateClass), true, propertyChanged: OnVisualProp);

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

    public TemplateClass()
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
        if (!IsMinimapVisible) return;

        // Background
        canvas.FillColor = Color.FromArgb("TemplateMinimapBackground");
        canvas.FillRoundedRectangle(dirtyRect, 4);

        // Border
        canvas.StrokeColor = Color.FromArgb("TemplateMinimapBorder");
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
            float nx = (float)node.Anchor.Horizontal;
            float ny = (float)node.Anchor.Vertical;
            float nw = (float)node.Size.Width;
            float nh = (float)node.Size.Height;
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
        float scale = Math.Min(drawW / contentW, drawH / contentH);
        float ox = (float)ScrollOffsetX - (float)ContentOffsetX;
        float oy = (float)ScrollOffsetY - (float)ContentOffsetY;

        // Draw nodes
        canvas.FillColor = Color.FromArgb("TemplateNodeFill");
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

        canvas.StrokeColor = Color.FromArgb("TemplateViewportStroke");
        canvas.StrokeSize = 1.5f;
        canvas.DrawRectangle(vx, vy, vw, vh);
    }
}
