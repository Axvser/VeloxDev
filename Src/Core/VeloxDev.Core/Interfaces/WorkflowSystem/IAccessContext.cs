using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// 数据流访问上下文的专用契约 —— <see cref="IWorkflowNodeViewModelHelper.AccessAsync"/> 的入参，
/// 描述一次「发送端 → 接收端」的数据流访问（一条有向数据边及其所处阶段）。
/// 负载 <see cref="IContext.Data"/> 继承自根契约（编译期恒为 null）。
/// 编译期（<see cref="VeloxDev.Core.WorkflowSystem.CompilerEx.ICompileContext"/>）与运行期
/// （<see cref="VeloxDev.Core.WorkflowSystem.CompilerEx.IRuntimeContext"/>/<see cref="ITaskContext"/>）
/// 两个阶段都实现它：<see cref="IsCompilePhase"/> 区分阶段，
/// <see cref="Sender"/>/<see cref="Receiver"/> 携带两端槽（编译期由编译器填入待校验的边）。
/// </summary>
[AgentContext(AgentLanguages.Chinese, "数据流访问上下文：一次「发送槽→接收槽」的数据流访问请求。AccessAsync 的专用入参。编译期（IsCompilePhase=true、Data=null）做无数据静态检测；运行期（IsCompilePhase=false、Data=负载）做实时检测")]
[AgentContext(AgentLanguages.English, "Dataflow access context: a dataflow access request from Sender to Receiver. The dedicated parameter contract of AccessAsync. Compile phase (IsCompilePhase=true, Data=null) performs static detection without data; runtime phase (IsCompilePhase=false, Data=payload) performs real-time detection")]
public interface IAccessContext : IContext
{
    /// <summary>true = 编译期静态检测（无数据）；false = 运行期实时检测（携带 Data）。</summary>
    [AgentContext(AgentLanguages.Chinese, "校验阶段：true 编译期（无数据），false 运行期（有数据）")]
    [AgentContext(AgentLanguages.English, "Validation phase: true = compile time (no data), false = runtime (with data)")]
    bool IsCompilePhase { get; }

    /// <summary>发送端输出槽（上游），可为空。</summary>
    [AgentContext(AgentLanguages.Chinese, "发送端输出槽（上游），可为空")]
    [AgentContext(AgentLanguages.English, "Sender output slot (upstream), nullable")]
    IWorkflowSlotViewModel? Sender { get; }

    /// <summary>接收端输入槽（下游），可为空。</summary>
    [AgentContext(AgentLanguages.Chinese, "接收端输入槽（下游），可为空")]
    [AgentContext(AgentLanguages.English, "Receiver input slot (downstream), nullable")]
    IWorkflowSlotViewModel? Receiver { get; }
}
