using VeloxDev.AI;

namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Lightweight read-only carrier of <see cref="ITaskContext"/>: the dataflow context constructed for broadcast
/// delivery, manual Run, or AI-driven node activation. All members are nullable.
/// </summary>
[AgentContext(AgentLanguages.Chinese, "ITaskContext 的只读结构体载体，可空 data/sender/receiver")]
[AgentContext(AgentLanguages.English, "Readonly struct carrier of ITaskContext with nullable data/sender/receiver")]
public readonly struct TaskContext : ITaskContext
{
    /// <summary>Runtime dataflow carrier, always false (only the compile phase is true).</summary>
    public bool IsCompilePhase => false;

    /// <summary>Payload / input data, nullable.</summary>
    [AgentContext(AgentLanguages.Chinese, "负载数据（可空）")]
    [AgentContext(AgentLanguages.English, "Payload data (nullable)")]
    public object? Data { get; }

    /// <summary>Upstream output slot (sender), nullable.</summary>
    [AgentContext(AgentLanguages.Chinese, "上游输出槽（发送端），可为空")]
    [AgentContext(AgentLanguages.English, "Upstream output slot (sender), nullable")]
    public IWorkflowSlotViewModel? Sender { get; }

    /// <summary>This node's input slot (receiver), nullable.</summary>
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
