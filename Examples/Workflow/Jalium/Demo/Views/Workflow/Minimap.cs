using Jalium.UI.Controls;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>Minimap overlay for the node-editor surface. Subclasses the adapter's self-contained
/// <see cref="WorkflowMinimapOverlay"/> (content-fit over the node bounding box, cyan node rects,
/// translucent viewport block, drag-to-pan through the surface's edge-aware navigation) exactly as
/// the Trimmed demo's MinimapOverlay does — its palette is the adapter's default, the same colours
/// this overlay used to hard-code. The composing window feeds its <see cref="WorkflowMinimapOverlay.WorkflowTree"/>,
/// its <see cref="WorkflowMinimapOverlay.ScrollViewer"/> and the scroll / content-offset / viewport
/// numbers whenever the surface scrolls or pans.
/// </summary>
internal sealed class Minimap : WorkflowMinimapOverlay
{
    public Minimap(ScrollViewer viewer)
    {
        ScrollViewer = viewer;
    }
}
