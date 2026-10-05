using System;
using System.Collections.Generic;
using Jalium.UI;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Reads a node's ports out of the model and locates them on the canvas.
/// </summary>
/// <remarks>
/// <para>
/// The card, the link endpoints and the surface's hit-testing all need the same answer to "where is this node's
/// Nth output", so it is derived here once rather than in each of them.
/// </para>
/// <para>
/// Nothing here is reflected and nothing is declared: a node's slots are <see cref="IWorkflowNodeViewModel.Slots"/>
/// and which side each faces is <see cref="IWorkflowSlotViewModel.Channel"/>. The only thing the node does not
/// publish on that path is what an <em>enumerated</em> port is called — those names live on the enumerator's
/// entries, and the node hands its enumerators over through
/// <see cref="IConditionalSlotProviders"/> so a caller holding only the node can reach them.
/// </para>
/// </remarks>
public static class WorkflowPortGeometry
{
    private const SlotChannel SourceBits = SlotChannel.OneSource | SlotChannel.MultipleSources;
    private const SlotChannel TargetBits = SlotChannel.OneTarget | SlotChannel.MultipleTargets;

    /// <summary>A node's input slots, with their display names.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The inputs, in the order the node declares them.</returns>
    public static IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Inputs(IWorkflowNodeViewModel node)
        => SlotsFacing(node, SourceBits);

    /// <summary>A node's output slots, with their display names.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The outputs, in the order the node declares them.</returns>
    public static IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Outputs(IWorkflowNodeViewModel node)
        => SlotsFacing(node, TargetBits);

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

    /// <summary>The node's slots that face <paramref name="bits"/>, with the names their enumerators gave them.</summary>
    /// <remarks>
    /// Filtered, not partitioned: a slot whose channel carries both directions is an input and an output, which is
    /// what <see cref="SlotChannel"/> means — a capacity in each direction, counted independently.
    /// </remarks>
    private static IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> SlotsFacing(IWorkflowNodeViewModel node, SlotChannel bits)
    {
        if (node?.Slots is not { } slots) return [];

        var names = EnumeratedNames(node);
        var result = new List<(IWorkflowSlotViewModel Slot, string Name)>(slots.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] is not { } slot) continue;
            if ((slot.Channel & bits) == 0) continue;

            result.Add((slot, names.TryGetValue(slot, out var name) ? name : string.Empty));
        }

        return result;
    }

    /// <summary>The names the node's enumerators gave the slots they selected.</summary>
    private static Dictionary<IWorkflowSlotViewModel, string> EnumeratedNames(IWorkflowNodeViewModel node)
    {
        var map = new Dictionary<IWorkflowSlotViewModel, string>();
        if (node?.GetHelper() is not IConditionalSlotProviders providers) return map;

        foreach (var provider in providers.Providers)
        {
            var items = provider.Slots;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is not { } item || item.Slot is not { } slot) continue;
                map[slot] = item.Name ?? string.Empty;
            }
        }

        return map;
    }
}
