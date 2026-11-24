
namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Data-exchange contract for the workflow minimap overlay. Derives from <see cref="IWorkflowGridDecorator"/>
/// (the minimap also needs the scroll/content offsets), plus the viewport size and the tree to render.
///
/// The surface behavior pushes all members before each render pass; the minimap draws node/link thumbnails
/// and a viewport indicator, and drag navigation resolves back into world scroll offsets.
///
/// Unified in Core so all 7 GUI adapters share one definition (previously 6 adapters carried an identical
/// flat copy and Jalium a derived copy under <c>VeloxDev.WorkflowSystem.AttachedBehaviors</c>; the derived
/// shape is the normalized form).
/// </summary>
public interface IWorkflowMinimapOverlay : IWorkflowGridDecorator
{
    /// <summary>
    /// Viewport width in the surface's scroll space (the same space as the pushed scroll offsets), used to scale
    /// the viewport indicator and resolve drags.
    /// </summary>
    double ViewportWidth { get; set; }

    /// <summary>Viewport height in the surface's scroll space (the same space as the pushed scroll offsets).</summary>
    double ViewportHeight { get; set; }

    /// <summary>The workflow tree whose nodes/links the minimap renders.</summary>
    ITreeComponent? WorkflowTree { get; set; }

    /// <summary>Whether the minimap overlay should render.</summary>
    bool IsMinimapVisible { get; set; }

    /// <summary>
    /// The x of the content viewport's right edge in this overlay's local pixels, excluding the vertical
    /// scrollbar. The surface behavior recomputes it whenever the layout or scroll range changes.
    /// </summary>
    double AvailableRight { get; set; }

    /// <summary>The y of the content viewport's top edge, below the horizontal ruler band.</summary>
    double AvailableTop { get; set; }

    /// <summary>The y of the content viewport's bottom edge in local pixels, excluding the horizontal scrollbar.</summary>
    double AvailableBottom { get; set; }

    /// <summary>The gap kept between the minimap and the available corner.</summary>
    double MinimapMargin { get; set; }
}
