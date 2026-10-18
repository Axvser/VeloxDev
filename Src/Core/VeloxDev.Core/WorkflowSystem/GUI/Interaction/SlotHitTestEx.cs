namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Finds the slot under a pointer position by its measured anchor, without involving the platform's visual tree.
/// </summary>
/// <remarks>
/// <para>
/// Every adapter can use this while a connection drag is in progress: the rubber band is drawn over the whole
/// surface, so the platform view under the pointer is often the surface itself and cannot answer "which slot".
/// The anchors are the same collapsed-frame values the slot layout writes and the link views read.
/// </para>
/// <para>
/// A slot whose anchor is still <see cref="double.NaN"/> (not measured yet) is skipped. When several slots overlap,
/// the nearest anchor wins.
/// </para>
/// </remarks>
public static class SlotHitTestEx
{
    /// <summary>The radius, in surface units, within which a pointer counts as over a slot.</summary>
    public const double DefaultHitRadius = 12d;

    /// <summary>Finds the nearest measured slot within <paramref name="radius"/> of the pointer.</summary>
    /// <param name="tree">The tree whose slots are tested.</param>
    /// <param name="x">Pointer x, in the same collapsed frame as <see cref="IWorkflowSlotViewModel.Anchor"/>.</param>
    /// <param name="y">Pointer y, in the same collapsed frame as <see cref="IWorkflowSlotViewModel.Anchor"/>.</param>
    /// <param name="radius">Hit radius; <see cref="DefaultHitRadius"/> when omitted.</param>
    /// <returns>The nearest slot, or <see langword="null"/> when none is within the radius.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static IWorkflowSlotViewModel? HitTest(
        IWorkflowTreeViewModel tree, double x, double y, double radius = DefaultHitRadius)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        var radiusSquared = radius * radius;
        var bestDistance = radiusSquared;
        IWorkflowSlotViewModel? best = null;

        foreach (var node in tree.Nodes)
        {
            foreach (var slot in node.Slots)
            {
                var anchor = slot.Anchor;
                if (double.IsNaN(anchor.Horizontal) || double.IsNaN(anchor.Vertical)) continue;

                var dx = anchor.Horizontal - x;
                var dy = anchor.Vertical - y;
                var distance = dx * dx + dy * dy;
                if (distance > bestDistance) continue;

                bestDistance = distance;
                best = slot;
            }
        }

        return best;
    }
}
