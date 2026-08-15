using VeloxDev.AI;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// 编译期身份上下文接口。由 <see cref="CompileContext"/> 实现，经
/// <see cref="ICompileTimeAware.AttachCompileTimeContext"/> 注入节点。
/// </summary>
[AgentContext(AgentLanguages.Chinese, "编译期身份上下文：固定编号 Order / 链内索引 ChainIndex / 子图偏移 Offset")]
[AgentContext(AgentLanguages.English, "Compile-time identity context: fixed Order / in-chain index ChainIndex / sub-graph Offset")]
public interface ICompileContext : IContext
{
    /// <summary>编译期固定执行编号（-1 = 绝对停止）。</summary>
    int Order { get; set; }

    /// <summary>线性段内索引。</summary>
    int ChainIndex { get; set; }

    /// <summary>子图入口偏移。</summary>
    int Offset { get; set; }
}
