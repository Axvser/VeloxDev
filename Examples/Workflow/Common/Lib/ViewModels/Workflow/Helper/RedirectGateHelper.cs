using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

/// <summary>
/// <see cref="RedirectGateNodeViewModel"/> helper: under compiled mode the first <c>FailCount</c> passes call
/// <see cref="RuntimeContext.Warn"/> to request a redirect (the engine then decides the fall-back target via the
/// node's <see cref="IRedirectable"/>); afterwards it takes the normal <see cref="HttpHelper{T}"/> path; stateless mode is ordinary.
/// </summary>
public class RedirectGateHelper : HttpHelper<NodeViewModel>
{
    public override Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (Component is RedirectGateNodeViewModel gate && ctx is IRuntimeContext rt && rt.Attempt <= gate.FailCount)
            rt.Warn($"Pass {rt.Attempt} must redirect (simulated fault)");
        return base.ReceiveAsync(ctx, ct);
    }
}
