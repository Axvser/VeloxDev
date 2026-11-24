using System.Collections.Generic;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Geometry and tree data for one minimap draw pass, computed by the adapter immediately before it draws.
/// </summary>
/// <remarks>
/// <para>
/// The adapter exposes a <c>protected virtual</c> draw method that receives this context, so a template can
/// override the drawing and still call the adapter's default implementation. Members are pre-mapped where the
/// adapter already had to map them: <see cref="Nodes"/> and <see cref="ContentBounds"/> / <see cref="ViewportBounds"/>
/// are canvas-local, while <see cref="MappedNodes"/> / <see cref="MappedViewport"/> are minimap-local and already
/// carry the minimum thumb size.
/// </para>
/// </remarks>
public sealed class MinimapDrawContext
{
    /// <summary>The tree whose nodes the minimap renders.</summary>
    public ITreeComponent? Tree { get; init; }

    /// <summary>Canvas-local bounds covering every node.</summary>
    public WorkflowBounds ContentBounds { get; init; }

    /// <summary>Canvas-local rectangle currently visible in the surface.</summary>
    public WorkflowBounds ViewportBounds { get; init; }

    /// <summary>Node rectangles in canvas-local space.</summary>
    public IReadOnlyList<WorkflowBounds> Nodes { get; init; } = System.Array.Empty<WorkflowBounds>();

    /// <summary>Node rectangles mapped into minimap-local space with the minimum thumb size applied.</summary>
    public IReadOnlyList<WorkflowBounds> MappedNodes { get; init; } = System.Array.Empty<WorkflowBounds>();

    /// <summary>The viewport indicator mapped into minimap-local space; empty when there is no viewport.</summary>
    public WorkflowBounds MappedViewport { get; init; }

    /// <summary>The drawable minimap width in device-independent pixels.</summary>
    public double MinimapWidth { get; init; }

    /// <summary>The drawable minimap height in device-independent pixels.</summary>
    public double MinimapHeight { get; init; }

    /// <summary>The inner padding around the mapped content.</summary>
    public double Padding { get; init; }

    /// <summary>Horizontal fit origin produced by <see cref="WorkflowSurfaceMath.MinimapFit"/>.</summary>
    public double OriginX { get; init; }

    /// <summary>Vertical fit origin produced by <see cref="WorkflowSurfaceMath.MinimapFit"/>.</summary>
    public double OriginY { get; init; }

    /// <summary>The content-to-minimap scale factor.</summary>
    public double Scale { get; init; }

    /// <summary>Corner radius applied to each node thumbnail.</summary>
    public double NodeCornerRadius { get; init; }

    /// <summary>Corner radius applied to the minimap frame.</summary>
    public double MinimapCornerRadius { get; init; }

    /// <summary>Whether any node produced a non-empty content box.</summary>
    public bool HasContent => !ContentBounds.IsEmpty;

    /// <summary>Whether the surface reported a non-empty viewport box.</summary>
    public bool HasViewport => !ViewportBounds.IsEmpty;
}
