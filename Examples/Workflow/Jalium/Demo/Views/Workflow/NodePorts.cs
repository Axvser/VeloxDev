using Demo.ViewModels;
using Jalium.UI;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// Generic port layout for the full demo's Common/Lib node view-models, matching the NodeEditorSurface's
/// card geometry: a single input is vertically centered on the left edge; multiple inputs and all outputs
/// are laid out as rows under the title bar. Port centers are computed straight from the node's Anchor/Size
/// (world coordinates) exactly like the trimmed demo, so links, hit-testing and the minimap never go stale.
/// </summary>
internal static class NodePorts
{
    public const double TitleBarH = 36;
    public const double RowH = 26;
    public const double InputPortX = 10;
    public const double OutputInset = 15;

    /// <summary>Input ports of a node, with their display names (empty for unnamed single slots).</summary>
    public static IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Inputs(IWorkflowNodeViewModel node)
    {
        if (node is PythonScriptNodeViewModel python)
        {
            return python.InputSlots.Items.Select(i => ((IWorkflowSlotViewModel)i.Slot, i.Name)).ToList();
        }

        var single = SingleSlot(node, "InputSlot");
        return single is { } s ? [(s, string.Empty)] : [];
    }

    /// <summary>Output ports of a node, with their display names (empty for unnamed single slots).</summary>
    public static IReadOnlyList<(IWorkflowSlotViewModel Slot, string Name)> Outputs(IWorkflowNodeViewModel node)
    {
        switch (node)
        {
            case PythonScriptNodeViewModel python:
                return python.OutputSlots.Items.Select(i => ((IWorkflowSlotViewModel)i.Slot, i.Name)).ToList();
            case BoolSelectorNodeViewModel b:
                return b.OutputSlots.Items.Select(i => ((IWorkflowSlotViewModel)i.Slot, i.Name)).ToList();
            case LogicGateNodeViewModel g:
                return g.OutputSlots.Items.Select(i => ((IWorkflowSlotViewModel)i.Slot, i.Name)).ToList();
            case EnumSelectorNodeViewModel e:
                return e.OutputSlots.Items.Select(i => ((IWorkflowSlotViewModel)i.Slot, i.Name)).ToList();
        }

        var single = SingleSlot(node, "OutputSlot");
        return single is { } s ? [(s, string.Empty)] : [];
    }

    /// <summary>Display title of a node (the generated Title property on the Common/Lib VMs).</summary>
    public static string TitleOf(IWorkflowNodeViewModel node)
        => node.GetType().GetProperty("Title")?.GetValue(node)?.ToString() ?? string.Empty;

    /// <summary>World-coordinate center of the i-th input port.</summary>
    public static Point InputCenter(IWorkflowNodeViewModel node, int i)
    {
        double y = Inputs(node).Count > 1
            ? TitleBarH + RowH * i + RowH / 2
            : node.Size.Height / 2;
        return new Point(node.Anchor.Horizontal + InputPortX, node.Anchor.Vertical + y);
    }

    /// <summary>World-coordinate center of the i-th output port.</summary>
    public static Point OutputCenter(IWorkflowNodeViewModel node, int i)
    {
        double y = TitleBarH + RowH * i + RowH / 2;
        return new Point(node.Anchor.Horizontal + node.Size.Width - OutputInset, node.Anchor.Vertical + y);
    }

    /// <summary>Local (card-space) center of the i-th input port.</summary>
    public static Point InputCenterLocal(IWorkflowNodeViewModel node, int i)
    {
        double y = Inputs(node).Count > 1
            ? TitleBarH + RowH * i + RowH / 2
            : node.Size.Height / 2;
        return new Point(InputPortX, y);
    }

    /// <summary>Local (card-space) center of the i-th output port.</summary>
    public static Point OutputCenterLocal(IWorkflowNodeViewModel node, int i)
    {
        double y = TitleBarH + RowH * i + RowH / 2;
        return new Point(node.Size.Width - OutputInset, y);
    }

    /// <summary>Finds whether a slot is an input or output of its node and its index among that list.</summary>
    public static (bool IsInput, int Index)? IndexOf(IWorkflowNodeViewModel node, IWorkflowSlotViewModel slot)
    {
        var inputs = Inputs(node);
        for (int i = 0; i < inputs.Count; i++)
        {
            if (ReferenceEquals(inputs[i].Slot, slot))
            {
                return (true, i);
            }
        }

        var outputs = Outputs(node);
        for (int i = 0; i < outputs.Count; i++)
        {
            if (ReferenceEquals(outputs[i].Slot, slot))
            {
                return (false, i);
            }
        }

        return null;
    }

    private static IWorkflowSlotViewModel? SingleSlot(IWorkflowNodeViewModel node, string propertyName)
        => node.GetType().GetProperty(propertyName)?.GetValue(node) as IWorkflowSlotViewModel;
}
