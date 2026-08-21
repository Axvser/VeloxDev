using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AI;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

/// <summary>
/// Redirectable node (in-chain fallback): under compiled mode the first <see cref="FailCount"/> passes have
/// <see cref="RedirectGateHelper"/> call <see cref="RuntimeContext.Warn"/> to request a redirect; this node
/// implements <see cref="IRedirectable"/> and picks the fall-back target = the compile state
/// (<see cref="CompileContext.Order"/>) <see cref="RedirectBackSteps"/> steps back. Afterwards it passes through.
/// Inherits <see cref="NodeViewModel"/>; shells use the generic node view.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "可重定向节点：前 FailCount 次通过时 Warn 请求重定向，回退到前 RedirectBackSteps 步的编译状态，之后放行")]
[AgentContext(AgentLanguages.English, "Redirectable node: Warns to request a redirect for the first FailCount passes, falls back RedirectBackSteps states, then continues")]
[WorkflowBuilder.Node<RedirectGateHelper>(workSemaphore: 1)]
public partial class RedirectGateNodeViewModel : NodeViewModel, IRedirectable
{
    /// <summary>The first few chain passes count as needing a fallback (simulated transient fault); afterwards passes through.</summary>
    [AgentContext(AgentLanguages.Chinese, "前几次链内通过视为需回退（模拟瞬时故障）")]
    [AgentContext(AgentLanguages.English, "First N chain passes fall back (simulated transient fault)")]
    [VeloxProperty] private int failCount = 2;

    /// <summary>Number of steps to fall back (1 = previous node, 2 = two nodes back, …).</summary>
    [AgentContext(AgentLanguages.Chinese, "回退步数：1 = 回退到前一节点，2 = 前二节点…")]
    [AgentContext(AgentLanguages.English, "Fall-back steps: 1 = previous node, 2 = two nodes back, …")]
    [VeloxProperty] private int redirectBackSteps = 1;

    /// <summary>Falls back to the compile state (Order) `RedirectBackSteps` steps earlier. The engine calls this only after the node has called Warn/Error.</summary>
    public Task<int?> ResolveRedirectAsync(IRuntimeContext context, CancellationToken ct)
    {
        if (CompileContext is { } cc)
            return Task.FromResult<int?>(cc.Order - RedirectBackSteps);
        return Task.FromResult<int?>(null);
    }
}
