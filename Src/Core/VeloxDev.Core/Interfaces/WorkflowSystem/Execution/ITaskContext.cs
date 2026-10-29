using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The dataflow task context: the only payload shape a node receives via
/// <see cref="IWorkflowNodeViewModel.ReceiveCommand"/>. Access members (Data/Sender/Receiver/IsCompilePhase) all
/// inherit from <see cref="IAccessContext"/> — broadcast along a concrete link carries
/// <see cref="IAccessContext.Sender"/>/<see cref="IAccessContext.Receiver"/>; the compiler driving in order or a
/// manual Run carries only <see cref="IContext.Data"/>.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "数据流任务上下文，节点 ReceiveCommand → Helper.ReceiveAsync 的入参契约，含可空的 data/sender/receiver")]
[AgentContext(AgentLanguages.English, "Dataflow task context; the input contract of ReceiveCommand → Helper.ReceiveAsync, carrying nullable data/sender/receiver")]
public interface ITaskContext : IAccessContext
{
    // Data / Sender / Receiver / IsCompilePhase are all inherited from IAccessContext.
}
