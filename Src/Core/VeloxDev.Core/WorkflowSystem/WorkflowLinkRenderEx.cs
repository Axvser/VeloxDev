namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The render-readiness contract for Links.
///
/// On the virtualized canvas, a Link and its two endpoint nodes are realized in adjacent batches by the
/// ViewManager; each GUI's slot-layout behavior asynchronously measures and writes the Slot anchors into
/// <c>slot.Anchor</c> at render priority. If a Link renders before the measurement lands, it reads the default
/// "no value" anchor (<see cref="double.NaN"/>), drawing a misaligned frame that then jumps back — visual flicker.
///
/// The mechanism lives in the core (<see cref="WorkflowSlotUpdateGate"/>): a Slot anchor defaults to NaN
/// (unmeasured), and before rendering a link both endpoint anchors are checked. Once the GUI measurement writes
/// real coordinates into the anchor, the existing anchor bindings automatically update the view and redraw at the
/// correct position — no event subscription, no timestamps, no explicit GUI notification.
/// </summary>
public static class WorkflowLinkRenderEx
{
    /// <summary>
    /// Whether the link is renderable: visible, and both endpoint anchors are in place
    /// (<see cref="WorkflowSlotUpdateGate.IsLinkRenderReady"/>).
    ///
    /// How to consume it (first line of each GUI's link view OnRender):
    /// <code>
    /// if (DataContext is IWorkflowLinkViewModel link &amp;&amp; !link.IsRenderReady()) return;
    /// </code>
    /// </summary>
    public static bool IsRenderReady(this IWorkflowLinkViewModel link)
    {
        if (!link.IsVisible) return false;
        return WorkflowSlotUpdateGate.IsLinkRenderReady(link);
    }
}
