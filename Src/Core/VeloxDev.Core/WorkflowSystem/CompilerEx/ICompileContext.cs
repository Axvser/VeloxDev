using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// The compile-time identity context interface. Implemented by <see cref="CompileContext"/> and injected into
/// nodes via <see cref="ICompileTimeAware.AttachCompileTimeContext"/>.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "编译期身份上下文：固定编号 Order / 链内索引 ChainIndex / 子图偏移 Offset")]
[AgentContext(AgentLanguages.English, "Compile-time identity context: fixed Order / in-chain index ChainIndex / sub-graph Offset")]
public interface ICompileContext : IAccessContext
{
    /// <summary>Compile-time fixed execution order (-1 = absolute stop).</summary>
    int Order { get; set; }

    /// <summary>Index within the linear segment.</summary>
    int ChainIndex { get; set; }

    /// <summary>Sub-graph entry offset.</summary>
    int Offset { get; set; }

    /// <summary>
    /// Join-point input source nodes (registered from each branch's exit at compile-time). When Count &gt; 1,
    /// the runtime aggregates each upstream output into a <see cref="GroupData"/> injected as
    /// <see cref="IRuntimeContext.Data"/>; null on chained/single-input nodes, preserving bare Data chaining
    /// semantics.
    /// </summary>
    [AgentContext(AgentLanguages.Chinese, "汇合点输入源节点列表：Count 大于 1 时运行期把各上游产物聚合为只读字典 GroupData 注入 Data；链式/单输入节点为 null")]
    [AgentContext(AgentLanguages.English, "Join-point input source nodes: when Count > 1 the runtime aggregates each upstream output into a read-only GroupData dictionary injected as Data; null on chained/single-input nodes")]
    IReadOnlyList<IWorkflowNodeViewModel>? InputNodes { get; set; }
}
