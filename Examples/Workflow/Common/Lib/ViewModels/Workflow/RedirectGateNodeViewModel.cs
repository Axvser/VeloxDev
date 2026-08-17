using Demo.ViewModels.Workflow.Helper;
using VeloxDev.AI;
using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels;

/// <summary>
/// 可重定向节点（链内回退）：编译模式下前 <see cref="FailCount"/> 次链内通过由
/// <see cref="RedirectGateHelper"/> 调用 <see cref="RuntimeContext.Warn"/> 请求重定向，
/// 本节点实现 <see cref="IRedirectable"/> 决定回退目标 = <see cref="RedirectBackSteps"/> 步前的
/// 编译状态（<see cref="CompileContext.Order"/>）。之后放行。
/// 继承 <see cref="NodeViewModel"/>，各 shell 走泛型节点视图。
/// </summary>
[AgentContext(AgentLanguages.Chinese, "可重定向节点：前 FailCount 次通过时 Warn 请求重定向，回退到前 RedirectBackSteps 步的编译状态，之后放行")]
[AgentContext(AgentLanguages.English, "Redirectable node: Warns to request a redirect for the first FailCount passes, falls back RedirectBackSteps states, then continues")]
[WorkflowBuilder.Node<RedirectGateHelper>(workSemaphore: 1)]
public partial class RedirectGateNodeViewModel : NodeViewModel, IRedirectable
{
    /// <summary>前几次链内通过视为需回退（模拟瞬时故障），之后放行。</summary>
    [AgentContext(AgentLanguages.Chinese, "前几次链内通过视为需回退（模拟瞬时故障）")]
    [AgentContext(AgentLanguages.English, "First N chain passes fall back (simulated transient fault)")]
    [VeloxProperty] private int failCount = 2;

    /// <summary>回退的步数（1 = 回退到前一节点，2 = 前二节点…）。</summary>
    [AgentContext(AgentLanguages.Chinese, "回退步数：1 = 回退到前一节点，2 = 前二节点…")]
    [AgentContext(AgentLanguages.English, "Fall-back steps: 1 = previous node, 2 = two nodes back, …")]
    [VeloxProperty] private int redirectBackSteps = 1;

    /// <summary>回退到 `RedirectBackSteps` 步前的编译状态（Order）。仅当节点已调用 Warn/Error 时由引擎调用。</summary>
    public Task<int?> ResolveRedirectAsync(IRuntimeContext context, CancellationToken ct)
    {
        if (CompileContext is { } cc)
            return Task.FromResult<int?>(cc.Order - RedirectBackSteps);
        return Task.FromResult<int?>(null);
    }
}
