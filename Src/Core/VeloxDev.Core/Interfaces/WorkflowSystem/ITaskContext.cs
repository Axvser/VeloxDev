using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// 数据流任务上下文：节点通过 <see cref="IWorkflowNodeViewModel.ReceiveCommand"/>
/// 接收的唯一载荷形态。三个成员均可空——广播沿具体连接投递时携带
/// <see cref="Sender"/>/<see cref="Receiver"/>；编译器按序驱动或手动 Run 时仅携带
/// <see cref="Data"/>。
/// </summary>
[AgentContext(AgentLanguages.Chinese, "数据流任务上下文，节点 ReceiveCommand → Helper.ReceiveAsync 的入参契约，含可空的 data/sender/receiver")]
[AgentContext(AgentLanguages.English, "Dataflow task context; the input contract of ReceiveCommand → Helper.ReceiveAsync, carrying nullable data/sender/receiver")]
public interface ITaskContext : IContext
{
    /// <summary>负载/输入数据，可为空。</summary>
    [AgentContext(AgentLanguages.Chinese, "负载数据（可空）")]
    [AgentContext(AgentLanguages.English, "Payload data (nullable)")]
    object? Data { get; }

    /// <summary>上游节点的输出槽（发起广播的连接发送端），可为空。</summary>
    [AgentContext(AgentLanguages.Chinese, "上游输出槽（发送端），可为空")]
    [AgentContext(AgentLanguages.English, "Upstream output slot (sender), nullable")]
    IWorkflowSlotViewModel? Sender { get; }

    /// <summary>本节点的输入槽（接收数据的连接接收端），可为空。</summary>
    [AgentContext(AgentLanguages.Chinese, "本节点输入槽（接收端），可为空")]
    [AgentContext(AgentLanguages.English, "This node's input slot (receiver), nullable")]
    IWorkflowSlotViewModel? Receiver { get; }
}
