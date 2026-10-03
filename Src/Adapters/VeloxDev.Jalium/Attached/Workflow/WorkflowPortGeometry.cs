using System;
using System.Collections;
using System.Collections.Generic;
using Jalium.UI;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Reads a node's ports out of its view-model and locates them on the canvas.
/// </summary>
/// <remarks>
/// <para>
/// The card, the link endpoints and the surface's hit-testing all need the same answer to "where is this node's
/// Nth output", so it is derived here once from the model's shape rather than in each of them.
/// </para>
/// <para>
/// The node's view-model exposes an input as a plain slot property and its outputs either as a
/// <see cref="SlotEnumerator{T}"/> property or as another plain slot. Neither is on
/// <see cref="IWorkflowNodeViewModel"/>, so they are resolved by name — that is the one thing about a workflow
/// node the interface does not describe.
/// </para>
/// </remarks>
public static class WorkflowPortGeometry
{
    /// <summary>A node's input slots, with their display names.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The inputs, in order.</returns>
    public static IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Inputs(IWorkflowNodeViewModel node)
    {
        var single = SingleSlot(node, "InputSlot");
        return single is { } s ? [(s, string.Empty)] : [];
    }

    /// <summary>A node's output slots, with their display names.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The outputs, in order.</returns>
    public static IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Outputs(IWorkflowNodeViewModel node)
    {
        // 节点可能暴露一个 SlotEnumerator（例如 OutputSlots）：枚举它的条目 —— 每个选中的枚举/布尔成员一个输出，
        // 名字取成员的标签。枚举器不在 IWorkflowNodeViewModel 接口上，所以按属性名反射取。
        var outputSlots = node.GetType().GetProperty("OutputSlots")?.GetValue(node);
        if (outputSlots is not null
            && outputSlots.GetType().GetProperty("Items")?.GetValue(outputSlots) is IEnumerable items)
        {
            var result = new List<(IWorkflowSlotViewModel Slot, string Name)>();
            foreach (var item in items)
            {
                if (item is null) continue;

                var itemType = item.GetType();
                var slot = itemType.GetProperty("Slot")?.GetValue(item) as IWorkflowSlotViewModel;
                var name = itemType.GetProperty("Name")?.GetValue(item)?.ToString() ?? string.Empty;
                if (slot is not null) result.Add((slot, name));
            }

            return result;
        }

        // 兜底：只有单个 OutputSlot 的普通节点（非选择器节点）。
        var single = SingleSlot(node, "OutputSlot");
        return single is { } s ? [(s, string.Empty)] : [];
    }

    /// <summary>A node's display title (its generated Title or Name).</summary>
    /// <param name="node">The node.</param>
    /// <returns>The title, or an empty string.</returns>
    public static string TitleOf(IWorkflowNodeViewModel node)
        => node.GetType().GetProperty("Title")?.GetValue(node)?.ToString()
            ?? node.GetType().GetProperty("Name")?.GetValue(node)?.ToString()
            ?? string.Empty;

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
        // 卡片按设计尺寸画在一个缩放到折叠后盒子的 Viewbox 里，所以设计局部坐标要乘以折叠因子
        // s = node.Size / DesignSize 才是世界坐标。
        var sx = layout.DesignWidth == 0 ? 1 : node.Size.Width / layout.DesignWidth;
        var sy = layout.DesignHeight == 0 ? 1 : node.Size.Height / layout.DesignHeight;
        return new Point(node.Anchor.Horizontal + designX * sx, node.Anchor.Vertical + designY * sy);
    }

    private static IWorkflowSlotViewModel? SingleSlot(IWorkflowNodeViewModel node, string propertyName)
        => node.GetType().GetProperty(propertyName)?.GetValue(node) as IWorkflowSlotViewModel;
}
