namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Broadcasts the current <see cref="SurfaceViewport"/> snapshot to viewport-driven overlays
/// (grid decorator, minimap) without forcing the whole surface to re-render. Pushed through a
/// cascading value by the surface behavior; consumers subscribe to <see cref="Changed"/> and re-render
/// themselves. Mirrors the XAML adapters, where the scroll viewer updates overlay transforms directly
/// while the node/link content stays untouched.
///
/// Promoted to Core from the Razor adapter so the push channel is a standard data-exchange primitive.
/// </summary>
public sealed class SurfaceViewportFeed
{
    /// <summary>Gets the last published viewport snapshot (null until the first publish).</summary>
    public SurfaceViewport? Current { get; private set; }

    /// <summary>Raised whenever a new viewport snapshot is published (scroll, pan, or resize).</summary>
    public event Action<SurfaceViewport>? Changed;

    /// <summary>
    /// Publishes a new viewport snapshot. Updates <see cref="Current"/> and raises
    /// <see cref="Changed"/> so subscribers can re-render their cheap overlay content.
    /// </summary>
    public void Publish(SurfaceViewport viewport)
    {
        Current = viewport;
        Changed?.Invoke(viewport);
    }
}
