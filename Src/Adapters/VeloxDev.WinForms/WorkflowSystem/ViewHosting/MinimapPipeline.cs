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
/// Reports the WinForms surface camera state to the minimap pipeline and applies navigation targets.
/// </summary>
/// <remarks>
/// The surface pushes canvas-local (world) offsets because the camera already carries the world origin, so
/// <see cref="ContentOffsetX"/> / <see cref="ContentOffsetY"/> stay zero and the viewport is reported in world units.
/// The pan camera is signed and has no native ceiling, while the Core pipeline clamps its scroll to
/// <c>[0, ScrollableWidth]</c> and grows <see cref="CanvasLayout.NegativeOffset"/> / <see cref="CanvasLayout.PositiveOffset"/>
/// on overshoot. Both offsets are therefore lifted by the same constant, keeping the pipeline's arithmetic
/// non-negative and its clamp inert; <see cref="ApplyScroll"/> subtracts the shifted content offset again and hands
/// the surface the original signed world origin. The sentinel maximum keeps navigation from writing the tree's
/// layout offsets.
/// </remarks>
public sealed class MinimapViewportHost : IMinimapViewportHost
{
    // 抬 1e9 而不是 MaxValue：双精度要留出世界坐标的小数位，否则加回去的目标会淹没在量级里。
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
