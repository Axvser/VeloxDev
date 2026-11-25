namespace VeloxDev.WorkflowSystem;

/// <summary>
/// An optional extension of <see cref="ISlotPart"/> that reports the part's effective visibility.
/// </summary>
/// <remarks>
/// The pipeline uses it to tell a <b>transient</b> visible part that has not been laid out yet (do not certify
/// the pass; retry on the next layout trigger) from a <b>steady</b> hidden port (certify; otherwise every layout
/// pass would re-measure). A part that does not implement this interface is treated as visible, so existing
/// implementers keep compiling and keep their anchors being retried.
/// </remarks>
public interface ISlotPartVisibility
{
    /// <summary>Whether the element is effectively visible (itself and the node view are not collapsed).</summary>
    bool IsVisible { get; }
}
