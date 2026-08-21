namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Core check for whether a Link is render-ready on the virtualized canvas.
///
/// A Slot anchor's default value is a "no value" placeholder — <see cref="Anchor"/> defaults its
/// horizontal/vertical coordinates to <see cref="double.NaN"/> (meaning: unmeasured / awaiting GUI positioning),
/// and no real GUI measurement can produce exactly NaN. Before rendering a link, both endpoint anchors are checked:
/// if either endpoint is still NaN, rendering is blocked, avoiding drawing a misaligned frame that then jumps back
/// (visual flicker).
///
/// Once released, redrawing is automatic: after the GUI measurement writes real coordinates into slot.Anchor,
/// the existing StartLeft/EndLeft anchor bindings update → the view InvalidateVisual → re-runs OnRender → draws at
/// the correct position. No event subscription, timestamp queue, or explicit GUI notification is needed;
/// non-virtualized trees render normally (the NaN check works for any GUI).
/// </summary>
public static class WorkflowSlotUpdateGate
{
    /// <summary>Whether the link is renderable: both endpoint anchors are in place.</summary>
    public static bool IsLinkRenderReady(IWorkflowLinkViewModel link)
        => EndpointReady(link.Sender) && EndpointReady(link.Receiver);

    /// <summary>
    /// Endpoint readiness check: an endpoint not mounted to a node (Parent is null, e.g. a drag preview /
    /// virtual-link placeholder endpoint) has no layout to wait for → ready; otherwise the anchor's
    /// horizontal/vertical coordinates are both non-NaN (real values written by GUI measurement) → ready.
    /// </summary>
    private static bool EndpointReady(IWorkflowSlotViewModel? slot)
    {
        if (slot is null || slot.Parent is null) return true;
        var anchor = slot.Anchor;
        return !double.IsNaN(anchor.Horizontal) && !double.IsNaN(anchor.Vertical);
    }
}
