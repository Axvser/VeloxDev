namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Supplies a workflow surface's current <see cref="SurfaceMetrics"/> to the shared push helpers.
/// </summary>
/// <remarks>
/// An adapter implements this over its own scroll container and decorators. It is read on every surface pass,
/// so it must be allocation-free and return the current measurements rather than a cached first frame.
/// </remarks>
/// <seealso cref="SurfaceMetrics"/>
/// <seealso cref="SurfaceMetricsPush"/>
public interface ISurfaceMetricsSource
{
    /// <summary>Reads the current surface measurements.</summary>
    /// <returns>The snapshot used to place overlays and to feed the tree's viewport.</returns>
    SurfaceMetrics ReadMetrics();
}
