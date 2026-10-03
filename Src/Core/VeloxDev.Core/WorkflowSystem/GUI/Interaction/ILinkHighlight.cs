namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Implemented by a link's <b>visual</b> so the interaction hub can light it on hover without knowing what
/// kind of control it is.
///
/// This is what makes the default hover feedback identical on all seven platforms: the hub decides *what* is
/// highlighted, the visual decides *how it looks*. A view that does not implement it simply never lights up —
/// hover still changes <see cref="LinkInteraction.HoveredLink"/>, which is all a host needs to drive its own
/// selection instead.
/// </summary>
/// <remarks>
/// Implement it on the same object the view passes as the <c>visual</c> when it calls
/// <see cref="LinkHitTestEx.PublishCurve"/>; the hub looks it up through that reference.
/// </remarks>
/// <seealso cref="LinkInteraction"/>
public interface ILinkHighlight
{
    /// <summary>
    /// Whether this link is the one the pointer is on. The hub sets it on the way in and off on the way out;
    /// an implementation should repaint itself when it changes, and do nothing else.
    /// </summary>
    bool IsHighlighted { get; set; }
}
