namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The platform layer that reports the surface scroll state and applies a new scroll offset.
/// The pipeline reads it while mapping the viewport indicator and writes back through
/// <see cref="ApplyScroll"/> during minimap navigation.
/// </summary>
public interface IMinimapViewportHost
{
    /// <summary>The surface's horizontal scroll offset.</summary>
    double ScrollOffsetX { get; }

    /// <summary>The surface's vertical scroll offset.</summary>
    double ScrollOffsetY { get; }

    /// <summary>The canvas content offset on the horizontal axis.</summary>
    double ContentOffsetX { get; }

    /// <summary>The canvas content offset on the vertical axis.</summary>
    double ContentOffsetY { get; }

    /// <summary>The visible viewport width.</summary>
    double ViewportWidth { get; }

    /// <summary>The visible viewport height.</summary>
    double ViewportHeight { get; }

    /// <summary>The platform's maximum horizontal scroll offset.</summary>
    double ScrollableWidth { get; }

    /// <summary>The platform's maximum vertical scroll offset.</summary>
    double ScrollableHeight { get; }

    /// <summary>Moves the surface to the given scroll offset.</summary>
    /// <param name="x">The horizontal scroll offset, already clamped by the pipeline.</param>
    /// <param name="y">The vertical scroll offset, already clamped by the pipeline.</param>
    void ApplyScroll(double x, double y);
}
