using Microsoft.UI.Xaml.Input;
using System;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>The WinUI closure of the Core minimap pipeline: it composes the renderer, viewport and input layers.</summary>
public sealed class MinimapPipeline : MinimapPipelineCore<
    WorkflowMinimapOverlay,
    IMinimapRenderer<WorkflowMinimapOverlay>,
    IMinimapViewportHost,
    IMinimapInputHost>
{
    /// <summary>Creates the pipeline for one surface.</summary>
    /// <param name="renderer">The layer that draws one minimap pass.</param>
    /// <param name="viewport">The layer that reports and applies scroll state.</param>
    /// <param name="input">The layer that reports pointer state and owns capture.</param>
    /// <param name="metrics">The initial sizing and placement inputs; defaults to <see cref="MinimapMetrics.Default"/>.</param>
    public MinimapPipeline(
        IMinimapRenderer<WorkflowMinimapOverlay> renderer,
        IMinimapViewportHost viewport,
        IMinimapInputHost input,
        MinimapMetrics? metrics = null)
        : base(renderer, viewport, input, metrics)
    {
    }
}

/// <summary>Renders the minimap through the owning overlay's protected <c>DrawMinimap</c> hook.</summary>
public sealed class MinimapRenderer : IMinimapRenderer<WorkflowMinimapOverlay>
{
    private readonly WorkflowMinimapOverlay owner;

    /// <summary>Creates the renderer for <paramref name="owner"/>.</summary>
    /// <param name="owner">The overlay whose drawing hook receives every pass.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    public MinimapRenderer(WorkflowMinimapOverlay owner)
        => this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <inheritdoc />
    public void Render(WorkflowMinimapOverlay surface, MinimapDrawContext context)
        => owner.DrawMinimapSurface(context);
}

/// <summary>
/// Reports the WinUI surface scroll state to the minimap pipeline and applies navigation scroll through the
/// surface's single write entry, so an in-flight <c>ChangeView</c> keeps its committed offset.
/// </summary>
public sealed class MinimapViewportHost : IMinimapViewportHost
{
    private readonly WorkflowMinimapOverlay owner;

    /// <summary>Creates the host for <paramref name="owner"/>.</summary>
    /// <param name="owner">The overlay whose dependency properties carry the pushed scroll state.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    public MinimapViewportHost(WorkflowMinimapOverlay owner)
        => this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <inheritdoc />
    public double ScrollOffsetX => owner.ScrollOffsetX;

    /// <inheritdoc />
    public double ScrollOffsetY => owner.ScrollOffsetY;

    /// <inheritdoc />
    public double ContentOffsetX => owner.ContentOffsetX;

    /// <inheritdoc />
    public double ContentOffsetY => owner.ContentOffsetY;

    /// <inheritdoc />
    public double ViewportWidth => owner.ViewportWidth;

    /// <inheritdoc />
    public double ViewportHeight => owner.ViewportHeight;

    /// <inheritdoc />
    public double ScrollableWidth => owner.MinimapScrollViewer?.ScrollableWidth ?? 0d;

    /// <inheritdoc />
    public double ScrollableHeight => owner.MinimapScrollViewer?.ScrollableHeight ?? 0d;

    /// <inheritdoc />
    public void ApplyScroll(double x, double y) => owner.ApplyScrollOffsets(x, y);
}

/// <summary>
/// Reports the WinUI pointer state to the minimap pipeline. The overlay sets <see cref="PointerPosition"/>,
/// <see cref="IsPressed"/> and <see cref="CurrentPointer"/> from its pointer handlers; capture goes back to
/// the overlay, which is the element the platform captures.
/// </summary>
public sealed class MinimapInputHost : IMinimapInputHost
{
    private readonly WorkflowMinimapOverlay? owner;

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
    public bool IsCaptured => owner?.PointerCaptures is { Count: > 0 };

    /// <inheritdoc />
    public WorkflowBounds ClampRect { get; set; }

    // 按下时由覆盖层写入，捕获点必须是同一枚指针。
    internal Pointer? CurrentPointer { get; set; }

    /// <inheritdoc />
    public bool Capture()
    {
        if (owner is null || CurrentPointer is null)
        {
            return false;
        }

        return owner.CapturePointer(CurrentPointer);
    }

    /// <inheritdoc />
    public void Release()
    {
        owner?.ReleasePointerCaptures();
        CurrentPointer = null;
    }
}
