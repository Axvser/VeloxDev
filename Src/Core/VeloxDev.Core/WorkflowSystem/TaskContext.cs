using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// <see cref="ITaskContext"/> 的轻量只读载体：广播投递、手动 Run、AI 主动唤起节点时
/// 构造的数据流上下文。成员均可空。
/// </summary>
[AgentContext(AgentLanguages.Chinese, "ITaskContext 的只读结构体载体，可空 data/sender/receiver")]
[AgentContext(AgentLanguages.English, "Readonly struct carrier of ITaskContext with nullable data/sender/receiver")]
public readonly struct TaskContext : ITaskContext
{
    /// <summary>负载/输入数据，可为空。</summary>
    [AgentContext(AgentLanguages.Chinese, "负载数据（可空）")]
    [AgentContext(AgentLanguages.English, "Payload data (nullable)")]
    public object? Data { get; }

    /// <summary>上游输出槽（发送端），可为空。</summary>
    [AgentContext(AgentLanguages.Chinese, "上游输出槽（发送端），可为空")]
    [AgentContext(AgentLanguages.English, "Upstream output slot (sender), nullable")]
    public IWorkflowSlotViewModel? Sender { get; }

    /// <summary>本节点输入槽（接收端），可为空。</summary>
    [AgentContext(AgentLanguages.Chinese, "本节点输入槽（接收端），可为空")]
    [AgentContext(AgentLanguages.English, "This node's input slot (receiver), nullable")]
    public IWorkflowSlotViewModel? Receiver { get; }

    public TaskContext(object? data = null,
        IWorkflowSlotViewModel? sender = null,
        IWorkflowSlotViewModel? receiver = null)
    {
        Data = data;
        Sender = sender;
        Receiver = receiver;
    }

    public void Deconstruct(out object? data,
        out IWorkflowSlotViewModel? sender,
        out IWorkflowSlotViewModel? receiver)
    {
        data = Data;
        sender = Sender;
        receiver = Receiver;
    }
}
