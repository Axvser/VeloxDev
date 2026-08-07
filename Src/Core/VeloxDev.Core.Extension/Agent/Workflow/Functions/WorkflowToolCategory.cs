using System;

namespace VeloxDev.AI.Workflow.Functions;

/// <summary>
/// Bit flags selecting which groups of tools <see cref="WorkflowAgentToolkit.CreateTools"/>
/// should register. Hosts can shrink the tool surface exposed to the LLM by passing a subset,
/// which lowers per-request token cost and improves tool-selection accuracy.
/// Developer-registered custom tools (via <see cref="WorkflowAgentScope.WithTools"/>) are always
/// included regardless of these flags.
/// </summary>
[Flags]
public enum WorkflowToolCategory
{
    /// <summary>Read-only inspection: ListNodes, GetNodeDetail, GetTypeSchema, GetFullTopology, ...</summary>
    Query = 1 << 0,

    /// <summary>Structural graph edits: move/resize/create/delete/connect/patch, slot collections, undo/redo.</summary>
    Mutation = 1 << 1,

    /// <summary>Run node business code: ExecuteWork, ExecuteWorkOnNodes, BroadcastNode, ReverseBroadcastNode.</summary>
    Execution = 1 << 2,

    /// <summary>Generic allowlisted command execution: ExecuteCommandOnNode, ExecuteCommandById.</summary>
    Command = 1 << 3,

    /// <summary>Graph traversal &amp; path finding: SearchForward/Reverse/AllRelative, IsConnected, FindPath.</summary>
    Graph = 1 << 4,

    /// <summary>Reserved. No bundled layout tools — multi-node layout is done node-by-node via MoveNode/SetNodePosition.</summary>
    Layout = 1 << 5,

    /// <summary>Analytics: GetNodeStatistics.</summary>
    Analytics = 1 << 6,

    /// <summary>State snapshots &amp; dirty marking: TakeSnapshot, GetChangesSinceSnapshot, MarkDirty.</summary>
    State = 1 << 7,

    /// <summary>Reserved. No composite/bundled tools — every operation is a single component-command step.</summary>
    Composite = 1 << 8,

    /// <summary>User interaction: RequestSelection, RequestConfirmation (only when handlers are configured).</summary>
    Interaction = 1 << 9,

    /// <summary>Every category.</summary>
    All = Query | Mutation | Execution | Command | Graph | Layout | Analytics | State | Composite | Interaction,
}
