using System.Collections.Generic;
using VeloxDev.WorkflowSystem;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>
/// Writes each slot's canvas-local anchor from the position its glyph was actually arranged at.
/// </summary>
/// <remarks>
/// The measurement happens after the card arranged its glyphs, so no window handle or screen coordinate is
/// involved. A per-card cache skips the model write when the computed anchor has not moved, which keeps a layout
/// pass from re-raising the same slot notifications.
/// </remarks>
public static class VisualWorkflowSlotLayout
{
    private const double Epsilon = 0.01d;

    /// <summary>Synchronises the model anchors of every slot glyph on <paramref name="view"/>.</summary>
    /// <param name="view">The card whose glyphs have just been arranged.</param>
    public static void Sync(VisualWorkflowNodeView view)
    {
        if (view is null) throw new ArgumentNullException(nameof(view));
        if (view.Node is not { } node)
        {
            return;
        }

        var nodeAnchor = node.Anchor;
        if (double.IsNaN(nodeAnchor.Horizontal) || double.IsNaN(nodeAnchor.Vertical))
        {
            return;
        }

        foreach (var slotView in view.SlotViews)
        {
            if (slotView.Slot is not { } slot)
            {
                continue;
            }

            var bounds = slotView.Bounds;
            if (bounds.Width <= 0f || bounds.Height <= 0f)
            {
                continue;
            }

            var localX = bounds.X + (bounds.Width / 2d);
            var localY = bounds.Y + (bounds.Height / 2d);
            var layer = slot.Anchor?.Layer ?? 0;
            var next = WorkflowSurfaceMath.SlotAnchorFromNode(
                nodeAnchor.Horizontal, nodeAnchor.Vertical, localX, localY, layer);

            if (view.AnchorCache.TryGetValue(slot, out var previous) && Same(previous, next))
            {
                continue;
            }

            view.AnchorCache[slot] = next;
            slot.Anchor = next;
        }
    }

    private static bool Same(Wf.Anchor left, Wf.Anchor right)
        => Math.Abs(left.Horizontal - right.Horizontal) < Epsilon
           && Math.Abs(left.Vertical - right.Vertical) < Epsilon
           && left.Layer == right.Layer;
}
