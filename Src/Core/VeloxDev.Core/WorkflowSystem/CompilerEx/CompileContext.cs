using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 编译期上下文：编译时为每个节点分配的编译身份。
/// 全局序号单调连续：分支下游的 Graph 起点带偏移（不归零）；-1 表示绝对停止状态（未选中分支/终止）。
/// </summary>
public sealed partial class CompileContext : ICompileContext
{
    /// <summary>编译期身份，恒为 true（静态检测阶段）。</summary>
    public bool IsCompilePhase => true;

    /// <summary>编译期无数据负载，恒为 null。</summary>
    public object? Data => null;

    /// <summary>待校验边的发送端输出槽；节点自身持有的身份实例上为 null，仅编译器构造的边实例填入。</summary>
    public IWorkflowSlotViewModel? Sender { get; set; }

    /// <summary>待校验边的接收端输入槽；节点自身持有的身份实例上为 null，仅编译器构造的边实例填入。</summary>
    public IWorkflowSlotViewModel? Receiver { get; set; }

    /// <summary>汇合点输入源节点列表（编译期登记；Count &gt; 1 时运行期聚合为 GroupData 注入 Data）。非汇合节点为 null。</summary>
    public IReadOnlyList<IWorkflowNodeViewModel>? InputNodes { get; set; }

    [VeloxProperty] private int _order = -1;         // 全局计算序号；-1 = 绝对停止状态
    [VeloxProperty] private int _chainIndex = -1;    // 在所属链路内的序号（从 0 起）
    [VeloxProperty] private int _offset = 0;         // 进入本图的起点偏移（Router 下游 > 0）
}
