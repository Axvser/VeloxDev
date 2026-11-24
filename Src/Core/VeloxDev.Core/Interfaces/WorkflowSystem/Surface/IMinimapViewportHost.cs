namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The platform layer that reports the surface scroll state and applies a new scroll offset.
/// The pipeline reads it while mapping the viewport indicator and writes back through
/// <see cref="ApplyScroll"/> during minimap navigation.
/// </summary>
/// <remarks>
/// Offsets and the visible viewport are expressed in the host's own scroll space. That space's absolute origin is
/// the host's choice — a scroll viewer may report its native offset, and a surface with a signed pan camera may
/// report world coordinates through a constant lift. The pipeline only relies on <c>scroll − contentOffset</c> and
/// on <see cref="ScrollableWidth"/> / <see cref="ScrollableHeight"/> for the navigation clamp, so any two getters
/// that share one origin stay consistent with <see cref="ApplyScroll"/>.
/// </remarks>
public interface IMinimapViewportHost
{
    /// <summary>The surface's horizontal scroll offset, in the host's scroll space.</summary>
    double ScrollOffsetX { get; }

    /// <summary>The surface's vertical scroll offset, in the host's scroll space.</summary>
    double ScrollOffsetY { get; }

    /// <summary>The canvas content offset on the horizontal axis.</summary>
    double ContentOffsetX { get; }

    /// <summary>The canvas content offset on the vertical axis.</summary>
    double ContentOffsetY { get; }

    /// <summary>The visible viewport width.</summary>
    double ViewportWidth { get; }

    /// <summary>The visible viewport height.</summary>
    double ViewportHeight { get; }

    /// <summary>
    /// The platform's maximum horizontal scroll offset, or a sentinel when the surface has no native ceiling and the
    /// pipeline's clamp must not fire (a signed pan camera).
    /// </summary>
    double ScrollableWidth { get; }

    /// <summary>
    /// The platform's maximum vertical scroll offset, or a sentinel when the surface has no native ceiling and the
    /// pipeline's clamp must not fire (a signed pan camera).
    /// </summary>
    double ScrollableHeight { get; }

    /// <summary>Moves the surface to the given scroll offset.</summary>
    /// <param name="x">The horizontal scroll offset, already clamped by the pipeline.</param>
    /// <param name="y">The vertical scroll offset, already clamped by the pipeline.</param>
    void ApplyScroll(double x, double y);
}
