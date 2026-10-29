using System;

namespace VeloxDev.WorkflowSystem.StandardEx;

/// <summary>
/// Locates workflow components by scrolling their surface so the component sits at the viewport center.
/// </summary>
/// <remarks>
/// <para>
/// The surface owns the scroll; these methods only turn a component into the world point to center on. The
/// request travels through <see cref="WorkflowInput.Navigator"/>, which an adapter registers while its surface
/// is attached. A tree with no attached navigator returns <see langword="false"/> and changes nothing — the same
/// optional-capability contract as <see cref="WorkflowInput.Scroller"/>.
/// </para>
/// <para>
/// Every target is model data, so nothing has to be measured or drawn first: a node centers on its rectangle,
/// a slot on its parent node's rectangle, and a link on the midpoint between its two endpoint nodes' rectangle
/// centers. <c>Locate</c> therefore works for elements that are off-screen and have no view yet — it never reads
/// <see cref="IWorkflowSlotViewModel.Anchor"/> or a published link curve, which exist only after layout/drawing.
/// Panning only: the current zoom is preserved.
/// </para>
/// </remarks>
/// <seealso cref="IWorkflowSurfaceNavigator"/>
public static class WorkflowNavigationEx
{
    /// <summary>Centers <paramref name="node"/> in the viewport of the surface it is drawn on.</summary>
    /// <param name="node">The node to locate.</param>
    /// <returns><see langword="true"/> when a surface navigator handled the request; otherwise
    /// <see langword="false"/> (no surface, or the node has no finite position).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public static bool Locate(this IWorkflowNodeViewModel node)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));

        var navigator = NavigatorOf(node.Parent);
        if (navigator is null || !TryGetNodeCenter(node, out var x, out var y)) return false;

        navigator.CenterOn(x, y);
        return true;
    }

    /// <summary>Centers <paramref name="slot"/> in the viewport of the surface it is drawn on.</summary>
    /// <param name="slot">The slot to locate.</param>
    /// <returns><see langword="true"/> when a surface navigator handled the request; otherwise
    /// <see langword="false"/> (no surface, or the parent node has no finite position).</returns>
    /// <remarks>
    /// The target is the parent node's rectangle center, not the slot's own anchor: a slot's anchor exists only
    /// after its view has been measured, and a slot inside an off-screen node has no view at all. The node is
    /// model data, so the request works before anything is drawn.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="slot"/> is <see langword="null"/>.</exception>
    public static bool Locate(this IWorkflowSlotViewModel slot)
    {
        if (slot is null) throw new ArgumentNullException(nameof(slot));

        var navigator = NavigatorOf(slot.Parent?.Parent);
        if (navigator is null) return false;

        if (slot.Parent is not { } node || !TryGetNodeCenter(node, out var x, out var y)) return false;

        navigator.CenterOn(x, y);
        return true;
    }

    /// <summary>Centers <paramref name="link"/> in the viewport of the surface it is drawn on.</summary>
    /// <param name="link">The link to locate.</param>
    /// <returns><see langword="true"/> when a surface navigator handled the request; otherwise
    /// <see langword="false"/> (no surface, or either endpoint node has no finite position).</returns>
    /// <remarks>
    /// The target is the midpoint between the two endpoint nodes' rectangle centers. It deliberately does not
    /// read the published curve (which exists only after the link has been drawn) or the slots' measured anchors;
    /// both endpoint nodes are model data, so an off-screen link can be located before its view exists.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="link"/> is <see langword="null"/>.</exception>
    public static bool Locate(this IWorkflowLinkViewModel link)
    {
        if (link is null) throw new ArgumentNullException(nameof(link));

        var navigator = NavigatorOf(link.Sender?.Parent?.Parent ?? link.Receiver?.Parent?.Parent);
        if (navigator is null) return false;

        if (link.Sender?.Parent is not { } senderNode || link.Receiver?.Parent is not { } receiverNode)
            return false;

        if (!TryGetNodeCenter(senderNode, out var senderX, out var senderY) ||
            !TryGetNodeCenter(receiverNode, out var receiverX, out var receiverY))
            return false;

        navigator.CenterOn((senderX + receiverX) / 2d, (senderY + receiverY) / 2d);
        return true;
    }

    private static bool TryGetNodeCenter(IWorkflowNodeViewModel node, out double x, out double y)
    {
        var anchor = node.Anchor;
        var size = node.Size;
        x = anchor.Horizontal + size.Width / 2d;
        y = anchor.Vertical + size.Height / 2d;
        return IsFinite(x) && IsFinite(y);
    }

    private static IWorkflowSurfaceNavigator? NavigatorOf(IWorkflowTreeViewModel? tree)
        => tree is null ? null : WorkflowInput.For(tree).Navigator;

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
