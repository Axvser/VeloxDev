using System;
using VeloxDev.AI.Workflow;
using VeloxDev.WorkflowSystem;

namespace Demo.Workflow;

/// <summary>
/// Bridges the Agent's node selection to the demo's node views.
/// </summary>
/// <remarks>
/// Ctrl+click on a node toggles it into the Agent's working set. The extension keeps the selection; this
/// helper only decides what the demo shows. A change is broadcast through <see cref="Changed"/> so a card can
/// repaint itself — the node model is not modified and raises no property notification.
/// </remarks>
public static class AgentSelection
{
    /// <summary>Raised after a node enters or leaves the Agent's working set.</summary>
    public static event Action<IWorkflowNodeViewModel>? Changed;

    /// <summary>Whether <paramref name="node"/> is in its tree's Agent selection.</summary>
    public static bool IsSelected(IWorkflowNodeViewModel? node)
    {
        if (node?.Parent is not { } tree) return false;

        foreach (var selected in tree.GetAgentSelection())
            if (ReferenceEquals(selected, node)) return true;

        return false;
    }

    /// <summary>Toggles <paramref name="node"/> and notifies the cards when the set actually changed.</summary>
    public static bool Toggle(IWorkflowNodeViewModel? node)
    {
        if (node?.Parent is null) return false;

        var before = IsSelected(node);
        var changed = before
            ? node.DeselectForAgent()
            : node.SelectForAgent();

        if (changed) Changed?.Invoke(node);
        return changed;
    }

    /// <summary>Clears the tree's selection and notifies every card that was on it.</summary>
    public static int Clear(IWorkflowTreeViewModel? tree)
    {
        if (tree is null) return 0;

        var released = tree.GetAgentSelection();
        var count = tree.ClearAgentSelection();
        if (count <= 0) return count;

        foreach (var node in released)
            Changed?.Invoke(node);

        return count;
    }
}
