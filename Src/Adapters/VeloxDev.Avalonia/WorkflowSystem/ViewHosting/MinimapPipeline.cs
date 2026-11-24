using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>The Avalonia closure of the Core minimap pipeline: it composes the renderer, viewport and input layers.</summary>
public sealed class MinimapPipeline : MinimapPipelineCore<
    DrawingContext,
    IMinimapRenderer<DrawingContext>,
    IMinimapViewportHost,
    IMinimapInputHost>
{
    /// <summary>Creates the pipeline for one surface.</summary>
    /// <param name="renderer">The layer that draws one minimap pass.</param>
    /// <param name="viewport">The layer that reports and applies scroll state.</param>
    /// <param name="input">The layer that reports pointer state and owns capture.</param>
    /// <param name="metrics">The initial sizing and placement inputs; defaults to <see cref="MinimapMetrics.Default"/>.</param>
    public MinimapPipeline(
        IMinimapRenderer<DrawingContext> renderer,
        IMinimapViewportHost viewport,
        IMinimapInputHost input,
        MinimapMetrics? metrics = null)
        : base(renderer, viewport, input, metrics)
    {
    }
}

/// <summary>Renders the minimap through the owning overlay's protected <c>DrawMinimap</c> hook.</summary>
public sealed class MinimapRenderer : IMinimapRenderer<DrawingContext>
{
    private readonly WorkflowMinimapOverlay owner;

    /// <summary>Creates the renderer for <paramref name="owner"/>.</summary>
    /// <param name="owner">The overlay whose drawing hook receives every pass.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    public MinimapRenderer(WorkflowMinimapOverlay owner)
        => this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <inheritdoc />
    public void Render(DrawingContext surface, MinimapDrawContext context)
        => owner.DrawMinimapSurface(surface, context);
}

/// <summary>
/// Reports the Avalonia surface scroll state to the minimap pipeline and applies navigation scroll.
/// The overlay constructor uses the overlay-backed form so the surface behavior's pushed values win;
/// the standalone form drives a <see cref="ScrollViewer"/> directly.
/// </summary>
public sealed class MinimapViewportHost : IMinimapViewportHost
{
    private readonly WorkflowMinimapOverlay? owner;
    private readonly ScrollViewer? scrollViewer;

    internal MinimapViewportHost(WorkflowMinimapOverlay owner)
        => this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <summary>Creates a host that reads from and scrolls <paramref name="scrollViewer"/> directly.</summary>
    /// <param name="scrollViewer">The surface scroll viewer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scrollViewer"/> is <see langword="null"/>.</exception>
    public MinimapViewportHost(ScrollViewer scrollViewer)
        => this.scrollViewer = scrollViewer ?? throw new ArgumentNullException(nameof(scrollViewer));

    private ScrollViewer? Resolved => owner?.MinimapScrollViewer ?? scrollViewer;

    /// <inheritdoc />
    public double ScrollOffsetX => owner?.ScrollOffsetX ?? Resolved?.Offset.X ?? 0d;

    /// <inheritdoc />
    public double ScrollOffsetY => owner?.ScrollOffsetY ?? Resolved?.Offset.Y ?? 0d;

    /// <inheritdoc />
    public double ContentOffsetX => owner?.ContentOffsetX ?? 0d;

    /// <inheritdoc />
    public double ContentOffsetY => owner?.ContentOffsetY ?? 0d;

    /// <inheritdoc />
    public double ViewportWidth => owner?.ViewportWidth ?? Resolved?.Viewport.Width ?? 0d;

    /// <inheritdoc />
    public double ViewportHeight => owner?.ViewportHeight ?? Resolved?.Viewport.Height ?? 0d;

    // Avalonia 11 的 ScrollViewer 没有 ScrollableWidth/Height（那是 WPF 的属性），原生上限由 extent 与 viewport 相减得到。
    /// <inheritdoc />
    public double ScrollableWidth => Resolved is { } viewer
        ? WorkflowSurfaceMath.ScrollMax(viewer.Extent.Width, viewer.Viewport.Width)
        : 0d;

    /// <inheritdoc />
    public double ScrollableHeight => Resolved is { } viewer
        ? WorkflowSurfaceMath.ScrollMax(viewer.Extent.Height, viewer.Viewport.Height)
        : 0d;

    /// <inheritdoc />
    public void ApplyScroll(double x, double y)
    {
        if (owner is not null)
        {
            owner.ApplyScrollOffsets(x, y);
            return;
        }

        if (scrollViewer is not null)
        {
            scrollViewer.Offset = new global::Avalonia.Vector(x, y);
        }
    }
}

/// <summary>
/// Reports the Avalonia pointer state to the minimap pipeline. The overlay sets <see cref="PointerPosition"/> and
/// <see cref="IsPressed"/> from its pointer handlers and tracks the active <see cref="IPointer"/> for capture.
/// </summary>
public sealed class MinimapInputHost : IMinimapInputHost
{
    private readonly WorkflowMinimapOverlay? owner;
    private IPointer? pointer;

    /// <summary>Creates a detached host for tests and custom surfaces.</summary>
    public MinimapInputHost()
    {
    }

    internal MinimapInputHost(WorkflowMinimapOverlay owner)
        => this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <inheritdoc />
    public (double X, double Y) PointerPosition { get; set; }

    /// <inheritdoc />
    public bool IsPressed { get; set; }

    /// <inheritdoc />
    public bool IsCaptured { get; private set; }

    /// <inheritdoc />
    public WorkflowBounds ClampRect { get; set; }

    // 捕获按 IPointer 逐实例：同一浮层上多指针都能各自拖拽，释放时也只放掉自己那一只。
    internal void TrackPointer(IPointer? value) => pointer = value;

    /// <inheritdoc />
    public bool Capture()
    {
        if (pointer is null || owner is null)
        {
            IsCaptured = false;
            return false;
        }

        // Avalonia 的 IPointer.Capture 不返回成败；失败时平台会发 PointerCaptureLost，覆盖层据此收尾。
        pointer.Capture(owner);
        IsCaptured = true;
        return true;
    }

    /// <inheritdoc />
    public void Release()
    {
        pointer?.Capture(null);
        IsCaptured = false;
    }
}
