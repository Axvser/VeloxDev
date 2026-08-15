using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 环路/重试：环路体子图 + 重试配置。失败重跑 <see cref="Body"/> 直到超过 <see cref="MaxRetries"/>。
/// </summary>
public sealed partial class RetryEntry : ActionEntry
{
    [VeloxProperty] private IWorkflowNodeViewModel? _entryNode;   // 环路重入点
    [VeloxProperty] private CompiledGraph? _body;                 // 环路体（可含分支）
    [VeloxProperty] private int _maxRetries;
}
