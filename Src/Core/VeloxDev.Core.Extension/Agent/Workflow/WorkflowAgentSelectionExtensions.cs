using System;
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.AI.Workflow;

/// <summary>
/// Adds nodes to, or removes them from, the Agent's current working set.
/// </summary>
/// <remarks>
/// <para>
/// This is context, not a tool: it changes only the per-turn dynamic instructions, which tell the model
/// which nodes the user has selected. The tool set is untouched, so a host that wants a surgical edit
/// gets it by narrowing the prompt rather than by learning a new call. The selection reaches the model
/// through <see cref="WorkflowAgentContextProvider"/> (the provider
/// <see cref="WorkflowAgentScope.CreateContextProviders"/> attaches); a host that freezes its instructions
/// once at construction never re-reads it.
/// </para>
/// <para>
/// The operations are set-like, as <see cref="HashSet{T}"/>: selecting a node that is already selected is a
/// no-op, selecting a collection unions it into the current selection, and removing reports only what was
/// actually present. To replace the whole selection, call <see cref="ClearAgentSelection"/> and then add the
/// new set.
/// </para>
/// <para>
/// The calls are silent: no exception escapes. A single-node call returns <see langword="bool"/>; a
/// collection call returns the number of nodes actually added or removed; <c>-1</c> reports an internal
/// error. A node that is null, unattached, missing from its tree's
/// <see cref="IWorkflowTreeViewModel.Nodes"/>, or without a stable runtime id is skipped, not thrown.
/// </para>
/// <para>
/// The selection is captured as an identity snapshot (stable runtime id plus type name) at call time.
/// Geometry, slots and properties are deliberately not captured — the model reads them through the normal
/// query tools, so the prompt never carries a stale position.
/// </para>
/// <para>
/// The selection only <i>observes</i> the workflow: selecting subscribes to the node's
/// <see cref="IWorkflowNodeViewModel.Parent"/> change, the parent tree's node-collection changes, and the
/// node helper's <see cref="IWorkflowNodeEvents.Deleted"/> event. Nothing is written back to the model. A
/// node that is deleted, reparented, or removed from its tree is dropped from the selection immediately, so
/// the next prompt no longer names it.
/// </para>
/// <para>
/// The prompt lists at most 64 identities; the reported count is always the full selection. This keeps a
/// select-all from turning every turn into a large prompt while still telling the model how large the
/// working set is.
/// </para>
/// <code>
/// node.SelectForAgent();                              // add one node; false if already selected
/// selectedNodes.SelectForAgent();                     // union; returns how many were newly added
/// node.DeselectForAgent();                            // remove one; false if it was not selected
/// selectedNodes.DeselectForAgent();                   // remove several; returns how many were removed
/// tree.ClearAgentSelection();                         // release everything; returns the count
/// </code>
/// </remarks>
/// <seealso cref="WorkflowAgentScope"/>
public static class WorkflowAgentSelectionExtensions
{
    /// <summary>Adds a single node to the tree's Agent selection.</summary>
    /// <param name="node">The node to register.</param>
    /// <returns>
    /// <see langword="true"/> when the node was newly added; <see langword="false"/> when it was already
    /// selected, could not be registered, or an internal error occurred.
    /// </returns>
    public static bool SelectForAgent(this IWorkflowNodeViewModel node)
    {
        try
        {
            if (node is null) return false;
            if (node.Parent is not { } tree) return false;
            if (!IsMounted(tree, node)) return false;
            if (node is not IWorkflowIdentifiable) return false;

            return WorkflowAgentSelection.Add(tree, new[] { node }) == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Adds several nodes to their trees' Agent selections.</summary>
    /// <param name="nodes">
    /// The nodes to register. They may belong to different trees; each is added to the selection of the tree
    /// it currently belongs to. Duplicates are collapsed by reference.
    /// </param>
    /// <returns>
    /// The number of nodes newly added; <c>0</c> when nothing changed or the input is unusable; <c>-1</c>
    /// when an internal error occurred.
    /// </returns>
    public static int SelectForAgent(this IReadOnlyCollection<IWorkflowNodeViewModel> nodes)
    {
        try
        {
            if (nodes is null) return 0;
            if (nodes.Count == 0) return 0;

            var groups = new List<(IWorkflowTreeViewModel Tree, List<IWorkflowNodeViewModel> Nodes)>();

            foreach (var node in nodes)
            {
                if (node is null) continue;
                if (node.Parent is not { } tree) continue;
                if (!IsMounted(tree, node)) continue;
                if (node is not IWorkflowIdentifiable) continue;

                var group = FindGroup(groups, tree);
                if (group is null)
                {
                    group = new List<IWorkflowNodeViewModel>();
                    groups.Add((tree, group));
                }

                if (!ContainsReference(group, node)) group.Add(node);
            }

            var added = 0;
            foreach (var group in groups)
                added += WorkflowAgentSelection.Add(group.Tree, group.Nodes);

            return added;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>Removes a single node from the Agent selection.</summary>
    /// <param name="node">The node to release.</param>
    /// <returns>
    /// <see langword="true"/> when the node was removed; <see langword="false"/> when it was not selected or
    /// an internal error occurred.
    /// </returns>
    public static bool DeselectForAgent(this IWorkflowNodeViewModel node)
    {
        try
        {
            if (node is null) return false;

            return WorkflowAgentSelection.Remove(new[] { node }) > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Removes several nodes from their trees' Agent selections.</summary>
    /// <param name="nodes">
    /// The nodes to release. They may belong to different trees; each is removed from every selection that
    /// contains it. Nodes that are not selected are ignored.
    /// </param>
    /// <returns>
    /// The number of nodes actually removed; <c>0</c> when none were selected or the input is unusable;
    /// <c>-1</c> when an internal error occurred.
    /// </returns>
    public static int DeselectForAgent(this IReadOnlyCollection<IWorkflowNodeViewModel> nodes)
    {
        try
        {
            if (nodes is null) return 0;
            if (nodes.Count == 0) return 0;

            var valid = new List<IWorkflowNodeViewModel>(nodes.Count);
            foreach (var node in nodes)
                if (node is not null) valid.Add(node);

            return valid.Count == 0 ? 0 : WorkflowAgentSelection.Remove(valid);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>Clears the tree's selection, so the next prompt carries no working set.</summary>
    /// <param name="tree">The tree whose selection to clear.</param>
    /// <returns>
    /// The number of nodes released; <c>0</c> when nothing was selected or <paramref name="tree"/> is
    /// <see langword="null"/>; <c>-1</c> when an internal error occurred.
    /// </returns>
    public static int ClearAgentSelection(this IWorkflowTreeViewModel tree)
    {
        try
        {
            if (tree is null) return 0;

            return WorkflowAgentSelection.Clear(tree);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>Returns a snapshot of the tree's current selection, in selection order.</summary>
    /// <param name="tree">The tree whose selection to read.</param>
    /// <returns>
    /// The selected nodes, or an empty list when nothing is selected. The returned list is a copy; it does
    /// not follow later selections.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static IReadOnlyList<IWorkflowNodeViewModel> GetAgentSelection(this IWorkflowTreeViewModel tree)
    {
        if (tree is null) throw new ArgumentNullException(nameof(tree));

        return WorkflowAgentSelection.Get(tree);
    }

    private static bool IsMounted(IWorkflowTreeViewModel tree, IWorkflowNodeViewModel node)
    {
        foreach (var candidate in tree.Nodes)
            if (ReferenceEquals(candidate, node)) return true;

        return false;
    }

    private static List<IWorkflowNodeViewModel>? FindGroup(
        List<(IWorkflowTreeViewModel Tree, List<IWorkflowNodeViewModel> Nodes)> groups,
        IWorkflowTreeViewModel tree)
    {
        foreach (var group in groups)
            if (ReferenceEquals(group.Tree, tree)) return group.Nodes;

        return null;
    }

    private static bool ContainsReference(List<IWorkflowNodeViewModel> nodes, IWorkflowNodeViewModel node)
    {
        foreach (var candidate in nodes)
            if (ReferenceEquals(candidate, node)) return true;

        return false;
    }
}
