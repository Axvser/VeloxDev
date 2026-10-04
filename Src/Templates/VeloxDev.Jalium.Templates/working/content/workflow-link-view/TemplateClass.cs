// VeloxDev customization: The connection view. This element is yours — how a link looks is your OnRender. The
// adapter's WorkflowLinkAttachment owns everything that is not drawing: the binding and endpoint tracking
// (rebind-safe for a pooled view), the self-bounding, the geometry, the hit-test contract, and the link's pointer
// events.
using Jalium.UI;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>The connection view: one element that draws one link, and re-bounds itself when its endpoints move.</summary>
public sealed class TemplateClass : FrameworkElement
{
    private readonly WorkflowLinkAttachment link;

    public TemplateClass()
    {
        // One call attaches the rest. Pointers over this element reach the link's own events too:
        // link.PointerEntered / PointerLeft / PointerPressed / PointerReleased.
        link = WorkflowLinkAttachment.Attach(this);
        link.PortLayout = SlotView.Layout;
        link.LinkColor = (Color)ColorConverter.ConvertFromString("TemplateLinkColor");
        link.Thickness = TemplateLinkThickness;
    }

    /// <summary>Gets the attachment, for a view that wants the curve or the pointer events.</summary>
    public WorkflowLinkAttachment Link => link;

    // VeloxDev customization: the drawing. link.Paint is the resting line; replace or wrap it with whatever this
    // project's links look like. link.Curve is the four control points in this element's own coordinates, and the
    // geometry has already been published for hit-testing — draw anything you like along it.
    /// <inheritdoc />
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        link.Paint(dc);
    }
}
