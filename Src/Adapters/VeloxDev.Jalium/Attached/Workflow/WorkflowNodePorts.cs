using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>One port of a node card: the slot, and the label the card prints beside it.</summary>
/// <param name="Slot">The slot.</param>
/// <param name="Name">The label, or an empty string when the port has none.</param>
public readonly record struct WorkflowNodePort(IWorkflowSlotViewModel Slot, string Name);

/// <summary>
/// What the view tells the adapter about one node's ports.
/// </summary>
/// <remarks>
/// <para>
/// Neither the property that holds a node's slots nor its title is on <see cref="IWorkflowNodeViewModel"/>, and the
/// adapter holds the node as that interface — so the one thing it cannot know is the thing the view knows for free:
/// <c>vm =&gt; [((MyNode)vm).InputSlot]</c> is a cast the view writes because the view was generated for that node
/// type. Jalium asks for it here rather than reading it out of the node by name, which would need reflection.
/// </para>
/// <para>
/// This is the same division the markup adapters use: there the view writes
/// <c>SlotNames="PART_InputSlot"</c> and the adapter reads the binding off the named control; here the view writes
/// the accessor and the adapter calls it.
/// </para>
/// </remarks>
public sealed class WorkflowNodePortSet
{
    /// <summary>Reads the node's input ports, in the order the card should stack them.</summary>
    public Func<IWorkflowNodeViewModel, IReadOnlyList<WorkflowNodePort>> Inputs { get; init; } = static _ => [];

    /// <summary>Reads the node's output ports, in the order the card should stack them.</summary>
    public Func<IWorkflowNodeViewModel, IReadOnlyList<WorkflowNodePort>> Outputs { get; init; } = static _ => [];

    /// <summary>Reads the node's display title.</summary>
    public Func<IWorkflowNodeViewModel, string> Title { get; init; } = static _ => string.Empty;
}

/// <summary>Where a declared <see cref="WorkflowNodePortSet"/> is kept, one per node instance.</summary>
/// <remarks>
/// Per instance rather than per type: the declarations are cheap, the table is weak (so a discarded tree takes its
/// declarations with it), and nothing has to be unregistered when a node's port set changes shape at runtime —
/// which it does, since a <c>SlotEnumerator</c> node rebuilds its outputs.
/// </remarks>
public static class WorkflowNodePorts
{
    private static readonly ConditionalWeakTable<IWorkflowNodeViewModel, WorkflowNodePortSet> Table = new();

    /// <summary>Declares (or replaces) the ports of <paramref name="node"/>.</summary>
    /// <param name="node">The node the view is declaring for.</param>
    /// <param name="ports">The accessors.</param>
    /// <exception cref="ArgumentNullException">Either argument is <see langword="null"/>.</exception>
    public static void Declare(IWorkflowNodeViewModel node, WorkflowNodePortSet ports)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(ports);

        Table.AddOrUpdate(node, ports);
    }

    /// <summary>The ports declared for <paramref name="node"/>, or <see langword="null"/> when the view declared none.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The declaration, or <see langword="null"/>.</returns>
    public static WorkflowNodePortSet? For(IWorkflowNodeViewModel node)
        => node is not null && Table.TryGetValue(node, out var ports) ? ports : null;
}
