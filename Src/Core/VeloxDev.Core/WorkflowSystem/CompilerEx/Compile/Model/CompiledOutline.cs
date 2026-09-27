using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.WorkflowSystem.CompilerEx;

/// <summary>
/// One line of a compiled graph's outline: how deep it sits, what kind of segment it is, and a short label.
/// </summary>
/// <param name="Depth">Nesting depth, <c>0</c> at the graph's own entries — what a list view indents by.</param>
/// <param name="Kind">
/// <c>"Execute"</c>, <c>"Branch"</c> or <c>"Parallel"</c> — the same three words the Agent's projection of a compiled
/// graph already uses, so the two do not drift into separate vocabularies.
/// </param>
/// <param name="Label">A compact description: the nodes of a chain, or the router and its keys.</param>
/// <param name="Nodes">The nodes this line names, in order. Empty for a segment that names none.</param>
public readonly record struct CompiledOutlineRow(
    int Depth,
    string Kind,
    string Label,
    IReadOnlyList<IWorkflowNodeViewModel> Nodes);

/// <summary>
/// Flattens a compiled graph into a list anyone can bind.
/// <para>
/// The graph is already a view model — segments in observable collections — so a nested list can bind it directly.
/// This exists for the other shape: a flat, virtualizable list that shows the whole structure at once, at the price
/// of computing it once (a graph is frozen after compilation, so nothing has to keep the flat view in step).
/// </para>
/// </summary>
public static class CompiledOutline
{
    /// <summary>Walks <paramref name="graph"/> depth-first, outermost first, into one row per segment.</summary>
    /// <param name="graph">The compiled graph to describe.</param>
    /// <returns>The rows, in display order. Empty when the graph has no entries.</returns>
    public static IReadOnlyList<CompiledOutlineRow> Of(CompiledGraph graph)
    {
        var rows = new List<CompiledOutlineRow>();
        if (graph is null) return rows;

        Walk(graph, 0, rows);
        return rows;
    }

    private static void Walk(CompiledGraph graph, int depth, List<CompiledOutlineRow> rows)
    {
        foreach (var entry in graph.Entries)
        {
            switch (entry)
            {
                case ChainSegment chain:
                    rows.Add(new CompiledOutlineRow(depth, "Execute", Describe(chain), chain.Nodes));
                    break;

                case BranchSegment branch:
                    rows.Add(new CompiledOutlineRow(depth, "Branch", Describe(branch), Named(branch)));
                    foreach (var option in branch.Options)
                    {
                        // The option's own line names no nodes: its label already says where it leads, and the
                        // descent below lists them.
                        rows.Add(new CompiledOutlineRow(
                            depth + 1,
                            option.IsTerminal ? "Terminal" : "Option",
                            Describe(option),
                            []));
                        if (option.Graph is not null) Walk(option.Graph, depth + 2, rows);
                    }
                    break;

                case ParallelSegment parallel:
                    rows.Add(new CompiledOutlineRow(depth, "Parallel", $"{parallel.Branches.Count} branches", []));
                    foreach (var sub in parallel.Branches) Walk(sub, depth + 1, rows);
                    break;
            }
        }
    }

    private static string Describe(ChainSegment chain)
        => chain.Nodes.Count == 0
            ? "empty chain"
            : string.Join(" → ", NamesOf(chain.Nodes));

    private static string Describe(BranchSegment branch)
    {
        var shape = branch.IsDynamic ? "dynamic" : $"key {branch.CompileKey}";
        return $"{branch.Router?.GetType().Name ?? "(no router)"} · {shape} · {branch.Options.Count} options";
    }

    private static string Describe(BranchOption option)
        => option.IsTerminal
            ? $"'{option.Label}' → terminal"
            : $"'{option.Label}' → {Summarize(option.Graph)}";

    /// <summary>A branch line names its router; the options name their own keys.</summary>
    private static IReadOnlyList<IWorkflowNodeViewModel> Named(BranchSegment branch)
        => branch.Router is null ? [] : [branch.Router];

    private static string Summarize(CompiledGraph? graph)
    {
        if (graph is null || graph.Entries.Count == 0) return "nothing";
        foreach (var entry in graph.Entries)
        {
            if (entry is ChainSegment chain && chain.Nodes.Count > 0) return Describe(chain);
        }
        return $"{graph.Entries.Count} segments";
    }

    private static IEnumerable<string> NamesOf(IReadOnlyList<IWorkflowNodeViewModel> nodes)
    {
        for (int i = 0; i < nodes.Count; i++) yield return nodes[i].GetType().Name;
    }
}
