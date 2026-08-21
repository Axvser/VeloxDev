using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 编译期身份上下文接口。由 <see cref="CompileContext"/> 实现，经
/// <see cref="ICompileTimeAware.AttachCompileTimeContext"/> 注入节点。
/// </summary>
[AgentContext(AgentLanguages.Chinese, "编译期身份上下文：固定编号 Order / 链内索引 ChainIndex / 子图偏移 Offset")]
[AgentContext(AgentLanguages.English, "Compile-time identity context: fixed Order / in-chain index ChainIndex / sub-graph Offset")]
public interface ICompileContext : IAccessContext
{
    /// <summary>编译期固定执行编号（-1 = 绝对停止）。</summary>
    int Order { get; set; }

    /// <summary>线性段内索引。</summary>
    int ChainIndex { get; set; }

    /// <summary>子图入口偏移。</summary>
    int Offset { get; set; }

    /// <summary>
    /// 汇合点输入源节点列表（编译期从各分支出口登记）。Count &gt; 1 时运行期把各上游产物聚合成
    /// <see cref="GroupData"/> 注入 <see cref="IRuntimeContext.Data"/>；链式/单输入节点为 null，
    /// 保持裸 Data 链式语义。
    /// </summary>
    [AgentContext(AgentLanguages.Chinese, "汇合点输入源节点列表：Count 大于 1 时运行期把各上游产物聚合为只读字典 GroupData 注入 Data；链式/单输入节点为 null")]
    [AgentContext(AgentLanguages.English, "Join-point input source nodes: when Count > 1 the runtime aggregates each upstream output into a read-only GroupData dictionary injected as Data; null on chained/single-input nodes")]
    IReadOnlyList<IWorkflowNodeViewModel>? InputNodes { get; set; }
}
