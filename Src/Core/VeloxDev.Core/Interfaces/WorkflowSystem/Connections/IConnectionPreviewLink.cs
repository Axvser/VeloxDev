namespace VeloxDev.WorkflowSystem;

/// <summary>The stage of an in-progress connection drag.</summary>
public enum ConnectionPreviewState
{
    /// <summary>No drag is in progress.</summary>
    None = 0,

    /// <summary>The sender is fixed; the pointer is not over a candidate slot.</summary>
    Started = 1,

    /// <summary>The pointer is over a slot that could receive this connection.</summary>
    ValidTarget = 2,

    /// <summary>The pointer is over a slot, but the connection would be refused.</summary>
    InvalidTarget = 3,
}

/// <summary>
/// Optional capability of the virtual link: it exposes the live stage of the connection drag so a link view can
/// render the rubber band differently before, over and outside a candidate slot.
/// </summary>
/// <remarks>
/// <para>
/// The state is maintained by <see cref="WorkflowInput"/> while the tree's <c>VirtualLink</c> is visible. A host can
/// subscribe to <see cref="WorkflowInput.PreviewChanged"/> instead of polling this property; both describe the same
/// snapshot.
/// </para>
/// <para>
/// The capability is kept off <see cref="IWorkflowLinkViewModel"/> for the same reason as the other optional event
/// interfaces: adding a member there would break every implementer. <see cref="LinkDefaultViewModel"/> implements it;
/// a custom virtual link may implement it too.
/// </para>
/// </remarks>
public interface IConnectionPreviewLink : IWorkflowLinkViewModel
{
    /// <summary>The current stage of the connection drag.</summary>
    ConnectionPreviewState Preview { get; }

    /// <summary>Sets the stage. Called by <see cref="WorkflowInput"/>; a view model does not call it itself.</summary>
    /// <param name="state">The new stage.</param>
    void SetPreview(ConnectionPreviewState state);
}
