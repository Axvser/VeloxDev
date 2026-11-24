using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>The MAUI closure of the Core minimap pipeline: it composes the renderer, viewport and input layers.</summary>
public sealed class MinimapPipeline : MinimapPipelineCore<
    ICanvas,
    IMinimapRenderer<ICanvas>,
    IMinimapViewportHost,
    IMinimapInputHost>
{
    /// <summary>Creates the pipeline for one surface.</summary>
    /// <param name="renderer">The layer that draws one minimap pass.</param>
    /// <param name="viewport">The layer that reports and applies scroll state.</param>
    /// <param name="input">The layer that reports pointer state and owns capture.</param>
    /// <param name="metrics">The initial sizing and placement inputs; defaults to <see cref="MinimapMetrics.Default"/>.</param>
    public MinimapPipeline(
        IMinimapRenderer<ICanvas> renderer,
        IMinimapViewportHost viewport,
        IMinimapInputHost input,
        MinimapMetrics? metrics = null)
        : base(renderer, viewport, input, metrics)
    {
    }
}

/// <summary>Renders the minimap through the owning overlay's protected <c>DrawMinimap</c> hook.</summary>
public sealed class MinimapRenderer : IMinimapRenderer<ICanvas>
{
    private readonly WorkflowMinimapOverlay owner;

    /// <summary>Creates the renderer for <paramref name="owner"/>.</summary>
    /// <param name="owner">The overlay whose drawing hook receives every pass.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    public MinimapRenderer(WorkflowMinimapOverlay owner)
        => this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <inheritdoc />
    public void Render(ICanvas surface, MinimapDrawContext context)
        => owner.DrawMinimapSurface(surface, context);
}

/// <summary>
/// Reports the MAUI surface scroll state to the minimap pipeline and applies navigation scroll.
/// The overlay-backed form reads the offsets the surface behavior pushed and scrolls the resolved
/// <see cref="ScrollView"/>.
/// </summary>
public sealed class MinimapViewportHost : IMinimapViewportHost
{
    private readonly WorkflowMinimapOverlay owner;

    /// <summary>Creates the viewport host over <paramref name="owner"/>.</summary>
    /// <param name="owner">The overlay that supplies the pushed offsets and the surface scroll view.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    public MinimapViewportHost(WorkflowMinimapOverlay owner)
        => this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <inheritdoc />
    public double ScrollOffsetX => Sanitize(owner.ScrollOffsetX);

    /// <inheritdoc />
    public double ScrollOffsetY => Sanitize(owner.ScrollOffsetY);

    /// <inheritdoc />
    public double ContentOffsetX => Sanitize(owner.ContentOffsetX);

    /// <inheritdoc />
    public double ContentOffsetY => Sanitize(owner.ContentOffsetY);

    /// <inheritdoc />
    public double ViewportWidth => owner.SafeViewportWidth;

    /// <inheritdoc />
    public double ViewportHeight => owner.SafeViewportHeight;

    /// <inheritdoc />
    public double ScrollableWidth => owner.ModelScrollWidth;

    /// <inheritdoc />
    public double ScrollableHeight => owner.ModelScrollHeight;

    /// <inheritdoc />
    public void ApplyScroll(double x, double y) => owner.ApplyScrollOffsets(x, y);

    private static double Sanitize(double value) => double.IsFinite(value) ? value : 0d;
}

/// <summary>
/// Reports the MAUI pointer state to the minimap pipeline. The overlay's gesture and native pointer
/// handlers set <see cref="PointerPosition"/> and <see cref="IsPressed"/>; capture bookkeeping stays
/// here because MAUI-driven drags are kept alive by the platform gesture pipeline, not by this host.
/// </summary>
public sealed class MinimapInputHost : IMinimapInputHost
{
    /// <inheritdoc />
    public (double X, double Y) PointerPosition { get; set; }

    /// <inheritdoc />
    public bool IsPressed { get; set; }

    /// <inheritdoc />
    public bool IsCaptured { get; private set; }

    /// <inheritdoc />
    public WorkflowBounds ClampRect { get; set; }

    /// <inheritdoc />
    public bool Capture()
    {
        IsCaptured = true;
        return true;
    }

    /// <inheritdoc />
    public void Release() => IsCaptured = false;
}
