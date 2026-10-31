using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// A redirectable node: while executing, decides whether to fall back to an upstream node and re-execute from it,
/// based on <see cref="IRuntimeContext"/>. <see cref="RuntimeEngine"/> calls its
/// <see cref="ResolveRedirectAsync"/> when a drive reports an <see cref="IRuntimeContext.Error"/> or throws — it is
/// the one configured answer to a failure, and without it that failure ends the flow.
/// <para>
/// A returned node must be an ancestor of the requesting node (see
/// <c>WorkflowNodeEx.SearchReverseNodes</c>) and must belong to the compiled graph; the engine maps it to that
/// node's <see cref="ICompileContext.Order"/>, so implementers never deal in compile orders. The pass that returns
/// a target ends at this node — the engine does not drive the rest of the chain or the remaining entries — and the
/// next pass re-runs the graph from the target. A target that is not an upstream compiled node is ignored with one
/// log line, and the flow carries on.
/// </para>
/// <para>
/// The compiled graph itself is acyclic; redirect is purely a runtime contract.
/// </para>
/// </summary>
public interface IRedirectable
{
    /// <summary>
    /// Decides where the run should go instead. Returning an upstream node falls back to it; returning
    /// <see langword="null"/> declines the redirect and the flow carries on.
    /// </summary>
    /// <param name="context">The session driving the node.</param>
    /// <param name="ct">The run's cancellation token.</param>
    /// <returns>An upstream node of this one, or <see langword="null"/>.</returns>
    Task<IWorkflowNodeViewModel?> ResolveRedirectAsync(IRuntimeContext context, CancellationToken ct);
}
