// VeloxDev customization: The connection view. The adapter's WorkflowLinkView owns the binding, the endpoint
// tracking, the self-bounding and the geometry; this file supplies the stroke and points at the card layout the
// endpoints are read from. Rename SlotView below if you renamed that item.
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The link view: the adapter's <see cref="WorkflowLinkView"/> with this project's stroke.
/// </summary>
public sealed class LinkView : WorkflowLinkView
{
    public LinkView()
    {
        PortLayout = SlotView.Layout;
        LinkColor = Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF);
        Thickness = 2;
    }
}
