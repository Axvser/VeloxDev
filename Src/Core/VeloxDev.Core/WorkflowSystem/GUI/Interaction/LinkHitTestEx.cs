namespace VeloxDev.WorkflowSystem;

/// <summary>
/// How a view publishes its curve and how a surface asks whether the pointer is on a link.
///
/// Both sides go through here so the capability probe lives in exactly one place, and so neither side has to
/// know which Helper type a given project uses. A link whose Helper does not implement
/// <see cref="ILinkHitTestable"/> answers <see langword="false"/> and is simply never hit — that is the whole
/// cost of not opting in.
/// </summary>
public static class LinkHitTestEx
{
    /// <summary>
    /// How far from the drawn line a point still counts as on the link, in canvas units — the radius every
    /// adapter's hand-rolled test used, now defined once. It is deliberately not scaled by zoom: a pointer is a
    /// physical thing, and the demos never scaled it either.
    /// </summary>
    public const double DefaultHitRadius = 6d;

    /// <summary>
    /// The link's hit-test capability, or <see langword="null"/> when its Helper does not implement one.
    /// </summary>
    public static ILinkHitTestable? HitTarget(this IWorkflowLinkViewModel link)
        => link.GetHelper() as ILinkHitTestable;

    /// <summary>
    /// Publishes the curve this link's view drew, so surfaces can hit-test it.
    ///
    /// Call it wherever the view (re)builds its geometry — publishing is a reference store, not a copy, so it
    /// is safe to call on every render. The curve's coordinates must be in the space the surface will query in.
    /// </summary>
    /// <param name="link">The link being drawn.</param>
    /// <param name="curve">The flattened curve, or <see langword="null"/> to retract it.</param>
    /// <param name="visual">
    /// The control that drew it, when the platform has one per link — what the surface focuses on hover, so the
    /// Delete key has a route to bubble, and how an immediate-mode layer knows this link is already painted. Leave
    /// it <see langword="null"/> where one surface draws every link.
    /// </param>
    /// <code>
    /// link.PublishCurve(LinkCurve.BuildCubic(StartLeft, StartTop, EndLeft, EndTop, pullMinimum), this);
    /// </code>
    public static void PublishCurve(this IWorkflowLinkViewModel link, LinkCurve? curve, object? visual = null)
        => link.HitTarget()?.SetCurve(curve, visual);

    /// <summary>
    /// Whether the point lies on this link, within <paramref name="radius"/>.
    ///
    /// Gated on visibility plus a published curve, and deliberately <b>not</b> on anchor measurement: the
    /// published curve is the evidence that something was painted there, because every link view retracts it
    /// (<see cref="PublishCurve"/>(<see langword="null"/>)) on the paths where it draws nothing. Asking whether
    /// the endpoints were measured instead would answer for the model, not for the drawing — and at least one
    /// platform derives its port positions from the model and never writes a slot anchor at all.
    /// </summary>
    public static bool HitTest(this IWorkflowLinkViewModel link, double x, double y, double radius = DefaultHitRadius)
    {
        if (!link.IsVisible) return false;
        return link.HitTarget()?.Contains(x, y, radius) ?? false;
    }

    /// <summary>
    /// The topmost link under the point among the tree's currently realized links, or <see langword="null"/>.
    ///
    /// This is the whole per-surface loop the seven demos used to write for themselves. It walks
    /// <c>VisibleItems</c> — the virtualized set, not the tree — from the back, because the last drawn link is
    /// the one on top, and skips the tree's own drag preview: the rubber band sits under the pointer by
    /// construction and must never be the thing that answers.
    ///
    /// A node card is opaque, so a link that passes underneath one is not drawn there and does not answer
    /// either — see <see cref="IsCoveredByANode"/>. A link's own port is the exception: the port is drawn on
    /// the card's edge, and stopping on it still means "this link".
    /// </summary>
    /// <param name="tree">The tree to search.</param>
    /// <param name="x">Point x, in the same space the links published their curves in.</param>
    /// <param name="y">Point y, same space.</param>
    /// <param name="radius">Reach either side of the drawn line; <see cref="DefaultHitRadius"/> by default.</param>
    public static IWorkflowLinkViewModel? HitTestVisibleLinks(
        this IWorkflowTreeViewModel tree, double x, double y, double radius = DefaultHitRadius)
    {
        var items = tree.GetHelper()?.VisibleItems;
        if (items is null) return null;

        var dragPreview = tree.VirtualLink;
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is not IWorkflowLinkViewModel link) continue;
            if (ReferenceEquals(link, dragPreview)) continue;
            if (!link.HitTest(x, y, radius)) continue;
            if (IsCoveredByANode(items, link, x, y, radius)) continue;
            return link;
        }

        return null;
    }

    /// <summary>
    /// Whether a node card is drawn over <paramref name="x"/>/<paramref name="y"/>, in which case the link
    /// underneath is not the thing the pointer is on.
    /// </summary>
    /// <remarks>
    /// Only the realized set is consulted — being in <c>VisibleItems</c> is itself the evidence that the node is
    /// on screen, so a node that is not in it cannot cover anything. A point within <paramref name="radius"/> of the link's <b>own</b> ports is never
    /// covered: ports are drawn on the card's edge, and stopping on one is still aiming at this link.
    /// </remarks>
    private static bool IsCoveredByANode(
        IList<IWorkflowViewModel> items, IWorkflowLinkViewModel link, double x, double y, double radius)
    {
        if (NearOwnPort(link, x, y, radius))
        {
            return false;
        }

        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (items[i] is not IWorkflowNodeViewModel node)
            {
                continue;
            }

            var left = node.Anchor.Horizontal;
            var top = node.Anchor.Vertical;
            if (x >= left && x <= left + node.Size.Width && y >= top && y <= top + node.Size.Height)
            {
                return true;
            }
        }

        return false;
    }

    private static bool NearOwnPort(IWorkflowLinkViewModel link, double x, double y, double radius)
        => Within(link.Sender, x, y, radius) || Within(link.Receiver, x, y, radius);

    private static bool Within(IWorkflowSlotViewModel? slot, double x, double y, double radius)
    {
        if (slot is null) return false;

        var dx = x - slot.Anchor.Horizontal;
        var dy = y - slot.Anchor.Vertical;
        return (dx * dx) + (dy * dy) <= radius * radius;
    }
}
