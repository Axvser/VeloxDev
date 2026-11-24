using System.Drawing;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WorkflowSystem.ViewHosting;

/// <summary>The WinForms closure of the Core minimap pipeline: it composes the renderer, viewport and input layers.</summary>
public sealed class MinimapPipeline : MinimapPipelineCore<
    Graphics,
    IMinimapRenderer<Graphics>,
    IMinimapViewportHost,
    IMinimapInputHost>
{
    /// <summary>Creates the pipeline for one overlay.</summary>
    /// <param name="renderer">The layer that draws one minimap pass.</param>
    /// <param name="viewport">The layer that reports and applies scroll state.</param>
    /// <param name="input">The layer that reports pointer state and owns capture.</param>
    /// <param name="metrics">The initial sizing and placement inputs; defaults to <see cref="MinimapMetrics.Default"/>.</param>
    public MinimapPipeline(
        IMinimapRenderer<Graphics> renderer,
        IMinimapViewportHost viewport,
        IMinimapInputHost input,
        MinimapMetrics? metrics = null)
        : base(renderer, viewport, input, metrics)
    {
    }
}

/// <summary>Renders the minimap through the owning overlay's protected <c>DrawMinimap</c> hook.</summary>
public sealed class MinimapRenderer : IMinimapRenderer<Graphics>
{
    private readonly WorkflowMinimapOverlay owner;

    /// <summary>Creates the renderer for <paramref name="owner"/>.</summary>
    /// <param name="owner">The overlay whose drawing hook receives every pass.</param>
    /// <exception cref="ArgumentNullException"><paramref name="owner"/> is <see langword="null"/>.</exception>
    public MinimapRenderer(WorkflowMinimapOverlay owner)
        => this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <inheritdoc />
    public void Render(Graphics surface, MinimapDrawContext context)
        => owner.DrawMinimapSurface(surface, context);
}

/// <summary>
/// Reports the WinForms surface scroll state to the minimap pipeline and applies navigation scroll.
/// </summary>
/// <remarks>
/// The WinForms camera keeps a signed pan with no native ceiling, while the Core pipeline clamps its scroll to
/// <c>[0, ScrollableWidth]</c>. Both offsets are shifted by a large constant so the pipeline's arithmetic stays in
/// a non-negative range; subtracting the same content offset again in <see cref="ApplyScroll"/> hands the surface
/// the original canvas-local position. The sentinel maxima keep the pipeline away from the tree's layout offsets, so
/// navigation never grows the model's <see cref="CanvasLayout.NegativeOffset"/> / <see cref="CanvasLayout.PositiveOffset"/>.
/// </remarks>
public sealed class MinimapViewportHost : IMinimapViewportHost
{
    // 平移是有符号的；把管线用的滚动值整体抬到非负区间，避免 ClampScrollOffset 写布局偏移。
    private const double ScrollOffsetShift = 1_000_000_000d;

    private readonly WorkflowMinimapOverlay owner;

    internal MinimapViewportHost(WorkflowMinimapOverlay owner)
        => this.owner = owner ?? throw new ArgumentNullException(nameof(owner));

    /// <inheritdoc />
    public double ScrollOffsetX => owner.ScrollOffsetX + ScrollOffsetShift;

    /// <inheritdoc />
    public double ScrollOffsetY => owner.ScrollOffsetY + ScrollOffsetShift;

    /// <inheritdoc />
    public double ContentOffsetX => owner.ContentOffsetX + ScrollOffsetShift;

    /// <inheritdoc />
    public double ContentOffsetY => owner.ContentOffsetY + ScrollOffsetShift;

    /// <inheritdoc />
    public double ViewportWidth => owner.ViewportWidth;

    /// <inheritdoc />
    public double ViewportHeight => owner.ViewportHeight;

    // WinForms 没有原生滚动上限：哨兵上限让导航能到任意位置，也不触发画布扩张。
    /// <inheritdoc />
    public double ScrollableWidth => double.MaxValue;

    /// <inheritdoc />
    public double ScrollableHeight => double.MaxValue;

    /// <inheritdoc />
    public void ApplyScroll(double x, double y)
        => owner.ApplyMinimapScroll(x - ContentOffsetX, y - ContentOffsetY);
}

/// <summary>
/// Reports the WinForms pointer state to the minimap pipeline. The overlay sets <see cref="PointerPosition"/> and
/// <see cref="IsPressed"/> from its pointer handlers; the capture calls go back to the host.
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
    public bool IsCaptured { get; private set; }

    /// <inheritdoc />
    public WorkflowBounds ClampRect { get; set; }

    /// <inheritdoc />
    public bool Capture()
    {
        var surface = owner?.Surface;
        if (surface is null)
        {
            IsCaptured = false;
            return false;
        }

        surface.Capture = true;
        IsCaptured = surface.Capture;
        return IsCaptured;
    }

    /// <inheritdoc />
    public void Release()
    {
        if (owner?.Surface is { } surface)
        {
            surface.Capture = false;
        }

        IsCaptured = false;
    }
}
