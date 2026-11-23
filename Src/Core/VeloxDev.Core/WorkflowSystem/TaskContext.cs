
namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Lightweight read-only carrier of <see cref="ITaskContext"/>: the dataflow context constructed for broadcast
/// delivery, manual Run, or AI-driven node activation. All members are nullable.
/// </summary>
public readonly struct TaskContext : ITaskContext
{
    /// <summary>Runtime dataflow carrier, always false (only the compile phase is true).</summary>
    public bool IsCompilePhase => false;

    /// <summary>Payload / input data, nullable.</summary>
    public object? Data { get; }

    /// <summary>Upstream output slot (sender), nullable.</summary>
    public ISlotComponent? Sender { get; }

    /// <summary>This node's input slot (receiver), nullable.</summary>
    public ISlotComponent? Receiver { get; }

    /// <summary>Creates a task context from its parts.</summary>
    public TaskContext(object? data = null,
        ISlotComponent? sender = null,
        ISlotComponent? receiver = null)
    {
        Data = data;
        Sender = sender;
        Receiver = receiver;
    }

    /// <summary>Deconstructs the context into its data, sender and receiver parts.</summary>
    public void Deconstruct(out object? data,
        out ISlotComponent? sender,
        out ISlotComponent? receiver)
    {
        data = Data;
        sender = Sender;
        receiver = Receiver;
    }
}
