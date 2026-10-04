using System;
using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Optional capability of a link's Helper: the link can say whether a point is on the line it drew, and
/// which visual drew it.
///
/// A link does not need this to be shown, selected in a list, or deleted programmatically — it is only what a
/// surface consults when the pointer is over the canvas. A surface that does not implement the capability
/// simply falls back to whatever the GUI framework gives it.
///
/// The Helper is the implementer (probe it with <c>link.GetHelper() is ILinkHitTestable</c>), because the two
/// halves have different owners: the *view* knows the shape it painted and publishes it through
/// <see cref="SetCurve"/>, while the *surface* asks <see cref="Contains"/>. Keeping the curve here rather than
/// in the view model is deliberate — the shape is a view/template strategy, and Core must not assume one.
/// </summary>
/// <remarks>
/// The capability is not a member of <see cref="IWorkflowLinkViewModelHelper"/>: adding members to that
/// interface would break every existing implementer, and most links never need it.
/// </remarks>
/// <seealso cref="LinkHitTestEx"/>
/// <seealso cref="LinkCurve"/>
[AgentContext(AgentLanguages.Chinese, "连线的可选命中能力：视图提交自己画出的曲线与那个控件，界面按它判命中")]
[AgentContext(AgentLanguages.English, "Optional link hit-test capability: the view publishes the curve it drew and the control that drew it; the surface queries it")]
public interface ILinkHitTestable
{
    /// <summary>
    /// The visual that drew the current curve — the per-link control on the platforms that have one, or
    /// <see langword="null"/> where one surface draws every link. A surface focuses it while the pointer is on the
    /// link, so the Delete key has a route to bubble; an immediate-mode layer reads it to skip what is already
    /// painted.
    /// </summary>
    object? Visual { get; }

    /// <summary>
    /// The curve the view last published — the very object hit-testing measures against.
    /// <see langword="null"/> when nothing is drawn where this link is, which is the same answer
    /// <see cref="SetCurve"/> retracts with.
    /// </summary>
    /// <remarks>
    /// Read-only on purpose: the view owns the shape and replaces it whole. A host that wants to draw something of
    /// its own along the link — a highlight, a badge, a label — draws from this instead of re-deriving the
    /// geometry, so the two can never disagree about where the line is.
    /// </remarks>
    LinkCurve? Curve { get; }

    /// <summary>
    /// Publishes the curve the view just drew, and the visual that drew it, replacing both previous values.
    /// Pass <see langword="null"/> for the curve when the link currently draws nothing, so a stale curve
    /// cannot answer for it — the visual is cleared with it.
    /// </summary>
    /// <param name="curve">The flattened curve, in the space the surface will query in.</param>
    /// <param name="visual">The control that drew it, or <see langword="null"/> on the immediate-mode platforms.</param>
    /// <remarks>
    /// Called on every geometry change, so it must stay cheap — storing the references is enough, and an
    /// implementation should not rebuild or copy anything on the publishing path.
    /// </remarks>
    void SetCurve(LinkCurve? curve, object? visual = null);

    /// <summary>
    /// Whether the point at <paramref name="x"/>/<paramref name="y"/> lies on the published curve, within
    /// <paramref name="radius"/>. Coordinates must use the same space the curve was published in.
    /// </summary>
    /// <returns><see langword="false"/> when no curve is published.</returns>
    bool Contains(double x, double y, double radius);
}
