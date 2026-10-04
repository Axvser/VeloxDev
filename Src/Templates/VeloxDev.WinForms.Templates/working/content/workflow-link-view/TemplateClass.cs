// VeloxDev customization: The connection view. This control is yours — how a link looks is your OnPaint. The
// adapter's WorkflowLinkAttachment owns everything that is not drawing: the endpoint subscription (rebind-safe for
// a pooled view), the window-region carving, the geometry, the hit-test contract, and the link's pointer events.
using System.Globalization;
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>The connection view: one control that draws one link, and repaints when its endpoints move.</summary>
public sealed class TemplateClass : Control
{
    private readonly WorkflowLinkAttachment link;

    public TemplateClass()
    {
        // Control styles are protected on Control, so they belong in the control — not in the attachment.
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint,
            true);

        // One call attaches the rest. Pointers over this control reach the link's own events too:
        // link.PointerEntered / PointerLeft / PointerPressed / PointerReleased.
        link = WorkflowLinkAttachment.Attach(this);
        link.SurfaceBackground = WorkflowLinkAttachment.ParseColor("TemplateSurfaceBackground");
        link.LineColor = WorkflowLinkAttachment.ParseColor("TemplateLinkColor");
        link.Thickness = float.Parse("TemplateLinkThickness", CultureInfo.InvariantCulture);
    }

    /// <summary>Gets the attachment, for a view that wants the curve or the pointer events.</summary>
    public WorkflowLinkAttachment Attachment => link;

    // VeloxDev customization: the drawing. link.Paint is the resting line; replace or wrap it with whatever this
    // project's links look like. link.Curve is the four control points in this control's own coordinates, and the
    // curve has already been published for hit-testing — draw anything you like along it.
    /// <inheritdoc />
    protected override void OnPaint(PaintEventArgs e)
    {
        if (e is null) throw new System.ArgumentNullException(nameof(e));

        base.OnPaint(e);
        link.Paint(e.Graphics);
    }
}
