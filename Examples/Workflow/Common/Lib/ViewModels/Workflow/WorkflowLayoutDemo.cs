using System;
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace Demo.Workflow;

/// <summary>
/// The demo-facing wrapper around the Core layout entry point: one node in, one undoable arrangement out.
/// </summary>
/// <remarks>
/// The menu labels and tolerance presets live here so every platform's demo offers the same three traversal
/// directions and the same three tolerances. A platform still owns its menu control and where the summary is
/// shown — that is the demo's appearance, not a library concern.
/// </remarks>
public static class WorkflowLayoutDemo
{
    /// <summary>The traversal choices offered by every demo's node menu.</summary>
    public static IReadOnlyList<(string Label, WorkflowLayoutTraversal Traversal)> Traversals { get; } =
    [
        ("从该节点整理（下游）", WorkflowLayoutTraversal.Downstream),
        ("从该节点整理（上游）", WorkflowLayoutTraversal.Upstream),
        ("从该节点整理（双向）", WorkflowLayoutTraversal.Both),
    ];

    /// <summary>The tolerance presets offered by every demo's node menu.</summary>
    public static IReadOnlyList<(string Label, double Tolerance)> Tolerances { get; } =
    [
        ("严格整理 (0)", 0d),
        ("默认 (8)", 8d),
        ("宽松 (32)", 32d),
    ];

    /// <summary>Arranges the subgraph reachable from <paramref name="node"/> through its tree's solver.</summary>
    /// <param name="node">The node the user right-clicked; it stays fixed.</param>
    /// <param name="traversal">Which links the scoped run follows.</param>
    /// <param name="tolerance">The per-axis tolerance passed to the solver.</param>
    /// <returns>The apply result, ready for <see cref="Describe"/>.</returns>
    public static WorkflowLayoutResult Arrange(
        IWorkflowNodeViewModel node, WorkflowLayoutTraversal traversal, double tolerance)
        => node.Arrange(new WorkflowLayoutOptions
        {
            Traversal = traversal,
            PinStartNode = true,
            PositionTolerance = tolerance,
        });

    /// <summary>One line for the demo's status area, built from the result the solver returned.</summary>
    /// <param name="result">The arrangement to summarize.</param>
    /// <param name="scope">The label shown in parentheses, usually the start node's type name.</param>
    public static string Describe(WorkflowLayoutResult result, string scope)
    {
        var cost = result.Solution.Cost;
        var summary = $"整理（{scope}）：写入 {result.AppliedCount} 个节点";
        if (result.SkippedCount > 0)
        {
            summary += $" · 跳过 {result.SkippedCount} 个已变动的节点";
        }

        summary += $" · 重叠 {cost.Overlaps} · 交叉 {cost.Crossings}";
        if (cost.CrossingsApproximate)
        {
            summary += "（中心线近似）";
        }

        if (!cost.CostMeasured)
        {
            summary += "（代价未测量）";
        }

        return summary;
    }
}
