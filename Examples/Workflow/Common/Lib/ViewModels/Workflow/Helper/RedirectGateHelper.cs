using VeloxDev.Core.WorkflowSystem.CompilerEx;
using VeloxDev.WorkflowSystem;

namespace Demo.ViewModels.Workflow.Helper;

/// <summary>
/// <see cref="RedirectGateNodeViewModel"/> 的 helper：编译模式下前 <c>FailCount</c> 次链内通过
/// 调用 <see cref="RuntimeContext.Warn"/> 请求重定向（引擎随后经节点自身的 <see cref="IRedirectable"/>
/// 决定回退目标）；之后放行走 <see cref="HttpHelper{T}"/> 正常路径。无状态模式等同普通节点。
/// </summary>
public class RedirectGateHelper : HttpHelper<NodeViewModel>
{
    public override Task<object?> ReceiveAsync(ITaskContext ctx, CancellationToken ct)
    {
        if (Component is RedirectGateNodeViewModel gate && ctx is IRuntimeContext rt && rt.Attempt <= gate.FailCount)
            rt.Warn($"第 {rt.Attempt} 次通过需回退（模拟故障）");
        return base.ReceiveAsync(ctx, ct);
    }
}
