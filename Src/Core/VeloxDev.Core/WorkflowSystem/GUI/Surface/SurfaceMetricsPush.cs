using System;
using VeloxDev.WorkflowSystem.StandardEx;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Pushes one <see cref="SurfaceMetrics"/> snapshot into the shared surface contracts: the grid decorator
/// offsets, the minimap placement, and the tree's viewport plus its persisted viewport position.
/// </summary>
/// <remarks>
/// The subtraction that turns scroll coordinates into canvas-local ones lives in
/// <see cref="WorkflowSurfaceMath.ToCanvasLocal"/>, so adapters do not re-derive it per platform. A missing
/// decorator, minimap or tree is skipped rather than treated as an error; the surface may run before every
/// optional part is resolved.
/// </remarks>
/// <seealso cref="SurfaceMetrics"/>
/// <seealso cref="ISurfaceMetricsSource"/>
public static class SurfaceMetricsPush
{
    /// <summary>Pushes scroll/content offsets and the virtualization ruler inset into a grid decorator.</summary>
    /// <param name="metrics">The snapshot to push.</param>
    /// <param name="decorator">The decorator to update.</param>
    /// <param name="tree">The tree whose virtualization inset is corrected, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="decorator"/> is <see langword="null"/>.</exception>
    public static void PushGrid(
        SurfaceMetrics metrics, IWorkflowGridDecorator decorator, ITreeComponent? tree)
    {
        if (decorator is null) throw new ArgumentNullException(nameof(decorator));

        decorator.ScrollOffsetX = metrics.ScrollOffsetX;
        decorator.ScrollOffsetY = metrics.ScrollOffsetY;
        decorator.ContentOffsetX = metrics.ContentOffsetX;
        decorator.ContentOffsetY = metrics.ContentOffsetY;

        // 让虚拟化可见区修正与装饰器的浮动标尺带保持一致，标尺下方的节点才不会提前一个标尺厚度被剔除。
        tree?.SetVirtualizeInset(left: decorator.RulerBand, top: decorator.RulerBand);
    }

    /// <summary>Pushes offsets, viewport size, tree and available edges into a minimap overlay.</summary>
    /// <param name="metrics">The snapshot to push.</param>
    /// <param name="minimap">The minimap to update.</param>
    /// <param name="tree">The tree the minimap renders, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="minimap"/> is <see langword="null"/>.</exception>
    public static void PushMinimap(
        SurfaceMetrics metrics, IWorkflowMinimapOverlay minimap, ITreeComponent? tree)
    {
        if (minimap is null) throw new ArgumentNullException(nameof(minimap));

        minimap.ScrollOffsetX = metrics.ScrollOffsetX;
        minimap.ScrollOffsetY = metrics.ScrollOffsetY;
        minimap.ContentOffsetX = metrics.ContentOffsetX;
        minimap.ContentOffsetY = metrics.ContentOffsetY;
        minimap.ViewportWidth = metrics.ViewportWidth;
        minimap.ViewportHeight = metrics.ViewportHeight;
        minimap.WorkflowTree = tree;
        minimap.AvailableRight = metrics.Available.Right;
        minimap.AvailableTop = metrics.Available.Top;
        minimap.AvailableBottom = metrics.Available.Bottom;
        minimap.MinimapMargin = metrics.Available.Margin;
    }

    /// <summary>Pushes the visible viewport into the tree and persists its canvas-local position.</summary>
    /// <param name="metrics">The snapshot to push.</param>
    /// <param name="tree">The tree to update.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static void PushViewport(SurfaceMetrics metrics, ITreeComponent tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        var localX = WorkflowSurfaceMath.ToCanvasLocal(metrics.ScrollOffsetX, metrics.ContentOffsetX);
        var localY = WorkflowSurfaceMath.ToCanvasLocal(metrics.ScrollOffsetY, metrics.ContentOffsetY);

        tree.GetHelper().Viewport = new Viewport(
            localX, localY, metrics.ViewportWidth, metrics.ViewportHeight);

        // 持久化视口位置，使其能熬过序列化往返。
        tree.Layout.ViewportOffset = new Offset(localX, localY);
    }
}
