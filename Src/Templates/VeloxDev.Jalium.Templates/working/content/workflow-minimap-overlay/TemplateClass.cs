using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>Minimap overlay for the node-editor surface. Subclasses the adapter's self-contained
/// <see cref="WorkflowMinimapOverlay"/> (content-fit over the node bounding box, cyan node rects,
/// drag-to-pan); the composing window feeds its WorkflowTree, ScrollViewer, and the
/// Scroll/ContentOffset/Viewport DPs whenever the surface scrolls or pans.</summary>
public class TemplateClass : WorkflowMinimapOverlay
{
    public TemplateClass()
    {
    }
}
