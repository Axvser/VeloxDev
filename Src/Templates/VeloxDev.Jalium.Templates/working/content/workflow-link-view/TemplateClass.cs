// VeloxDev customization: The connection view. The adapter's WorkflowLinkView owns the binding, the endpoint
// tracking, the self-bounding and the geometry; this file supplies the stroke and points at the card layout the
// endpoints are read from. Rename SlotView below if you renamed that item.
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// The link view: the adapter's <see cref="WorkflowLinkView"/> with this project's stroke.
/// </summary>
public sealed class TemplateClass : WorkflowLinkView
{
    public TemplateClass()
    {
        PortLayout = SlotView.Layout;
        LinkColor = (Color)ColorConverter.ConvertFromString("TemplateLinkColor");
        Thickness = TemplateLinkThickness;
    }
}
