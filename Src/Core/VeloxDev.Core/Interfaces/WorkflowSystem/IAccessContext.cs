using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The dedicated contract for the dataflow access context — the parameter of
/// <see cref="IWorkflowNodeViewModelHelper.AccessAsync"/>, describing one dataflow access from "sender → receiver"
/// (a directed data edge and its phase). The payload <see cref="IContext.Data"/> is inherited from the root
/// contract (always null at compile-time). Both the compile phase
/// (<see cref="VeloxDev.Core.WorkflowSystem.CompilerEx.ICompileContext"/>) and the runtime phase
/// (<see cref="VeloxDev.Core.WorkflowSystem.CompilerEx.IRuntimeContext"/>/<see cref="ITaskContext"/>) implement it:
/// <see cref="IsCompilePhase"/> distinguishes the phase, and <see cref="Sender"/>/<see cref="Receiver"/> carry the
/// two end slots (at compile-time the compiler fills in the edges to validate).
/// </summary>
[AgentContext(AgentLanguages.Chinese, "数据流访问上下文：一次「发送槽→接收槽」的数据流访问请求。AccessAsync 的专用入参。编译期（IsCompilePhase=true、Data=null）做无数据静态检测；运行期（IsCompilePhase=false、Data=负载）做实时检测")]
[AgentContext(AgentLanguages.English, "Dataflow access context: a dataflow access request from Sender to Receiver. The dedicated parameter contract of AccessAsync. Compile phase (IsCompilePhase=true, Data=null) performs static detection without data; runtime phase (IsCompilePhase=false, Data=payload) performs real-time detection")]
public interface IAccessContext : IContext
{
    /// <summary>true = compile-time static check (no data); false = runtime real-time check (carries Data).</summary>
    [AgentContext(AgentLanguages.Chinese, "校验阶段：true 编译期（无数据），false 运行期（有数据）")]
    [AgentContext(AgentLanguages.English, "Validation phase: true = compile time (no data), false = runtime (with data)")]
    bool IsCompilePhase { get; }

    /// <summary>Sender output slot (upstream), nullable.</summary>
    [AgentContext(AgentLanguages.Chinese, "发送端输出槽（上游），可为空")]
    [AgentContext(AgentLanguages.English, "Sender output slot (upstream), nullable")]
    IWorkflowSlotViewModel? Sender { get; }

    /// <summary>Receiver input slot (downstream), nullable.</summary>
    [AgentContext(AgentLanguages.Chinese, "接收端输入槽（下游），可为空")]
    [AgentContext(AgentLanguages.English, "Receiver input slot (downstream), nullable")]
    IWorkflowSlotViewModel? Receiver { get; }
}
