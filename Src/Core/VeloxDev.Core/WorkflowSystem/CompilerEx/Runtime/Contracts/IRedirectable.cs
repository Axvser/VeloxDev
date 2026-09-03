namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// A redirectable node: while executing, decides whether to fall back to an earlier compile state in its chain based
/// on <see cref="IRuntimeContext"/>. <see cref="RuntimeEngine"/> calls its <see cref="ResolveRedirectAsync"/> after
/// driving a node in the chain; a non-null <see cref="CompileContext.Order"/> means falling back to that state and
/// re-executing (in-chain redirect, v1). The compiled graph itself is acyclic; redirect is purely a runtime contract.
/// </summary>
public interface IRedirectable
{
    /// <summary>
    /// Compile-time contract method: decides whether to redirect based on the runtime context. Returning a non-null
    /// Order means falling back to the in-chain node whose CompileContext.Order equals that value; null means continue.
    /// </summary>
    Task<int?> ResolveRedirectAsync(IRuntimeContext context, CancellationToken ct);
}
