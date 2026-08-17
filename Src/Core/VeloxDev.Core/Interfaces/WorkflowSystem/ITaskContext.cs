using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// 数据流任务上下文：节点通过 <see cref="IWorkflowNodeViewModel.ReceiveCommand"/>
/// 接收的唯一载荷形态。访问成员（Data/Sender/Receiver/IsCompilePhase）均继承自
/// <see cref="IAccessContext"/>——广播沿具体连接投递时携带 <see cref="IAccessContext.Sender"/>/
/// <see cref="IAccessContext.Receiver"/>；编译器按序驱动或手动 Run 时仅携带
/// <see cref="IContext.Data"/>。
/// </summary>
[AgentContext(AgentLanguages.Chinese, "数据流任务上下文，节点 ReceiveCommand → Helper.ReceiveAsync 的入参契约，含可空的 data/sender/receiver")]
[AgentContext(AgentLanguages.English, "Dataflow task context; the input contract of ReceiveCommand → Helper.ReceiveAsync, carrying nullable data/sender/receiver")]
public interface ITaskContext : IAccessContext
{
    // Data / Sender / Receiver / IsCompilePhase 均继承自 IAccessContext。
}
