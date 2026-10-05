using System;
using System.Collections.Generic;
using Jalium.UI;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Reads a node's ports and locates them on the canvas.
/// </summary>
/// <remarks>
/// <para>
/// The card, the link endpoints and the surface's hit-testing all need the same answer to "where is this node's
/// Nth output", so it is derived here once rather than in each of them.
/// </para>
/// <para>
/// Where the ports <i>are</i> is geometry and is computed here. <i>Which</i> slots are a node's ports is not
/// computable: a node holds its slots in properties of its own naming, and this adapter holds the node as
/// <see cref="IWorkflowNodeViewModel"/>, which says nothing about them. The view declares that part — see
/// <see cref="WorkflowNodePorts"/> — exactly as the markup adapters' <c>SlotNames</c> has their view declare it.
/// </para>
/// </remarks>
public static class WorkflowPortGeometry
{
    /// <summary>A node's input slots, with their display names.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The inputs, in order; empty when the view declared none.</returns>
    public static IReadOnlyList<WorkflowNodePort> Inputs(IWorkflowNodeViewModel node)
        => WorkflowNodePorts.For(node) is { } ports ? ports.Inputs(node) : [];

    /// <summary>A node's output slots, with their display names.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The outputs, in order; empty when the view declared none.</returns>
    public static IReadOnlyList<WorkflowNodePort> Outputs(IWorkflowNodeViewModel node)
        => WorkflowNodePorts.For(node) is { } ports ? ports.Outputs(node) : [];

    /// <summary>A node's display title.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The title, or an empty string when the view declared none.</returns>
    public static string TitleOf(IWorkflowNodeViewModel node)
        => WorkflowNodePorts.For(node) is { } ports ? ports.Title(node) : string.Empty;

    /// <summary>Whether a slot is an input or an output of its node, and its index among those.</summary>
    /// <param name="node">The node.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The role and index, or <see langword="null"/> when the node does not own the slot.</returns>
    public static (bool IsInput, int Index)? IndexOf(IWorkflowNodeViewModel node, IWorkflowSlotViewModel slot)
    {
        var inputs = Inputs(node);
        for (int i = 0; i < inputs.Count; i++)
            if (ReferenceEquals(inputs[i].Slot, slot)) return (true, i);

        var outputs = Outputs(node);
        for (int i = 0; i < outputs.Count; i++)
            if (ReferenceEquals(outputs[i].Slot, slot)) return (false, i);

        return null;
    }

    /// <summary>The canvas point a node's input port sits at, in world coordinates.</summary>
    /// <param name="node">The node.</param>
    /// <param name="layout">Where the card puts its ports.</param>
    /// <returns>The port centre.</returns>
    public static Point InputCenter(IWorkflowNodeViewModel node, WorkflowPortLayout layout)
        => Center(node, layout.InputPortX, layout.DesignHeight / 2.0, layout);

    /// <summary>The canvas point a node's <paramref name="index"/>-th output port sits at, in world coordinates.</summary>
    /// <param name="node">The node.</param>
    /// <param name="index">The output's index.</param>
    /// <param name="layout">Where the card puts its ports.</param>
    /// <returns>The port centre.</returns>
    public static Point OutputCenter(IWorkflowNodeViewModel node, int index, WorkflowPortLayout layout)
        => Center(
            node,
            layout.DesignWidth - layout.OutputInset,
            layout.TitleBarH + layout.RowH * index + layout.RowH / 2.0,
            layout);

    /// <summary>The canvas point a design-local position on a node's card sits at, in world coordinates.</summary>
    /// <param name="node">The node.</param>
    /// <param name="designX">X in the card's design coordinates.</param>
    /// <param name="designY">Y in the card's design coordinates.</param>
    /// <param name="layout">The card's design size.</param>
    /// <returns>The world point.</returns>
    public static Point Center(IWorkflowNodeViewModel node, double designX, double designY, WorkflowPortLayout layout)
    {
        ArgumentNullException.ThrowIfNull(node);

        // 卡片按设计尺寸画在一个缩放到折叠后盒子的 Viewbox 里，所以设计局部坐标要乘以折叠因子
        // s = node.Size / DesignSize 才是世界坐标。
        var sx = layout.DesignWidth == 0 ? 1 : node.Size.Width / layout.DesignWidth;
        var sy = layout.DesignHeight == 0 ? 1 : node.Size.Height / layout.DesignHeight;
        return new Point(node.Anchor.Horizontal + designX * sx, node.Anchor.Vertical + designY * sy);
    }
}
